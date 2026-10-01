using System.Drawing;
using System.Text;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>Which report the shared Reports screen is showing.</summary>
    internal enum ReportKind
    {
        Sales,
        Payments,
        Inventory,
        Membership,
        Employees,
        Payroll
    }

    /// <summary>
    /// Date-filtered reports. Every figure is aggregated server-side in the tenant database;
    /// this screen only chooses a range and renders what comes back.
    ///
    /// One page serves seven tabs. Business Intelligence's own Reports tab offers every kind
    /// behind a selector, and each operational module has a Reports tab that pins the same
    /// screen to its own kind. Six near-identical report forms would have drifted apart on the
    /// first change to the date filter, so the screen is built once and told what to show -
    /// the same reason the analytics areas all share <see cref="AnalyticsPage"/>.
    ///
    /// The kinds on offer are filtered by what the server said this account may open, because
    /// each report endpoint is guarded by its own module's Reports subfeature. Without that
    /// filter the Business Intelligence tab on a Micro tenant would offer Employees and
    /// Payroll - which that plan does not include - and both would answer 403.
    /// </summary>
    internal sealed class ReportsPage : ModulePageBase
    {
        private static readonly ReportKind[] EveryKind =
        {
            ReportKind.Sales, ReportKind.Payments, ReportKind.Inventory,
            ReportKind.Membership, ReportKind.Employees, ReportKind.Payroll
        };

        private readonly ReportKind[] _kinds;

        private DateTimePicker _from = null!;
        private DateTimePicker _to = null!;
        private ComboBox? _which;
        private ComboBox _preset = null!;
        private DataGridView _grid = null!;
        private FlowLayoutPanel _headline = null!;
        private Label _caption = null!;

        private string _exportName = "report";
        private readonly List<(string Label, string Value)> _figures = new();

        protected override bool UsesGrid => false;

        /// <summary>Business Intelligence's Reports tab: every kind this account may open.</summary>
        public ReportsPage(FitCoreSession session)
            : this(session, "Reports",
                   "Sales, payments, inventory, membership, employees and payroll, filtered by date.",
                   EveryKind)
        {
        }

        /// <summary>One module's own Reports tab, pinned to a single kind.</summary>
        public ReportsPage(FitCoreSession session, string title, string subtitle, params ReportKind[] kinds)
            : base(session, title, subtitle)
        {
            var requested = kinds is { Length: > 0 } ? kinds : EveryKind;
            var user = session.CurrentUser;

            var offered = requested.Where(k => user?.CanUse(SubmoduleFor(k)) == true).ToArray();

            // A module's Reports tab is only drawn when its own subfeature is permitted, so an
            // empty list here means the caller asked for a kind this account cannot open.
            // Keeping the requested kind rather than nothing lets the screen report that
            // plainly through the usual 403 message instead of rendering a page with no report.
            _kinds = offered.Length > 0 ? offered : requested;

            BuildFilters();
            BuildBody();
        }

        /// <summary>The subfeature that guards a kind, and the endpoint behind it.</summary>
        private static string SubmoduleFor(ReportKind kind) => kind switch
        {
            ReportKind.Sales => Submodules.SalesReports,
            ReportKind.Payments => Submodules.PaymentReports,
            ReportKind.Inventory => Submodules.InventoryReports,
            ReportKind.Membership => Submodules.MembershipReports,
            ReportKind.Employees => Submodules.EmployeeReports,
            ReportKind.Payroll => Submodules.PayrollReports,
            _ => Submodules.OperationalReports
        };

        private ReportKind Current =>
            _which is null
                ? _kinds[0]
                : _kinds[Math.Max(0, Math.Min(_which.SelectedIndex, _kinds.Length - 1))];

        private void BuildFilters()
        {
            // A one-item dropdown is not a decision the operator has, so it is not drawn.
            if (_kinds.Length > 1)
            {
                FilterBar.Controls.Add(UiKit.FilterLabel("Report"));

                _which = UiKit.Select(170);
                _which.Items.AddRange(_kinds.Select(k => (object)k.ToString()).ToArray());
                _which.SelectedIndex = 0;
                _which.SelectedIndexChanged += async (_, _) => await LoadAsync();
                FilterBar.Controls.Add(_which);
            }

            FilterBar.Controls.Add(UiKit.FilterLabel("Period"));

            _preset = UiKit.Select(150);
            _preset.DisplayMember = "Value";
            _preset.ValueMember = "Key";
            _preset.DataSource = new List<KeyValuePair<string, string>>
            {
                new("month", "This month"),
                new("last30", "Last 30 days"),
                new("quarter", "Last 3 months"),
                new("year", "This year"),
                new("custom", "Custom range")
            };
            _preset.SelectedIndexChanged += async (_, _) => await ApplyPresetAsync();
            FilterBar.Controls.Add(_preset);

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            _from = UiKit.DatePicker(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
            FilterBar.Controls.Add(_from);

            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            _to = UiKit.DatePicker(DateTime.Today);
            FilterBar.Controls.Add(_to);

            FilterBar.Controls.Add(UiKit.Action("Apply", ButtonTone.Primary,
                async (_, _) => await LoadAsync(), 92));

            AddAction("Refresh", ButtonTone.Secondary, () => LoadAsync(), 90);
            AddAction("Export", ButtonTone.Secondary, () => ExportAsync(), 88);
        }

        private async Task ApplyPresetAsync()
        {
            var today = DateTime.Today;

            switch (_preset.SelectedValue?.ToString())
            {
                case "month":
                    _from.Value = new DateTime(today.Year, today.Month, 1);
                    _to.Value = today;
                    break;
                case "last30":
                    _from.Value = today.AddDays(-30);
                    _to.Value = today;
                    break;
                case "quarter":
                    _from.Value = today.AddMonths(-3);
                    _to.Value = today;
                    break;
                case "year":
                    _from.Value = new DateTime(today.Year, 1, 1);
                    _to.Value = today;
                    break;
                default:
                    return;   // custom: leave the pickers to the operator
            }

            await LoadAsync();
        }

        private void BuildBody()
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(1)
            };
            card.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, card.Width, card.Height), UiTheme.Surface, UiTheme.Border);

            _headline = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 78,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.Surface,
                Padding = new Padding(14, 14, 14, 10)
            };

            _caption = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                BackColor = UiTheme.Surface,
                UseMnemonic = false
            };

            _grid = UiKit.Grid();
            _grid.AutoGenerateColumns = true;
            UiKit.HumaniseColumns(_grid);

            card.Controls.Add(_grid);
            card.Controls.Add(_caption);
            card.Controls.Add(_headline);

            SetBody(card);
        }

        /// <summary>One headline figure on the strip above the report table.</summary>
        private void Figure(string label, string value, Color? accent = null)
        {
            _figures.Add((label, value));

            var box = new Panel
            {
                Width = 168,
                Height = 50,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, 0, 10, 0)
            };

            box.Controls.Add(new Label
            {
                Text = label.ToUpperInvariant(),
                Font = UiTheme.Overline,
                ForeColor = UiTheme.TextMuted,
                Location = new Point(0, 0),
                AutoSize = true,
                UseMnemonic = false
            });

            box.Controls.Add(new Label
            {
                Text = value,
                Font = new Font(UiTheme.FamilySemibold, 13F),
                ForeColor = accent ?? UiTheme.TextPrimary,
                Location = new Point(0, 18),
                AutoSize = true,
                UseMnemonic = false
            });

            _headline.Controls.Add(box);
        }

        public override Task LoadAsync() => GuardAsync(async () =>
        {
            var from = _from.Value.Date;
            var to = _to.Value.Date;

            if (to < from)
            {
                ShowError("The “to” date cannot be before the “from” date.");
                return;
            }

            _grid.DataSource = null;
            _headline.Controls.Clear();
            _figures.Clear();

            var range = $"{UiKit.Date(from)} to {UiKit.Date(to)}";

            switch (Current)
            {
                case ReportKind.Sales:
                    await LoadSalesAsync(from, to, range);
                    break;
                case ReportKind.Payments:
                    await LoadPaymentsAsync(from, to, range);
                    break;
                case ReportKind.Inventory:
                    await LoadInventoryAsync(from, to, range);
                    break;
                case ReportKind.Membership:
                    await LoadMembershipAsync(from, to, range);
                    break;
                case ReportKind.Employees:
                    await LoadEmployeesAsync(from, to, range);
                    break;
                case ReportKind.Payroll:
                    await LoadPayrollAsync(from, to, range);
                    break;
            }
        }, "Running the report…");

        private async Task LoadSalesAsync(DateTime from, DateTime to, string range)
        {
            var report = Unwrap(await Session.Reports.GetSalesReportAsync(from, to));
            if (report is null) return;

            _exportName = "sales";

            Figure("Transactions", report.TransactionCount.ToString("N0"));
            Figure("Gross sales", UiKit.Money(report.GrossSales));
            Figure("Discounts", UiKit.Money(report.Discounts));
            Figure("Net sales", UiKit.Money(report.NetSales), UiTheme.Primary);
            Figure("Units sold", report.UnitsSold.ToString("N0"));
            Figure("Outstanding", UiKit.Money(report.Outstanding),
                report.Outstanding > 0 ? UiTheme.Danger : UiTheme.Success);

            _grid.DataSource = report.TopProducts;
            _caption.Text = $"Top products by revenue — {range}";
            SetStatus($"{report.TopProducts.Count:N0} product(s) sold in this period");
        }

        private async Task LoadPaymentsAsync(DateTime from, DateTime to, string range)
        {
            var report = Unwrap(await Session.Reports.GetPaymentReportAsync(from, to));
            if (report is null) return;

            _exportName = "payments";

            Figure("Payments", report.Count.ToString("N0"));
            Figure("Collected", UiKit.Money(report.TotalCollected), UiTheme.Success);
            Figure("Pending", UiKit.Money(report.Pending), UiTheme.Warning);
            Figure("Refunded", UiKit.Money(report.Refunded));
            Figure("Outstanding", UiKit.Money(report.TotalOutstanding),
                report.TotalOutstanding > 0 ? UiTheme.Danger : UiTheme.Success);

            _grid.DataSource = report.ByMethod;
            _caption.Text = $"Collections by payment method — {range}";
            SetStatus($"{report.PaidCount:N0} paid, {report.PartiallyPaidCount:N0} part paid, " +
                      $"{report.UnpaidCount:N0} unpaid");
        }

        private async Task LoadInventoryAsync(DateTime from, DateTime to, string range)
        {
            var report = Unwrap(await Session.Reports.GetInventoryReportAsync(from, to));
            if (report is null) return;

            _exportName = "inventory";

            Figure("Products", report.Summary.TotalProducts.ToString("N0"));
            Figure("Stock value", UiKit.Money(report.Summary.StockValue), UiTheme.Primary);
            Figure("Retail value", UiKit.Money(report.Summary.RetailValue));
            Figure("Low stock", report.Summary.LowStock.ToString("N0"),
                report.Summary.LowStock > 0 ? UiTheme.Warning : UiTheme.Success);
            Figure("Out of stock", report.Summary.OutOfStock.ToString("N0"),
                report.Summary.OutOfStock > 0 ? UiTheme.Danger : UiTheme.Success);

            _grid.DataSource = report.Movements;
            _caption.Text = $"Stock movements — {range}";
            SetStatus($"{report.Movements.Count:N0} movement(s) in this period");
        }

        private async Task LoadMembershipAsync(DateTime from, DateTime to, string range)
        {
            var report = Unwrap(await Session.Reports.GetMembershipReportAsync(from, to));
            if (report is null) return;

            _exportName = "membership";

            Figure("Members", report.TotalMembers.ToString("N0"));
            Figure("Active", report.ActiveMembers.ToString("N0"), UiTheme.Success);
            Figure("New in range", report.NewMembersInRange.ToString("N0"));
            Figure("Expiring soon", report.ExpiringSoon.ToString("N0"),
                report.ExpiringSoon > 0 ? UiTheme.Warning : UiTheme.Success);
            Figure("Revenue", UiKit.Money(report.MembershipRevenue), UiTheme.Primary);
            Figure("Outstanding", UiKit.Money(report.MembershipOutstanding),
                report.MembershipOutstanding > 0 ? UiTheme.Danger : UiTheme.Success);

            _grid.DataSource = report.ByPlan;
            _caption.Text = $"Membership by plan — {range}";
            SetStatus($"{report.ActiveSubscriptions:N0} active subscription(s)");
        }

        private async Task LoadEmployeesAsync(DateTime from, DateTime to, string range)
        {
            var report = Unwrap(await Session.Reports.GetEmployeeReportAsync(from, to));
            if (report is null) return;

            _exportName = "employees";

            Figure("Headcount", report.TotalEmployees.ToString("N0"));
            Figure("Active", report.ActiveEmployees.ToString("N0"), UiTheme.Success);
            Figure("New hires", report.NewHiresInRange.ToString("N0"));
            Figure("Attendance", $"{report.AttendanceRate:N1}%",
                report.AttendanceRate >= 90m ? UiTheme.Success : UiTheme.Warning);
            Figure("Regular hrs", report.TotalRegularHours.ToString("N1"));
            Figure("Overtime hrs", report.TotalOvertimeHours.ToString("N1"), UiTheme.Primary);
            Figure("Leave pending", report.PendingLeaveRequests.ToString("N0"),
                report.PendingLeaveRequests > 0 ? UiTheme.Warning : UiTheme.Success);

            _grid.DataSource = report.Employees;
            _caption.Text = $"Attendance by employee — {range}";
            SetStatus($"{report.Employees.Count:N0} employee(s) on the roll, " +
                      $"{report.DaysPresent:N0} day(s) present, {report.DaysAbsent:N0} absent, " +
                      $"{report.ApprovedLeaveDays:N1} day(s) approved leave");
        }

        private async Task LoadPayrollAsync(DateTime from, DateTime to, string range)
        {
            var report = Unwrap(await Session.Reports.GetPayrollReportAsync(from, to));
            if (report is null) return;

            _exportName = "payroll";

            Figure("Runs", report.RunCount.ToString("N0"));
            Figure("Employees", report.EmployeeCount.ToString("N0"));
            Figure("Gross", UiKit.Money(report.GrossPay));
            Figure("Deductions", UiKit.Money(report.TotalDeductions), UiTheme.Warning);
            Figure("Net", UiKit.Money(report.NetPay), UiTheme.Primary);
            Figure("Employer share", UiKit.Money(report.EmployerContributions));
            Figure("Total cost", UiKit.Money(report.TotalEmploymentCost), UiTheme.Primary);
            Figure("Unpaid", UiKit.Money(report.Outstanding),
                report.Outstanding > 0 ? UiTheme.Danger : UiTheme.Success);

            // The statutory breakdown rather than the run list: "which deduction is this" is the
            // question a payroll report is opened to answer, and the runs themselves are one
            // click away on Payroll Records.
            _grid.DataSource = report.DeductionBreakdown;
            _caption.Text = $"Statutory deductions — {range}";
            SetStatus($"{report.PaidRunCount:N0} paid, {report.UnpaidRunCount:N0} unpaid      " +
                      $"regular {report.RegularHours:N1} hrs, overtime {report.OvertimeHours:N1} hrs");
        }

        // ------------------------------------------------------------------ export

        /// <summary>
        /// Writes the headline figures and the table to a CSV. The figures go in as well as the
        /// rows, because the headline is usually the answer and a file holding only the
        /// breakdown sends the operator back to the screen to read it off the strip.
        /// </summary>
        private async Task ExportAsync()
        {
            if (_grid.DataSource is null && _figures.Count == 0)
            {
                ShowError("Run the report first - there is nothing to export yet.");
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "Export report",
                Filter = "CSV file (*.csv)|*.csv",
                FileName = $"fitcore-{_exportName}-{_from.Value:yyyyMMdd}-{_to.Value:yyyyMMdd}.csv"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            await GuardAsync(async () =>
            {
                var csv = new StringBuilder();

                csv.AppendLine(Escape(_caption.Text));
                csv.AppendLine();

                foreach (var (label, value) in _figures)
                {
                    csv.AppendLine($"{Escape(label)},{Escape(value)}");
                }

                csv.AppendLine();

                var columns = _grid.Columns
                    .Cast<DataGridViewColumn>()
                    .Where(c => c.Visible)
                    .ToList();

                if (columns.Count > 0)
                {
                    csv.AppendLine(string.Join(",", columns.Select(c => Escape(c.HeaderText))));

                    foreach (DataGridViewRow row in _grid.Rows)
                    {
                        if (row.IsNewRow) continue;

                        csv.AppendLine(string.Join(",", columns.Select(
                            c => Escape(row.Cells[c.Index].FormattedValue?.ToString()))));
                    }
                }

                await File.WriteAllTextAsync(dialog.FileName, csv.ToString(), Encoding.UTF8);

                Notify($"Exported to {Path.GetFileName(dialog.FileName)}.");
            }, "Exporting…");
        }

        /// <summary>Quotes a CSV field when it needs it - product names carry commas often.</summary>
        private static string Escape(string? value)
        {
            var text = value ?? "";

            if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\n')) return text;

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
