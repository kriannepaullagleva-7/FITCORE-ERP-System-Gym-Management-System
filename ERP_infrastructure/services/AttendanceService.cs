using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Attendance rules.
    ///
    /// Attendance is what Payroll Calculation reads instead of a hand-typed hour count, so
    /// hours here are always derived from Time In/Time Out (or zeroed for Absent/Leave) rather
    /// than accepted as a free-standing number.
    /// </summary>
    public class AttendanceService : IAttendanceService
    {
        private static readonly string[] AllowedStatuses = { "Present", "Absent", "Late", "Leave" };

        /// <summary>Hours worked beyond this in a day count as overtime.</summary>
        private const decimal StandardShiftHours = 8m;

        private readonly IAttendanceRepository _repository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly ICurrentUserAccessor _actor;

        public AttendanceService(
            IAttendanceRepository repository,
            IEmployeeRepository employeeRepository,
            ICurrentUserAccessor actor)
        {
            _repository = repository;
            _employeeRepository = employeeRepository;
            _actor = actor;
        }

        private int ActingLevel => ErpRoles.LevelOf(_actor.Current.RoleKey);

        /// <summary>
        /// Recording or correcting attendance is a Manager/Admin action. Staff may see the
        /// module for their own schedule, but changing a record is not theirs to do.
        /// </summary>
        private void RequireAttendanceAuthority(string verb)
        {
            // No signed-in user is the bootstrapper or a test, which is trusted.
            if (_actor.Current.AppUserId is null) return;

            if (ActingLevel > ErpRoles.LevelOf(ErpRoles.Manager))
            {
                throw new ForbiddenOperationException(
                    $"Your account does not have permission to {verb} attendance.");
            }
        }

        private static AttendanceView ToView(Attendance a) => new()
        {
            AttendanceId = a.AttendanceId,
            EmployeeId = a.EmployeeId,
            EmployeeCode = a.Employee?.EmployeeCode ?? "",
            EmployeeName = a.Employee == null
                ? $"Employee #{a.EmployeeId}"
                : $"{a.Employee.FirstName} {a.Employee.LastName}".Trim(),
            Position = a.Employee?.Position ?? "",
            Date = a.Date,
            TimeIn = a.TimeIn,
            TimeOut = a.TimeOut,
            RegularHours = a.RegularHours,
            OvertimeHours = a.OvertimeHours,
            Status = a.Status,
            Notes = a.Notes,
            RecordedByUserId = a.RecordedByUserId,
            RecordedBy = string.IsNullOrWhiteSpace(a.RecordedBy) ? "—" : a.RecordedBy,
            ModifiedByUserId = a.ModifiedByUserId,
            ModifiedBy = a.ModifiedBy
        };

        public async Task<List<AttendanceView>> GetAllAsync(
            int? employeeId = null, DateTime? from = null, DateTime? to = null)
        {
            var rows = await _repository.GetAllWithEmployeeAsync(employeeId, from, to);
            return rows.Select(ToView).ToList();
        }

        public async Task<AttendanceView?> GetByIdAsync(int id)
        {
            var row = await _repository.GetWithEmployeeAsync(id);
            return row == null ? null : ToView(row);
        }

        public async Task<AttendancePeriodSummary> GetPeriodSummaryAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd)
        {
            var employee = await _employeeRepository.GetByIdAsync(employeeId);
            var rows = await _repository.GetForPeriodAsync(employeeId, periodStart, periodEnd);

            return new AttendancePeriodSummary
            {
                EmployeeId = employeeId,
                EmployeeName = employee == null
                    ? $"Employee #{employeeId}"
                    : $"{employee.FirstName} {employee.LastName}".Trim(),
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                DaysPresent = rows.Count(r => r.Status == "Present"),
                DaysAbsent = rows.Count(r => r.Status == "Absent"),
                DaysLate = rows.Count(r => r.Status == "Late"),
                DaysOnLeave = rows.Count(r => r.Status == "Leave"),
                TotalRegularHours = rows.Sum(r => r.RegularHours),
                TotalOvertimeHours = rows.Sum(r => r.OvertimeHours)
            };
        }

        public async Task<AttendanceView> CreateAsync(
            int employeeId, DateTime date, DateTime? timeIn, DateTime? timeOut, string status, string notes)
        {
            RequireAttendanceAuthority("record");

            var employee = await _employeeRepository.GetByIdAsync(employeeId)
                ?? throw new ValidationException($"No employee with id {employeeId} exists.");

            if (employee.Status == "Terminated")
            {
                throw new ValidationException(
                    "A terminated employee cannot have new attendance recorded.");
            }

            var normalisedStatus = RequireStatus(status);

            if (await _repository.GetForEmployeeOnDateAsync(employeeId, date) is not null)
            {
                throw new ValidationException(
                    $"{employee.FirstName} {employee.LastName} already has an attendance record for {date:d}.");
            }

            var (regular, overtime) = ComputeHours(normalisedStatus, timeIn, timeOut);

            var actor = _actor.Current;

            var attendance = new Attendance
            {
                EmployeeId = employeeId,
                Date = date.Date,
                TimeIn = timeIn,
                TimeOut = timeOut,
                RegularHours = regular,
                OvertimeHours = overtime,
                Status = normalisedStatus,
                Notes = Clean(notes),
                RecordedByUserId = actor.AppUserId,
                RecordedBy = actor.DisplayName,
                CreatedAt = DateTime.UtcNow
            };

            await _repository.AddAsync(attendance);

            return (await GetByIdAsync(attendance.AttendanceId))!;
        }

        public async Task<AttendanceView?> UpdateAsync(
            int id, DateTime date, DateTime? timeIn, DateTime? timeOut, string status, string notes)
        {
            RequireAttendanceAuthority("correct");

            var attendance = await _repository.GetByIdAsync(id);
            if (attendance == null) return null;

            var normalisedStatus = RequireStatus(status);

            if (await _repository.GetForEmployeeOnDateAsync(attendance.EmployeeId, date, id) is not null)
            {
                throw new ValidationException(
                    "This employee already has another attendance record for that date.");
            }

            var (regular, overtime) = ComputeHours(normalisedStatus, timeIn, timeOut);

            var actor = _actor.Current;

            attendance.Date = date.Date;
            attendance.TimeIn = timeIn;
            attendance.TimeOut = timeOut;
            attendance.RegularHours = regular;
            attendance.OvertimeHours = overtime;
            attendance.Status = normalisedStatus;
            attendance.Notes = Clean(notes);
            attendance.ModifiedByUserId = actor.AppUserId;
            attendance.ModifiedBy = actor.DisplayName;

            await _repository.UpdateAsync(attendance);

            return await GetByIdAsync(id);
        }

        public async Task<bool> DeleteAsync(int id)
        {
            RequireAttendanceAuthority("remove");
            return await _repository.DeleteAsync(id);
        }

        private static string RequireStatus(string? status)
        {
            var normalised = AllowedStatuses.FirstOrDefault(s =>
                string.Equals(s, (status ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

            if (normalised is null)
            {
                throw new ValidationException("Status must be Present, Absent, Late or Leave.");
            }

            return normalised;
        }

        /// <summary>
        /// Hours are derived, never typed: Absent/Leave carry no hours, and Present/Late split
        /// whatever Time In/Time Out cover into regular (up to the standard shift) and
        /// overtime (the rest).
        /// </summary>
        private static (decimal Regular, decimal Overtime) ComputeHours(
            string status, DateTime? timeIn, DateTime? timeOut)
        {
            if (status is "Absent" or "Leave") return (0m, 0m);

            if (timeIn is null || timeOut is null) return (0m, 0m);

            if (timeOut <= timeIn)
            {
                throw new ValidationException("Time out must be after time in.");
            }

            var worked = (decimal)(timeOut.Value - timeIn.Value).TotalHours;

            var regular = Math.Min(worked, StandardShiftHours);
            var overtime = Math.Max(0m, worked - StandardShiftHours);

            return (Math.Round(regular, 2), Math.Round(overtime, 2));
        }

        private static string Clean(string? value) => (value ?? string.Empty).Trim();
    }
}
