using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The Finance module's landing screen: what the gym earned, what it spent, what it owns
    /// and what it owes.
    ///
    /// Every figure is read from the general ledger rather than recomputed from the operational
    /// tables, so this screen and the income statement cannot disagree. When something has
    /// happened that the ledger has not caught up with - automatic posting switched off, or a
    /// posting that failed at the time - the count is shown here with a way to put it right,
    /// rather than being discovered when a statement looks wrong.
    /// </summary>
    internal sealed class FinanceOverviewPage : ModulePageBase
    {
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly Panel _body;

        private readonly Label _revenue;
        private readonly Label _revenueHint;
        private readonly Label _grossProfit;
        private readonly Label _grossProfitHint;
        private readonly Label _expenses;
        private readonly Label _expensesHint;
        private readonly Label _netIncome;
        private readonly Label _netIncomeHint;
        private readonly Label _cash;
        private readonly Label _cashHint;
        private readonly Label _receivable;
        private readonly Label _receivableHint;
        private readonly Label _payable;
        private readonly Label _payableHint;
        private readonly Label _inventory;
        private readonly Label _inventoryHint;

        private readonly FitChart _trend;
        private readonly FitChart _expenseMix;
        private readonly FitChart _revenueMix;

        private Button? _postOutstanding;

        public FinanceOverviewPage(FitCoreSession session)
            : base(session, "Finance",
                   "Revenue, cost of sales, expenses and what the gym is worth - straight from the ledger.")
        {
            var today = DateTime.Today;

            _from = UiKit.DatePicker(new DateTime(today.Year, today.Month, 1));
            _to = UiKit.DatePicker(today);

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);

            AddAction("Apply", ButtonTone.Secondary, LoadAsync, 86);

            StatsRow.Controls.Add(UiKit.StatCard("Net revenue", out _revenue, out _revenueHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("Gross profit", out _grossProfit, out _grossProfitHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Total expenses", out _expenses, out _expensesHint, UiTheme.Warning));
            StatsRow.Controls.Add(UiKit.StatCard("Net income", out _netIncome, out _netIncomeHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Cash and bank", out _cash, out _cashHint, UiTheme.Info));
            StatsRow.Controls.Add(UiKit.StatCard("Owed to us", out _receivable, out _receivableHint, UiTheme.Warning));
            StatsRow.Controls.Add(UiKit.StatCard("We owe", out _payable, out _payableHint, UiTheme.Danger));
            StatsRow.Controls.Add(UiKit.StatCard("Stock value", out _inventory, out _inventoryHint, UiTheme.Neutral));

            _trend = new FitChart { Size = new Size(760, 330) };
            _revenueMix = new FitChart { Size = new Size(400, 330) };
            _expenseMix = new FitChart { Size = new Size(400, 330) };

            var charts = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = UiTheme.Canvas
            };

            charts.Controls.Add(_trend);
            charts.Controls.Add(_revenueMix);
            charts.Controls.Add(_expenseMix);

            _body = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.Canvas
            };

            _body.Controls.Add(charts);

            SetBody(_body);
        }

        protected override bool UsesGrid => false;

        public override async Task LoadAsync()
        {
            await GuardAsync(async () =>
            {
                var overview = Unwrap(await Session.Finance.GetOverviewAsync(
                    _from.Value.Date, _to.Value.Date));

                if (overview is null) return;

                Money(_revenue, overview.Revenue);
                _revenueHint.Text = $"cost of sales {overview.CostOfGoodsSold:N2}";

                Money(_grossProfit, overview.GrossProfit);
                _grossProfitHint.Text = overview.Revenue == 0m
                    ? "no revenue yet"
                    : $"{overview.GrossProfit / overview.Revenue * 100m:N1}% margin";

                Money(_expenses, overview.OperatingExpenses + overview.PayrollExpenses);
                _expensesHint.Text = $"payroll {overview.PayrollExpenses:N2}";

                Money(_netIncome, overview.NetIncome);
                _netIncome.ForeColor = overview.NetIncome < 0m ? UiTheme.Danger : UiTheme.TextPrimary;
                _netIncomeHint.Text = overview.NetIncome < 0m ? "a loss for the period" : "for the period";

                Money(_cash, overview.CashOnHand);
                _cashHint.Text = "on hand and in bank";

                Money(_receivable, overview.AccountsReceivable);
                _receivableHint.Text = "members and customers";

                Money(_payable, overview.AccountsPayable);
                _payableHint.Text = "suppliers and unpaid bills";

                Money(_inventory, overview.InventoryValue);
                _inventoryHint.Text = "at weighted average cost";

                _trend.Definition = new ChartDefinitionDto
                {
                    Key = "finance-trend",
                    Title = "Revenue, expenses and net income",
                    Caption = "Twelve months from the general ledger.",
                    ChartType = ChartTypes.Line,
                    ValueFormat = KpiFormats.Money,
                    Labels = overview.RevenueByMonth.Select(p => p.Label).ToList(),
                    Series = new List<ChartSeriesDto>
                    {
                        new() { Name = "Revenue", Values = overview.RevenueByMonth.Select(p => p.Value).ToList() },
                        new() { Name = "Expenses", Values = overview.ExpensesByMonth.Select(p => p.Value).ToList() },
                        new() { Name = "Net income", Values = overview.NetIncomeByMonth.Select(p => p.Value).ToList() }
                    }
                };

                _revenueMix.Definition = Breakdown(
                    "revenue-mix", "Where revenue comes from",
                    "Each revenue account over the period.", overview.RevenueBreakdown);

                _expenseMix.Definition = Breakdown(
                    "expense-mix", "Where the money goes",
                    "Operating expenses, grouped as the income statement shows them.",
                    overview.ExpenseBreakdown);

                UpdateOutstanding(overview.UnpostedCount);

                SetStatus(
                    $"{overview.FromUtc:d MMM yyyy} – {overview.ToUtc.AddDays(-1):d MMM yyyy} · " +
                    $"assets {overview.TotalAssets:N2}, liabilities {overview.TotalLiabilities:N2}, " +
                    $"equity {overview.TotalEquity:N2}.");
            }, "Reading the ledger…");
        }

        private static ChartDefinitionDto Breakdown(
            string key, string title, string caption, List<CategorySliceDto> slices) => new()
            {
                Key = key,
                Title = title,
                Caption = caption,
                ChartType = ChartTypes.Donut,
                ValueFormat = KpiFormats.Money,
                Labels = slices.Select(s => s.Label).ToList(),
                Series = new List<ChartSeriesDto>
                {
                    new() { Name = "Amount", Values = slices.Select(s => Math.Abs(s.Value)).ToList() }
                }
            };

        private static void Money(Label label, decimal value) => label.Text = value.ToString("N2");

        /// <summary>
        /// Offers to post anything the ledger has not caught up with.
        ///
        /// The button only appears when there is something to do. A permanent "post
        /// everything" control invites an operator to press it as a ritual, which teaches them
        /// that the books are something they have to maintain by hand - and they are not.
        /// </summary>
        private void UpdateOutstanding(int count)
        {
            if (count <= 0)
            {
                if (_postOutstanding is null) return;

                Toolbar.Controls.Remove(_postOutstanding);
                _postOutstanding.Dispose();
                _postOutstanding = null;
                return;
            }

            var caption = $"Post {count} pending";

            if (_postOutstanding is not null)
            {
                _postOutstanding.Text = caption;
                return;
            }

            _postOutstanding = AddAction(caption, ButtonTone.Warning, PostOutstandingAsync, 140);
        }

        private async Task PostOutstandingAsync()
        {
            if (UiKit.Confirm(
                    "Post every completed transaction that has not reached the ledger yet?\r\n\r\n" +
                    "Anything already posted is skipped, so this is safe to run more than once.")
                != DialogResult.Yes)
            {
                return;
            }

            await GuardAsync(async () =>
            {
                var result = Unwrap(await Session.Finance.PostOutstandingAsync());
                if (result is null) return;

                await LoadAsync();

                Notify(result.Posted == 0
                    ? "Nothing was left to post."
                    : $"Posted {result.Posted} transaction{(result.Posted == 1 ? "" : "s")} to the ledger." +
                      (result.Remaining > 0 ? $" {result.Remaining} could not be posted." : ""));
            }, "Posting…");
        }
    }
}
