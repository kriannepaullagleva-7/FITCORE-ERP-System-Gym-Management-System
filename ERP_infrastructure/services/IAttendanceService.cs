namespace ERP_infrastructure.services
{
    public interface IAttendanceService
    {
        Task<List<AttendanceView>> GetAllAsync(
            int? employeeId = null, DateTime? from = null, DateTime? to = null);

        Task<AttendanceView?> GetByIdAsync(int id);

        /// <summary>Aggregated regular/overtime hours for one employee over one period, the input to payroll.</summary>
        Task<AttendancePeriodSummary> GetPeriodSummaryAsync(int employeeId, DateTime periodStart, DateTime periodEnd);

        Task<AttendanceView> CreateAsync(
            int employeeId, DateTime date, DateTime? timeIn, DateTime? timeOut, string status, string notes);

        Task<AttendanceView?> UpdateAsync(
            int id, DateTime date, DateTime? timeIn, DateTime? timeOut, string status, string notes);

        Task<bool> DeleteAsync(int id);
    }
}
