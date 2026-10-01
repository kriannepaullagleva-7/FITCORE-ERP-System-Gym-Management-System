namespace ERP_infrastructure.services
{
    public interface IPayrollService
    {
        Task<List<PayrollView>> GetAllPayrollsAsync();
        Task<PayrollView?> GetPayrollByIdAsync(int id);
        Task<List<PayrollView>> GetEmployeePayrollsAsync(int employeeId);
        Task<PayrollSummary> GetSummaryAsync();

        /// <summary>
        /// Manual creation, kept as the fallback for a period with no attendance recorded yet.
        /// The single <paramref name="deductions"/> figure is stored as OtherDeductions; the
        /// statutory lines are zero for a manually-entered run.
        /// </summary>
        Task<PayrollView> CreatePayrollAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd,
            decimal? basicSalary, decimal allowances, decimal deductions,
            decimal overtimeHours, decimal overtimeRate, string notes);

        /// <summary>
        /// Generates a run from the employee's recorded attendance for the period: regular and
        /// overtime hours come from Attendance, pay comes from the employee's hourly rate, and
        /// the statutory deductions come from the configured reference table.
        /// </summary>
        Task<PayrollView> GenerateFromAttendanceAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd,
            decimal allowances, decimal otherDeductions, string notes);

        Task<PayrollView?> UpdatePayrollAsync(
            int id, DateTime periodStart, DateTime periodEnd,
            decimal basicSalary, decimal allowances, decimal deductions,
            decimal overtimeHours, decimal overtimeRate, string notes);

        Task<PayrollView?> SetStatusAsync(int id, string status);

        Task<bool> DeletePayrollAsync(int id);
    }
}
