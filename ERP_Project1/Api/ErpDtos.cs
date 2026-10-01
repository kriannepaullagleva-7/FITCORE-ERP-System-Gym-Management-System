namespace ERP_Project1.Api
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

        /// <summary>
        /// Weighted average cost, and the unit cost of the most recent receipt. The server
        /// sends both; they were previously dropped here for want of a property to land in,
        /// which left the Valuation screen unable to show what the stock is actually worth
        /// holding rather than what it would sell for.
        /// </summary>
        public decimal AverageCost { get; set; }
        public decimal LastUnitCost { get; set; }

        public decimal StockValue { get; set; }
        public decimal RetailValue { get; set; }

        /// <summary>Retail value less stock value: the margin still sitting on the shelf.</summary>
        public decimal PotentialMargin { get; set; }

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
        public int? SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public int? RecordedByEmployeeId { get; set; }
        public string RecordedByName { get; set; } = "";
        public int? PerformedByUserId { get; set; }
        public string PerformedBy { get; set; } = "";
        public DateTime MovementDate { get; set; }
    }

    public class StockInRequestDto
    {
        public decimal Quantity { get; set; }
        public int SupplierId { get; set; }
        public string Reference { get; set; } = "";
        public string Notes { get; set; } = "";
        public int? RecordedByEmployeeId { get; set; }
    }

    public class StockOutRequestDto
    {
        public decimal Quantity { get; set; }
        public string Reason { get; set; } = "";
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
        public int? MemberId { get; set; }
        public string? WalkInName { get; set; }
        public string? WalkInPhone { get; set; }
        public int PlanId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "";
    }

    public class CreateSubscriptionDto
    {
        public int? MemberId { get; set; }
        public string? WalkInName { get; set; }
        public string? WalkInPhone { get; set; }
        public int PlanId { get; set; }
        public DateTime? StartDate { get; set; }
    }

    // ------------------------------------------------------------------ Payments

    public class PaymentViewDto
    {
        public int PaymentId { get; set; }
        public int? MemberId { get; set; }
        public string? WalkInName { get; set; }

        /// <summary>The member's real name, or the walk-in's name. Used on receipts and detail views.</summary>
        public string MemberName { get; set; } = "";

        /// <summary>
        /// What the Payments grid shows in its Member column: the member for membership and
        /// general payments, "Others" for a counter sale, where the member is incidental.
        /// </summary>
        public string MemberDisplay { get; set; } = "";

        /// <summary>Membership, Sales or General. Decided by the server from what it settles.</summary>
        public string Category { get; set; } = "";

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

        /// <summary>The signed-in user who took the money.</summary>
        public int? ProcessedByUserId { get; set; }
        public string ProcessedBy { get; set; } = "";

        public decimal? AmountTendered { get; set; }
        public decimal? ChangeGiven { get; set; }
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
        public int? MemberId { get; set; }
        public string? WalkInName { get; set; }
        public int? SubscriptionId { get; set; }
        public int? SaleId { get; set; }
        public decimal Amount { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string Method { get; set; } = "Cash";
        public string ReferenceNo { get; set; } = "";
        public string Status { get; set; } = "Completed";
        public string Notes { get; set; } = "";
        public decimal? AmountTendered { get; set; }
    }

    public class UpdatePaymentDto
    {
        public decimal Amount { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string Method { get; set; } = "Cash";
        public string ReferenceNo { get; set; } = "";
        public string Status { get; set; } = "Completed";
        public string Notes { get; set; } = "";
        public decimal? AmountTendered { get; set; }
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
        public int? MemberId { get; set; }
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

        /// <summary>The signed-in user who completed the sale.</summary>
        public int? ProcessedByUserId { get; set; }
        public string ProcessedBy { get; set; } = "";

        public string Notes { get; set; } = "";
        public decimal AmountPaid { get; set; }
        public decimal Balance { get; set; }
        public string PaymentStatus { get; set; } = "";
        public decimal? AmountTendered { get; set; }
        public decimal? ChangeGiven { get; set; }
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
        public int? MemberId { get; set; }
        public string? WalkInName { get; set; }
        public List<CreateSaleItemDto> Items { get; set; } = new();
        public decimal Discount { get; set; }
        public int? CashierEmployeeId { get; set; }
        public string Notes { get; set; } = "";

        /// <summary>
        /// Settle the sale in full as part of the same server-side transaction. The till
        /// sets this instead of posting a second payment request, so a completed sale and
        /// its money can never end up out of step with each other.
        /// </summary>
        public bool SettleNow { get; set; }

        public string PaymentMethod { get; set; } = "Cash";
        public decimal? AmountTendered { get; set; }
    }

    public class CancelSaleDto
    {
        public string Reason { get; set; } = "";
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
        public decimal? AmountTendered { get; set; }
        public decimal? ChangeGiven { get; set; }
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
        public decimal HourlyRate { get; set; }
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
        public decimal HourlyRate { get; set; }
    }

    public class UpdateEmployeeDto : CreateEmployeeDto
    {
        public string Status { get; set; } = "Active";
    }

    /// <summary>What happened when an employee was given a FitCore sign-in.</summary>
    public class EmployeeAccountResultDto
    {
        /// <summary>Created, AlreadyExisted, Skipped or Failed.</summary>
        public string Outcome { get; set; } = "";
        public string Message { get; set; } = "";
        public bool AccountUsable { get; set; }
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

        public decimal RegularHours { get; set; }
        public decimal HourlyRate { get; set; }
        public decimal RegularPay { get; set; }

        public decimal OvertimeHours { get; set; }
        public decimal OvertimeRate { get; set; }
        public decimal OvertimePay { get; set; }

        public decimal GrossPay { get; set; }

        public decimal SssDeduction { get; set; }
        public decimal PhilHealthDeduction { get; set; }
        public decimal PagIbigDeduction { get; set; }
        public decimal WithholdingTax { get; set; }
        public decimal OtherDeductions { get; set; }
        public decimal Deductions { get; set; }

        public decimal NetPay { get; set; }

        // The employer's own statutory contributions. Never deducted from anybody and never
        // shown on a payslip as a reduction - they are a cost on top of gross pay, which is
        // why the true cost of employing this person is GrossPay + EmployerContributions.
        public decimal SssEmployerShare { get; set; }
        public decimal PhilHealthEmployerShare { get; set; }
        public decimal PagIbigEmployerShare { get; set; }
        public decimal EmployerContributions { get; set; }
        public decimal TotalEmploymentCost { get; set; }

        public string Status { get; set; } = "";
        public DateTime? PaidDate { get; set; }

        /// <summary>Set once the run has been approved, the step before it is paid.</summary>
        public DateTime? ApprovedAt { get; set; }
        public string ApprovedBy { get; set; } = "";

        /// <summary>The signed-in user who created the run. Named on the payslip.</summary>
        public int? ProcessedByUserId { get; set; }
        public string ProcessedBy { get; set; } = "";

        /// <summary>Set only once the run has been edited after it was created.</summary>
        public int? LastModifiedByUserId { get; set; }
        public string LastModifiedBy { get; set; } = "";

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

    /// <summary>
    /// Generates a run from recorded attendance rather than typed hours. Regular/overtime
    /// hours, the hourly rate and the statutory deductions are all derived server-side.
    /// </summary>
    public class GeneratePayrollDto
    {
        public int EmployeeId { get; set; }
        public DateTime PeriodStart { get; set; } = DateTime.UtcNow.Date;
        public DateTime PeriodEnd { get; set; } = DateTime.UtcNow.Date;
        public decimal Allowances { get; set; }
        public decimal OtherDeductions { get; set; }
        public string Notes { get; set; } = "";
    }

    // ------------------------------------------------------------------ Attendance

    public class AttendanceDto
    {
        public int AttendanceId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string Position { get; set; } = "";
        public DateTime Date { get; set; }
        public DateTime? TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }
        public decimal RegularHours { get; set; }
        public decimal OvertimeHours { get; set; }
        public string Status { get; set; } = "Present";
        public string Notes { get; set; } = "";
        public int? RecordedByUserId { get; set; }
        public string RecordedBy { get; set; } = "";
        public int? ModifiedByUserId { get; set; }
        public string ModifiedBy { get; set; } = "";
    }

    public class CreateAttendanceDto
    {
        public int EmployeeId { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow.Date;
        public DateTime? TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }
        public string Status { get; set; } = "Present";
        public string Notes { get; set; } = "";
    }

    public class UpdateAttendanceDto : CreateAttendanceDto
    {
    }

    public class AttendancePeriodSummaryDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int DaysPresent { get; set; }
        public int DaysAbsent { get; set; }
        public int DaysLate { get; set; }
        public int DaysOnLeave { get; set; }
        public decimal TotalRegularHours { get; set; }
        public decimal TotalOvertimeHours { get; set; }
    }

    // ------------------------------------------------------------------ Audit

    public class AuditEventDto
    {
        public long AuditEventId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string Username { get; set; } = "";
        public string RoleKey { get; set; } = "";
        public string Action { get; set; } = "";
        public string Module { get; set; } = "";
        public string EntityName { get; set; } = "";
        public string? EntityId { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
        public string? Summary { get; set; }

        /// <summary>Where the request came from. Shown on the Security screen.</summary>
        public string? IpAddress { get; set; }
    }

    // ------------------------------------------------------------------ Expenses

    public class ExpenseDto
    {
        public int ExpenseId { get; set; }
        public string Category { get; set; } = "";

        /// <summary>The broad grouping the income statement shows, derived from the category.</summary>
        public string CategoryGroup { get; set; } = "";

        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string PaymentMethod { get; set; } = "";
        public string ReferenceNo { get; set; } = "";

        /// <summary>Paid or Unpaid. An unpaid expense is money owed and sits in payables.</summary>
        public string Status { get; set; } = "";

        public string PaidTo { get; set; } = "";
        public int? SupplierId { get; set; }
        public string SupplierName { get; set; } = "";

        /// <summary>The ledger account it posted to.</summary>
        public int? AccountId { get; set; }
        public string AccountName { get; set; } = "";

        public int? BankAccountId { get; set; }
        public string BankAccountName { get; set; } = "";

        public int? RecordedByEmployeeId { get; set; }
        public string RecordedByName { get; set; } = "";
        public string RecordedBy { get; set; } = "";

        /// <summary>Set once the expense has reached the ledger.</summary>
        public int? JournalEntryId { get; set; }

        public DateTime CreatedAt { get; set; }
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

        /// <summary>Recorded but not yet settled. Part of what the gym owes.</summary>
        public decimal Unpaid { get; set; }
        public int UnpaidCount { get; set; }

        public List<ExpenseCategoryTotalDto> ByCategory { get; set; } = new();

        /// <summary>The same totals rolled up to the groups the income statement shows.</summary>
        public List<CategorySliceDto> ByGroup { get; set; } = new();

        public List<TrendPointDto> ByMonth { get; set; } = new();
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

        /// <summary>Paid or Unpaid. Unpaid puts it in accounts payable until it is settled.</summary>
        public string Status { get; set; } = "Paid";

        public string PaidTo { get; set; } = "";
        public int? SupplierId { get; set; }

        /// <summary>Null lets the category decide which ledger account it posts to.</summary>
        public int? AccountId { get; set; }

        public int? BankAccountId { get; set; }
    }

    public class UpdateExpenseDto : CreateExpenseDto
    {
    }

    /// <summary>Settles an expense that was recorded as owed.</summary>
    public class SettleExpenseDto
    {
        public string PaymentMethod { get; set; } = "Cash";
        public int? BankAccountId { get; set; }
    }

    // ------------------------------------------------------------------ Membership overview

    public class MembershipOverviewDto
    {
        public int? MemberId { get; set; }
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

    // ----------------------------------------------------------------------------------
    // Suppliers - tenant master data behind /api/suppliers.
    // ----------------------------------------------------------------------------------

    public class SupplierDto
    {
        public int SupplierId { get; set; }
        public string SupplierCode { get; set; } = "";
        public string SupplierName { get; set; } = "";
        public string? ContactPerson { get; set; }
        public string? ContactNumber { get; set; }
        public string? EmailAddress { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateSupplierDto
    {
        public string SupplierCode { get; set; } = "";
        public string SupplierName { get; set; } = "";
        public string? ContactPerson { get; set; }
        public string? ContactNumber { get; set; }
        public string? EmailAddress { get; set; }
        public string? Address { get; set; }
    }

    public class UpdateSupplierDto : CreateSupplierDto
    {
        public bool IsActive { get; set; } = true;
    }
}
