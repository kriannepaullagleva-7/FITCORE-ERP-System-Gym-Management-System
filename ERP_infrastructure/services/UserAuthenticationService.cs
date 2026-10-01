using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERP_infrastructure.services
{
    /// <inheritdoc />
    public class UserAuthenticationService : IUserAuthenticationService
    {
        private readonly MasterErpDbContext _master;
        private readonly IPasswordHasher<AppUser> _passwordHasher;
        private readonly ILogger<UserAuthenticationService> _logger;
        private readonly IAuthAuditService _audit;

        public UserAuthenticationService(
            MasterErpDbContext master,
            IPasswordHasher<AppUser> passwordHasher,
            ILogger<UserAuthenticationService> logger,
            IAuthAuditService audit)
        {
            _master = master;
            _passwordHasher = passwordHasher;
            _logger = logger;
            _audit = audit;
        }

        public async Task<LoginResult> AuthenticateAsync(
            string username, string password, CancellationToken cancellationToken = default)
        {
            var normalised = (username ?? string.Empty).Trim();

            // The same account may be addressed by username or by email address; people
            // remember one or the other, and a sign-in form that insists on the right one is
            // just a worse form.
            var user = await LoadUserGraph()
                .FirstOrDefaultAsync(u =>
                    u.Username == normalised || u.Email == normalised,
                    cancellationToken);

            if (user is null)
            {
                // Hash anyway. Returning immediately would make "no such user" measurably
                // faster than "wrong password", which turns the login form into an oracle for
                // which account names exist.
                _passwordHasher.HashPassword(new AppUser(), password ?? string.Empty);

                _logger.LogInformation("Rejected sign-in for an unknown account name.");

                // Recorded without the name that was tried: a trail an administrator reads
                // should not become a list of guessed usernames. The count is the signal.
                await _audit.RecordAsync(
                    AuditActions.LoginFailed,
                    AuditActor.System,
                    "Sign-in attempt for an account name that does not exist",
                    cancellationToken);

                return LoginResult.Fail(LoginFailureReason.InvalidCredentials);
            }

            var verification = _passwordHasher.VerifyHashedPassword(
                user, user.PasswordHash, password ?? string.Empty);

            if (verification == PasswordVerificationResult.Failed)
            {
                _logger.LogInformation(
                    "Rejected sign-in for {Username}: wrong password.", user.Username);

                await _audit.RecordAsync(
                    AuditActions.LoginFailed, ActorFor(user), "Wrong password", cancellationToken);

                return LoginResult.Fail(LoginFailureReason.InvalidCredentials);
            }

            // Checked after the password so that probing the form cannot reveal which accounts
            // exist but are switched off.
            if (!user.IsActive)
            {
                _logger.LogWarning(
                    "Rejected sign-in for {Username}: the account is deactivated.", user.Username);

                await _audit.RecordAsync(
                    AuditActions.LoginFailed, ActorFor(user),
                    "Account is deactivated", cancellationToken);

                return LoginResult.Fail(LoginFailureReason.AccountDeactivated);
            }

            if (!user.Company.IsActive)
            {
                _logger.LogWarning(
                    "Rejected sign-in for {Username}: company {CompanyId} is inactive.",
                    user.Username, user.CompanyId);

                await _audit.RecordAsync(
                    AuditActions.LoginFailed, ActorFor(user),
                    "Company is inactive", cancellationToken);

                return LoginResult.Fail(LoginFailureReason.CompanyInactive);
            }

            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, password ?? string.Empty);
            }

            user.LastLoginAt = DateTime.UtcNow;
            await _master.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "{Username} signed in to company {CompanyId} as {Role}.",
                user.Username, user.CompanyId, user.Role.RoleKey);

            await _audit.RecordAsync(
                AuditActions.Login, ActorFor(user),
                $"Signed in as {ErpRoles.DisplayNameOf(user.Role.RoleKey)}", cancellationToken);

            return LoginResult.Success(Project(user));
        }

        /// <summary>
        /// The actor for a sign-in event. It cannot come from the request, because the point of
        /// the event is that the caller is not signed in yet.
        /// </summary>
        private static AuditActor ActorFor(AppUser user) =>
            new(user.AppUserId, user.Username, user.Role?.RoleKey ?? "",
                user.CompanyId, null, null);

        public async Task<AuthenticatedUser?> GetAuthenticatedUserAsync(
            int appUserId, CancellationToken cancellationToken = default)
        {
            var user = await LoadUserGraph()
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.AppUserId == appUserId, cancellationToken);

            if (user is null || !user.IsActive || !user.Company.IsActive)
            {
                return null;
            }

            return Project(user);
        }

        public async Task<bool> ChangePasswordAsync(
            int appUserId, string currentPassword, string newPassword,
            CancellationToken cancellationToken = default)
        {
            var user = await _master.AppUsers
                .FirstOrDefaultAsync(u => u.AppUserId == appUserId, cancellationToken);

            if (user is null || !user.IsActive)
            {
                return false;
            }

            var verification = _passwordHasher.VerifyHashedPassword(
                user, user.PasswordHash, currentPassword ?? string.Empty);

            if (verification == PasswordVerificationResult.Failed)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Trim().Length < 8)
            {
                throw new ValidationException("A new password must be at least 8 characters long.");
            }

            user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
            user.MustChangePassword = false;
            user.UpdatedAt = DateTime.UtcNow;

            await _master.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("{Username} changed their password.", user.Username);

            // The automatic sweep records that AppUser changed, but it excludes PasswordHash
            // from the values - so without this the trail would show an edit with nothing in it.
            await _audit.RecordAsync(
                AuditActions.PasswordChanged, ActorFor(user),
                "Changed their own password", cancellationToken);

            return true;
        }

        private IQueryable<AppUser> LoadUserGraph() =>
            _master.AppUsers
                .Include(u => u.Company)
                .Include(u => u.Role)
                    .ThenInclude(r => r.Permissions)
                .Include(u => u.Permissions);

        private static AuthenticatedUser Project(AppUser user)
        {
            var projected = ProjectModules(user);

            // Derived from what was just resolved rather than read from anywhere, so the
            // subfeatures a user is offered can never name a module they do not hold.
            projected.Submodules = PermissionResolver.ResolveSubmodules(
                projected.EnterpriseTier, projected.RoleLevel, projected.Modules);

            return projected;
        }

        private static AuthenticatedUser ProjectModules(AppUser user) => new()
        {
            AppUserId = user.AppUserId,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,

            CompanyId = user.CompanyId,
            CompanyName = user.Company.CompanyName,
            CompanyCode = user.Company.CompanyCode,
            EnterpriseTier = user.Company.EnterpriseTier,

            RoleKey = user.Role.RoleKey,
            RoleDisplayName = user.Role.DisplayName,
            RoleLevel = user.Role.HierarchyLevel,

            EmployeeId = user.EmployeeId,
            BranchId = user.BranchId,
            MustChangePassword = user.MustChangePassword,

            Modules = PermissionResolver.Resolve(
                user.Company.EnterpriseTier,
                user.Role.Permissions.Select(p => p.Module),
                user.Permissions)
        };
    }
}
