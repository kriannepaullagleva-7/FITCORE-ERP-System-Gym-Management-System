namespace ERP_domain.entities
{
    /// <summary>
    /// One employee's attendance for one day. Payroll reads these to calculate a run rather
    /// than trusting hours typed by hand, so the numbers on a payslip trace back to a
    /// recorded time in/out rather than to whatever the operator remembered.
    /// </summary>
    public class Attendance : IAuditable, IBranchScoped
    {
        public int AttendanceId { get; set; }

        /// <summary>The branch the shift was worked at. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }

        public int EmployeeId { get; set; }

        public DateTime Date { get; set; }

        public DateTime? TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }

        /// <summary>Hours worked up to the standard shift length. Zero for Absent/Leave.</summary>
        public decimal RegularHours { get; set; }

        /// <summary>Hours worked beyond the standard shift length.</summary>
        public decimal OvertimeHours { get; set; }

        /// <summary>Present, Absent, Late or Leave.</summary>
        public string Status { get; set; } = "Present";

        public string Notes { get; set; } = "";

        /// <summary>The Manager/Admin who recorded this entry, taken from the token.</summary>
        public int? RecordedByUserId { get; set; }
        public string RecordedBy { get; set; } = "";

        /// <summary>Set only when the entry is later corrected.</summary>
        public int? ModifiedByUserId { get; set; }
        public string ModifiedBy { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        public Employee Employee { get; set; } = null!;
    }
}
