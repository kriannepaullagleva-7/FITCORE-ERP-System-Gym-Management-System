namespace ERP_domain.entities
{
    public static class BillingCycles
    {
        public const string Monthly = "Monthly";
        public const string Annual = "Annual";

        public static readonly IReadOnlyList<string> All = new[] { Monthly, Annual };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>How many months one billing term covers.</summary>
        public static int MonthsIn(string? cycle) =>
            string.Equals(cycle, Annual, StringComparison.OrdinalIgnoreCase) ? 12 : 1;
    }

    public static class TenantSubscriptionStatuses
    {
        /// <summary>Evaluating FitCore. Full access, no money yet.</summary>
        public const string Trial = "Trial";

        /// <summary>Paid and current.</summary>
        public const string Active = "Active";

        /// <summary>Past its end date and not renewed.</summary>
        public const string Expired = "Expired";

        /// <summary>Ended deliberately.</summary>
        public const string Cancelled = "Cancelled";

        /// <summary>Held for non-payment. The tenant is refused sign-in.</summary>
        public const string Suspended = "Suspended";

        public static readonly IReadOnlyList<string> All =
            new[] { Trial, Active, Expired, Cancelled, Suspended };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether a tenant on this status may use the product.</summary>
        public static bool PermitsAccess(string? value) =>
            string.Equals(value, Active, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, Trial, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A FitCore plan a tenant can be sold.
    ///
    /// Platform data, so it lives in the master database with the companies it applies to,
    /// never in a tenant's own. A gym must not be able to read - let alone change - what it is
    /// being charged or what tier it has been granted.
    ///
    /// The plan names a <see cref="EnterpriseTier"/>, but the tier on the company row remains
    /// authoritative for access. Subscribing a company is what sets that column; the two are
    /// kept deliberately separate so a lapsed subscription can be recorded without silently
    /// deleting a tenant's data access mid-month.
    /// </summary>
    public class SubscriptionPlan
    {
        public int SubscriptionPlanId { get; set; }

        /// <summary>Stable code, unique across the platform. MICRO, SMALL, MEDIUM.</summary>
        public string PlanCode { get; set; } = "";

        public string PlanName { get; set; } = "";

        /// <summary>The tier a company on this plan is licensed for.</summary>
        public EnterpriseTier Tier { get; set; } = EnterpriseTier.Micro;

        public decimal MonthlyPrice { get; set; }
        public decimal AnnualPrice { get; set; }

        /// <summary>Zero means no ceiling.</summary>
        public int MaxUsers { get; set; }

        public string Description { get; set; } = "";

        /// <summary>
        /// The one-sentence "what this buys you" a pricing page shows under the tier name -
        /// distinct from <see cref="Description"/>, which is free for a shorter tagline. Pure
        /// marketing copy: nothing here ever feeds authorization, which is why it can be edited
        /// freely without touching what a company on this plan can actually reach.
        /// </summary>
        public string Capability { get; set; } = "";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<CompanySubscription> Subscriptions { get; set; } = new List<CompanySubscription>();

        /// <summary>What one term costs on a given cycle.</summary>
        public decimal PriceFor(string? cycle) =>
            string.Equals(cycle, BillingCycles.Annual, StringComparison.OrdinalIgnoreCase)
                ? AnnualPrice
                : MonthlyPrice;
    }

    /// <summary>
    /// One company's subscription to a plan, for one term.
    ///
    /// Kept as a history rather than a single current row on the company, so platform revenue,
    /// tier changes and churn are all answerable from the records themselves rather than from
    /// whatever the company happens to look like today.
    /// </summary>
    public class CompanySubscription
    {
        public int CompanySubscriptionId { get; set; }

        public int CompanyId { get; set; }
        public int SubscriptionPlanId { get; set; }

        public DateTime StartDate { get; set; } = DateTime.UtcNow;

        /// <summary>Derived from the start date and the cycle when the term is created.</summary>
        public DateTime EndDate { get; set; }

        /// <summary>One of <see cref="BillingCycles"/>.</summary>
        public string BillingCycle { get; set; } = BillingCycles.Monthly;

        /// <summary>
        /// What was charged for this term, copied from the plan at the time. Held on the row so
        /// a later price change does not rewrite what a tenant was historically billed.
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>One of <see cref="TenantSubscriptionStatuses"/>.</summary>
        public string Status { get; set; } = TenantSubscriptionStatuses.Active;

        public bool AutoRenew { get; set; } = true;

        public string Notes { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Company Company { get; set; } = null!;
        public SubscriptionPlan Plan { get; set; } = null!;

        public bool IsCurrent(DateTime asOfUtc) =>
            TenantSubscriptionStatuses.PermitsAccess(Status) &&
            StartDate <= asOfUtc && EndDate >= asOfUtc;
    }

    /// <summary>
    /// Configuration that applies to the whole installation rather than to one tenant.
    ///
    /// Same shape and same reasoning as <see cref="TenantSetting"/>: only departures from the
    /// default are stored, so changing a default reaches every installation that never
    /// overrode it.
    /// </summary>
    public class PlatformSetting
    {
        public int PlatformSettingId { get; set; }

        public string SettingKey { get; set; } = "";

        public string Value { get; set; } = "";

        public int? UpdatedByUserId { get; set; }
        public string UpdatedBy { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    public static class PlatformSettingKeys
    {
        public const string PlatformName          = "platform.name";
        public const string SupportEmail          = "platform.supportEmail";
        public const string TrialLengthDays       = "platform.trialLengthDays";
        public const string AllowSelfRegistration = "platform.allowSelfRegistration";
        public const string MaintenanceMode       = "platform.maintenanceMode";
        public const string MaintenanceMessage    = "platform.maintenanceMessage";

        public static readonly IReadOnlyList<TenantSettingDefinition> Catalogue = new[]
        {
            new TenantSettingDefinition(PlatformName, "Platform", "Platform name",
                "FitCore ERP", TenantSettingKind.Text, "Shown on the sign-in window and in emails."),
            new TenantSettingDefinition(SupportEmail, "Platform", "Support email",
                "", TenantSettingKind.Text, "Where a tenant is told to write when something breaks."),
            new TenantSettingDefinition(TrialLengthDays, "Subscriptions", "Trial length (days)",
                "30", TenantSettingKind.Number, "How long a new tenant gets before a plan is required."),
            new TenantSettingDefinition(AllowSelfRegistration, "Subscriptions", "Allow self-registration",
                "false", TenantSettingKind.Boolean,
                "When off, only a Super Admin can register a new tenant."),
            new TenantSettingDefinition(MaintenanceMode, "Platform", "Maintenance mode",
                "false", TenantSettingKind.Boolean, "When on, tenants are refused sign-in."),
            new TenantSettingDefinition(MaintenanceMessage, "Platform", "Maintenance message",
                "FitCore is briefly unavailable for maintenance. Please try again shortly.",
                TenantSettingKind.Text, "Shown on the sign-in window while maintenance mode is on.")
        };

        public static TenantSettingDefinition? Find(string? key) =>
            Catalogue.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

        public static bool IsKnown(string? key) => Find(key) is not null;

        public static string DefaultFor(string key) => Find(key)?.DefaultValue ?? "";
    }
}
