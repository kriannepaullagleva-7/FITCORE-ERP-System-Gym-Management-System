namespace ERP_domain.entities
{
    /// <summary>
    /// One pay run for one employee over one period.
    ///
    /// GrossPay and NetPay are stored rather than computed on read, so a historical payslip
    /// still shows what was actually paid even if the employee's salary changes later. The
    /// server is what calculates them; they are never taken from the client.
    /// </summary>
    public class Payroll : IAuditable, IBranchScoped
    {
        public int PayrollId { get; set; }

        /// <summary>The branch whose wage bill this run belongs to. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }

        public int EmployeeId { get; set; }

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public decimal BasicSalary { get; set; }
        public decimal Allowances { get; set; }

        /// <summary>Sum of SssDeduction, PhilHealthDeduction, PagIbigDeduction, WithholdingTax and OtherDeductions.</summary>
        public decimal Deductions { get; set; }

        /// <summary>Hours worked within the standard shift length, from attendance. Zero for salaried-only runs.</summary>
        public decimal RegularHours { get; set; }

        /// <summary>The employee's hourly rate at the time this run was generated.</summary>
        public decimal HourlyRate { get; set; }

        /// <summary>RegularHours * HourlyRate, calculated on the server.</summary>
        public decimal RegularPay { get; set; }

        /// <summary>Hours worked beyond the normal period. Zero for salaried staff.</summary>
        public decimal OvertimeHours { get; set; }

        /// <summary>Pay per overtime hour, held per run so a later rate change is not backdated.</summary>
        public decimal OvertimeRate { get; set; }

        /// <summary>OvertimeHours * OvertimeRate, calculated on the server.</summary>
        public decimal OvertimePay { get; set; }

        /// <summary>BasicSalary + RegularPay + Allowances + OvertimePay, calculated on the server.</summary>
        public decimal GrossPay { get; set; }

        /// <summary>The employee share of SSS, capped per the deduction configuration.</summary>
        public decimal SssDeduction { get; set; }

        /// <summary>The employee share of PhilHealth, capped per the deduction configuration.</summary>
        public decimal PhilHealthDeduction { get; set; }

        /// <summary>The employee share of Pag-IBIG, capped per the deduction configuration.</summary>
        public decimal PagIbigDeduction { get; set; }

        /// <summary>Withholding tax, from the configured bracket table.</summary>
        public decimal WithholdingTax { get; set; }

        /// <summary>Anything deducted outside the statutory set - a cash advance, a loss, a loan.</summary>
        public decimal OtherDeductions { get; set; }

        /// <summary>GrossPay - Deductions, calculated on the server.</summary>
        public decimal NetPay { get; set; }

        // ---------------------------------------------------------------- employer share
        //
        // The employer's own contributions are a cost to the gym that the employee never sees
        // on a payslip: they are not deducted from gross pay and do not affect net pay. They
        // are recorded here because payroll is where the amount is determined, and because the
        // true cost of employing somebody is gross pay plus these - which is the figure that
        // belongs in an expense report and nowhere else would produce it.

        /// <summary>The employer share of SSS for this period.</summary>
        public decimal SssEmployerShare { get; set; }

        /// <summary>The employer share of PhilHealth for this period.</summary>
        public decimal PhilHealthEmployerShare { get; set; }

        /// <summary>The employer share of Pag-IBIG for this period.</summary>
        public decimal PagIbigEmployerShare { get; set; }

        /// <summary>The three employer shares together. The gym's cost above gross pay.</summary>
        public decimal EmployerContributions { get; set; }

        /// <summary>Draft, Approved or Paid.</summary>
        public string Status { get; set; } = "Draft";

        /// <summary>Set when the run is approved, which is the step before it can be paid.</summary>
        public DateTime? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string ApprovedBy { get; set; } = "";

        /// <summary>Set when the run is marked Paid.</summary>
        public DateTime? PaidDate { get; set; }

        /// <summary>
        /// The signed-in user who created the run, captured from the token. A payslip has to
        /// name who authorised it, and the audit trail alone is not something a receipt can
        /// be reprinted from.
        /// </summary>
        public int? ProcessedByUserId { get; set; }

        /// <summary>Their display name, denormalised so the payslip survives the account.</summary>
        public string ProcessedBy { get; set; } = "";

        /// <summary>Set only when the run is later edited, so a payslip can show who corrected it.</summary>
        public int? LastModifiedByUserId { get; set; }
        public string LastModifiedBy { get; set; } = "";

        public string Notes { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        public Employee Employee { get; set; } = null!;

        /// <summary>What this employee actually cost the gym for the period.</summary>
        public decimal TotalEmploymentCost => GrossPay + EmployerContributions;
    }
}
