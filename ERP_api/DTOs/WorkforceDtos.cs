using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    // ----------------------------------------------------------------------------------
    // Employees
    // ----------------------------------------------------------------------------------

    public class EmployeeDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Position { get; set; } = "";
        public string Department { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime HireDate { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal HourlyRate { get; set; }
        public string Status { get; set; } = "";
    }

    public class CreateEmployeeDto
    {
        [Required(ErrorMessage = "An employee code is required.")]
        [StringLength(50)]
        public string EmployeeCode { get; set; } = "";

        [Required(ErrorMessage = "A first name is required.")]
        [StringLength(100)]
        public string FirstName { get; set; } = "";

        [Required(ErrorMessage = "A last name is required.")]
        [StringLength(100)]
        public string LastName { get; set; } = "";

        // Deliberately no [RegularExpression] here: EmployeeService.RequirePosition (create) and
        // RequirePositionChange (update) are the one place this is validated, because an update
        // has to accept a real job title already on the roster ("Branch Manager",
        // "Receptionist") that a strict "Staff or Manager only" pattern at this edge would
        // reject outright - even when nothing about the position is actually being changed.
        [Required(ErrorMessage = "A position is required.")]
        public string Position { get; set; } = "";

        [StringLength(100)]
        public string Department { get; set; } = "";

        [StringLength(20)]
        public string Phone { get; set; } = "";

        [StringLength(100)]
        [OptionalEmailAddress]
        public string Email { get; set; } = "";

        public DateTime HireDate { get; set; }

        [Range(0, 10000000, ErrorMessage = "Basic salary cannot be negative.")]
        public decimal BasicSalary { get; set; }

        [Range(0, 100000, ErrorMessage = "Hourly rate cannot be negative.")]
        public decimal HourlyRate { get; set; }
    }

    public class UpdateEmployeeDto : CreateEmployeeDto
    {
        [Required]
        [RegularExpression("^(Active|Inactive|Terminated)$",
            ErrorMessage = "Status must be Active, Inactive or Terminated.")]
        public string Status { get; set; } = "Active";
    }

    /// <summary>
    /// What happened when an employee was given a FitCore sign-in. The employee record is
    /// unaffected either way - this reports on the account only.
    /// </summary>
    public class EmployeeAccountResultDto
    {
        /// <summary>Created, AlreadyExisted, Skipped or Failed.</summary>
        public string Outcome { get; set; } = "";

        public string Message { get; set; } = "";

        /// <summary>True when the employee now has a sign-in they can use, one way or another.</summary>
        public bool AccountUsable { get; set; }
    }

    // ----------------------------------------------------------------------------------
    // Payroll
    //
    // The write DTOs deliberately carry no gross or net pay. Those are calculated on the
    // server from the inputs below, so a client cannot dictate what someone is paid.
    // ----------------------------------------------------------------------------------

    public class CreatePayrollDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "A valid employee must be selected.")]
        public int EmployeeId { get; set; }

        [Required(ErrorMessage = "A pay period start date is required.")]
        public DateTime PeriodStart { get; set; }

        [Required(ErrorMessage = "A pay period end date is required.")]
        public DateTime PeriodEnd { get; set; }

        /// <summary>Optional. Defaults to the employee's current basic salary.</summary>
        [Range(0, 10000000, ErrorMessage = "Basic salary cannot be negative.")]
        public decimal? BasicSalary { get; set; }

        [Range(0, 10000000, ErrorMessage = "Allowances cannot be negative.")]
        public decimal Allowances { get; set; }

        [Range(0, 10000000, ErrorMessage = "Deductions cannot be negative.")]
        public decimal Deductions { get; set; }

        [Range(0, 100000, ErrorMessage = "Overtime hours cannot be negative.")]
        public decimal OvertimeHours { get; set; }

        [Range(0, 1000000, ErrorMessage = "The overtime rate cannot be negative.")]
        public decimal OvertimeRate { get; set; }

        [StringLength(300)]
        public string Notes { get; set; } = "";
    }

    public class UpdatePayrollDto
    {
        [Required(ErrorMessage = "A pay period start date is required.")]
        public DateTime PeriodStart { get; set; }

        [Required(ErrorMessage = "A pay period end date is required.")]
        public DateTime PeriodEnd { get; set; }

        [Range(0, 10000000, ErrorMessage = "Basic salary cannot be negative.")]
        public decimal BasicSalary { get; set; }

        [Range(0, 10000000, ErrorMessage = "Allowances cannot be negative.")]
        public decimal Allowances { get; set; }

        [Range(0, 10000000, ErrorMessage = "Deductions cannot be negative.")]
        public decimal Deductions { get; set; }

        [Range(0, 100000, ErrorMessage = "Overtime hours cannot be negative.")]
        public decimal OvertimeHours { get; set; }

        [Range(0, 1000000, ErrorMessage = "The overtime rate cannot be negative.")]
        public decimal OvertimeRate { get; set; }

        [StringLength(300)]
        public string Notes { get; set; } = "";
    }

    public class UpdatePayrollStatusDto
    {
        [Required]
        [RegularExpression("^(Draft|Approved|Paid)$",
            ErrorMessage = "Status must be Draft, Approved or Paid.")]
        public string Status { get; set; } = "Draft";
    }

    /// <summary>
    /// Generates a run from recorded attendance rather than typed hours. Regular/overtime
    /// hours, the hourly rate and the statutory deductions are all derived server-side; only
    /// allowances and any extra deduction outside the statutory set are supplied here.
    /// </summary>
    public class GeneratePayrollDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "A valid employee must be selected.")]
        public int EmployeeId { get; set; }

        [Required(ErrorMessage = "A pay period start date is required.")]
        public DateTime PeriodStart { get; set; }

        [Required(ErrorMessage = "A pay period end date is required.")]
        public DateTime PeriodEnd { get; set; }

        [Range(0, 10000000, ErrorMessage = "Allowances cannot be negative.")]
        public decimal Allowances { get; set; }

        [Range(0, 10000000, ErrorMessage = "Other deductions cannot be negative.")]
        public decimal OtherDeductions { get; set; }

        [StringLength(300)]
        public string Notes { get; set; } = "";
    }

    // ----------------------------------------------------------------------------------
    // Attendance
    // ----------------------------------------------------------------------------------

    public class AttendanceDto
    {
        public int AttendanceId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string Position { get; set; } = "";
        public DateTime Date { get; set; }
        public DateTime? TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }
        public decimal RegularHours { get; set; }
        public decimal OvertimeHours { get; set; }
        public string Status { get; set; } = "Present";
        public string Notes { get; set; } = "";
        public int? RecordedByUserId { get; set; }
        public string RecordedBy { get; set; } = "";
        public int? ModifiedByUserId { get; set; }
        public string ModifiedBy { get; set; } = "";
    }

    public class CreateAttendanceDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "A valid employee must be selected.")]
        public int EmployeeId { get; set; }

        [Required(ErrorMessage = "A date is required.")]
        public DateTime Date { get; set; }

        public DateTime? TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }

        [Required(ErrorMessage = "A status is required.")]
        [RegularExpression("^(Present|Absent|Late|Leave)$",
            ErrorMessage = "Status must be Present, Absent, Late or Leave.")]
        public string Status { get; set; } = "Present";

        [StringLength(300)]
        public string Notes { get; set; } = "";
    }

    public class UpdateAttendanceDto : CreateAttendanceDto
    {
    }

    // ----------------------------------------------------------------------------------
    // Expenses
    // ----------------------------------------------------------------------------------

    public class CreateExpenseDto
    {
        [Required(ErrorMessage = "A category is required.")]
        [StringLength(50)]
        public string Category { get; set; } = "Other";

        [Required(ErrorMessage = "A description is required.")]
        [StringLength(300)]
        public string Description { get; set; } = "";

        [Range(0.01, 10000000, ErrorMessage = "The amount must be greater than zero.")]
        public decimal Amount { get; set; }

        public DateTime ExpenseDate { get; set; }

        [StringLength(50)]
        public string PaymentMethod { get; set; } = "Cash";

        [StringLength(60)]
        public string ReferenceNo { get; set; } = "";

        /// <summary>Optional link to the employee who recorded the expense.</summary>
        public int? RecordedByEmployeeId { get; set; }

        /// <summary>
        /// Paid or Unpaid. An unpaid expense is money owed and shows up in accounts payable
        /// rather than leaving cash straight away.
        /// </summary>
        [StringLength(20)]
        public string Status { get; set; } = "Paid";

        /// <summary>Who it was paid to, for a landlord or a utility that is not a supplier.</summary>
        [StringLength(150)]
        public string PaidTo { get; set; } = "";

        /// <summary>Set instead of <see cref="PaidTo"/> when the payee is already on record.</summary>
        public int? SupplierId { get; set; }

        /// <summary>
        /// Overrides the ledger account this posts to. Left null, the category decides, which
        /// is what a tenant who has never opened the chart of accounts wants.
        /// </summary>
        public int? AccountId { get; set; }

        /// <summary>Which cash or bank account the money came out of.</summary>
        public int? BankAccountId { get; set; }
    }

    public class UpdateExpenseDto : CreateExpenseDto
    {
    }

    /// <summary>Settles an expense that was recorded as owed.</summary>
    public class SettleExpenseDto
    {
        [StringLength(50)]
        public string PaymentMethod { get; set; } = "Cash";

        public int? BankAccountId { get; set; }
    }
}
