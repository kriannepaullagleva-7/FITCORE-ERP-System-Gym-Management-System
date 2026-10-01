using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Pay runs. Gross, overtime and net are calculated on the server from the inputs below -
    /// this screen never computes pay, so the desktop cannot dictate what anyone is paid.
    ///
    /// A run is usually created from Payroll Calculation, which reads attendance. Manual
    /// creation here stays as the fallback for a period with no attendance recorded yet.
    /// Only an administrator may edit or delete a run once created - a Manager can start one,
    /// not finish correcting one.
    /// </summary>
    internal sealed class PayrollRecordsPage : CrudPageBase<PayrollDto>
    {
        private readonly Label _summary;
        private readonly ComboBox _statusFilter;
        private List<EmployeeDto> _employees = new();
        private List<PayrollDto> _all = new();

        public PayrollRecordsPage(FitCoreSession session)
            : base(session, "Payroll Records", "Pay runs, with gross and net calculated by the server.",
                   "pay run", "Employee name or code")
        {
            AddAction("View receipt", ButtonTone.Secondary, () => ShowReceiptAsync(), 116);

            // A discrete last step rather than something buried inside Edit's status combo -
            // finalising a run is the one action in this workflow that cannot be undone from
            // here, so it gets a button of its own instead of hiding behind "Save changes".
            if (Session.CurrentUser?.IsAdminOrAbove == true)
            {
                AddAction("Finalize (mark Paid)", ButtonTone.Success, () => FinalizeAsync(), 156);
            }

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

        private bool IsAdmin => Session.CurrentUser?.IsAdminOrAbove == true;

        // A Manager may start a Draft run (usually from Payroll Calculation, or manually when
        // no attendance exists yet); only an administrator corrects or removes one afterwards.
        protected override bool SupportsAdd => Session.CurrentUser?.IsManagerOrAbove == true;
        protected override bool SupportsEdit => IsAdmin;
        protected override bool SupportsDelete => IsAdmin;

        protected override string CreatedMessage => "Pay run created successfully.";
        protected override string UpdatedMessage => "Pay run updated successfully.";
        protected override string DeletedMessage => "Pay run deleted successfully.";

        protected override string DeleteConsequence =>
            "A pay run that has been marked Paid cannot be deleted. Deleting a draft removes " +
            "it from payroll totals. This cannot be undone.";

        protected override string EmptyHeadline => "No pay runs yet";
        protected override string EmptyDetail =>
            "Generate a pay run from the Payroll Calculation tab, or create one manually here " +
            "for a period with no attendance recorded yet.";

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
            Column(nameof(PayrollDto.EmployeeCode), "Code", 65);
            DateColumn(nameof(PayrollDto.PeriodStart), "From", 72);
            DateColumn(nameof(PayrollDto.PeriodEnd), "To", 72);
            MoneyColumn(nameof(PayrollDto.RegularPay), "Regular pay", 78);
            MoneyColumn(nameof(PayrollDto.OvertimePay), "Overtime", 72);
            MoneyColumn(nameof(PayrollDto.GrossPay), "Gross", 78);
            MoneyColumn(nameof(PayrollDto.Deductions), "Deductions", 78);
            MoneyColumn(nameof(PayrollDto.NetPay), "Net", 78);
            StatusColumn(nameof(PayrollDto.Status), "Status", 72);
            Column(nameof(PayrollDto.ProcessedBy), "Generated by", 100);
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

                if (p.RegularHours > 0)
                {
                    fields.Add(new FieldSpec("regularHours", "Regular hours (from attendance)", FieldKind.Number)
                        { Value = p.RegularHours, ReadOnly = true });
                    fields.Add(new FieldSpec("regularPay", "Regular pay", FieldKind.Money)
                        { Value = p.RegularPay, ReadOnly = true });
                    fields.Add(new FieldSpec("statutory", "SSS / PhilHealth / Pag-IBIG / Tax", FieldKind.Money)
                    {
                        Value = p.SssDeduction + p.PhilHealthDeduction + p.PagIbigDeduction + p.WithholdingTax,
                        ReadOnly = true,
                        Hint = "Computed from the configured statutory reference table."
                    });
                }
            }

            fields.Add(new FieldSpec("allowances", "Allowances", FieldKind.Money)
                { Value = p?.Allowances ?? 0m, Minimum = 0m });
            fields.Add(new FieldSpec("deductions", "Deductions (total)", FieldKind.Money)
            {
                Value = p?.Deductions ?? 0m, Minimum = 0m,
                Hint = p is { RegularHours: > 0 }
                    ? "Includes the statutory lines above plus any other deduction."
                    : null
            });
            fields.Add(new FieldSpec("othours", "Overtime hours", FieldKind.Number)
                { Value = p?.OvertimeHours ?? 0m, Minimum = 0m, Maximum = 400m, ReadOnly = p?.RegularHours > 0 });
            fields.Add(new FieldSpec("otrate", "Overtime rate per hour", FieldKind.Money)
                { Value = p?.OvertimeRate ?? 0m, Minimum = 0m, ReadOnly = p?.RegularHours > 0 });
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
                ShowError("There are no active employees. Add one on Employee Records, or " +
                          "set an existing employee back to Active, before creating a pay run.");
                return Task.FromResult(false);
            }

            var saved = EditDialog.Run(this, "New pay run (manual)",
                "For a period with no attendance recorded yet. Prefer Payroll Calculation when " +
                "attendance exists - it derives hours and pay automatically. Gross, overtime " +
                "and net are calculated by the server.",
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

        /// <summary>The three states a pay run moves through.</summary>
        private static List<KeyValuePair<string, string>> StatusOptions() => new()
        {
            new("Draft", "Draft"),
            new("Approved", "Approved"),
            new("Paid", "Paid")
        };

        protected override Task<bool> OnEditAsync(PayrollDto p)
        {
            // A paid run is locked against field edits by the server, so Edit narrows to the
            // one thing that is still legitimate: putting it back to Draft because it was
            // recorded in error. Offering the full form and having the save refused would be
            // worse than offering only what will work.
            if (string.Equals(p.Status, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(EditStatusOnly(p));
            }

            var fields = Fields(p);

            fields.Add(new FieldSpec("status", "Status", FieldKind.Combo)
            {
                Required = true,
                Value = p.Status,
                Options = StatusOptions(),
                Hint = "Marking a run Paid locks it against further editing and deletion."
            });

            var saved = EditDialog.Run(this, $"Edit pay run — {p.EmployeeName}",
                $"{UiKit.Date(p.PeriodStart)} to {UiKit.Date(p.PeriodEnd)}, currently {p.Status}.",
                fields, async f =>
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

                    if (!result.IsSuccess) return result.ErrorMessage;

                    var wanted = f.First(x => x.Key == "status").ComboValue ?? p.Status;

                    if (!string.Equals(wanted, p.Status, StringComparison.OrdinalIgnoreCase))
                    {
                        var status = await Session.Payroll.SetStatusAsync(
                            p.PayrollId, new UpdatePayrollStatusDto { Status = wanted });

                        if (!status.IsSuccess) return status.ErrorMessage;
                    }

                    return null;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        /// <summary>The reduced Edit offered for a run the server has locked.</summary>
        private bool EditStatusOnly(PayrollDto p)
        {
            var fields = new List<FieldSpec>
            {
                new("status", "Status", FieldKind.Combo)
                {
                    Required = true,
                    Value = p.Status,
                    Options = StatusOptions(),
                    Hint = "Set this back to Draft to reopen the run for editing."
                }
            };

            return EditDialog.Run(this, $"Pay run — {p.EmployeeName}",
                $"This run is marked Paid, so its figures are locked. The status can still be " +
                $"changed if it was recorded in error. Net pay {UiKit.Money(p.NetPay)}.",
                fields, async f =>
                {
                    var result = await Session.Payroll.SetStatusAsync(p.PayrollId,
                        new UpdatePayrollStatusDto
                        { Status = f.First(x => x.Key == "status").ComboValue ?? "Draft" });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Update status");
        }

        /// <summary>Opens the payslip for the selected run.</summary>
        private Task ShowReceiptAsync()
        {
            var p = Selected;
            if (p is null) { ShowError("Select a pay run to view its payslip."); return Task.CompletedTask; }

            ReceiptDialog.Show(this, ReceiptBuilder.ForPayroll(Session, p));
            return Task.CompletedTask;
        }

        protected override async Task<string?> OnDeleteAsync(PayrollDto p)
        {
            var result = await Session.Payroll.DeleteAsync(p.PayrollId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        /// <summary>
        /// The last step of the workflow: Draft/Approved → Paid, in one action rather than
        /// through Edit's status combo. Admin-only client-side as a courtesy; the server
        /// refuses it from anyone else regardless.
        /// </summary>
        private async Task FinalizeAsync()
        {
            if (IsBusy) return;

            var p = Selected;
            if (p is null) { ShowError("Select a pay run to finalize."); return; }

            if (string.Equals(p.Status, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                ShowError($"{p.EmployeeName}'s run for this period is already marked Paid.");
                return;
            }

            if (UiKit.Confirm(
                    $"Mark {p.EmployeeName}'s pay run ({UiKit.Date(p.PeriodStart)} – " +
                    $"{UiKit.Date(p.PeriodEnd)}, net {UiKit.Money(p.NetPay)}) as Paid?\r\n\r\n" +
                    "This locks it against further editing or deletion.", "Finalize pay run")
                != DialogResult.Yes) return;

            await GuardAsync(async () =>
            {
                var result = await Session.Payroll.SetStatusAsync(
                    p.PayrollId, new UpdatePayrollStatusDto { Status = "Paid" });

                if (!result.IsSuccess) { ShowError(result.ErrorMessage); return; }

                await LoadAsync();
                Notify($"{p.EmployeeName}'s pay run finalized successfully.");
            }, "Finalizing…");
        }
    }
}
