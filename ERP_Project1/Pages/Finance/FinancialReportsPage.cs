using System.ComponentModel;
using System.Drawing;
using System.Text;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The four financial statements: trial balance, income statement, balance sheet and cash
    /// flow.
    ///
    /// All four are read from the same set of postings, which is the point of keeping a ledger
    /// - assembled instead from the sales, payments and expense tables they would disagree with
    /// each other the first time somebody cancelled something. Where they nonetheless fail to
    /// balance, the screen says so rather than hiding it: a trial balance that does not add up
    /// is the most useful thing a set of books can tell you.
    /// </summary>
    internal sealed class FinancialReportsPage : ModulePageBase
    {
        private readonly ComboBox _report;
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;

        private readonly Label _headline;
        private readonly Label _headlineHint;
        private readonly Label _second;
        private readonly Label _secondHint;
        private readonly Label _third;
        private readonly Label _thirdHint;
        private readonly Label _balanceCheck;
        private readonly Label _balanceCheckHint;

        private List<StatementRow> _rows = new();

        public FinancialReportsPage(FitCoreSession session)
            : base(session, "Financial Reports",
                   "Trial balance, income statement, balance sheet and cash flow - all from the same postings.")
        {
            var today = DateTime.Today;

            _report = UiKit.Select(190);
            _report.Items.AddRange(new object[]
            {
                "Income statement", "Balance sheet", "Trial balance", "Cash flow"
            });
            _report.SelectedIndex = 0;
            _report.SelectedIndexChanged += async (_, _) => await LoadAsync();

            _from = UiKit.DatePicker(new DateTime(today.Year, today.Month, 1));
            _to = UiKit.DatePicker(today);

            FilterBar.Controls.Add(UiKit.FilterLabel("Report"));
            FilterBar.Controls.Add(_report);
            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);

            AddAction("Apply", ButtonTone.Secondary, LoadAsync, 86);
            AddAction("Export", ButtonTone.Secondary, ExportAsync, 88);

            StatsRow.Controls.Add(UiKit.StatCard("—", out _headline, out _headlineHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("—", out _second, out _secondHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("—", out _third, out _thirdHint, UiTheme.Warning));
            StatsRow.Controls.Add(UiKit.StatCard("Balanced", out _balanceCheck, out _balanceCheckHint, UiTheme.Neutral));

            Grid.AutoGenerateColumns = false;

            Grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = nameof(StatementRow.Label),
                DataPropertyName = nameof(StatementRow.Label),
                HeaderText = "",
                FillWeight = 260
            });

            Grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = nameof(StatementRow.Left),
                DataPropertyName = nameof(StatementRow.Left),
                HeaderText = "",
                FillWeight = 90,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
            });

            Grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = nameof(StatementRow.Right),
                DataPropertyName = nameof(StatementRow.Right),
                HeaderText = "",
                FillWeight = 90,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
            });

            // Subtotals are drawn in bold with a rule above them, which is how a statement is
            // read: the eye goes to the totals first and the detail second.
            Grid.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= Grid.Rows.Count) return;
                if (Grid.Rows[e.RowIndex].DataBoundItem is not StatementRow row) return;

                var style = Grid.Rows[e.RowIndex].DefaultCellStyle;

                style.Font = row.IsSubtotal ? UiTheme.BodyStrong : UiTheme.Body;
                style.ForeColor = row.IsSubtotal ? UiTheme.TextPrimary : UiTheme.TextSecondary;
                style.BackColor = row.IsSubtotal ? UiTheme.SurfaceAlt : UiTheme.Surface;
            };
        }

        /// <summary>One line of a statement, flattened so all four reports share a grid.</summary>
        private sealed class StatementRow
        {
            public string Label { get; init; } = "";
            public string Left { get; init; } = "";
            public string Right { get; init; } = "";
            public bool IsSubtotal { get; init; }
        }

        private string Selected => (string)(_report.SelectedItem ?? "Income statement");

        /// <summary>
        /// Captions the two money columns. Both are added by name in the constructor, so the
        /// lookup always succeeds - but it is written to tolerate a miss rather than to assert
        /// one, because a report heading is not worth throwing over.
        /// </summary>
        private void HeadMoneyColumns(string left, string right)
        {
            var leftColumn = Grid.Columns[nameof(StatementRow.Left)];
            if (leftColumn is not null) leftColumn.HeaderText = left;

            var rightColumn = Grid.Columns[nameof(StatementRow.Right)];
            if (rightColumn is not null) rightColumn.HeaderText = right;
        }

        public override async Task LoadAsync()
        {
            await GuardAsync(async () =>
            {
                // Only the trial balance has two money columns. Setting the headings here, for
                // every report, means switching back to the income statement does not leave it
                // captioned "Debit" and "Credit" from the report before it.
                if (Selected == "Trial balance") HeadMoneyColumns("Debit", "Credit");
                else HeadMoneyColumns("", "");

                _rows = Selected switch
                {
                    "Balance sheet" => await BalanceSheetAsync(),
                    "Trial balance" => await TrialBalanceAsync(),
                    "Cash flow" => await CashFlowAsync(),
                    _ => await IncomeStatementAsync()
                };

                Grid.DataSource = new BindingList<StatementRow>(_rows);

                if (_rows.Count == 0)
                {
                    ShowEmptyState("Nothing posted in this period",
                        "Once sales, payments and expenses have been recorded, the statements fill in.");
                }
                else
                {
                    HideEmptyState();
                }
            }, "Preparing the report…");
        }

        // ------------------------------------------------------------------ reports

        private async Task<List<StatementRow>> IncomeStatementAsync()
        {
            var statement = Unwrap(await Session.Finance.GetIncomeStatementAsync(
                _from.Value.Date, _to.Value.Date));

            if (statement is null) return new List<StatementRow>();

            Card(_headline, _headlineHint, "Net revenue", statement.NetRevenue,
                $"gross {statement.Revenue:N2}");

            Card(_second, _secondHint, "Gross profit", statement.GrossProfit,
                $"{statement.GrossMarginPercent:N1}% margin");

            Card(_third, _thirdHint, "Total expenses", statement.TotalExpenses,
                $"cost of sales {statement.CostOfGoodsSold:N2}");

            Card(_balanceCheck, _balanceCheckHint, "Net income", statement.NetIncome,
                $"{statement.NetMarginPercent:N1}% of revenue");

            _balanceCheck.ForeColor = statement.NetIncome < 0m ? UiTheme.Danger : UiTheme.TextPrimary;

            var rows = new List<StatementRow> { Heading("REVENUE") };
            rows.AddRange(statement.RevenueLines.Select(Line));

            rows.Add(Heading("COST OF SALES"));
            rows.AddRange(statement.CostOfSalesLines.Select(Line));

            rows.Add(Heading("EXPENSES"));
            rows.AddRange(statement.ExpenseLines.Select(Line));

            rows.Add(new StatementRow
            {
                Label = "NET INCOME",
                Right = statement.NetIncome.ToString("N2"),
                IsSubtotal = true
            });

            SetStatus(
                $"{statement.FromUtc:d MMM yyyy} – {statement.ToUtc.AddDays(-1):d MMM yyyy}. " +
                "Revenue less cost of sales is gross profit; less expenses is net income.");

            return rows;
        }

        private async Task<List<StatementRow>> BalanceSheetAsync()
        {
            var sheet = Unwrap(await Session.Finance.GetBalanceSheetAsync(_to.Value.Date));

            if (sheet is null) return new List<StatementRow>();

            Card(_headline, _headlineHint, "Total assets", sheet.TotalAssets,
                $"fixed {sheet.FixedAssets:N2}");

            Card(_second, _secondHint, "Total liabilities", sheet.TotalLiabilities, "what the gym owes");
            Card(_third, _thirdHint, "Total equity", sheet.TotalEquity,
                $"retained {sheet.RetainedEarnings:N2}");

            _balanceCheck.Text = sheet.IsBalanced ? "Yes" : "No";
            _balanceCheck.ForeColor = sheet.IsBalanced ? UiTheme.Success : UiTheme.Danger;
            _balanceCheckHint.Text = sheet.IsBalanced
                ? "assets = liabilities + equity"
                : $"out by {Math.Abs(sheet.TotalAssets - sheet.LiabilitiesAndEquity):N2}";

            var rows = new List<StatementRow> { Heading("ASSETS") };
            rows.AddRange(sheet.AssetLines.Select(Line));

            rows.Add(Heading("LIABILITIES"));
            rows.AddRange(sheet.LiabilityLines.Select(Line));

            rows.Add(Heading("EQUITY"));
            rows.AddRange(sheet.EquityLines.Select(Line));

            rows.Add(new StatementRow
            {
                Label = "LIABILITIES AND EQUITY",
                Right = sheet.LiabilitiesAndEquity.ToString("N2"),
                IsSubtotal = true
            });

            SetStatus($"As at {sheet.AsOfUtc:d MMM yyyy}. " +
                      (sheet.IsBalanced
                          ? "Assets equal liabilities plus equity."
                          : "The sheet does not balance - check the trial balance."));

            return rows;
        }

        private async Task<List<StatementRow>> TrialBalanceAsync()
        {
            var balance = Unwrap(await Session.Finance.GetTrialBalanceAsync(_to.Value.Date));

            if (balance is null) return new List<StatementRow>();

            Card(_headline, _headlineHint, "Total debits", balance.TotalDebit, "across every account");
            Card(_second, _secondHint, "Total credits", balance.TotalCredit, "across every account");
            Card(_third, _thirdHint, "Accounts with a balance", balance.Rows.Count, "");

            _balanceCheck.Text = balance.IsBalanced ? "Yes" : "No";
            _balanceCheck.ForeColor = balance.IsBalanced ? UiTheme.Success : UiTheme.Danger;
            _balanceCheckHint.Text = balance.IsBalanced
                ? "the two sides agree"
                : $"out by {Math.Abs(balance.Difference):N2}";

            var rows = balance.Rows.Select(r => new StatementRow
            {
                Label = $"{r.AccountCode}  {r.AccountName}",
                Left = r.Debit == 0m ? "" : r.Debit.ToString("N2"),
                Right = r.Credit == 0m ? "" : r.Credit.ToString("N2")
            }).ToList();

            rows.Add(new StatementRow
            {
                Label = "TOTAL",
                Left = balance.TotalDebit.ToString("N2"),
                Right = balance.TotalCredit.ToString("N2"),
                IsSubtotal = true
            });

            SetStatus(balance.IsBalanced
                ? $"As at {balance.AsOfUtc:d MMM yyyy}. Debits and credits agree, as they must."
                : $"As at {balance.AsOfUtc:d MMM yyyy}. The two sides differ by " +
                  $"{Math.Abs(balance.Difference):N2} - something was written outside the ledger.");

            return rows;
        }

        private async Task<List<StatementRow>> CashFlowAsync()
        {
            var flow = Unwrap(await Session.Finance.GetCashFlowAsync(_from.Value.Date, _to.Value.Date));

            if (flow is null) return new List<StatementRow>();

            Card(_headline, _headlineHint, "Opening cash", flow.OpeningCash, "at the start");
            Card(_second, _secondHint, "Received", flow.CashFromCustomers, "from members and customers");
            Card(_third, _thirdHint, "Paid out",
                Math.Abs(flow.CashToSuppliers + flow.CashToEmployees + flow.CashForExpenses),
                "suppliers, staff and bills");

            _balanceCheck.Text = flow.ClosingCash.ToString("N2");
            _balanceCheck.ForeColor = flow.ClosingCash < 0m ? UiTheme.Danger : UiTheme.TextPrimary;
            _balanceCheckHint.Text = $"net {flow.NetCashFlow:N2}";

            SetStatus(
                $"{flow.FromUtc:d MMM yyyy} – {flow.ToUtc.AddDays(-1):d MMM yyyy}. " +
                "Every movement through cash and bank, grouped by what caused it.");

            return flow.Lines.Select(Line).ToList();
        }

        // ------------------------------------------------------------------ helpers

        private static StatementRow Heading(string text) =>
            new() { Label = text, IsSubtotal = true };

        private static StatementRow Line(StatementLineDto line) => new()
        {
            Label = new string(' ', line.Depth * 4) +
                    (string.IsNullOrWhiteSpace(line.AccountCode)
                        ? line.Label
                        : $"{line.AccountCode}  {line.Label}"),
            Right = line.Amount.ToString("N2"),
            IsSubtotal = line.IsSubtotal
        };

        private static void Card(Label value, Label hint, string caption, decimal amount, string detail)
        {
            // The caption sits on the card itself, which StatCard renders from the label it
            // was built with - so the caption is set by walking to it rather than rebuilt,
            // and the four cards can change meaning as the report does.
            if (value.Parent is { } card)
            {
                foreach (Control control in card.Controls)
                {
                    if (control is Label label && label != value && label != hint)
                    {
                        label.Text = caption.ToUpperInvariant();
                        break;
                    }
                }
            }

            value.Text = amount.ToString("N2");
            value.ForeColor = UiTheme.TextPrimary;
            hint.Text = detail;
        }

        private async Task ExportAsync()
        {
            if (_rows.Count == 0)
            {
                ShowError("There is nothing to export yet.");
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "Export report",
                Filter = "CSV file (*.csv)|*.csv",
                FileName = $"fitcore-{Selected.Replace(' ', '-').ToLowerInvariant()}-" +
                           $"{_to.Value:yyyyMMdd}.csv"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            await GuardAsync(async () =>
            {
                var csv = new StringBuilder();

                csv.AppendLine($"FitCore · {Selected}");
                csv.AppendLine($"{_from.Value:d MMM yyyy} to {_to.Value:d MMM yyyy}");
                csv.AppendLine();
                csv.AppendLine("Line,Debit,Credit");

                foreach (var row in _rows)
                {
                    csv.AppendLine($"\"{row.Label.Replace("\"", "\"\"")}\",{row.Left},{row.Right}");
                }

                await File.WriteAllTextAsync(dialog.FileName, csv.ToString(), Encoding.UTF8);

                Notify($"Exported to {Path.GetFileName(dialog.FileName)}.");
            }, "Exporting…");
        }
    }
}
