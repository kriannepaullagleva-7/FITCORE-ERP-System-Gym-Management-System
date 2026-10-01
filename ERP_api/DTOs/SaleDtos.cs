using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    public class CreateSaleItemDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "A valid product must be selected.")]
        public int ProductId { get; set; }

        [Range(1, 1000000, ErrorMessage = "Quantity must be at least one.")]
        public int Quantity { get; set; }
    }

    public class CreateSaleDto
    {
        /// <summary>
        /// Null for a walk-in sale - a sale names neither a member nor a customer record by
        /// default. See <see cref="WalkInName"/>.
        /// </summary>
        public int? MemberId { get; set; }

        /// <summary>A typed name for the till slip when there is no member. Defaults to "Walk-In".</summary>
        [StringLength(150)]
        public string? WalkInName { get; set; }

        [MinLength(1, ErrorMessage = "A sale must have at least one line.")]
        public List<CreateSaleItemDto> Items { get; set; } = new();

        [Range(0, 10000000, ErrorMessage = "Discount cannot be negative.")]
        public decimal Discount { get; set; }

        /// <summary>Optional: the member of staff who rang the sale up.</summary>
        public int? CashierEmployeeId { get; set; }

        [StringLength(300)]
        public string Notes { get; set; } = "";

        /// <summary>
        /// Settle the sale in full inside the same transaction that writes it. The till
        /// sets this rather than posting a separate payment, so a completed sale and its
        /// money cannot end up out of step.
        /// </summary>
        public bool SettleNow { get; set; }

        [RegularExpression("^(Cash|Card|Transfer|Check|GCash)$",
            ErrorMessage = "Method must be Cash, Card, Transfer, Check or GCash.")]
        public string PaymentMethod { get; set; } = "Cash";

        /// <summary>Cash handed over, when <see cref="PaymentMethod"/> is Cash.</summary>
        [Range(0, 100000000, ErrorMessage = "Amount tendered cannot be negative.")]
        public decimal? AmountTendered { get; set; }
    }

    public class CancelSaleDto
    {
        [StringLength(200)]
        public string Reason { get; set; } = "";
    }

    public class SaleItemDto
    {
        public int SaleItemId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class SaleDto
    {
        public int SaleId { get; set; }
        public int? MemberId { get; set; }
        public string? WalkInName { get; set; }
        public DateTime SaleDate { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "";
        public int? CashierEmployeeId { get; set; }

        /// <summary>The signed-in user who completed the sale, taken from the token.</summary>
        public int? ProcessedByUserId { get; set; }
        public string ProcessedBy { get; set; } = "";

        public string Notes { get; set; } = "";
        public List<SaleItemDto> Items { get; set; } = new();

        public decimal? AmountTendered { get; set; }
        public decimal? ChangeGiven { get; set; }
    }
}
