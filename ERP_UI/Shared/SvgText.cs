using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace ERP_UI.Shared
{
    /// <summary>
    /// Razor treats &lt;text&gt; as its own control element, so an SVG text label written
    /// directly in a component is a compile error. The chart components build their labels
    /// through here instead and emit the result as markup.
    /// </summary>
    internal static class Svg
    {
        public static MarkupString Text(
            double x,
            double y,
            string content,
            string cssClass = "fc-chart-axis",
            string anchor = "middle",
            string? style = null)
        {
            var styleAttribute = string.IsNullOrWhiteSpace(style) ? "" : $" style=\"{style}\"";

            return new MarkupString(
                $"<text class=\"{cssClass}\" x=\"{N(x)}\" y=\"{N(y)}\" text-anchor=\"{anchor}\"{styleAttribute}>" +
                $"{Escape(content)}</text>");
        }

        public static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        public static string N(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>
        /// Labels come from product names and payment methods, which are user-entered, so
        /// they are escaped before being placed into markup.
        /// </summary>
        private static string Escape(string value) => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

        /// <summary>Axis labels are shortened so a large figure does not crowd out the plot.</summary>
        public static string Compact(decimal value) => value switch
        {
            >= 1_000_000 => (value / 1_000_000).ToString("0.#") + "M",
            >= 1_000 => (value / 1_000).ToString("0.#") + "k",
            _ => value.ToString("0.#")
        };

        /// <summary>
        /// Rounds a series maximum up to a readable ceiling, and never returns zero, which
        /// would make every point divide by nothing.
        /// </summary>
        public static decimal Ceiling(decimal peak)
        {
            if (peak <= 0) return 1m;

            var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)peak)));
            return Math.Ceiling(peak / magnitude) * magnitude;
        }
    }
}
