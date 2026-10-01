using System.ComponentModel.DataAnnotations;
using ERP_domain.entities;

namespace ERP_api.DTOs
{
    /// <summary>
    /// A plain reason, for the several actions whose only input is why.
    ///
    /// Shared rather than duplicated per endpoint: a cancellation is a cancellation, and three
    /// identical single-property classes would only differ in their names.
    /// </summary>
    public class ReasonDto
    {
        [StringLength(300)]
        public string Reason { get; set; } = "";
    }

    // ---------------------------------------------------------------------- purchasing

    public class PurchaseLineDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Choose a product for every line.")]
        public int ProductId { get; set; }

        [Range(0.01, 9999999, ErrorMessage = "The quantity must be greater than zero.")]
        public decimal Quantity { get; set; }

        /// <summary>
        /// What one unit costs on this order. Zero falls back to the product's catalogue cost,
        /// so a quick order does not value the delivery at nothing and wreck the weighted
        /// average every later sale is costed against.
        /// </summary>
        [Range(0, 9999999)]
        public decimal UnitCost { get; set; }
    }

    public class CreatePurchaseDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Choose a supplier.")]
        public int SupplierId { get; set; }

        public DateTime OrderDate { get; set; }
        public DateTime? ExpectedDate { get; set; }

        /// <summary>The supplier's own document number, for matching their invoice later.</summary>
        [StringLength(60)]
        public string SupplierReference { get; set; } = "";

        [Range(0, 9999999)]
        public decimal Discount { get; set; }

        [Range(0, 9999999)]
        public decimal Tax { get; set; }

        [StringLength(300)]
        public string Notes { get; set; } = "";

        [MinLength(1, ErrorMessage = "A purchase needs at least one line.")]
        public List<PurchaseLineDto> Lines { get; set; } = new();
    }

    public class UpdatePurchaseDto : CreatePurchaseDto { }

    /// <summary>
    /// What is actually arriving. An empty list receives everything still outstanding, which is
    /// what a complete delivery means; naming quantities records a short delivery and leaves
    /// the rest outstanding rather than quietly writing it off.
    /// </summary>
    public class ReceivePurchaseDto
    {
        public Dictionary<int, decimal>? ReceivedByItemId { get; set; }
    }

    public class CancelPurchaseDto : ReasonDto { }

    public class PaySupplierDto
    {
        [Range(1, int.MaxValue)]
        public int SupplierId { get; set; }

        /// <summary>Null pays on account rather than against a specific order.</summary>
        public int? PurchaseId { get; set; }

        [Range(0.01, 9999999, ErrorMessage = "The amount must be greater than zero.")]
        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; }

        [StringLength(50)]
        public string Method { get; set; } = "Cash";

        [StringLength(60)]
        public string ReferenceNo { get; set; } = "";

        [StringLength(300)]
        public string Notes { get; set; } = "";

        public int? BankAccountId { get; set; }
    }

    // ---------------------------------------------------------------------- returns

    public class ReturnLineDto
    {
        [Range(1, int.MaxValue)]
        public int SaleItemId { get; set; }

        [Range(1, 100000, ErrorMessage = "The quantity must be at least one.")]
        public int Quantity { get; set; }
    }

    public class CreateSaleReturnDto
    {
        [Range(1, int.MaxValue)]
        public int SaleId { get; set; }

        [MinLength(1, ErrorMessage = "Choose at least one item to return.")]
        public List<ReturnLineDto> Lines { get; set; } = new();

        [StringLength(40)]
        public string Reason { get; set; } = ReturnReasons.Other;

        /// <summary>
        /// Null refunds the full value of what came back. Naming a smaller figure records a
        /// restocking deduction or a partial refund, which has to be recorded rather than
        /// inferred from the difference.
        /// </summary>
        public decimal? RefundAmount { get; set; }

        [StringLength(50)]
        public string RefundMethod { get; set; } = "Cash";

        /// <summary>
        /// Whether the goods go back on the shelf. Ignored for a damaged or expired return -
        /// restocking those would put stock in the system that cannot be sold.
        /// </summary>
        public bool RestockToInventory { get; set; } = true;

        [StringLength(300)]
        public string Notes { get; set; } = "";
    }

    public class CancelSaleReturnDto : ReasonDto { }

    // ---------------------------------------------------------------------- leave

    public class CreateLeaveRequestDto
    {
        [Range(1, int.MaxValue)]
        public int EmployeeId { get; set; }

        [Required]
        [StringLength(40)]
        public string LeaveType { get; set; } = LeaveTypes.Vacation;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>Null takes the default for the leave type.</summary>
        public bool? IsPaid { get; set; }

        [StringLength(300)]
        public string Reason { get; set; } = "";
    }

    public class UpdateLeaveRequestDto
    {
        [Required]
        [StringLength(40)]
        public string LeaveType { get; set; } = LeaveTypes.Vacation;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsPaid { get; set; } = true;

        [StringLength(300)]
        public string Reason { get; set; } = "";
    }

    public class DecideLeaveRequestDto
    {
        public bool Approve { get; set; }

        /// <summary>Required when refusing, so the employee is told something.</summary>
        [StringLength(300)]
        public string Notes { get; set; } = "";
    }

    // ---------------------------------------------------------------------- member notes

    public class CreateMemberNoteDto
    {
        [StringLength(30)]
        public string Category { get; set; } = MemberNoteCategories.General;

        [Required(ErrorMessage = "A note needs something in it.")]
        [StringLength(2000)]
        public string Note { get; set; } = "";

        /// <summary>Pinned notes are shown first - what the front desk must see.</summary>
        public bool IsPinned { get; set; }
    }

    public class UpdateMemberNoteDto : CreateMemberNoteDto { }

    /// <summary>Pauses a membership without ending it.</summary>
    public class SuspendMemberDto
    {
        [Required(ErrorMessage = "Say why the membership is being suspended.")]
        [StringLength(300)]
        public string Reason { get; set; } = "";

        /// <summary>When they are expected back. Null for an open-ended suspension.</summary>
        public DateTime? Until { get; set; }
    }

    // ---------------------------------------------------------------------- settings

    public class UpdateSettingsDto
    {
        /// <summary>
        /// The settings to change, by key. Only the ones named are touched, and a value equal
        /// to the shipped default removes the override rather than storing the default twice.
        /// </summary>
        public Dictionary<string, string> Values { get; set; } = new();
    }
}
