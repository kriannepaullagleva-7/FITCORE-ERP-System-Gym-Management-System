using System.Drawing;
using System.Drawing.Drawing2D;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The only way into FitCore. Credentials are posted to ERP_api, which verifies them
    /// against the master database with PBKDF2 and returns a signed token carrying the
    /// company and the permitted modules. No password is stored or cached here.
    ///
    /// Everything that can go wrong - empty fields, wrong password, a server that is not
    /// running - is answered with one sentence the operator can act on. No status codes, no
    /// exception text.
    /// </summary>
    internal sealed class LoginForm : Form
    {
        private readonly FitCoreSession _session;
        private readonly TextBox _username;
        private readonly TextBox _password;
        private readonly Button _signIn;
        private readonly Button _reveal;
        private readonly Panel _errorPanel;
        private readonly Label _errorText;
        private readonly Label _working;

        private bool _busy;

        public LoginForm(FitCoreSession session)
        {
            _session = session;

            Text = "FitCore ERP — Sign in";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(880, 520);
            BackColor = UiTheme.Surface;
            DoubleBuffered = true;

            Controls.Add(BuildFormPanel(out _username, out _password, out _signIn, out _reveal,
                                        out _errorPanel, out _errorText, out _working));
            Controls.Add(BuildBrandPanel());

            AcceptButton = _signIn;

            _username.KeyDown += async (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                if (string.IsNullOrWhiteSpace(_password.Text)) _password.Focus();
                else await AttemptAsync();
            };

            _password.KeyDown += async (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                await AttemptAsync();
            };

            Shown += (_, _) => _username.Focus();
        }

        // ------------------------------------------------------------------ left: brand

        private Panel BuildBrandPanel()
        {
            var brand = new Panel
            {
                Dock = DockStyle.Left,
                Width = 380,
                BackColor = UiTheme.Navy
            };

            brand.Paint += (_, e) =>
            {
                // A soft diagonal wash keeps the panel from reading as a flat block of navy.
                using var wash = new LinearGradientBrush(
                    new Rectangle(0, 0, brand.Width, brand.Height),
                    UiTheme.Navy, UiTheme.PrimaryDark, 55f);
                e.Graphics.FillRectangle(wash, 0, 0, brand.Width, brand.Height);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                // The FitCore mark: a rounded square with the initial, drawn rather than
                // shipped as an image so there is no asset to lose.
                var mark = new Rectangle(46, 74, 56, 56);
                using var markBrush = new SolidBrush(Color.FromArgb(56, 255, 255, 255));
                using var markPath = UiTheme.RoundedPath(mark, 14);
                e.Graphics.FillPath(markBrush, markPath);

                TextRenderer.DrawText(e.Graphics, "FC", new Font(UiTheme.FamilySemibold, 20F),
                    mark, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            brand.Controls.Add(new Label
            {
                Text = "FitCore",
                Font = new Font(UiTheme.FamilySemibold, 32F),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(44, 150),
                BackColor = Color.Transparent,
                UseMnemonic = false
            });

            brand.Controls.Add(new Label
            {
                Text = "ERP SYSTEM",
                Font = new Font(UiTheme.FamilySemibold, 10F),
                ForeColor = Color.FromArgb(178, 203, 244),
                AutoSize = true,
                Location = new Point(48, 218),
                BackColor = Color.Transparent,
                UseMnemonic = false
            });

            brand.Controls.Add(new Label
            {
                Text = "Gym Membership Management",
                Font = new Font(UiTheme.Family, 10.5F),
                ForeColor = Color.FromArgb(214, 229, 252),
                AutoSize = true,
                Location = new Point(48, 252),
                BackColor = Color.Transparent,
                UseMnemonic = false
            });

            var points = new[]
            {
                "Members, plans and subscriptions",
                "Point of sale, stock and suppliers",
                "Payments, employees and payroll"
            };

            var y = 312;

            foreach (var point in points)
            {
                brand.Controls.Add(new Label
                {
                    Text = "—   " + point,
                    Font = new Font(UiTheme.Family, 9.5F),
                    ForeColor = Color.FromArgb(186, 209, 247),
                    AutoSize = true,
                    Location = new Point(48, y),
                    BackColor = Color.Transparent,
                    UseMnemonic = false
                });
                y += 28;
            }

            return brand;
        }

        // ------------------------------------------------------------------ right: form

        private Panel BuildFormPanel(out TextBox username, out TextBox password, out Button signIn,
                                     out Button reveal, out Panel errorPanel, out Label errorText,
                                     out Label working)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };

            const int left = 56;
            const int fieldWidth = 388;

            panel.Controls.Add(new Label
            {
                Text = "Welcome back",
                Font = new Font(UiTheme.FamilySemibold, 19F),
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(left, 74),
                UseMnemonic = false
            });

            panel.Controls.Add(new Label
            {
                Text = "Sign in to your FitCore workspace.",
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Location = new Point(left + 2, 110),
                UseMnemonic = false
            });

            // ---- error banner ----
            errorPanel = new Panel
            {
                Location = new Point(left, 142),
                Size = new Size(fieldWidth, 44),
                Visible = false,
                BackColor = UiTheme.Surface,
                Padding = new Padding(12, 0, 12, 0)
            };
            var banner = errorPanel;
            banner.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, banner.Width, banner.Height),
                UiTheme.DangerSoft, UiKit.Blend(UiTheme.Danger, Color.White, 0.55f), 6);

            errorText = new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.Body,
                ForeColor = UiTheme.Danger,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };
            errorPanel.Controls.Add(errorText);
            panel.Controls.Add(errorPanel);

            // ---- username ----
            panel.Controls.Add(Caption("Username or email", left, 200));

            username = new TextBox
            {
                Location = new Point(left, 222),
                Width = fieldWidth,
                Height = UiTheme.ControlHeight,
                Font = new Font(UiTheme.Family, 10.5F),
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "e.g. admin"
            };
            panel.Controls.Add(username);

            // ---- password ----
            panel.Controls.Add(Caption("Password", left, 268));

            password = new TextBox
            {
                Location = new Point(left, 290),
                Width = fieldWidth - 86,
                Height = UiTheme.ControlHeight,
                Font = new Font(UiTheme.Family, 10.5F),
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = true,
                PlaceholderText = "Your password"
            };
            panel.Controls.Add(password);

            var passwordBox = password;
            reveal = UiKit.Action("Show", ButtonTone.Secondary, null, 80, 27);
            reveal.Location = new Point(left + fieldWidth - 80, 290);
            reveal.Font = UiTheme.Small;
            var revealButton = reveal;
            reveal.Click += (_, _) =>
            {
                passwordBox.UseSystemPasswordChar = !passwordBox.UseSystemPasswordChar;
                revealButton.Text = passwordBox.UseSystemPasswordChar ? "Show" : "Hide";
                passwordBox.Focus();
                passwordBox.SelectionStart = passwordBox.TextLength;
            };
            panel.Controls.Add(reveal);

            // ---- sign in ----
            signIn = UiKit.Action("Sign in", ButtonTone.Primary, null, fieldWidth, 44);
            signIn.Location = new Point(left, 346);
            signIn.Font = new Font(UiTheme.FamilySemibold, 10.5F);
            var button = signIn;
            signIn.Click += async (_, _) => await AttemptAsync();
            panel.Controls.Add(signIn);

            working = new Label
            {
                Location = new Point(left, 398),
                Size = new Size(fieldWidth, 20),
                Font = UiTheme.Small,
                ForeColor = UiTheme.Primary,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false,
                UseMnemonic = false
            };
            panel.Controls.Add(working);

            panel.Controls.Add(new Label
            {
                Location = new Point(left, 438),
                Size = new Size(fieldWidth, 34),
                Font = new Font(UiTheme.Family, 8F),
                ForeColor = UiTheme.TextMuted,
                Text = $"Server: {_session.ApiBaseUrl}",
                UseMnemonic = false
            });

            _ = button;
            return panel;
        }

        private static Label Caption(string text, int x, int y) => new()
        {
            Text = text,
            Font = UiTheme.Label,
            ForeColor = UiTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(x, y),
            UseMnemonic = false
        };

        // ------------------------------------------------------------------ behaviour

        private async Task AttemptAsync()
        {
            if (_busy) return;

            // Validated here, one field at a time, so the operator is told exactly what is
            // missing rather than being sent to the server to be refused.
            if (string.IsNullOrWhiteSpace(_username.Text))
            {
                Show("Please enter your email address.");
                _username.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(_password.Text))
            {
                Show("Please enter your password.");
                _password.Focus();
                return;
            }

            try
            {
                SetBusy(true);
                Show(null);

                var result = await _session.SignInAsync(_username.Text.Trim(), _password.Text);

                if (!result.IsSuccess)
                {
                    Show(result.ErrorMessage ?? "Sign in failed. Please try again.");
                    _password.SelectAll();
                    _password.Focus();
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;

            _signIn.Enabled = !busy;
            _signIn.Text = busy ? "Signing in…" : "Sign in";
            _username.Enabled = !busy;
            _password.Enabled = !busy;
            _reveal.Enabled = !busy;
            _working.Text = busy ? "Contacting the FitCore server…" : "";
            _working.Visible = busy;

            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void Show(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                _errorPanel.Visible = false;
                return;
            }

            // A connection failure is two lines; a wrong password is one.
            var measured = TextRenderer.MeasureText(
                message, UiTheme.Body, new Size(_errorPanel.Width - 24, 0), TextFormatFlags.WordBreak);

            _errorPanel.Height = Math.Max(44, measured.Height + 22);
            _errorText.Text = message;
            _errorPanel.Visible = true;
        }
    }
}
