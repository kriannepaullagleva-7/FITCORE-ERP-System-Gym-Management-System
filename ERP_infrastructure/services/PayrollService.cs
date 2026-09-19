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

        private readonly IPayrollRepository _repository;
        private readonly IEmployeeRepository _employeeRepository;

        public PayrollService(
            IPayrollRepository repository,
            IEmployeeRepository employeeRepository)
        {
            _repository = repository;
            _employeeRepository = employeeRepository;
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
            OvertimeHours = p.OvertimeHours,
            OvertimeRate = p.OvertimeRate,
            OvertimePay = p.OvertimePay,
            Deductions = p.Deductions,
            GrossPay = p.GrossPay,
            NetPay = p.NetPay,
            Status = p.Status,
            PaidDate = p.PaidDate,
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
                TotalOutstanding = rows.Where(r => r.Status != "Paid").Sum(r => r.NetPay)
            };
        }

        public async Task<PayrollView> CreatePayrollAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd,
            decimal? basicSalary, decimal allowances, decimal deductions,
            decimal overtimeHours, decimal overtimeRate, string notes)
        {
            var employee = await _employeeRepository.GetByIdAsync(employeeId)
                ?? throw new ValidationException($"No employee with id {employeeId} exists.");

            if (employee.Status == "Terminated")
            {
                throw new ValidationException(
                    "A terminated employee cannot be included in a new payroll run.");
            }

            ValidatePeriodAndAmounts(
                periodStart, periodEnd, allowances, deductions, overtimeHours, overtimeRate);

            if (await _repository.PeriodExistsAsync(employeeId, periodStart, periodEnd))
            {
                throw new ValidationException(
                    "A payroll run already exists for this employee and period.");
            }

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

            var payroll = new Payroll
            {
                EmployeeId = employeeId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                BasicSalary = basic,
                Allowances = allowances,
                Deductions = deductions,
                OvertimeHours = overtimeHours,
                OvertimeRate = overtimeRate,
                OvertimePay = overtimePay,
                GrossPay = gross,
                NetPay = gross - deductions,
                Status = "Draft",
                Notes = Clean(notes),
                CreatedAt = DateTime.UtcNow
            };

            await _repository.AddAsync(payroll);

            return (await GetPayrollByIdAsync(payroll.PayrollId))!;
        }

        public async Task<PayrollView?> UpdatePayrollAsync(
            int id, DateTime periodStart, DateTime periodEnd,
            decimal basicSalary, decimal allowances, decimal deductions,
            decimal overtimeHours, decimal overtimeRate, string notes)
        {
            var payroll = await _repository.GetByIdAsync(id);
            if (payroll == null) return null;

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

            if (await _repository.PeriodExistsAsync(payroll.EmployeeId, periodStart, periodEnd, id))
            {
                throw new ValidationException(
                    "Another payroll run already covers this employee and period.");
            }

            var overtimePay = overtimeHours * overtimeRate;
            var gross = basicSalary + allowances + overtimePay;

            if (deductions > gross)
            {
                throw new ValidationException("Deductions cannot be greater than gross pay.");
            }

            payroll.PeriodStart = periodStart;
            payroll.PeriodEnd = periodEnd;
            payroll.BasicSalary = basicSalary;
            payroll.Allowances = allowances;
            payroll.Deductions = deductions;
            payroll.OvertimeHours = overtimeHours;
            payroll.OvertimeRate = overtimeRate;
            payroll.OvertimePay = overtimePay;
            payroll.GrossPay = gross;
            payroll.NetPay = gross - deductions;
            payroll.Notes = Clean(notes);

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

            payroll.Status = status;
            payroll.PaidDate = status == "Paid" ? DateTime.UtcNow : null;

            await _repository.UpdateAsync(payroll);

            return await GetPayrollByIdAsync(id);
        }

        public async Task<bool> DeletePayrollAsync(int id)
        {
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
