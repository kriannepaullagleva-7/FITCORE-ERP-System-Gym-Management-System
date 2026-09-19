namespace ERP_infrastructure.services
{
    /// <summary>
    /// Backs the User Access screen.
    ///
    /// Every method takes the acting user's company and role level, and every query is filtered
    /// by that company. That is what keeps User Access tenant-safe: an administrator of one
    /// company cannot see, edit or deactivate an account belonging to another, whatever id they
    /// put in the URL.
    /// </summary>
    public interface IUserAccountService
    {
        Task<IEnumerable<UserAccountView>> GetUsersAsync(
            int companyId, CancellationToken cancellationToken = default);

        Task<UserAccountView?> GetUserAsync(
            int companyId, int appUserId, CancellationToken cancellationToken = default);

        Task<UserPermissionEditorView?> GetPermissionEditorAsync(
            int companyId, int appUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Replaces this user's module access with the supplied set, written as overrides
        /// against their role. Returns null when the user is not in the acting company.
        /// </summary>
        Task<UserPermissionEditorView?> SetModuleAccessAsync(
            int companyId,
            int appUserId,
            IEnumerable<string> grantedModules,
            int actingRoleLevel,
            string actingUsername,
            CancellationToken cancellationToken = default);

        Task<UserAccountView?> SetActiveAsync(
            int companyId, int appUserId, bool isActive, int actingRoleLevel, int actingUserId,
            CancellationToken cancellationToken = default);

        Task<UserAccountView?> SetRoleAsync(
            int companyId, int appUserId, string roleKey, int actingRoleLevel, int actingUserId,
            CancellationToken cancellationToken = default);

        Task<UserAccountView> CreateUserAsync(
            int companyId,
            string username,
            string fullName,
            string email,
            string password,
            string roleKey,
            int? employeeId,
            int actingRoleLevel,
            CancellationToken cancellationToken = default);

        Task<UserAccountView?> UpdateUserAsync(
            int companyId,
            int appUserId,
            string fullName,
            string email,
            int? employeeId,
            int actingRoleLevel,
            CancellationToken cancellationToken = default);

        /// <summary>Sets a new password without knowing the old one. Administrators only.</summary>
        Task<bool> ResetPasswordAsync(
            int companyId, int appUserId, string newPassword, int actingRoleLevel,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<RoleView>> GetRolesAsync(CancellationToken cancellationToken = default);
    }
}
