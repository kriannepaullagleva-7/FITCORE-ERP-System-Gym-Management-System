using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Gives a Medium tenant its branch network, and each branch the staff that run it.
    ///
    /// This is the one bootstrap step that writes to both databases, and the split is the same
    /// one the rest of the system keeps: the <see cref="Branch"/> rows and the
    /// <see cref="Employee"/> records go into the <em>tenant</em> database, because they are the
    /// company's own operational data; only the <see cref="AppUser"/> sign-in accounts are
    /// master-side, because an account is what selects a tenant and so has to be readable before
    /// any tenant database is opened. No tenant data is copied into the master database - the
    /// account carries the branch's identifier and nothing else about it.
    ///
    /// Idempotent and additive throughout. Branches are matched by code, employees by code and
    /// accounts by username; nothing is deleted, no password already set is rewritten, and a
    /// second run against a seeded tenant does nothing at all.
    /// </summary>
    public interface ITenantBranchBootstrapper
    {
        Task RunAsync(BootstrapReport report, CancellationToken cancellationToken = default);
    }

    public class TenantBranchBootstrapper : ITenantBranchBootstrapper
    {
        private readonly MasterErpDbContext _master;
        private readonly ITenantDbContextFactory _tenantFactory;
        private readonly IPasswordHasher<AppUser> _passwordHasher;
        private readonly BootstrapOptions _options;
        private readonly ILogger<TenantBranchBootstrapper> _logger;

        public TenantBranchBootstrapper(
            MasterErpDbContext master,
            ITenantDbContextFactory tenantFactory,
            IPasswordHasher<AppUser> passwordHasher,
            IOptions<BootstrapOptions> options,
            ILogger<TenantBranchBootstrapper> logger)
        {
            _master = master;
            _tenantFactory = tenantFactory;
            _passwordHasher = passwordHasher;
            _options = options.Value;
            _logger = logger;
        }

        public async Task RunAsync(
            BootstrapReport report, CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled) return;

            foreach (var tenant in _options.Tenants)
            {
                if (!tenant.Enabled || tenant.Branches.Count == 0) continue;

                if (!Enum.TryParse<EnterpriseTier>(tenant.Tier, true, out var tier) ||
                    tier < EnterpriseTier.Medium)
                {
                    // Branching is a Medium feature. Listing branches for a smaller plan is a
                    // configuration mistake, and creating them anyway would hand a tenant a
                    // feature they are not licensed for through the back door.
                    _logger.LogWarning(
                        "Bootstrap tenant {Code} lists branches but is on the {Tier} tier. " +
                        "Branching is Medium and above; the branches were ignored.",
                        tenant.CompanyCode, tenant.Tier);
                    continue;
                }

                var company = await _master.Companies.FirstOrDefaultAsync(
                    c => c.CompanyCode == tenant.CompanyCode, cancellationToken);

                if (company is null || !company.IsActive) continue;

                try
                {
                    await SeedCompanyAsync(company, tenant, report, cancellationToken);
                }
                catch (Exception ex)
                {
                    // One unreachable tenant database must not stop the others, and must not
                    // stop the API. Sign-in does not depend on this having run.
                    _logger.LogError(ex,
                        "Branch bootstrap did not complete for company {CompanyId} ({Code}).",
                        company.CompanyId, tenant.CompanyCode);
                }
            }
        }

        private async Task SeedCompanyAsync(
            Company company,
            BootstrapTenant tenant,
            BootstrapReport report,
            CancellationToken cancellationToken)
        {
            await using var db = await _tenantFactory.CreateAsync(company.CompanyId);

            var existing = await db.Branches.ToListAsync(cancellationToken);
            var hadNoBranches = existing.Count == 0;

            var byCode = existing.ToDictionary(b => b.Code, StringComparer.OrdinalIgnoreCase);

            var isFirst = true;

            foreach (var configured in tenant.Branches)
            {
                if (string.IsNullOrWhiteSpace(configured.Code)) continue;

                var code = configured.Code.Trim().ToUpperInvariant();

                if (!byCode.TryGetValue(code, out var branch))
                {
                    branch = new Branch
                    {
                        Code = code,
                        Name = string.IsNullOrWhiteSpace(configured.Name)
                            ? code
                            : configured.Name.Trim(),
                        Address = configured.Address?.Trim() ?? "",
                        Phone = configured.Phone?.Trim() ?? "",
                        Email = configured.Email?.Trim() ?? "",

                        // The first configured branch is the primary one: where an unassigned
                        // record falls, and what existing history is adopted into below.
                        IsPrimary = isFirst,
                        IsActive = true,
                        OpenedOn = DateTime.UtcNow.Date,
                        CreatedAt = DateTime.UtcNow
                    };

                    db.Branches.Add(branch);
                    await db.SaveChangesAsync(cancellationToken);

                    byCode[code] = branch;

                    report.Actions.Add(
                        $"created branch '{code}' ({branch.Name}) for company {company.CompanyId}");
                }

                isFirst = false;
            }

            // A company that has just grown its first branches still has all of its existing
            // records belonging to no branch. Left that way they would vanish the moment an
            // Admin selected a branch, and would count towards no branch at all - so they are
            // adopted, once, into the primary branch. This runs only on the transition from
            // "no branches" to "some", so it can never reassign a record a second time.
            if (hadNoBranches)
            {
                var primary = byCode.Values.FirstOrDefault(b => b.IsPrimary);

                if (primary is not null)
                {
                    var adopted = await AdoptExistingRecordsAsync(
                        db, primary.BranchId, cancellationToken);

                    if (adopted > 0)
                    {
                        report.Actions.Add(
                            $"assigned {adopted} existing record(s) of company {company.CompanyId} " +
                            $"to its primary branch '{primary.Code}'");
                    }
                }
            }

            foreach (var configured in tenant.Branches)
            {
                if (string.IsNullOrWhiteSpace(configured.Code)) continue;

                var code = configured.Code.Trim().ToUpperInvariant();
                if (!byCode.TryGetValue(code, out var branch)) continue;

                foreach (var seedUser in configured.Users)
                {
                    await EnsureBranchStaffAsync(
                        db, company, branch, seedUser, report, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Moves every branch-scoped row that belongs to no branch into the primary one.
        ///
        /// Driven off the model rather than a list of table names, so an entity that becomes
        /// branch-scoped later is adopted too: the sweep covers everything implementing
        /// <see cref="IBranchScoped"/>, which is exactly the set the query filter covers.
        /// </summary>
        private static async Task<int> AdoptExistingRecordsAsync(
            TenantErpDbContext db, int primaryBranchId, CancellationToken cancellationToken)
        {
            var total = 0;

            foreach (var entityType in db.Model.GetEntityTypes())
            {
                if (entityType.BaseType is not null) continue;
                if (!typeof(IBranchScoped).IsAssignableFrom(entityType.ClrType)) continue;

                var task = (Task<int>)AdoptMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(null, new object[] { db, primaryBranchId, cancellationToken })!;

                total += await task;
            }

            return total;
        }

        private static readonly System.Reflection.MethodInfo AdoptMethod =
            typeof(TenantBranchBootstrapper).GetMethod(
                nameof(AdoptAsync),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        /// <summary>
        /// Set-based on purpose: loading every member, sale and payment of a live tenant into
        /// memory to stamp one column would make start-up take minutes. ExecuteUpdate issues one
        /// UPDATE per table and, being typed, involves no SQL string for a table name to be
        /// interpolated into.
        /// </summary>
        private static Task<int> AdoptAsync<TEntity>(
            TenantErpDbContext db, int primaryBranchId, CancellationToken cancellationToken)
            where TEntity : class, IBranchScoped =>
            db.Set<TEntity>()
                .IgnoreQueryFilters()
                .Where(e => e.BranchId == null)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.BranchId, primaryBranchId),
                    cancellationToken);

        /// <summary>
        /// One member of branch staff: an Employee row inside the tenant, and a sign-in account
        /// in the master database bound to the branch.
        ///
        /// The account's <see cref="AppUser.BranchId"/> is what sign-in turns into a branch
        /// claim, and that claim is what narrows every query they make. A Manager or Staff
        /// account created here can never read a sibling branch, whatever it sends.
        /// </summary>
        private async Task EnsureBranchStaffAsync(
            TenantErpDbContext db,
            Company company,
            Branch branch,
            BootstrapUser seedUser,
            BootstrapReport report,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(seedUser.Username)) return;

            var username = seedUser.Username.Trim();

            var account = await _master.AppUsers
                .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

            // An account that exists but predates branching is adopted into its branch rather
            // than left unscoped - otherwise a seeded branch manager would quietly keep seeing
            // the whole company. One already bound to a branch is left exactly as it is.
            if (account is not null)
            {
                if (account.BranchId is null && account.CompanyId == company.CompanyId)
                {
                    account.BranchId = branch.BranchId;
                    await _master.SaveChangesAsync(cancellationToken);

                    report.Actions.Add(
                        $"bound existing account '{username}' to branch '{branch.Code}'");
                }

                return;
            }

            var role = await _master.AppRoles.FirstOrDefaultAsync(
                r => r.RoleKey == (seedUser.Role ?? ""), cancellationToken);

            if (role is null)
            {
                _logger.LogWarning(
                    "Branch account {Username} names an unknown role '{Role}'; skipping.",
                    username, seedUser.Role);
                return;
            }

            if (string.IsNullOrWhiteSpace(_options.SeedPassword) ||
                _options.SeedPassword.Trim().Length < 8)
            {
                _logger.LogError(
                    "Cannot create branch account {Username}: Bootstrap:SeedPassword is missing " +
                    "or shorter than 8 characters.", username);
                return;
            }

            var employeeCode = $"{branch.Code}-{username.ToUpperInvariant()}";

            var employee = await db.Employees.IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.EmployeeCode == employeeCode, cancellationToken);

            if (employee is null)
            {
                var fullName = (seedUser.FullName ?? username).Trim();
                var split = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

                employee = new Employee
                {
                    BranchId = branch.BranchId,
                    EmployeeCode = employeeCode,
                    FirstName = split.Length > 0 ? split[0] : fullName,
                    LastName = split.Length > 1 ? split[1] : "",
                    Position = string.IsNullOrWhiteSpace(seedUser.Position)
                        ? ErpRoles.DisplayNameOf(seedUser.Role ?? "")
                        : seedUser.Position.Trim(),
                    Department = branch.Name,
                    Email = (seedUser.Email ?? "").Trim(),
                    HireDate = DateTime.UtcNow.Date,
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                };

                db.Employees.Add(employee);
                await db.SaveChangesAsync(cancellationToken);
            }

            var user = new AppUser
            {
                CompanyId = company.CompanyId,
                BranchId = branch.BranchId,
                EmployeeId = employee.EmployeeId,
                Username = username,
                FullName = (seedUser.FullName ?? username).Trim(),
                Email = (seedUser.Email ?? "").Trim(),
                RoleId = role.RoleId,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = DateTime.UtcNow
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, _options.SeedPassword);

            _master.AppUsers.Add(user);
            await _master.SaveChangesAsync(cancellationToken);

            report.Actions.Add(
                $"created branch account '{username}' ({role.RoleKey}) at '{branch.Code}' " +
                $"for company {company.CompanyId}");
        }
    }
}
