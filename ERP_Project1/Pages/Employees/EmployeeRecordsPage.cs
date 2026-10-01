using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Small Enterprise and above. A Micro user never sees the navigation entry, and the API
    /// answers 403 for every endpoint below regardless.
    /// </summary>
    internal sealed class EmployeeRecordsPage : CrudPageBase<EmployeeDto>
    {
        private readonly Label _summary;
        private readonly ComboBox _statusFilter;
        private List<EmployeeDto> _all = new();

        public EmployeeRecordsPage(FitCoreSession session)
            : base(session, "Employee Records", "Your staff, and the salary payroll is calculated from.",
                   "employee", "Name, code, position or email")
        {
            AddAction("Pay history", ButtonTone.Secondary, () => ShowPayrollsAsync(), 110);
            AddAction("Create login", ButtonTone.Secondary, () => CreateLoginAsync(), 110);

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
            "attendance and the hourly rate you record against each of them.";

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
            Column(nameof(EmployeeDto.Position), "Position", 110);
            Column(nameof(EmployeeDto.Department), "Department", 100);
            Column(nameof(EmployeeDto.Phone), "Phone", 85);
            DateColumn(nameof(EmployeeDto.HireDate), "Hired", 80);
            MoneyColumn(nameof(EmployeeDto.BasicSalary), "Basic salary", 90);
            MoneyColumn(nameof(EmployeeDto.HourlyRate), "Hourly rate", 85);
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
            // Items, not _all: AfterLoad runs inside base.LoadAsync(), which sets Items to
            // the freshly-fetched full list before calling here. _all is this page's own
            // copy for the status filter, but it is only assigned after base.LoadAsync()
            // returns - reading it here would show last load's numbers, one refresh behind.
            var active = Items.Count(e => string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase));
            var cost = Items.Where(e => string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase))
                            .Sum(e => e.BasicSalary);
            var departments = Items.Select(e => e.Department)
                                   .Where(d => !string.IsNullOrWhiteSpace(d))
                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                   .Count();

            _summary.Text = $"{Items.Count:N0} employee(s)      {active:N0} active      " +
                            $"{departments:N0} department(s)      " +
                            $"monthly basic pay {UiKit.Money(cost)}";
        }

        /// <summary>
        /// The position options this operator may assign. A Manager may only appoint Staff -
        /// promoting someone to Manager is an administrator's decision - but editing an
        /// existing record whose position is already senior still needs an option that
        /// matches it, or the combo would have nothing selected.
        /// </summary>
        private List<KeyValuePair<string, string>> PositionOptions(EmployeeDto? existing)
        {
            var isAdmin = Session.CurrentUser?.IsAdminOrAbove == true;

            var options = isAdmin
                ? EmployeePositions.All
                : new[] { EmployeePositions.Staff };

            var list = options.Select(p => new KeyValuePair<string, string>(p, p)).ToList();

            if (existing is not null &&
                !list.Any(o => string.Equals(o.Key, existing.Position, StringComparison.OrdinalIgnoreCase)))
            {
                list.Insert(0, new KeyValuePair<string, string>(existing.Position, existing.Position));
            }

            return list;
        }

        private List<FieldSpec> Fields(EmployeeDto? e)
        {
            var isAdmin = Session.CurrentUser?.IsAdminOrAbove == true;

            return new List<FieldSpec>
            {
                new("code", "Employee code")
                {
                    Required = true, Value = e?.EmployeeCode, MaxLength = 30,
                    Hint = "Must be unique, e.g. EMP-001."
                },
                new("first", "First name") { Required = true, Value = e?.FirstName, MaxLength = 100 },
                new("last", "Last name") { Required = true, Value = e?.LastName, MaxLength = 100 },

                new("position", "Position", FieldKind.Combo)
                {
                    Required = true,
                    Value = e?.Position ?? EmployeePositions.Staff,
                    Options = PositionOptions(e),

                    // Only an administrator promotes or demotes; a Manager can still see what
                    // an existing employee's position is, but cannot change it here - the
                    // server would refuse the attempt anyway.
                    ReadOnly = e is not null && !isAdmin,

                    Hint = isAdmin
                        ? "Decides their system access. Changing this promotes or demotes them."
                        : e is null
                            ? "You can add Staff. An administrator can appoint a Manager."
                            : "Only an administrator can change an employee's position."
                },

                new("department", "Department") { Value = e?.Department, MaxLength = 100 },
                new("phone", "Phone", FieldKind.Phone) { Value = e?.Phone, MaxLength = 30 },
                new("email", "Email", FieldKind.Email)
                {
                    Value = e?.Email, MaxLength = 150,
                    Hint = "Used as their sign-in when their FitCore account is created."
                },
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
                    Required = true,
                    Value = e?.BasicSalary ?? 0m,
                    Minimum = 0m,

                    // Setting or changing pay is an administrator's decision. A Manager hiring
                    // a Staff member leaves this at zero for an administrator to fill in later.
                    ReadOnly = !isAdmin,

                    Hint = isAdmin
                        ? "Used for a salaried run. Payroll from attendance uses the hourly rate instead."
                        : "Only an administrator can set or change an employee's pay."
                },

                new("hourlyRate", "Hourly rate", FieldKind.Money)
                {
                    Value = e?.HourlyRate ?? 0m,
                    Minimum = 0m,
                    ReadOnly = !isAdmin,

                    Hint = isAdmin
                        ? "Payroll generated from attendance is calculated from this."
                        : "Only an administrator can set or change an employee's pay."
                }
            };
        }

        protected override Task<bool> OnAddAsync()
        {
            EmployeeDto? created = null;

            var saved = EditDialog.Run(this, "Add employee",
                "Payroll can be generated from attendance using the hourly rate below, or run " +
                "manually from the basic salary. A FitCore sign-in is created automatically once " +
                "they are saved.", Fields(null), async f =>
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
                        BasicSalary = f.First(x => x.Key == "salary").Decimal,
                        HourlyRate = f.First(x => x.Key == "hourlyRate").Decimal
                    });

                    if (!result.IsSuccess) return result.ErrorMessage;
                    created = result.Value;
                    return null;
                }, "Create employee");

            if (saved && created is not null)
            {
                // Fire-and-forget from the dialog's point of view: the employee is already
                // saved and visible in the grid regardless of how this turns out. A failure
                // here is reported once the grid has reloaded, not by blocking the dialog.
                _ = CreateAccountAsync(created, announce: true);
            }

            return Task.FromResult(saved);
        }

        /// <summary>
        /// Asks the server for a sign-in for this employee and reports the outcome. Used both
        /// automatically after Create and from the explicit "Create login" action, which is
        /// what makes it the retry path when the first attempt could not reach the master
        /// database - calling it again finds the account already exists rather than
        /// duplicating it.
        /// </summary>
        private async Task CreateAccountAsync(EmployeeDto employee, bool announce)
        {
            var result = await Session.Employees.EnsureAccountAsync(employee.EmployeeId);

            if (!result.IsSuccess)
            {
                ShowError(result.ErrorMessage);
                return;
            }

            var outcome = result.Value!;

            if (outcome.AccountUsable) Notify(outcome.Message);
            else if (announce) ShowError(outcome.Message);
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

                        // A read-only combo still reports its selected value, so a non-admin
                        // submits the position back unchanged rather than an empty string.
                        Position = f.First(x => x.Key == "position").ComboValue ?? e.Position,

                        Department = f.First(x => x.Key == "department").Text,
                        Phone = f.First(x => x.Key == "phone").Text,
                        Email = f.First(x => x.Key == "email").Text,
                        HireDate = f.First(x => x.Key == "hired").Date,
                        BasicSalary = f.First(x => x.Key == "salary").Decimal,
                        HourlyRate = f.First(x => x.Key == "hourlyRate").Decimal,
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

        /// <summary>
        /// The explicit retry: creates a sign-in for the selected employee, or reports that
        /// one already exists. Safe to press more than once - the server detects an existing
        /// account rather than duplicating it.
        /// </summary>
        private async Task CreateLoginAsync()
        {
            if (IsBusy) return;

            var e = Selected;
            if (e is null) { ShowError("Select an employee to create their login."); return; }

            await GuardAsync(() => CreateAccountAsync(e, announce: true), "Creating login…");
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
                var runs = Unwrap(await Session.Employees.GetPayrollsAsync(e.EmployeeId));
                if (runs is null) return;

                var theirs = runs.OrderByDescending(r => r.PeriodStart).ToList();

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
}
