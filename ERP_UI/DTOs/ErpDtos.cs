namespace ERP_UI.DTOs
{
    // These mirror the wire contracts served by ERP_api. They are deliberately plain: the
    // browser never talks to a database, only to the API over HTTPS.

    // ------------------------------------------------------------------ Products

    public class ProductDto
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal CostPrice { get; set; }
        public decimal UnitPrice { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Null until the product has been edited since it was created.</summary>
        public DateTime? UpdatedAt { get; set; }

        public decimal Margin => UnitPrice - CostPrice;
    }

    public class CreateProductDto
    {
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Category { get; set; } = "Other";
        public decimal CostPrice { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal OpeningStock { get; set; }
        public decimal ReorderLevel { get; set; }
    }

    public class UpdateProductDto
    {
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Category { get; set; } = "Other";
        public decimal CostPrice { get; set; }
        public decimal UnitPrice { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // ------------------------------------------------------------------ Inventory

    public class InventoryDto
    {
        public int InventoryId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal CostPrice { get; set; }
        public decimal UnitPrice { get; set; }
        public bool IsActive { get; set; }
        public decimal QuantityOnHand { get; set; }
        public decimal ReorderLevel { get; set; }
        public string StockStatus { get; set; } = "";
        public decimal StockValue { get; set; }
        public decimal RetailValue { get; set; }
        public DateTime LastUpdatedAt { get; set; }
    }

    public class InventorySummaryDto
    {
        public int TotalProducts { get; set; }
        public int ActiveProducts { get; set; }
        public int InStock { get; set; }
        public int LowStock { get; set; }
        public int OutOfStock { get; set; }
        public decimal TotalUnits { get; set; }
        public decimal StockValue { get; set; }
        public decimal RetailValue { get; set; }
    }

    public class StockMovementDto
    {
        public int StockMovementId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string MovementType { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal BalanceBefore { get; set; }
        public decimal BalanceAfter { get; set; }
        public string Reference { get; set; } = "";
        public string Notes { get; set; } = "";
        public int? RecordedByEmployeeId { get; set; }
        public string RecordedByName { get; set; } = "";
        public DateTime MovementDate { get; set; }
    }

    public class StockMovementRequestDto
    {
        public decimal Quantity { get; set; }
        public string Reference { get; set; } = "";
        public string Notes { get; set; } = "";
        public int? RecordedByEmployeeId { get; set; }
    }

    public class StockAdjustmentDto
    {
        public decimal NewQuantity { get; set; }
        public string Notes { get; set; } = "";
        public int? RecordedByEmployeeId { get; set; }
    }

    public class ReorderLevelDto
    {
        public decimal ReorderLevel { get; set; }
    }

    // ------------------------------------------------------------------ Subscriptions

    public class SubscriptionDto
    {
        public int SubscriptionId { get; set; }
        public int MemberId { get; set; }
        public int PlanId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "";
    }

    public class CreateSubscriptionDto
    {
        public int MemberId { get; set; }
        public int PlanId { get; set; }
        public DateTime? StartDate { get; set; }
    }

    // ------------------------------------------------------------------ Payments

    public class PaymentViewDto
    {
        public int PaymentId { get; set; }
        public int MemberId { get; set; }
        public string MemberName { get; set; } = "";
        public int? SubscriptionId { get; set; }
        public string PlanName { get; set; } = "";
        public int? SaleId { get; set; }
        public string AppliesTo { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string Method { get; set; } = "";
        public string ReferenceNo { get; set; } = "";
        public string Status { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    public class PaymentMethodTotalDto
    {
        public string Method { get; set; } = "";
        public int Count { get; set; }
        public decimal Total { get; set; }
    }

    public class PaymentSummaryDto
    {
        public int Count { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal CollectedThisMonth { get; set; }
        public decimal CollectedToday { get; set; }
        public int PendingCount { get; set; }
        public decimal PendingAmount { get; set; }
        public decimal RefundedAmount { get; set; }
        public decimal OutstandingFromSales { get; set; }
        public decimal OutstandingFromMemberships { get; set; }
        public decimal TotalOutstanding { get; set; }
        public int PartiallyPaidSales { get; set; }
        public int UnpaidSales { get; set; }
        public List<PaymentMethodTotalDto> ByMethod { get; set; } = new();
    }

    public class RecordPaymentDto
    {
        public int MemberId { get; set; }
        public int? SubscriptionId { get; set; }
        public int? SaleId { get; set; }
        public decimal Amount { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string Method { get; set; } = "Cash";
        public string ReferenceNo { get; set; } = "";
        public string Status { get; set; } = "Completed";
        public string Notes { get; set; } = "";
    }

    public class UpdatePaymentDto
    {
        public decimal Amount { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string Method { get; set; } = "Cash";
        public string ReferenceNo { get; set; } = "";
        public string Status { get; set; } = "Completed";
        public string Notes { get; set; } = "";
    }

    public class UpdatePaymentStatusDto
    {
        public string Status { get; set; } = "Completed";
    }

    public class VoidPaymentDto
    {
        public string Reason { get; set; } = "";
    }

    // ------------------------------------------------------------------ Sales

    public class SaleViewDto
    {
        public int SaleId { get; set; }
        public int MemberId { get; set; }
        public string MemberName { get; set; } = "";
        public DateTime SaleDate { get; set; }
        public int ItemCount { get; set; }
        public int TotalQuantity { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "";
        public int? CashierEmployeeId { get; set; }
        public string CashierName { get; set; } = "";
        public string Notes { get; set; } = "";
        public decimal AmountPaid { get; set; }
        public decimal Balance { get; set; }
        public string PaymentStatus { get; set; } = "";
    }

    public class SaleLineDto
    {
        public int SaleItemId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
    }

    public class SaleDetailDto
    {
        public SaleViewDto Sale { get; set; } = new();
        public List<SaleLineDto> Lines { get; set; } = new();
        public List<PaymentViewDto> Payments { get; set; } = new();
    }

    /// <summary>
    /// Only the product and how many of it. The unit price is resolved on the server from
    /// the catalogue, so the browser cannot name its own price.
    /// </summary>
    public class CreateSaleItemDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class CreateSaleDto
    {
        public int MemberId { get; set; }
        public List<CreateSaleItemDto> Items { get; set; } = new();
        public decimal Discount { get; set; }
        public int? CashierEmployeeId { get; set; }
        public string Notes { get; set; } = "";
    }

    public class CancelSaleDto
    {
        public string Reason { get; set; } = "";
    }

    public class SaleDto
    {
        public int SaleId { get; set; }
        public int MemberId { get; set; }
        public DateTime SaleDate { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    // ------------------------------------------------------------------ Employees

    public class EmployeeDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Position { get; set; } = "";
        public string Department { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime HireDate { get; set; }
        public decimal BasicSalary { get; set; }
        public string Status { get; set; } = "";
    }

    public class CreateEmployeeDto
    {
        public string EmployeeCode { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Position { get; set; } = "";
        public string Department { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime HireDate { get; set; } = DateTime.UtcNow.Date;
        public decimal BasicSalary { get; set; }
    }

    public class UpdateEmployeeDto : CreateEmployeeDto
    {
        public string Status { get; set; } = "Active";
    }

    // ------------------------------------------------------------------ Payroll

    public class PayrollDto
    {
        public int PayrollId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string Position { get; set; } = "";
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal Allowances { get; set; }
        public decimal Deductions { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal OvertimeRate { get; set; }
        public decimal OvertimePay { get; set; }
        public decimal GrossPay { get; set; }
        public decimal NetPay { get; set; }
        public string Status { get; set; } = "";
        public DateTime? PaidDate { get; set; }
        public string Notes { get; set; } = "";
    }

    public class PayrollSummaryDto
    {
        public int RunCount { get; set; }
        public int EmployeeCount { get; set; }
        public decimal TotalGross { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal TotalOvertime { get; set; }
        public decimal TotalNet { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal TotalOutstanding { get; set; }
    }

    /// <summary>
    /// Note there is no gross or net here. The server calculates those, so the form only
    /// collects the inputs.
    /// </summary>
    public class CreatePayrollDto
    {
        public int EmployeeId { get; set; }
        public DateTime PeriodStart { get; set; } = DateTime.UtcNow.Date;
        public DateTime PeriodEnd { get; set; } = DateTime.UtcNow.Date;
        public decimal? BasicSalary { get; set; }
        public decimal Allowances { get; set; }
        public decimal Deductions { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal OvertimeRate { get; set; }
        public decimal OvertimePay { get; set; }
        public string Notes { get; set; } = "";
    }

    public class UpdatePayrollDto
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal Allowances { get; set; }
        public decimal Deductions { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal OvertimeRate { get; set; }
        public decimal OvertimePay { get; set; }
        public string Notes { get; set; } = "";
    }

    public class UpdatePayrollStatusDto
    {
        public string Status { get; set; } = "Draft";
    }

    // ------------------------------------------------------------------ Expenses

    public class ExpenseDto
    {
        public int ExpenseId { get; set; }
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string PaymentMethod { get; set; } = "";
        public string ReferenceNo { get; set; } = "";
        public int? RecordedByEmployeeId { get; set; }
        public string RecordedByName { get; set; } = "";
    }

    public class ExpenseCategoryTotalDto
    {
        public string Category { get; set; } = "";
        public decimal Total { get; set; }
        public int Count { get; set; }
    }

    public class ExpenseSummaryDto
    {
        public int Count { get; set; }
        public decimal Total { get; set; }
        public decimal ThisMonth { get; set; }
        public List<ExpenseCategoryTotalDto> ByCategory { get; set; } = new();
    }

    public class CreateExpenseDto
    {
        public string Category { get; set; } = "Other";
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; } = DateTime.UtcNow.Date;
        public string PaymentMethod { get; set; } = "Cash";
        public string ReferenceNo { get; set; } = "";
        public int? RecordedByEmployeeId { get; set; }
    }

    public class UpdateExpenseDto : CreateExpenseDto
    {
    }

    // ------------------------------------------------------------------ Membership overview

    public class MembershipOverviewDto
    {
        public int MemberId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string MemberStatus { get; set; } = "";
        public DateTime JoinDate { get; set; }
        public int? SubscriptionId { get; set; }
        public int? PlanId { get; set; }
        public string PlanName { get; set; } = "";
        public decimal PlanPrice { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string MembershipStatus { get; set; } = "";
        public int? DaysRemaining { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Balance { get; set; }
        public string PaymentStatus { get; set; } = "";
    }

    // ------------------------------------------------------------------ System administration

    public class CompanyDto
    {
        public int CompanyId { get; set; }
        public string CompanyCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CompanyDatabaseDto
    {
        public int CompanyDatabaseId { get; set; }
        public int CompanyId { get; set; }
        public string ServerName { get; set; } = "";
        public string DatabaseName { get; set; } = "";
        public string CredentialKey { get; set; } = "";
        public bool IsActive { get; set; }
    }

    public class ProvisioningStatusDto
    {
        public int CompanyId { get; set; }
        public bool CanConnect { get; set; }
        public bool DatabaseExists { get; set; }
        public bool IsUpToDate { get; set; }
        public List<string> AppliedMigrations { get; set; } = new();
        public List<string> PendingMigrations { get; set; } = new();
        public string? Problem { get; set; }
    }

    public class CreateCompanyDto
    {
        public string CompanyCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }

    public class CreateCompanyDatabaseDto
    {
        public int CompanyId { get; set; }
        public string ServerName { get; set; } = "";
        public string DatabaseName { get; set; } = "";
        public string CredentialKey { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }
}
