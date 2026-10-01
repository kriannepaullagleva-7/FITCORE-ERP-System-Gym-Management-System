using System.Drawing;
using System.Drawing.Drawing2D;

namespace ERP_Project1
{
    /// <summary>
    /// The FitCore design tokens: one palette, one type scale, one spacing rhythm.
    ///
    /// Every screen reads from here rather than choosing its own colours, which is what makes
    /// the application look like a single ERP rather than a folder of forms. Nothing in this
    /// file draws anything - the controls that use these tokens live in <see cref="UiKit"/>.
    /// </summary>
    internal static class UiTheme
    {
        // ---------------------------------------------------------------- brand blues

        /// <summary>Primary action colour. Buttons, active navigation, focus.</summary>
        public static readonly Color Primary = Color.FromArgb(29, 78, 184);
        public static readonly Color PrimaryDark = Color.FromArgb(21, 58, 142);
        public static readonly Color PrimaryHover = Color.FromArgb(37, 93, 210);
        public static readonly Color PrimarySoft = Color.FromArgb(234, 241, 253);
        public static readonly Color PaleBlue = Color.FromArgb(234, 241, 253);
        public static readonly Color TextBlue = Color.FromArgb(21, 58, 142);

        /// <summary>The sidebar runs darker than the primary so the content reads as the subject.</summary>
        public static readonly Color Navy = Color.FromArgb(16, 35, 68);
        public static readonly Color NavyRaised = Color.FromArgb(24, 49, 91);
        public static readonly Color NavyActive = Color.FromArgb(29, 78, 184);

        // ---------------------------------------------------------------- surfaces

        /// <summary>The page behind the cards.</summary>
        public static readonly Color Canvas = Color.FromArgb(244, 247, 252);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceAlt = Color.FromArgb(249, 251, 254);
        public static readonly Color Border = Color.FromArgb(223, 230, 240);
        public static readonly Color BorderStrong = Color.FromArgb(203, 214, 229);

        // ---------------------------------------------------------------- text

        public static readonly Color TextPrimary = Color.FromArgb(21, 32, 48);
        public static readonly Color TextSecondary = Color.FromArgb(88, 105, 129);
        public static readonly Color TextMuted = Color.FromArgb(129, 145, 167);
        public static readonly Color TextOnDark = Color.FromArgb(225, 234, 249);
        public static readonly Color TextOnDarkMuted = Color.FromArgb(140, 166, 208);

        // ---------------------------------------------------------------- status

        public static readonly Color Success = Color.FromArgb(22, 137, 90);
        public static readonly Color SuccessSoft = Color.FromArgb(226, 246, 237);
        public static readonly Color Warning = Color.FromArgb(180, 108, 9);
        public static readonly Color WarningSoft = Color.FromArgb(253, 243, 224);
        public static readonly Color Danger = Color.FromArgb(190, 45, 45);
        public static readonly Color DangerSoft = Color.FromArgb(253, 235, 234);
        public static readonly Color Info = Color.FromArgb(29, 78, 184);
        public static readonly Color InfoSoft = Color.FromArgb(234, 241, 253);
        public static readonly Color Neutral = Color.FromArgb(96, 113, 134);
        public static readonly Color NeutralSoft = Color.FromArgb(238, 242, 247);

        // ---------------------------------------------------------------- type scale

        public const string Family = "Segoe UI";
        public const string FamilySemibold = "Segoe UI Semibold";

        public static readonly Font PageTitle = new(FamilySemibold, 16.5F);
        public static readonly Font SectionTitle = new(FamilySemibold, 11F);
        public static readonly Font CardValue = new(FamilySemibold, 19F);
        public static readonly Font Body = new(Family, 9.25F);
        public static readonly Font BodyStrong = new(FamilySemibold, 9.25F);
        public static readonly Font Small = new(Family, 8.5F);
        public static readonly Font Label = new(FamilySemibold, 8.5F);
        public static readonly Font Overline = new(FamilySemibold, 7.5F);
        public static readonly Font Numeric = new("Segoe UI", 9.25F);

        // ---------------------------------------------------------------- spacing

        public const int SpaceXs = 4;
        public const int SpaceS = 8;
        public const int SpaceM = 14;
        public const int SpaceL = 20;
        public const int SpaceXl = 28;

        public const int CornerRadius = 8;
        public const int ControlHeight = 34;

        // ---------------------------------------------------------------- drawing helpers

        /// <summary>A rounded rectangle path, used for cards, buttons, badges and inputs.</summary>
        public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();

            if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            var d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }

        /// <summary>Fills and outlines a rounded surface. The standard card treatment.</summary>
        public static void PaintCard(Graphics g, Rectangle bounds, Color fill, Color? border = null,
                                     int radius = CornerRadius)
        {
            if (bounds.Width <= 1 || bounds.Height <= 1) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            using var path = RoundedPath(r, radius);
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);

            if (border is not null)
            {
                using var pen = new Pen(border.Value);
                g.DrawPath(pen, path);
            }
        }

        /// <summary>Rounds a control's own outline, so borders and corners agree.</summary>
        public static void RoundControl(Control control, int radius = CornerRadius)
        {
            void Apply()
            {
                if (control.Width <= 0 || control.Height <= 0) return;

                using var path = RoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius);
                control.Region?.Dispose();
                control.Region = new Region(path);
            }

            control.Resize += (_, _) => Apply();
            Apply();
        }

        /// <summary>The soft/strong colour pair a status word should be drawn in.</summary>
        public static (Color Fore, Color Back) StatusColours(string? status)
        {
            var value = (status ?? "").Trim().ToLowerInvariant();

            return value switch
            {
                "active" or "completed" or "paid" or "in stock" or "approved" or "true"
                    => (Success, SuccessSoft),

                "pending" or "low stock" or "partially paid" or "draft" or "expiring soon" or "expiring"
                    => (Warning, WarningSoft),

                "inactive" or "cancelled" or "canceled" or "expired" or "failed" or "out of stock"
                or "unpaid" or "suspended" or "terminated" or "refunded" or "voided" or "false"
                    => (Danger, DangerSoft),

                "archived" or "none" or "general" or "" => (Neutral, NeutralSoft),

                // Payment categories. Coloured so a financial history can be scanned by what
                // the money was for, not only read line by line.
                "membership" => (Info, InfoSoft),
                "sales" => (Success, SuccessSoft),

                // Stock movement direction. "in"/"out"/"adjustment" match none of the words
                // above, so without this every movement painted the same neutral pill - the
                // direction of a stock change should read at a glance, not require the word to
                // be read.
                "in" => (Success, SuccessSoft),
                "out" => (Danger, DangerSoft),
                "adjustment" or "adjust" => (Warning, WarningSoft),
                "return" => (Warning, WarningSoft),

                _ => ReferenceColours(value.ToUpperInvariant())
            };
        }

        /// <summary>
        /// A reference number ("PO-000123", "SALE-45", "RET-000012-CANCEL") has too many
        /// distinct values for an exact-match table, but the prefix - what kind of document it
        /// is - is exactly the thing worth telling apart at a glance. Checked by substring
        /// rather than position, since a few references carry their qualifier as a suffix
        /// instead (a cancelled return keeps RET as its prefix and CANCEL as its suffix).
        /// </summary>
        private static (Color Fore, Color Back) ReferenceColours(string upper) => upper switch
        {
            _ when upper.Contains("CANCEL") => (Danger, DangerSoft),
            _ when upper.StartsWith("PO") => (Info, InfoSoft),
            _ when upper.StartsWith("SALE") => (Success, SuccessSoft),
            _ when upper.StartsWith("SUB") => (Info, InfoSoft),
            _ when upper.StartsWith("RET") => (Warning, WarningSoft),
            _ when upper.StartsWith("ADJUST") => (Warning, WarningSoft),
            _ => (Info, InfoSoft)
        };
    }
}
