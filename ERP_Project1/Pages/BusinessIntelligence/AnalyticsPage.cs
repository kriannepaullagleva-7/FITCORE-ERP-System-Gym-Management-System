using System.Drawing;
using System.Text;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The Business Intelligence screen.
    ///
    /// One page serves every analytics area - membership, sales, payments, inventory,
    /// workforce, finance, profitability, and the Super Admin's platform views - because the
    /// server answers all of them in the same shape: a set of KPI groups, a set of chart
    /// definitions and the rows behind them. Adding an area is a method on the server, not
    /// another form here, and every figure on every chart is computed once on the side that
    /// can see the database.
    ///
    /// The date range is a filter over the whole screen, and every KPI is measured twice - over
    /// the window asked for and over the equal-length window before it - so a card says what
    /// changed rather than only where things stand.
    /// </summary>
    internal sealed class AnalyticsPage : ModulePageBase
    {
        private readonly Func<DateTime?, DateTime?, Task<ApiResult<AnalyticsViewDto>>> _fetch;

        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly ComboBox _preset;

        private readonly Panel _body;
        private AnalyticsViewDto? _view;

        private bool _suppressPreset;

        public AnalyticsPage(
            FitCoreSession session,
            string title,
            string subtitle,
            Func<DateTime?, DateTime?, Task<ApiResult<AnalyticsViewDto>>> fetch)
            : base(session, title, subtitle)
        {
            _fetch = fetch;

            _body = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(0, 0, UiTheme.SpaceS, 0)
            };

            SetBody(_body);

            // Presets first, because "this month" is what an operator actually wants nine
            // times in ten and picking two dates for it is three clicks too many.
            _preset = UiKit.Select(160);
            _preset.Items.AddRange(new object[]
            {
                "This month", "Last month", "Last 7 days", "Last 30 days",
                "Last 90 days", "This year", "Custom"
            });
            _preset.SelectedIndex = 0;
            _preset.SelectedIndexChanged += async (_, _) => await PresetChangedAsync();

            var now = DateTime.Today;

            _from = UiKit.DatePicker(new DateTime(now.Year, now.Month, 1));
            _to = UiKit.DatePicker(now);

            _from.ValueChanged += (_, _) => MarkCustom();
            _to.ValueChanged += (_, _) => MarkCustom();

            FilterBar.Controls.Add(UiKit.FilterLabel("Period"));
            FilterBar.Controls.Add(_preset);
            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);

            AddAction("Apply", ButtonTone.Secondary, LoadAsync, 86);
            AddAction("Export", ButtonTone.Secondary, ExportAsync, 88);
        }

        protected override bool UsesGrid => false;

        public override async Task LoadAsync()
        {
            await GuardAsync(async () =>
            {
                var result = await _fetch(_from.Value.Date, _to.Value.Date);

                var view = Unwrap(result);
                if (view is null) return;

                _view = view;
                Render(view);

                SetStatus(
                    $"{view.Groups.Sum(g => g.Cards.Count)} measures and {view.Charts.Count} " +
                    $"chart{(view.Charts.Count == 1 ? "" : "s")} for " +
                    $"{view.FromUtc:d MMM yyyy} – {view.ToUtc.AddDays(-1):d MMM yyyy}, " +
                    $"compared with the {(view.ToUtc - view.FromUtc).Days} days before it.");
            }, "Calculating…");
        }

        // ------------------------------------------------------------------ filters

        private void MarkCustom()
        {
            if (_suppressPreset) return;

            _suppressPreset = true;
            _preset.SelectedItem = "Custom";
            _suppressPreset = false;
        }

        private async Task PresetChangedAsync()
        {
            if (_suppressPreset) return;

            var today = DateTime.Today;

            var (from, to) = (_preset.SelectedItem as string) switch
            {
                "Last month" => (
                    new DateTime(today.Year, today.Month, 1).AddMonths(-1),
                    new DateTime(today.Year, today.Month, 1).AddDays(-1)),

                "Last 7 days" => (today.AddDays(-6), today),
                "Last 30 days" => (today.AddDays(-29), today),
                "Last 90 days" => (today.AddDays(-89), today),
                "This year" => (new DateTime(today.Year, 1, 1), today),
                "Custom" => (_from.Value.Date, _to.Value.Date),
                _ => (new DateTime(today.Year, today.Month, 1), today)
            };

            _suppressPreset = true;
            _from.Value = from;
            _to.Value = to;
            _suppressPreset = false;

            if ((_preset.SelectedItem as string) != "Custom") await LoadAsync();
        }

        // ------------------------------------------------------------------ rendering

        private void Render(AnalyticsViewDto view)
        {
            _body.SuspendLayout();

            foreach (Control child in _body.Controls) child.Dispose();
            _body.Controls.Clear();

            // Added bottom-first: a Top-docked stack draws in reverse order of addition, so
            // building the page backwards is what puts the KPIs above the charts.
            foreach (var table in Enumerable.Reverse(view.Tables))
            {
                _body.Controls.Add(BuildTable(table));
            }

            if (view.Charts.Count > 0)
            {
                _body.Controls.Add(BuildCharts(view));
            }

            foreach (var group in Enumerable.Reverse(view.Groups))
            {
                _body.Controls.Add(BuildKpiGroup(group));
            }

            _body.ResumeLayout();
        }

        private static Control BuildKpiGroup(KpiGroupDto group)
        {
            var host = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(0, 0, 0, UiTheme.SpaceS)
            };

            var cards = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = UiTheme.Canvas
            };

            foreach (var kpi in group.Cards)
            {
                cards.Controls.Add(BuildKpiCard(kpi));
            }

            var heading = UiKit.SectionHeading(group.Title);
            heading.Dock = DockStyle.Top;
            heading.AutoSize = false;
            heading.Height = 26;

            host.Controls.Add(cards);
            host.Controls.Add(heading);

            return host;
        }

        private static Control BuildKpiCard(KpiCardDto kpi)
        {
            // The accent says whether the movement is welcome, which is a property of the
            // measure rather than of the number: more members is good, more overdue debt is
            // not, and the sign alone cannot tell them apart.
            var accent = !kpi.HasComparison || kpi.Delta == 0m
                ? UiTheme.Neutral
                : kpi.IsFavourable ? UiTheme.Success : UiTheme.Danger;

            var card = UiKit.StatCard(kpi.Label, out var value, out var hint, accent);

            value.Text = kpi.Display;

            hint.Text = kpi.HasComparison
                ? $"{kpi.Movement} vs previous"
                : string.IsNullOrWhiteSpace(kpi.Hint) ? "no prior period" : kpi.Hint;

            hint.ForeColor = !kpi.HasComparison ? UiTheme.TextMuted : accent;

            return card;
        }

        private static Control BuildCharts(AnalyticsViewDto view)
        {
            var host = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(0, 0, 0, UiTheme.SpaceS)
            };

            foreach (var definition in view.Charts)
            {
                // Part-to-whole charts are squarer; a trend needs width to be readable at all.
                var wide = definition.ChartType is not (ChartTypes.Pie or ChartTypes.Donut);

                host.Controls.Add(new FitChart
                {
                    Definition = definition,
                    Size = new Size(wide ? 660 : 400, 320)
                });
            }

            return host;
        }

        private static Control BuildTable(AnalyticsTableDto table)
        {
            var host = new Panel
            {
                Dock = DockStyle.Top,
                Height = Math.Min(420, 96 + (table.Rows.Count * 26)),
                BackColor = UiTheme.Surface,
                Padding = new Padding(1),
                Margin = new Padding(0, 0, 0, UiTheme.SpaceM)
            };

            host.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, host.Width, host.Height),
                UiTheme.Surface, UiTheme.Border);

            var grid = UiKit.Grid();
            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.Margin = new Padding(1);

            foreach (var column in table.Columns)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = column,
                    Name = column,

                    // Everything after the first column is a figure, and figures read right.
                    DefaultCellStyle = new DataGridViewCellStyle
                    {
                        Alignment = grid.Columns.Count == 0
                            ? DataGridViewContentAlignment.MiddleLeft
                            : DataGridViewContentAlignment.MiddleRight
                    }
                });
            }

            foreach (var row in table.Rows)
            {
                grid.Rows.Add(row.Cast<object>().ToArray());
            }

            var heading = new Label
            {
                Text = table.Title,
                Font = UiTheme.SectionTitle,
                ForeColor = UiTheme.TextPrimary,
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(UiTheme.SpaceM, 12, 0, 0),
                BackColor = UiTheme.Surface,
                UseMnemonic = false
            };

            host.Controls.Add(grid);
            host.Controls.Add(heading);

            return host;
        }

        // ------------------------------------------------------------------ export

        /// <summary>
        /// Writes everything on screen to a CSV: the KPIs, every chart's own numbers, and the
        /// drill-down tables.
        ///
        /// The charts go in too, rather than only the tables. A chart is a picture of numbers
        /// and an export that leaves them out sends the operator back to the screen to read
        /// values off a bar, which is the thing a spreadsheet was supposed to replace.
        /// </summary>
        private async Task ExportAsync()
        {
            if (_view is null)
            {
                ShowError("There is nothing to export yet.");
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "Export analytics",
                Filter = "CSV file (*.csv)|*.csv",
                FileName = $"fitcore-{_view.Key}-{_view.FromUtc:yyyyMMdd}-{_view.ToUtc:yyyyMMdd}.csv"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            await GuardAsync(async () =>
            {
                var csv = BuildCsv(_view);

                await File.WriteAllTextAsync(dialog.FileName, csv, Encoding.UTF8);

                Notify($"Exported to {Path.GetFileName(dialog.FileName)}.");
            }, "Exporting…");
        }

        private static string BuildCsv(AnalyticsViewDto view)
        {
            var csv = new StringBuilder();

            csv.AppendLine(Escape(view.Title));
            csv.AppendLine($"{Escape("Period")},{Escape($"{view.FromUtc:d MMM yyyy} to {view.ToUtc.AddDays(-1):d MMM yyyy}")}");
            csv.AppendLine();

            foreach (var group in view.Groups)
            {
                csv.AppendLine(Escape(group.Title));
                csv.AppendLine("Measure,Value,Previous,Change %");

                foreach (var card in group.Cards)
                {
                    csv.AppendLine(string.Join(",",
                        Escape(card.Label),
                        Escape(card.Display),
                        Escape(card.HasComparison ? card.PreviousValue.ToString("N2") : ""),
                        Escape(card.HasComparison ? card.DeltaPercent.ToString("N1") : "")));
                }

                csv.AppendLine();
            }

            foreach (var chart in view.Charts)
            {
                csv.AppendLine(Escape(chart.Title));
                csv.AppendLine(string.Join(",",
                    new[] { "" }.Concat(chart.Series.Select(s => Escape(s.Name)))));

                for (var i = 0; i < chart.Labels.Count; i++)
                {
                    var cells = new List<string> { Escape(chart.Labels[i]) };

                    foreach (var series in chart.Series)
                    {
                        cells.Add(i < series.Values.Count ? series.Values[i].ToString("0.##") : "");
                    }

                    csv.AppendLine(string.Join(",", cells));
                }

                csv.AppendLine();
            }

            foreach (var table in view.Tables)
            {
                csv.AppendLine(Escape(table.Title));
                csv.AppendLine(string.Join(",", table.Columns.Select(Escape)));

                foreach (var row in table.Rows)
                {
                    csv.AppendLine(string.Join(",", row.Select(Escape)));
                }

                csv.AppendLine();
            }

            return csv.ToString();
        }

        /// <summary>
        /// Quotes a CSV field when it needs it. A gym's product names contain commas often
        /// enough that skipping this produces a file that opens with the columns shifted.
        /// </summary>
        private static string Escape(string? value)
        {
            var text = value ?? "";

            if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\n')) return text;

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
