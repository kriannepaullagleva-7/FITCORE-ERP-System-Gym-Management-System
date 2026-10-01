using System.Drawing;

namespace ERP_Project1
{
    /// <summary>
    /// The FitCore-styled replacement for a Yes/No <see cref="MessageBox"/>. Every confirmation
    /// and destructive-action prompt in the app goes through <see cref="UiKit.Confirm"/> or
    /// <see cref="UiKit.ConfirmDelete"/>, so fixing this once - a labelled, coloured pair of
    /// buttons instead of a generic Yes/No, with the destructive action never the default -
    /// reaches every one of them without a per-screen change.
    /// </summary>
    internal sealed class ConfirmDialog : Form
    {
        private ConfirmDialog(string caption, string message, string confirmLabel,
                              string cancelLabel, ButtonTone confirmTone)
        {
            Text = caption;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = UiTheme.Surface;
            Font = UiTheme.Body;
            Width = 440;

            const int contentWidth = 360;

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(24, 22, 24, 10),
                BackColor = UiTheme.Surface
            };

            body.Controls.Add(new Label
            {
                Text = message,
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                MaximumSize = new Size(contentWidth, 0),
                Margin = new Padding(0),
                UseMnemonic = false
            });

            Controls.Add(body);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = UiTheme.SurfaceAlt };
            footer.Paint += (_, e) =>
            {
                using var pen = new Pen(UiTheme.Border);
                e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            var confirmWidth = Math.Max(96,
                TextRenderer.MeasureText(confirmLabel, UiTheme.BodyStrong).Width + 36);
            var cancelWidth = Math.Max(88,
                TextRenderer.MeasureText(cancelLabel, UiTheme.BodyStrong).Width + 36);

            var confirmButton = UiKit.Action(confirmLabel, confirmTone,
                (_, _) => { DialogResult = DialogResult.Yes; Close(); }, confirmWidth);
            var cancelButton = UiKit.Action(cancelLabel, ButtonTone.Secondary,
                (_, _) => { DialogResult = DialogResult.No; Close(); }, cancelWidth);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 14, 18, 0)
            };
            buttons.Controls.Add(confirmButton);
            buttons.Controls.Add(cancelButton);
            footer.Controls.Add(buttons);

            Controls.Add(footer);

            // The confirming/destructive action is never what Enter activates and never where
            // focus starts - matching the safety property the native MessageBox this replaces
            // always had (MessageBoxDefaultButton.Button2, the "No"-equivalent button).
            AcceptButton = cancelButton;
            CancelButton = cancelButton;

            Height = body.PreferredSize.Height + footer.Height + 40;
        }

        public static DialogResult Show(IWin32Window? owner, string caption, string message,
                                        string confirmLabel, string cancelLabel, ButtonTone confirmTone)
        {
            using var dialog = new ConfirmDialog(caption, message, confirmLabel, cancelLabel, confirmTone);
            return dialog.ShowDialog(owner) == DialogResult.Yes ? DialogResult.Yes : DialogResult.No;
        }
    }
}
