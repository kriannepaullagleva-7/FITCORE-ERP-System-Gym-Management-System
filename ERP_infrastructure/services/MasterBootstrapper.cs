using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Brings the master database up to a usable state on start-up.
    ///
    /// Three jobs, all idempotent:
    ///   1. The role catalogue and what each role grants by default.
    ///   2. The company-to-tenant registry: which company is on which tier, and where its
    ///      database lives. This is how Tenant B becomes reachable at all.
    ///   3. One account per role per active tenant, so there is somebody to sign in as.
    ///
    /// It matches on natural keys (company code, role key, username) and only ever adds or
    /// updates. It never drops a table, never deletes a row, and never rewrites a password that
    /// has already been set by a real person.
    /// </summary>
    public interface IMasterBootstrapper
    {
        Task<BootstrapReport> RunAsync(CancellationToken cancellationToken = default);
    }

    public class BootstrapReport
    {
        public List<string> Actions { get; } = new();
        public bool Skipped { get; set; }
    }

    public class MasterBootstrapper : IMasterBootstrapper
    {
        private readonly MasterErpDbContext _master;
        private readonly IPasswordHasher<AppUser> _passwordHasher;
        private readonly BootstrapOptions _options;
        private readonly ILogger<MasterBootstrapper> _logger;

        public MasterBootstrapper(
            MasterErpDbContext master,
            IPasswordHasher<AppUser> passwordHasher,
            IOptions<BootstrapOptions> options,
            ILogger<MasterBootstrapper> logger)
        {
            _master = master;
            _passwordHasher = passwordHasher;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<BootstrapReport> RunAsync(CancellationToken cancellationToken = default)
        {
            var report = new BootstrapReport();

            if (!_options.Enabled)
            {
                report.Skipped = true;
                _logger.LogInformation("Bootstrap is disabled; skipping master seeding.");
                return report;
            }

            var roles = await EnsureRolesAsync(report, cancellationToken);

            foreach (var tenant in _options.Tenants)
            {
                await EnsureTenantAsync(tenant, roles, report, cancellationToken);
            }

            if (report.Actions.Count == 0)
            {
                _logger.LogInformation("Master bootstrap: nothing to do, everything is in place.");
            }
            else
            {
                foreach (var action in report.Actions)
                {
                    _logger.LogInformation("Master bootstrap: {Action}", action);
                }
            }

            return report;
        }

        /// <summary>
        /// Creates the three shipped roles and their default module grants.
        ///
        /// An existing role keeps whatever an administrator has since changed it to; only
        /// missing permission rows are added, so re-running this never quietly restores access
        /// that somebody deliberately removed.
        /// </summary>
        private async Task<Dictionary<string, AppRole>> EnsureRolesAsync(
            BootstrapReport report, CancellationToken cancellationToken)
        {
            var existing = await _master.AppRoles
                .Include(r => r.Permissions)
                .ToDictionaryAsync(r => r.RoleKey, StringComparer.OrdinalIgnoreCase, cancellationToken);

            foreach (var roleKey in new[] { ErpRoles.Admin, ErpRoles.Manager, ErpRoles.Staff })
            {
                if (!existing.TryGetValue(roleKey, out var role))
                {
                    role = new AppRole
                    {
                        RoleKey = roleKey,
                        DisplayName = ErpRoles.DisplayNameOf(roleKey),
                        HierarchyLevel = ErpRoles.LevelOf(roleKey),
                        IsSystemRole = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    _master.AppRoles.Add(role);
                    existing[roleKey] = role;
                    report.Actions.Add($"created role '{roleKey}'");
                }

                var have = new HashSet<string>(
                    role.Permissions.Select(p => p.Module), StringComparer.OrdinalIgnoreCase);

                // Only fill in a brand-new role. Once a role exists, its grants belong to
                // whoever has been administering it.
                if (have.Count > 0) continue;

                foreach (var module in ErpRoles.DefaultModulesFor(roleKey))
                {
                    role.Permissions.Add(new AppRolePermission { Module = module });
                }

                report.Actions.Add(
                    $"granted role '{roleKey}' its default modules " +
                    $"({string.Join(", ", ErpRoles.DefaultModulesFor(roleKey))})");
            }

            await _master.SaveChangesAsync(cancellationToken);
            return existing;
        }

        private async Task EnsureTenantAsync(
            BootstrapTenant tenant,
            Dictionary<string, AppRole> roles,
            BootstrapReport report,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(tenant.CompanyCode))
            {
                _logger.LogWarning("A Bootstrap tenant entry has no CompanyCode; skipping it.");
                return;
            }

            if (!Enum.TryParse<EnterpriseTier>(tenant.Tier, ignoreCase: true, out var tier))
            {
                _logger.LogWarning(
                    "Bootstrap tenant {Code} has an unrecognised tier '{Tier}'; skipping it.",
                    tenant.CompanyCode, tenant.Tier);
                return;
            }

            var company = await _master.Companies
                .FirstOrDefaultAsync(c => c.CompanyCode == tenant.CompanyCode, cancellationToken);

            if (company is null)
            {
                company = new Company
                {
                    CompanyCode = tenant.CompanyCode,
                    CompanyName = tenant.CompanyName,
                    IsActive = tenant.Enabled,
                    EnterpriseTier = tier,
                    CreatedAt = DateTime.UtcNow
                };

                _master.Companies.Add(company);
                await _master.SaveChangesAsync(cancellationToken);

                report.Actions.Add(
                    $"registered company '{tenant.CompanyCode}' ({tenant.CompanyName}) " +
                    $"as {tier} Enterprise, id {company.CompanyId}");
            }
            else
            {
                if (company.EnterpriseTier != tier)
                {
                    var previous = company.EnterpriseTier;
                    company.EnterpriseTier = tier;

                    report.Actions.Add(
                        $"moved company {company.CompanyId} ('{tenant.CompanyCode}') " +
                        $"from {previous} to {tier} Enterprise");
                }

                // The company name is shown in the topbar and on every User Access row, so it
                // is kept in step with configuration. Configuration is operator-owned, which
                // makes this a correction rather than an override.
                if (!string.IsNullOrWhiteSpace(tenant.CompanyName) &&
                    company.CompanyName != tenant.CompanyName)
                {
                    report.Actions.Add(
                        $"renamed company {company.CompanyId} from " +
                        $"'{company.CompanyName}' to '{tenant.CompanyName}'");

                    company.CompanyName = tenant.CompanyName;
                }

                if (_master.ChangeTracker.HasChanges())
                {
                    await _master.SaveChangesAsync(cancellationToken);
                }
            }

            await EnsureCompanyDatabaseAsync(company, tenant, report, cancellationToken);

            if (!tenant.Enabled)
            {
                // A reserved tenant is registered so the platform knows it exists, but nobody
                // is given an account for it.
                return;
            }

            foreach (var seedUser in tenant.Users)
            {
                await EnsureUserAsync(company, seedUser, roles, report, cancellationToken);
            }
        }

        /// <summary>
        /// Points the company at its database. An existing active row for the same server and
        /// database is left alone; a row pointing somewhere else is never silently rewritten,
        /// because that would move a live tenant onto a different database.
        /// </summary>
        private async Task EnsureCompanyDatabaseAsync(
            Company company,
            BootstrapTenant tenant,
            BootstrapReport report,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(tenant.Server) || string.IsNullOrWhiteSpace(tenant.Database))
            {
                return;
            }

            var rows = await _master.CompanyDatabases
                .Where(d => d.CompanyId == company.CompanyId)
                .ToListAsync(cancellationToken);

            var match = rows.FirstOrDefault(d =>
                string.Equals(d.ServerName, tenant.Server, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.DatabaseName, tenant.Database, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                var changed = false;

                if (!match.IsActive)
                {
                    match.IsActive = true;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(tenant.CredentialKey) &&
                    match.CredentialKey != tenant.CredentialKey)
                {
                    match.CredentialKey = tenant.CredentialKey;
                    changed = true;
                }

                if (changed)
                {
                    await _master.SaveChangesAsync(cancellationToken);
                    report.Actions.Add(
                        $"corrected the database registration for company {company.CompanyId}");
                }

                return;
            }

            if (rows.Any(d => d.IsActive))
            {
                // Already pointed at a different database. Changing that is an operational
                // decision, not something a start-up routine should make on its own.
                _logger.LogWarning(
                    "Company {CompanyId} is already registered against a different active database. " +
                    "Bootstrap left it unchanged.", company.CompanyId);
                return;
            }

            _master.CompanyDatabases.Add(new CompanyDatabase
            {
                CompanyId = company.CompanyId,
                ServerName = tenant.Server,
                DatabaseName = tenant.Database,
                CredentialKey = tenant.CredentialKey,
                IsActive = true
            });

            await _master.SaveChangesAsync(cancellationToken);

            report.Actions.Add(
                $"registered the tenant database for company {company.CompanyId} " +
                $"(credential key '{tenant.CredentialKey}')");
        }

        private async Task EnsureUserAsync(
            Company company,
            BootstrapUser seedUser,
            Dictionary<string, AppRole> roles,
            BootstrapReport report,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(seedUser.Username)) return;

            if (!roles.TryGetValue(seedUser.Role ?? "", out var role))
            {
                _logger.LogWarning(
                    "Bootstrap user {Username} names an unknown role '{Role}'; skipping.",
                    seedUser.Username, seedUser.Role);
                return;
            }

            var exists = await _master.AppUsers
                .AnyAsync(u => u.Username == seedUser.Username, cancellationToken);

            if (exists) return;

            if (string.IsNullOrWhiteSpace(_options.SeedPassword) ||
                _options.SeedPassword.Trim().Length < 8)
            {
                _logger.LogError(
                    "Cannot create bootstrap user {Username}: Bootstrap:SeedPassword is missing or " +
                    "shorter than 8 characters.", seedUser.Username);
                return;
            }

            var user = new AppUser
            {
                CompanyId = company.CompanyId,
                Username = seedUser.Username.Trim(),
                FullName = (seedUser.FullName ?? seedUser.Username).Trim(),
                Email = (seedUser.Email ?? "").Trim(),
                RoleId = role.RoleId,
                IsActive = true,

                // Seeded credentials are a way in, not a permanent account.
                MustChangePassword = true,
                CreatedAt = DateTime.UtcNow
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, _options.SeedPassword);

            _master.AppUsers.Add(user);
            await _master.SaveChangesAsync(cancellationToken);

            report.Actions.Add(
                $"created user '{user.Username}' ({role.RoleKey}) for company {company.CompanyId}");
        }
    }
}
