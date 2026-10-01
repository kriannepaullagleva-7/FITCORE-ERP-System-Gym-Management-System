using System;
using System.Collections.Generic;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// The window a report covers. Held as an explicit pair so every report agrees on what
    /// "from 1 March to 31 March" means: inclusive of the first day, exclusive of the day
    /// after the last, which is how the queries are written.
    /// </summary>
    public class ReportRange
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public static ReportRange Resolve(DateTime? from, DateTime? to)
        {
            // Default to the current month, which is what a report screen is usually opened for.
            var now = DateTime.UtcNow;
            var start = from?.Date ?? new DateTime(now.Year, now.Month, 1);
            var endInclusive = to?.Date ?? now.Date;

            if (endInclusive < start) endInclusive = start;

            return new ReportRange
            {
                FromUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc),
                // Exclusive upper bound, so the whole of the last day is included.
                ToUtc = DateTime.SpecifyKind(endInclusive.AddDays(1), DateTimeKind.Utc)
            };
        }
    }

    // ---------------------------------------------------------------------- Sales

    public class ProductSalesRow
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int QuantitySold { get; set; }
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
        public decimal Margin => Revenue - Cost;
    }

    public class SalesReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public int TransactionCount { get; set; }
        public int CancelledCount { get; set; }
        public decimal GrossSales { get; set; }
        public decimal Discounts { get; set; }
        public decimal NetSales { get; set; }
        public decimal AverageSale { get; set; }
        public int UnitsSold { get; set; }

        public decimal Collected { get; set; }
        public decimal Outstanding { get; set; }

        public List<TrendPoint> ByDay { get; set; } = new();
        public List<ProductSalesRow> TopProducts { get; set; } = new();
        public List<CategorySlice> ByCategory { get; set; } = new();
        public List<SaleView> Sales { get; set; } = new();
    }

    // ---------------------------------------------------------------------- Payments

    public class OutstandingRow
    {
        public string Source { get; set; } = string.Empty; // "Sale" or "Membership"
        public int ReferenceId { get; set; }

        /// <summary>Null for a walk-in - see <see cref="MemberName"/>.</summary>
        public int? MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public decimal Total { get; set; }
        public decimal Paid { get; set; }
        public decimal Balance { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class PaymentReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public int Count { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal Pending { get; set; }
        public decimal Refunded { get; set; }

        public decimal OutstandingFromSales { get; set; }
        public decimal OutstandingFromMemberships { get; set; }
        public decimal TotalOutstanding => OutstandingFromSales + OutstandingFromMemberships;

        public int PaidCount { get; set; }
        public int PartiallyPaidCount { get; set; }
        public int UnpaidCount { get; set; }

        public List<CategorySlice> ByMethod { get; set; } = new();
        public List<TrendPoint> ByDay { get; set; } = new();
        public List<OutstandingRow> Outstanding { get; set; } = new();
        public List<PaymentView> Payments { get; set; } = new();
    }

    // ---------------------------------------------------------------------- Inventory

    public class InventoryReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public InventorySummary Summary { get; set; } = new();
        public List<InventoryView> Stock { get; set; } = new();
        public List<InventoryView> LowStock { get; set; } = new();
        public List<InventoryView> OutOfStock { get; set; } = new();
        public List<StockMovementView> Movements { get; set; } = new();
        public List<CategorySlice> ValueByCategory { get; set; } = new();
    }

    // ---------------------------------------------------------------------- Membership

    public class MembershipReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public int TotalMembers { get; set; }
        public int ActiveMembers { get; set; }
        public int InactiveMembers { get; set; }
        public int NewMembersInRange { get; set; }

        public int ActiveSubscriptions { get; set; }
        public int ExpiringSoon { get; set; }
        public int ExpiredSubscriptions { get; set; }
        public int CancelledSubscriptions { get; set; }

        public decimal MembershipRevenue { get; set; }
        public decimal MembershipOutstanding { get; set; }

        public List<CategorySlice> ByPlan { get; set; } = new();
        public List<TrendPoint> JoinsByDay { get; set; } = new();
        public List<MembershipView> Members { get; set; } = new();
    }

    // ---------------------------------------------------------------------- Expenses

    public class ExpenseReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public int Count { get; set; }
        public decimal Total { get; set; }
        public decimal PayrollPaid { get; set; }
        public decimal TotalOutgoings => Total + PayrollPaid;

        public List<CategorySlice> ByCategory { get; set; } = new();
        public List<TrendPoint> ByDay { get; set; } = new();
        public List<ExpenseView> Expenses { get; set; } = new();
    }

    // ---------------------------------------------------------------------- Employees

    /// <summary>
    /// One employee's attendance for the period the report covers.
    ///
    /// The hours here are the same ones Payroll calculates from, read back through the same
    /// Attendance rows, so an Employee report and a payslip for the same period cannot
    /// disagree about how long somebody worked.
    /// </summary>
    public class EmployeeAttendanceRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime HireDate { get; set; }

        public int DaysPresent { get; set; }
        public int DaysAbsent { get; set; }
        public int DaysLate { get; set; }
        public int DaysOnLeave { get; set; }

        public decimal RegularHours { get; set; }
        public decimal OvertimeHours { get; set; }

        /// <summary>Days recorded at all - the denominator the rate is measured against.</summary>
        public int DaysRecorded => DaysPresent + DaysAbsent + DaysLate + DaysOnLeave;

        /// <summary>
        /// Turning up, as a percentage of the days recorded. Late still counts as attending:
        /// the employee was there, and counting lateness as absence would both overstate
        /// absenteeism and understate the hours payroll is about to pay for.
        /// </summary>
        public decimal AttendanceRate =>
            DaysRecorded == 0 ? 0m
                : Math.Round((DaysPresent + DaysLate) * 100m / DaysRecorded, 1);
    }

    /// <summary>
    /// Headcount, attendance and leave over a period.
    ///
    /// Every figure is counted from real Employee, Attendance and LeaveRequest rows. FitCore
    /// holds no establishment or target headcount, so this report states what happened rather
    /// than scoring it against a number nobody entered.
    /// </summary>
    public class EmployeeReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public int TotalEmployees { get; set; }
        public int ActiveEmployees { get; set; }
        public int InactiveEmployees { get; set; }
        public int NewHiresInRange { get; set; }

        public int DaysPresent { get; set; }
        public int DaysAbsent { get; set; }
        public int DaysLate { get; set; }
        public int DaysOnLeave { get; set; }

        public decimal TotalRegularHours { get; set; }
        public decimal TotalOvertimeHours { get; set; }

        /// <summary>Days attended as a percentage of days recorded, across everybody.</summary>
        public decimal AttendanceRate { get; set; }

        public int PendingLeaveRequests { get; set; }
        public int ApprovedLeaveRequests { get; set; }
        public decimal ApprovedLeaveDays { get; set; }

        public List<CategorySlice> ByPosition { get; set; } = new();
        public List<CategorySlice> ByLeaveType { get; set; } = new();

        /// <summary>Attendances recorded per day: Value is hours worked, Count is head count.</summary>
        public List<TrendPoint> AttendanceByDay { get; set; } = new();

        public List<EmployeeAttendanceRow> Employees { get; set; } = new();
    }

    // ------------------------------------------------------------------- Reconciliation

    /// <summary>One day's takings set against what the ledger was told about them.</summary>
    public class ReconciliationDayRow
    {
        public DateTime Date { get; set; }
        public int PaymentCount { get; set; }

        /// <summary>What the front desk recorded as collected.</summary>
        public decimal Takings { get; set; }

        /// <summary>What actually reached a cash or bank account in the general ledger.</summary>
        public decimal Posted { get; set; }

        public decimal Variance => Takings - Posted;

        public bool IsBalanced => Variance == 0m;
    }

    /// <summary>
    /// The takings, the ledger and the bank, set against each other.
    ///
    /// Three records of the same money that are written by three different paths, so the
    /// interesting number is not any one of them but where they disagree. The reason a gap can
    /// exist at all is deliberate: <c>FinancePostingService.GuardedAsync</c> never fails the
    /// operation that triggered it, so a sale or a payment is always committed even when the
    /// posting behind it could not be written. That is the right trade - a till that refuses
    /// money because the books are busy is worse than books that need catching up - but it
    /// means something has to be able to find the arrears afterwards. This is that something,
    /// and <see cref="UnpostedPayments"/> is the list the Finance catch-up sweep would fix.
    ///
    /// A variance is therefore a finding, not an error. It says a posting is missing, and names
    /// which payments are missing it.
    /// </summary>
    public class PaymentReconciliationView
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public int PaymentCount { get; set; }

        /// <summary>Completed payments only: pending and refunded money was never taken.</summary>
        public decimal Takings { get; set; }
        public decimal Pending { get; set; }
        public decimal Refunded { get; set; }

        /// <summary>Cash and bank debits in the ledger that a payment raised.</summary>
        public decimal PostedToLedger { get; set; }

        public decimal Variance => Takings - PostedToLedger;

        public bool IsBalanced => Variance == 0m;

        /// <summary>Money recorded as arriving in a cash or bank account over the period.</summary>
        public decimal BankedIn { get; set; }
        public decimal BankedOut { get; set; }

        public int UnreconciledBankCount { get; set; }
        public decimal UnreconciledBankAmount { get; set; }

        /// <summary>Completed payments with no journal entry keyed to them at all.</summary>
        public int UnpostedPaymentCount { get; set; }
        public decimal UnpostedPaymentAmount { get; set; }

        /// <summary>Reuses the Payments module's own method breakdown rather than restating it.</summary>
        public List<PaymentMethodTotal> ByMethod { get; set; } = new();
        public List<ReconciliationDayRow> ByDay { get; set; } = new();
        public List<PaymentView> UnpostedPayments { get; set; } = new();
    }

    // ---------------------------------------------------------------------- Payroll

    /// <summary>
    /// What payroll cost over a period, with every statutory figure kept separate.
    ///
    /// Runs are selected by the period they <em>end</em> in, not by overlap. A pay period that
    /// straddles a month boundary belongs to exactly one report either way, so summing twelve
    /// monthly reports gives the year - which is not true of an overlap rule, where a run
    /// spanning the turn of the month would be counted in both.
    ///
    /// The employer share is reported beside the deductions rather than inside them, because
    /// the two are opposite things: a deduction comes out of what the employee is owed, an
    /// employer contribution is a cost on top of it. Adding them together would overstate the
    /// deductions and understate what the gym actually spends on staff.
    /// </summary>
    public class PayrollReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public int RunCount { get; set; }
        public int EmployeeCount { get; set; }
        public int PaidRunCount { get; set; }
        public int UnpaidRunCount { get; set; }

        public decimal RegularHours { get; set; }
        public decimal OvertimeHours { get; set; }

        public decimal RegularPay { get; set; }
        public decimal OvertimePay { get; set; }
        public decimal Allowances { get; set; }
        public decimal GrossPay { get; set; }

        public decimal Sss { get; set; }
        public decimal PhilHealth { get; set; }
        public decimal PagIbig { get; set; }
        public decimal WithholdingTax { get; set; }
        public decimal OtherDeductions { get; set; }
        public decimal TotalDeductions { get; set; }

        public decimal NetPay { get; set; }

        public decimal SssEmployerShare { get; set; }
        public decimal PhilHealthEmployerShare { get; set; }
        public decimal PagIbigEmployerShare { get; set; }
        public decimal EmployerContributions { get; set; }

        /// <summary>Gross plus the employer share: what the staff actually cost the gym.</summary>
        public decimal TotalEmploymentCost => GrossPay + EmployerContributions;

        public decimal Paid { get; set; }
        public decimal Outstanding { get; set; }

        public List<CategorySlice> ByStatus { get; set; } = new();

        /// <summary>The statutory deductions broken out, so a payslip can be checked against it.</summary>
        public List<CategorySlice> DeductionBreakdown { get; set; } = new();

        public List<TrendPoint> ByDay { get; set; } = new();
        public List<PayrollView> Runs { get; set; } = new();
    }
}
