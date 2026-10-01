namespace ERP_domain.entities
{
    /// <summary>
    /// The settings a tenant administers for itself, and their defaults.
    ///
    /// Keys are stable strings because they are stored as rows; the defaults here are what a
    /// tenant gets before anybody changes anything, so a fresh database needs no seeding for
    /// the application to behave sensibly.
    /// </summary>
    public static class TenantSettingKeys
    {
        // Business identity - what appears on a receipt or a payslip.
        public const string BusinessName        = "business.name";
        public const string BusinessAddress     = "business.address";
        public const string BusinessPhone       = "business.phone";
        public const string BusinessEmail       = "business.email";
        public const string BusinessTaxId       = "business.taxId";

        // Operational defaults.
        public const string Currency            = "business.currency";
        public const string CurrencySymbol      = "business.currencySymbol";
        public const string StandardShiftHours  = "operations.standardShiftHours";
        public const string OvertimeMultiplier  = "operations.overtimeMultiplier";
        public const string LowStockThreshold   = "operations.lowStockThreshold";
        public const string MembershipGraceDays = "operations.membershipGraceDays";
        public const string ExpiringSoonDays    = "operations.expiringSoonDays";

        // Security.
        public const string PasswordMinimumLength = "security.passwordMinimumLength";
        public const string SessionTimeoutMinutes = "security.sessionTimeoutMinutes";
        public const string RequirePasswordChangeDays = "security.requirePasswordChangeDays";

        // Finance.
        public const string FiscalYearStartMonth = "finance.fiscalYearStartMonth";
        public const string AutoPostToLedger     = "finance.autoPostToLedger";
        public const string DefaultTaxRate       = "finance.defaultTaxRate";

        /// <summary>
        /// Every setting with its category, its default and how to read it. The Settings screen
        /// is built from this rather than from a hand-written list of controls, so adding a
        /// setting is one entry here.
        /// </summary>
        public static readonly IReadOnlyList<TenantSettingDefinition> Catalogue = new[]
        {
            new TenantSettingDefinition(BusinessName, "Business", "Business name",
                "", TenantSettingKind.Text, "Appears on receipts, payslips and reports."),
            new TenantSettingDefinition(BusinessAddress, "Business", "Address",
                "", TenantSettingKind.Text, "Printed under the business name on a receipt."),
            new TenantSettingDefinition(BusinessPhone, "Business", "Contact number",
                "", TenantSettingKind.Text, ""),
            new TenantSettingDefinition(BusinessEmail, "Business", "Contact email",
                "", TenantSettingKind.Text, ""),
            new TenantSettingDefinition(BusinessTaxId, "Business", "Tax identification number",
                "", TenantSettingKind.Text, "TIN, printed on official receipts."),

            new TenantSettingDefinition(Currency, "Business", "Currency code",
                "PHP", TenantSettingKind.Text, "Three-letter ISO code."),
            new TenantSettingDefinition(CurrencySymbol, "Business", "Currency symbol",
                "₱", TenantSettingKind.Text, "Shown before an amount."),

            new TenantSettingDefinition(StandardShiftHours, "Operations", "Standard shift (hours)",
                "8", TenantSettingKind.Number, "Hours beyond this are recorded as overtime."),
            new TenantSettingDefinition(OvertimeMultiplier, "Operations", "Overtime multiplier",
                "1.25", TenantSettingKind.Number, "Applied to the hourly rate for overtime hours."),
            new TenantSettingDefinition(LowStockThreshold, "Operations", "Default reorder level",
                "10", TenantSettingKind.Number, "Used for a new product until one is set for it."),
            new TenantSettingDefinition(MembershipGraceDays, "Operations", "Membership grace (days)",
                "3", TenantSettingKind.Number, "Days a lapsed membership still admits a member."),
            new TenantSettingDefinition(ExpiringSoonDays, "Operations", "Expiring soon (days)",
                "7", TenantSettingKind.Number, "How far ahead the dashboard warns of expiries."),

            new TenantSettingDefinition(PasswordMinimumLength, "Security", "Minimum password length",
                "8", TenantSettingKind.Number, "Enforced when a password is set or changed."),
            new TenantSettingDefinition(SessionTimeoutMinutes, "Security", "Session timeout (minutes)",
                "480", TenantSettingKind.Number, "How long a sign-in lasts before it must be renewed."),
            new TenantSettingDefinition(RequirePasswordChangeDays, "Security", "Password expiry (days)",
                "0", TenantSettingKind.Number, "Zero means passwords do not expire."),

            new TenantSettingDefinition(FiscalYearStartMonth, "Finance", "Fiscal year starts in month",
                "1", TenantSettingKind.Number, "1 is January. Decides which year a period belongs to."),
            new TenantSettingDefinition(AutoPostToLedger, "Finance", "Post to the ledger automatically",
                "true", TenantSettingKind.Boolean,
                "When on, sales, payments, purchases and pay runs raise their own journal entries."),
            new TenantSettingDefinition(DefaultTaxRate, "Finance", "Default tax rate (%)",
                "0", TenantSettingKind.Number, "Applied to a new purchase unless overridden.")
        };

        public static TenantSettingDefinition? Find(string? key) =>
            Catalogue.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

        public static bool IsKnown(string? key) => Find(key) is not null;

        public static string DefaultFor(string key) => Find(key)?.DefaultValue ?? "";
    }

    public enum TenantSettingKind { Text, Number, Boolean }

    public sealed record TenantSettingDefinition(
        string Key,
        string Category,
        string DisplayName,
        string DefaultValue,
        TenantSettingKind Kind,
        string Description);

    /// <summary>
    /// One setting a tenant has actually changed.
    ///
    /// Only departures from the default are stored, for the same reason only departures from a
    /// role are stored in <see cref="AppUserPermission"/>: changing a default then reaches
    /// every tenant that never overrode it, and a tenant that made a deliberate choice keeps
    /// it. A missing row is not a missing setting, it is the default.
    /// </summary>
    public class TenantSetting : IAuditable
    {
        public int TenantSettingId { get; set; }

        /// <summary>A key from <see cref="TenantSettingKeys"/>. Unique within the tenant.</summary>
        public string SettingKey { get; set; } = "";

        public string Value { get; set; } = "";

        public int? UpdatedByUserId { get; set; }
        public string UpdatedBy { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
