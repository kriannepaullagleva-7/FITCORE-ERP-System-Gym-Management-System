using System;
using System.Collections.Generic;

namespace ERP_domain.entities
{
    public class Sale : IAuditable, IBranchScoped
    {
        public int SaleId { get; set; }

        /// <summary>The branch whose till rang this sale. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }

        /// <summary>
        /// Null for a walk-in sale - a normal sale is not required to name a member or a
        /// customer at all. See <see cref="WalkInName"/>.
        /// </summary>
        public int? MemberId { get; set; }
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

        // Optional: the member of staff who rang the sale up, as an Employee record. Only a
        // tier with the Employees module has these, so it stays optional.
        public int? CashierEmployeeId { get; set; }

        /// <summary>
        /// The signed-in user who completed the sale, captured from the token rather than
        /// chosen on screen. This is the accountable one - <see cref="CashierEmployeeId"/> is
        /// a convenience for gyms that roster staff, and may be unset.
        /// </summary>
        public int? ProcessedByUserId { get; set; }

        /// <summary>Their display name, denormalised so a reprinted receipt still names them.</summary>
        public string ProcessedBy { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// A typed name for the till slip when there is no member - defaults to "Walk-In" when
        /// left blank. Used only when <see cref="MemberId"/> is null.
        /// </summary>
        public string? WalkInName { get; set; }

        /// <summary>Cash handed over by the customer. Set only when the sale was paid in cash.</summary>
        public decimal? AmountTendered { get; set; }

        /// <summary>
        /// <see cref="AmountTendered"/> minus <see cref="TotalAmount"/>, computed server-side.
        /// Null whenever <see cref="AmountTendered"/> is null.
        /// </summary>
        public decimal? ChangeGiven { get; set; }

        // Foreign keys
        public Member? Member { get; set; }
        public Employee? CashierEmployee { get; set; }

        // Navigation properties
        public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
