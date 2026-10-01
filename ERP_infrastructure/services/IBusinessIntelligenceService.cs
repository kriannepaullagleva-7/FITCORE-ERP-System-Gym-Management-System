namespace ERP_infrastructure.services
{
    /// <summary>The analytics areas Business Intelligence offers, keyed as the API routes them.</summary>
    public static class AnalyticsAreas
    {
        public const string Overview = "overview";
        public const string Membership = "membership";
        public const string Sales = "sales";
        public const string Payments = "payments";
        public const string Inventory = "inventory";
        public const string Workforce = "workforce";
        public const string Finance = "finance";
        public const string Profitability = "profitability";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Overview, Membership, Sales, Payments, Inventory, Workforce, Finance, Profitability
        };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Business Intelligence: the KPIs and charts for one tenant, over a date range.
    ///
    /// Every figure here is computed from the tenant's own rows at the moment it is asked for.
    /// Nothing is cached, precomputed into a reporting table, or estimated - which is slower
    /// than a warehouse would be and is the right trade for a gym, where the data is small and
    /// "why does the dashboard disagree with the sales screen?" is the question that destroys
    /// trust in a report.
    ///
    /// Each area also returns the equivalent preceding window, so every card can show what
    /// changed rather than only where things stand.
    /// </summary>
    public interface IBusinessIntelligenceService
    {
        /// <summary>
        /// One analytics area. <paramref name="area"/> is a key from
        /// <see cref="AnalyticsAreas"/>; an unknown one is refused rather than silently
        /// answered with the overview.
        /// </summary>
        Task<AnalyticsView> GetAnalyticsAsync(string area, DateTime? fromUtc, DateTime? toUtc);

        /// <summary>
        /// Every KPI the product measures, across all nine modules, in one call.
        ///
        /// This is what the KPI screen shows. It is deliberately one round trip: nine separate
        /// requests against a remote database is most of a second of latency before anything
        /// is drawn.
        /// </summary>
        Task<AnalyticsView> GetKpiDashboardAsync(DateTime? fromUtc, DateTime? toUtc);
    }
}
