using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    public enum EmployeeDeleteResult
    {
        Deleted,
        NotFound,
        HasPayrollHistory
    }

    /// <summary>
    /// What happened when an employee was given a sign-in. The employee is saved either way,
    /// so this reports on the account only and always carries a sentence for the operator.
    /// </summary>
    public sealed record EmployeeAccountResult(
        EmployeeAccountOutcome Outcome,
        string Message)
    {
        public bool AccountUsable =>
            Outcome is EmployeeAccountOutcome.Created or EmployeeAccountOutcome.AlreadyExisted;

        public static EmployeeAccountResult Created(string message) =>
            new(EmployeeAccountOutcome.Created, message);

        public static EmployeeAccountResult AlreadyExisted(string message) =>
            new(EmployeeAccountOutcome.AlreadyExisted, message);

        public static EmployeeAccountResult Skipped(string message) =>
            new(EmployeeAccountOutcome.Skipped, message);

        public static EmployeeAccountResult Failed(string message) =>
            new(EmployeeAccountOutcome.Failed, message);
    }

    public enum EmployeeAccountOutcome
    {
        Created,
        AlreadyExisted,

        /// <summary>Nothing to do - no email, or no company context.</summary>
        Skipped,

        /// <summary>The employee is saved; the account is not. Safe to retry.</summary>
        Failed
    }

    public interface IEmployeeService
    {
        Task<List<Employee>> GetAllEmployeesAsync();
        Task<List<Employee>> GetActiveEmployeesAsync();
        Task<List<Employee>> SearchEmployeesAsync(string term);
        Task<Employee?> GetEmployeeByIdAsync(int id);

        Task<Employee> CreateEmployeeAsync(
            string employeeCode, string firstName, string lastName, string position,
            string department, string phone, string email, DateTime hireDate, decimal basicSalary,
            decimal hourlyRate = 0m);

        Task<Employee?> UpdateEmployeeAsync(
            int id, string employeeCode, string firstName, string lastName, string position,
            string department, string phone, string email, DateTime hireDate,
            decimal basicSalary, string status, decimal hourlyRate = 0m);

        Task<EmployeeDeleteResult> DeleteEmployeeAsync(int id);

        /// <summary>
        /// Gives this employee a FitCore sign-in, or reports why it could not. Safe to call
        /// again: an account that already exists is detected rather than duplicated.
        /// </summary>
        Task<EmployeeAccountResult> EnsureAccountAsync(
            Employee employee, CancellationToken cancellationToken = default);
    }
}
