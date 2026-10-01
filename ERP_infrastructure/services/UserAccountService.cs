using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERP_infrastructure.services
{
    /// <inheritdoc />
    public class UserAccountService : IUserAccountService
    {
        private readonly MasterErpDbContext _master;
        private readonly IPasswordHasher<AppUser> _passwordHasher;
        private readonly ILogger<UserAccountService> _logger;

        public UserAccountService(
            MasterErpDbContext master,
            IPasswordHasher<AppUser> passwordHasher,
            ILogger<UserAccountService> logger)
        {
            _master = master;
            _passwordHasher = passwordHasher;
            _logger = logger;
        }

        public async Task<IEnumerable<UserAccountView>> GetUsersAsync(
            int companyId, CancellationToken cancellationToken = default)
        {
            var users = await LoadGraph()
                .AsNoTracking()
                .Where(u => u.CompanyId == companyId)
                .OrderBy(u => u.Role.HierarchyLevel)
                .ThenBy(u => u.FullName)
                .ToListAsync(cancellationToken);

            return users.Select(Project).ToList();
        }

        public async Task<UserAccountView?> GetUserAsync(
            int companyId, int appUserId, CancellationToken cancellationToken = default)
        {
            var user = await LoadGraph()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    u => u.AppUserId == appUserId && u.CompanyId == companyId, cancellationToken);

            return user is null ? null : Project(user);
        }

        public async Task<UserPermissionEditorView?> GetPermissionEditorAsync(
            int companyId, int appUserId, CancellationToken cancellationToken = default)
        {
            var user = await LoadGraph()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    u => u.AppUserId == appUserId && u.CompanyId == companyId, cancellationToken);

            return user is null ? null : ProjectEditor(user);
        }

        public async Task<UserPermissionEditorView?> SetModuleAccessAsync(
            int companyId,
            int appUserId,
            IEnumerable<string> grantedModules,
            int actingRoleLevel,
            string actingUsername,
            CancellationToken cancellationToken = default)
        {
            var user = await LoadGraph()
                .FirstOrDefaultAsync(
                    u => u.AppUserId == appUserId && u.CompanyId == companyId, cancellationToken);

            if (user is null) return null;

            GuardSeniority(actingRoleLevel, user, "change the access of");

            var tier = user.Company.EnterpriseTier;

            var requested = new HashSet<string>(
                (grantedModules ?? Enumerable.Empty<string>())
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var unknown = requested.Where(m => !ErpModules.IsKnown(m)).ToList();
            if (unknown.Count > 0)
            {
                throw new ValidationException(
                    $"Unknown module(s): {string.Join(", ", unknown)}.");
            }

            // Granting a module the company is not licensed for would write a permission that
            // can never take effect, so it is refused rather than silently dropped.
            var outOfTier = requested
                .Select(m => ErpModules.Find(m)!)
                .Where(d => d.MinimumTier > tier)
                .Select(d => d.DisplayName)
                .ToList();

            if (outOfTier.Count > 0)
            {
                throw new ValidationException(
                    $"{string.Join(", ", outOfTier)} is not part of the {tier} Enterprise plan, " +
                    "so access to it cannot be granted.");
            }

            var roleModules = new HashSet<string>(
                user.Role.Permissions.Select(p => p.Module), StringComparer.OrdinalIgnoreCase);

            var existing = user.Permissions.ToDictionary(
                p => p.Module, StringComparer.OrdinalIgnoreCase);

            var now = DateTime.UtcNow;

            // Only the modules the company actually has are decided here; the rest are left
            // alone so that promoting a tenant to a higher tier later does not find stale rows.
            foreach (var definition in ErpModules.ForTier(tier))
            {
                var shouldBeGranted = requested.Contains(definition.Key);
                var roleGrants = roleModules.Contains(definition.Key);

                existing.TryGetValue(definition.Key, out var row);

                if (shouldBeGranted == roleGrants)
                {
                    // Back in step with the role, so the override is removed rather than
                    // recorded. That way a later change to the role reaches this user again.
                    if (row is not null)
                    {
                        _master.AppUserPermissions.Remove(row);
                        user.Permissions.Remove(row);
                    }

                    continue;
                }

                if (row is null)
                {
                    row = new AppUserPermission
                    {
                        AppUserId = user.AppUserId,
                        Module = definition.Key
                    };

                    _master.AppUserPermissions.Add(row);
                    user.Permissions.Add(row);
                }

                row.IsGranted = shouldBeGranted;
                row.UpdatedAt = now;
                row.UpdatedBy = actingUsername ?? "";
            }

            user.UpdatedAt = now;
            await _master.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "{Actor} updated module access for {Username} (company {CompanyId}) to: {Modules}.",
                actingUsername, user.Username, companyId, string.Join(", ", requested));

            return ProjectEditor(user);
        }

        public async Task<UserAccountView?> SetActiveAsync(
            int companyId, int appUserId, bool isActive, int actingRoleLevel, int actingUserId,
            CancellationToken cancellationToken = default)
        {
            var user = await LoadGraph()
                .FirstOrDefaultAsync(
                    u => u.AppUserId == appUserId && u.CompanyId == companyId, cancellationToken);

            if (user is null) return null;

            if (user.AppUserId == actingUserId && !isActive)
            {
                throw new ValidationException(
                    "You cannot deactivate the account you are signed in with.");
            }

            GuardSeniority(actingRoleLevel, user, isActive ? "reactivate" : "deactivate");

            // The last active administrator is what stops a company locking itself out of its
            // own User Access screen, so removing them is refused.
            if (!isActive && user.Role.RoleKey == ErpRoles.Admin)
            {
                var otherActiveAdmins = await _master.AppUsers
                    .CountAsync(u =>
                        u.CompanyId == companyId &&
                        u.AppUserId != appUserId &&
                        u.IsActive &&
                        u.Role.RoleKey == ErpRoles.Admin,
                        cancellationToken);

                if (otherActiveAdmins == 0)
                {
                    throw new ValidationException(
                        "This is the only active administrator for the company. " +
                        "Give another user the Admin role before deactivating this one.");
                }
            }

            user.IsActive = isActive;
            user.UpdatedAt = DateTime.UtcNow;

            await _master.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "{Username} was {State}.", user.Username, isActive ? "reactivated" : "deactivated");

            return Project(user);
        }

        public async Task<UserAccountView?> SetRoleAsync(
            int companyId, int appUserId, string roleKey, int actingRoleLevel, int actingUserId,
            CancellationToken cancellationToken = default)
        {
            var user = await LoadGraph()
                .FirstOrDefaultAsync(
                    u => u.AppUserId == appUserId && u.CompanyId == companyId, cancellationToken);

            if (user is null) return null;

            GuardSeniority(actingRoleLevel, user, "change the role of");

            var role = await _master.AppRoles
                .Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.RoleKey == roleKey, cancellationToken)
                ?? throw new ValidationException($"There is no role called '{roleKey}'.");

            // Handing out a role more senior than your own is a privilege escalation, whether
            // the target is yourself or somebody else.
            if (role.HierarchyLevel < actingRoleLevel)
            {
                throw new ValidationException(
                    $"You cannot assign the {role.DisplayName} role, because it is more senior " +
                    "than your own.");
            }

            if (user.AppUserId == actingUserId && role.RoleKey != user.Role.RoleKey)
            {
                throw new ValidationException("You cannot change your own role.");
            }

            // Overrides were decided against the old role, so carrying them across would give
            // access nobody chose. The new role starts from its own defaults.
            if (user.Permissions.Count > 0)
            {
                _master.AppUserPermissions.RemoveRange(user.Permissions);
                user.Permissions.Clear();
            }

            user.RoleId = role.RoleId;
            user.Role = role;
            user.UpdatedAt = DateTime.UtcNow;

            await _master.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "{Username} was moved to the {Role} role.", user.Username, role.RoleKey);

            return Project(user);
        }

        public async Task<UserAccountView> CreateUserAsync(
            int companyId,
            string username,
            string fullName,
            string email,
            string password,
            string roleKey,
            int? employeeId,
            int actingRoleLevel,
            CancellationToken cancellationToken = default)
        {
            username = (username ?? "").Trim();
            fullName = (fullName ?? "").Trim();
            email = (email ?? "").Trim();

            if (string.IsNullOrWhiteSpace(username))
                throw new ValidationException("A username is required.");

            if (username.Length < 3)
                throw new ValidationException("A username must be at least 3 characters long.");

            if (string.IsNullOrWhiteSpace(fullName))
                throw new ValidationException("A full name is required.");

            if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 8)
                throw new ValidationException("A password must be at least 8 characters long.");

            var role = await _master.AppRoles
                .Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.RoleKey == roleKey, cancellationToken)
                ?? throw new ValidationException($"There is no role called '{roleKey}'.");

            if (role.HierarchyLevel < actingRoleLevel)
            {
                throw new ValidationException(
                    $"You cannot create a user with the {role.DisplayName} role, because it is " +
                    "more senior than your own.");
            }

            // Usernames are unique platform-wide because sign-in asks for nothing else.
            var taken = await _master.AppUsers
                .AnyAsync(u => u.Username == username, cancellationToken);

            if (taken)
            {
                throw new ValidationException(
                    $"The username '{username}' is already taken.");
            }

            var company = await _master.Companies
                .FirstOrDefaultAsync(c => c.CompanyId == companyId, cancellationToken)
                ?? throw new ValidationException("The company could not be found.");

            var user = new AppUser
            {
                CompanyId = companyId,
                Company = company,
                Username = username,
                FullName = fullName,
                Email = email,
                RoleId = role.RoleId,
                Role = role,
                EmployeeId = employeeId,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = DateTime.UtcNow
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            _master.AppUsers.Add(user);
            await _master.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Created user {Username} in company {CompanyId} with the {Role} role.",
                username, companyId, role.RoleKey);

            return Project(user);
        }

        public async Task<UserAccountView?> UpdateUserAsync(
            int companyId,
            int appUserId,
            string fullName,
            string email,
            int? employeeId,
            int actingRoleLevel,
            CancellationToken cancellationToken = default)
        {
            var user = await LoadGraph()
                .FirstOrDefaultAsync(
                    u => u.AppUserId == appUserId && u.CompanyId == companyId, cancellationToken);

            if (user is null) return null;

            GuardSeniority(actingRoleLevel, user, "edit");

            fullName = (fullName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(fullName))
                throw new ValidationException("A full name is required.");

            user.FullName = fullName;
            user.Email = (email ?? "").Trim();
            user.EmployeeId = employeeId;
            user.UpdatedAt = DateTime.UtcNow;

            await _master.SaveChangesAsync(cancellationToken);
            return Project(user);
        }

        public async Task<bool> ResetPasswordAsync(
            int companyId, int appUserId, string newPassword, int actingRoleLevel,
            CancellationToken cancellationToken = default)
        {
            var user = await LoadGraph()
                .FirstOrDefaultAsync(
                    u => u.AppUserId == appUserId && u.CompanyId == companyId, cancellationToken);

            if (user is null) return false;

            GuardSeniority(actingRoleLevel, user, "reset the password of");

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Trim().Length < 8)
                throw new ValidationException("A password must be at least 8 characters long.");

            user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
            user.MustChangePassword = true;
            user.UpdatedAt = DateTime.UtcNow;

            await _master.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("The password for {Username} was reset.", user.Username);
            return true;
        }

        public async Task<IEnumerable<RoleView>> GetRolesAsync(
            CancellationToken cancellationToken = default)
        {
            var roles = await _master.AppRoles
                .AsNoTracking()
                .Include(r => r.Permissions)
                .OrderBy(r => r.HierarchyLevel)
                .ToListAsync(cancellationToken);

            return roles.Select(r => new RoleView
            {
                RoleId = r.RoleId,
                RoleKey = r.RoleKey,
                DisplayName = r.DisplayName,
                HierarchyLevel = r.HierarchyLevel,
                DefaultModules = r.Permissions.Select(p => p.Module).OrderBy(m => m).ToList()
            }).ToList();
        }

        /// <summary>
        /// Refuses an edit aimed at somebody at or above the caller's own level. Without this a
        /// manager could grant themselves - or another manager - whatever an owner has.
        /// </summary>
        private static void GuardSeniority(int actingRoleLevel, AppUser target, string verb)
        {
            if (target.Role.HierarchyLevel < actingRoleLevel)
            {
                throw new ValidationException(
                    $"You cannot {verb} a {target.Role.DisplayName} account, because that role is " +
                    "more senior than your own.");
            }
        }

        private IQueryable<AppUser> LoadGraph() =>
            _master.AppUsers
                .Include(u => u.Company)
                .Include(u => u.Role)
                    .ThenInclude(r => r.Permissions)
                .Include(u => u.Permissions);

        private static UserAccountView Project(AppUser user) => new()
        {
            AppUserId = user.AppUserId,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            RoleKey = user.Role.RoleKey,
            RoleDisplayName = user.Role.DisplayName,
            RoleLevel = user.Role.HierarchyLevel,
            IsActive = user.IsActive,
            EmployeeId = user.EmployeeId,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            CompanyId = user.CompanyId,
            CompanyName = user.Company?.CompanyName ?? "",
            Modules = PermissionResolver.Resolve(
                user.Company?.EnterpriseTier ?? EnterpriseTier.Micro,
                user.Role.Permissions.Select(p => p.Module),
                user.Permissions)
        };

        private static UserPermissionEditorView ProjectEditor(AppUser user)
        {
            var tier = user.Company?.EnterpriseTier ?? EnterpriseTier.Micro;

            return new UserPermissionEditorView
            {
                AppUserId = user.AppUserId,
                Username = user.Username,
                FullName = user.FullName,
                RoleKey = user.Role.RoleKey,
                RoleDisplayName = user.Role.DisplayName,
                IsActive = user.IsActive,
                EnterpriseTierName = tier.ToString(),
                RoleLevel = user.Role.HierarchyLevel,
                Modules = PermissionResolver.Describe(
                    tier,
                    user.Role.Permissions.Select(p => p.Module),
                    user.Permissions),

                // The whole subfeature catalogue with the reason each one is or is not
                // available, so an administrator can see the shape of the plan rather than
                // nine checkboxes that do not explain why a screen is missing.
                Submodules = PermissionResolver.DescribeSubmodules(
                    tier,
                    user.Role.HierarchyLevel,
                    PermissionResolver.Resolve(
                        tier,
                        user.Role.Permissions.Select(p => p.Module),
                        user.Permissions))
            };
        }
    }
}
