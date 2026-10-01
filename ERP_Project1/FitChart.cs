using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The FitCore chart palette.
    ///
    /// Eight categorical hues in a fixed order. The order is the colour-blindness safety
    /// mechanism rather than a matter of taste: adjacent pairs were checked for separation
    /// under protanopia, deuteranopia and tritanopia as well as for normal vision, and this
    /// sequence is one that clears every gate on a white surface.
    ///
    /// Two rules follow from that and are enforced below:
    ///
    ///   - **Hues are assigned in slot order and never cycled.** A ninth series does not get a
    ///     generated colour - it folds into "Other". Past eight, adjacent hues stop being
    ///     distinguishable under colour-blind vision and the chart quietly starts lying.
    ///   - **Colour follows the series, not its rank.** Slot 0 is whatever the server listed
    ///     first, so filtering a chart never repaints the survivors and a reader who learned
    ///     "revenue is blue" keeps being right.
    ///
    /// Three of the eight sit below 3:1 contrast against white. That is allowed only with
    /// relief, which is why every chart here carries a legend with text labels and a data-table
    /// view - identity is never carried by colour alone.
    /// </summary>
    internal static class ChartPalette
    {
        public static readonly Color[] Series =
        {
            Color.FromArgb(0x2a, 0x78, 0xd6),   // blue
            Color.FromArgb(0xeb, 0x68, 0x34),   // orange
            Color.FromArgb(0x1b, 0xaf, 0x7a),   // aqua
            Color.FromArgb(0xed, 0xa1, 0x00),   // yellow
            Color.FromArgb(0xe8, 0x7b, 0xa4),   // magenta
            Color.FromArgb(0x00, 0x83, 0x00),   // green
            Color.FromArgb(0x4a, 0x3a, 0xa7),   // violet
            Color.FromArgb(0xe3, 0x49, 0x48)    // red
        };

        /// <summary>Anything beyond the eighth slot has been folded into "Other" already.</summary>
        public const int MaxSlots = 8;

        public static Color At(int index) => Series[Math.Clamp(index, 0, MaxSlots - 1) % MaxSlots];

        /// <summary>Hairline grid, one shade off the surface. Solid - dashing reads as a threshold.</summary>
        public static readonly Color Grid = Color.FromArgb(0xEC, 0xF0, 0xF6);
        public static readonly Color Axis = Color.FromArgb(0xD8, 0xDF, 0xEA);
    }

    /// <summary>
    /// A chart, drawn from a <see cref="ChartDefinitionDto"/> the server produced.
    ///
    /// The division of labour is deliberate: the server decides what the chart *is* - its
    /// shape, its categories and its numbers - and this control decides how it looks. Every
    /// figure is therefore computed once, on the side that can see the database, and a chart
    /// on screen cannot disagree with the report it came from.
    ///
    /// Each chart carries a "Show data" toggle that replaces the plot with the same numbers in
    /// a grid. That is not a nicety: three of the eight series colours sit below 3:1 contrast
    /// on white, and the rule for using them at all is that the reader has a way to get the
    /// values without relying on colour.
    /// </summary>
    internal sealed class FitChart : Panel
    {
        private const int HeaderHeight = 52;
        private const int LegendHeight = 30;
        private const int AxisBand = 26;
        private const int LeftGutter = 62;

        private ChartDefinitionDto? _definition;
        private readonly Canvas _canvas;
        private readonly DataGridView _table;
        private readonly Label _title;
        private readonly Label _caption;
        private readonly LinkLabel _toggle;

        public FitChart()
        {
            BackColor = UiTheme.Surface;
            Padding = new Padding(UiTheme.SpaceM);
            DoubleBuffered = true;
            Margin = new Padding(0, 0, UiTheme.SpaceM, UiTheme.SpaceM);

            Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, Width, Height), UiTheme.Surface, UiTheme.Border);

            _title = new Label
            {
                Font = UiTheme.SectionTitle,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(UiTheme.SpaceM, UiTheme.SpaceM),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };

            _caption = new Label
            {
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Location = new Point(UiTheme.SpaceM + 1, UiTheme.SpaceM + 20),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };

            _toggle = new LinkLabel
            {
                Text = "Show data",
                Font = UiTheme.Small,
                AutoSize = true,
                LinkColor = UiTheme.Primary,
                ActiveLinkColor = UiTheme.PrimaryHover,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Color.Transparent
            };
            _toggle.Click += (_, _) => ShowTable = !ShowTable;

            _canvas = new Canvas(this) { Dock = DockStyle.Fill };

            _table = UiKit.Grid();
            _table.Dock = DockStyle.Fill;
            _table.Visible = false;
            _table.AutoGenerateColumns = false;

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Surface,
                Padding = new Padding(0, HeaderHeight - UiTheme.SpaceM, 0, 0)
            };

            body.Controls.Add(_canvas);
            body.Controls.Add(_table);

            Controls.Add(body);
            Controls.Add(_title);
            Controls.Add(_caption);
            Controls.Add(_toggle);

            Resize += (_, _) => PositionToggle();
            PositionToggle();
        }

        // Neither of these is ever set from a designer - the charts are built in code from
        // whatever the server sent - so both opt out of code serialization rather than teaching
        // the designer how to write a ChartDefinitionDto literal.
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ChartDefinitionDto? Definition
        {
            get => _definition;
            set
            {
                _definition = value;

                _title.Text = value?.Title ?? "";
                _caption.Text = value?.Caption ?? "";

                _toggle.Visible = value is not null && value.Series.Count > 0;

                BuildTable();
                PositionToggle();
                _canvas.Invalidate();
            }
        }

        private bool _showTable;

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowTable
        {
            get => _showTable;
            set
            {
                _showTable = value;

                _table.Visible = value;
                _canvas.Visible = !value;
                _toggle.Text = value ? "Show chart" : "Show data";

                if (value) _table.BringToFront(); else _canvas.BringToFront();
            }
        }

        private void PositionToggle()
        {
            _toggle.Location = new Point(
                Math.Max(UiTheme.SpaceM, Width - _toggle.Width - UiTheme.SpaceM - 2), UiTheme.SpaceM + 2);
        }

        /// <summary>
        /// The same numbers as a grid. Kept in step with the plot rather than fetched
        /// separately, so the two can never disagree.
        /// </summary>
        private void BuildTable()
        {
            _table.Columns.Clear();
            _table.Rows.Clear();

            if (_definition is null || _definition.Series.Count == 0) return;

            _table.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "", Name = "label", FillWeight = 140
            });

            foreach (var series in _definition.Series)
            {
                _table.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = series.Name,
                    Name = "s" + _table.Columns.Count,
                    DefaultCellStyle = new DataGridViewCellStyle
                    {
                        Alignment = DataGridViewContentAlignment.MiddleRight,

                        // Equal-width digits, so a column of figures lines up. Deliberately not
                        // used on the headline numbers elsewhere, where it makes them look loose.
                        Font = new Font(UiTheme.Family, 9.25F)
                    }
                });
            }

            for (var i = 0; i < _definition.Labels.Count; i++)
            {
                var cells = new List<object> { _definition.Labels[i] };

                foreach (var series in _definition.Series)
                {
                    cells.Add(i < series.Values.Count
                        ? FormatValue(series.Values[i], _definition.ValueFormat)
                        : "");
                }

                _table.Rows.Add(cells.ToArray());
            }
        }

        internal static string FormatValue(decimal value, string format) => format switch
        {
            KpiFormats.Money => value.ToString("N2"),
            KpiFormats.Percent => value.ToString("N1") + "%",
            KpiFormats.Decimal => value.ToString("N2"),
            _ => value.ToString("N0")
        };

        /// <summary>Axis ticks are abbreviated - 1.2M is readable where 1,200,000 is not.</summary>
        private static string FormatTick(decimal value, string format)
        {
            if (format == KpiFormats.Percent) return value.ToString("N0") + "%";

            var magnitude = Math.Abs(value);

            return magnitude switch
            {
                >= 1_000_000_000m => (value / 1_000_000_000m).ToString("0.#") + "B",
                >= 1_000_000m => (value / 1_000_000m).ToString("0.#") + "M",
                >= 10_000m => (value / 1_000m).ToString("0.#") + "k",
                _ => value.ToString("N0")
            };
        }

        // ==================================================================== drawing

        /// <summary>
        /// The plot surface. Separated from the card so the title, caption and toggle are
        /// ordinary controls and only the data marks are hand-drawn.
        /// </summary>
        private sealed class Canvas : Control
        {
            private readonly FitChart _owner;
            private int _hoverIndex = -1;
            private int _hoverSeries = -1;

            public Canvas(FitChart owner)
            {
                _owner = owner;
                BackColor = UiTheme.Surface;
                DoubleBuffered = true;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);

                var definition = _owner._definition;
                if (definition is null) return;

                var (index, series) = HitTest(definition, e.Location);

                if (index != _hoverIndex || series != _hoverSeries)
                {
                    _hoverIndex = index;
                    _hoverSeries = series;
                    Invalidate();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);

                if (_hoverIndex < 0 && _hoverSeries < 0) return;

                _hoverIndex = -1;
                _hoverSeries = -1;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                var definition = _owner._definition;

                if (definition is null || definition.Series.Count == 0 ||
                    definition.Series.All(s => s.Values.Count == 0))
                {
                    DrawEmpty(g);
                    return;
                }

                var plot = PlotBounds(definition);

                if (plot.Width < 40 || plot.Height < 40) return;

                switch (definition.ChartType)
                {
                    case ChartTypes.Pie:
                    case ChartTypes.Donut:
                        DrawSlices(g, definition, plot);
                        break;

                    case ChartTypes.HorizontalBar:
                        DrawHorizontalBars(g, definition, plot);
                        break;

                    case ChartTypes.StackedBar:
                        DrawBars(g, definition, plot, stacked: true);
                        break;

                    case ChartTypes.Bar:
                        DrawBars(g, definition, plot, stacked: false);
                        break;

                    default:
                        DrawLines(g, definition, plot,
                            filled: definition.ChartType == ChartTypes.Area);
                        break;
                }

                // A legend is always drawn for two or more series, so identity never rests on
                // colour alone. One series needs none - the title names it.
                if (definition.Series.Count > 1 || IsSliceChart(definition))
                {
                    DrawLegend(g, definition);
                }

                DrawTooltip(g, definition, plot);
            }

            private static bool IsSliceChart(ChartDefinitionDto d) =>
                d.ChartType is ChartTypes.Pie or ChartTypes.Donut;

            private void DrawEmpty(Graphics g)
            {
                var text = "No data for this period.";
                var size = TextRenderer.MeasureText(text, UiTheme.Body);

                TextRenderer.DrawText(g, text, UiTheme.Body,
                    new Point((Width - size.Width) / 2, (Height - size.Height) / 2),
                    UiTheme.TextMuted);
            }

            private Rectangle PlotBounds(ChartDefinitionDto definition)
            {
                var legend = definition.Series.Count > 1 || IsSliceChart(definition) ? LegendHeight : 4;

                if (IsSliceChart(definition))
                {
                    return new Rectangle(8, 8, Width - 16, Height - legend - 16);
                }

                var left = definition.ChartType == ChartTypes.HorizontalBar
                    ? Math.Min(180, Math.Max(90, Width / 4))
                    : LeftGutter;

                return new Rectangle(
                    left, 10,
                    Math.Max(10, Width - left - 16),
                    Math.Max(10, Height - legend - AxisBand - 14));
            }

            // ------------------------------------------------------------ scales

            /// <summary>
            /// A rounded upper bound and a tick step that produce readable axis labels.
            ///
            /// Picking 1, 2, 2.5 or 5 times a power of ten is what turns an awkward maximum of
            /// 4,732 into gridlines at 0, 1,000, 2,000 … rather than at 946, 1,892 and so on.
            /// </summary>
            private static (decimal Max, decimal Min, decimal Step) Scale(
                decimal max, decimal min, int targetTicks)
            {
                if (min > 0m) min = 0m;              // bars are read against zero, always
                if (max <= min) max = min + 1m;

                var range = max - min;
                var rough = range / Math.Max(1, targetTicks);

                var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)Math.Max(rough, 0.0001m))));
                var normalised = rough / magnitude;

                var step = normalised switch
                {
                    <= 1m => 1m,
                    <= 2m => 2m,
                    <= 2.5m => 2.5m,
                    <= 5m => 5m,
                    _ => 10m
                } * magnitude;

                var niceMax = Math.Ceiling(max / step) * step;
                var niceMin = Math.Floor(min / step) * step;

                return (niceMax, niceMin, step);
            }

            private void DrawGrid(
                Graphics g, Rectangle plot, decimal min, decimal max, decimal step, string format)
            {
                using var gridPen = new Pen(ChartPalette.Grid);
                using var axisPen = new Pen(ChartPalette.Axis);

                for (var value = min; value <= max + (step / 2m); value += step)
                {
                    var y = ValueToY(value, min, max, plot);

                    var isZero = value == 0m;
                    g.DrawLine(isZero ? axisPen : gridPen, plot.Left, y, plot.Right, y);

                    TextRenderer.DrawText(
                        g, FormatTick(value, format), UiTheme.Small,
                        new Rectangle(0, y - 9, plot.Left - 8, 18),
                        UiTheme.TextMuted,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
            }

            private static int ValueToY(decimal value, decimal min, decimal max, Rectangle plot)
            {
                var span = max - min;
                if (span == 0m) return plot.Bottom;

                return plot.Bottom - (int)Math.Round((double)((value - min) / span) * plot.Height);
            }

            /// <summary>
            /// Draws the category axis, thinning labels when they would collide.
            ///
            /// Every third label on a crowded axis is honest; overlapping text is not, and
            /// rotating it makes a chart harder to read than dropping some of it.
            /// </summary>
            private void DrawCategoryAxis(Graphics g, ChartDefinitionDto definition, Rectangle plot)
            {
                var count = definition.Labels.Count;
                if (count == 0) return;

                var slot = (float)plot.Width / count;
                var widest = definition.Labels.Max(l => TextRenderer.MeasureText(l, UiTheme.Small).Width);

                var every = Math.Max(1, (int)Math.Ceiling((widest + 12) / Math.Max(1f, slot)));

                for (var i = 0; i < count; i++)
                {
                    if (i % every != 0 && i != count - 1) continue;

                    var centre = plot.Left + (slot * (i + 0.5f));

                    TextRenderer.DrawText(
                        g, definition.Labels[i], UiTheme.Small,
                        new Rectangle((int)(centre - (slot * every / 2)), plot.Bottom + 6,
                                      (int)(slot * every), 18),
                        UiTheme.TextMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding);
                }
            }

            // ------------------------------------------------------------ line and area

            private void DrawLines(
                Graphics g, ChartDefinitionDto definition, Rectangle plot, bool filled)
            {
                var all = definition.Series.SelectMany(s => s.Values).ToList();
                if (all.Count == 0) return;

                var (max, min, step) = Scale(all.Max(), all.Min(), 5);

                DrawGrid(g, plot, min, max, step, definition.ValueFormat);
                DrawCategoryAxis(g, definition, plot);

                var count = definition.Labels.Count;
                if (count == 0) return;

                var slot = (float)plot.Width / Math.Max(1, count);

                for (var s = 0; s < definition.Series.Count && s < ChartPalette.MaxSlots; s++)
                {
                    var series = definition.Series[s];
                    var colour = ChartPalette.At(s);

                    var points = new List<PointF>();

                    for (var i = 0; i < count && i < series.Values.Count; i++)
                    {
                        points.Add(new PointF(
                            plot.Left + (slot * (i + 0.5f)),
                            ValueToY(series.Values[i], min, max, plot)));
                    }

                    if (points.Count == 0) continue;

                    if (filled && points.Count > 1)
                    {
                        var region = new List<PointF>(points)
                        {
                            new(points[^1].X, plot.Bottom),
                            new(points[0].X, plot.Bottom)
                        };

                        // A light wash rather than a solid block: the line carries the value,
                        // the fill only says which side of it is "below".
                        using var fill = new SolidBrush(Color.FromArgb(38, colour));
                        g.FillPolygon(fill, region.ToArray());
                    }

                    using var pen = new Pen(colour, 2f)
                    {
                        LineJoin = LineJoin.Round,
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round
                    };

                    if (points.Count > 1) g.DrawLines(pen, points.ToArray());

                    // Markers only where they fit. A dot on every one of ninety points is a
                    // smear, and the crosshair already answers "what is the value here?".
                    if (points.Count <= 20)
                    {
                        using var surface = new SolidBrush(UiTheme.Surface);
                        using var body = new SolidBrush(colour);

                        foreach (var point in points)
                        {
                            g.FillEllipse(surface, point.X - 5f, point.Y - 5f, 10f, 10f);
                            g.FillEllipse(body, point.X - 3.5f, point.Y - 3.5f, 7f, 7f);
                        }
                    }

                    // One direct label, on the last point, so the reader does not have to go
                    // to the legend to learn which line is which.
                    if (definition.Series.Count <= 4 && points.Count > 0)
                    {
                        var last = points[^1];
                        var text = FormatValue(series.Values[^1], definition.ValueFormat);
                        var size = TextRenderer.MeasureText(text, UiTheme.Small);

                        TextRenderer.DrawText(
                            g, text, UiTheme.Small,
                            new Point((int)Math.Min(last.X + 6, plot.Right - size.Width),
                                      (int)last.Y - size.Height - 4),
                            UiTheme.TextSecondary);
                    }
                }
            }

            // ------------------------------------------------------------ bars

            private void DrawBars(
                Graphics g, ChartDefinitionDto definition, Rectangle plot, bool stacked)
            {
                var count = definition.Labels.Count;
                if (count == 0) return;

                decimal max;

                if (stacked)
                {
                    max = Enumerable.Range(0, count)
                        .Select(i => definition.Series.Sum(s => i < s.Values.Count ? s.Values[i] : 0m))
                        .DefaultIfEmpty(0m)
                        .Max();
                }
                else
                {
                    max = definition.Series.SelectMany(s => s.Values).DefaultIfEmpty(0m).Max();
                }

                var min = definition.Series.SelectMany(s => s.Values).DefaultIfEmpty(0m).Min();

                var (niceMax, niceMin, step) = Scale(max, min, 5);

                DrawGrid(g, plot, niceMin, niceMax, step, definition.ValueFormat);
                DrawCategoryAxis(g, definition, plot);

                var slot = (float)plot.Width / count;
                var groupWidth = Math.Max(4f, slot * 0.62f);
                var seriesCount = Math.Min(definition.Series.Count, ChartPalette.MaxSlots);

                var barWidth = stacked
                    ? groupWidth
                    : Math.Max(3f, (groupWidth - (2f * (seriesCount - 1))) / seriesCount);

                var zeroY = ValueToY(0m, niceMin, niceMax, plot);

                for (var i = 0; i < count; i++)
                {
                    var left = plot.Left + (slot * i) + ((slot - groupWidth) / 2f);
                    var stackTop = (float)zeroY;

                    for (var s = 0; s < seriesCount; s++)
                    {
                        var series = definition.Series[s];
                        if (i >= series.Values.Count) continue;

                        var value = series.Values[i];
                        if (value == 0m) continue;

                        var colour = ChartPalette.At(s);

                        RectangleF bar;

                        if (stacked)
                        {
                            var height = Math.Abs(zeroY - ValueToY(value, niceMin, niceMax, plot));

                            // A 2px gap of surface between segments, rather than a stroke
                            // around each one. A border adds a third colour to every edge.
                            bar = new RectangleF(left, stackTop - height, barWidth,
                                Math.Max(1f, height - 2f));

                            stackTop -= height;
                        }
                        else
                        {
                            var y = ValueToY(value, niceMin, niceMax, plot);
                            var height = Math.Abs(zeroY - y);

                            bar = new RectangleF(
                                left + (s * (barWidth + 2f)),
                                value >= 0m ? y : zeroY,
                                barWidth,
                                Math.Max(1f, height));
                        }

                        DrawBar(g, bar, colour, roundTop: true);
                    }
                }
            }

            private void DrawHorizontalBars(
                Graphics g, ChartDefinitionDto definition, Rectangle plot)
            {
                var series = definition.Series.FirstOrDefault();
                if (series is null || series.Values.Count == 0) return;

                var count = Math.Min(definition.Labels.Count, series.Values.Count);
                if (count == 0) return;

                var max = series.Values.Take(count).DefaultIfEmpty(0m).Max();
                if (max <= 0m) max = 1m;

                var slot = (float)plot.Height / count;
                var barHeight = Math.Max(6f, Math.Min(26f, slot * 0.6f));

                // One measure, one colour. Shading each bar by its own length would burn the
                // colour channel on information the bar length already carries.
                var colour = ChartPalette.At(0);

                for (var i = 0; i < count; i++)
                {
                    var value = series.Values[i];
                    var width = (float)Math.Max(1.0, (double)(value / max) * plot.Width * 0.82);
                    var y = plot.Top + (slot * i) + ((slot - barHeight) / 2f);

                    DrawBar(g, new RectangleF(plot.Left, y, width, barHeight), colour,
                        roundTop: true, horizontal: true);

                    TextRenderer.DrawText(
                        g, definition.Labels[i], UiTheme.Small,
                        new Rectangle(0, (int)y - 2, plot.Left - 10, (int)barHeight + 4),
                        UiTheme.TextSecondary,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis);

                    // The value sits outside the bar end, so it is never clipped by a short one.
                    TextRenderer.DrawText(
                        g, FormatValue(value, definition.ValueFormat), UiTheme.Small,
                        new Rectangle((int)(plot.Left + width) + 6, (int)y - 2, 120, (int)barHeight + 4),
                        UiTheme.TextMuted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                }
            }

            /// <summary>
            /// A bar with 4px rounded ends at the data end and square corners at the baseline,
            /// so the mark reads as growing from the axis rather than floating.
            /// </summary>
            private static void DrawBar(
                Graphics g, RectangleF bounds, Color colour, bool roundTop, bool horizontal = false)
            {
                if (bounds.Width < 0.5f || bounds.Height < 0.5f) return;

                using var brush = new SolidBrush(colour);

                const float radius = 4f;

                if (!roundTop || bounds.Height <= radius * 2 || bounds.Width <= radius * 2)
                {
                    g.FillRectangle(brush, bounds);
                    return;
                }

                using var path = new GraphicsPath();

                if (horizontal)
                {
                    path.AddLine(bounds.Left, bounds.Top, bounds.Right - radius, bounds.Top);
                    path.AddArc(bounds.Right - (radius * 2), bounds.Top, radius * 2, radius * 2, 270, 90);
                    path.AddArc(bounds.Right - (radius * 2), bounds.Bottom - (radius * 2),
                        radius * 2, radius * 2, 0, 90);
                    path.AddLine(bounds.Right - radius, bounds.Bottom, bounds.Left, bounds.Bottom);
                }
                else
                {
                    path.AddArc(bounds.Left, bounds.Top, radius * 2, radius * 2, 180, 90);
                    path.AddArc(bounds.Right - (radius * 2), bounds.Top, radius * 2, radius * 2, 270, 90);
                    path.AddLine(bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom);
                }

                path.CloseFigure();
                g.FillPath(brush, path);
            }

            // ------------------------------------------------------------ pie and donut

            private void DrawSlices(Graphics g, ChartDefinitionDto definition, Rectangle plot)
            {
                var series = definition.Series.FirstOrDefault();
                if (series is null) return;

                var slices = Folded(definition, series);
                var total = slices.Sum(s => s.Value);

                if (total <= 0m)
                {
                    DrawEmpty(g);
                    return;
                }

                var size = Math.Min(plot.Width, plot.Height) - 16;
                if (size < 40) return;

                var box = new Rectangle(
                    plot.Left + ((plot.Width - size) / 2),
                    plot.Top + ((plot.Height - size) / 2),
                    size, size);

                var start = -90f;

                for (var i = 0; i < slices.Count; i++)
                {
                    var sweep = (float)((double)(slices[i].Value / total) * 360.0);

                    using var brush = new SolidBrush(ChartPalette.At(i));

                    // A 1° gap of surface between slices instead of an outline - the same
                    // separation, without adding a third colour to every edge.
                    g.FillPie(brush, box, start, Math.Max(0.5f, sweep - 1f));

                    start += sweep;
                }

                if (definition.ChartType == ChartTypes.Donut)
                {
                    var holeSize = (int)(size * 0.56);
                    var hole = new Rectangle(
                        box.Left + ((size - holeSize) / 2),
                        box.Top + ((size - holeSize) / 2),
                        holeSize, holeSize);

                    using var surface = new SolidBrush(UiTheme.Surface);
                    g.FillEllipse(surface, hole);

                    // The total in the hole: the one number a part-to-whole chart is usually
                    // being read for, and the only place it can go without a label per slice.
                    var text = FormatValue(total, definition.ValueFormat);

                    TextRenderer.DrawText(g, text, UiTheme.CardValue, hole,
                        UiTheme.TextPrimary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }

            /// <summary>
            /// At most six slices, with the tail folded into "Other".
            ///
            /// Past six a pie stops being readable at a glance, which is the only thing it is
            /// good for - and past eight there is no ninth colour that stays distinguishable
            /// under colour-blind vision.
            /// </summary>
            private static List<(string Label, decimal Value)> Folded(
                ChartDefinitionDto definition, ChartSeriesDto series)
            {
                var pairs = new List<(string Label, decimal Value)>();

                for (var i = 0; i < series.Values.Count; i++)
                {
                    var label = i < definition.Labels.Count ? definition.Labels[i] : $"#{i + 1}";
                    if (series.Values[i] <= 0m) continue;

                    pairs.Add((label, series.Values[i]));
                }

                pairs = pairs.OrderByDescending(p => p.Value).ToList();

                if (pairs.Count <= 6) return pairs;

                var head = pairs.Take(5).ToList();
                head.Add(("Other", pairs.Skip(5).Sum(p => p.Value)));

                return head;
            }

            // ------------------------------------------------------------ legend

            private void DrawLegend(Graphics g, ChartDefinitionDto definition)
            {
                var entries = IsSliceChart(definition)
                    ? Folded(definition, definition.Series[0]).Select(s => s.Label).ToList()
                    : definition.Series.Select(s => s.Name).ToList();

                var y = Height - LegendHeight + 6;
                var x = 8;

                for (var i = 0; i < entries.Count && i < ChartPalette.MaxSlots; i++)
                {
                    var size = TextRenderer.MeasureText(entries[i], UiTheme.Small);

                    if (x + size.Width + 26 > Width) break;

                    using (var brush = new SolidBrush(ChartPalette.At(i)))
                    {
                        g.FillEllipse(brush, x, y + 4, 9, 9);
                    }

                    // The label is drawn in ink, not in the series colour. Coloured text on a
                    // white surface is the part of this palette that fails contrast; the swatch
                    // beside it carries the identity instead.
                    TextRenderer.DrawText(g, entries[i], UiTheme.Small,
                        new Point(x + 14, y), UiTheme.TextSecondary);

                    x += size.Width + 28;
                }
            }

            // ------------------------------------------------------------ hover

            private (int Index, int Series) HitTest(ChartDefinitionDto definition, Point location)
            {
                var plot = PlotBounds(definition);

                if (IsSliceChart(definition)) return (-1, -1);
                if (!plot.Contains(location)) return (-1, -1);

                if (definition.ChartType == ChartTypes.HorizontalBar)
                {
                    var rows = Math.Min(definition.Labels.Count,
                        definition.Series.FirstOrDefault()?.Values.Count ?? 0);

                    if (rows == 0) return (-1, -1);

                    var slot = (float)plot.Height / rows;
                    var row = (int)((location.Y - plot.Top) / slot);

                    return (Math.Clamp(row, 0, rows - 1), 0);
                }

                var count = definition.Labels.Count;
                if (count == 0) return (-1, -1);

                var width = (float)plot.Width / count;
                var index = (int)((location.X - plot.Left) / width);

                return (Math.Clamp(index, 0, count - 1), -1);
            }

            /// <summary>
            /// A crosshair and a readout for the category under the pointer.
            ///
            /// Every series at that category, not just the nearest mark - the question a reader
            /// hovering over March is asking is "what happened in March", and answering with
            /// one line's value alone makes them hover three more times.
            /// </summary>
            private void DrawTooltip(Graphics g, ChartDefinitionDto definition, Rectangle plot)
            {
                if (_hoverIndex < 0 || _hoverIndex >= definition.Labels.Count) return;

                var label = definition.Labels[_hoverIndex];

                var lines = new List<(string Text, Color Swatch)> { (label, Color.Empty) };

                for (var s = 0; s < definition.Series.Count && s < ChartPalette.MaxSlots; s++)
                {
                    var series = definition.Series[s];
                    if (_hoverIndex >= series.Values.Count) continue;

                    var value = FormatValue(series.Values[_hoverIndex], definition.ValueFormat);

                    lines.Add((definition.Series.Count == 1
                        ? value
                        : $"{series.Name}   {value}", ChartPalette.At(s)));
                }

                if (lines.Count < 2) return;

                var width = lines.Max(l => TextRenderer.MeasureText(l.Text, UiTheme.Small).Width) + 34;
                var height = (lines.Count * 17) + 14;

                if (definition.ChartType is not ChartTypes.HorizontalBar)
                {
                    var slot = (float)plot.Width / Math.Max(1, definition.Labels.Count);
                    var centre = plot.Left + (slot * (_hoverIndex + 0.5f));

                    using var crosshair = new Pen(ChartPalette.Axis);
                    g.DrawLine(crosshair, centre, plot.Top, centre, plot.Bottom);
                }

                var box = new Rectangle(
                    Math.Min(Math.Max(8, PointToClient(MousePosition).X + 14), Width - width - 8),
                    Math.Max(8, PointToClient(MousePosition).Y - (height / 2)),
                    width, height);

                UiTheme.PaintCard(g, box, UiTheme.Surface, UiTheme.BorderStrong, 6);

                var y = box.Top + 7;

                foreach (var (text, swatch) in lines)
                {
                    if (swatch != Color.Empty)
                    {
                        using var brush = new SolidBrush(swatch);
                        g.FillEllipse(brush, box.Left + 10, y + 4, 8, 8);
                    }

                    TextRenderer.DrawText(g, text,
                        swatch == Color.Empty ? UiTheme.BodyStrong : UiTheme.Small,
                        new Point(box.Left + (swatch == Color.Empty ? 10 : 24), y),
                        swatch == Color.Empty ? UiTheme.TextPrimary : UiTheme.TextSecondary);

                    y += 17;
                }
            }
        }
    }
}
