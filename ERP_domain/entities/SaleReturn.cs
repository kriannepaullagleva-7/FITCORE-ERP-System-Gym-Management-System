namespace ERP_domain.entities
{
    public static class ReturnReasons
    {
        public const string Damaged = "Damaged";
        public const string WrongItem = "Wrong item";
        public const string Expired = "Expired";
        public const string CustomerChangedMind = "Changed mind";
        public const string Other = "Other";

        public static readonly IReadOnlyList<string> All =
            new[] { Damaged, WrongItem, Expired, CustomerChangedMind, Other };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Goods coming back over the counter.
    ///
    /// A return is its own transaction rather than an edit to the sale it came from. The
    /// original sale stays exactly as it was rung up - which is what a receipt, a report and an
    /// audit all depend on - and the return records separately that some of it came back, what
    /// was refunded and when.
    ///
    /// Recording one restores stock, posts
    /// <c>Dr Sales Returns / Cr Cash</c> for the money and
    /// <c>Dr Inventory / Cr Cost of Goods Sold</c> for the goods, so revenue, stock and cost of
    /// sales all come back into agreement without any of them being edited in place.
    /// </summary>
    public class SaleReturn : IAuditable, IBranchScoped
    {
        public int SaleReturnId { get; set; }

        /// <summary>The branch that accepted the return. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }

        /// <summary>Running number, unique within the tenant. RET-000001.</summary>
        public string ReturnNo { get; set; } = "";

        public int SaleId { get; set; }

        /// <summary>
        /// Denormalised from the sale so the list does not have to join to show it. Null when
        /// the original sale was a walk-in.
        /// </summary>
        public int? MemberId { get; set; }

        public DateTime ReturnDate { get; set; } = DateTime.UtcNow;

        /// <summary>One of <see cref="ReturnReasons"/>.</summary>
        public string Reason { get; set; } = ReturnReasons.Other;

        /// <summary>Sum of the returned line values at the price they were sold for.</summary>
        public decimal Subtotal { get; set; }

        /// <summary>
        /// What was actually given back. Normally the subtotal, but a restocking deduction or
        /// a partial refund makes them differ, and the difference has to be recorded rather
        /// than inferred.
        /// </summary>
        public decimal RefundAmount { get; set; }

        public string RefundMethod { get; set; } = "Cash";

        /// <summary>
        /// Whether the returned goods went back on the shelf. Damaged stock comes back as a
        /// loss rather than as inventory, and the posting differs accordingly.
        /// </summary>
        public bool RestockedToInventory { get; set; } = true;

        /// <summary>Completed or Cancelled.</summary>
        public string Status { get; set; } = "Completed";

        public string Notes { get; set; } = "";

        public int? ProcessedByUserId { get; set; }
        public string ProcessedBy { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Sale Sale { get; set; } = null!;
        public Member? Member { get; set; }
        public ICollection<SaleReturnItem> Items { get; set; } = new List<SaleReturnItem>();
    }

    /// <summary>One product line coming back, at the price and cost it went out at.</summary>
    public class SaleReturnItem
    {
        public int SaleReturnItemId { get; set; }

        public int SaleReturnId { get; set; }

        /// <summary>The line it came from, so the same item cannot be returned twice.</summary>
        public int SaleItemId { get; set; }

        public int ProductId { get; set; }

        public int Quantity { get; set; }

        /// <summary>What it was sold for, copied from the sale line.</summary>
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// What it cost when it was sold, copied from the sale line so the cost of goods sold
        /// that is reversed is the one that was originally charged - not today's average cost,
        /// which has moved on since.
        /// </summary>
        public decimal UnitCost { get; set; }

        public SaleReturn Return { get; set; } = null!;
        public SaleItem SaleItem { get; set; } = null!;
        public Product Product { get; set; } = null!;

        public decimal LineTotal => Quantity * UnitPrice;
        public decimal LineCost => Quantity * UnitCost;
    }
}
