namespace ERP_infrastructure.services
{
    /// <summary>
    /// The FitCore statutory deduction reference, bound from configuration (section
    /// <c>Payroll:Deductions</c>) rather than hardcoded, so a rate change is a config edit and
    /// not a redeploy. The defaults below are the values a fresh install gets when the section
    /// is absent.
    /// </summary>
    public class PayrollDeductionOptions
    {
        public const string SectionName = "Payroll:Deductions";

        public SssOptions Sss { get; set; } = new();
        public PhilHealthOptions PhilHealth { get; set; } = new();
        public PagIbigOptions PagIbig { get; set; } = new();
        public List<TaxBracket> WithholdingTaxBrackets { get; set; } = DefaultBrackets();

        public class SssOptions
        {
            public decimal EmployeeRate { get; set; } = 0.05m;

            /// <summary>
            /// The employer's own share, which is not deducted from the employee. It is a cost
            /// to the gym rather than a reduction of somebody's pay, so it never touches net
            /// pay and is reported separately.
            /// </summary>
            public decimal EmployerRate { get; set; } = 0.10m;

            public decimal MscMin { get; set; } = 5000m;
            public decimal MscMax { get; set; } = 35000m;
            public decimal MonthlyCap { get; set; } = 1750m;

            /// <summary>The ceiling on the employer share, mirroring the employee cap.</summary>
            public decimal EmployerMonthlyCap { get; set; } = 3500m;
        }

        public class PhilHealthOptions
        {
            public decimal EmployeeRate { get; set; } = 0.025m;
            public decimal EmployerRate { get; set; } = 0.025m;
            public decimal SalaryMin { get; set; } = 10000m;
            public decimal SalaryMax { get; set; } = 100000m;
            public decimal MonthlyCap { get; set; } = 2500m;
            public decimal EmployerMonthlyCap { get; set; } = 2500m;
        }

        public class PagIbigOptions
        {
            public decimal EmployeeRate { get; set; } = 0.02m;
            public decimal EmployerRate { get; set; } = 0.02m;
            public decimal SalaryCap { get; set; } = 10000m;
            public decimal MonthlyCap { get; set; } = 200m;
            public decimal EmployerMonthlyCap { get; set; } = 200m;
        }

        /// <summary>
        /// One band of the progressive annual withholding tax table. <see cref="AnnualUpTo"/>
        /// null marks the top, unbounded band.
        /// </summary>
        public class TaxBracket
        {
            public decimal AnnualThreshold { get; set; }
            public decimal? AnnualUpTo { get; set; }
            public decimal BaseTax { get; set; }
            public decimal RateOverThreshold { get; set; }
        }

        /// <summary>The BIR TRAIN law annual bracket table, the FitCore simplified reference.</summary>
        private static List<TaxBracket> DefaultBrackets() => new()
        {
            new TaxBracket { AnnualThreshold = 0,         AnnualUpTo = 250000m,  BaseTax = 0,         RateOverThreshold = 0.00m },
            new TaxBracket { AnnualThreshold = 250000m,   AnnualUpTo = 400000m,  BaseTax = 0,         RateOverThreshold = 0.15m },
            new TaxBracket { AnnualThreshold = 400000m,   AnnualUpTo = 800000m,  BaseTax = 22500m,    RateOverThreshold = 0.20m },
            new TaxBracket { AnnualThreshold = 800000m,   AnnualUpTo = 2000000m, BaseTax = 102500m,   RateOverThreshold = 0.25m },
            new TaxBracket { AnnualThreshold = 2000000m,  AnnualUpTo = 8000000m, BaseTax = 402500m,   RateOverThreshold = 0.30m },
            new TaxBracket { AnnualThreshold = 8000000m,  AnnualUpTo = null,     BaseTax = 2202500m,  RateOverThreshold = 0.35m }
        };
    }
}
