using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Payroll rules.
    ///
    /// Gross and net pay are always computed here and never accepted from a caller. A client
    /// that sent its own totals could pay someone an arbitrary amount, so the request carries
    /// only the inputs.
    /// </summary>
    public class PayrollService : IPayrollService
    {
        private static readonly string[] AllowedStatuses = { "Draft", "Approved", "Paid" };

        /// <summary>The FitCore overtime premium: time and a fifteen percent.</summary>
        public const decimal OvertimeMultiplier = 1.15m;

        private readonly IPayrollRepository _repository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IAttendanceRepository _attendanceRepository;
        private readonly IPayrollDeductionCalculator _deductionCalculator;
        private readonly ICurrentUserAccessor _actor;
        private readonly IAuditService _audit;
        private readonly IFinancePostingService _finance;

        public PayrollService(
            IPayrollRepository repository,
            IEmployeeRepository employeeRepository,
            IAttendanceRepository attendanceRepository,
            IPayrollDeductionCalculator deductionCalculator,
            ICurrentUserAccessor actor,
            IAuditService audit,
            IFinancePostingService finance)
        {
            _repository = repository;
            _employeeRepository = employeeRepository;
            _attendanceRepository = attendanceRepository;
            _deductionCalculator = deductionCalculator;
            _actor = actor;
            _audit = audit;
            _finance = finance;
        }

        // ------------------------------------------------------------------ authorisation
        //
        // Mirrors EmployeeService: enforced here rather than only at the HTTP edge, so the
        // rule holds for every caller. A Manager may create a Draft run - attendance review is
        // theirs - but only an Admin corrects or finalises one.

        private int ActingLevel => ErpRoles.LevelOf(_actor.Current.RoleKey);

        private bool IsAdmin => ActingLevel <= ErpRoles.LevelOf(ErpRoles.Admin);

        private void RequirePayrollAuthority(string verb)
        {
            if (_actor.Current.AppUserId is null) return;

            if (ActingLevel > ErpRoles.LevelOf(ErpRoles.Manager))
            {
                throw new ForbiddenOperationException(
                    $"Your account does not have permission to {verb} payroll.");
            }
        }

        private void RequireAdminFor(string what)
        {
            if (_actor.Current.AppUserId is null) return;

            if (!IsAdmin)
            {
                throw new ForbiddenOperationException($"Only an administrator can {what}.");
            }
        }

        /// <summary>
        /// Who may create, edit or correct a given employee's pay run. Mirrors
        /// EmployeeService.RequireSalaryAuthority exactly: an administrator may act on anyone's;
        /// a manager on anyone's but their own - Staff or a peer Manager alike.
        /// </summary>
        private void RequireSalaryAuthority(Employee target)
        {
            if (_actor.Current.AppUserId is null) return;
            if (IsAdmin) return;

            if (ActingLevel == ErpRoles.LevelOf(ErpRoles.Manager))
            {
                if (_actor.Current.EmployeeId.HasValue &&
                    _actor.Current.EmployeeId.Value == target.EmployeeId)
                {
                    throw new ForbiddenOperationException("You cannot run your own payroll.");
                }

                return;
            }

            throw new ForbiddenOperationException(
                "Only an administrator or manager can run payroll.");
        }

        private static PayrollView ToView(Payroll p) => new()
        {
            PayrollId = p.PayrollId,
            EmployeeId = p.EmployeeId,
            EmployeeCode = p.Employee?.EmployeeCode ?? "",
            EmployeeName = p.Employee == null
                ? $"Employee #{p.EmployeeId}"
                : $"{p.Employee.FirstName} {p.Employee.LastName}".Trim(),
            Position = p.Employee?.Position ?? "",
            PeriodStart = p.PeriodStart,
            PeriodEnd = p.PeriodEnd,
            BasicSalary = p.BasicSalary,
            Allowances = p.Allowances,
            RegularHours = p.RegularHours,
            HourlyRate = p.HourlyRate,
            RegularPay = p.RegularPay,
            OvertimeHours = p.OvertimeHours,
            OvertimeRate = p.OvertimeRate,
            OvertimePay = p.OvertimePay,
            Deductions = p.Deductions,
            SssDeduction = p.SssDeduction,
            PhilHealthDeduction = p.PhilHealthDeduction,
            PagIbigDeduction = p.PagIbigDeduction,
            WithholdingTax = p.WithholdingTax,
            OtherDeductions = p.OtherDeductions,
            GrossPay = p.GrossPay,
            NetPay = p.NetPay,
            SssEmployerShare = p.SssEmployerShare,
            PhilHealthEmployerShare = p.PhilHealthEmployerShare,
            PagIbigEmployerShare = p.PagIbigEmployerShare,
            EmployerContributions = p.EmployerContributions,
            Status = p.Status,
            PaidDate = p.PaidDate,
            ApprovedAt = p.ApprovedAt,
            ApprovedBy = p.ApprovedBy,
            ProcessedByUserId = p.ProcessedByUserId,
            ProcessedBy = string.IsNullOrWhiteSpace(p.ProcessedBy) ? "—" : p.ProcessedBy,
            LastModifiedByUserId = p.LastModifiedByUserId,
            LastModifiedBy = p.LastModifiedBy,
            Notes = p.Notes
        };

        public async Task<List<PayrollView>> GetAllPayrollsAsync()
        {
            var rows = await _repository.GetAllWithEmployeeAsync();
            return rows.Select(ToView).ToList();
        }

        public async Task<PayrollView?> GetPayrollByIdAsync(int id)
        {
            var row = await _repository.GetWithEmployeeAsync(id);
            return row == null ? null : ToView(row);
        }

        public async Task<List<PayrollView>> GetEmployeePayrollsAsync(int employeeId)
        {
            var rows = await _repository.GetForEmployeeAsync(employeeId);
            return rows.Select(ToView).ToList();
        }

        public async Task<PayrollSummary> GetSummaryAsync()
        {
            var rows = await _repository.GetAllWithEmployeeAsync();

            return new PayrollSummary
            {
                RunCount = rows.Count,
                EmployeeCount = rows.Select(r => r.EmployeeId).Distinct().Count(),
                TotalGross = rows.Sum(r => r.GrossPay),
                TotalOvertime = rows.Sum(r => r.OvertimePay),
                TotalDeductions = rows.Sum(r => r.Deductions),
                TotalNet = rows.Sum(r => r.NetPay),
                TotalPaid = rows.Where(r => r.Status == "Paid").Sum(r => r.NetPay),
                TotalOutstanding = rows.Where(r => r.Status != "Paid").Sum(r => r.NetPay),
                TotalEmployerContributions = rows.Sum(r => r.EmployerContributions)
            };
        }

        public async Task<PayrollView> CreatePayrollAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd,
            decimal? basicSalary, decimal allowances, decimal deductions,
            decimal overtimeHours, decimal overtimeRate, string notes)
        {
            RequirePayrollAuthority("create");

            var employee = await RequireEmployeeAsync(employeeId);
            RequireSalaryAuthority(employee);

            ValidatePeriodAndAmounts(
                periodStart, periodEnd, allowances, deductions, overtimeHours, overtimeRate);

            await RequireNoOverlapAsync(employeeId, periodStart, periodEnd);

            // Falls back to the employee's current salary when the caller does not override it.
            var basic = basicSalary ?? employee.BasicSalary;

            if (basic < 0m)
            {
                throw new ValidationException("Basic salary cannot be negative.");
            }

            var overtimePay = overtimeHours * overtimeRate;
            var gross = basic + allowances + overtimePay;

            if (deductions > gross)
            {
                throw new ValidationException("Deductions cannot be greater than gross pay.");
            }

            var actor = _actor.Current;

            var payroll = new Payroll
            {
                EmployeeId = employeeId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                BasicSalary = basic,
                Allowances = allowances,
                OtherDeductions = deductions,
                Deductions = deductions,
                OvertimeHours = overtimeHours,
                OvertimeRate = overtimeRate,
                OvertimePay = overtimePay,
                GrossPay = gross,
                NetPay = gross - deductions,
                Status = "Draft",
                Notes = Clean(notes),

                // Who authorised the run, taken from the token. A payslip has to name the
                // person who prepared it, and it must survive their account being removed.
                ProcessedByUserId = actor.AppUserId,
                ProcessedBy = actor.DisplayName,

                CreatedAt = DateTime.UtcNow
            };

            await _repository.AddAsync(payroll);

            await _audit.RecordAsync(
                AuditActions.PayrollGenerated, ErpModules.Payroll, nameof(Payroll),
                payroll.PayrollId.ToString(),
                $"Manual run for {employee.FirstName} {employee.LastName}, " +
                $"{periodStart:d MMM yyyy} – {periodEnd:d MMM yyyy}, net {payroll.NetPay:N2}.");

            return (await GetPayrollByIdAsync(payroll.PayrollId))!;
        }

        public async Task<PayrollView> GenerateFromAttendanceAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd,
            decimal allowances, decimal otherDeductions, string notes)
        {
            RequirePayrollAuthority("create");

            var employee = await RequireEmployeeAsync(employeeId);
            RequireSalaryAuthority(employee);

            if (periodStart == default || periodEnd == default || periodEnd < periodStart)
            {
                throw new ValidationException(
                    "A valid pay period start and end date are required.");
            }

            if (allowances < 0m)
                throw new ValidationException("Allowances cannot be negative.");

            if (otherDeductions < 0m)
                throw new ValidationException("Other deductions cannot be negative.");

            if (employee.HourlyRate <= 0m)
            {
                throw new ValidationException(
                    $"{employee.FirstName} {employee.LastName} has no hourly rate configured. " +
                    "Set one on the employee record before generating payroll from attendance.");
            }

            var attendance = await _attendanceRepository.GetForPeriodAsync(employeeId, periodStart, periodEnd);

            if (attendance.Count == 0)
            {
                throw new ValidationException(
                    $"No attendance is recorded for {employee.FirstName} {employee.LastName} " +
                    "in this period. Record attendance first, or use manual entry.");
            }

            await RequireNoOverlapAsync(employeeId, periodStart, periodEnd);

            var regularHours = attendance.Sum(a => a.RegularHours);
            var overtimeHours = attendance.Sum(a => a.OvertimeHours);

            var hourlyRate = employee.HourlyRate;
            var overtimeRate = hourlyRate * OvertimeMultiplier;

            var regularPay = regularHours * hourlyRate;
            var overtimePay = overtimeHours * overtimeRate;
            var gross = regularPay + overtimePay + allowances;

            var statutory = _deductionCalculator.Compute(gross);
            var totalDeductions = statutory.Total + otherDeductions;

            // What the gym itself owes on top of gross pay. Never deducted from anyone and
            // never on a payslip as a reduction - it is a cost of employing somebody, which is
            // why the true cost of this run is gross pay plus this and not gross pay alone.
            var employerShare = _deductionCalculator.ComputeEmployerShare(gross);

            if (totalDeductions > gross)
            {
                throw new ValidationException(
                    "Deductions cannot be greater than gross pay. Review allowances or other deductions.");
            }

            var actor = _actor.Current;

            var payroll = new Payroll
            {
                EmployeeId = employeeId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                BasicSalary = 0m,
                Allowances = allowances,
                RegularHours = regularHours,
                HourlyRate = hourlyRate,
                RegularPay = regularPay,
                OvertimeHours = overtimeHours,
                OvertimeRate = overtimeRate,
                OvertimePay = overtimePay,
                GrossPay = gross,
                SssDeduction = statutory.Sss,
                PhilHealthDeduction = statutory.PhilHealth,
                PagIbigDeduction = statutory.PagIbig,
                WithholdingTax = statutory.WithholdingTax,
                OtherDeductions = otherDeductions,
                Deductions = totalDeductions,

                SssEmployerShare = employerShare.Sss,
                PhilHealthEmployerShare = employerShare.PhilHealth,
                PagIbigEmployerShare = employerShare.PagIbig,
                EmployerContributions = employerShare.Total,

                NetPay = gross - totalDeductions,
                Status = "Draft",
                Notes = Clean(notes),
                ProcessedByUserId = actor.AppUserId,
                ProcessedBy = actor.DisplayName,
                CreatedAt = DateTime.UtcNow
            };

            await _repository.AddAsync(payroll);

            await _audit.RecordAsync(
                AuditActions.PayrollGenerated, ErpModules.Payroll, nameof(Payroll),
                payroll.PayrollId.ToString(),
                $"Generated from attendance for {employee.FirstName} {employee.LastName}, " +
                $"{periodStart:d MMM yyyy} – {periodEnd:d MMM yyyy}: {regularHours:0.##}h regular, " +
                $"{overtimeHours:0.##}h overtime, net {payroll.NetPay:N2}.");

            return (await GetPayrollByIdAsync(payroll.PayrollId))!;
        }

        public async Task<PayrollView?> UpdatePayrollAsync(
            int id, DateTime periodStart, DateTime periodEnd,
            decimal basicSalary, decimal allowances, decimal deductions,
            decimal overtimeHours, decimal overtimeRate, string notes)
        {
            var payroll = await _repository.GetByIdAsync(id);
            if (payroll == null) return null;

            RequireSalaryAuthority(await RequireEmployeeAsync(payroll.EmployeeId));

            if (payroll.Status == "Paid")
            {
                throw new ValidationException(
                    "A payroll run that has been paid can no longer be edited.");
            }

            ValidatePeriodAndAmounts(
                periodStart, periodEnd, allowances, deductions, overtimeHours, overtimeRate);

            if (basicSalary < 0m)
            {
                throw new ValidationException("Basic salary cannot be negative.");
            }

            await RequireNoOverlapAsync(payroll.EmployeeId, periodStart, periodEnd, id);

            var overtimePay = overtimeHours * overtimeRate;
            var regularPay = payroll.RegularHours * payroll.HourlyRate;
            var gross = basicSalary + regularPay + allowances + overtimePay;

            if (deductions > gross)
            {
                throw new ValidationException("Deductions cannot be greater than gross pay.");
            }

            var actor = _actor.Current;

            payroll.PeriodStart = periodStart;
            payroll.PeriodEnd = periodEnd;
            payroll.BasicSalary = basicSalary;
            payroll.Allowances = allowances;
            payroll.OtherDeductions = deductions - payroll.SssDeduction - payroll.PhilHealthDeduction
                - payroll.PagIbigDeduction - payroll.WithholdingTax;
            payroll.Deductions = deductions;
            payroll.OvertimeHours = overtimeHours;
            payroll.OvertimeRate = overtimeRate;
            payroll.OvertimePay = overtimePay;
            payroll.GrossPay = gross;
            payroll.NetPay = gross - deductions;
            payroll.Notes = Clean(notes);
            payroll.LastModifiedByUserId = actor.AppUserId;
            payroll.LastModifiedBy = actor.DisplayName;

            await _repository.UpdateAsync(payroll);

            return await GetPayrollByIdAsync(id);
        }

        public async Task<PayrollView?> SetStatusAsync(int id, string status)
        {
            if (!AllowedStatuses.Contains(status))
            {
                throw new ValidationException("Status must be Draft, Approved or Paid.");
            }

            var payroll = await _repository.GetByIdAsync(id);
            if (payroll == null) return null;

            // Moving a run past Draft is finalising it - only an administrator does that.
            if (status != "Draft" || payroll.Status != "Draft")
            {
                RequireAdminFor("change a payroll run's status");
            }

            var actor = _actor.Current;
            var previousStatus = payroll.Status;

            payroll.Status = status;
            payroll.PaidDate = status == "Paid" ? DateTime.UtcNow : null;
            payroll.LastModifiedByUserId = actor.AppUserId;
            payroll.LastModifiedBy = actor.DisplayName;

            if (status == "Approved")
            {
                payroll.ApprovedAt = DateTime.UtcNow;
                payroll.ApprovedByUserId = actor.AppUserId;
                payroll.ApprovedBy = actor.DisplayName;
            }
            else if (status == "Draft")
            {
                payroll.ApprovedAt = null;
                payroll.ApprovedByUserId = null;
                payroll.ApprovedBy = "";
            }

            await _repository.UpdateAsync(payroll);

            await _audit.RecordAsync(
                status == "Paid" ? AuditActions.PayrollPaid : AuditActions.PayrollStatusChanged,
                ErpModules.Payroll, nameof(Payroll), id.ToString(),
                $"{previousStatus} → {status}" +
                (status == "Paid" ? $", net {payroll.NetPay:N2}." : "."));

            // A pay run reaches the books when it is paid, not when it is drafted or approved:
            // until the money leaves, nothing has been spent. Taking a paid run back to Draft
            // reverses it rather than deleting the posting, so the correction stays visible.
            if (status == "Paid" && previousStatus != "Paid")
            {
                await _finance.PostPayrollAsync(id);
            }
            else if (previousStatus == "Paid" && status != "Paid")
            {
                await _finance.ReversePayrollAsync(id, $"Pay run returned to {status}");
            }

            return await GetPayrollByIdAsync(id);
        }

        public async Task<bool> DeletePayrollAsync(int id)
        {
            RequireAdminFor("delete payroll runs");

            var payroll = await _repository.GetByIdAsync(id);
            if (payroll == null) return false;

            if (payroll.Status == "Paid")
            {
                throw new ValidationException(
                    "A payroll run that has been paid cannot be deleted. " +
                    "Set its status back to Draft first if it was recorded in error.");
            }

            return await _repository.DeleteAsync(id);
        }

        private async Task<Employee> RequireEmployeeAsync(int employeeId)
        {
            var employee = await _employeeRepository.GetByIdAsync(employeeId)
                ?? throw new ValidationException($"No employee with id {employeeId} exists.");

            if (employee.Status == "Terminated")
            {
                throw new ValidationException(
                    "A terminated employee cannot be included in a new payroll run.");
            }

            return employee;
        }

        private async Task RequireNoOverlapAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd, int? excludePayrollId = null)
        {
            if (await _repository.PeriodExistsAsync(employeeId, periodStart, periodEnd, excludePayrollId))
            {
                throw new ValidationException(
                    "This employee already has a payroll run overlapping these dates. " +
                    "Paying an overlapping period would pay the same days twice.");
            }
        }

        private static void ValidatePeriodAndAmounts(
            DateTime periodStart, DateTime periodEnd, decimal allowances, decimal deductions,
            decimal overtimeHours, decimal overtimeRate)
        {
            if (periodStart == default || periodEnd == default)
            {
                throw new ValidationException("A pay period start and end date are required.");
            }

            if (periodEnd < periodStart)
            {
                throw new ValidationException(
                    "The pay period end date cannot be before the start date.");
            }

            if (allowances < 0m)
            {
                throw new ValidationException("Allowances cannot be negative.");
            }

            if (deductions < 0m)
            {
                throw new ValidationException("Deductions cannot be negative.");
            }

            if (overtimeHours < 0m)
            {
                throw new ValidationException("Overtime hours cannot be negative.");
            }

            if (overtimeRate < 0m)
            {
                throw new ValidationException("The overtime rate cannot be negative.");
            }

            // Hours without a rate would silently pay nothing for work that was recorded, so
            // the two are required together rather than being quietly ignored.
            if (overtimeHours > 0m && overtimeRate <= 0m)
            {
                throw new ValidationException(
                    "An overtime rate is required when overtime hours are recorded.");
            }
        }

        private static string Clean(string? value) => (value ?? string.Empty).Trim();
    }
}
