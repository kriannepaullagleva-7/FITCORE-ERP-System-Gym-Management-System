namespace ERP_domain.entities
{
    /// <summary>
    /// One pay run for one employee over one period.
    ///
    /// GrossPay and NetPay are stored rather than computed on read, so a historical payslip
    /// still shows what was actually paid even if the employee's salary changes later. The
    /// server is what calculates them; they are never taken from the client.
    /// </summary>
    public class Payroll : IAuditable
    {
        public int PayrollId { get; set; }

        public int EmployeeId { get; set; }

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public decimal BasicSalary { get; set; }
        public decimal Allowances { get; set; }
        public decimal Deductions { get; set; }

        /// <summary>Hours worked beyond the normal period. Zero for salaried staff.</summary>
        public decimal OvertimeHours { get; set; }

        /// <summary>Pay per overtime hour, held per run so a later rate change is not backdated.</summary>
        public decimal OvertimeRate { get; set; }

        /// <summary>OvertimeHours * OvertimeRate, calculated on the server.</summary>
        public decimal OvertimePay { get; set; }

        /// <summary>BasicSalary + Allowances + OvertimePay, calculated on the server.</summary>
        public decimal GrossPay { get; set; }

        /// <summary>GrossPay - Deductions, calculated on the server.</summary>
        public decimal NetPay { get; set; }

        /// <summary>Draft, Approved or Paid.</summary>
        public string Status { get; set; } = "Draft";

        /// <summary>Set when the run is marked Paid.</summary>
        public DateTime? PaidDate { get; set; }

        public string Notes { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        public Employee Employee { get; set; } = null!;
    }
}
