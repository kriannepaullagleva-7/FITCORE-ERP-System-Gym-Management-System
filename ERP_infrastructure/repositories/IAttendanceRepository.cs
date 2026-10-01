using ERP_domain.entities;

namespace ERP_infrastructure.repositories
{
    public interface IAttendanceRepository : IGenericRepository<Attendance>
    {
        Task<List<Attendance>> GetAllWithEmployeeAsync(
            int? employeeId = null, DateTime? from = null, DateTime? to = null);

        Task<Attendance?> GetWithEmployeeAsync(int attendanceId);

        Task<Attendance?> GetForEmployeeOnDateAsync(int employeeId, DateTime date, int? excludeAttendanceId = null);

        Task<List<Attendance>> GetForPeriodAsync(int employeeId, DateTime periodStart, DateTime periodEnd);
    }
}
