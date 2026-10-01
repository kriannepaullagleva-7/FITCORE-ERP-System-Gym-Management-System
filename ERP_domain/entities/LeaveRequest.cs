namespace ERP_domain.entities
{
    public static class LeaveTypes
    {
        public const string Vacation = "Vacation";
        public const string Sick = "Sick";
        public const string Emergency = "Emergency";
        public const string Maternity = "Maternity/Paternity";
        public const string Unpaid = "Unpaid";

        public static readonly IReadOnlyList<string> All =
            new[] { Vacation, Sick, Emergency, Maternity, Unpaid };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

        public static string? Normalise(string? value) =>
            All.FirstOrDefault(t => string.Equals(t, (value ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Whether this kind of leave is paid by default.
        ///
        /// Only a default: the request carries its own flag, because a gym may run out of a
        /// employee's paid allocation halfway through a period and the rest of that same
        /// absence is unpaid.
        /// </summary>
        public static bool IsPaidByDefault(string? value) =>
            Normalise(value) is not null && !string.Equals(Normalise(value), Unpaid, StringComparison.Ordinal);
    }

    public static class LeaveStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";

        public static readonly IReadOnlyList<string> All =
            new[] { Pending, Approved, Rejected, Cancelled };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Only approved leave affects attendance or pay.</summary>
        public static bool IsEffective(string? value) =>
            string.Equals(value, Approved, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Time an employee is away, and whether they are paid for it.
    ///
    /// Leave belongs to Employee Management rather than to Payroll, because the decision to
    /// grant it is a staffing one. Payroll only reads the outcome: approved paid leave counts
    /// towards the hours a run is calculated from, and approved unpaid leave does not - which
    /// is why the flag is on the request rather than inferred from the type.
    /// </summary>
    public class LeaveRequest : IAuditable, IBranchScoped
    {
        public int LeaveRequestId { get; set; }

        /// <summary>The branch the employee belongs to. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }

        public int EmployeeId { get; set; }

        /// <summary>One of <see cref="LeaveTypes"/>.</summary>
        public string LeaveType { get; set; } = LeaveTypes.Vacation;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Working days covered, calculated by the server from the dates rather than typed, so
        /// two requests over the same span always agree with each other.
        /// </summary>
        public decimal Days { get; set; }

        /// <summary>Whether these days are paid. Defaulted from the type, then editable.</summary>
        public bool IsPaid { get; set; } = true;

        public string Reason { get; set; } = "";

        /// <summary>One of <see cref="LeaveStatuses"/>.</summary>
        public string Status { get; set; } = LeaveStatuses.Pending;

        public int? RequestedByUserId { get; set; }
        public string RequestedBy { get; set; } = "";

        public DateTime? DecidedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecidedBy { get; set; } = "";

        /// <summary>Why it was refused, so the employee is told something.</summary>
        public string DecisionNotes { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Employee Employee { get; set; } = null!;
    }
}
