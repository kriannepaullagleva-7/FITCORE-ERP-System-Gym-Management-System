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
            // Any overlap, not just an identical pair of dates. Matching the dates exactly let
            // 1-15 March and 10-20 March both be paid, which pays the same six days twice -
            // and that is the mistake worth catching, since the money has already gone out by
            // the time anyone reconciles it.
            //
            // Two periods overlap when each starts before the other ends.
            return await _dbSet.AnyAsync(p =>
                p.EmployeeId == employeeId &&
                p.PeriodStart <= periodEnd &&
                p.PeriodEnd >= periodStart &&
                (excludePayrollId == null || p.PayrollId != excludePayrollId.Value));
        }
    }
}
