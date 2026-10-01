namespace ERP_domain.entities
{
    public static class PeriodStatuses
    {
        /// <summary>Postings are accepted.</summary>
        public const string Open = "Open";

        /// <summary>Closed off. Nothing new may be posted into it without reopening.</summary>
        public const string Closed = "Closed";

        public static readonly IReadOnlyList<string> All = new[] { Open, Closed };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One month of the books, and whether it still accepts postings.
    ///
    /// Closing a period is what stops last quarter's figures moving after they have been
    /// reported. A period that has no row is treated as open, so a tenant that never uses this
    /// feature is unaffected - it is an opt-in control, not a gate every posting has to pass
    /// a lookup for.
    /// </summary>
    public class FinancialPeriod : IAuditable
    {
        public int FinancialPeriodId { get; set; }

        public int Year { get; set; }

        /// <summary>1-12.</summary>
        public int Month { get; set; }

        /// <summary>First day of the month, inclusive.</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Last day of the month, inclusive.</summary>
        public DateTime EndDate { get; set; }

        /// <summary>One of <see cref="PeriodStatuses"/>.</summary>
        public string Status { get; set; } = PeriodStatuses.Open;

        public DateTime? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }
        public string ClosedBy { get; set; } = "";

        public string Notes { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public bool IsOpen =>
            string.Equals(Status, PeriodStatuses.Open, StringComparison.OrdinalIgnoreCase);

        /// <summary>"March 2026", for a screen or a report heading.</summary>
        public string DisplayName =>
            new DateTime(Year, Math.Clamp(Month, 1, 12), 1).ToString("MMMM yyyy");
    }
}
