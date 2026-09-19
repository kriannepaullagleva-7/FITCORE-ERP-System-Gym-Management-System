using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.repositories
{
    public class EmployeeRepository : GenericRepository<Employee>, IEmployeeRepository
    {
        public EmployeeRepository(TenantErpDbContext context) : base(context) { }

        public override async Task<List<Employee>> GetAllAsync()
        {
            return await _dbSet
                .AsNoTracking()
                .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
                .ToListAsync();
        }

        public async Task<List<Employee>> GetActiveEmployeesAsync()
        {
            return await _dbSet
                .AsNoTracking()
                .Where(e => e.Status == "Active")
                .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
                .ToListAsync();
        }

        public async Task<List<Employee>> SearchAsync(string term)
        {
            var query = _dbSet.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(term))
            {
                var t = term.Trim();
                query = query.Where(e =>
                    e.FirstName.Contains(t) ||
                    e.LastName.Contains(t) ||
                    e.EmployeeCode.Contains(t) ||
                    e.Position.Contains(t) ||
                    e.Department.Contains(t) ||
                    e.Email.Contains(t) ||
                    e.Phone.Contains(t));
            }

            return await query
                .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
                .ToListAsync();
        }

        public async Task<bool> CodeExistsAsync(string employeeCode, int? excludeEmployeeId = null)
        {
            var code = (employeeCode ?? string.Empty).Trim();

            return await _dbSet.AnyAsync(e =>
                e.EmployeeCode == code &&
                (excludeEmployeeId == null || e.EmployeeId != excludeEmployeeId.Value));
        }

        public async Task<int> CountPayrollsAsync(int employeeId)
        {
            return await _context.Payrolls.CountAsync(p => p.EmployeeId == employeeId);
        }
    }
}
