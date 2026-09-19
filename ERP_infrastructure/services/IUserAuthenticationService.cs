namespace ERP_infrastructure.services
{
    /// <summary>
    /// Verifies credentials against the master database and produces the access facts the rest
    /// of the request depends on: which company the caller belongs to, and which modules they
    /// may use. Nothing here issues a token, which is the job of the API layer, so the
    /// infrastructure layer stays free of any particular authentication scheme.
    /// </summary>
    public interface IUserAuthenticationService
    {
        Task<LoginResult> AuthenticateAsync(
            string username, string password, CancellationToken cancellationToken = default);

        /// <summary>
        /// Re-reads a user's access from the database. Used by /api/auth/me so a client that
        /// reloads the page gets current permissions rather than whatever its token was minted
        /// with, and returns null when the account has since been deactivated or removed.
        /// </summary>
        Task<AuthenticatedUser?> GetAuthenticatedUserAsync(
            int appUserId, CancellationToken cancellationToken = default);

        Task<bool> ChangePasswordAsync(
            int appUserId, string currentPassword, string newPassword,
            CancellationToken cancellationToken = default);
    }
}
