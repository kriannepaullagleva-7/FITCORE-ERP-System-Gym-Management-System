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

        [StringLength(100)]
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
    }

    public class UpdateEmployeeDto : CreateEmployeeDto
    {
        [Required]
        [RegularExpression("^(Active|Inactive|Terminated)$",
            ErrorMessage = "Status must be Active, Inactive or Terminated.")]
        public string Status { get; set; } = "Active";
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
    }

    public class UpdateExpenseDto : CreateExpenseDto
    {
    }
}
