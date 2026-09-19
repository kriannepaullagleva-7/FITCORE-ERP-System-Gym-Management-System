using System.Drawing;
using Microsoft.Extensions.DependencyInjection;

namespace ERP_Project1
{
    public partial class MainForm : Form
    {
        private readonly IServiceScopeFactory _scopeFactory;

        private Panel pnlNav = null!;
        private Panel pnlContent = null!;
        private Label lblTitle = null!;
        private Label lblSubtitle = null!;
        private Label lblStatus = null!;
        private Button btnExit = null!;

        private readonly List<Button> _navButtons = new();
        private IServiceScope? _currentScope;
        private Form? _currentForm;

        public MainForm(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.ClientSize = new Size(1280, 780);
            this.MinimumSize = new Size(1100, 700);
            this.Name = "MainForm";
            this.Text = "FitCore ERP - Membership, Sales, Payment & Inventory";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = UiTheme.Canvas;

            pnlNav = new Panel
            {
                Dock = DockStyle.Left,
                Width = 235,
                BackColor = UiTheme.Primary
            };

            lblTitle = new Label
            {
                Text = "FitCore ERP",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.White,
                Dock = DockStyle.Top,
                Height = 46,
                TextAlign = ContentAlignment.MiddleCenter
            };

            lblSubtitle = new Label
            {
                Text = "Fitness Management Suite",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = UiTheme.PaleBlue,
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var top = 100;
            AddNavButton("Dashboard", top, (_, _) => Navigate<DashboardForm>("Dashboard"));
            AddNavButton("Membership", top + 54, (_, _) => Navigate<MembershipForm>("Membership"));
            AddNavButton("Sales", top + 108, (_, _) => Navigate<SalesForm>("Sales"));
            AddNavButton("Payment", top + 162, (_, _) => Navigate<PaymentForm>("Payment"));
            AddNavButton("Inventory", top + 216, (_, _) => Navigate<InventoryForm>("Inventory"));

            btnExit = UiTheme.CreateButton("Exit", 14, 0, UiTheme.Danger, BtnExit_Click, 207, 38);
            btnExit.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            pnlNav.Controls.Add(btnExit);

            // Title and subtitle dock to the top, so they are added last to sit above the buttons.
            pnlNav.Controls.Add(lblSubtitle);
            pnlNav.Controls.Add(lblTitle);

            lblStatus = new Label
            {
                Text = "Ready",
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = UiTheme.PaleBlue,
                ForeColor = UiTheme.TextBlue,
                Font = new Font("Segoe UI", 9),
                Padding = new Padding(12, 6, 12, 6)
            };

            pnlContent = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Canvas
            };

            this.Controls.Add(pnlContent);
            this.Controls.Add(lblStatus);
            this.Controls.Add(pnlNav);

            this.Load += MainForm_Load;
            this.Resize += (_, _) => PositionExitButton();
            this.FormClosed += (_, _) => ReleaseCurrentModule();

            this.ResumeLayout(false);
            PositionExitButton();
        }

        private void AddNavButton(string text, int top, EventHandler handler)
        {
            var button = UiTheme.CreateButton(text, 14, top, UiTheme.PrimaryDark, handler, 207, 44);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(16, 0, 0, 0);
            button.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            button.Tag = text;

            _navButtons.Add(button);
            pnlNav.Controls.Add(button);
        }

        private void PositionExitButton()
        {
            if (btnExit != null)
                btnExit.Location = new Point(14, pnlNav.Height - 58);
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            Navigate<DashboardForm>("Dashboard");
        }

        // Each module gets a dedicated DI scope so it works against its own DbContext and
        // always reads the current database state rather than cached tracked entities.
        private void Navigate<TForm>(string moduleName) where TForm : Form
        {
            IServiceScope? scope = null;

            try
            {
                Cursor = Cursors.WaitCursor;

                scope = _scopeFactory.CreateScope();
                var form = scope.ServiceProvider.GetRequiredService<TForm>();

                ReleaseCurrentModule();

                form.TopLevel = false;
                form.FormBorderStyle = FormBorderStyle.None;
                form.Dock = DockStyle.Fill;

                pnlContent.Controls.Add(form);
                form.Show();

                _currentForm = form;
                _currentScope = scope;
                scope = null;

                HighlightNavButton(moduleName);
                lblStatus.Text = $"Module: {moduleName}";
            }
            catch (Exception ex)
            {
                scope?.Dispose();
                lblStatus.Text = $"Could not open {moduleName}";
                MessageBox.Show(
                    $"Error opening {moduleName}: {ex.InnerException?.Message ?? ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void ReleaseCurrentModule()
        {
            if (_currentForm != null)
            {
                pnlContent.Controls.Remove(_currentForm);
                _currentForm.Dispose();
                _currentForm = null;
            }

            _currentScope?.Dispose();
            _currentScope = null;
        }

        private void HighlightNavButton(string moduleName)
        {
            foreach (var button in _navButtons)
            {
                var isActive = string.Equals(button.Tag as string, moduleName, StringComparison.Ordinal);
                button.BackColor = isActive ? Color.White : UiTheme.PrimaryDark;
                button.ForeColor = isActive ? UiTheme.TextBlue : Color.White;
            }
        }

        private void BtnExit_Click(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to exit?",
                "Confirm Exit",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
                this.Close();
        }
    }
}
