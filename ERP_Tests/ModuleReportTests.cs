using ERP_domain.entities;
using ERP_infrastructure.repositories;
using ERP_infrastructure.services;
using Microsoft.Extensions.Options;
using Xunit;

namespace ERP_Tests;

/// <summary>
/// The employee, payroll and reconciliation reports, exercised through the services that create
/// the records they describe.
///
/// The reports are asserted against operations rather than against hand-written rows, for the
/// same reason the ledger tests are: a report's whole value is that it agrees with what actually
/// happened. Rows inserted directly would prove only that the aggregation can add up.
///
/// Everything runs against real SQLite, so the grouping, the joins and the date arithmetic are
/// genuinely translated and executed.
/// </summary>
public class ModuleReportTests
{
    private sealed class Harness : IDisposable
    {
        public TenantDbFixture Db { get; }
        public IReportService Reports { get; }
        public IEmployeeService Employees { get; }
        public IAttendanceService Attendance { get; }
        public IPayrollService Payroll { get; }
        public ILeaveService Leave { get; }
        public IPaymentService Payments { get; }
        public IMemberService Members { get; }

        public Harness()
        {
            Db = new TenantDbFixture();

            var actor = new FakeCurrentUserAccessor(roleKey: ErpRoles.Admin);

            var memberRepo = new MemberRepository(Db.Context);
            var productRepo = new ProductRepository(Db.Context);
            var inventoryRepo = new InventoryRepository(Db.Context);
            var paymentRepo = new PaymentRepository(Db.Context);
            var saleRepo = new SaleRepository(Db.Context);
            var subscriptionRepo = new SubscriptionRepository(Db.Context);
            var planRepo = new GenericRepository<MembershipPlan>(Db.Context);
            var supplierRepo = new GenericRepository<Supplier>(Db.Context);
            var employeeRepo = new EmployeeRepository(Db.Context);
            var attendanceRepo = new AttendanceRepository(Db.Context);
            var payrollRepo = new PayrollRepository(Db.Context);
            var expenseRepo = new ExpenseRepository(Db.Context);

            var audit = new TenantAuditService(Db.Context, actor);

            Members = new MemberService(memberRepo, paymentRepo, subscriptionRepo);

            var inventory = new InventoryService(
                inventoryRepo, productRepo, supplierRepo, Db.Context, actor, audit, Db.Finance);

            Payments = new PaymentService(
                paymentRepo, subscriptionRepo, memberRepo, Db.Context, actor, Db.Finance);

            var sales = new SaleService(
                saleRepo, memberRepo, productRepo, paymentRepo, Db.Context, actor, audit, Db.Finance);

            var subscriptions = new SubscriptionService(
                subscriptionRepo, planRepo, memberRepo, audit, Db.Finance);

            var expenses = new ExpenseService(
                expenseRepo, employeeRepo, Db.Context, Db.Accounts, Db.Finance, actor);

            Employees = new EmployeeService(employeeRepo, actor, new NoOpUserAccountService());
            Attendance = new AttendanceService(attendanceRepo, employeeRepo, actor);
            Leave = new LeaveService(Db.Context, actor, audit);

            Payroll = new PayrollService(
                payrollRepo, employeeRepo, attendanceRepo,
                new PayrollDeductionCalculator(Options.Create(new PayrollDeductionOptions())),
                actor, audit, Db.Finance);

            Reports = new ReportService(
                Db.Context, sales, Payments, inventory, Members, expenses, subscriptions);
        }

        public void Dispose() => Db.Dispose();
    }

    // ------------------------------------------------------------------ employees

    [Fact]
    public async Task The_employee_report_counts_attendance_from_the_rows_payroll_reads()
    {
        using var h = new Harness();

        var employee = await h.Employees.CreateEmployeeAsync(
            "EMP-1", "Maria", "Santos", EmployeePositions.Staff, "Operations",
            "555-1", "maria@example.com", new DateTime(2026, 3, 2), 0m, 70m);

        // Two ordinary days and one absence, inside the window.
        foreach (var day in new[] { 3, 4 })
        {
            var date = new DateTime(2026, 3, day);
            await h.Attendance.CreateAsync(
                employee.EmployeeId, date,
                timeIn: date.AddHours(8), timeOut: date.AddHours(17),
                status: "Present", notes: "");
        }

        var absent = new DateTime(2026, 3, 5);
        await h.Attendance.CreateAsync(
            employee.EmployeeId, absent, timeIn: null, timeOut: null,
            status: "Absent", notes: "");

        var report = await h.Reports.GetEmployeeReportAsync(
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.Equal(1, report.TotalEmployees);
        Assert.Equal(1, report.ActiveEmployees);
        Assert.Equal(1, report.NewHiresInRange);

        Assert.Equal(2, report.DaysPresent);
        Assert.Equal(1, report.DaysAbsent);

        // Two eight-hour days: the hours come from time in/out, not from anything typed.
        Assert.Equal(16m, report.TotalRegularHours);

        // Present on two of the three days recorded.
        Assert.Equal(66.7m, report.AttendanceRate);

        var row = Assert.Single(report.Employees);
        Assert.Equal("Maria Santos", row.EmployeeName);
        Assert.Equal(3, row.DaysRecorded);
        Assert.Equal(66.7m, row.AttendanceRate);
    }

    /// <summary>
    /// Somebody with nothing recorded still appears. A report that quietly dropped the employee
    /// who never turned up would hide the one case it exists to surface.
    /// </summary>
    [Fact]
    public async Task The_employee_report_lists_employees_with_no_attendance_at_all()
    {
        using var h = new Harness();

        await h.Employees.CreateEmployeeAsync(
            "EMP-2", "Absent", "Andy", EmployeePositions.Staff, "Operations",
            "555-2", "andy@example.com", new DateTime(2026, 1, 5), 0m, 70m);

        var report = await h.Reports.GetEmployeeReportAsync(
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        var row = Assert.Single(report.Employees);

        Assert.Equal(0, row.DaysRecorded);
        Assert.Equal(0m, row.AttendanceRate);

        // Hired before the window, so not a new hire in it.
        Assert.Equal(0, report.NewHiresInRange);

        // No days recorded means the rate has no denominator; it must not be reported as 100%.
        Assert.Equal(0m, report.AttendanceRate);
    }

    /// <summary>
    /// Leave that began before the window but runs into it is leave taken in the window. Counting
    /// only requests that start inside it would lose a fortnight's holiday spanning the month end.
    /// </summary>
    [Fact]
    public async Task The_employee_report_counts_leave_that_overlaps_the_window()
    {
        using var h = new Harness();

        var employee = await h.Employees.CreateEmployeeAsync(
            "EMP-3", "Paolo", "Cruz", EmployeePositions.Staff, "Operations",
            "555-3", "paolo@example.com", new DateTime(2026, 1, 5), 0m, 70m);

        var request = await h.Leave.CreateRequestAsync(
            employee.EmployeeId, LeaveTypes.Vacation,
            new DateTime(2026, 2, 25), new DateTime(2026, 3, 4), isPaid: null, reason: "Holiday");

        await h.Leave.DecideAsync(request.LeaveRequestId, approve: true, notes: "");

        var report = await h.Reports.GetEmployeeReportAsync(
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.Equal(1, report.ApprovedLeaveRequests);
        Assert.True(report.ApprovedLeaveDays > 0m);

        var slice = Assert.Single(report.ByLeaveType);
        Assert.Equal(LeaveTypes.Vacation, slice.Label);
    }

    // ------------------------------------------------------------------ payroll

    /// <summary>
    /// The payroll report must keep the employee's deductions and the employer's contributions
    /// apart. They are opposite things - one reduces the net, the other is a cost on top of
    /// gross - and adding them together would both overstate the deductions and misstate what
    /// the staff actually cost.
    /// </summary>
    [Fact]
    public async Task The_payroll_report_separates_deductions_from_the_employer_share()
    {
        using var h = new Harness();

        var employee = await h.Employees.CreateEmployeeAsync(
            "EMP-4", "Grace", "Bautista", EmployeePositions.Staff, "Operations",
            "555-4", "grace@example.com", new DateTime(2026, 1, 5), 0m, 100m);

        foreach (var day in new[] { 2, 3, 4, 5, 6 })
        {
            var date = new DateTime(2026, 3, day);
            await h.Attendance.CreateAsync(
                employee.EmployeeId, date,
                timeIn: date.AddHours(8), timeOut: date.AddHours(17),
                status: "Present", notes: "");
        }

        var run = await h.Payroll.GenerateFromAttendanceAsync(
            employee.EmployeeId, new DateTime(2026, 3, 1), new DateTime(2026, 3, 15),
            allowances: 0m, otherDeductions: 0m, notes: "");

        var report = await h.Reports.GetPayrollReportAsync(
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.Equal(1, report.RunCount);
        Assert.Equal(1, report.EmployeeCount);

        Assert.Equal(run.GrossPay, report.GrossPay);
        Assert.Equal(run.Deductions, report.TotalDeductions);
        Assert.Equal(run.NetPay, report.NetPay);
        Assert.Equal(run.EmployerContributions, report.EmployerContributions);

        // The identity the whole report rests on.
        Assert.Equal(report.GrossPay - report.TotalDeductions, report.NetPay);
        Assert.Equal(report.GrossPay + report.EmployerContributions, report.TotalEmploymentCost);

        // The named statutory figures add up to the total deduction.
        Assert.Equal(
            report.Sss + report.PhilHealth + report.PagIbig
                + report.WithholdingTax + report.OtherDeductions,
            report.TotalDeductions);

        // Nothing has been paid yet, so all of it is still owed.
        Assert.Equal(0, report.PaidRunCount);
        Assert.Equal(1, report.UnpaidRunCount);
        Assert.Equal(0m, report.Paid);
        Assert.Equal(report.NetPay, report.Outstanding);
    }

    /// <summary>
    /// A run belongs to the period it ends in, so consecutive monthly reports partition the runs
    /// rather than double-counting the one that straddles the boundary.
    /// </summary>
    [Fact]
    public async Task A_payroll_run_is_counted_in_exactly_one_month()
    {
        using var h = new Harness();

        var employee = await h.Employees.CreateEmployeeAsync(
            "EMP-5", "Straddle", "Sam", EmployeePositions.Staff, "Operations",
            "555-5", "sam@example.com", new DateTime(2026, 1, 5), 0m, 100m);

        foreach (var (month, day) in new[] { (2, 26), (2, 27), (3, 2) })
        {
            var date = new DateTime(2026, month, day);
            await h.Attendance.CreateAsync(
                employee.EmployeeId, date,
                timeIn: date.AddHours(8), timeOut: date.AddHours(17),
                status: "Present", notes: "");
        }

        // 25 February to 3 March: the period spans the month end.
        await h.Payroll.GenerateFromAttendanceAsync(
            employee.EmployeeId, new DateTime(2026, 2, 25), new DateTime(2026, 3, 3),
            allowances: 0m, otherDeductions: 0m, notes: "");

        var february = await h.Reports.GetPayrollReportAsync(
            new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

        var march = await h.Reports.GetPayrollReportAsync(
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        // It ends in March, so March owns it and February does not.
        Assert.Equal(0, february.RunCount);
        Assert.Equal(1, march.RunCount);
    }

    // ------------------------------------------------------------------ reconciliation

    /// <summary>
    /// With posting switched on, a completed payment reaches the ledger and the takings and the
    /// books agree. This is the baseline the variance is measured against.
    /// </summary>
    [Fact]
    public async Task Reconciliation_balances_when_every_payment_reached_the_ledger()
    {
        using var h = new Harness();

        var member = await h.Members.CreateMemberAsync("Paying", "Member", "555", "pm@example.com");

        await h.Payments.RecordPaymentAsync(
            member.MemberId, subscriptionId: null, saleId: null,
            amount: 500m, paymentDate: new DateTime(2026, 3, 10),
            method: "Cash", referenceNo: "R-1", status: "Completed", notes: "");

        var view = await h.Reports.GetPaymentReconciliationAsync(
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.Equal(1, view.PaymentCount);
        Assert.Equal(500m, view.Takings);
        Assert.Equal(500m, view.PostedToLedger);

        Assert.Equal(0m, view.Variance);
        Assert.True(view.IsBalanced);

        Assert.Equal(0, view.UnpostedPaymentCount);
        Assert.Empty(view.UnpostedPayments);

        var method = Assert.Single(view.ByMethod);
        Assert.Equal("Cash", method.Method);
        Assert.Equal(500m, method.Total);
    }

    /// <summary>
    /// The case the screen exists for. Posting is deliberately allowed to fall behind the till,
    /// so a payment can be taken with no journal entry behind it - and the report has to name
    /// which one, using the same definition of "already posted" the catch-up sweep uses.
    /// </summary>
    [Fact]
    public async Task Reconciliation_names_the_payments_that_never_reached_the_ledger()
    {
        using var h = new Harness();

        var member = await h.Members.CreateMemberAsync("Unposted", "Member", "555", "um@example.com");

        // Automatic posting off: the payment is taken, the books are not written. This is the
        // documented behaviour of FinancePostingService, not a contrivance.
        await h.Db.Context.TenantSettings.AddAsync(new TenantSetting
        {
            SettingKey = TenantSettingKeys.AutoPostToLedger,
            Value = "false"
        });
        await h.Db.Context.SaveChangesAsync();

        var payment = await h.Payments.RecordPaymentAsync(
            member.MemberId, subscriptionId: null, saleId: null,
            amount: 250m, paymentDate: new DateTime(2026, 3, 12),
            method: "Cash", referenceNo: "R-2", status: "Completed", notes: "");

        var view = await h.Reports.GetPaymentReconciliationAsync(
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.Equal(250m, view.Takings);
        Assert.Equal(0m, view.PostedToLedger);

        Assert.Equal(250m, view.Variance);
        Assert.False(view.IsBalanced);

        Assert.Equal(1, view.UnpostedPaymentCount);
        Assert.Equal(250m, view.UnpostedPaymentAmount);

        var missing = Assert.Single(view.UnpostedPayments);
        Assert.Equal(payment.PaymentId, missing.PaymentId);
    }

    /// <summary>
    /// Pending money has not arrived, so it is not part of the takings the ledger is checked
    /// against - counting it would report a variance against money nobody ever received.
    /// </summary>
    [Fact]
    public async Task Reconciliation_excludes_money_that_was_never_collected()
    {
        using var h = new Harness();

        var member = await h.Members.CreateMemberAsync("Pending", "Member", "555", "pd@example.com");

        var payment = await h.Payments.RecordPaymentAsync(
            member.MemberId, subscriptionId: null, saleId: null,
            amount: 300m, paymentDate: new DateTime(2026, 3, 14),
            method: "Transfer", referenceNo: "R-3", status: "Completed", notes: "");

        await h.Payments.UpdatePaymentStatusAsync(payment.PaymentId, "Pending");

        var view = await h.Reports.GetPaymentReconciliationAsync(
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.Equal(1, view.PaymentCount);
        Assert.Equal(0m, view.Takings);
        Assert.Equal(300m, view.Pending);

        // Pending money is not owed to the ledger, so nothing is reported as missing.
        Assert.Equal(0, view.UnpostedPaymentCount);
    }
}
