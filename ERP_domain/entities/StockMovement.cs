using System;

namespace ERP_domain.entities
{
    public class StockMovement
    {
        public int StockMovementId { get; set; }
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

        // Optional: the member of staff who recorded the movement.
        public int? RecordedByEmployeeId { get; set; }

        public DateTime MovementDate { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Foreign keys
        public Product? Product { get; set; }
        public Employee? RecordedByEmployee { get; set; }
    }
}
