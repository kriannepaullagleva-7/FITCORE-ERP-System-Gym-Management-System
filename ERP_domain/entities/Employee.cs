namespace ERP_domain.entities
{
    /// <summary>
    /// A person employed by the gym. Payroll runs against this record, and expenses can be
    /// attributed to whoever recorded them.
    /// </summary>
    public class Employee : IAuditable, IBranchScoped
    {
        public int EmployeeId { get; set; }

        /// <summary>The branch this employee works at. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }

        /// <summary>Short human-readable code, unique within the tenant.</summary>
        public string EmployeeCode { get; set; } = "";

        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";

        public string Position { get; set; } = "";
        public string Department { get; set; } = "";

        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";

        public DateTime HireDate { get; set; } = DateTime.UtcNow;

        /// <summary>Monthly basic pay, used as the default when a payroll line is created.</summary>
        public decimal BasicSalary { get; set; }

        /// <summary>
        /// Pay per hour worked, the basis for attendance-driven payroll calculation. Zero for
        /// an employee paid a flat monthly salary with no hourly component recorded yet.
        /// </summary>
        public decimal HourlyRate { get; set; }

        /// <summary>Active, Inactive or Terminated.</summary>
        public string Status { get; set; } = "Active";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        public ICollection<Payroll> Payrolls { get; set; } = new List<Payroll>();
        public ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();
    }
}
