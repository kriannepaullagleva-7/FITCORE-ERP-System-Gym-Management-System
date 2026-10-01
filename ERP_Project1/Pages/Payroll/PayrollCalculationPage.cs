using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Generates a pay run from recorded attendance: pick an employee for the period, review
    /// their attendance-derived hours, then generate. Regular/overtime pay, the hourly rate and
    /// the statutory deductions are all calculated by the server - this screen only collects
    /// allowances and any deduction outside the statutory set.
    /// </summary>
    internal sealed class PayrollCalculationPage : CrudPageBase<AttendancePeriodSummaryDto>
    {
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly Label _summary;
        private List<EmployeeDto> _employees = new();

        public PayrollCalculationPage(FitCoreSession session)
            : base(session, "Payroll Calculation",
                   "Generate a pay run from an employee's recorded attendance for the period.",
                   "employee", "Employee name or code")
        {
            AddAction("Generate payroll", ButtonTone.Primary, () => GenerateAsync(), 150);

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            _from = UiKit.DatePicker(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
            _from.ValueChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_from);

            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            _to = UiKit.DatePicker(DateTime.Today);
            _to.ValueChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_to);

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

        protected override string EmptyHeadline => "No attendance to calculate from";
        protected override string EmptyDetail =>
            "Record attendance for the period on the Employees module's Attendance tab, then " +
            "come back here to generate payroll.";

        protected override async Task<List<AttendancePeriodSummaryDto>?> FetchAsync()
        {
            _employees = Unwrap(await Session.Employees.GetActiveAsync()) ?? new();
            return await PayrollAttendanceSupport.LoadSummariesAsync(
                Session, _employees, _from.Value.Date, _to.Value.Date);
        }

        protected override void DefineColumns()
        {
            Column(nameof(AttendancePeriodSummaryDto.EmployeeName), "Employee", 150);
            Column(nameof(AttendancePeriodSummaryDto.TotalRegularHours), "Regular hrs", 80, "N2", rightAlign: true);
            Column(nameof(AttendancePeriodSummaryDto.TotalOvertimeHours), "OT hrs", 75, "N2", rightAlign: true);
        }

        protected override bool Matches(AttendancePeriodSummaryDto a, string term) =>
            (a.EmployeeName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override void AfterLoad()
        {
            _summary.Text = $"{Items.Count:N0} employee(s) ready for payroll calculation for " +
                            $"{UiKit.Date(_from.Value)} – {UiKit.Date(_to.Value)}";
        }

        private async Task GenerateAsync()
        {
            if (IsBusy) return;

            var row = Selected;
            if (row is null) { ShowError("Select an employee to generate their pay run."); return; }

            var employee = _employees.FirstOrDefault(e => e.EmployeeId == row.EmployeeId);

            var fields = new List<FieldSpec>
            {
                new("allowances", "Allowances", FieldKind.Money) { Value = 0m, Minimum = 0m },
                new("other", "Other deductions", FieldKind.Money)
                {
                    Value = 0m, Minimum = 0m,
                    Hint = "A cash advance, a loss or a loan - anything outside SSS, PhilHealth, " +
                           "Pag-IBIG and withholding tax, which are calculated automatically."
                },
                new("notes", "Notes", FieldKind.Multiline) { MaxLength = 300 }
            };

            var subtitle = $"{row.EmployeeName}: {row.TotalRegularHours:N2} regular hrs, " +
                           $"{row.TotalOvertimeHours:N2} overtime hrs, {UiKit.Date(_from.Value)} – " +
                           $"{UiKit.Date(_to.Value)}.";

            PayrollDto? generated = null;

            var saved = EditDialog.Run(this, "Generate payroll", subtitle, fields, async f =>
            {
                var result = await Session.Payroll.GenerateAsync(new GeneratePayrollDto
                {
                    EmployeeId = row.EmployeeId,
                    PeriodStart = _from.Value.Date,
                    PeriodEnd = _to.Value.Date,
                    Allowances = f.First(x => x.Key == "allowances").Decimal,
                    OtherDeductions = f.First(x => x.Key == "other").Decimal,
                    Notes = f.First(x => x.Key == "notes").Text
                });

                if (!result.IsSuccess) return result.ErrorMessage;
                generated = result.Value;
                return null;
            }, "Generate");

            if (!saved || generated is null) return;

            Notify($"Pay run generated for {row.EmployeeName}. Net pay {UiKit.Money(generated.NetPay)}.");

            // The breakdown answers "why this amount" better than the grid can, so it is shown
            // once - a modal, because a generated payslip is exactly what the operator cannot
            // already see on this screen.
            ReceiptDialog.Show(this, ReceiptBuilder.ForPayroll(Session, generated));

            // Generating is the Draft step; Payroll Records is where it gets reviewed and,
            // for an administrator, finalized - so that is where the operator lands next
            // rather than staying on a form whose job is already done.
            RequestNavigation("payroll", "records");
        }
    }
}
