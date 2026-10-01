using Microsoft.Extensions.Options;

namespace ERP_infrastructure.services
{
    /// <summary>What a month's statutory deductions come to for a given gross pay.</summary>
    public sealed record PayrollDeductionResult(
        decimal Sss, decimal PhilHealth, decimal PagIbig, decimal WithholdingTax)
    {
        public decimal Total => Sss + PhilHealth + PagIbig + WithholdingTax;
    }

    /// <summary>
    /// What the gym itself owes on top of an employee's gross pay.
    ///
    /// These are not deductions. They are never taken out of anybody's pay and never appear on
    /// a payslip as a reduction; they are the employer's own statutory contributions, which
    /// make the true cost of employing somebody higher than their gross pay. Payroll is where
    /// the amount is worked out, and Finance is where it becomes an expense and a liability.
    /// </summary>
    public sealed record EmployerContributionResult(
        decimal Sss, decimal PhilHealth, decimal PagIbig)
    {
        public decimal Total => Sss + PhilHealth + PagIbig;
    }

    /// <summary>
    /// Computes SSS, PhilHealth, Pag-IBIG and withholding tax from a month's gross pay, using
    /// the configured reference table rather than values scattered through the service that
    /// generates a payroll run.
    /// </summary>
    public interface IPayrollDeductionCalculator
    {
        PayrollDeductionResult Compute(decimal grossMonthlyPay);

        /// <summary>The employer's own share for the same gross pay.</summary>
        EmployerContributionResult ComputeEmployerShare(decimal grossMonthlyPay);
    }

    public class PayrollDeductionCalculator : IPayrollDeductionCalculator
    {
        private readonly PayrollDeductionOptions _options;

        public PayrollDeductionCalculator(IOptions<PayrollDeductionOptions> options)
        {
            _options = options.Value;
        }

        public PayrollDeductionResult Compute(decimal grossMonthlyPay)
        {
            if (grossMonthlyPay < 0m) grossMonthlyPay = 0m;

            // Every figure is rounded to centavos here, where it is worked out, rather than
            // left as a fraction for the database to round on the way into a decimal(18,2)
            // column.
            //
            // Rounding late looks harmless and is not. PhilHealth on a gross of 15,725 is
            // 393.125 for each half; stored independently both become 393.13, and the payslip
            // then shows deductions that do not add up to the total printed beside them while
            // net pay is a centavo adrift of gross minus deductions. The journal entry built
            // from those columns cannot balance, and because posting is deliberately not
            // allowed to fail the pay run, the whole thing disappears quietly - which is how a
            // paid run reached nobody's books at all.
            //
            // You cannot pay somebody half a centavo, so the rounded figure is the real one.
            var sss = Round(ComputeSss(grossMonthlyPay));
            var philHealth = Round(ComputePhilHealth(grossMonthlyPay));
            var pagIbig = Round(ComputePagIbig(grossMonthlyPay));

            // The taxable base uses the contributions actually withheld, not their unrounded
            // originals, so the payslip and the tax agree about what was taken.
            var withholding = ComputeWithholdingTax(grossMonthlyPay, sss, philHealth, pagIbig);

            return new PayrollDeductionResult(sss, philHealth, pagIbig, withholding);
        }

        private static decimal Round(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        public EmployerContributionResult ComputeEmployerShare(decimal grossMonthlyPay)
        {
            if (grossMonthlyPay <= 0m) return new EmployerContributionResult(0m, 0m, 0m);

            var s = _options.Sss;
            var p = _options.PhilHealth;
            var g = _options.PagIbig;

            // Each share uses the same basis as its employee counterpart - the same clamped
            // salary credit, the same floor and ceiling - and only the rate and the cap
            // differ. Deriving both from one basis is what stops the two halves of a
            // contribution being computed against different numbers.
            var sss = Math.Min(
                Math.Clamp(grossMonthlyPay, s.MscMin, s.MscMax) * s.EmployerRate,
                s.EmployerMonthlyCap);

            var philHealth = Math.Min(
                Math.Clamp(grossMonthlyPay, p.SalaryMin, p.SalaryMax) * p.EmployerRate,
                p.EmployerMonthlyCap);

            var pagIbig = Math.Min(
                Math.Min(grossMonthlyPay, g.SalaryCap) * g.EmployerRate,
                g.EmployerMonthlyCap);

            return new EmployerContributionResult(Round(sss), Round(philHealth), Round(pagIbig));
        }

        private decimal ComputeSss(decimal grossMonthlyPay)
        {
            var s = _options.Sss;
            if (grossMonthlyPay <= 0m) return 0m;

            var msc = Math.Clamp(grossMonthlyPay, s.MscMin, s.MscMax);
            return Math.Min(msc * s.EmployeeRate, s.MonthlyCap);
        }

        private decimal ComputePhilHealth(decimal grossMonthlyPay)
        {
            var p = _options.PhilHealth;
            if (grossMonthlyPay <= 0m) return 0m;

            var basis = Math.Clamp(grossMonthlyPay, p.SalaryMin, p.SalaryMax);
            return Math.Min(basis * p.EmployeeRate, p.MonthlyCap);
        }

        private decimal ComputePagIbig(decimal grossMonthlyPay)
        {
            var g = _options.PagIbig;
            if (grossMonthlyPay <= 0m) return 0m;

            var basis = Math.Min(grossMonthlyPay, g.SalaryCap);
            return Math.Min(basis * g.EmployeeRate, g.MonthlyCap);
        }

        /// <summary>
        /// Annualises the gross pay net of the mandatory contributions above, applies the
        /// progressive bracket table, and returns the tax for one month of it. The
        /// ≤₱250,000-a-year band is the first bracket and yields zero, per the FitCore
        /// simplified reference.
        /// </summary>
        private decimal ComputeWithholdingTax(
            decimal grossMonthlyPay, decimal sss, decimal philHealth, decimal pagIbig)
        {
            var monthlyTaxable = grossMonthlyPay - sss - philHealth - pagIbig;
            if (monthlyTaxable <= 0m) return 0m;

            var annualTaxable = monthlyTaxable * 12m;

            var bracket = _options.WithholdingTaxBrackets
                .OrderBy(b => b.AnnualThreshold)
                .FirstOrDefault(b => b.AnnualUpTo is null || annualTaxable <= b.AnnualUpTo.Value);

            if (bracket is null) return 0m;

            var annualTax = bracket.BaseTax +
                Math.Max(0m, annualTaxable - bracket.AnnualThreshold) * bracket.RateOverThreshold;

            return Math.Round(annualTax / 12m, 2, MidpointRounding.AwayFromZero);
        }
    }
}
