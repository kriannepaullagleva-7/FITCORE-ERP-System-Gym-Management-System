namespace ERP_domain.entities
{
    public static class BudgetStatuses
    {
        /// <summary>Being prepared. Not yet compared against actuals.</summary>
        public const string Draft = "Draft";

        /// <summary>Signed off. This is the one Budget vs Actual reports against.</summary>
        public const string Approved = "Approved";

        /// <summary>Superseded by a later budget for the same year.</summary>
        public const string Archived = "Archived";

        public static readonly IReadOnlyList<string> All = new[] { Draft, Approved, Archived };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A year's plan, by account and month.
    ///
    /// Budgets are deliberately not postings: they express intent rather than a transaction,
    /// so they live beside the ledger rather than in it. Budget vs Actual reads a budget's
    /// lines and the general ledger's totals for the same account and month and shows the
    /// difference - the ledger is never adjusted to match the plan.
    /// </summary>
    public class Budget : IAuditable
    {
        public int BudgetId { get; set; }

        public string Name { get; set; } = "";

        public int Year { get; set; }

        /// <summary>One of <see cref="BudgetStatuses"/>.</summary>
        public string Status { get; set; } = BudgetStatuses.Draft;

        public string Notes { get; set; } = "";

        public int? ApprovedByUserId { get; set; }
        public string ApprovedBy { get; set; } = "";
        public DateTime? ApprovedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<BudgetLine> Lines { get; set; } = new List<BudgetLine>();
    }

    /// <summary>
    /// One account's budgeted amount for one month.
    ///
    /// Held per month rather than per year so a seasonal gym can budget January's promotions
    /// differently from August's, and so Budget vs Actual can be read for a month without
    /// dividing an annual figure by twelve and pretending that means something.
    /// </summary>
    public class BudgetLine
    {
        public int BudgetLineId { get; set; }

        public int BudgetId { get; set; }

        public int AccountId { get; set; }

        /// <summary>1-12.</summary>
        public int Month { get; set; }

        public decimal Amount { get; set; }

        public string Notes { get; set; } = "";

        public Budget Budget { get; set; } = null!;
        public Account Account { get; set; } = null!;
    }
}
