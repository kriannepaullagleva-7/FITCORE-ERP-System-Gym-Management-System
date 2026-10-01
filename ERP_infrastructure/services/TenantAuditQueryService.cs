using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// The audit trail names every actor who touched the tenant's data, so reading it back is
    /// itself restricted to an administrator - the same seniority EmployeeService requires to
    /// change somebody's pay. Anything looser would let a Manager watch a Staff colleague's
    /// every move, which is not what "authorized administrator" means in the brief.
    /// </summary>
    public class TenantAuditQueryService : IAuditQueryService
    {
        private const int MaxTake = 500;

        private readonly TenantErpDbContext _context;
        private readonly ICurrentUserAccessor _actor;

        public TenantAuditQueryService(TenantErpDbContext context, ICurrentUserAccessor actor)
        {
            _context = context;
            _actor = actor;
        }

        private void RequireAdmin()
        {
            // No signed-in user is the bootstrapper or a test, which is trusted.
            if (_actor.Current.AppUserId is null) return;

            if (ErpRoles.LevelOf(_actor.Current.RoleKey) > ErpRoles.LevelOf(ErpRoles.Admin))
            {
                throw new ForbiddenOperationException(
                    "Only an administrator can view the audit trail.");
            }
        }

        public async Task<List<AuditEventView>> SearchAsync(AuditEventQuery query)
        {
            RequireAdmin();

            var take = query.Take is > 0 and <= MaxTake ? query.Take : MaxTake;

            var events = _context.AuditEvents.AsNoTracking().AsQueryable();

            if (query.From is { } from) events = events.Where(e => e.OccurredAt >= from);
            if (query.To is { } to) events = events.Where(e => e.OccurredAt <= to);

            if (!string.IsNullOrWhiteSpace(query.Module))
                events = events.Where(e => e.Module == query.Module);

            if (!string.IsNullOrWhiteSpace(query.Action))
                events = events.Where(e => e.Action == query.Action);

            if (!string.IsNullOrWhiteSpace(query.EntityName))
                events = events.Where(e => e.EntityName == query.EntityName);

            if (!string.IsNullOrWhiteSpace(query.EntityId))
                events = events.Where(e => e.EntityId == query.EntityId);

            return await events
                .OrderByDescending(e => e.OccurredAt)
                .ThenByDescending(e => e.AuditEventId)
                .Take(take)
                .Select(e => new AuditEventView
                {
                    AuditEventId = e.AuditEventId,
                    OccurredAt = e.OccurredAt,
                    Username = e.Username,
                    RoleKey = e.RoleKey,
                    Action = e.Action,
                    Module = e.Module,
                    EntityName = e.EntityName,
                    EntityId = e.EntityId,
                    OldValues = e.OldValues,
                    NewValues = e.NewValues,
                    Summary = e.Summary,
                    IpAddress = e.IpAddress
                })
                .ToListAsync();
        }
    }
}
