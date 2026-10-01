using System;

namespace ERP_domain.entities
{
    public class StockMovement : IBranchScoped
    {
        public int StockMovementId { get; set; }

        /// <summary>The branch that moved the stock. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }
        public int ProductId { get; set; }

        // In, Out, Adjustment, Sale
        public string MovementType { get; set; } = "In";

        // Always stored as a positive magnitude; MovementType carries the direction.
        public decimal Quantity { get; set; }

        // Quantity on hand immediately before this movement was applied. Kept so an
        // adjustment shows what it changed rather than only where it landed.
        public decimal BalanceBefore { get; set; }

        // Running quantity on hand recorded right after this movement was applied.
        public decimal BalanceAfter { get; set; }

        public string Reference { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;

        /// <summary>
        /// What one unit was worth as it moved: the purchase cost on a receipt, the weighted
        /// average cost on an issue or an adjustment.
        ///
        /// Recorded on the movement so the value of a change in stock is answerable from the
        /// ledger of movements alone. Without it a stock-out tells you the quantity that left
        /// but not what leaving it cost, and inventory valuation has to be reconstructed by
        /// replaying every receipt.
        /// </summary>
        public decimal UnitCost { get; set; }

        /// <summary>Quantity times unit cost - the value this movement added or removed.</summary>
        public decimal TotalCost { get; set; }

        /// <summary>The purchase this receipt came from, when it came from one.</summary>
        public int? PurchaseId { get; set; }

        // Required for a stock-in movement: who supplied the goods. Never set for Out,
        // Adjustment or Sale movements - a supplier answers "who supplied the product?", not
        // "who moved it".
        public int? SupplierId { get; set; }

        // Optional: the member of staff who recorded the movement.
        public int? RecordedByEmployeeId { get; set; }

        // The signed-in user who performed this movement, taken from the token rather than the
        // client. Answers "who received/recorded the transaction?", distinct from the supplier.
        public int? PerformedByUserId { get; set; }
        public string PerformedBy { get; set; } = string.Empty;

        public DateTime MovementDate { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Foreign keys
        public Product? Product { get; set; }
        public Supplier? Supplier { get; set; }
        public Employee? RecordedByEmployee { get; set; }
    }
}
