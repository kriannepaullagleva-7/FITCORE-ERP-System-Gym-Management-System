using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    public class EmployeeService : IEmployeeService
    {
        private static readonly string[] AllowedStatuses = { "Active", "Inactive", "Terminated" };

        private readonly IEmployeeRepository _repository;

        public EmployeeService(IEmployeeRepository repository)
        {
            _repository = repository;
        }

        public Task<List<Employee>> GetAllEmployeesAsync() => _repository.GetAllAsync();

        public Task<List<Employee>> GetActiveEmployeesAsync() => _repository.GetActiveEmployeesAsync();

        public Task<List<Employee>> SearchEmployeesAsync(string term) => _repository.SearchAsync(term);

        public Task<Employee?> GetEmployeeByIdAsync(int id) => _repository.GetByIdAsync(id);

        public async Task<Employee> CreateEmployeeAsync(
            string employeeCode, string firstName, string lastName, string position,
            string department, string phone, string email, DateTime hireDate, decimal basicSalary)
        {
            var code = Clean(employeeCode);

            ValidateCore(code, firstName, lastName, basicSalary);

            if (await _repository.CodeExistsAsync(code))
            {
                throw new ValidationException($"Employee code '{code}' is already in use.");
            }

            return await _repository.AddAsync(new Employee
            {
                EmployeeCode = code,
                FirstName = Clean(firstName),
                LastName = Clean(lastName),
                Position = Clean(position),
                Department = Clean(department),
                Phone = Clean(phone),
                Email = Clean(email),
                HireDate = hireDate == default ? DateTime.UtcNow : hireDate,
                BasicSalary = basicSalary,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            });
        }

        public async Task<Employee?> UpdateEmployeeAsync(
            int id, string employeeCode, string firstName, string lastName, string position,
            string department, string phone, string email, DateTime hireDate,
            decimal basicSalary, string status)
        {
            var employee = await _repository.GetByIdAsync(id);
            if (employee == null) return null;

            var code = Clean(employeeCode);

            ValidateCore(code, firstName, lastName, basicSalary);

            if (!AllowedStatuses.Contains(status))
            {
                throw new ValidationException(
                    "Status must be Active, Inactive or Terminated.");
            }

            if (await _repository.CodeExistsAsync(code, id))
            {
                throw new ValidationException($"Employee code '{code}' is already in use.");
            }

            employee.EmployeeCode = code;
            employee.FirstName = Clean(firstName);
            employee.LastName = Clean(lastName);
            employee.Position = Clean(position);
            employee.Department = Clean(department);
            employee.Phone = Clean(phone);
            employee.Email = Clean(email);
            employee.HireDate = hireDate == default ? employee.HireDate : hireDate;
            employee.BasicSalary = basicSalary;
            employee.Status = status;

            return await _repository.UpdateAsync(employee);
        }

        public async Task<EmployeeDeleteResult> DeleteEmployeeAsync(int id)
        {
            var employee = await _repository.GetByIdAsync(id);
            if (employee == null) return EmployeeDeleteResult.NotFound;

            // Pay history is a financial record. Rather than destroy it, the caller is told to
            // mark the employee Inactive instead.
            if (await _repository.CountPayrollsAsync(id) > 0)
            {
                return EmployeeDeleteResult.HasPayrollHistory;
            }

            await _repository.DeleteAsync(id);
            return EmployeeDeleteResult.Deleted;
        }

        private static void ValidateCore(
            string code, string firstName, string lastName, decimal basicSalary)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ValidationException("An employee code is required.");

            if (string.IsNullOrWhiteSpace(firstName))
                throw new ValidationException("A first name is required.");

            if (string.IsNullOrWhiteSpace(lastName))
                throw new ValidationException("A last name is required.");

            if (basicSalary < 0m)
                throw new ValidationException("Basic salary cannot be negative.");
        }

        private static string Clean(string? value) => (value ?? string.Empty).Trim();
    }
}
