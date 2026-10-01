using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Read-only: per-employee attendance totals for a period, the same numbers Payroll
    /// Calculation reads when it generates a run. Nothing here is editable - attendance itself
    /// is recorded and corrected on the Employees module's Attendance tab.
    /// </summary>
    internal sealed class AttendanceSummaryPage : CrudPageBase<AttendancePeriodSummaryDto>
    {
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly Label _summary;
        private List<EmployeeDto> _employees = new();

        public AttendanceSummaryPage(FitCoreSession session)
            : base(session, "Attendance Summary",
                   "Regular and overtime hours per employee for the period selected.",
                   "employee", "Employee name or code")
        {
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

        protected override string EmptyHeadline => "No attendance in this period";
        protected override string EmptyDetail =>
            "Record attendance on the Employees module's Attendance tab, then come back here.";

        protected override async Task<List<AttendancePeriodSummaryDto>?> FetchAsync()
        {
            _employees = Unwrap(await Session.Employees.GetActiveAsync()) ?? new();
            return await PayrollAttendanceSupport.LoadSummariesAsync(
                Session, _employees, _from.Value.Date, _to.Value.Date);
        }

        protected override void DefineColumns()
        {
            Column(nameof(AttendancePeriodSummaryDto.EmployeeName), "Employee", 150);
            Column(nameof(AttendancePeriodSummaryDto.DaysPresent), "Present", 60, rightAlign: true);
            Column(nameof(AttendancePeriodSummaryDto.DaysAbsent), "Absent", 60, rightAlign: true);
            Column(nameof(AttendancePeriodSummaryDto.DaysLate), "Late", 55, rightAlign: true);
            Column(nameof(AttendancePeriodSummaryDto.DaysOnLeave), "Leave", 55, rightAlign: true);
            Column(nameof(AttendancePeriodSummaryDto.TotalRegularHours), "Regular hrs", 75, "N2", rightAlign: true);
            Column(nameof(AttendancePeriodSummaryDto.TotalOvertimeHours), "OT hrs", 70, "N2", rightAlign: true);
        }

        protected override bool Matches(AttendancePeriodSummaryDto a, string term) =>
            (a.EmployeeName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override void AfterLoad()
        {
            _summary.Text = $"{Items.Count:N0} employee(s) with recorded attendance      " +
                            $"total regular {Items.Sum(a => a.TotalRegularHours):N1} hrs      " +
                            $"total overtime {Items.Sum(a => a.TotalOvertimeHours):N1} hrs";
        }
    }
}
