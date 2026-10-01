using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.repositories
{
    public class AttendanceRepository : GenericRepository<Attendance>, IAttendanceRepository
    {
        public AttendanceRepository(TenantErpDbContext context) : base(context) { }

        public async Task<List<Attendance>> GetAllWithEmployeeAsync(
            int? employeeId = null, DateTime? from = null, DateTime? to = null)
        {
            var query = _dbSet.AsNoTracking().Include(a => a.Employee).AsQueryable();

            if (employeeId.HasValue)
                query = query.Where(a => a.EmployeeId == employeeId.Value);

            if (from.HasValue)
                query = query.Where(a => a.Date >= from.Value.Date);

            if (to.HasValue)
                query = query.Where(a => a.Date <= to.Value.Date);

            return await query
                .OrderByDescending(a => a.Date)
                .ThenBy(a => a.EmployeeId)
                .ToListAsync();
        }

        public async Task<Attendance?> GetWithEmployeeAsync(int attendanceId)
        {
            return await _dbSet
                .Include(a => a.Employee)
                .FirstOrDefaultAsync(a => a.AttendanceId == attendanceId);
        }

        public async Task<Attendance?> GetForEmployeeOnDateAsync(
            int employeeId, DateTime date, int? excludeAttendanceId = null)
        {
            return await _dbSet.FirstOrDefaultAsync(a =>
                a.EmployeeId == employeeId &&
                a.Date.Date == date.Date &&
                (excludeAttendanceId == null || a.AttendanceId != excludeAttendanceId.Value));
        }

        public async Task<List<Attendance>> GetForPeriodAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd)
        {
            return await _dbSet
                .AsNoTracking()
                .Where(a =>
                    a.EmployeeId == employeeId &&
                    a.Date.Date >= periodStart.Date &&
                    a.Date.Date <= periodEnd.Date)
                .OrderBy(a => a.Date)
                .ToListAsync();
        }
    }
}
