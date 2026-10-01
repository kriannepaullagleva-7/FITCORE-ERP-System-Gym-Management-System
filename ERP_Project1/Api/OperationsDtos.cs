namespace ERP_Project1.Api
{
    // ---------------------------------------------------------------------- purchasing

    public static class PurchaseStatuses
    {
        public const string Draft = "Draft";
        public const string Ordered = "Ordered";
        public const string PartiallyReceived = "Partially Received";
        public const string Received = "Received";
        public const string Cancelled = "Cancelled";

        public static readonly string[] All =
            { Draft, Ordered, PartiallyReceived, Received, Cancelled };
    }

    public class PurchaseItemDto
    {
        public int PurchaseItemId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal QuantityReceived { get; set; }
        public decimal OutstandingQuantity { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class PurchaseDto
    {
        public int PurchaseId { get; set; }
        public string PurchaseNo { get; set; } = "";
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public DateTime OrderDate { get; set; }
        public DateTime? ExpectedDate { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public string Status { get; set; } = "";
        public string SupplierReference { get; set; } = "";
        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Balance { get; set; }
        public string PaymentStatus { get; set; } = "";
        public string Notes { get; set; } = "";
        public string ProcessedBy { get; set; } = "";
        public int ItemCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<PurchaseItemDto> Items { get; set; } = new();
    }

    public class PurchaseLineDto
    {
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
    }

    public class CreatePurchaseDto
    {
        public int SupplierId { get; set; }
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public DateTime? ExpectedDate { get; set; }
        public string SupplierReference { get; set; } = "";
        public decimal Discount { get; set; }
        public decimal Tax { get; set; }
        public string Notes { get; set; } = "";
        public List<PurchaseLineDto> Lines { get; set; } = new();
    }

    public class UpdatePurchaseDto : CreatePurchaseDto { }

    public class ReceivePurchaseDto
    {
        /// <summary>Null receives everything outstanding, which is what a full delivery means.</summary>
        public Dictionary<int, decimal>? ReceivedByItemId { get; set; }
    }

    public class PurchaseSummaryDto
    {
        public int Count { get; set; }
        public int Draft { get; set; }
        public int Ordered { get; set; }
        public int AwaitingDelivery { get; set; }
        public decimal TotalOrdered { get; set; }
        public decimal TotalReceived { get; set; }
        public decimal TotalOutstanding { get; set; }
        public decimal PayableBalance { get; set; }
    }

    public class SupplierPaymentDto
    {
        public int SupplierPaymentId { get; set; }
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public int? PurchaseId { get; set; }
        public string PurchaseNo { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string Method { get; set; } = "";
        public string ReferenceNo { get; set; } = "";
        public string Notes { get; set; } = "";
        public string ProcessedBy { get; set; } = "";
    }

    public class PaySupplierDto
    {
        public int SupplierId { get; set; }
        public int? PurchaseId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
        public string Method { get; set; } = "Cash";
        public string ReferenceNo { get; set; } = "";
        public string Notes { get; set; } = "";
        public int? BankAccountId { get; set; }
    }

    // ---------------------------------------------------------------------- returns

    public class ReturnableLineDto
    {
        public int SaleItemId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public int QuantitySold { get; set; }
        public int QuantityReturned { get; set; }
        public int QuantityReturnable { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal UnitCost { get; set; }
    }

    public class SaleReturnItemDto
    {
        public int SaleReturnItemId { get; set; }
        public int SaleItemId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal UnitCost { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class SaleReturnDto
    {
        public int SaleReturnId { get; set; }
        public string ReturnNo { get; set; } = "";
        public int SaleId { get; set; }
        public int MemberId { get; set; }
        public string MemberName { get; set; } = "";
        public DateTime ReturnDate { get; set; }
        public string Reason { get; set; } = "";
        public decimal Subtotal { get; set; }
        public decimal RefundAmount { get; set; }
        public string RefundMethod { get; set; } = "";
        public bool RestockedToInventory { get; set; }
        public string Status { get; set; } = "";
        public string Notes { get; set; } = "";
        public string ProcessedBy { get; set; } = "";
        public int ItemCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<SaleReturnItemDto> Items { get; set; } = new();
    }

    public class ReturnLineDto
    {
        public int SaleItemId { get; set; }
        public int Quantity { get; set; }
    }

    public class CreateSaleReturnDto
    {
        public int SaleId { get; set; }
        public List<ReturnLineDto> Lines { get; set; } = new();
        public string Reason { get; set; } = "Other";
        public decimal? RefundAmount { get; set; }
        public string RefundMethod { get; set; } = "Cash";
        public bool RestockToInventory { get; set; } = true;
        public string Notes { get; set; } = "";
    }

    // ---------------------------------------------------------------------- leave

    public static class LeaveStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";

        public static readonly string[] All = { Pending, Approved, Rejected, Cancelled };
    }

    public class LeaveRequestDto
    {
        public int LeaveRequestId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string LeaveType { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal Days { get; set; }
        public bool IsPaid { get; set; }
        public string Reason { get; set; } = "";
        public string Status { get; set; } = "";
        public string RequestedBy { get; set; } = "";
        public DateTime? DecidedAt { get; set; }
        public string DecidedBy { get; set; } = "";
        public string DecisionNotes { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class LeaveBalanceDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public int Year { get; set; }
        public decimal PaidDaysTaken { get; set; }
        public decimal UnpaidDaysTaken { get; set; }
        public decimal PendingDays { get; set; }
        public decimal TotalDaysTaken { get; set; }
        public List<CategorySliceDto> ByType { get; set; } = new();
    }

    public class CreateLeaveRequestDto
    {
        public int EmployeeId { get; set; }
        public string LeaveType { get; set; } = "Vacation";
        public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
        public DateTime EndDate { get; set; } = DateTime.UtcNow.Date;
        public bool? IsPaid { get; set; }
        public string Reason { get; set; } = "";
    }

    public class UpdateLeaveRequestDto
    {
        public string LeaveType { get; set; } = "Vacation";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsPaid { get; set; } = true;
        public string Reason { get; set; } = "";
    }

    public class DecideLeaveRequestDto
    {
        public bool Approve { get; set; }
        public string Notes { get; set; } = "";
    }

    // ---------------------------------------------------------------------- member notes

    public class MemberNoteDto
    {
        public int MemberNoteId { get; set; }
        public int MemberId { get; set; }
        public string Category { get; set; } = "";
        public string Note { get; set; } = "";
        public bool IsPinned { get; set; }
        public string Author { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsMine { get; set; }
    }

    public class CreateMemberNoteDto
    {
        public string Category { get; set; } = "General";
        public string Note { get; set; } = "";
        public bool IsPinned { get; set; }
    }

    public class SuspendMemberDto
    {
        public string Reason { get; set; } = "";
        public DateTime? Until { get; set; }
    }

    // ---------------------------------------------------------------------- settings

    public class SettingDto
    {
        public string Key { get; set; } = "";
        public string Category { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Value { get; set; } = "";
        public string DefaultValue { get; set; } = "";

        /// <summary>Text, Number or Boolean.</summary>
        public string Kind { get; set; } = "Text";

        public bool IsOverridden { get; set; }
        public string UpdatedBy { get; set; } = "";
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpdateSettingsDto
    {
        public Dictionary<string, string> Values { get; set; } = new();
    }

    // ------------------------------------------------------------------ data integrity

    /// <summary>One thing that was checked in the tenant database, and what was found.</summary>
    public class IntegrityCheckDto
    {
        public string Key { get; set; } = "";
        public string Area { get; set; } = "";
        public string Check { get; set; } = "";
        public int IssueCount { get; set; }
        public bool Passed { get; set; }
        public string Severity { get; set; } = "";
        public string Detail { get; set; } = "";
        public List<string> Examples { get; set; } = new();
    }

    /// <summary>
    /// The whole consistency sweep. Read-only: the server offers no repair beside it, because
    /// the two findings that have a supported recovery are fixed by the Finance catch-up sweep.
    /// </summary>
    public class IntegrityReportDto
    {
        public DateTime CheckedAtUtc { get; set; }
        public List<IntegrityCheckDto> Checks { get; set; } = new();
        public int ChecksRun { get; set; }
        public int ChecksPassed { get; set; }
        public int IssuesFound { get; set; }
        public int CriticalCount { get; set; }
        public bool IsClean { get; set; }
    }
}
