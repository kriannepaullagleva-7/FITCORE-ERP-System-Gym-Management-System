using ERP_domain.entities;

namespace ERP_infrastructure.repositories
{
    public interface IEmployeeRepository : IGenericRepository<Employee>
    {
        Task<List<Employee>> GetActiveEmployeesAsync();
        Task<List<Employee>> SearchAsync(string term);
        Task<bool> CodeExistsAsync(string employeeCode, int? excludeEmployeeId = null);
        Task<int> CountPayrollsAsync(int employeeId);
    }
}
