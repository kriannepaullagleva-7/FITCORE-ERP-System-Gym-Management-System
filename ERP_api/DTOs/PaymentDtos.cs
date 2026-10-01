using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    /// <summary>Subscription-scoped shorthand kept for existing callers.</summary>
    public class CreatePaymentDto
    {
        [Range(1, int.MaxValue)]
        public int SubscriptionId { get; set; }

        [Range(0.01, 10000000, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [StringLength(50)]
        public string Method { get; set; } = "Cash";
    }

    public class RecordPaymentDto
    {
        /// <summary>
        /// Null for a walk-in payment - one taken from somebody with no Member record. See
        /// <see cref="WalkInName"/>.
        /// </summary>
        public int? MemberId { get; set; }

        /// <summary>The payer's name, used only when <see cref="MemberId"/> is null.</summary>
        [StringLength(150)]
        public string? WalkInName { get; set; }

        /// <summary>Null for a payment that is not tied to a subscription.</summary>
        public int? SubscriptionId { get; set; }

        /// <summary>Null for a payment that is not settling a sale.</summary>
        public int? SaleId { get; set; }

        [Range(0.01, 10000000, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        /// <summary>Defaults to now when omitted.</summary>
        public DateTime? PaymentDate { get; set; }

        [Required]
        [RegularExpression("^(Cash|Card|Transfer|Check|GCash)$",
            ErrorMessage = "Method must be Cash, Card, Transfer, Check or GCash.")]
        public string Method { get; set; } = "Cash";

        [StringLength(60)]
        public string ReferenceNo { get; set; } = "";

        [Required]
        [RegularExpression("^(Completed|Pending|Failed|Refunded)$",
            ErrorMessage = "Status must be Completed, Pending, Failed or Refunded.")]
        public string Status { get; set; } = "Completed";

        [StringLength(300)]
        public string Notes { get; set; } = "";

        /// <summary>Cash handed over, when <see cref="Method"/> is Cash.</summary>
        [Range(0, 100000000, ErrorMessage = "Amount tendered cannot be negative.")]
        public decimal? AmountTendered { get; set; }
    }

    public class UpdatePaymentDto
    {
        [Range(0.01, 10000000, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        public DateTime? PaymentDate { get; set; }

        [Required]
        [RegularExpression("^(Cash|Card|Transfer|Check|GCash)$",
            ErrorMessage = "Method must be Cash, Card, Transfer, Check or GCash.")]
        public string Method { get; set; } = "Cash";

        [StringLength(60)]
        public string ReferenceNo { get; set; } = "";

        [Required]
        [RegularExpression("^(Completed|Pending|Failed|Refunded)$",
            ErrorMessage = "Status must be Completed, Pending, Failed or Refunded.")]
        public string Status { get; set; } = "Completed";

        [StringLength(300)]
        public string Notes { get; set; } = "";

        /// <summary>Cash handed over, when <see cref="Method"/> is Cash.</summary>
        [Range(0, 100000000, ErrorMessage = "Amount tendered cannot be negative.")]
        public decimal? AmountTendered { get; set; }
    }

    public class UpdatePaymentStatusDto
    {
        [Required]
        [RegularExpression("^(Completed|Pending|Failed|Refunded)$",
            ErrorMessage = "Status must be Completed, Pending, Failed or Refunded.")]
        public string Status { get; set; } = "Completed";
    }

    public class VoidPaymentDto
    {
        [StringLength(200)]
        public string Reason { get; set; } = "";
    }

    public class PaymentDto
    {
        public int PaymentId { get; set; }
        public int? MemberId { get; set; }
        public string? WalkInName { get; set; }
        public int? SubscriptionId { get; set; }
        public int? SaleId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string Method { get; set; } = "";
        public string ReferenceNo { get; set; } = "";
        public string Status { get; set; } = "";
        public string Notes { get; set; } = "";

        /// <summary>Membership, Sales or General - decided by the server.</summary>
        public string Category { get; set; } = "";

        /// <summary>The signed-in user who took the money. Named on the receipt.</summary>
        public int? ProcessedByUserId { get; set; }
        public string ProcessedBy { get; set; } = "";

        public decimal? AmountTendered { get; set; }
        public decimal? ChangeGiven { get; set; }
    }
}
