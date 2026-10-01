namespace ERP_Project1.Api
{
    /// <summary>How a KPI should be formatted. Mirrors the server's <c>KpiFormats</c>.</summary>
    public static class KpiFormats
    {
        public const string Number = "number";
        public const string Money = "money";
        public const string Percent = "percent";
        public const string Decimal = "decimal";
    }

    /// <summary>
    /// Chart shapes the desktop can draw. Mirrors the server's <c>ChartTypes</c>; anything the
    /// client does not recognise falls back to a bar chart rather than drawing nothing.
    /// </summary>
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

    public class KpiCardDto
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public decimal Value { get; set; }
        public string Format { get; set; } = KpiFormats.Number;
        public decimal PreviousValue { get; set; }
        public decimal Delta { get; set; }
        public decimal DeltaPercent { get; set; }
        public bool HasComparison { get; set; }
        public bool RiseIsGood { get; set; } = true;
        public bool IsFavourable { get; set; }
        public string Hint { get; set; } = "";

        /// <summary>The figure as it should read on the card.</summary>
        public string Display => Format switch
        {
            KpiFormats.Money => Value.ToString("N2"),
            KpiFormats.Percent => Value.ToString("N1") + "%",
            KpiFormats.Decimal => Value.ToString("N2"),
            _ => Value.ToString("N0")
        };

        /// <summary>"+12.4%" against the previous period, or blank when there is nothing to compare.</summary>
        public string Movement => !HasComparison
            ? ""
            : (DeltaPercent >= 0 ? "+" : "") + DeltaPercent.ToString("N1") + "%";
    }

    public class KpiGroupDto
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public List<KpiCardDto> Cards { get; set; } = new();
    }

    public class ChartSeriesDto
    {
        public string Name { get; set; } = "";
        public List<decimal> Values { get; set; } = new();
    }

    public class ChartDefinitionDto
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string ChartType { get; set; } = ChartTypes.Line;
        public string ValueFormat { get; set; } = KpiFormats.Money;
        public List<string> Labels { get; set; } = new();
        public List<ChartSeriesDto> Series { get; set; } = new();
        public string Caption { get; set; } = "";
    }

    public class AnalyticsTableDto
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string> Columns { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
    }

    /// <summary>
    /// One analytics screen as the server describes it: its KPIs, its charts and the rows
    /// behind them.
    ///
    /// The desktop has a single screen that renders this shape, so adding an analytics area is
    /// a method on the server rather than another form here - and every figure on every chart
    /// is computed once, on the side that can see the database.
    /// </summary>
    public class AnalyticsViewDto
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public DateTime ComparisonFromUtc { get; set; }
        public DateTime ComparisonToUtc { get; set; }
        public List<KpiGroupDto> Groups { get; set; } = new();
        public List<ChartDefinitionDto> Charts { get; set; } = new();
        public List<AnalyticsTableDto> Tables { get; set; } = new();
        public DateTime GeneratedAtUtc { get; set; }
    }
}
