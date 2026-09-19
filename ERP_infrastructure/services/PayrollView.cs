namespace ERP_infrastructure.services
{
    /// <summary>
    /// A payroll line flattened with the employee name, which is what every payroll screen
    /// needs. Gross and net are the stored values, calculated by the server when the run was
    /// created or updated.
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
        public decimal Deductions { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal OvertimeRate { get; set; }
        public decimal OvertimePay { get; set; }
        public decimal GrossPay { get; set; }
        public decimal NetPay { get; set; }

        public string Status { get; set; } = "Draft";
        public DateTime? PaidDate { get; set; }
        public string Notes { get; set; } = "";
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
    }
}
