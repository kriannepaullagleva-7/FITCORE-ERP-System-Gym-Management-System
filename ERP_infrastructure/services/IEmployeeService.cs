using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    public enum EmployeeDeleteResult
    {
        Deleted,
        NotFound,
        HasPayrollHistory
    }

    public interface IEmployeeService
    {
        Task<List<Employee>> GetAllEmployeesAsync();
        Task<List<Employee>> GetActiveEmployeesAsync();
        Task<List<Employee>> SearchEmployeesAsync(string term);
        Task<Employee?> GetEmployeeByIdAsync(int id);

        Task<Employee> CreateEmployeeAsync(
            string employeeCode, string firstName, string lastName, string position,
            string department, string phone, string email, DateTime hireDate, decimal basicSalary);

        Task<Employee?> UpdateEmployeeAsync(
            int id, string employeeCode, string firstName, string lastName, string position,
            string department, string phone, string email, DateTime hireDate,
            decimal basicSalary, string status);

        Task<EmployeeDeleteResult> DeleteEmployeeAsync(int id);
    }
}
