using ERP_domain.entities;
using ERP_infrastructure.repositories;
using ERP_infrastructure.services;
using Microsoft.Extensions.Options;
using Xunit;

namespace ERP_Tests;

/// <summary>
/// Attendance rules: hours are derived from time in/out rather than typed, recording is a
/// Manager/Admin action, and a day cannot be recorded twice for the same employee.
/// </summary>
public class AttendanceServiceTests
{
    private sealed class Harness : IDisposable
    {
        public TenantDbFixture Db { get; }
        public IEmployeeService Employees { get; }
        public Func<ICurrentUserAccessor, IAttendanceService> AttendanceAs { get; }

        public Harness()
        {
            Db = new TenantDbFixture();
            var employeeRepo = new EmployeeRepository(Db.Context);
            var attendanceRepo = new AttendanceRepository(Db.Context);

            Employees = new EmployeeService(
                employeeRepo, new NullCurrentUserAccessor(), new NoOpUserAccountService());
            AttendanceAs = actor => new AttendanceService(attendanceRepo, employeeRepo, actor);
        }

        public void Dispose() => Db.Dispose();
    }

    private static Task<Employee> SeedEmployeeAsync(Harness h, decimal hourlyRate = 70m) =>
        h.Employees.CreateEmployeeAsync(
            "EMP-1", "Maria", "Santos", EmployeePositions.Staff, "Operations",
            "555-1", "maria@example.com", new DateTime(2026, 1, 15), 0m, hourlyRate);

    private static readonly ICurrentUserAccessor Manager =
        new FakeCurrentUserAccessor(roleKey: ErpRoles.Manager);
    private static readonly ICurrentUserAccessor Admin =
        new FakeCurrentUserAccessor(roleKey: ErpRoles.Admin);
    private static readonly ICurrentUserAccessor Staff =
        new FakeCurrentUserAccessor(roleKey: ErpRoles.Staff);

    [Fact]
    public async Task Regular_hours_are_capped_at_the_standard_shift_and_the_rest_is_overtime()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h);
        var attendance = h.AttendanceAs(Manager);

        var date = new DateTime(2026, 3, 2);
        var row = await attendance.CreateAsync(
            employee.EmployeeId, date,
            timeIn: date.AddHours(8), timeOut: date.AddHours(18), // 10 hours worked
            status: "Present", notes: "");

        Assert.Equal(8m, row.RegularHours);
        Assert.Equal(2m, row.OvertimeHours);
    }

    [Fact]
    public async Task Absent_and_leave_carry_no_hours_even_with_no_times_given()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h);
        var attendance = h.AttendanceAs(Manager);

        var row = await attendance.CreateAsync(
            employee.EmployeeId, new DateTime(2026, 3, 3), null, null, "Absent", "");

        Assert.Equal(0m, row.RegularHours);
        Assert.Equal(0m, row.OvertimeHours);
    }

    [Fact]
    public async Task Manager_and_admin_can_record_attendance()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h);

        var byManager = await h.AttendanceAs(Manager).CreateAsync(
            employee.EmployeeId, new DateTime(2026, 3, 4), null, null, "Present", "");
        Assert.True(byManager.AttendanceId > 0);

        var byAdmin = await h.AttendanceAs(Admin).CreateAsync(
            employee.EmployeeId, new DateTime(2026, 3, 5), null, null, "Present", "");
        Assert.True(byAdmin.AttendanceId > 0);
    }

    [Fact]
    public async Task Staff_cannot_record_attendance()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h);

        await Assert.ThrowsAsync<ForbiddenOperationException>(() =>
            h.AttendanceAs(Staff).CreateAsync(
                employee.EmployeeId, new DateTime(2026, 3, 4), null, null, "Present", ""));
    }

    [Fact]
    public async Task An_employee_cannot_have_two_attendance_rows_on_the_same_day()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h);
        var attendance = h.AttendanceAs(Manager);

        var date = new DateTime(2026, 3, 6);
        await attendance.CreateAsync(employee.EmployeeId, date, null, null, "Present", "");

        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            attendance.CreateAsync(employee.EmployeeId, date, null, null, "Present", ""));

        Assert.Contains("already has", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recorded_by_is_stamped_from_the_signed_in_actor()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h);

        var row = await h.AttendanceAs(Manager).CreateAsync(
            employee.EmployeeId, new DateTime(2026, 3, 7), null, null, "Present", "");

        Assert.Equal("Test User", row.RecordedBy);
    }
}

/// <summary>
/// Payroll generated from attendance: the worked example from the FitCore reference, and the
/// statutory deduction table's cap behaviour.
/// </summary>
public class PayrollFromAttendanceTests
{
    private sealed class Harness : IDisposable
    {
        public TenantDbFixture Db { get; }
        public IEmployeeService Employees { get; }
        public IAttendanceService Attendance { get; }
        public IPayrollService Payroll { get; }

        public Harness()
        {
            Db = new TenantDbFixture();
            var employeeRepo = new EmployeeRepository(Db.Context);
            var payrollRepo = new PayrollRepository(Db.Context);
            var attendanceRepo = new AttendanceRepository(Db.Context);
            var deductionCalculator = new PayrollDeductionCalculator(
                Options.Create(new PayrollDeductionOptions()));

            var actor = new NullCurrentUserAccessor();

            Employees = new EmployeeService(
                employeeRepo, actor, new NoOpUserAccountService());
            Attendance = new AttendanceService(
                attendanceRepo, employeeRepo, new FakeCurrentUserAccessor(roleKey: ErpRoles.Manager));
            Payroll = new PayrollService(
                payrollRepo, employeeRepo, attendanceRepo, deductionCalculator, actor,
                new TenantAuditService(Db.Context, actor), Db.Finance);
        }

        public void Dispose() => Db.Dispose();
    }

    private static Task<Employee> SeedEmployeeAsync(Harness h, decimal hourlyRate) =>
        h.Employees.CreateEmployeeAsync(
            "EMP-1", "Juan", "Dela Cruz", EmployeePositions.Staff, "Operations",
            "555-1", "juan@example.com", new DateTime(2026, 1, 15), 0m, hourlyRate);

    /// <summary>
    /// Seeds one attendance row per day so the period totals come to the requested regular
    /// and overtime hours, spread across enough days that no single day looks implausible.
    /// </summary>
    private static async Task SeedAttendanceAsync(
        Harness h, int employeeId, DateTime periodStart, decimal totalRegularHours, decimal totalOvertimeHours)
    {
        var days = (int)Math.Ceiling(totalRegularHours / 8m);
        var remainingRegular = totalRegularHours;
        var remainingOvertime = totalOvertimeHours;

        for (var i = 0; i < days; i++)
        {
            var regularToday = Math.Min(8m, remainingRegular);
            var overtimeToday = i == days - 1 ? remainingOvertime : 0m;
            remainingRegular -= regularToday;

            var date = periodStart.AddDays(i);
            var timeIn = date.AddHours(8);
            var timeOut = timeIn.AddHours((double)(regularToday + overtimeToday));

            await h.Attendance.CreateAsync(employeeId, date, timeIn, timeOut, "Present", "");
        }
    }

    [Fact]
    public async Task Reproduces_the_reference_worked_example()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h, hourlyRate: 70m);

        var periodStart = new DateTime(2026, 3, 2);
        var periodEnd = periodStart.AddDays(21);
        await SeedAttendanceAsync(h, employee.EmployeeId, periodStart, 176m, 10m);

        var run = await h.Payroll.GenerateFromAttendanceAsync(
            employee.EmployeeId, periodStart, periodEnd, allowances: 0m, otherDeductions: 0m, notes: "");

        Assert.Equal(176m, run.RegularHours);
        Assert.Equal(10m, run.OvertimeHours);
        Assert.Equal(12320m, run.RegularPay);
        Assert.Equal(80.5m, run.OvertimeRate);
        Assert.Equal(805m, run.OvertimePay);
        Assert.Equal(13125m, run.GrossPay);
    }

    [Fact]
    public async Task Generating_without_any_attendance_recorded_is_refused()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h, hourlyRate: 70m);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => h.Payroll.GenerateFromAttendanceAsync(
            employee.EmployeeId, new DateTime(2026, 3, 1), new DateTime(2026, 3, 15), 0m, 0m, ""));

        Assert.Contains("attendance", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generating_for_an_employee_with_no_hourly_rate_is_refused()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h, hourlyRate: 0m);

        var periodStart = new DateTime(2026, 3, 2);
        await SeedAttendanceAsync(h, employee.EmployeeId, periodStart, 8m, 0m);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => h.Payroll.GenerateFromAttendanceAsync(
            employee.EmployeeId, periodStart, periodStart.AddDays(1), 0m, 0m, ""));

        Assert.Contains("hourly rate", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A manager runs and corrects both Staff-level and a fellow Manager's payroll - the rule is
    /// "not your own", not "not anyone senior to Staff" - but never their own run, even when it
    /// happens to be a Staff-level record. An administrator can edit anyone's.
    /// </summary>
    [Fact]
    public async Task A_manager_edits_staff_and_peer_payroll_but_not_their_own()
    {
        using var h = new Harness();
        var staffEmployee = await SeedEmployeeAsync(h, hourlyRate: 70m);

        var managerEmployee = await h.Employees.CreateEmployeeAsync(
            "EMP-2", "Grace", "Bautista", EmployeePositions.Manager, "Operations",
            "555-2", "grace@example.com", new DateTime(2026, 1, 15), 40000m);

        var periodStart = new DateTime(2026, 3, 2);
        await SeedAttendanceAsync(h, staffEmployee.EmployeeId, periodStart, 40m, 0m);

        var staffRun = await h.Payroll.GenerateFromAttendanceAsync(
            staffEmployee.EmployeeId, periodStart, periodStart.AddDays(6), 0m, 0m, "");

        var managerRun = await h.Payroll.CreatePayrollAsync(
            managerEmployee.EmployeeId, periodStart, periodStart.AddDays(6),
            managerEmployee.BasicSalary, 0m, 0m, 0m, 0m, "");

        var payrollRepo = new PayrollRepository(h.Db.Context);
        var employeeRepo = new EmployeeRepository(h.Db.Context);
        var attendanceRepo = new AttendanceRepository(h.Db.Context);
        var calculator = new PayrollDeductionCalculator(Options.Create(new PayrollDeductionOptions()));

        var managerActor = new FakeCurrentUserAccessor(roleKey: ErpRoles.Manager);
        var managerActingOnOwnRecord =
            new FakeCurrentUserAccessor(roleKey: ErpRoles.Manager, employeeId: managerEmployee.EmployeeId);
        var adminActor = new FakeCurrentUserAccessor(roleKey: ErpRoles.Admin);

        IPayrollService PayrollAs(ICurrentUserAccessor actor) => new PayrollService(
            payrollRepo, employeeRepo, attendanceRepo, calculator, actor,
            new TenantAuditService(h.Db.Context, actor), h.Db.Finance);

        // A manager may correct the Staff run.
        var updated = await PayrollAs(managerActor).UpdatePayrollAsync(
            staffRun.PayrollId, staffRun.PeriodStart, staffRun.PeriodEnd, staffRun.BasicSalary,
            100m, 0m, 0m, 0m, "");

        Assert.NotNull(updated);
        Assert.Equal(100m, updated!.Allowances);

        // A manager may also correct a peer manager's run - the rule is "not your own", not
        // "not anyone senior to Staff".
        var peerEdit = await PayrollAs(managerActor).UpdatePayrollAsync(
            managerRun.PayrollId, managerRun.PeriodStart, managerRun.PeriodEnd, managerRun.BasicSalary,
            150m, 0m, 0m, 0m, "");

        Assert.NotNull(peerEdit);
        Assert.Equal(150m, peerEdit!.Allowances);

        // Nor their own, even though it is otherwise an ordinary manager-level run.
        await Assert.ThrowsAsync<ForbiddenOperationException>(
            () => PayrollAs(managerActingOnOwnRecord).UpdatePayrollAsync(
                managerRun.PayrollId, managerRun.PeriodStart, managerRun.PeriodEnd, managerRun.BasicSalary,
                100m, 0m, 0m, 0m, ""));

        // An administrator can edit either.
        var adminEdit = await PayrollAs(adminActor).UpdatePayrollAsync(
            managerRun.PayrollId, managerRun.PeriodStart, managerRun.PeriodEnd, managerRun.BasicSalary,
            250m, 0m, 0m, 0m, "");

        Assert.NotNull(adminEdit);
        Assert.Equal(250m, adminEdit!.Allowances);
    }
}

/// <summary>Pins down the statutory deduction table's cap behaviour against known salary bands.</summary>
public class PayrollDeductionCalculatorTests
{
    private static readonly IPayrollDeductionCalculator Calculator =
        new PayrollDeductionCalculator(Options.Create(new PayrollDeductionOptions()));

    [Fact]
    public void Sss_is_capped_at_the_configured_monthly_maximum()
    {
        // Above the MSC ceiling of 35,000, so SSS is capped at 1,750 rather than scaling further.
        var result = Calculator.Compute(60000m);
        Assert.Equal(1750m, result.Sss);
    }

    [Fact]
    public void PhilHealth_is_capped_at_the_configured_monthly_maximum()
    {
        var result = Calculator.Compute(150000m);
        Assert.Equal(2500m, result.PhilHealth);
    }

    [Fact]
    public void PagIbig_is_capped_at_the_configured_monthly_maximum()
    {
        var result = Calculator.Compute(50000m);
        Assert.Equal(200m, result.PagIbig);
    }

    [Fact]
    public void Annual_taxable_compensation_at_or_below_250000_owes_no_withholding_tax()
    {
        // 20,000 a month nets to well under 250,000 a year even before the mandatory
        // contributions are taken off, so withholding tax must be zero.
        var result = Calculator.Compute(20000m);
        Assert.Equal(0m, result.WithholdingTax);
    }

    [Fact]
    public void Withholding_tax_applies_once_annual_taxable_compensation_passes_250000()
    {
        var result = Calculator.Compute(40000m);
        Assert.True(result.WithholdingTax > 0m);
    }

    /// <summary>
    /// Every figure has to be payable in centavos, because every one of them is stored in a
    /// decimal(18,2) column and printed on a payslip.
    ///
    /// A gross of 15,725 is the case that found this: PhilHealth at 2.5% is 393.125, and left
    /// unrounded it became 393.13 in the column while the total it was summed into rounded
    /// separately. The payslip then showed deductions that did not add up to their own total,
    /// net pay a centavo away from gross minus deductions, and a payroll journal entry whose
    /// two sides differed by that centavo - so it was refused, silently, and a paid run reached
    /// nobody's books.
    /// </summary>
    [Theory]
    [InlineData(15725)]   // PhilHealth lands exactly on half a centavo
    [InlineData(12345.67)]
    [InlineData(23456.78)]
    [InlineData(31111.11)]
    [InlineData(8333.33)]
    [InlineData(45678.91)]
    public void Every_deduction_is_a_whole_number_of_centavos(decimal gross)
    {
        var employee = Calculator.Compute(gross);
        var employer = Calculator.ComputeEmployerShare(gross);

        foreach (var amount in new[]
                 {
                     employee.Sss, employee.PhilHealth, employee.PagIbig, employee.WithholdingTax,
                     employer.Sss, employer.PhilHealth, employer.PagIbig
                 })
        {
            Assert.Equal(amount, Math.Round(amount, 2));
        }

        // And the totals are the sums of exactly those figures, not of their unrounded
        // originals - otherwise the payslip disagrees with itself.
        Assert.Equal(
            employee.Sss + employee.PhilHealth + employee.PagIbig + employee.WithholdingTax,
            employee.Total);

        Assert.Equal(employer.Sss + employer.PhilHealth + employer.PagIbig, employer.Total);
    }

    /// <summary>
    /// The arithmetic the payroll journal entry depends on. Its debits are the salary cost plus
    /// the employer's contributions; its credits are the statutory liabilities plus net pay.
    /// Those two sides only agree if net pay is gross less exactly the deductions withheld, so
    /// this is the ledger's balance condition expressed in payroll's own terms.
    /// </summary>
    [Theory]
    [InlineData(15725)]
    [InlineData(12345.67)]
    [InlineData(45678.91)]
    public void Net_pay_is_gross_less_exactly_the_deductions_withheld(decimal gross)
    {
        var d = Calculator.Compute(gross);

        var net = gross - d.Total;
        var creditSide = d.Sss + d.PhilHealth + d.PagIbig + d.WithholdingTax + net;

        Assert.Equal(gross, creditSide);
        Assert.Equal(net, Math.Round(net, 2));
    }
}
