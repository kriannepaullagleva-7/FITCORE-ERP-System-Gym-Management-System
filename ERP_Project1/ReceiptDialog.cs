using System.Drawing;
using System.Drawing.Printing;
using System.Text;

namespace ERP_Project1
{
    /// <summary>
    /// The one receipt window in FitCore.
    ///
    /// Every money transaction - a membership, a sale, a payment, a pay run - renders through
    /// here from the same <see cref="ReceiptModel"/>, which is what makes them look like
    /// documents from one system rather than four screens that each grew their own.
    ///
    /// The layout is a document on a desk: a white sheet on the canvas, the gym's name at the
    /// top, the party and the facts, the priced lines, then the totals against the right edge
    /// where money belongs.
    /// </summary>
    internal sealed class ReceiptDialog : Form
    {
        private const int SheetWidth = 560;

        private readonly ReceiptModel _model;
        private readonly Panel _sheet;


        private ReceiptDialog(ReceiptModel model)
        {
            _model = model;

            Text = $"{model.Title} — {model.DocumentNo}";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = UiTheme.Canvas;
            ClientSize = new Size(SheetWidth + 80, 720);
            MinimumSize = new Size(SheetWidth + 96, 480);

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(UiTheme.SpaceL, UiTheme.SpaceL, UiTheme.SpaceL, UiTheme.SpaceL)
            };

            _sheet = BuildSheet();
            scroll.Controls.Add(_sheet);

            Controls.Add(scroll);
            Controls.Add(BuildFooter());
        }

        private Color Accent => UiTheme.StatusColours(_model.TransactionType).Fore;

        // ------------------------------------------------------------------ sheet

        private Panel BuildSheet()
        {
            var sheet = new Panel
            {
                Width = SheetWidth,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(28, 24, 28, 24)
            };
            sheet.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, sheet.Width, sheet.Height),
                UiTheme.Surface, UiTheme.Border);

            // Positioned rather than docked. A Dock=Top child inside an AutoSize panel has no
            // width to measure against until the panel has one, and the panel has none until
            // the child is measured - the sheet collapses to a sliver.
            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Location = new Point(28, 24),
                BackColor = Color.Transparent
            };

            var inner = SheetWidth - 56;

            AddBrand(stack, inner);
            AddTitleBlock(stack, inner);
            AddParty(stack, inner);
            AddFacts(stack, inner);

            if (_model.Lines.Count > 0) AddLines(stack, inner);
            if (_model.Totals.Count > 0) AddTotals(stack, inner);
            if (_model.AmountPaid.HasValue) AddCashSummary(stack, inner);

            AddFooterNote(stack, inner);

            sheet.Controls.Add(stack);
            return sheet;
        }

        private static Label Para(string text, Font font, Color colour, int width,
                                  ContentAlignment align = ContentAlignment.MiddleLeft) => new()
        {
            Text = text,
            Font = font,
            ForeColor = colour,
            AutoSize = false,
            Width = width,
            Height = Math.Max(18, TextRenderer.MeasureText(
                text, font, new Size(width, 0), TextFormatFlags.WordBreak).Height + 2),
            TextAlign = align,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            UseMnemonic = false
        };

        private void AddBrand(Control parent, int width)
        {
            parent.Controls.Add(Para("FitCore", new Font(UiTheme.FamilySemibold, 17F),
                UiTheme.TextPrimary, width));

            parent.Controls.Add(Para(_model.CompanyName, UiTheme.Body, UiTheme.TextSecondary, width));

            if (!string.IsNullOrWhiteSpace(_model.TierName))
            {
                parent.Controls.Add(Para(_model.TierName, UiTheme.Small, UiTheme.TextMuted, width));
            }

            parent.Controls.Add(Rule(width, 16, 14));
        }

        private void AddTitleBlock(Control parent, int width)
        {
            var row = new Panel
            {
                Width = width,
                Height = 52,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 14)
            };

            row.Controls.Add(new Label
            {
                Text = _model.Title,
                Font = new Font(UiTheme.FamilySemibold, 13F),
                ForeColor = Accent,
                AutoSize = true,
                Location = new Point(0, 0),
                BackColor = Color.Transparent,
                UseMnemonic = false
            });

            row.Controls.Add(new Label
            {
                Text = _model.DocumentNo,
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Location = new Point(1, 24),
                BackColor = Color.Transparent,
                UseMnemonic = false
            });

            // Issued date and status hug the right edge, the way a document header reads.
            var issued = new Label
            {
                Text = _model.IssuedAt.ToString("d MMM yyyy  HH:mm"),
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextSecondary,
                AutoSize = false,
                Width = 220,
                Height = 20,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(width - 220, 2),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            row.Controls.Add(issued);

            if (!string.IsNullOrWhiteSpace(_model.Status))
            {
                var (fore, back) = UiTheme.StatusColours(_model.Status);
                var size = TextRenderer.MeasureText(_model.Status, UiTheme.Overline);

                var badge = new Label
                {
                    Text = _model.Status,
                    Font = UiTheme.Overline,
                    AutoSize = false,
                    Size = new Size(size.Width + 22, 22),
                    Location = new Point(width - size.Width - 22, 26),
                    BackColor = Color.Transparent,
                    UseMnemonic = false
                };
                badge.Paint += (_, e) =>
                {
                    UiTheme.PaintCard(e.Graphics, new Rectangle(0, 0, badge.Width, badge.Height),
                        back, null, 11);
                    TextRenderer.DrawText(e.Graphics, badge.Text, badge.Font,
                        new Rectangle(0, 0, badge.Width, badge.Height), fore,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                row.Controls.Add(badge);
            }

            parent.Controls.Add(row);
        }

        private void AddParty(Control parent, int width)
        {
            parent.Controls.Add(Para(_model.PartyLabel.ToUpperInvariant(),
                UiTheme.Overline, UiTheme.TextMuted, width));

            parent.Controls.Add(Para(_model.PartyName,
                new Font(UiTheme.FamilySemibold, 11F), UiTheme.TextPrimary, width));

            parent.Controls.Add(Rule(width, 14, 12));
        }

        private void AddFacts(Control parent, int width)
        {
            var facts = _model.Facts.Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToList();
            if (facts.Count == 0) return;

            var grid = new TableLayoutPanel
            {
                Width = width,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 8)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            foreach (var fact in facts)
            {
                grid.Controls.Add(Para(fact.Label, UiTheme.Small, UiTheme.TextMuted, 150));
                grid.Controls.Add(Para(fact.Value, UiTheme.Body, UiTheme.TextPrimary, width - 150));
            }

            parent.Controls.Add(grid);
        }

        /// <summary>Column widths sized to what the content actually needs - a six-figure
        /// amount at most - rather than a round number, so Description gets back the space the
        /// numeric columns were not using.</summary>
        private const int QtyColumnWidth = 42;
        private const int PriceColumnWidth = 70;
        private const int AmountColumnWidth = 76;

        private void AddLines(Control parent, int width)
        {
            parent.Controls.Add(Para("DETAILS", UiTheme.Overline, UiTheme.TextMuted, width));
            parent.Controls.Add(Rule(width, 6, 6));

            var grid = new TableLayoutPanel
            {
                Width = width,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 10)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, QtyColumnWidth));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, PriceColumnWidth));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AmountColumnWidth));

            var descWidth = width - QtyColumnWidth - PriceColumnWidth - AmountColumnWidth;

            // Header row, so the numbers underneath are read as quantity/price/amount.
            grid.Controls.Add(Para("Description", UiTheme.Overline, UiTheme.TextMuted, descWidth));
            grid.Controls.Add(Para("Qty", UiTheme.Overline, UiTheme.TextMuted, QtyColumnWidth, ContentAlignment.MiddleRight));
            grid.Controls.Add(Para("Price", UiTheme.Overline, UiTheme.TextMuted, PriceColumnWidth, ContentAlignment.MiddleRight));
            grid.Controls.Add(Para("Amount", UiTheme.Overline, UiTheme.TextMuted, AmountColumnWidth, ContentAlignment.MiddleRight));

            foreach (var line in _model.Lines)
            {
                grid.Controls.Add(Para(line.Description, UiTheme.Body, UiTheme.TextPrimary, descWidth));

                grid.Controls.Add(Para(line.Quantity is null ? "" : $"{line.Quantity:0.##}",
                    UiTheme.Body, UiTheme.TextSecondary, QtyColumnWidth, ContentAlignment.MiddleRight));

                grid.Controls.Add(Para(line.UnitPrice is null ? "" : $"{line.UnitPrice:N2}",
                    UiTheme.Body, UiTheme.TextSecondary, PriceColumnWidth, ContentAlignment.MiddleRight));

                grid.Controls.Add(Para($"{line.Amount:N2}",
                    UiTheme.Body, UiTheme.TextPrimary, AmountColumnWidth, ContentAlignment.MiddleRight));
            }

            parent.Controls.Add(grid);
        }

        private void AddTotals(Control parent, int width)
        {
            parent.Controls.Add(Rule(width, 4, 10));

            var grid = new TableLayoutPanel
            {
                Width = width,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 8)
            };

            // Totals sit against the right edge; the left column is empty space.
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));

            foreach (var total in _model.Totals)
            {
                var labelFont = total.Emphasise ? new Font(UiTheme.FamilySemibold, 11F) : UiTheme.Body;
                var valueFont = total.Emphasise ? new Font(UiTheme.FamilySemibold, 13F) : UiTheme.Body;
                var colour = total.Emphasise ? Accent : UiTheme.TextSecondary;

                grid.Controls.Add(Para(total.Label, labelFont, colour, width - 170,
                    ContentAlignment.MiddleRight));

                grid.Controls.Add(Para($"{total.Value:N2}", valueFont,
                    total.Emphasise ? Accent : UiTheme.TextPrimary, 170, ContentAlignment.MiddleRight));
            }

            parent.Controls.Add(grid);
        }

        /// <summary>
        /// Amount due / amount paid / change, set apart in its own highlighted box rather than
        /// folded into <see cref="AddTotals"/> - the same three figures in the same position on
        /// every receipt that took cash, so a cashier reconciling a drawer at the end of a shift
        /// reads them from the same place every time regardless of what kind of sale it was.
        /// </summary>
        private void AddCashSummary(Control parent, int width)
        {
            if (_model.AmountPaid is not decimal paid) return;

            var due = _model.AmountDue ?? paid;
            var change = _model.ChangeGiven ?? Math.Max(0m, paid - due);

            var box = new Panel
            {
                Width = width,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Padding = new Padding(14, 10, 14, 10),
                Margin = new Padding(0, 4, 0, 14)
            };
            box.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, box.Width, box.Height), UiTheme.SurfaceAlt, UiTheme.Border, 8);

            var grid = new TableLayoutPanel
            {
                Width = width - 28,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                BackColor = Color.Transparent,
                Location = new Point(14, 10)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));

            void Row(string label, decimal value, bool emphasise)
            {
                var labelFont = emphasise ? new Font(UiTheme.FamilySemibold, 11F) : UiTheme.Body;
                var valueFont = emphasise ? new Font(UiTheme.FamilySemibold, 13F) : UiTheme.Body;
                var colour = emphasise ? Accent : UiTheme.TextSecondary;

                grid.Controls.Add(Para(label, labelFont, colour, width - 28 - 170,
                    ContentAlignment.MiddleRight));
                grid.Controls.Add(Para($"{value:N2}", valueFont,
                    emphasise ? Accent : UiTheme.TextPrimary, 170, ContentAlignment.MiddleRight));
            }

            Row("Amount due", due, false);
            Row("Amount paid", paid, false);
            Row("Change", change, true);

            box.Controls.Add(grid);
            parent.Controls.Add(box);
        }

        private void AddFooterNote(Control parent, int width)
        {
            if (string.IsNullOrWhiteSpace(_model.FooterNote)) return;

            parent.Controls.Add(Rule(width, 8, 10));
            parent.Controls.Add(Para(_model.FooterNote, UiTheme.Small, UiTheme.TextMuted, width));
        }

        private static Panel Rule(int width, int above, int below) => new()
        {
            Width = width,
            Height = 1,
            BackColor = UiTheme.Border,
            Margin = new Padding(0, above, 0, below)
        };

        // ------------------------------------------------------------------ footer

        private Panel BuildFooter()
        {
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 62,
                BackColor = UiTheme.SurfaceAlt
            };
            footer.Paint += (_, e) =>
            {
                using var pen = new Pen(UiTheme.Border);
                e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            var close = UiKit.Action("Close", ButtonTone.Primary, (_, _) => Close(), 110);
            var print = UiKit.Action("Print", ButtonTone.Secondary, (_, _) => Print(), 100);
            var copy = UiKit.Action("Copy", ButtonTone.Secondary, (_, _) => CopyToClipboard(), 100);

            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 14, 18, 0),
                BackColor = Color.Transparent
            };
            row.Controls.Add(close);
            row.Controls.Add(print);
            row.Controls.Add(copy);

            footer.Controls.Add(row);

            CancelButton = close;
            AcceptButton = close;
            return footer;
        }

        /// <summary>The receipt as plain text, for pasting into an email or a chat.</summary>
        private string AsText()
        {
            var text = new StringBuilder();

            text.AppendLine(_model.CompanyName);
            if (!string.IsNullOrWhiteSpace(_model.TierName)) text.AppendLine(_model.TierName);
            text.AppendLine();
            text.AppendLine($"{_model.Title}   {_model.DocumentNo}");
            text.AppendLine($"{_model.IssuedAt:d MMM yyyy HH:mm}   {_model.Status}");
            text.AppendLine();
            text.AppendLine($"{_model.PartyLabel}: {_model.PartyName}");

            foreach (var fact in _model.Facts.Where(f => !string.IsNullOrWhiteSpace(f.Value)))
            {
                text.AppendLine($"{fact.Label}: {fact.Value}");
            }

            if (_model.Lines.Count > 0)
            {
                text.AppendLine();
                foreach (var line in _model.Lines)
                {
                    var qty = line.Quantity is null ? "" : $" x{line.Quantity:0.##}";
                    text.AppendLine($"  {line.Description}{qty}   {line.Amount:N2}");
                }
            }

            text.AppendLine();
            foreach (var total in _model.Totals)
            {
                text.AppendLine($"  {total.Label,-20}{total.Value,12:N2}");
            }

            if (_model.AmountPaid is decimal paid)
            {
                var due = _model.AmountDue ?? paid;
                var change = _model.ChangeGiven ?? Math.Max(0m, paid - due);

                text.AppendLine();
                text.AppendLine($"  {"Amount due",-20}{due,12:N2}");
                text.AppendLine($"  {"Amount paid",-20}{paid,12:N2}");
                text.AppendLine($"  {"Change",-20}{change,12:N2}");
            }

            if (!string.IsNullOrWhiteSpace(_model.FooterNote))
            {
                text.AppendLine();
                text.AppendLine(_model.FooterNote);
            }

            return text.ToString();
        }

        private void CopyToClipboard()
        {
            try
            {
                Clipboard.SetText(AsText());
                UiKit.Info("Receipt copied to the clipboard.", "Copied");
            }
            catch (Exception)
            {
                // The clipboard belongs to the whole desktop and another application can hold
                // it open. Nothing is lost - the receipt is still on screen.
                UiKit.Error("The clipboard is in use by another application. Please try again.");
            }
        }

        /// <summary>
        /// Prints the plain-text rendering. Deliberately text rather than a redraw of the
        /// panel: a receipt printer is usually a narrow thermal device, and text is what it
        /// handles well.
        /// </summary>
        private void Print()
        {
            var lines = AsText().Replace("\r\n", "\n").Split('\n');
            var index = 0;

            using var document = new PrintDocument();
            document.DocumentName = $"{_model.Title} {_model.DocumentNo}";

            document.PrintPage += (_, e) =>
            {
                if (e.Graphics is null) return;

                using var font = new Font("Consolas", 9F);
                var y = (float)e.MarginBounds.Top;
                var lineHeight = font.GetHeight(e.Graphics);

                while (index < lines.Length && y + lineHeight < e.MarginBounds.Bottom)
                {
                    e.Graphics.DrawString(lines[index++], font, Brushes.Black,
                        e.MarginBounds.Left, y);
                    y += lineHeight;
                }

                e.HasMorePages = index < lines.Length;
            };

            try
            {
                using var preview = new PrintPreviewDialog
                {
                    Document = document,
                    Width = 900,
                    Height = 700,
                    StartPosition = FormStartPosition.CenterParent
                };

                preview.ShowDialog(this);
            }
            catch (Exception)
            {
                // No printer configured is the usual cause, and it must not take the
                // application down - the receipt is readable on screen regardless.
                UiKit.Error(
                    "FitCore could not open a print preview. Check that a printer is installed.",
                    "Printing unavailable");
            }
            finally
            {
                index = 0;
            }
        }

        /// <summary>Shows a receipt. The one way any transaction document is opened.</summary>
        public static void Show(IWin32Window owner, ReceiptModel model)
        {
            using var dialog = new ReceiptDialog(model);
            dialog.ShowDialog(owner);
        }
    }
}
