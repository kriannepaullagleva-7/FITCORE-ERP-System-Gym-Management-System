namespace ERP_domain.entities
{
    /// <summary>
    /// What a payment settles.
    ///
    /// Stored as a short string rather than an enum so the column stays readable in the
    /// database and a report can group on it without a lookup. The set is closed: services
    /// validate against <see cref="All"/> before writing, and the API rejects anything else.
    /// </summary>
    public static class PaymentCategories
    {
        /// <summary>Membership dues - a subscription being bought, renewed or settled.</summary>
        public const string Membership = "Membership";

        /// <summary>Goods sold over the counter.</summary>
        public const string Sales = "Sales";

        /// <summary>Money taken from a member that settles neither a subscription nor a sale.</summary>
        public const string General = "General";

        public static readonly IReadOnlyList<string> All = new[] { Membership, Sales, General };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The category a payment belongs to, inferred from what it is attached to. Used when
        /// classifying rows written before the column existed.
        /// </summary>
        public static string Infer(int? subscriptionId, int? saleId) =>
            saleId.HasValue ? Sales
            : subscriptionId.HasValue ? Membership
            : General;
    }
}
