using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Shared by Attendance Summary and Payroll Calculation: both need "every active employee's
    /// attendance totals for a period", just for different purposes (reading vs. generating).
    /// </summary>
    internal static class PayrollAttendanceSupport
    {
        public static async Task<List<AttendancePeriodSummaryDto>> LoadSummariesAsync(
            FitCoreSession session, IEnumerable<EmployeeDto> employees, DateTime periodStart, DateTime periodEnd)
        {
            var summaries = new List<AttendancePeriodSummaryDto>();

            foreach (var employee in employees)
            {
                var result = await session.Attendance.GetSummaryAsync(
                    employee.EmployeeId, periodStart, periodEnd);

                if (result.IsSuccess && result.Value is { } summary &&
                    (summary.TotalRegularHours > 0 || summary.TotalOvertimeHours > 0 ||
                     summary.DaysPresent + summary.DaysAbsent + summary.DaysLate + summary.DaysOnLeave > 0))
                {
                    summaries.Add(summary);
                }
            }

            return summaries;
        }
    }
}
