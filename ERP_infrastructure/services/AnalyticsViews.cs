namespace ERP_infrastructure.services
{
    /// <summary>How a figure should be read, so the client formats it without guessing.</summary>
    public static class KpiFormats
    {
        public const string Number = "number";
        public const string Money = "money";
        public const string Percent = "percent";
        public const string Decimal = "decimal";
    }

    /// <summary>
    /// One measured figure, with the context that makes it mean something.
    ///
    /// A number on its own is not a KPI. "₱48,200" answers nothing; "₱48,200, up 12% on the
    /// previous month, which is good" answers a question. Every card therefore carries the
    /// comparison and whether the direction of travel is welcome - because for revenue up is
    /// good and for outstanding debt it is not, and the client has no business deciding which.
    /// </summary>
    public class KpiCard
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";

        public decimal Value { get; set; }

        /// <summary>One of <see cref="KpiFormats"/>.</summary>
        public string Format { get; set; } = KpiFormats.Number;

        /// <summary>The same measure over the immediately preceding period of equal length.</summary>
        public decimal PreviousValue { get; set; }

        public decimal Delta => Value - PreviousValue;

        /// <summary>
        /// The change as a percentage of the previous period. Zero when there is nothing to
        /// compare against - growth from nothing is not "infinite", it is unmeasurable, and
        /// showing a percentage there is worse than showing none.
        /// </summary>
        public decimal DeltaPercent { get; set; }

        public bool HasComparison { get; set; }

        /// <summary>
        /// Whether the movement is welcome. Decided here because it is a property of the
        /// measure rather than of the number: more members is good, more days of overdue debt
        /// is not, and the difference cannot be inferred from the sign.
        /// </summary>
        public bool RiseIsGood { get; set; } = true;

        public bool IsFavourable => Delta == 0m || (Delta > 0m == RiseIsGood);

        /// <summary>A short line of context under the figure.</summary>
        public string Hint { get; set; } = "";
    }

    /// <summary>A related set of KPI cards, shown together under one heading.</summary>
    public class KpiGroup
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public List<KpiCard> Cards { get; set; } = new();
    }

    public static class ChartTypes
    {
        public const string Line = "line";
        public const string Area = "area";
        public const string Bar = "bar";
        public const string StackedBar = "stacked-bar";
        public const string Pie = "pie";
        public const string Donut = "donut";
        public const string HorizontalBar = "hbar";
    }

    /// <summary>One line or set of bars on a chart.</summary>
    public class ChartSeries
    {
        public string Name { get; set; } = "";
        public List<decimal> Values { get; set; } = new();
    }

    /// <summary>
    /// A chart, described rather than drawn.
    ///
    /// The server decides what the chart is - its shape, its categories, its numbers - and the
    /// client decides how it looks. That keeps every figure on every chart computed once, on
    /// the side that can see the database, and means a desktop chart and a printed report
    /// cannot disagree about what the month's revenue was.
    /// </summary>
    public class ChartDefinition
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>One of <see cref="ChartTypes"/>.</summary>
        public string ChartType { get; set; } = ChartTypes.Line;

        /// <summary>One of <see cref="KpiFormats"/>, for the axis and the tooltips.</summary>
        public string ValueFormat { get; set; } = KpiFormats.Money;

        /// <summary>The category axis: dates, months, product names, status names.</summary>
        public List<string> Labels { get; set; } = new();

        public List<ChartSeries> Series { get; set; } = new();

        /// <summary>A sentence under the title saying what the chart is for.</summary>
        public string Caption { get; set; } = "";
    }

    /// <summary>
    /// One analytics screen: its KPIs, its charts, and the window they cover.
    ///
    /// The same shape serves every area - membership, sales, finance, the platform - so the
    /// client has one screen implementation rather than nine, and adding an analytics area is
    /// a method on the server rather than a new form.
    /// </summary>
    public class AnalyticsView
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";

        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        /// <summary>The equal-length window immediately before this one, used for every delta.</summary>
        public DateTime ComparisonFromUtc { get; set; }
        public DateTime ComparisonToUtc { get; set; }

        public List<KpiGroup> Groups { get; set; } = new();
        public List<ChartDefinition> Charts { get; set; } = new();

        /// <summary>
        /// Rows behind the charts, so a screen can offer a drill-down and an export without a
        /// second round trip. Each table names its own columns.
        /// </summary>
        public List<AnalyticsTable> Tables { get; set; } = new();

        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>A named table of rows, for drill-down and export.</summary>
    public class AnalyticsTable
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string> Columns { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
    }
}
