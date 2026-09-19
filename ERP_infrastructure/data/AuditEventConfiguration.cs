using ERP_domain.entities;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.data
{
    /// <summary>
    /// The audit table, mapped identically into the master and tenant databases so the two
    /// halves of the trail can be read and merged the same way.
    /// </summary>
    internal static class AuditEventConfiguration
    {
        internal static void ConfigureAuditEvents(this ModelBuilder builder)
        {
            builder.Entity<AuditEvent>(entity =>
            {
                entity.ToTable("AuditEvents");
                entity.HasKey(e => e.AuditEventId);

                // Defaulted in the database so no insert path can leave it unset.
                entity.Property(e => e.OccurredAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
                entity.Property(e => e.RoleKey).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Module).IsRequired().HasMaxLength(50);
                entity.Property(e => e.EntityName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.EntityId).HasMaxLength(50);
                entity.Property(e => e.IpAddress).HasMaxLength(45);   // fits IPv6
                entity.Property(e => e.DeviceId).HasMaxLength(100);
                entity.Property(e => e.Summary).HasMaxLength(300);

                // OldValues and NewValues stay nvarchar(max): they are JSON of unpredictable
                // shape, already capped at 4000 characters before they are written.

                entity.HasIndex(e => e.OccurredAt);
                entity.HasIndex(e => new { e.Module, e.OccurredAt });
                entity.HasIndex(e => new { e.EntityName, e.EntityId });
                entity.HasIndex(e => new { e.AppUserId, e.OccurredAt });

                // No foreign keys anywhere, deliberately. An audit row has to survive the
                // deletion of the record and the account it describes.
            });
        }
    }
}
