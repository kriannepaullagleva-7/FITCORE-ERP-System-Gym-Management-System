using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class LeaveRequestView
    {
        public int LeaveRequestId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string LeaveType { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal Days { get; set; }
        public bool IsPaid { get; set; }
        public string Reason { get; set; } = "";
        public string Status { get; set; } = "";
        public string RequestedBy { get; set; } = "";
        public DateTime? DecidedAt { get; set; }
        public string DecidedBy { get; set; } = "";
        public string DecisionNotes { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>What one employee has taken and has pending, over a year.</summary>
    public class LeaveBalanceView
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public int Year { get; set; }
        public decimal PaidDaysTaken { get; set; }
        public decimal UnpaidDaysTaken { get; set; }
        public decimal PendingDays { get; set; }
        public decimal TotalDaysTaken => PaidDaysTaken + UnpaidDaysTaken;
        public List<CategorySlice> ByType { get; set; } = new();
    }

    /// <summary>
    /// Leave: time an employee is away, and whether they are paid for it.
    ///
    /// Belongs to Employee Management rather than Payroll, because granting it is a staffing
    /// decision. Payroll only reads the outcome - approved paid leave counts towards the hours
    /// a run is calculated from, approved unpaid leave does not.
    /// </summary>
    public interface ILeaveService
    {
        Task<List<LeaveRequestView>> GetRequestsAsync(
            int? employeeId = null, DateTime? fromUtc = null, DateTime? toUtc = null,
            string? status = null);

        Task<LeaveRequestView?> GetRequestAsync(int leaveRequestId);

        Task<LeaveBalanceView> GetBalanceAsync(int employeeId, int? year = null);

        Task<LeaveRequestView> CreateRequestAsync(
            int employeeId, string leaveType, DateTime startDate, DateTime endDate,
            bool? isPaid, string reason);

        Task<LeaveRequestView?> UpdateRequestAsync(
            int leaveRequestId, string leaveType, DateTime startDate, DateTime endDate,
            bool isPaid, string reason);

        Task<LeaveRequestView?> DecideAsync(int leaveRequestId, bool approve, string notes);

        Task<LeaveRequestView?> CancelAsync(int leaveRequestId, string reason);

        Task<bool> DeleteAsync(int leaveRequestId);

        /// <summary>Approved paid leave days inside a pay period, which payroll counts as worked.</summary>
        Task<decimal> GetPaidLeaveDaysAsync(int employeeId, DateTime periodStart, DateTime periodEnd);
    }

    public class LeaveService : ILeaveService
    {
        private readonly TenantErpDbContext _context;
        private readonly ICurrentUserAccessor _actor;
        private readonly IAuditService _audit;

        public LeaveService(
            TenantErpDbContext context, ICurrentUserAccessor actor, IAuditService audit)
        {
            _context = context;
            _actor = actor;
            _audit = audit;
        }

        public async Task<List<LeaveRequestView>> GetRequestsAsync(
            int? employeeId = null, DateTime? fromUtc = null, DateTime? toUtc = null,
            string? status = null)
        {
            var query = _context.LeaveRequests
                .AsNoTracking()
                .Include(r => r.Employee)
                .AsQueryable();

            if (employeeId.HasValue) query = query.Where(r => r.EmployeeId == employeeId.Value);

            // Overlap rather than containment: a request that starts before the window and
            // ends inside it is still leave taken during that window.
            if (fromUtc.HasValue) query = query.Where(r => r.EndDate >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(r => r.StartDate < toUtc.Value);

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.Status == status);
            }

            var rows = await query
                .OrderByDescending(r => r.StartDate)
                .ThenByDescending(r => r.LeaveRequestId)
                .ToListAsync();

            return rows.Select(ToView).ToList();
        }

        public async Task<LeaveRequestView?> GetRequestAsync(int leaveRequestId)
        {
            var request = await _context.LeaveRequests
                .AsNoTracking()
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.LeaveRequestId == leaveRequestId);

            return request is null ? null : ToView(request);
        }

        public async Task<LeaveBalanceView> GetBalanceAsync(int employeeId, int? year = null)
        {
            var employee = await RequireEmployeeAsync(employeeId);

            var target = year ?? DateTime.UtcNow.Year;
            var from = new DateTime(target, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddYears(1);

            var rows = await _context.LeaveRequests
                .AsNoTracking()
                .Where(r => r.EmployeeId == employeeId &&
                            r.StartDate < to && r.EndDate >= from &&
                            r.Status != LeaveStatuses.Rejected &&
                            r.Status != LeaveStatuses.Cancelled)
                .Select(r => new { r.Status, r.IsPaid, r.Days, r.LeaveType })
                .ToListAsync();

            var view = new LeaveBalanceView
            {
                EmployeeId = employeeId,
                EmployeeName = $"{employee.FirstName} {employee.LastName}".Trim(),
                Year = target,
                PaidDaysTaken = rows
                    .Where(r => r.Status == LeaveStatuses.Approved && r.IsPaid)
                    .Sum(r => r.Days),
                UnpaidDaysTaken = rows
                    .Where(r => r.Status == LeaveStatuses.Approved && !r.IsPaid)
                    .Sum(r => r.Days),
                PendingDays = rows
                    .Where(r => r.Status == LeaveStatuses.Pending)
                    .Sum(r => r.Days)
            };

            view.ByType = rows
                .Where(r => r.Status == LeaveStatuses.Approved)
                .GroupBy(r => r.LeaveType)
                .Select(g => new CategorySlice
                {
                    Label = g.Key,
                    Value = g.Sum(r => r.Days),
                    Count = g.Count()
                })
                .OrderByDescending(s => s.Value)
                .ToList();

            return view;
        }

        public async Task<LeaveRequestView> CreateRequestAsync(
            int employeeId, string leaveType, DateTime startDate, DateTime endDate,
            bool? isPaid, string reason)
        {
            var employee = await RequireEmployeeAsync(employeeId);

            var type = LeaveTypes.Normalise(leaveType)
                ?? throw new ValidationException(
                    $"The leave type must be one of: {string.Join(", ", LeaveTypes.All)}.");

            ValidateDates(startDate, endDate);

            await RequireNoOverlapAsync(employeeId, startDate, endDate, null);

            var actor = _actor.Current;

            var request = new LeaveRequest
            {
                EmployeeId = employeeId,
                LeaveType = type,
                StartDate = startDate.Date,
                EndDate = endDate.Date,
                Days = WorkingDaysBetween(startDate, endDate),

                // Defaulted from the type but held on the request, because a gym can run out
                // of an employee's paid allocation halfway through an absence and the rest of
                // that same absence is unpaid.
                IsPaid = isPaid ?? LeaveTypes.IsPaidByDefault(type),

                Reason = Clean(reason),
                Status = LeaveStatuses.Pending,
                RequestedByUserId = actor.AppUserId,
                RequestedBy = actor.DisplayName,
                CreatedAt = DateTime.UtcNow
            };

            _context.LeaveRequests.Add(request);
            await _context.SaveChangesAsync();

            await _audit.RecordAsync(
                AuditActions.Create, ErpModules.Employees, nameof(LeaveRequest),
                request.LeaveRequestId.ToString(),
                $"{employee.FirstName} {employee.LastName}: {type.ToLowerInvariant()} leave, " +
                $"{request.Days:0.##} day(s) from {request.StartDate:d MMM yyyy}.");

            return (await GetRequestAsync(request.LeaveRequestId))!;
        }

        public async Task<LeaveRequestView?> UpdateRequestAsync(
            int leaveRequestId, string leaveType, DateTime startDate, DateTime endDate,
            bool isPaid, string reason)
        {
            var request = await _context.LeaveRequests
                .FirstOrDefaultAsync(r => r.LeaveRequestId == leaveRequestId);

            if (request is null) return null;

            if (!string.Equals(request.Status, LeaveStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"This request has been {request.Status.ToLowerInvariant()} and cannot be changed. " +
                    "Cancel it and raise a new one if the dates have moved.");
            }

            var type = LeaveTypes.Normalise(leaveType)
                ?? throw new ValidationException(
                    $"The leave type must be one of: {string.Join(", ", LeaveTypes.All)}.");

            ValidateDates(startDate, endDate);

            await RequireNoOverlapAsync(request.EmployeeId, startDate, endDate, leaveRequestId);

            request.LeaveType = type;
            request.StartDate = startDate.Date;
            request.EndDate = endDate.Date;
            request.Days = WorkingDaysBetween(startDate, endDate);
            request.IsPaid = isPaid;
            request.Reason = Clean(reason);

            await _context.SaveChangesAsync();

            return await GetRequestAsync(leaveRequestId);
        }

        public async Task<LeaveRequestView?> DecideAsync(int leaveRequestId, bool approve, string notes)
        {
            var request = await _context.LeaveRequests
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.LeaveRequestId == leaveRequestId);

            if (request is null) return null;

            if (!string.Equals(request.Status, LeaveStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"This request has already been {request.Status.ToLowerInvariant()}.");
            }

            if (!approve && string.IsNullOrWhiteSpace(notes))
            {
                throw new ValidationException(
                    "Say why the request is being refused, so the employee is told something.");
            }

            var actor = _actor.Current;

            request.Status = approve ? LeaveStatuses.Approved : LeaveStatuses.Rejected;
            request.DecidedAt = DateTime.UtcNow;
            request.DecidedByUserId = actor.AppUserId;
            request.DecidedBy = actor.DisplayName;
            request.DecisionNotes = Clean(notes);

            await _context.SaveChangesAsync();

            await _audit.RecordAsync(
                AuditActions.StatusChanged, ErpModules.Employees, nameof(LeaveRequest),
                leaveRequestId.ToString(),
                $"{request.Employee?.FirstName} {request.Employee?.LastName}: " +
                $"{request.Days:0.##} day(s) from {request.StartDate:d MMM yyyy} " +
                $"{request.Status.ToLowerInvariant()}.");

            return await GetRequestAsync(leaveRequestId);
        }

        public async Task<LeaveRequestView?> CancelAsync(int leaveRequestId, string reason)
        {
            var request = await _context.LeaveRequests
                .FirstOrDefaultAsync(r => r.LeaveRequestId == leaveRequestId);

            if (request is null) return null;

            if (string.Equals(request.Status, LeaveStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("This request has already been cancelled.");
            }

            var actor = _actor.Current;

            request.Status = LeaveStatuses.Cancelled;
            request.DecidedAt = DateTime.UtcNow;
            request.DecidedByUserId = actor.AppUserId;
            request.DecidedBy = actor.DisplayName;
            request.DecisionNotes = Clean(reason);

            await _context.SaveChangesAsync();

            return await GetRequestAsync(leaveRequestId);
        }

        public async Task<bool> DeleteAsync(int leaveRequestId)
        {
            var request = await _context.LeaveRequests
                .FirstOrDefaultAsync(r => r.LeaveRequestId == leaveRequestId);

            if (request is null) return false;

            if (string.Equals(request.Status, LeaveStatuses.Approved, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "Approved leave is part of the attendance record and payroll may already " +
                    "have counted it. Cancel it instead, so the change is visible.");
            }

            _context.LeaveRequests.Remove(request);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<decimal> GetPaidLeaveDaysAsync(
            int employeeId, DateTime periodStart, DateTime periodEnd)
        {
            var from = periodStart.Date;
            var to = periodEnd.Date;

            var rows = await _context.LeaveRequests
                .AsNoTracking()
                .Where(r => r.EmployeeId == employeeId &&
                            r.Status == LeaveStatuses.Approved &&
                            r.IsPaid &&
                            r.StartDate <= to && r.EndDate >= from)
                .Select(r => new { r.StartDate, r.EndDate })
                .ToListAsync();

            // Only the part of each absence that falls inside the period counts. Leave
            // straddling a pay boundary must not be paid twice.
            return rows.Sum(r => WorkingDaysBetween(
                r.StartDate > from ? r.StartDate : from,
                r.EndDate < to ? r.EndDate : to));
        }

        // ------------------------------------------------------------------ helpers

        private async Task<Employee> RequireEmployeeAsync(int employeeId) =>
            await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId)
            ?? throw new ValidationException($"No employee with id {employeeId} exists.");

        private static void ValidateDates(DateTime startDate, DateTime endDate)
        {
            if (startDate == default || endDate == default)
            {
                throw new ValidationException("A start and end date are both required.");
            }

            if (endDate.Date < startDate.Date)
            {
                throw new ValidationException("Leave cannot end before it starts.");
            }

            if ((endDate.Date - startDate.Date).TotalDays > 365)
            {
                throw new ValidationException(
                    "A single request cannot cover more than a year. Raise separate requests.");
            }
        }

        private async Task RequireNoOverlapAsync(
            int employeeId, DateTime startDate, DateTime endDate, int? excludeId)
        {
            var clash = await _context.LeaveRequests
                .AsNoTracking()
                .Where(r => r.EmployeeId == employeeId &&
                            r.LeaveRequestId != (excludeId ?? 0) &&
                            (r.Status == LeaveStatuses.Pending || r.Status == LeaveStatuses.Approved) &&
                            r.StartDate <= endDate.Date && r.EndDate >= startDate.Date)
                .Select(r => new { r.StartDate, r.EndDate })
                .FirstOrDefaultAsync();

            if (clash is not null)
            {
                // The same reasoning as payroll's overlap guard: two overlapping absences mean
                // the same day is accounted for twice, and paid twice if both are paid.
                throw new ValidationException(
                    $"This overlaps leave already recorded from {clash.StartDate:d MMM yyyy} " +
                    $"to {clash.EndDate:d MMM yyyy}. The same day cannot be booked off twice.");
            }
        }

        /// <summary>
        /// Days between two dates, inclusive, excluding weekends.
        ///
        /// A gym is open at weekends, but leave is counted against a working pattern and
        /// counting Saturdays would charge an employee two days for a Friday-to-Monday break.
        /// Both bounds are inclusive: one day off is one day.
        /// </summary>
        private static decimal WorkingDaysBetween(DateTime start, DateTime end)
        {
            if (end.Date < start.Date) return 0m;

            var days = 0;

            for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                days++;
            }

            return days;
        }

        private static LeaveRequestView ToView(LeaveRequest r) => new()
        {
            LeaveRequestId = r.LeaveRequestId,
            EmployeeId = r.EmployeeId,
            EmployeeCode = r.Employee?.EmployeeCode ?? "",
            EmployeeName = r.Employee is null
                ? $"Employee #{r.EmployeeId}"
                : $"{r.Employee.FirstName} {r.Employee.LastName}".Trim(),
            LeaveType = r.LeaveType,
            StartDate = r.StartDate,
            EndDate = r.EndDate,
            Days = r.Days,
            IsPaid = r.IsPaid,
            Reason = r.Reason,
            Status = r.Status,
            RequestedBy = string.IsNullOrWhiteSpace(r.RequestedBy) ? "—" : r.RequestedBy,
            DecidedAt = r.DecidedAt,
            DecidedBy = r.DecidedBy,
            DecisionNotes = r.DecisionNotes,
            CreatedAt = r.CreatedAt
        };

        private static string Clean(string? value) => (value ?? "").Trim();
    }
}
