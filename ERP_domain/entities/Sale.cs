using System;
using System.Collections.Generic;

namespace ERP_domain.entities
{
    public class Sale : IAuditable
    {
        public int SaleId { get; set; }
        public int MemberId { get; set; }
        public DateTime SaleDate { get; set; } = DateTime.UtcNow;

        // Sum of the line subtotals, before any discount. Stored rather than recomputed so a
        // historical receipt still shows what was charged if a product price changes later.
        public decimal Subtotal { get; set; }

        // Absolute amount taken off the subtotal, never a percentage.
        public decimal Discount { get; set; }

        // Subtotal - Discount. This is what the customer owes and what payments settle.
        public decimal TotalAmount { get; set; }

        // Completed or Cancelled. A cancelled sale keeps its rows so the history and the
        // reversing stock movements stay auditable.
        public string Status { get; set; } = "Completed";

        // Optional: the member of staff who rang the sale up.
        public int? CashierEmployeeId { get; set; }

        public string Notes { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        // Foreign keys
        public Member Member { get; set; } = null!;
        public Employee? CashierEmployee { get; set; }

        // Navigation properties
        public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
