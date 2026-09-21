using System.ComponentModel;
using System.Drawing;

namespace ERP_Project1
{
    /// <summary>
    /// A read-only popup grid for detail views - a stock ledger, the lines on a sale, a
    /// member's payment history. The caller describes the columns; nothing here is editable.
    /// </summary>
    internal sealed class ListDialog : Form
    {
        public ListDialog(string title, System.Collections.IList rows, Action<DataGridView> configure,
                          string? footer = null)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 540);
            MinimumSize = new Size(700, 380);
            BackColor = Color.White;
            ShowInTaskbar = false;

            var heading = new Label
            {
                Text = "  " + title,
                Dock = DockStyle.Top,
                Height = 44,
                Font = new Font("Segoe UI Semibold", 12F),
                ForeColor = UiKit.HeaderText,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };

            var grid = UiKit.Grid();
            grid.AutoGenerateColumns = false;
            configure(grid);
            grid.DataSource = rows;

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.White };

            var count = new Label
            {
                Text = footer ?? $"  {rows.Count} row(s)",
                Dock = DockStyle.Left,
                Width = 420,
                Font = UiKit.SubtitleFont,
                ForeColor = UiKit.MutedText,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };

            var close = UiKit.QuietButton("Close", (_, _) => Close(), 100);
            close.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            close.Location = new Point(bottom.Width - 118, 10);
            bottom.Resize += (_, _) => close.Location = new Point(bottom.Width - 118, 10);

            bottom.Controls.Add(close);
            bottom.Controls.Add(count);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
            host.Controls.Add(grid);

            Controls.Add(host);
            Controls.Add(bottom);
            Controls.Add(heading);

            CancelButton = close;
        }
    }
}
