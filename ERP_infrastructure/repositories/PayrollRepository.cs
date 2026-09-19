using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.repositories
{
    public class PayrollRepository : GenericRepository<Payroll>, IPayrollRepository
    {
        public PayrollRepository(TenantErpDbContext context) : base(context) { }

        public async Task<List<Payroll>> GetAllWithEmployeeAsync()
        {
            return await _dbSet
                .AsNoTracking()
                .Include(p => p.Employee)
                .OrderByDescending(p => p.PeriodStart)
                .ThenBy(p => p.PayrollId)
                .ToListAsync();
        }

        public async Task<Payroll?> GetWithEmployeeAsync(int payrollId)
        {
            return await _dbSet
                .AsNoTracking()
                .Include(p => p.Employee)
                .FirstOrDefaultAsync(p => p.PayrollId == payrollId);
        }

        public async Task<List<Payroll>> GetForEmployeeAsync(int employeeId)
        {
            return await _dbSet
                .AsNoTracking()
                .Include(p => p.Employee)
                .Where(p => p.EmployeeId == employeeId)
                .OrderByDescending(p => p.PeriodStart)
                .ToListAsync();
        }

        public async Task<bool> PeriodExistsAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd, int? excludePayrollId = null)
        {
            // Two runs for the same employee covering the same period is almost always a
            // double entry, so the service refuses it.
            return await _dbSet.AnyAsync(p =>
                p.EmployeeId == employeeId &&
                p.PeriodStart == periodStart &&
                p.PeriodEnd == periodEnd &&
                (excludePayrollId == null || p.PayrollId != excludePayrollId.Value));
        }
    }
}
