using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    // -------------------------------------------------------------------------- Employees

    /// <summary>
    /// Small Enterprise and above. A Micro user never sees the navigation entry, and the API
    /// answers 403 for every endpoint below regardless.
    /// </summary>
    internal sealed class EmployeesPage : CrudPageBase<EmployeeDto>
    {
        private readonly Label _summary;
        private readonly ComboBox _statusFilter;
        private List<EmployeeDto> _all = new();

        public EmployeesPage(FitCoreSession session)
            : base(session, "Employee Records", "Your staff, and the salary payroll is calculated from.",
                   "employee", "Name, code, position or email")
        {
            AddAction("Pay history", ButtonTone.Secondary, () => ShowPayrollsAsync(), 110);

            FilterBar.Controls.Add(UiKit.FilterLabel("Status"));

            _statusFilter = UiKit.Select(150);
            _statusFilter.DisplayMember = "Value";
            _statusFilter.ValueMember = "Key";
            _statusFilter.DataSource = new List<KeyValuePair<string, string>>
            {
                new("", "All employees"),
                new("Active", "Active"),
                new("Inactive", "Inactive"),
                new("Terminated", "Terminated")
            };
            _statusFilter.SelectedIndexChanged += (_, _) => ApplyStatusFilter();
            FilterBar.Controls.Add(_statusFilter);

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

        protected override string DeleteConsequence =>
            "An employee who has a pay run cannot be deleted, because that would destroy " +
            "their pay history. Set their status to Terminated instead to keep the record.";

        protected override string EmptyHeadline => "No employees yet";
        protected override string EmptyDetail =>
            "Add your trainers, receptionists and managers here. Payroll is calculated from " +
            "the basic salary you record against each of them.";

        protected override async Task<List<EmployeeDto>?> FetchAsync() =>
            Unwrap(await Session.Employees.GetAllAsync());

        public override async Task LoadAsync()
        {
            await base.LoadAsync();
            _all = Items.ToList();
            ApplyStatusFilter();
        }

        private void ApplyStatusFilter()
        {
            var wanted = _statusFilter.SelectedValue?.ToString() ?? "";

            Items = string.IsNullOrWhiteSpace(wanted)
                ? _all.ToList()
                : _all.Where(e => string.Equals(e.Status, wanted, StringComparison.OrdinalIgnoreCase)).ToList();

            ApplyFilter();
        }

        protected override void DefineColumns()
        {
            Column(nameof(EmployeeDto.EmployeeCode), "Code", 70);
            Column(nameof(EmployeeDto.FullName), "Name", 140);
            Column(nameof(EmployeeDto.Position), "Position", 120);
            Column(nameof(EmployeeDto.Department), "Department", 100);
            Column(nameof(EmployeeDto.Phone), "Phone", 85);
            DateColumn(nameof(EmployeeDto.HireDate), "Hired", 80);
            MoneyColumn(nameof(EmployeeDto.BasicSalary), "Basic salary", 90);
            StatusColumn(nameof(EmployeeDto.Status), "Status", 80);
        }

        protected override bool Matches(EmployeeDto e, string term) =>
            (e.FullName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.EmployeeCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (e.Position ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (e.Department ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (e.Email ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(EmployeeDto e) =>
            $"{e.FullName} ({e.EmployeeCode})";

        protected override void AfterLoad()
        {
            var active = _all.Count(e => string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase));
            var cost = _all.Where(e => string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase))
                           .Sum(e => e.BasicSalary);
            var departments = _all.Select(e => e.Department)
                                  .Where(d => !string.IsNullOrWhiteSpace(d))
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .Count();

            _summary.Text = $"{_all.Count:N0} employee(s)      {active:N0} active      " +
                            $"{departments:N0} department(s)      " +
                            $"monthly basic pay {UiKit.Money(cost)}";
        }

        private static List<FieldSpec> Fields(EmployeeDto? e) => new()
        {
            new("code", "Employee code")
            {
                Required = true, Value = e?.EmployeeCode, MaxLength = 30,
                Hint = "Must be unique, e.g. EMP-001."
            },
            new("first", "First name") { Required = true, Value = e?.FirstName, MaxLength = 100 },
            new("last", "Last name") { Required = true, Value = e?.LastName, MaxLength = 100 },
            new("position", "Position") { Value = e?.Position, MaxLength = 100 },
            new("department", "Department") { Value = e?.Department, MaxLength = 100 },
            new("phone", "Phone") { Value = e?.Phone, MaxLength = 30 },
            new("email", "Email", FieldKind.Email) { Value = e?.Email, MaxLength = 150 },
            new("hired", "Hire date", FieldKind.Date)
            {
                Required = true,
                Value = e?.HireDate ?? DateTime.Today,
                Validate = f => f.Date.Date > DateTime.Today
                    ? "The hire date cannot be in the future."
                    : null
            },
            new("salary", "Basic salary", FieldKind.Money)
            {
                Required = true, Value = e?.BasicSalary ?? 0m, Minimum = 0m,
                Hint = "Payroll is calculated from this."
            }
        };

        protected override Task<bool> OnAddAsync()
        {
            var saved = EditDialog.Run(this, "Add employee",
                "Payroll is calculated from the basic salary.", Fields(null), async f =>
                {
                    var result = await Session.Employees.CreateAsync(new CreateEmployeeDto
                    {
                        EmployeeCode = f.First(x => x.Key == "code").Text,
                        FirstName = f.First(x => x.Key == "first").Text,
                        LastName = f.First(x => x.Key == "last").Text,
                        Position = f.First(x => x.Key == "position").Text,
                        Department = f.First(x => x.Key == "department").Text,
                        Phone = f.First(x => x.Key == "phone").Text,
                        Email = f.First(x => x.Key == "email").Text,
                        HireDate = f.First(x => x.Key == "hired").Date,
                        BasicSalary = f.First(x => x.Key == "salary").Decimal
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create employee");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(EmployeeDto e)
        {
            var fields = Fields(e);
            fields.Add(new FieldSpec("status", "Status", FieldKind.Combo)
            {
                Required = true,
                Value = e.Status,
                Options = new()
                {
                    new("Active", "Active"),
                    new("Inactive", "Inactive"),
                    new("Terminated", "Terminated")
                },
                Hint = "Only Active employees can be chosen for a new pay run."
            });

            var saved = EditDialog.Run(this, $"Edit {e.FullName}",
                $"Employee #{e.EmployeeId}, hired {UiKit.Date(e.HireDate)}.", fields, async f =>
                {
                    var result = await Session.Employees.UpdateAsync(e.EmployeeId, new UpdateEmployeeDto
                    {
                        EmployeeCode = f.First(x => x.Key == "code").Text,
                        FirstName = f.First(x => x.Key == "first").Text,
                        LastName = f.First(x => x.Key == "last").Text,
                        Position = f.First(x => x.Key == "position").Text,
                        Department = f.First(x => x.Key == "department").Text,
                        Phone = f.First(x => x.Key == "phone").Text,
                        Email = f.First(x => x.Key == "email").Text,
                        HireDate = f.First(x => x.Key == "hired").Date,
                        BasicSalary = f.First(x => x.Key == "salary").Decimal,
                        Status = f.First(x => x.Key == "status").ComboValue ?? "Active"
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(EmployeeDto e)
        {
            var result = await Session.Employees.DeleteAsync(e.EmployeeId);

            // The server refuses to delete an employee who has a pay run, because that would
            // destroy their pay history.
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        private async Task ShowPayrollsAsync()
        {
            if (IsBusy) return;

            var e = Selected;
            if (e is null) { ShowError("Select an employee to see their pay history."); return; }

            if (!Session.Can(Modules.Payroll))
            {
                ShowError("Your account does not include Payroll.");
                return;
            }

            await GuardAsync(async () =>
            {
                var runs = Unwrap(await Session.Payroll.GetAllAsync());
                if (runs is null) return;

                var theirs = runs.Where(r => r.EmployeeId == e.EmployeeId)
                                 .OrderByDescending(r => r.PeriodStart)
                                 .ToList();

                if (theirs.Count == 0)
                {
                    ShowError($"No pay runs have been recorded for {e.FullName} yet.");
                    return;
                }

                using var dialog = new ListDialog($"Pay history — {e.FullName}", theirs, grid =>
                {
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        DataPropertyName = nameof(PayrollDto.PeriodStart), HeaderText = "From",
                        FillWeight = 70, DefaultCellStyle = { Format = "d MMM yyyy" }
                    });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        DataPropertyName = nameof(PayrollDto.PeriodEnd), HeaderText = "To",
                        FillWeight = 70, DefaultCellStyle = { Format = "d MMM yyyy" }
                    });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        DataPropertyName = nameof(PayrollDto.GrossPay), HeaderText = "Gross", FillWeight = 70,
                        DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
                    });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        DataPropertyName = nameof(PayrollDto.Deductions), HeaderText = "Deductions", FillWeight = 70,
                        DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
                    });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        DataPropertyName = nameof(PayrollDto.NetPay), HeaderText = "Net", FillWeight = 70,
                        DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
                    });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        Name = nameof(PayrollDto.Status),
                        DataPropertyName = nameof(PayrollDto.Status), HeaderText = "Status", FillWeight = 60
                    });
                },
                $"   {theirs.Count:N0} pay run(s)      total net {UiKit.Money(theirs.Sum(r => r.NetPay))}");

                dialog.ShowDialog(this);
            }, "Loading pay history…");
        }
    }

    // ---------------------------------------------------------------------------- Payroll

    /// <summary>
    /// Pay runs. Gross, overtime and net are calculated on the server from the inputs below -
    /// this screen never computes pay, so the desktop cannot dictate what anyone is paid.
    /// </summary>
    internal sealed class PayrollPage : CrudPageBase<PayrollDto>
    {
        private readonly Label _summary;
        private readonly ComboBox _statusFilter;
        private List<EmployeeDto> _employees = new();
        private List<PayrollDto> _all = new();

        public PayrollPage(FitCoreSession session)
            : base(session, "Payroll Records", "Pay runs, with gross and net calculated by the server.",
                   "pay run", "Employee name or code")
        {
            AddAction("Set status", ButtonTone.Secondary, () => SetStatusAsync(), 104);

            FilterBar.Controls.Add(UiKit.FilterLabel("Status"));

            _statusFilter = UiKit.Select(150);
            _statusFilter.DisplayMember = "Value";
            _statusFilter.ValueMember = "Key";
            _statusFilter.DataSource = new List<KeyValuePair<string, string>>
            {
                new("", "All pay runs"),
                new("Draft", "Draft"),
                new("Approved", "Approved"),
                new("Paid", "Paid")
            };
            _statusFilter.SelectedIndexChanged += (_, _) => ApplyStatusFilter();
            FilterBar.Controls.Add(_statusFilter);

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

        protected override string CreatedMessage => "Pay run created successfully.";
        protected override string UpdatedMessage => "Pay run updated successfully.";
        protected override string DeletedMessage => "Pay run deleted successfully.";

        protected override string DeleteConsequence =>
            "A pay run that has been marked Paid cannot be deleted. Deleting a draft removes " +
            "it from payroll totals. This cannot be undone.";

        protected override string EmptyHeadline => "No pay runs yet";
        protected override string EmptyDetail =>
            "Create a pay run for an employee and a period. The server calculates gross, " +
            "overtime and net, and refuses any period that overlaps one already recorded.";

        protected override async Task<List<PayrollDto>?> FetchAsync()
        {
            _employees = Unwrap(await Session.Employees.GetActiveAsync()) ?? new();
            return Unwrap(await Session.Payroll.GetAllAsync());
        }

        public override async Task LoadAsync()
        {
            await base.LoadAsync();
            _all = Items.ToList();
            ApplyStatusFilter();
        }

        private void ApplyStatusFilter()
        {
            var wanted = _statusFilter.SelectedValue?.ToString() ?? "";

            Items = string.IsNullOrWhiteSpace(wanted)
                ? _all.ToList()
                : _all.Where(p => string.Equals(p.Status, wanted, StringComparison.OrdinalIgnoreCase)).ToList();

            ApplyFilter();
        }

        protected override void DefineColumns()
        {
            Column(nameof(PayrollDto.EmployeeName), "Employee", 130);
            Column(nameof(PayrollDto.EmployeeCode), "Code", 70);
            DateColumn(nameof(PayrollDto.PeriodStart), "From", 75);
            DateColumn(nameof(PayrollDto.PeriodEnd), "To", 75);
            MoneyColumn(nameof(PayrollDto.BasicSalary), "Basic", 75);
            MoneyColumn(nameof(PayrollDto.Allowances), "Allowances", 78);
            MoneyColumn(nameof(PayrollDto.OvertimePay), "Overtime", 75);
            MoneyColumn(nameof(PayrollDto.Deductions), "Deductions", 78);
            MoneyColumn(nameof(PayrollDto.GrossPay), "Gross", 80);
            MoneyColumn(nameof(PayrollDto.NetPay), "Net", 80);
            StatusColumn(nameof(PayrollDto.Status), "Status", 75);
        }

        protected override bool Matches(PayrollDto p, string term) =>
            (p.EmployeeName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (p.EmployeeCode ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(PayrollDto p) =>
            $"the pay run for {p.EmployeeName} ({UiKit.Date(p.PeriodStart)} – {UiKit.Date(p.PeriodEnd)}, " +
            $"net {UiKit.Money(p.NetPay)})";

        protected override void AfterLoad()
        {
            _ = RefreshSummaryAsync();
        }

        private async Task RefreshSummaryAsync()
        {
            var summary = Unwrap(await Session.Payroll.GetSummaryAsync());
            if (summary is null || IsDisposed) return;

            _summary.Text =
                $"{summary.RunCount:N0} run(s)      {summary.EmployeeCount:N0} employee(s)      " +
                $"gross {UiKit.Money(summary.TotalGross)}      net {UiKit.Money(summary.TotalNet)}      " +
                $"paid {UiKit.Money(summary.TotalPaid)}      " +
                $"outstanding {UiKit.Money(summary.TotalOutstanding)}";
        }

        private List<FieldSpec> Fields(PayrollDto? p)
        {
            var fields = new List<FieldSpec>();

            if (p is null)
            {
                fields.Add(new FieldSpec("employee", "Employee", FieldKind.Combo)
                {
                    Required = true,
                    Options = _employees
                        .Select(e => new KeyValuePair<string, string>(
                            e.EmployeeId.ToString(),
                            $"{e.FullName} ({e.EmployeeCode}) — {UiKit.Money(e.BasicSalary)}"))
                        .ToList()
                });
            }

            fields.Add(new FieldSpec("from", "Period start", FieldKind.Date)
            {
                Required = true,
                Value = p?.PeriodStart ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
            });

            fields.Add(new FieldSpec("to", "Period end", FieldKind.Date)
            {
                Required = true,
                Value = p?.PeriodEnd ?? DateTime.Today,
                Hint = "Periods must not overlap another run for the same employee."
            });

            if (p is not null)
            {
                fields.Add(new FieldSpec("basic", "Basic salary", FieldKind.Money)
                    { Value = p.BasicSalary, Minimum = 0m });
            }

            fields.Add(new FieldSpec("allowances", "Allowances", FieldKind.Money)
                { Value = p?.Allowances ?? 0m, Minimum = 0m });
            fields.Add(new FieldSpec("deductions", "Deductions", FieldKind.Money)
                { Value = p?.Deductions ?? 0m, Minimum = 0m });
            fields.Add(new FieldSpec("othours", "Overtime hours", FieldKind.Number)
                { Value = p?.OvertimeHours ?? 0m, Minimum = 0m, Maximum = 400m });
            fields.Add(new FieldSpec("otrate", "Overtime rate per hour", FieldKind.Money)
                { Value = p?.OvertimeRate ?? 0m, Minimum = 0m });
            fields.Add(new FieldSpec("notes", "Notes", FieldKind.Multiline)
                { Value = p?.Notes, MaxLength = 300 });

            return fields;
        }

        /// <summary>Shared by create and edit: the period has to make sense before it is sent.</summary>
        private static string? CheckPeriod(IList<FieldSpec> f)
        {
            var from = f.First(x => x.Key == "from").Date.Date;
            var to = f.First(x => x.Key == "to").Date.Date;

            if (to < from) return "The period end cannot be before the period start.";
            if ((to - from).TotalDays > 400) return "A pay period cannot be longer than a year.";

            return null;
        }

        protected override Task<bool> OnAddAsync()
        {
            if (_employees.Count == 0)
            {
                ShowError("There are no active employees. Add one on the Employees module, or " +
                          "set an existing employee back to Active, before creating a pay run.");
                return Task.FromResult(false);
            }

            var saved = EditDialog.Run(this, "New pay run",
                "Gross, overtime and net are calculated by the server. A period that overlaps " +
                "an existing run for the same employee is refused.",
                Fields(null), async f =>
                {
                    if (CheckPeriod(f) is { } problem) return problem;

                    var result = await Session.Payroll.CreateAsync(new CreatePayrollDto
                    {
                        EmployeeId = int.Parse(f.First(x => x.Key == "employee").ComboValue ?? "0"),
                        PeriodStart = f.First(x => x.Key == "from").Date,
                        PeriodEnd = f.First(x => x.Key == "to").Date,
                        Allowances = f.First(x => x.Key == "allowances").Decimal,
                        Deductions = f.First(x => x.Key == "deductions").Decimal,
                        OvertimeHours = f.First(x => x.Key == "othours").Decimal,
                        OvertimeRate = f.First(x => x.Key == "otrate").Decimal,
                        Notes = f.First(x => x.Key == "notes").Text
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create pay run");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(PayrollDto p)
        {
            if (string.Equals(p.Status, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("A pay run that has been paid can no longer be edited. Set its " +
                          "status back to Draft first if it was recorded in error.");
                return Task.FromResult(false);
            }

            var saved = EditDialog.Run(this, $"Edit pay run — {p.EmployeeName}",
                $"{UiKit.Date(p.PeriodStart)} to {UiKit.Date(p.PeriodEnd)}, currently {p.Status}.",
                Fields(p), async f =>
                {
                    if (CheckPeriod(f) is { } problem) return problem;

                    var result = await Session.Payroll.UpdateAsync(p.PayrollId, new UpdatePayrollDto
                    {
                        PeriodStart = f.First(x => x.Key == "from").Date,
                        PeriodEnd = f.First(x => x.Key == "to").Date,
                        BasicSalary = f.First(x => x.Key == "basic").Decimal,
                        Allowances = f.First(x => x.Key == "allowances").Decimal,
                        Deductions = f.First(x => x.Key == "deductions").Decimal,
                        OvertimeHours = f.First(x => x.Key == "othours").Decimal,
                        OvertimeRate = f.First(x => x.Key == "otrate").Decimal,
                        Notes = f.First(x => x.Key == "notes").Text
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(PayrollDto p)
        {
            var result = await Session.Payroll.DeleteAsync(p.PayrollId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        private async Task SetStatusAsync()
        {
            if (IsBusy) return;

            var p = Selected;
            if (p is null) { ShowError("Select a pay run to change its status."); return; }

            var fields = new List<FieldSpec>
            {
                new("status", "Status", FieldKind.Combo)
                {
                    Required = true,
                    Value = p.Status,
                    Options = new()
                    {
                        new("Draft", "Draft"),
                        new("Approved", "Approved"),
                        new("Paid", "Paid")
                    },
                    Hint = "A Paid run is locked against editing and deletion."
                }
            };

            var changed = EditDialog.Run(this, $"Status — {p.EmployeeName}",
                $"{UiKit.Date(p.PeriodStart)} to {UiKit.Date(p.PeriodEnd)}, " +
                $"net {UiKit.Money(p.NetPay)}.", fields, async f =>
                {
                    var next = f.First(x => x.Key == "status").ComboValue ?? "Draft";

                    var result = await Session.Payroll.SetStatusAsync(p.PayrollId,
                        new UpdatePayrollStatusDto { Status = next });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Apply");

            if (!changed) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify("Pay run status updated successfully.");
            }, "Refreshing…");
        }
    }
}
