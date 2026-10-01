using System.Drawing;
using System.Globalization;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Time in/out, hours worked and status, per employee per day. Feeds Payroll Calculation -
    /// a run generated from attendance reads these rather than a typed hour count.
    ///
    /// Recording and correcting attendance is a Manager/Admin action; the toolbar reflects that
    /// as a courtesy, and the server refuses the same operations with 403 regardless.
    /// </summary>
    internal sealed class AttendancePage : CrudPageBase<AttendanceDto>
    {
        private static readonly string[] Statuses = { "Present", "Absent", "Late", "Leave" };

        private readonly ComboBox _employeeFilter;
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly Label _summary;
        private List<EmployeeDto> _employees = new();

        public AttendancePage(FitCoreSession session)
            : base(session, "Attendance", "Time in/out and daily hours, which Payroll Calculation reads.",
                   "attendance record", "Employee name or code")
        {
            FilterBar.Controls.Add(UiKit.FilterLabel("Employee"));
            _employeeFilter = UiKit.Select(190);
            _employeeFilter.DisplayMember = "Value";
            _employeeFilter.ValueMember = "Key";
            _employeeFilter.SelectedIndexChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_employeeFilter);

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            _from = UiKit.DatePicker(DateTime.Today.AddDays(-30));
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

        private bool CanRecord => Session.CurrentUser?.IsManagerOrAbove == true;

        protected override bool SupportsAdd => CanRecord;
        protected override bool SupportsEdit => CanRecord;
        protected override bool SupportsDelete => CanRecord;

        protected override string DeleteConsequence =>
            "Deleting an attendance record removes it from any pay run generated from " +
            "attendance going forward. This cannot be undone.";

        protected override string EmptyHeadline => "No attendance recorded";
        protected override string EmptyDetail => CanRecord
            ? "Record time in/out for the period selected above."
            : "Nothing has been recorded for this period yet.";

        protected override async Task<List<AttendanceDto>?> FetchAsync()
        {
            _employees = Unwrap(await Session.Employees.GetActiveAsync()) ?? new();

            var options = new List<KeyValuePair<string, string>> { new("", "All employees") };
            options.AddRange(_employees.Select(e =>
                new KeyValuePair<string, string>(e.EmployeeId.ToString(), $"{e.FullName} ({e.EmployeeCode})")));

            if (_employeeFilter.DataSource is null)
            {
                _employeeFilter.DataSource = options;
            }

            int? employeeId = int.TryParse(_employeeFilter.SelectedValue?.ToString(), out var id) && id > 0
                ? id : null;

            return Unwrap(await Session.Attendance.GetAllAsync(employeeId, _from.Value.Date, _to.Value.Date));
        }

        protected override void DefineColumns()
        {
            Column(nameof(AttendanceDto.EmployeeName), "Employee", 130);
            DateColumn(nameof(AttendanceDto.Date), "Date", 80);
            Column(nameof(AttendanceDto.TimeIn), "Time in", 70, "HH:mm");
            Column(nameof(AttendanceDto.TimeOut), "Time out", 70, "HH:mm");
            Column(nameof(AttendanceDto.RegularHours), "Regular hrs", 70, "N2", rightAlign: true);
            Column(nameof(AttendanceDto.OvertimeHours), "OT hrs", 65, "N2", rightAlign: true);
            StatusColumn(nameof(AttendanceDto.Status), "Status", 80);
            Column(nameof(AttendanceDto.RecordedBy), "Recorded by", 110);
        }

        protected override bool Matches(AttendanceDto a, string term) =>
            (a.EmployeeName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (a.EmployeeCode ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(AttendanceDto a) =>
            $"{a.EmployeeName}'s attendance for {UiKit.Date(a.Date)}";

        protected override void AfterLoad()
        {
            var totalRegular = Items.Sum(a => a.RegularHours);
            var totalOvertime = Items.Sum(a => a.OvertimeHours);

            _summary.Text = $"{Items.Count:N0} record(s)      " +
                            $"regular {totalRegular:N1} hrs      overtime {totalOvertime:N1} hrs";
        }

        private static string? ParseTime(string text, out TimeSpan value)
        {
            if (string.IsNullOrWhiteSpace(text)) { value = default; return null; }

            if (!TimeSpan.TryParseExact(text.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out value) &&
                !TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out value))
            {
                return "Enter a time as HH:mm, e.g. 08:00.";
            }

            return null;
        }

        private List<FieldSpec> Fields(AttendanceDto? a)
        {
            return new List<FieldSpec>
            {
                new("employee", "Employee", FieldKind.Combo)
                {
                    Required = true,
                    Value = a?.EmployeeId.ToString(),
                    ReadOnly = a is not null,
                    Options = _employees
                        .Select(e => new KeyValuePair<string, string>(
                            e.EmployeeId.ToString(), $"{e.FullName} ({e.EmployeeCode})"))
                        .ToList()
                },
                new("date", "Date", FieldKind.Date)
                {
                    Required = true,
                    Value = a?.Date ?? DateTime.Today,
                    Validate = f => f.Date.Date > DateTime.Today ? "The date cannot be in the future." : null
                },
                new("status", "Status", FieldKind.Combo)
                {
                    Required = true,
                    Value = a?.Status ?? "Present",
                    Options = Statuses.Select(s => new KeyValuePair<string, string>(s, s)).ToList(),
                    Hint = "Absent and Leave carry no hours."
                },
                new("timeIn", "Time in (HH:mm)")
                {
                    Value = a?.TimeIn?.ToString("HH:mm"),
                    MaxLength = 5,
                    Validate = f => ParseTime(f.Text, out _)
                },
                new("timeOut", "Time out (HH:mm)")
                {
                    Value = a?.TimeOut?.ToString("HH:mm"),
                    MaxLength = 5,
                    Hint = "Hours beyond the standard 8-hour shift count as overtime.",
                    Validate = f => ParseTime(f.Text, out _)
                },
                new("notes", "Notes", FieldKind.Multiline) { Value = a?.Notes, MaxLength = 300 }
            };
        }

        /// <summary>
        /// Combines the Date field with an HH:mm field into one DateTime, or null when left
        /// blank. Called only after EditDialog's own field validation has already passed, so
        /// a non-blank value here is always a well-formed time.
        /// </summary>
        private static DateTime? CombineDateAndTime(DateTime date, string timeText)
        {
            if (string.IsNullOrWhiteSpace(timeText)) return null;

            ParseTime(timeText, out var time);
            return date.Date.Add(time);
        }

        protected override Task<bool> OnAddAsync()
        {
            if (_employees.Count == 0)
            {
                ShowError("There are no active employees to record attendance for.");
                return Task.FromResult(false);
            }

            var saved = EditDialog.Run(this, "Record attendance",
                "Regular and overtime hours are calculated from time in/out.",
                Fields(null), async f =>
                {
                    var date = f.First(x => x.Key == "date").Date;

                    var result = await Session.Attendance.CreateAsync(new CreateAttendanceDto
                    {
                        EmployeeId = int.Parse(f.First(x => x.Key == "employee").ComboValue ?? "0"),
                        Date = date,
                        TimeIn = CombineDateAndTime(date, f.First(x => x.Key == "timeIn").Text),
                        TimeOut = CombineDateAndTime(date, f.First(x => x.Key == "timeOut").Text),
                        Status = f.First(x => x.Key == "status").ComboValue ?? "Present",
                        Notes = f.First(x => x.Key == "notes").Text
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Record attendance");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(AttendanceDto a)
        {
            var saved = EditDialog.Run(this, $"Edit attendance — {a.EmployeeName}",
                $"{UiKit.Date(a.Date)}.", Fields(a), async f =>
                {
                    var date = f.First(x => x.Key == "date").Date;

                    var result = await Session.Attendance.UpdateAsync(a.AttendanceId, new UpdateAttendanceDto
                    {
                        EmployeeId = a.EmployeeId,
                        Date = date,
                        TimeIn = CombineDateAndTime(date, f.First(x => x.Key == "timeIn").Text),
                        TimeOut = CombineDateAndTime(date, f.First(x => x.Key == "timeOut").Text),
                        Status = f.First(x => x.Key == "status").ComboValue ?? "Present",
                        Notes = f.First(x => x.Key == "notes").Text
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(AttendanceDto a)
        {
            var result = await Session.Attendance.DeleteAsync(a.AttendanceId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }
    }
}
