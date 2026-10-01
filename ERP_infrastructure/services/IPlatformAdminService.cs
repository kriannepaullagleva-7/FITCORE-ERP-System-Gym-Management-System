using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    // ---------------------------------------------------------------------- views

    /// <summary>
    /// One tenant as the Super Admin sees it: who they are, what they are licensed for, what
    /// they are paying, and whether their database is reachable.
    ///
    /// Deliberately carries no connection string and no password. The Super Admin administers
    /// tenants; they do not get handed the keys to every tenant's database in a JSON payload.
    /// </summary>
    public class TenantView
    {
        public int CompanyId { get; set; }
        public string CompanyCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public bool IsActive { get; set; }
        public string EnterpriseTier { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        public int UserCount { get; set; }
        public int ActiveUserCount { get; set; }
        public DateTime? LastSignIn { get; set; }

        /// <summary>
        /// The company's own Admin/Owner - the earliest-created Admin-role account on it, if
        /// any. Blank for a tenant that was registered but never given a first account.
        /// </summary>
        public string AdminFullName { get; set; } = "";
        public string AdminEmail { get; set; } = "";

        /// <summary>Where the tenant's data lives, by name only.</summary>
        public string ServerName { get; set; } = "";
        public string DatabaseName { get; set; } = "";
        public bool HasDatabaseRegistration { get; set; }

        public string SubscriptionPlan { get; set; } = "";
        public string SubscriptionStatus { get; set; } = "";
        public DateTime? SubscriptionEnds { get; set; }
        public decimal SubscriptionAmount { get; set; }

        /// <summary>Negative once the term has lapsed, which is what makes it worth showing.</summary>
        public int? DaysUntilRenewal { get; set; }
    }

    /// <summary>One module a plan's tier includes, named the same way the catalogue names it.</summary>
    public class PlanModuleView
    {
        public string ModuleKey { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Group { get; set; } = "";
    }

    public class SubscriptionPlanView
    {
        public int SubscriptionPlanId { get; set; }
        public string PlanCode { get; set; } = "";
        public string PlanName { get; set; } = "";
        public string Tier { get; set; } = "";
        public decimal MonthlyPrice { get; set; }
        public decimal AnnualPrice { get; set; }
        public int MaxUsers { get; set; }
        public string Description { get; set; } = "";

        /// <summary>The one-line "what this buys you" a pricing card shows under the tier name.</summary>
        public string Capability { get; set; } = "";

        public bool IsActive { get; set; }

        /// <summary>How many tenants are currently on it, so a plan is not deleted blind.</summary>
        public int TenantCount { get; set; }

        /// <summary>
        /// Computed live from <see cref="Tier"/> against <c>ErpModules.All</c> - never stored,
        /// so this can never drift from what <c>IsSubmoduleAllowed</c> actually grants. The one
        /// and only source of which modules a tier includes is the module catalogue itself.
        /// </summary>
        public List<PlanModuleView> Modules { get; set; } = new();

        public int ModuleCount => Modules.Count;

        /// <summary>MonthlyPrice × 12 − AnnualPrice. Computed, not stored, so it can never
        /// disagree with the two prices it is derived from.</summary>
        public decimal AnnualSavings { get; set; }

        /// <summary>AnnualSavings as a percentage of what twelve months would cost monthly.</summary>
        public decimal AnnualSavingsPercent { get; set; }
    }

    public class CompanySubscriptionView
    {
        public int CompanySubscriptionId { get; set; }
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = "";
        public int SubscriptionPlanId { get; set; }
        public string PlanName { get; set; } = "";
        public string Tier { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string BillingCycle { get; set; } = "";
        public decimal Amount { get; set; }
        public string Status { get; set; } = "";
        public bool AutoRenew { get; set; }
        public string Notes { get; set; } = "";
        public int DaysRemaining { get; set; }
    }

    /// <summary>One account anywhere on the platform, with the tenant it belongs to.</summary>
    public class PlatformUserView
    {
        public int AppUserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = "";
        public string CompanyCode { get; set; } = "";
        public string RoleKey { get; set; } = "";
        public string RoleDisplayName { get; set; } = "";
        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Whether a tenant's database can actually be reached, and where it stands.</summary>
    public class TenantHealthView
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = "";
        public string DatabaseName { get; set; } = "";
        public bool CanConnect { get; set; }
        public bool IsUpToDate { get; set; }
        public int PendingMigrations { get; set; }
        public string Problem { get; set; } = "";
        public DateTime CheckedAtUtc { get; set; }
    }

    // ---------------------------------------------------------------------- service

    /// <summary>
    /// Platform administration: the Super Admin's own workspace.
    ///
    /// Everything here reads and writes the master database - companies, subscriptions,
    /// accounts, platform settings and the platform audit trail. It never reads a tenant's
    /// business data: when the Super Admin wants to look inside Tenant C, the request goes
    /// through the ordinary tenant-resolution path and the ordinary business endpoints, exactly
    /// as a tenant user's would. Copying a tenant's operational records into the master
    /// database so the platform can see them is precisely the mistake this separation exists
    /// to prevent.
    /// </summary>
    public interface IPlatformAdminService
    {
        // ---------------------------------------------------------------- tenants

        Task<List<TenantView>> GetTenantsAsync();

        Task<TenantView?> GetTenantAsync(int companyId);

        Task<TenantView> CreateTenantAsync(
            string companyCode, string companyName, EnterpriseTier tier,
            string serverName, string databaseName, string credentialKey);

        Task<TenantView?> UpdateTenantAsync(
            int companyId, string companyName, EnterpriseTier tier, bool isActive);

        /// <summary>
        /// Changes what a tenant is licensed for. This is the one switch that decides which of
        /// the nine modules their users can reach, so it is a deliberate act with its own audit
        /// entry rather than a side effect of a billing change.
        /// </summary>
        Task<TenantView?> SetTierAsync(int companyId, EnterpriseTier tier, string reason);

        Task<TenantView?> SetActiveAsync(int companyId, bool isActive, string reason);

        // ---------------------------------------------------------------- subscriptions

        Task<List<SubscriptionPlanView>> GetPlansAsync();

        Task<SubscriptionPlanView> CreatePlanAsync(
            string planCode, string planName, EnterpriseTier tier,
            decimal monthlyPrice, decimal annualPrice, int maxUsers, string description,
            string capability);

        Task<SubscriptionPlanView?> UpdatePlanAsync(
            int planId, string planName, decimal monthlyPrice, decimal annualPrice,
            int maxUsers, string description, string capability, bool isActive);

        Task<bool> DeletePlanAsync(int planId);

        /// <summary>Every subscription term, newest first. Filterable to one tenant.</summary>
        Task<List<CompanySubscriptionView>> GetSubscriptionsAsync(
            int? companyId = null, string? status = null);

        /// <summary>
        /// Puts a tenant on a plan for one term, and promotes their tier to match.
        ///
        /// Subscribing is what sets the company's tier, so billing and entitlement cannot
        /// silently disagree. A term that lapses does not revoke access by itself - that is a
        /// separate, deliberate act, so a payment dispute does not lock a paying customer out
        /// of their own data mid-argument.
        /// </summary>
        Task<CompanySubscriptionView> SubscribeAsync(
            int companyId, int planId, string billingCycle, DateTime startDate, bool autoRenew);

        Task<CompanySubscriptionView?> RenewSubscriptionAsync(int companySubscriptionId);

        Task<CompanySubscriptionView?> CancelSubscriptionAsync(int companySubscriptionId, string reason);

        /// <summary>Marks every term that has run out. Idempotent, and safe to call repeatedly.</summary>
        Task<int> ExpireLapsedSubscriptionsAsync();

        // ---------------------------------------------------------------- users

        Task<List<PlatformUserView>> GetPlatformUsersAsync(int? companyId = null, string? search = null);

        Task<PlatformUserView?> SetUserActiveAsync(int appUserId, bool isActive);

        /// <summary>
        /// Resets an account's password and forces a change on next sign-in. The Super Admin
        /// can do this for any tenant, which is what makes them the recovery path when a gym's
        /// only owner is locked out.
        /// </summary>
        Task<PlatformUserView?> ResetPasswordAsync(int appUserId, string newPassword);

        // ---------------------------------------------------------------- settings and health

        Task<List<TenantSettingView>> GetPlatformSettingsAsync();

        Task<List<TenantSettingView>> SetPlatformSettingsAsync(IDictionary<string, string> values);

        /// <summary>
        /// Probes each tenant database in turn. Slow by nature - it opens a connection per
        /// tenant - so it is a screen the Super Admin asks for rather than something the
        /// dashboard runs on every load.
        /// </summary>
        Task<List<TenantHealthView>> GetTenantHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>The master trail: sign-in, permission and company changes across the platform.</summary>
        Task<List<AuditEvent>> GetPlatformAuditAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null,
            string? action = null, int? companyId = null, int take = 300);
    }
}
