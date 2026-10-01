using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The payslip for a pay run, with every statutory figure on the face of the grid.
    ///
    /// Distinct from Payroll History, which lists what has been paid and when. This is the
    /// payslip itself: the five deductions named separately, the employer's own contributions
    /// beside them, and the printable document behind each row.
    ///
    /// Deductions and employer contributions are kept in separate columns rather than totalled
    /// together, because they are opposite things. A deduction comes out of what the employee is
    /// owed; an employer contribution is a cost on top of it and never reduces anybody's pay.
    /// Adding them would overstate the deductions and misstate the net.
    ///
    /// Draft and approved runs are listed as well as paid ones, so a payslip can be checked
    /// before the money moves rather than only afterwards - but only a paid run's slip is a
    /// receipt for anything, which is what the Status column is for.
    /// </summary>
    internal sealed class PayslipsPage : CrudPageBase<PayrollDto>
    {
        private readonly ComboBox _status;
        private readonly Label _summary;

        public PayslipsPage(FitCoreSession session)
            : base(session, "Payslips",
                   "The payslip behind every pay run, with each statutory deduction named.",
                   "payslip", "Employee name or code")
        {
            AddAction("View payslip", ButtonTone.Primary, () => ShowPayslipAsync(), 124);

            FilterBar.Controls.Add(UiKit.FilterLabel("Status"));

            _status = UiKit.Select(140);
            _status.DisplayMember = "Value";
            _status.ValueMember = "Key";
            _status.DataSource = new List<KeyValuePair<string, string>>
            {
                new("Paid", "Paid"),
                new("", "All runs"),
                new("Draft", "Draft"),
                new("Approved", "Approved")
            };
            _status.SelectedIndexChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_status);

            _summary = new Label
            {
                AutoSize = true,
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                Margin = new Padding(16, 10, 0, 0),
                UseMnemonic = false
            };
            FilterBar.Controls.Add(_summary);
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string EmptyHeadline => "No payslips to show";
        protected override string EmptyDetail =>
            "Generate a pay run on Payroll Records or Payroll Calculation, and its payslip appears here.";

        protected override async Task<List<PayrollDto>?> FetchAsync()
        {
            var all = Unwrap(await Session.Payroll.GetAllAsync());
            if (all is null) return null;

            var wanted = _status.SelectedValue?.ToString();

            var rows = string.IsNullOrWhiteSpace(wanted)
                ? all
                : all.Where(p => string.Equals(p.Status, wanted, StringComparison.OrdinalIgnoreCase))
                     .ToList();

            return rows
                .OrderByDescending(p => p.PaidDate ?? p.PeriodEnd)
                .ToList();
        }

        protected override void DefineColumns()
        {
            Column(nameof(PayrollDto.EmployeeCode), "Code", 70);
            Column(nameof(PayrollDto.EmployeeName), "Employee", 140);
            DateColumn(nameof(PayrollDto.PeriodStart), "From", 75);
            DateColumn(nameof(PayrollDto.PeriodEnd), "To", 75);
            MoneyColumn(nameof(PayrollDto.GrossPay), "Gross", 80);
            MoneyColumn(nameof(PayrollDto.SssDeduction), "SSS", 70);
            MoneyColumn(nameof(PayrollDto.PhilHealthDeduction), "PhilHealth", 80);
            MoneyColumn(nameof(PayrollDto.PagIbigDeduction), "Pag-IBIG", 75);
            MoneyColumn(nameof(PayrollDto.WithholdingTax), "Tax", 70);
            MoneyColumn(nameof(PayrollDto.OtherDeductions), "Other", 70);
            MoneyColumn(nameof(PayrollDto.NetPay), "Net pay", 85);
            MoneyColumn(nameof(PayrollDto.EmployerContributions), "Employer", 80);
            StatusColumn(nameof(PayrollDto.Status), "Status", 75);
        }

        protected override bool Matches(PayrollDto p, string term) =>
            (p.EmployeeName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (p.EmployeeCode ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override void AfterLoad()
        {
            _summary.Text =
                $"{Items.Count:N0} payslip(s)      " +
                $"gross {UiKit.Money(Items.Sum(p => p.GrossPay))}      " +
                $"deductions {UiKit.Money(Items.Sum(p => p.Deductions))}      " +
                $"net {UiKit.Money(Items.Sum(p => p.NetPay))}      " +
                $"employer share {UiKit.Money(Items.Sum(p => p.EmployerContributions))}";
        }

        /// <summary>Opens the shared receipt window, which is where Print and Copy live.</summary>
        private Task ShowPayslipAsync()
        {
            var run = Selected;

            if (run is null)
            {
                ShowError("Select a pay run to view its payslip.");
                return Task.CompletedTask;
            }

            ReceiptDialog.Show(this, ReceiptBuilder.ForPayroll(Session, run));
            return Task.CompletedTask;
        }
    }
}
