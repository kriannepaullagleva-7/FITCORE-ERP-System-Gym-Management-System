namespace ERP_infrastructure.services
{
    public interface IPayrollService
    {
        Task<List<PayrollView>> GetAllPayrollsAsync();
        Task<PayrollView?> GetPayrollByIdAsync(int id);
        Task<List<PayrollView>> GetEmployeePayrollsAsync(int employeeId);
        Task<PayrollSummary> GetSummaryAsync();

        Task<PayrollView> CreatePayrollAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd,
            decimal? basicSalary, decimal allowances, decimal deductions,
            decimal overtimeHours, decimal overtimeRate, string notes);

        Task<PayrollView?> UpdatePayrollAsync(
            int id, DateTime periodStart, DateTime periodEnd,
            decimal basicSalary, decimal allowances, decimal deductions,
            decimal overtimeHours, decimal overtimeRate, string notes);

        Task<PayrollView?> SetStatusAsync(int id, string status);

        Task<bool> DeletePayrollAsync(int id);
    }
}
