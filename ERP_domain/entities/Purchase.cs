namespace ERP_domain.entities
{
    public static class PurchaseStatuses
    {
        /// <summary>Being prepared. Nothing has been ordered or received.</summary>
        public const string Draft = "Draft";

        /// <summary>Placed with the supplier. Stock has not arrived yet.</summary>
        public const string Ordered = "Ordered";

        /// <summary>Some lines received, others outstanding.</summary>
        public const string PartiallyReceived = "Partially Received";

        /// <summary>Everything received. Stock is in and the payable is raised.</summary>
        public const string Received = "Received";

        public const string Cancelled = "Cancelled";

        public static readonly IReadOnlyList<string> All =
            new[] { Draft, Ordered, PartiallyReceived, Received, Cancelled };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether stock has arrived, and therefore whether anything is owed for it.</summary>
        public static bool HasReceipts(string? status) =>
            string.Equals(status, PartiallyReceived, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, Received, StringComparison.OrdinalIgnoreCase);
    }

    public static class SettlementStatuses
    {
        public const string Unpaid = "Unpaid";
        public const string PartiallyPaid = "Partially Paid";
        public const string Paid = "Paid";

        public static readonly IReadOnlyList<string> All = new[] { Unpaid, PartiallyPaid, Paid };

        /// <summary>Which of the three a total and an amount paid amount to.</summary>
        public static string For(decimal total, decimal paid) =>
            paid <= 0 ? Unpaid
            : paid >= total ? Paid
            : PartiallyPaid;
    }

    /// <summary>
    /// Goods bought from a supplier.
    ///
    /// This is the missing half of inventory. Stock-in on its own records that quantity
    /// arrived; a purchase records what was agreed, what it cost and who is owed for it - which
    /// is what lets inventory be carried as an asset at what was actually paid, and what turns
    /// a delivery into an account payable rather than an unexplained increase in stock.
    ///
    /// Receiving a purchase is the event that matters: it writes the stock movements, updates
    /// the weighted average cost, and posts
    /// <c>Dr Inventory / Cr Accounts Payable</c>. Nothing is expensed at that point - goods
    /// bought for resale are an asset until they are sold, and only then become cost of goods
    /// sold. Treating a delivery as an operating expense is the classic way to make a month
    /// with a big order look like a loss.
    /// </summary>
    public class Purchase : IAuditable, IBranchScoped
    {
        public int PurchaseId { get; set; }

        /// <summary>The branch that ordered and received the goods. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }

        /// <summary>Running number, unique within the tenant. PO-000001.</summary>
        public string PurchaseNo { get; set; } = "";

        public int SupplierId { get; set; }

        public DateTime OrderDate { get; set; } = DateTime.UtcNow;

        public DateTime? ExpectedDate { get; set; }

        /// <summary>Set when the last outstanding line is received.</summary>
        public DateTime? ReceivedDate { get; set; }

        /// <summary>One of <see cref="PurchaseStatuses"/>.</summary>
        public string Status { get; set; } = PurchaseStatuses.Draft;

        /// <summary>The supplier's own document number, for matching an invoice.</summary>
        public string SupplierReference { get; set; } = "";

        /// <summary>Sum of the line costs, before discount and tax.</summary>
        public decimal Subtotal { get; set; }

        /// <summary>Absolute amount off, never a percentage.</summary>
        public decimal Discount { get; set; }

        public decimal Tax { get; set; }

        /// <summary>Subtotal - Discount + Tax. What is owed.</summary>
        public decimal Total { get; set; }

        /// <summary>
        /// Settled so far, maintained as supplier payments are recorded. Stored rather than
        /// summed on read so the payables list is one query.
        /// </summary>
        public decimal AmountPaid { get; set; }

        /// <summary>One of <see cref="SettlementStatuses"/>.</summary>
        public string PaymentStatus { get; set; } = SettlementStatuses.Unpaid;

        public string Notes { get; set; } = "";

        public int? ProcessedByUserId { get; set; }
        public string ProcessedBy { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Supplier Supplier { get; set; } = null!;
        public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();

        /// <summary>What is still owed on this purchase.</summary>
        public decimal Balance => Total - AmountPaid;
    }

    /// <summary>One product on a purchase, at the cost agreed for it.</summary>
    public class PurchaseItem
    {
        public int PurchaseItemId { get; set; }

        public int PurchaseId { get; set; }
        public int ProductId { get; set; }

        public decimal Quantity { get; set; }

        /// <summary>What one unit costs on this order. Becomes part of the weighted average.</summary>
        public decimal UnitCost { get; set; }

        /// <summary>
        /// How much has actually arrived. A short delivery leaves this below
        /// <see cref="Quantity"/> and the purchase partially received, so the outstanding
        /// quantity stays visible rather than being quietly written off.
        /// </summary>
        public decimal QuantityReceived { get; set; }

        public Purchase Purchase { get; set; } = null!;
        public Product Product { get; set; } = null!;

        public decimal LineTotal => Quantity * UnitCost;
        public decimal OutstandingQuantity => Quantity - QuantityReceived;
    }

    /// <summary>
    /// Money paid to a supplier, settling one purchase or paid on account.
    ///
    /// The mirror of <see cref="Payment"/>, which is money coming in. Both reduce a balance and
    /// both move cash, so both post to the ledger the same way round about the same accounts -
    /// one debits cash and credits receivables, the other debits payables and credits cash.
    /// </summary>
    public class SupplierPayment : IAuditable
    {
        public int SupplierPaymentId { get; set; }

        public int SupplierId { get; set; }

        /// <summary>Null when paid on account rather than against a specific order.</summary>
        public int? PurchaseId { get; set; }

        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

        public string Method { get; set; } = "Cash";

        public string ReferenceNo { get; set; } = "";

        public string Notes { get; set; } = "";

        /// <summary>Which cash or bank account the money left, when one is named.</summary>
        public int? BankAccountId { get; set; }

        public int? ProcessedByUserId { get; set; }
        public string ProcessedBy { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Supplier Supplier { get; set; } = null!;
        public Purchase? Purchase { get; set; }
    }
}
