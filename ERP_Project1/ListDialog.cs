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

            // Applied once, here, rather than by every caller: every detail grid in FitCore
            // - a stock ledger, a sale's lines, a pay history - goes through this dialog, so
            // fixing the floor in one place fixes it everywhere a column would otherwise be
            // squeezed past its own header on a narrow window.
            //
            // Deliberately after the data source is set rather than before: an explicitly
            // configured grid already has its columns at this point either way, but an
            // auto-generated one (Show(items), used by most of the detail popups in the app)
            // does not create its columns until the data actually binds - measured any earlier,
            // this floor was being computed against zero columns and silently doing nothing.
            UiKit.SetMinimumColumnWidths(grid);

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

        /// <summary>
        /// Shows a detail grid built from the shape of the rows themselves.
        ///
        /// The columns come from the row type's own properties, humanised - so a caller passes
        /// a list of anonymous objects and gets a readable grid, rather than writing a column
        /// definition per field for what is a throwaway popup. Where a screen needs control
        /// over the columns, the constructor taking a configure callback is still there.
        /// </summary>
        public static void Show(
            IWin32Window owner, string title, string? footer, System.Collections.IList rows)
        {
            using var dialog = new ListDialog(title, rows, grid =>
            {
                grid.AutoGenerateColumns = true;
                UiKit.HumaniseColumns(grid);
            }, footer);

            dialog.ShowDialog(owner);
        }

        private void InitializeComponent()
        {

        }
    }
}
