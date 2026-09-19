using ERP_domain.entities;

namespace ERP_infrastructure.repositories
{
    public interface IPayrollRepository : IGenericRepository<Payroll>
    {
        Task<List<Payroll>> GetAllWithEmployeeAsync();
        Task<Payroll?> GetWithEmployeeAsync(int payrollId);
        Task<List<Payroll>> GetForEmployeeAsync(int employeeId);
        Task<bool> PeriodExistsAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd, int? excludePayrollId = null);
    }
}
