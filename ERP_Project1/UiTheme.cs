using System.Drawing;

namespace ERP_Project1
{
    // Single source of truth for the FitCore palette and the control factories every
    // module form uses, so the screens stay visually consistent.
    internal static class UiTheme
    {
        public static readonly Color Primary = Color.FromArgb(77, 130, 222);
        public static readonly Color PrimaryDark = Color.FromArgb(51, 109, 217);
        public static readonly Color PaleBlue = Color.FromArgb(230, 237, 251);
        public static readonly Color Canvas = Color.FromArgb(242, 247, 252);
        public static readonly Color TextBlue = Color.FromArgb(0, 79, 216);

        public static readonly Color Success = Color.FromArgb(76, 175, 80);
        public static readonly Color Warning = Color.FromArgb(255, 152, 0);
        public static readonly Color Danger = Color.FromArgb(244, 67, 54);
        public static readonly Color Neutral = Color.FromArgb(96, 125, 139);

        public static Button CreateButton(string text, int x, int y, Color backColor, EventHandler handler, int width = 110, int height = 32)
        {
            var button = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = backColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += handler;
            return button;
        }

        public static Label CreateLabel(string text, int x, int y, int width = 110, int height = 20)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(55, 71, 79),
                UseMnemonic = false
            };
        }

        public static Label CreateHeading(string text, int x, int y, int width = 300)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 26),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = TextBlue,
                UseMnemonic = false
            };
        }

        public static DataGridView CreateGrid(int x, int y, int width, int height)
        {
            var grid = new DataGridView
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                AutoGenerateColumns = true
            };

            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Primary;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 32;
            grid.AlternatingRowsDefaultCellStyle.BackColor = PaleBlue;
            grid.DefaultCellStyle.SelectionBackColor = PrimaryDark;
            grid.DefaultCellStyle.SelectionForeColor = Color.White;

            return grid;
        }

        // Module forms are docked into the shell, so their controls must be laid out by
        // docking rather than anchors - anchor offsets are measured against the default
        // 300x300 form size and would stretch the grids far past the visible area.
        public static Panel CreateHeaderPanel(int height)
        {
            return new Panel
            {
                Dock = DockStyle.Top,
                Height = height,
                BackColor = Canvas
            };
        }

        public static Panel CreateBodyPanel()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Canvas,
                Padding = new Padding(16, 0, 16, 16)
            };
        }

        // Shows a plain informational message. Every module now reads and writes the tenant
        // database, so this is used for confirmations rather than to explain a preview.
        public static void ShowInfo(string caption, string message)
        {
            MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static SplitContainer CreateSplit(double leftRatio)
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 8,
                BackColor = Canvas
            };

            // A SplitContainer rejects panel sizes that do not fit its current width, and
            // while it is unparented that width is still the 150px default - setting a
            // minimum size there drags SplitterDistance out of range and throws. Everything
            // that depends on the real width is applied on the first layout pass instead.
            var positioned = false;

            split.SizeChanged += (_, _) =>
            {
                if (positioned) return;

                var usable = split.Width - split.SplitterWidth;
                if (usable < 420) return;

                split.Panel1MinSize = 200;
                split.Panel2MinSize = 180;
                split.SplitterDistance = Math.Clamp((int)(usable * leftRatio), 200, usable - 180);

                positioned = true;
            };

            return split;
        }
    }
}
