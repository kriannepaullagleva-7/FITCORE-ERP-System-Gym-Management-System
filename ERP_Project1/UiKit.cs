using System.Drawing;
using System.Drawing.Drawing2D;

namespace ERP_Project1
{
    /// <summary>What a button is for, which decides how it is drawn.</summary>
    internal enum ButtonTone { Primary, Secondary, Success, Warning, Danger, Ghost }

    /// <summary>
    /// The FitCore control library. Every screen builds itself from these, so spacing,
    /// corner radius, hover behaviour and type are decided once.
    ///
    /// The palette lives in <see cref="UiTheme"/>; this file only draws.
    /// </summary>
    internal static class UiKit
    {
        // Names kept from the original kit so every screen keeps compiling against them.
        public static readonly Color CardBorder = UiTheme.Border;
        public static readonly Color HeaderText = UiTheme.TextPrimary;
        public static readonly Color MutedText = UiTheme.TextSecondary;
        public static readonly Color GridAltRow = UiTheme.SurfaceAlt;

        public static readonly Font TitleFont = UiTheme.PageTitle;
        public static readonly Font SubtitleFont = UiTheme.Small;
        public static readonly Font CardValueFont = UiTheme.CardValue;
        public static readonly Font CardLabelFont = UiTheme.Overline;
        public static readonly Font BodyFont = UiTheme.Body;

        // ------------------------------------------------------------------ buttons

        /// <summary>
        /// A flat, rounded button with a real hover and pressed state, and a disabled state
        /// that reads as disabled rather than merely grey.
        /// </summary>
        public static Button Action(string text, ButtonTone tone, EventHandler? onClick = null,
                                    int width = 116, int height = UiTheme.ControlHeight)
        {
            var (back, fore, border) = Palette(tone);

            var button = new Button
            {
                Text = text,
                AutoSize = false,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                Font = UiTheme.BodyStrong,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, UiTheme.SpaceS, 0),
                BackColor = back,
                ForeColor = fore,
                UseVisualStyleBackColor = false,
                TabStop = true
            };

            button.FlatAppearance.BorderSize = border is null ? 0 : 1;
            if (border is not null) button.FlatAppearance.BorderColor = border.Value;
            button.FlatAppearance.MouseOverBackColor = Hover(tone, back);
            button.FlatAppearance.MouseDownBackColor = Pressed(tone, back);

            UiTheme.RoundControl(button, 6);

            button.EnabledChanged += (_, _) =>
            {
                button.BackColor = button.Enabled ? back : Blend(back, UiTheme.Canvas, 0.62f);
                button.ForeColor = button.Enabled ? fore : UiTheme.TextMuted;
            };

            if (onClick is not null) button.Click += onClick;
            return button;
        }

        private static (Color Back, Color Fore, Color? Border) Palette(ButtonTone tone) => tone switch
        {
            ButtonTone.Primary => (UiTheme.Primary, Color.White, null),
            ButtonTone.Success => (UiTheme.Success, Color.White, null),
            ButtonTone.Warning => (UiTheme.Warning, Color.White, null),
            ButtonTone.Danger => (UiTheme.Danger, Color.White, null),
            ButtonTone.Ghost => (UiTheme.Canvas, UiTheme.TextSecondary, null),
            _ => (UiTheme.Surface, UiTheme.TextPrimary, UiTheme.BorderStrong)
        };

        private static Color Hover(ButtonTone tone, Color back) => tone switch
        {
            ButtonTone.Secondary => UiTheme.PrimarySoft,
            ButtonTone.Ghost => UiTheme.PrimarySoft,
            _ => Blend(back, Color.White, 0.14f)
        };

        private static Color Pressed(ButtonTone tone, Color back) => tone switch
        {
            ButtonTone.Secondary or ButtonTone.Ghost => UiTheme.Border,
            _ => Blend(back, Color.Black, 0.12f)
        };

        public static Color Blend(Color a, Color b, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * amount),
                (int)(a.G + (b.G - a.G) * amount),
                (int)(a.B + (b.B - a.B) * amount));
        }

        // Kept for compatibility with the original kit: these now route to Action so the
        // whole application picks up the new treatment at once.
        public static Button Button(string text, Color back, EventHandler onClick, int width = 116)
        {
            var tone =
                back == UiTheme.Success ? ButtonTone.Success :
                back == UiTheme.Warning ? ButtonTone.Warning :
                back == UiTheme.Danger ? ButtonTone.Danger :
                ButtonTone.Primary;

            return Action(text, tone, onClick, width);
        }

        public static Button QuietButton(string text, EventHandler onClick, int width = 104) =>
            Action(text, ButtonTone.Secondary, onClick, width);

        // ------------------------------------------------------------------ cards

        /// <summary>A white rounded panel. The basic container for everything on a page.</summary>
        public static Panel Card(Padding? padding = null, Color? fill = null)
        {
            var card = new Panel
            {
                BackColor = UiTheme.Canvas,
                Padding = padding ?? new Padding(UiTheme.SpaceM)
            };

            var surface = fill ?? UiTheme.Surface;

            card.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, card.Width, card.Height), surface, UiTheme.Border);

            return card;
        }

        /// <summary>A dashboard statistic tile: label, big value, supporting hint, accent rule.</summary>
        public static Panel StatCard(string label, out Label valueLabel, out Label hintLabel,
                                     Color? accent = null)
        {
            var tone = accent ?? UiTheme.Primary;

            var card = new Panel
            {
                BackColor = UiTheme.Canvas,
                Margin = new Padding(0, 0, UiTheme.SpaceM, UiTheme.SpaceM),
                Size = new Size(218, 104),
                Padding = new Padding(16, 14, 16, 12)
            };

            card.Paint += (_, e) =>
            {
                var bounds = new Rectangle(0, 0, card.Width, card.Height);
                UiTheme.PaintCard(e.Graphics, bounds, UiTheme.Surface, UiTheme.Border);

                // A short accent rule rather than a full bar: it marks the card without
                // turning the row into a barcode.
                using var brush = new SolidBrush(tone);
                using var path = UiTheme.RoundedPath(new Rectangle(0, 14, 4, 34), 2);
                e.Graphics.FillPath(brush, path);
            };

            // The card's white surface is painted, not set as BackColor - the panel itself
            // stays canvas-coloured so the rounded corners blend. Child labels must therefore
            // be transparent, or each one shows as a grey block inside the white card.
            //
            // Layout: the caption sits upper-left, the value is pinned lower-right - not
            // simply stacked below the caption - so the card reads as a title over an empty
            // field with the number anchored to the opposite corner, per the FitCore KPI
            // layout convention.
            var caption = new Label
            {
                Text = label.ToUpperInvariant(),
                Font = UiTheme.Overline,
                ForeColor = UiTheme.TextMuted,
                BackColor = Color.Transparent,
                Dock = DockStyle.Top,
                Height = 18,
                TextAlign = ContentAlignment.TopLeft,
                UseMnemonic = false
            };

            hintLabel = new Label
            {
                Text = "",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                BackColor = Color.Transparent,
                Dock = DockStyle.Top,
                Height = 18,
                TextAlign = ContentAlignment.TopLeft,
                UseMnemonic = false
            };

            valueLabel = new Label
            {
                Text = "—",
                Font = UiTheme.CardValue,
                ForeColor = UiTheme.TextPrimary,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomRight,
                UseMnemonic = false
            };

            // Top-docked siblings stack topmost-last-added, so hint is added before caption to
            // land caption above it; the Fill value takes whatever vertical space remains and
            // anchors its text to that area's bottom-right corner.
            card.Controls.Add(hintLabel);
            card.Controls.Add(caption);
            card.Controls.Add(valueLabel);
            return card;
        }

        // ------------------------------------------------------------------ inputs

        /// <summary>A bordered, rounded host that gives a bare TextBox a modern outline.</summary>
        public static Panel InputShell(TextBox inner, int width, int height = UiTheme.ControlHeight,
                                       int leftPad = 10, int rightPad = 10)
        {
            var shell = new Panel
            {
                Width = width,
                Height = height,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, 0, UiTheme.SpaceS, 0)
            };

            var focused = false;

            shell.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, shell.Width, shell.Height),
                UiTheme.Surface, focused ? UiTheme.Primary : UiTheme.BorderStrong, 6);

            inner.BorderStyle = BorderStyle.None;
            inner.BackColor = UiTheme.Surface;
            inner.Font = UiTheme.Body;
            inner.Dock = DockStyle.Fill;

            // A borderless TextBox sits flush with the top of its host; nudging it down
            // centres the text inside the rounded outline. The left inset leaves room for a
            // glyph drawn by the caller, and the right inset for one too (the search box's
            // clear button).
            var pad = Math.Max(0, (height - inner.PreferredSize.Height) / 2);
            shell.Padding = new Padding(leftPad, pad, rightPad, 0);

            inner.GotFocus += (_, _) => { focused = true; shell.Invalidate(); };
            inner.LostFocus += (_, _) => { focused = false; shell.Invalidate(); };

            shell.Controls.Add(inner);
            return shell;
        }

        /// <summary>The search field used on every list screen.</summary>
        public static TextBox SearchBox(string placeholder)
        {
            return new TextBox
            {
                Font = UiTheme.Body,
                Width = 280,
                PlaceholderText = placeholder,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, UiTheme.SpaceS, 0)
            };
        }

        /// <summary>A search field wrapped in the rounded shell, with a magnifier glyph and,
        /// once there is something typed, a clear ("x") button.</summary>
        public static Panel SearchField(out TextBox box, string placeholder, int width = 300)
        {
            var inner = new TextBox { PlaceholderText = placeholder };
            box = inner;

            // The glyphs are painted rather than added as controls: a docked label would
            // compete with the filled TextBox for the same space and clip the placeholder.
            var shell = InputShell(inner, width, leftPad: 32, rightPad: 26);

            shell.Paint += (_, e) => TextRenderer.DrawText(
                e.Graphics, "\U0001F50D", new Font(UiTheme.Family, 9F),
                new Rectangle(9, 0, 20, shell.Height), UiTheme.TextMuted,
                TextFormatFlags.VerticalCenter);

            var clearArea = new Rectangle(width - 24, 0, 22, shell.Height);

            shell.Paint += (_, e) =>
            {
                if (inner.Text.Length == 0) return;

                TextRenderer.DrawText(e.Graphics, "✕", new Font(UiTheme.Family, 9F),
                    clearArea, UiTheme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            inner.TextChanged += (_, _) => shell.Invalidate();

            shell.MouseUp += (_, e) =>
            {
                if (inner.Text.Length > 0 && clearArea.Contains(e.Location)) inner.Clear();
            };

            return shell;
        }

        public static ComboBox Select(int width = 180)
        {
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = width,
                Height = UiTheme.ControlHeight,
                Font = UiTheme.Body,
                FlatStyle = FlatStyle.Flat,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, 0, UiTheme.SpaceS, 0)
            };
        }

        public static DateTimePicker DatePicker(DateTime? value = null, int width = 130)
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Width = width,
                Font = UiTheme.Body,
                Value = value ?? DateTime.Today,
                Margin = new Padding(0, 0, UiTheme.SpaceS, 0)
            };
        }

        /// <summary>A small bold caption sitting above an input or beside a filter.</summary>
        public static Label FieldLabel(string text, bool required = false) => new()
        {
            Text = required ? text + " *" : text,
            Font = UiTheme.Label,
            ForeColor = UiTheme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
            UseMnemonic = false
        };

        /// <summary>A caption aligned to sit next to a 34px-high filter control.</summary>
        public static Label FilterLabel(string text) => new()
        {
            Text = text,
            Font = UiTheme.Label,
            ForeColor = UiTheme.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 10, 6, 0),
            UseMnemonic = false
        };

        public static Label SectionHeading(string text) => new()
        {
            Text = text,
            Font = UiTheme.SectionTitle,
            ForeColor = UiTheme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, UiTheme.SpaceS),
            UseMnemonic = false
        };

        // ------------------------------------------------------------------ badges

        /// <summary>A status pill, coloured by what the status means.</summary>
        public static Label Badge(string text, Color? fore = null, Color? back = null)
        {
            var (f, b) = UiTheme.StatusColours(text);
            var foreColour = fore ?? f;
            var backColour = back ?? b;

            var badge = new Label
            {
                Text = "  " + text + "  ",
                Font = UiTheme.Overline,
                ForeColor = foreColour,
                BackColor = UiTheme.Canvas,
                AutoSize = true,
                Padding = new Padding(4, 5, 4, 5),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, 6, 0),
                UseMnemonic = false
            };

            badge.Paint += (_, e) =>
            {
                UiTheme.PaintCard(e.Graphics, new Rectangle(0, 0, badge.Width, badge.Height),
                    backColour, null, badge.Height / 2);

                TextRenderer.DrawText(e.Graphics, badge.Text, badge.Font,
                    new Rectangle(0, 0, badge.Width, badge.Height), foreColour,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            return badge;
        }

        // ------------------------------------------------------------------ grid

        /// <summary>The FitCore grid: read-only, full-row selection, generous rows.</summary>
        public static DataGridView Grid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = UiTheme.Surface,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false,
                GridColor = UiTheme.Border,
                Font = UiTheme.Body,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ScrollBars = ScrollBars.Both
            };

            grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextSecondary;
            grid.ColumnHeadersDefaultCellStyle.Font = UiTheme.Overline;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 8, 8, 8);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.TextSecondary;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 38;

            grid.RowTemplate.Height = 34;
            grid.DefaultCellStyle.BackColor = UiTheme.Surface;
            grid.DefaultCellStyle.ForeColor = UiTheme.TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = UiTheme.PrimarySoft;
            grid.DefaultCellStyle.SelectionForeColor = UiTheme.TextPrimary;
            grid.DefaultCellStyle.Padding = new Padding(10, 0, 8, 0);
            grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.SurfaceAlt;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = UiTheme.PrimarySoft;

            // Hovering a row is a strong hint that rows are clickable, which matters because
            // double-click opens the editor on most screens.
            var hovered = -1;
            grid.CellMouseEnter += (_, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
                if (hovered == e.RowIndex) return;
                ResetHover(grid, hovered);
                hovered = e.RowIndex;
                if (!grid.Rows[hovered].Selected)
                {
                    grid.Rows[hovered].DefaultCellStyle.BackColor = UiTheme.PrimarySoft;
                }
            };
            grid.MouseLeave += (_, _) => { ResetHover(grid, hovered); hovered = -1; };

            return grid;
        }

        private static void ResetHover(DataGridView grid, int row)
        {
            if (row < 0 || row >= grid.Rows.Count) return;
            grid.Rows[row].DefaultCellStyle.BackColor = row % 2 == 1
                ? UiTheme.SurfaceAlt
                : UiTheme.Surface;
        }

        /// <summary>
        /// Gives every column in a grid a floor it will not shrink below in
        /// <see cref="DataGridViewAutoSizeColumnsMode.Fill"/> mode: at least its own header,
        /// and a wider floor still for a column formatted as money. Applied to the grids that
        /// build their columns directly - the POS catalogue and cart, and the read-only
        /// detail dialogs - which is the same protection <c>CrudPageBase</c> gives every
        /// list screen automatically, kept in one place so a resize behaves the same way
        /// everywhere in FitCore rather than only on the screens that went through the base
        /// class.
        /// </summary>
        public static void SetMinimumColumnWidths(DataGridView grid)
        {
            var moneyFloor = TextRenderer.MeasureText("999,999.99", UiTheme.Body).Width + 20;

            foreach (DataGridViewColumn column in grid.Columns)
            {
                var headerFloor = TextRenderer.MeasureText(column.HeaderText, UiTheme.Overline).Width + 26;

                var isMoney = (column.DefaultCellStyle.Format ?? column.InheritedStyle?.Format ?? "")
                    .StartsWith("N", StringComparison.OrdinalIgnoreCase);

                column.MinimumWidth = Math.Max(52, isMoney ? Math.Max(headerFloor, moneyFloor) : headerFloor);
            }
        }

        /// <summary>
        /// Draws the named columns as status pills instead of plain words, so "Out of stock"
        /// and "Active" are distinguishable at a glance rather than by reading.
        /// </summary>
        public static void PaintStatusColumns(DataGridView grid, params string[] columnNames)
        {
            var targets = new HashSet<string>(columnNames, StringComparer.OrdinalIgnoreCase);

            grid.CellPainting += (_, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                if (!targets.Contains(grid.Columns[e.ColumnIndex].Name) &&
                    !targets.Contains(grid.Columns[e.ColumnIndex].DataPropertyName)) return;

                var text = e.FormattedValue?.ToString();
                if (string.IsNullOrWhiteSpace(text)) return;

                e.PaintBackground(e.CellBounds, true);

                var (fore, back) = UiTheme.StatusColours(text);

                var size = TextRenderer.MeasureText(text, UiTheme.Overline);
                var pill = new Rectangle(
                    e.CellBounds.X + 10,
                    e.CellBounds.Y + (e.CellBounds.Height - 21) / 2,
                    Math.Min(size.Width + 18, e.CellBounds.Width - 16),
                    21);

                if (pill.Width > 8)
                {
                    UiTheme.PaintCard(e.Graphics!, pill, back, null, 10);
                    TextRenderer.DrawText(e.Graphics!, text, UiTheme.Overline, pill, fore,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis);
                }

                e.Handled = true;
            };
        }

        /// <summary>
        /// Makes auto-generated columns presentable: "QuantitySold" becomes "Quantity sold",
        /// numbers are right-aligned and money is formatted. Used by the report grids, whose
        /// shape changes with the report chosen, so their columns cannot be declared up front.
        /// </summary>
        public static void HumaniseColumns(DataGridView grid)
        {
            grid.DataBindingComplete += (_, _) =>
            {
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    column.HeaderText = Humanise(column.DataPropertyName ?? column.Name);

                    var type = Nullable.GetUnderlyingType(column.ValueType ?? typeof(string))
                               ?? column.ValueType ?? typeof(string);

                    if (type == typeof(decimal) || type == typeof(double))
                    {
                        column.DefaultCellStyle.Format = "N2";
                        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    }
                    else if (type == typeof(int) || type == typeof(long))
                    {
                        column.DefaultCellStyle.Format = "N0";
                        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    }
                    else if (type == typeof(DateTime))
                    {
                        column.DefaultCellStyle.Format = "d MMM yyyy";
                    }

                    // An identifier column tells the operator nothing in a report.
                    if (column.HeaderText.EndsWith(" id", StringComparison.OrdinalIgnoreCase))
                    {
                        column.Visible = false;
                    }
                }
            };
        }

        /// <summary>"QuantitySold" becomes "Quantity sold".</summary>
        private static string Humanise(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;

            var text = new System.Text.StringBuilder(name.Length + 6);

            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    text.Append(' ').Append(char.ToLowerInvariant(name[i]));
                }
                else
                {
                    text.Append(i == 0 ? char.ToUpperInvariant(name[i]) : name[i]);
                }
            }

            return text.ToString();
        }

        // ------------------------------------------------------------------ states

        /// <summary>
        /// The "nothing here" panel. Shown in place of an empty grid so a new tenant sees an
        /// invitation rather than a blank rectangle.
        /// </summary>
        public static Panel EmptyState(string headline, string detail, string? actionText = null,
                                       EventHandler? action = null, Color? headlineColor = null)
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };

            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.None,
                BackColor = UiTheme.Surface
            };

            stack.Controls.Add(new Label
            {
                Text = headline,
                Font = UiTheme.SectionTitle,
                ForeColor = headlineColor ?? UiTheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
                UseMnemonic = false
            });

            stack.Controls.Add(new Label
            {
                Text = detail,
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Margin = new Padding(0, 0, 0, 14),
                UseMnemonic = false
            });

            if (actionText is not null && action is not null)
            {
                var button = Action(actionText, ButtonTone.Primary, action, 160);
                button.Margin = new Padding(0);
                stack.Controls.Add(button);
            }

            host.Controls.Add(stack);
            host.Resize += (_, _) => Centre(stack, host);
            Centre(stack, host);

            return host;
        }

        private static void Centre(Control child, Control parent)
        {
            child.Location = new Point(
                Math.Max(0, (parent.Width - child.Width) / 2),
                Math.Max(0, (parent.Height - child.Height) / 2));
        }

        /// <summary>
        /// A translucent overlay with a spinner, laid over a panel while the server is
        /// working. Nothing underneath is clickable, which is what stops double submits.
        /// </summary>
        public sealed class BusyOverlay : Panel
        {
            private readonly System.Windows.Forms.Timer _timer;
            private readonly Label _caption;
            private int _angle;

            public BusyOverlay()
            {
                Dock = DockStyle.Fill;
                Visible = false;
                BackColor = Color.FromArgb(232, 244, 248, 253);
                DoubleBuffered = true;

                _caption = new Label
                {
                    Text = "Loading…",
                    Font = UiTheme.BodyStrong,
                    ForeColor = UiTheme.TextSecondary,
                    AutoSize = true,
                    BackColor = Color.Transparent,
                    UseMnemonic = false
                };
                Controls.Add(_caption);

                _timer = new System.Windows.Forms.Timer { Interval = 60 };
                _timer.Tick += (_, _) => { _angle = (_angle + 24) % 360; Invalidate(); };

                Resize += (_, _) => Reposition();
            }

            public void Start(string caption)
            {
                _caption.Text = caption;
                Reposition();
                Visible = true;
                BringToFront();
                _timer.Start();
            }

            public void Stop()
            {
                _timer.Stop();
                Visible = false;
            }

            private void Reposition() =>
                _caption.Location = new Point(
                    Math.Max(0, (Width - _caption.Width) / 2),
                    Math.Max(0, Height / 2 + 6));

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                var size = 30;
                var box = new Rectangle((Width - size) / 2, Height / 2 - size - 6, size, size);

                using var track = new Pen(UiTheme.Border, 3);
                e.Graphics.DrawEllipse(track, box);

                using var arc = new Pen(UiTheme.Primary, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                e.Graphics.DrawArc(arc, box, _angle, 100);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _timer.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Rejects a keystroke that could not possibly belong to a number: everything but
        /// digits, at most one decimal separator (skipped for whole-number fields), and a
        /// leading minus when the field is allowed to go negative. This is never the only
        /// defence - the text is still parsed and range-checked afterwards, same as before -
        /// it only stops "type a letter into an amount, find out on Save" before it starts.
        /// Shared by <see cref="EditDialog"/>'s Number/Money/Integer fields and the point-of-sale
        /// screen's own hand-built amount boxes, so both read the same rule.
        /// </summary>
        public static void AttachNumericFilter(TextBox box, bool allowDecimal = true,
                                               bool allowNegative = false)
        {
            // Read from the current culture rather than assuming "." and "-": the same figure is
            // parsed back with CultureInfo.CurrentCulture, so refusing the separator this machine
            // actually writes would make a valid amount impossible to type.
            var format = System.Globalization.CultureInfo.CurrentCulture.NumberFormat;
            var separator = format.NumberDecimalSeparator;
            var negativeSign = format.NegativeSign;

            box.KeyPress += (_, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                if (char.IsDigit(e.KeyChar)) return;

                var typed = e.KeyChar.ToString();

                if (allowDecimal && typed == separator && !box.Text.Contains(separator)) return;

                if (allowNegative && typed == negativeSign && box.SelectionStart == 0 &&
                    !box.Text.Contains(negativeSign))
                {
                    return;
                }

                e.Handled = true;
            };
        }

        /// <summary>
        /// Rejects a keystroke that could not belong to a phone number: digits, spaces,
        /// parentheses, a hyphen, and one leading plus for a country code - "0917-555-0142" and
        /// "+63 917 555 0142" both type cleanly. Deliberately looser than digits-only: real
        /// seeded numbers in this application already contain hyphens, and a filter strict
        /// enough to reject them would make existing data impossible to re-type. Only letters
        /// and other obviously-wrong characters are refused; nothing here enforces a specific
        /// phone format, and the server remains the authority on whether a number is valid.
        /// </summary>
        public static void AttachPhoneFilter(TextBox box)
        {
            box.KeyPress += (_, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                if (char.IsDigit(e.KeyChar)) return;
                if (e.KeyChar is ' ' or '-' or '(' or ')') return;

                if (e.KeyChar == '+' && box.SelectionStart == 0 && !box.Text.Contains('+')) return;

                e.Handled = true;
            };
        }

        // ------------------------------------------------------------------ formatting

        public static string Money(decimal value) => value.ToString("N2");

        public static string Date(DateTime value) => value.ToLocalTime().ToString("d MMM yyyy");

        public static string Plural(int count, string singular, string? plural = null) =>
            count == 1 ? $"{count:N0} {singular}" : $"{count:N0} {plural ?? singular + "s"}";

        // ------------------------------------------------------------------ messages

        /// <summary>
        /// A destructive-action confirmation. Takes the consequence as well as the question,
        /// because "Are you sure?" tells an operator nothing about what they are about to lose.
        /// </summary>
        public static bool ConfirmDelete(IWin32Window? owner, string question, string consequence,
                                         string confirmLabel = "Delete")
        {
            var body = string.IsNullOrWhiteSpace(consequence)
                ? question
                : $"{question}\r\n\r\n{consequence}";

            return ConfirmDialog.Show(owner, "Confirm", body, confirmLabel, "Cancel",
                ButtonTone.Danger) == DialogResult.Yes;
        }

        public static DialogResult Confirm(string message, string caption = "FitCore ERP",
                                           string confirmLabel = "Yes", string cancelLabel = "No") =>
            ConfirmDialog.Show(null, caption, message, confirmLabel, cancelLabel, ButtonTone.Primary);

        public static void Info(string message, string caption = "FitCore ERP") =>
            MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);

        public static void Success(string message, string caption = "Done") =>
            MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);

        public static void Error(string message, string caption = "FitCore ERP") =>
            MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
