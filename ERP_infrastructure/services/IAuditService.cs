using ERP_domain.entities;
using ERP_infrastructure.data;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Records an action the change tracker cannot name for itself.
    ///
    /// The automatic sweep in the DbContext sees that a Payroll row changed; it does not know
    /// the change meant "marked paid". Services raise those through this so the trail reads as
    /// a history of what the business did, not only of which columns moved.
    /// </summary>
    public interface IAuditService
    {
        Task RecordAsync(
            string action,
            string module,
            string entityName,
            string? entityId = null,
            string? summary = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Writes to the tenant trail, beside the rows it describes.</summary>
    public sealed class TenantAuditService : IAuditService
    {
        private readonly TenantErpDbContext _context;
        private readonly ICurrentUserAccessor _actor;

        public TenantAuditService(TenantErpDbContext context, ICurrentUserAccessor actor)
        {
            _context = context;
            _actor = actor;
        }

        public async Task RecordAsync(
            string action, string module, string entityName, string? entityId = null,
            string? summary = null, CancellationToken cancellationToken = default)
        {
            _context.AuditEvents.Add(AuditEventFactory.Create(
                _actor.Current, action, module, entityName, entityId, summary));

            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Writes to the master trail: sign-in, permissions, companies. These have no tenant, and
    /// a failed sign-in has no company at all.
    /// </summary>
    public sealed class MasterAuditService : IAuditService
    {
        private readonly MasterErpDbContext _context;
        private readonly ICurrentUserAccessor _actor;

        public MasterAuditService(MasterErpDbContext context, ICurrentUserAccessor actor)
        {
            _context = context;
            _actor = actor;
        }

        public async Task RecordAsync(
            string action, string module, string entityName, string? entityId = null,
            string? summary = null, CancellationToken cancellationToken = default)
        {
            _context.AuditEvents.Add(AuditEventFactory.Create(
                _actor.Current, action, module, entityName, entityId, summary));

            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Sign-in events, which cannot use <see cref="MasterAuditService"/> because there is no
    /// signed-in user yet - the whole point of the event is that somebody is trying to become
    /// one. The actor is therefore passed in rather than read from the request.
    /// </summary>
    public interface IAuthAuditService
    {
        Task RecordAsync(
            string action,
            AuditActor actor,
            string? summary = null,
            CancellationToken cancellationToken = default);
    }

    public sealed class AuthAuditService : IAuthAuditService
    {
        private readonly MasterErpDbContext _context;

        public AuthAuditService(MasterErpDbContext context)
        {
            _context = context;
        }

        public async Task RecordAsync(
            string action, AuditActor actor, string? summary = null,
            CancellationToken cancellationToken = default)
        {
            _context.AuditEvents.Add(AuditEventFactory.Create(
                actor, action, ErpModules.UserAccess, nameof(AppUser),
                actor.AppUserId?.ToString(), summary));

            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    internal static class AuditEventFactory
    {
        internal static AuditEvent Create(
            AuditActor actor, string action, string module,
            string entityName, string? entityId, string? summary) =>
            new()
            {
                OccurredAt = DateTime.UtcNow,
                CompanyId  = actor.CompanyId,
                AppUserId  = actor.AppUserId,
                Username   = Cap(actor.Username, 100),
                RoleKey    = Cap(actor.RoleKey, 50),
                Action     = Cap(action, 50),
                Module     = Cap(module, 50),
                EntityName = Cap(entityName, 100),
                EntityId   = entityId is null ? null : Cap(entityId, 50),
                IpAddress  = actor.IpAddress is null ? null : Cap(actor.IpAddress, 45),
                DeviceId   = actor.DeviceId is null ? null : Cap(actor.DeviceId, 100),
                Summary    = summary is null ? null : Cap(summary, 300)
            };

        private static string Cap(string? value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value[..max];
        }
    }
}
