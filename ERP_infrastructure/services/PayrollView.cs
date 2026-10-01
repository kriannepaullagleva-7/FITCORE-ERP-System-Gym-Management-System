namespace ERP_infrastructure.services
{
    /// <summary>
    /// A payroll line flattened with the employee name, which is what every payroll screen
    /// needs. Gross and net are the stored values, calculated by the server when the run was
    /// created or updated - and carry enough of a breakdown to answer "why this amount?" from
    /// hours worked, rate, allowances and each deduction.
    /// </summary>
    public class PayrollView
    {
        public int PayrollId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string Position { get; set; } = "";

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public decimal BasicSalary { get; set; }
        public decimal Allowances { get; set; }

        public decimal RegularHours { get; set; }
        public decimal HourlyRate { get; set; }
        public decimal RegularPay { get; set; }

        public decimal OvertimeHours { get; set; }
        public decimal OvertimeRate { get; set; }
        public decimal OvertimePay { get; set; }

        public decimal GrossPay { get; set; }

        public decimal SssDeduction { get; set; }
        public decimal PhilHealthDeduction { get; set; }
        public decimal PagIbigDeduction { get; set; }
        public decimal WithholdingTax { get; set; }
        public decimal OtherDeductions { get; set; }

        /// <summary>Sum of the five deduction lines above.</summary>
        public decimal Deductions { get; set; }

        public decimal NetPay { get; set; }

        // ---------------------------------------------------------------- employer share
        //
        // The gym's own statutory contributions. Never deducted from anybody and never shown
        // on a payslip as a reduction - they are a cost on top of gross pay, which is why the
        // true cost of employing this person for the period is GrossPay + EmployerContributions
        // and no other figure on this record says so.

        public decimal SssEmployerShare { get; set; }
        public decimal PhilHealthEmployerShare { get; set; }
        public decimal PagIbigEmployerShare { get; set; }
        public decimal EmployerContributions { get; set; }

        /// <summary>Gross pay plus the employer contributions. What the run actually costs.</summary>
        public decimal TotalEmploymentCost => GrossPay + EmployerContributions;

        public string Status { get; set; } = "Draft";
        public DateTime? PaidDate { get; set; }
        public string Notes { get; set; } = "";

        /// <summary>Set once the run has been approved, which is the step before it is paid.</summary>
        public DateTime? ApprovedAt { get; set; }
        public string ApprovedBy { get; set; } = "";

        /// <summary>The signed-in user who created the run. Named on the payslip.</summary>
        public int? ProcessedByUserId { get; set; }
        public string ProcessedBy { get; set; } = "";

        /// <summary>Set only once the run has been edited after it was created.</summary>
        public int? LastModifiedByUserId { get; set; }
        public string LastModifiedBy { get; set; } = "";
    }

    /// <summary>Totals across a set of payroll runs, used by the payroll report.</summary>
    public class PayrollSummary
    {
        public int RunCount { get; set; }
        public int EmployeeCount { get; set; }
        public decimal TotalGross { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal TotalOvertime { get; set; }
        public decimal TotalNet { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal TotalOutstanding { get; set; }

        /// <summary>The employer's own contributions across the set - a cost above gross pay.</summary>
        public decimal TotalEmployerContributions { get; set; }

        /// <summary>What employing these people actually cost: gross pay plus the employer share.</summary>
        public decimal TotalEmploymentCost => TotalGross + TotalEmployerContributions;
    }
}
