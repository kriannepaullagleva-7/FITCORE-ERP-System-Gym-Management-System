using System.Text.Json;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ERP_infrastructure.data
{
    /// <summary>
    /// Turns what the change tracker is about to save into audit rows.
    ///
    /// Reading the tracker means no write path can forget to record itself, and old values come
    /// for free. What it cannot do is name a business action - it sees an UPDATE on Payroll, not
    /// "payroll marked paid" - so services raise those through <see cref="IAuditService"/> as
    /// well. The two together give a trail that is both complete and readable.
    /// </summary>
    internal static class AuditCapture
    {
        /// <summary>
        /// Never recorded. Either a credential, or a value large enough to bloat the trail
        /// without telling anybody anything.
        /// </summary>
        private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        {
            "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "Password",
            "ConnectionString", "CredentialKey", "Token", "RefreshToken", "Secret", "ApiKey"
        };

        /// <summary>Beyond this a value is truncated; nothing legitimate in this schema is near it.</summary>
        private const int MaxSerialisedLength = 4000;

        /// <summary>A row captured before saving, still waiting for its database-generated key.</summary>
        internal sealed record Draft(
            EntityEntry Entry,
            AuditEvent Event);

        /// <summary>
        /// Called before SaveChanges, while the tracker still knows the original values and the
        /// deleted rows still exist.
        /// </summary>
        internal static List<Draft> Collect(ChangeTracker tracker, AuditActor actor)
        {
            var drafts = new List<Draft>();
            var now = DateTime.UtcNow;

            foreach (var entry in tracker.Entries())
            {
                // The trail must not record itself, and an unchanged row is not an event.
                if (entry.Entity is AuditEvent) continue;

                var action = entry.State switch
                {
                    EntityState.Added    => AuditActions.Create,
                    EntityState.Modified => AuditActions.Update,
                    EntityState.Deleted  => AuditActions.Delete,
                    _ => null
                };

                if (action is null) continue;

                var (oldValues, newValues) = Values(entry);

                // An "update" where every changed property was excluded is not worth a row.
                if (action == AuditActions.Update && oldValues is null && newValues is null) continue;

                drafts.Add(new Draft(entry, new AuditEvent
                {
                    OccurredAt = now,
                    CompanyId  = actor.CompanyId,
                    AppUserId  = actor.AppUserId,
                    Username   = Trim(actor.Username, 100),
                    RoleKey    = Trim(actor.RoleKey, 50),
                    Action     = action,
                    Module     = ModuleFor(entry.Entity),
                    EntityName = Trim(entry.Entity.GetType().Name, 100),
                    EntityId   = action == AuditActions.Create ? null : KeyOf(entry),
                    OldValues  = oldValues,
                    NewValues  = newValues,
                    IpAddress  = Trim(actor.IpAddress, 45),
                    DeviceId   = Trim(actor.DeviceId, 100)
                }));
            }

            return drafts;
        }

        /// <summary>
        /// Called after SaveChanges, when an inserted row finally has its identity value.
        /// </summary>
        internal static IEnumerable<AuditEvent> Finalise(IEnumerable<Draft> drafts)
        {
            foreach (var draft in drafts)
            {
                draft.Event.EntityId ??= KeyOf(draft.Entry);
                yield return draft.Event;
            }
        }

        private static (string? Old, string? New) Values(EntityEntry entry)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    return (null, Serialise(entry.Properties
                        .Where(p => !Skip(p.Metadata))
                        .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)));

                case EntityState.Deleted:
                    return (Serialise(entry.Properties
                        .Where(p => !Skip(p.Metadata))
                        .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue)), null);

                case EntityState.Modified:
                    // Only what actually changed. An edited phone number should not write a
                    // copy of the whole row twice over.
                    var changed = entry.Properties
                        .Where(p => p.IsModified && !Skip(p.Metadata))
                        .ToList();

                    if (changed.Count == 0) return (null, null);

                    return (
                        Serialise(changed.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue)),
                        Serialise(changed.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)));

                default:
                    return (null, null);
            }
        }

        private static bool Skip(IProperty property) =>
            Excluded.Contains(property.Name)
            || property.Name.EndsWith("Hash", StringComparison.OrdinalIgnoreCase)
            || property.ClrType == typeof(byte[]);

        private static string? Serialise(Dictionary<string, object?> values)
        {
            if (values.Count == 0) return null;

            var json = JsonSerializer.Serialize(values);

            return json.Length <= MaxSerialisedLength
                ? json
                : json[..MaxSerialisedLength] + "…[truncated]";
        }

        private static string? KeyOf(EntityEntry entry)
        {
            var key = entry.Metadata.FindPrimaryKey();

            if (key is null) return null;

            var parts = key.Properties
                .Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "")
                .ToArray();

            return Trim(string.Join("/", parts), 50);
        }

        /// <summary>
        /// Which sidebar module an entity belongs to, so the trail can be filtered the same way
        /// the application is navigated.
        /// </summary>
        private static string ModuleFor(object entity) => entity switch
        {
            Member or MembershipPlan or Subscription => ErpModules.Membership,
            Sale or SaleItem or Customer             => ErpModules.Sales,
            Payment                                  => ErpModules.Payments,
            Product or Inventory or StockMovement or Supplier => ErpModules.Inventory,
            Employee                                 => ErpModules.Employees,
            Payroll                                  => ErpModules.Payroll,
            Expense                                  => ErpModules.Expenses,
            AppUser or AppRole or AppUserPermission or AppRolePermission => ErpModules.UserAccess,
            Company or CompanyDatabase or Device     => ErpModules.SystemAdmin,
            _                                        => ""
        };

        private static string Trim(string? value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value[..max];
        }
    }
}
