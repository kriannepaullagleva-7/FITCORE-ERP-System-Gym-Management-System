namespace ERP_infrastructure.services
{
    /// <summary>
    /// The chart of accounts: what the tenant's accounts are, and what has been posted to them.
    ///
    /// Also owns the seeding of the standard chart. A tenant's books have to exist before the
    /// first sale can be posted, so <see cref="EnsureChartOfAccountsAsync"/> is idempotent and
    /// called on the way into any finance screen rather than as a one-off migration step that
    /// a newly provisioned database might miss.
    /// </summary>
    public interface IAccountService
    {
        Task<List<AccountView>> GetAccountsAsync(
            string? accountType = null, bool includeInactive = true, bool withBalances = true);

        Task<AccountView?> GetAccountAsync(int accountId);

        /// <summary>The account playing a named role in automatic posting, if it exists.</summary>
        Task<int?> ResolveSystemAccountIdAsync(string systemKey);

        Task<AccountView> CreateAccountAsync(
            string accountCode, string accountName, string accountType,
            string accountSubType, string description, int? parentAccountId);

        Task<AccountView?> UpdateAccountAsync(
            int accountId, string accountCode, string accountName, string accountType,
            string accountSubType, string description, int? parentAccountId, bool isActive);

        /// <summary>
        /// Removes an account. Refused for a system account, and for one that has ever been
        /// posted to - deleting that would silently change every balance it appeared in.
        /// </summary>
        Task<bool> DeleteAccountAsync(int accountId);

        /// <summary>
        /// Creates any standard account this tenant is missing, and returns how many it added.
        ///
        /// Matches on system key first and account code second, so a tenant that renamed
        /// "Cash on Hand" to "Till" keeps their name rather than getting a duplicate.
        /// </summary>
        Task<int> EnsureChartOfAccountsAsync();
    }
}
