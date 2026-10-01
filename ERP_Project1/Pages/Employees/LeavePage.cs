using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Leave: time an employee is away, and whether they are paid for it.
    ///
    /// Belongs to Employee Management rather than Payroll, because granting leave is a
    /// staffing decision. Payroll reads the outcome - approved paid leave counts towards the
    /// hours a run is calculated from, and approved unpaid leave does not.
    /// </summary>
    internal sealed class LeavePage : CrudPageBase<LeaveRequestDto>
    {
        private readonly ComboBox _employee;
        private readonly ComboBox _status;
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;

        private readonly Label _pending;
        private readonly Label _pendingHint;
        private readonly Label _approved;
        private readonly Label _approvedHint;
        private readonly Label _paidDays;
        private readonly Label _paidDaysHint;
        private readonly Label _unpaidDays;
        private readonly Label _unpaidDaysHint;

        private List<EmployeeDto> _employees = new();
        private List<string> _types = new();

        public LeavePage(FitCoreSession session)
            : base(session, "Leave",
                   "Requests, approvals, and the days each employee has taken.",
                   "leave request", "Employee, type or reason")
        {
            var today = DateTime.Today;

            _from = UiKit.DatePicker(new DateTime(today.Year, 1, 1));
            _to = UiKit.DatePicker(new DateTime(today.Year, 12, 31));

            _employee = UiKit.Select(200);
            _employee.Items.Add("All employees");
            _employee.SelectedIndex = 0;
            _employee.SelectedIndexChanged += async (_, _) => await LoadAsync();

            _status = UiKit.Select(130);
            _status.Items.Add("All statuses");
            _status.Items.AddRange(LeaveStatuses.All.Cast<object>().ToArray());
            _status.SelectedIndex = 0;
            _status.SelectedIndexChanged += async (_, _) => await LoadAsync();

            FilterBar.Controls.Add(UiKit.FilterLabel("Employee"));
            FilterBar.Controls.Add(_employee);
            FilterBar.Controls.Add(UiKit.FilterLabel("Status"));
            FilterBar.Controls.Add(_status);
            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);

            AddAction("Balance", ButtonTone.Secondary, ShowBalanceAsync, 92);
            AddAction("Decide", ButtonTone.Primary, DecideAsync, 86);

            StatsRow.Controls.Add(UiKit.StatCard("Awaiting a decision", out _pending, out _pendingHint, UiTheme.Warning));
            StatsRow.Controls.Add(UiKit.StatCard("Approved", out _approved, out _approvedHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Paid days", out _paidDays, out _paidDaysHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("Unpaid days", out _unpaidDays, out _unpaidDaysHint, UiTheme.Neutral));
        }

        protected override string DeleteConsequence =>
            "The request is removed. Approved leave cannot be deleted - it is part of the " +
            "attendance record and payroll may already have counted it - so cancel it instead.";

        protected override string EmptyHeadline => "No leave recorded";
        protected override string EmptyDetail =>
            "Record an absence here so payroll knows whether to pay for those days.";

        private int? SelectedEmployeeId =>
            _employee.SelectedIndex <= 0
                ? null
                : _employees[_employee.SelectedIndex - 1].EmployeeId;

        protected override async Task<List<LeaveRequestDto>?> FetchAsync()
        {
            if (_employees.Count == 0)
            {
                _employees = Unwrap(await Session.Employees.GetActiveAsync()) ?? new List<EmployeeDto>();

                _employee.Items.AddRange(_employees
                    .Select(e => (object)$"{e.FirstName} {e.LastName}".Trim())
                    .ToArray());
            }

            if (_types.Count == 0)
            {
                _types = Unwrap(await Session.Leave.GetTypesAsync()) ?? new List<string>();
            }

            var requests = Unwrap(await Session.Leave.GetAllAsync(
                SelectedEmployeeId,
                _from.Value.Date,
                _to.Value.Date,
                _status.SelectedIndex <= 0 ? null : (string)_status.SelectedItem!));

            if (requests is not null)
            {
                var pending = requests.Where(r => r.Status == LeaveStatuses.Pending).ToList();
                var approved = requests.Where(r => r.Status == LeaveStatuses.Approved).ToList();

                _pending.Text = pending.Count.ToString("N0");
                _pending.ForeColor = pending.Count > 0 ? UiTheme.Warning : UiTheme.TextPrimary;
                _pendingHint.Text = pending.Count == 0
                    ? "nothing outstanding"
                    : $"{pending.Sum(r => r.Days):N1} day(s) requested";

                _approved.Text = approved.Count.ToString("N0");
                _approvedHint.Text = $"{approved.Sum(r => r.Days):N1} day(s) granted";

                _paidDays.Text = approved.Where(r => r.IsPaid).Sum(r => r.Days).ToString("N1");
                _paidDaysHint.Text = "counted as worked";

                _unpaidDays.Text = approved.Where(r => !r.IsPaid).Sum(r => r.Days).ToString("N1");
                _unpaidDaysHint.Text = "not paid for";
            }

            return requests;
        }

        protected override void DefineColumns()
        {
            Column(nameof(LeaveRequestDto.EmployeeName), "Employee", 150);
            Column(nameof(LeaveRequestDto.LeaveType), "Type", 110);
            DateColumn(nameof(LeaveRequestDto.StartDate), "From", 75);
            DateColumn(nameof(LeaveRequestDto.EndDate), "To", 75);
            Column(nameof(LeaveRequestDto.Days), "Days", 50, "N1", rightAlign: true);
            FlagColumn(nameof(LeaveRequestDto.IsPaid), "Paid", 55);
            StatusColumn(nameof(LeaveRequestDto.Status), "Status", 75);
            Column(nameof(LeaveRequestDto.Reason), "Reason", 160);
            Column(nameof(LeaveRequestDto.DecidedBy), "Decided by", 100);
        }

        protected override bool Matches(LeaveRequestDto r, string term) =>
            r.EmployeeName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            r.LeaveType.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            r.Reason.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            r.Status.Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(LeaveRequestDto r) =>
            $"{r.EmployeeName}'s {r.Days:N1}-day {r.LeaveType.ToLowerInvariant()} leave " +
            $"from {r.StartDate:d MMM yyyy}";

        private List<FieldSpec> Fields(LeaveRequestDto? request) => new()
        {
            new("employee", "Employee", FieldKind.Combo)
            {
                Required = true,
                Value = request?.EmployeeId.ToString(),
                ReadOnly = request is not null,
                Options = _employees
                    .Select(e => new KeyValuePair<string, string>(
                        e.EmployeeId.ToString(), $"{e.FirstName} {e.LastName}".Trim()))
                    .ToList()
            },
            new("type", "Leave type", FieldKind.Combo)
            {
                Required = true,
                Value = request?.LeaveType ?? _types.FirstOrDefault() ?? "Vacation",
                Options = _types.Select(t => new KeyValuePair<string, string>(t, t)).ToList()
            },
            new("start", "First day", FieldKind.Date)
                { Required = true, Value = request?.StartDate ?? DateTime.Today },
            new("end", "Last day", FieldKind.Date)
                { Required = true, Value = request?.EndDate ?? DateTime.Today },
            new("paid", "Paid leave", FieldKind.Check)
            {
                Value = request?.IsPaid ?? true,
                Hint = "Paid days count towards the hours payroll calculates from."
            },
            new("reason", "Reason", FieldKind.Multiline)
                { Value = request?.Reason, MaxLength = 300 }
        };

        protected override Task<bool> OnAddAsync()
        {
            if (_employees.Count == 0)
            {
                ShowError("There are no active employees to record leave for.");
                return Task.FromResult(false);
            }

            var saved = EditDialog.Run(this, "Record leave",
                "Weekends are not counted. The request starts as pending until somebody decides it.",
                Fields(null), async f =>
                {
                    var result = await Session.Leave.CreateAsync(new CreateLeaveRequestDto
                    {
                        EmployeeId = int.Parse(f.First(x => x.Key == "employee").ComboValue!),
                        LeaveType = f.First(x => x.Key == "type").Text,
                        StartDate = f.First(x => x.Key == "start").Date,
                        EndDate = f.First(x => x.Key == "end").Date,
                        IsPaid = f.First(x => x.Key == "paid").Flag,
                        Reason = f.First(x => x.Key == "reason").Text
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Record leave");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(LeaveRequestDto request)
        {
            if (request.Status != LeaveStatuses.Pending)
            {
                ShowError($"This request has been {request.Status.ToLowerInvariant()} and cannot " +
                          "be changed. Cancel it and raise a new one if the dates have moved.");

                return Task.FromResult(false);
            }

            var saved = EditDialog.Run(this, $"Edit {request.EmployeeName}'s leave", "",
                Fields(request), async f =>
                {
                    var result = await Session.Leave.UpdateAsync(
                        request.LeaveRequestId, new UpdateLeaveRequestDto
                        {
                            LeaveType = f.First(x => x.Key == "type").Text,
                            StartDate = f.First(x => x.Key == "start").Date,
                            EndDate = f.First(x => x.Key == "end").Date,
                            IsPaid = f.First(x => x.Key == "paid").Flag,
                            Reason = f.First(x => x.Key == "reason").Text
                        });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(LeaveRequestDto request)
        {
            var result = await Session.Leave.DeleteAsync(request.LeaveRequestId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        private async Task DecideAsync()
        {
            var request = Selected;

            if (request is null)
            {
                ShowError("Select the request to decide.");
                return;
            }

            if (request.Status != LeaveStatuses.Pending)
            {
                ShowError($"This request has already been {request.Status.ToLowerInvariant()}.");
                return;
            }

            var decided = EditDialog.Run(this,
                $"{request.EmployeeName} · {request.Days:N1} day(s) from {request.StartDate:d MMM}",
                $"{request.LeaveType}{(request.IsPaid ? ", paid" : ", unpaid")}. " +
                (string.IsNullOrWhiteSpace(request.Reason) ? "" : $"“{request.Reason}”"),
                new List<FieldSpec>
                {
                    new("approve", "Approve this request", FieldKind.Check) { Value = true },
                    new("notes", "Notes", FieldKind.Multiline)
                    {
                        MaxLength = 300,
                        Hint = "Required when refusing, so the employee is told something."
                    }
                },
                async f =>
                {
                    var approve = f.First(x => x.Key == "approve").Flag;
                    var notes = f.First(x => x.Key == "notes").Text;

                    if (!approve && string.IsNullOrWhiteSpace(notes))
                    {
                        return "Say why the request is being refused.";
                    }

                    var result = await Session.Leave.DecideAsync(
                        request.LeaveRequestId, approve, notes);

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Record decision");

            if (!decided) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"{request.EmployeeName}'s request has been decided.");
            }, "Refreshing…");
        }

        private async Task ShowBalanceAsync()
        {
            var employeeId = SelectedEmployeeId ?? Selected?.EmployeeId;

            if (employeeId is null)
            {
                ShowError("Choose an employee, or select one of their requests.");
                return;
            }

            await GuardAsync(async () =>
            {
                var balance = Unwrap(await Session.Leave.GetBalanceAsync(
                    employeeId.Value, _from.Value.Year));

                if (balance is null) return;

                if (balance.ByType.Count == 0)
                {
                    UiKit.Info(
                        $"{balance.EmployeeName} has taken no leave in {balance.Year}.",
                        "Leave balance");
                    return;
                }

                ListDialog.Show(this,
                    $"{balance.EmployeeName} · {balance.Year}",
                    $"{balance.PaidDaysTaken:N1} paid, {balance.UnpaidDaysTaken:N1} unpaid, " +
                    $"{balance.PendingDays:N1} awaiting a decision",
                    balance.ByType.Select(t => new
                    {
                        Type = t.Label,
                        Days = t.Value.ToString("N1"),
                        Requests = t.Count
                    }).ToList());
            }, "Loading…");
        }
    }
}
