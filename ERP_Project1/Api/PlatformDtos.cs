namespace ERP_Project1.Api
{
    // The Super Admin's platform contracts. Note what is absent: no connection strings and no
    // credentials. A platform administrator administers tenants; they are not handed the keys
    // to every tenant's database in a JSON payload, and the desktop could not use them if they
    // were - it holds no database client at all.

    public static class EnterpriseTiers
    {
        public const string Micro = "Micro";
        public const string Small = "Small";
        public const string Medium = "Medium";

        public static readonly string[] All = { Micro, Small, Medium };

        /// <summary>The numeric value the API binds a tier from.</summary>
        public static int ValueOf(string? tier) => (tier ?? "").Trim().ToLowerInvariant() switch
        {
            "small" => 2,
            "medium" => 3,
            _ => 1
        };
    }

    public static class SubscriptionStatuses
    {
        public const string Trial = "Trial";
        public const string Active = "Active";
        public const string Expired = "Expired";
        public const string Cancelled = "Cancelled";
        public const string Suspended = "Suspended";

        public static readonly string[] All = { Trial, Active, Expired, Cancelled, Suspended };
    }

    public static class BillingCycles
    {
        public const string Monthly = "Monthly";
        public const string Annual = "Annual";

        public static readonly string[] All = { Monthly, Annual };
    }

    public class TenantDto
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
        public string AdminFullName { get; set; } = "";
        public string AdminEmail { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string DatabaseName { get; set; } = "";
        public bool HasDatabaseRegistration { get; set; }
        public string SubscriptionPlan { get; set; } = "";
        public string SubscriptionStatus { get; set; } = "";
        public DateTime? SubscriptionEnds { get; set; }
        public decimal SubscriptionAmount { get; set; }
        public int? DaysUntilRenewal { get; set; }

        public string Status => IsActive ? "Active" : "Inactive";
    }

    public class CreateTenantDto
    {
        public string CompanyCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public int Tier { get; set; } = 1;
        public string ServerName { get; set; } = "";
        public string DatabaseName { get; set; } = "";
        public string CredentialKey { get; set; } = "";
    }

    public class UpdateTenantDto
    {
        public string CompanyName { get; set; } = "";
        public int Tier { get; set; } = 1;
        public bool IsActive { get; set; } = true;
    }

    public class SetTierDto
    {
        public int Tier { get; set; } = 1;
        public string Reason { get; set; } = "";
    }

    public class SetTenantStatusDto
    {
        public bool IsActive { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>One module a plan's tier includes. Never edited directly - see <see cref="SubscriptionPlanDto.Modules"/>.</summary>
    public class PlanModuleDto
    {
        public string ModuleKey { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Group { get; set; } = "";
    }

    public class SubscriptionPlanDto
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
        public int TenantCount { get; set; }

        /// <summary>
        /// Always computed server-side from the plan's tier against the module catalogue -
        /// never edited here, never hardcoded on this side of the wire either. This is what the
        /// "View Included Modules" action shows.
        /// </summary>
        public List<PlanModuleDto> Modules { get; set; } = new();

        public int ModuleCount { get; set; }

        /// <summary>MonthlyPrice × 12 − AnnualPrice, computed server-side.</summary>
        public decimal AnnualSavings { get; set; }
        public decimal AnnualSavingsPercent { get; set; }

        public string Status => IsActive ? "Active" : "Inactive";
    }

    public class CreateSubscriptionPlanDto
    {
        public string PlanCode { get; set; } = "";
        public string PlanName { get; set; } = "";
        public int Tier { get; set; } = 1;
        public decimal MonthlyPrice { get; set; }
        public decimal AnnualPrice { get; set; }
        public int MaxUsers { get; set; }
        public string Description { get; set; } = "";
        public string Capability { get; set; } = "";
    }

    public class UpdateSubscriptionPlanDto
    {
        public string PlanName { get; set; } = "";
        public decimal MonthlyPrice { get; set; }
        public decimal AnnualPrice { get; set; }
        public int MaxUsers { get; set; }
        public string Description { get; set; } = "";
        public string Capability { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }

    public class CompanySubscriptionDto
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

    public class SubscribeDto
    {
        public int CompanyId { get; set; }
        public int SubscriptionPlanId { get; set; }
        public string BillingCycle { get; set; } = "Monthly";
        public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
        public bool AutoRenew { get; set; } = true;
    }

    public class PlatformUserDto
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

        public string Status => IsActive ? "Active" : "Inactive";
    }

    public class TenantHealthDto
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = "";
        public string DatabaseName { get; set; } = "";
        public bool CanConnect { get; set; }
        public bool IsUpToDate { get; set; }
        public int PendingMigrations { get; set; }
        public string Problem { get; set; } = "";
        public DateTime CheckedAtUtc { get; set; }

        public string Status =>
            !CanConnect ? "Unreachable"
            : !IsUpToDate ? "Migrations pending"
            : "Healthy";
    }

    public class ProvisioningResultDto
    {
        public int CompanyId { get; set; }
        public bool Succeeded { get; set; }
        public List<string> MigrationsApplied { get; set; } = new();
        public string? Message { get; set; }
    }
}
