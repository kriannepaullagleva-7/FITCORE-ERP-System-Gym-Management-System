using ERP_domain.entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ERP_infrastructure.services;

namespace ERP_infrastructure.data
{
    /// <summary>
    /// The platform-wide database: which companies exist, where each one's tenant database
    /// lives, and who is allowed to sign in to which company.
    ///
    /// Sign-in lives here rather than in a tenant database because a user is what *chooses* a
    /// tenant. If the account were stored per tenant the server would need to know the tenant
    /// before it could authenticate, which is the wrong way round and is exactly how a header
    /// ends up being trusted.
    /// </summary>
    public class MasterErpDbContext : IdentityDbContext
    {
        private readonly ICurrentUserAccessor? _actor;

        /// <summary>
        /// The accessor is optional so the bootstrapper, the design-time factory and the tests
        /// construct this context unchanged. Without one, changes are saved but not attributed.
        /// </summary>
        public MasterErpDbContext(
            DbContextOptions<MasterErpDbContext> options,
            ICurrentUserAccessor? actor = null)
            : base(options)
        {
            _actor = actor;
        }

        /// <summary>
        /// The half of the trail that has no tenant: sign-in, permission and company changes.
        /// A failed sign-in in particular has no company yet, so there is no tenant database to
        /// write it to.
        /// </summary>
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        public DbSet<Company> Companies => Set<Company>();
        public DbSet<CompanyDatabase> CompanyDatabases => Set<CompanyDatabase>();
        public DbSet<Device> Devices => Set<Device>();

        // Access control.
        public DbSet<AppRole> AppRoles => Set<AppRole>();
        public DbSet<AppRolePermission> AppRolePermissions => Set<AppRolePermission>();
        public DbSet<AppUser> AppUsers => Set<AppUser>();
        public DbSet<AppUserPermission> AppUserPermissions => Set<AppUserPermission>();

        // Platform administration. What FitCore sells, to whom, and for how long. This is
        // deliberately master-side: a tenant must not be able to read - let alone change -
        // which plan it is on or what it is being charged, and the Super Admin's revenue and
        // churn figures have to be answerable without opening a single tenant database.
        public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
        public DbSet<CompanySubscription> CompanySubscriptions => Set<CompanySubscription>();
        public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();

        private bool _writingAudit;

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (_writingAudit || _actor is null)
            {
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }

            var drafts = AuditCapture.Collect(ChangeTracker, _actor.Current);

            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

            if (drafts.Count > 0)
            {
                _writingAudit = true;
                try
                {
                    AuditEvents.AddRange(AuditCapture.Finalise(drafts));
                    await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                }
                finally
                {
                    _writingAudit = false;
                }
            }

            return result;
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.ConfigureAuditEvents();

            builder.Entity<Company>(entity =>
            {
                entity.HasKey(x => x.CompanyId);
                entity.Property(x => x.CompanyCode).IsRequired().HasMaxLength(50);
                entity.Property(x => x.CompanyName).IsRequired().HasMaxLength(200);
                entity.HasIndex(x => x.CompanyCode).IsUnique();

                // Stored as the underlying int. Existing rows predate the column, so the
                // default keeps them on the tier they already behave as.
                //
                // The sentinel is stated explicitly because the enum deliberately starts at
                // Micro = 1: zero is not a tier, it is "nobody set one", which is exactly
                // when the database default should win. Without saying so, EF cannot tell a
                // genuine value from an unset one and warns on every start-up.
                entity.Property(x => x.EnterpriseTier)
                      .HasConversion<int>()
                      .IsRequired()
                      .HasDefaultValue(EnterpriseTier.Micro)
                      .HasSentinel(default(EnterpriseTier));
            });

            builder.Entity<CompanyDatabase>(entity =>
            {
                entity.HasKey(x => x.CompanyDatabaseId);
                entity.Property(x => x.ServerName).IsRequired().HasMaxLength(200);
                entity.Property(x => x.DatabaseName).IsRequired().HasMaxLength(200);
                entity.Property(x => x.IsActive).IsRequired();
                entity.HasOne(x => x.Company)
                      .WithMany()
                      .HasForeignKey(x => x.CompanyId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Device>(entity =>
            {
                entity.HasKey(x => x.DeviceId);
                entity.Property(x => x.DeviceCode).HasMaxLength(50).IsRequired();
                entity.Property(x => x.DeviceName).HasMaxLength(200).IsRequired();
                entity.HasOne(x => x.Company)
                      .WithMany(x => x.Devices)
                      .HasForeignKey(x => x.CompanyId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.DeviceCode }).IsUnique();
            });

            ConfigureAccessControl(builder);
            ConfigurePlatformAdministration(builder);
        }

        /// <summary>
        /// The subscription registry and the installation's own settings.
        ///
        /// A company's <see cref="Company.EnterpriseTier"/> stays the authoritative fact for
        /// access, and subscribing a company is what sets it. The two are kept apart on
        /// purpose: recording that a term has lapsed is a billing event, and it must not be the
        /// same act as revoking a paying customer's data access halfway through a dispute.
        /// </summary>
        private static void ConfigurePlatformAdministration(ModelBuilder builder)
        {
            builder.Entity<SubscriptionPlan>(entity =>
            {
                entity.HasKey(x => x.SubscriptionPlanId);
                entity.Property(x => x.PlanCode).IsRequired().HasMaxLength(40);
                entity.Property(x => x.PlanName).IsRequired().HasMaxLength(150);
                entity.Property(x => x.Description).IsRequired().HasMaxLength(500).HasDefaultValue("");
                entity.Property(x => x.Capability).IsRequired().HasMaxLength(500).HasDefaultValue("");
                entity.Property(x => x.MonthlyPrice).HasPrecision(18, 2);
                entity.Property(x => x.AnnualPrice).HasPrecision(18, 2);
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.Property(x => x.Tier)
                      .HasConversion<int>()
                      .IsRequired()
                      .HasDefaultValue(EnterpriseTier.Micro)
                      .HasSentinel(default(EnterpriseTier));

                entity.HasIndex(x => x.PlanCode).IsUnique();
            });

            builder.Entity<CompanySubscription>(entity =>
            {
                entity.HasKey(x => x.CompanySubscriptionId);
                entity.Property(x => x.BillingCycle).IsRequired().HasMaxLength(20)
                      .HasDefaultValue(BillingCycles.Monthly);
                entity.Property(x => x.Status).IsRequired().HasMaxLength(20)
                      .HasDefaultValue(TenantSubscriptionStatuses.Active);
                entity.Property(x => x.Notes).IsRequired().HasMaxLength(300).HasDefaultValue("");
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.StartDate).IsRequired();
                entity.Property(x => x.EndDate).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // Billing history has to outlive an experiment: a company that was registered
                // and removed still had revenue recognised against it.
                entity.HasOne(x => x.Company)
                      .WithMany(c => c.Subscriptions)
                      .HasForeignKey(x => x.CompanyId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Plan)
                      .WithMany(p => p.Subscriptions)
                      .HasForeignKey(x => x.SubscriptionPlanId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.CompanyId, x.StartDate });
                entity.HasIndex(x => x.Status);
                entity.HasIndex(x => x.EndDate);
            });

            builder.Entity<PlatformSetting>(entity =>
            {
                entity.HasKey(x => x.PlatformSettingId);
                entity.Property(x => x.SettingKey).IsRequired().HasMaxLength(100);
                entity.Property(x => x.Value).IsRequired().HasMaxLength(1000).HasDefaultValue("");
                entity.Property(x => x.UpdatedBy).IsRequired().HasMaxLength(150).HasDefaultValue("");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(x => x.SettingKey).IsUnique();
            });
        }

        private static void ConfigureAccessControl(ModelBuilder builder)
        {
            builder.Entity<AppRole>(entity =>
            {
                entity.HasKey(x => x.RoleId);
                entity.Property(x => x.RoleKey).IsRequired().HasMaxLength(40);
                entity.Property(x => x.DisplayName).IsRequired().HasMaxLength(80);
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");
                entity.HasIndex(x => x.RoleKey).IsUnique();
            });

            builder.Entity<AppRolePermission>(entity =>
            {
                entity.HasKey(x => x.AppRolePermissionId);
                entity.Property(x => x.Module).IsRequired().HasMaxLength(40);

                entity.HasOne(x => x.Role)
                      .WithMany(r => r.Permissions)
                      .HasForeignKey(x => x.RoleId)
                      .OnDelete(DeleteBehavior.Cascade);

                // A role either grants a module or it does not; two rows would be ambiguous.
                entity.HasIndex(x => new { x.RoleId, x.Module }).IsUnique();
            });

            builder.Entity<AppUser>(entity =>
            {
                entity.HasKey(x => x.AppUserId);
                entity.Property(x => x.Username).IsRequired().HasMaxLength(100);
                entity.Property(x => x.Email).IsRequired().HasMaxLength(200).HasDefaultValue("");
                entity.Property(x => x.FullName).IsRequired().HasMaxLength(200);
                entity.Property(x => x.PasswordHash).IsRequired().HasMaxLength(400);
                entity.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // Sign-in asks for a username and nothing else, so the name has to identify a
                // single account platform-wide rather than one per company.
                entity.HasIndex(x => x.Username).IsUnique();

                entity.HasOne(x => x.Company)
                      .WithMany(c => c.Users)
                      .HasForeignKey(x => x.CompanyId)
                      .OnDelete(DeleteBehavior.Restrict);

                // Removing a role would otherwise orphan its users into having no permissions
                // at all, which reads as a broken account rather than a deliberate one.
                entity.HasOne(x => x.Role)
                      .WithMany(r => r.Users)
                      .HasForeignKey(x => x.RoleId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.CompanyId);
            });

            builder.Entity<AppUserPermission>(entity =>
            {
                entity.HasKey(x => x.AppUserPermissionId);
                entity.Property(x => x.Module).IsRequired().HasMaxLength(40);
                entity.Property(x => x.UpdatedBy).IsRequired().HasMaxLength(100).HasDefaultValue("");
                entity.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.User)
                      .WithMany(u => u.Permissions)
                      .HasForeignKey(x => x.AppUserId)
                      .OnDelete(DeleteBehavior.Cascade);

                // One decision per module per user. The unique index is what makes "grant then
                // revoke" an update rather than a second contradictory row.
                entity.HasIndex(x => new { x.AppUserId, x.Module }).IsUnique();
            });
        }
    }
}
