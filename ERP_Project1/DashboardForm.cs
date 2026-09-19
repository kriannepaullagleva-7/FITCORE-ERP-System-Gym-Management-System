using System.Drawing;
using ERP_infrastructure.services;

namespace ERP_Project1
{
    public partial class DashboardForm : Form
    {
        private readonly IDashboardService _dashboardService;

        private FlowLayoutPanel pnlTiles = null!;
        private Label lblHeading = null!;
        private Label lblGenerated = null!;
        private Button btnRefresh = null!;
        private bool _isLoading;

        public DashboardForm(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.BackColor = UiTheme.Canvas;
            this.Name = "DashboardForm";

            lblHeading = UiTheme.CreateHeading("Dashboard", 20, 18, 400);

            lblGenerated = UiTheme.CreateLabel("", 22, 48, 500);
            lblGenerated.ForeColor = UiTheme.Neutral;

            btnRefresh = UiTheme.CreateButton("Refresh", 20, 74, UiTheme.Primary, BtnRefresh_Click);

            pnlTiles = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(4)
            };

            var header = UiTheme.CreateHeaderPanel(114);
            header.Controls.Add(lblHeading);
            header.Controls.Add(lblGenerated);
            header.Controls.Add(btnRefresh);

            var body = UiTheme.CreateBodyPanel();
            body.Controls.Add(pnlTiles);

            this.Controls.Add(body);
            this.Controls.Add(header);

            this.Load += DashboardForm_Load;
            this.ResumeLayout(false);
        }

        private async void DashboardForm_Load(object? sender, EventArgs e)
        {
            await LoadSummaryAsync();
        }

        private async void BtnRefresh_Click(object? sender, EventArgs e)
        {
            await LoadSummaryAsync();
        }

        private async Task LoadSummaryAsync()
        {
            if (_isLoading) return;
            _isLoading = true;
            btnRefresh.Enabled = false;

            try
            {
                var summary = await _dashboardService.GetSummaryAsync();
                RenderTiles(summary);
                lblGenerated.Text = $"Live figures from the TenantErp database - read {summary.GeneratedAtUtc.ToLocalTime():dd MMM yyyy HH:mm:ss}";
            }
            catch (Exception ex)
            {
                pnlTiles.Controls.Clear();
                lblGenerated.Text = "Dashboard could not be loaded.";
                MessageBox.Show(
                    $"Error loading dashboard: {ex.InnerException?.Message ?? ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                btnRefresh.Enabled = true;
            }
        }

        private void RenderTiles(DashboardSummary summary)
        {
            pnlTiles.SuspendLayout();
            pnlTiles.Controls.Clear();

            AddSectionTitle("Membership");
            AddTile("Total Members", summary.TotalMembers.ToString("N0"), UiTheme.Primary);
            AddTile("Active Members", summary.ActiveMembers.ToString("N0"), UiTheme.Success);
            AddTile("Active Subscriptions", summary.ActiveSubscriptions.ToString("N0"), UiTheme.Success);
            AddTile("Expiring Soon", summary.ExpiringSoon.ToString("N0"),
                summary.ExpiringSoon > 0 ? UiTheme.Warning : UiTheme.Neutral);
            AddTile("Expired", summary.ExpiredSubscriptions.ToString("N0"),
                summary.ExpiredSubscriptions > 0 ? UiTheme.Danger : UiTheme.Neutral);
            AddTile("Membership Plans", summary.TotalPlans.ToString("N0"), UiTheme.Primary);

            AddSectionTitle("Sales");
            AddTile("Transactions", summary.SalesCount.ToString("N0"), UiTheme.Primary);
            AddTile("Total Sales", summary.SalesTotal.ToString("C2"), UiTheme.Success);
            AddTile("Sales This Month", summary.SalesThisMonth.ToString("C2"), UiTheme.Success);

            AddSectionTitle("Payments");
            AddTile("Payments Recorded", summary.PaymentsCount.ToString("N0"), UiTheme.Primary);
            AddTile("Collected", summary.PaymentsTotal.ToString("C2"), UiTheme.Success);
            AddTile("Collected This Month", summary.PaymentsThisMonth.ToString("C2"), UiTheme.Success);
            AddTile("Pending", summary.PendingPayments.ToString("N0"),
                summary.PendingPayments > 0 ? UiTheme.Warning : UiTheme.Neutral);

            AddSectionTitle("Inventory");
            AddTile("Products", summary.TotalProducts.ToString("N0"), UiTheme.Primary);
            AddTile("Low Stock", summary.LowStockProducts.ToString("N0"),
                summary.LowStockProducts > 0 ? UiTheme.Warning : UiTheme.Neutral);
            AddTile("Out of Stock", summary.OutOfStockProducts.ToString("N0"),
                summary.OutOfStockProducts > 0 ? UiTheme.Danger : UiTheme.Neutral);
            AddTile("Stock Value", summary.InventoryValue.ToString("C2"), UiTheme.Primary);

            pnlTiles.ResumeLayout(true);
        }

        // A full-width heading that forces the tiles after it onto a fresh row.
        private void AddSectionTitle(string text)
        {
            var label = new Label
            {
                Text = text.ToUpperInvariant(),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = UiTheme.TextBlue,
                Width = 940,
                Height = 30,
                TextAlign = ContentAlignment.BottomLeft,
                Margin = new Padding(4, 12, 4, 2)
            };

            pnlTiles.Controls.Add(label);
            pnlTiles.SetFlowBreak(label, true);
        }

        private void AddTile(string caption, string value, Color accent)
        {
            var tile = new Panel
            {
                Width = 218,
                Height = 92,
                BackColor = Color.White,
                Margin = new Padding(4, 4, 8, 8),
                Padding = new Padding(0)
            };

            var stripe = new Panel
            {
                Dock = DockStyle.Left,
                Width = 6,
                BackColor = accent
            };

            var lblValue = new Label
            {
                Text = value,
                Font = new Font("Segoe UI", 17, FontStyle.Bold),
                ForeColor = accent,
                Location = new Point(18, 14),
                Size = new Size(190, 38),
                AutoEllipsis = true
            };

            var lblCaption = new Label
            {
                Text = caption,
                Font = new Font("Segoe UI", 9),
                ForeColor = UiTheme.Neutral,
                Location = new Point(20, 56),
                Size = new Size(190, 22)
            };

            tile.Controls.Add(lblValue);
            tile.Controls.Add(lblCaption);
            tile.Controls.Add(stripe);

            pnlTiles.Controls.Add(tile);
        }
    }
}
