using System.ComponentModel;
using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// An aged balance: what is owed, bucketed by how long it has been outstanding.
    ///
    /// Ageing is the point. A total of ₱40,000 owed says nothing on its own; ₱40,000 that has
    /// been sitting for ninety days is a different problem from ₱40,000 invoiced last week,
    /// and it is the one worth acting on today.
    ///
    /// Shared by receivables and payables because they are the same report read in opposite
    /// directions.
    /// </summary>
    internal abstract class AgedBalancePage : ModulePageBase
    {
        private readonly DateTimePicker _asOf;
        private readonly ComboBox _bucket;

        private readonly Label _current;
        private readonly Label _currentHint;
        private readonly Label _overdue;
        private readonly Label _overdueHint;
        private readonly Label _oldest;
        private readonly Label _oldestHint;
        private readonly Label _total;
        private readonly Label _totalHint;

        private readonly FitChart _ageing;
        private AgedBalanceDto? _view;

        protected AgedBalancePage(
            FitCoreSession session, string title, string subtitle, string partyHeader)
            : base(session, title, subtitle)
        {
            _asOf = UiKit.DatePicker(DateTime.Today);

            _bucket = UiKit.Select(150);
            _bucket.Items.AddRange(new object[]
            {
                "All ages", "Current", "1-30 days", "31-60 days", "61-90 days", "Over 90 days"
            });
            _bucket.SelectedIndex = 0;
            _bucket.SelectedIndexChanged += (_, _) => ApplyBucket();

            FilterBar.Controls.Add(UiKit.FilterLabel("As at"));
            FilterBar.Controls.Add(_asOf);
            FilterBar.Controls.Add(UiKit.FilterLabel("Age"));
            FilterBar.Controls.Add(_bucket);

            AddAction("Apply", ButtonTone.Secondary, LoadAsync, 86);

            StatsRow.Controls.Add(UiKit.StatCard("Not yet due", out _current, out _currentHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Overdue", out _overdue, out _overdueHint, UiTheme.Warning));
            StatsRow.Controls.Add(UiKit.StatCard("Over 90 days", out _oldest, out _oldestHint, UiTheme.Danger));
            StatsRow.Controls.Add(UiKit.StatCard("Total outstanding", out _total, out _totalHint, UiTheme.Primary));

            _ageing = new FitChart { Dock = DockStyle.Top, Height = 260 };

            Grid.AutoGenerateColumns = false;

            void Column(string property, string header, int fill, bool right = false, string? format = null)
            {
                var column = new DataGridViewTextBoxColumn
                {
                    Name = property,
                    DataPropertyName = property,
                    HeaderText = header,
                    FillWeight = fill
                };

                if (right) column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                if (format is not null) column.DefaultCellStyle.Format = format;

                Grid.Columns.Add(column);
            }

            Column(nameof(AgedRowDto.Party), partyHeader, 160);
            Column(nameof(AgedRowDto.Document), "Document", 110);
            Column(nameof(AgedRowDto.DocumentDate), "Dated", 75, format: "d MMM yyyy");
            Column(nameof(AgedRowDto.DaysOutstanding), "Days", 50, right: true, format: "N0");
            Column(nameof(AgedRowDto.Total), "Total", 80, right: true, format: "N2");
            Column(nameof(AgedRowDto.Settled), "Settled", 80, right: true, format: "N2");
            Column(nameof(AgedRowDto.Balance), "Balance", 85, right: true, format: "N2");
            Column(nameof(AgedRowDto.Bucket), "Age", 80);

            UiKit.PaintStatusColumns(Grid, nameof(AgedRowDto.Bucket));
            UiKit.SetMinimumColumnWidths(Grid);
        }

        protected abstract Task<ApiResult<AgedBalanceDto>> FetchAsync(DateTime asOf);

        public override async Task LoadAsync()
        {
            await GuardAsync(async () =>
            {
                var view = Unwrap(await FetchAsync(_asOf.Value.Date));
                if (view is null) return;

                _view = view;

                _current.Text = view.Current.ToString("N2");
                _currentHint.Text = "within terms";

                var overdue = view.Days1To30 + view.Days31To60 + view.Days61To90 + view.Over90Days;
                _overdue.Text = overdue.ToString("N2");
                _overdueHint.Text = view.Total == 0m
                    ? "nothing outstanding"
                    : $"{overdue / view.Total * 100m:N0}% of the total";

                _oldest.Text = view.Over90Days.ToString("N2");
                _oldest.ForeColor = view.Over90Days > 0m ? UiTheme.Danger : UiTheme.TextPrimary;
                _oldestHint.Text = view.Over90Days > 0m ? "worth chasing today" : "nothing this old";

                _total.Text = view.Total.ToString("N2");
                _totalHint.Text = $"{UiKit.Plural(view.Rows.Count, "document")}";

                _ageing.Definition = new ChartDefinitionDto
                {
                    Key = "ageing",
                    Title = "Outstanding by age",
                    Caption = "Debt that has sat longer is less likely to be collected.",
                    ChartType = ChartTypes.Bar,
                    ValueFormat = KpiFormats.Money,
                    Labels = new List<string>
                        { "Current", "1-30 days", "31-60 days", "61-90 days", "Over 90" },
                    Series = new List<ChartSeriesDto>
                    {
                        new()
                        {
                            Name = "Outstanding",
                            Values = new List<decimal>
                            {
                                view.Current, view.Days1To30, view.Days31To60,
                                view.Days61To90, view.Over90Days
                            }
                        }
                    }
                };

                ApplyBucket();
            }, "Ageing the balances…");
        }

        private void ApplyBucket()
        {
            if (_view is null) return;

            var rows = _bucket.SelectedIndex <= 0
                ? _view.Rows
                : _view.Rows.Where(r => r.Bucket == (string)_bucket.SelectedItem!).ToList();

            Grid.DataSource = new BindingList<AgedRowDto>(rows);

            if (_view.Rows.Count == 0)
            {
                ShowEmptyState("Nothing outstanding",
                    "Everything recorded up to this date has been settled.");
            }
            else if (rows.Count == 0)
            {
                ShowEmptyState("Nothing in this age band",
                    "Try a different age, or clear the filter to see everything outstanding.",
                    "Show all", (_, _) => _bucket.SelectedIndex = 0);
            }
            else
            {
                HideEmptyState();
            }

            SetStatus(
                $"{rows.Sum(r => r.Balance):N2} outstanding across " +
                $"{UiKit.Plural(rows.Count, "document")}, as at {_asOf.Value:d MMM yyyy}.");
        }

        /// <summary>The ageing chart sits above the grid, so the shape is read before the rows.</summary>
        protected FitChart AgeingChart => _ageing;
    }

    internal sealed class ReceivablesPage : AgedBalancePage
    {
        public ReceivablesPage(FitCoreSession session)
            : base(session, "Accounts Receivable",
                   "What members and customers owe, and how long they have owed it.",
                   "Member")
        {
            StatsRow.Controls.Add(AgeingChart);
            AgeingChart.Size = new Size(560, 260);
        }

        protected override Task<ApiResult<AgedBalanceDto>> FetchAsync(DateTime asOf) =>
            Session.Finance.GetReceivablesAsync(asOf);
    }

    internal sealed class PayablesPage : AgedBalancePage
    {
        public PayablesPage(FitCoreSession session)
            : base(session, "Accounts Payable",
                   "What the gym owes suppliers and on unpaid bills, aged the same way.",
                   "Supplier")
        {
            StatsRow.Controls.Add(AgeingChart);
            AgeingChart.Size = new Size(560, 260);
        }

        protected override Task<ApiResult<AgedBalanceDto>> FetchAsync(DateTime asOf) =>
            Session.Finance.GetPayablesAsync(asOf);
    }
}
