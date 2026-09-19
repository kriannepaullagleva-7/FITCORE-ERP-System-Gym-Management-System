using System.Drawing;
using ERP_infrastructure.services;
using Microsoft.Extensions.DependencyInjection;

namespace ERP_Project1
{
    // The Membership module: member records, the plans they can buy, the subscriptions
    // that grant access, and the derived status/expiry view over all three.
    public partial class MembershipForm : Form
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMemberService _memberService;
        private readonly ISubscriptionService _subscriptionService;

        // Each hosted tab runs in its own DI scope. All four tabs load as soon as the module
        // opens, and a DbContext cannot serve two queries at once - sharing one scope across
        // them throws "a second operation was started on this context instance".
        private readonly List<IServiceScope> _tabScopes = new();

        private TabControl tabs = null!;
        private DataGridView gridOverview = null!;
        private TextBox txtFilter = null!;
        private ComboBox cmbStatusFilter = null!;
        private Label lblCounts = null!;
        private Button btnRefreshOverview = null!;

        private List<MembershipView> _overview = new();
        private bool _isLoading;

        public MembershipForm(
            IServiceScopeFactory scopeFactory,
            IMemberService memberService,
            ISubscriptionService subscriptionService)
        {
            _scopeFactory = scopeFactory;
            _memberService = memberService;
            _subscriptionService = subscriptionService;

            InitializeComponent();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var scope in _tabScopes)
                    scope.Dispose();

                _tabScopes.Clear();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.BackColor = UiTheme.Canvas;
            this.Name = "MembershipForm";

            tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f),
                Padding = new Point(14, 6)
            };

            tabs.TabPages.Add(BuildOverviewTab());
            tabs.TabPages.Add(BuildHostedTab<Form1>("Members"));
            tabs.TabPages.Add(BuildHostedTab<MembershipPlanForm>("Membership Plans"));
            tabs.TabPages.Add(BuildHostedTab<SubscriptionForm>("Subscriptions"));

            this.Controls.Add(tabs);
            this.Load += MembershipForm_Load;
            this.ResumeLayout(false);
        }

        // Hosts a module form inside a tab, built from its own scope so it gets its own
        // DbContext and never collides with the sibling tabs.
        private TabPage BuildHostedTab<TForm>(string title) where TForm : Form
        {
            var scope = _scopeFactory.CreateScope();
            _tabScopes.Add(scope);

            var form = scope.ServiceProvider.GetRequiredService<TForm>();
            var page = new TabPage(title) { BackColor = UiTheme.Canvas };

            form.TopLevel = false;
            form.FormBorderStyle = FormBorderStyle.None;
            form.Dock = DockStyle.Fill;

            page.Controls.Add(form);
            form.Show();

            return page;
        }

        private TabPage BuildOverviewTab()
        {
            var page = new TabPage("Membership Status") { BackColor = UiTheme.Canvas };

            var heading = UiTheme.CreateHeading("Membership Status & Expiration", 16, 14, 460);

            var lblFilter = UiTheme.CreateLabel("Search:", 18, 54, 55);
            txtFilter = new TextBox { Location = new Point(76, 51), Size = new Size(220, 25) };
            txtFilter.TextChanged += (_, _) => ApplyFilter();

            var lblStatus = UiTheme.CreateLabel("Status:", 312, 54, 50);
            cmbStatusFilter = new ComboBox
            {
                Location = new Point(366, 51),
                Size = new Size(160, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbStatusFilter.Items.AddRange(new object[]
            {
                "All", "Active", "Expiring Soon", "Expired", "Cancelled", "No Plan"
            });
            cmbStatusFilter.SelectedIndex = 0;
            cmbStatusFilter.SelectedIndexChanged += (_, _) => ApplyFilter();

            btnRefreshOverview = UiTheme.CreateButton("Refresh", 544, 49, UiTheme.Primary, BtnRefreshOverview_Click);

            lblCounts = UiTheme.CreateLabel("", 666, 56, 400);
            lblCounts.ForeColor = UiTheme.Neutral;

            gridOverview = UiTheme.CreateGrid(0, 0, 100, 100);
            gridOverview.Dock = DockStyle.Fill;
            gridOverview.AutoGenerateColumns = false;
            BuildOverviewColumns();
            gridOverview.CellFormatting += GridOverview_CellFormatting;

            var header = UiTheme.CreateHeaderPanel(88);
            header.Controls.Add(heading);
            header.Controls.Add(lblFilter);
            header.Controls.Add(txtFilter);
            header.Controls.Add(lblStatus);
            header.Controls.Add(cmbStatusFilter);
            header.Controls.Add(btnRefreshOverview);
            header.Controls.Add(lblCounts);

            var body = UiTheme.CreateBodyPanel();
            body.Controls.Add(gridOverview);

            page.Controls.Add(body);
            page.Controls.Add(header);

            return page;
        }

        private void BuildOverviewColumns()
        {
            void AddColumn(string header, string property, string? format = null, int fillWeight = 100)
            {
                gridOverview.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = header,
                    DataPropertyName = property,
                    FillWeight = fillWeight,
                    DefaultCellStyle = { Format = format ?? string.Empty }
                });
            }

            AddColumn("ID", nameof(MembershipView.MemberId), null, 40);
            AddColumn("Member", nameof(MembershipView.FullName), null, 140);
            AddColumn("Phone", nameof(MembershipView.Phone), null, 100);
            AddColumn("Member Status", nameof(MembershipView.MemberStatus), null, 95);
            AddColumn("Plan", nameof(MembershipView.PlanName), null, 120);
            AddColumn("Start", nameof(MembershipView.StartDate), "dd MMM yyyy", 95);
            AddColumn("Expires", nameof(MembershipView.ExpiryDate), "dd MMM yyyy", 95);
            AddColumn("Days Left", nameof(MembershipView.DaysRemaining), null, 70);
            AddColumn("Membership", nameof(MembershipView.MembershipStatus), null, 100);
            AddColumn("Plan Price", nameof(MembershipView.PlanPrice), "C2", 90);
            AddColumn("Paid", nameof(MembershipView.AmountPaid), "C2", 90);
            AddColumn("Balance", nameof(MembershipView.Balance), "C2", 90);
            AddColumn("Payment", nameof(MembershipView.PaymentStatus), null, 80);
        }

        private async void MembershipForm_Load(object? sender, EventArgs e)
        {
            await LoadOverviewAsync();
        }

        private async void BtnRefreshOverview_Click(object? sender, EventArgs e)
        {
            await LoadOverviewAsync();
        }

        private async Task LoadOverviewAsync()
        {
            if (_isLoading) return;
            _isLoading = true;
            btnRefreshOverview.Enabled = false;

            try
            {
                // Lapsed memberships are settled first so the grid never shows a stale Active.
                await _subscriptionService.ExpireOverdueSubscriptionsAsync();

                _overview = await _memberService.GetMembershipOverviewAsync();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                _overview = new List<MembershipView>();
                gridOverview.DataSource = null;
                lblCounts.Text = "Membership status could not be loaded.";
                MessageBox.Show(
                    $"Error loading membership status: {ex.InnerException?.Message ?? ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                btnRefreshOverview.Enabled = true;
            }
        }

        private void ApplyFilter()
        {
            var term = txtFilter.Text?.Trim() ?? string.Empty;
            var status = cmbStatusFilter.SelectedItem as string ?? "All";

            IEnumerable<MembershipView> rows = _overview;

            if (term.Length > 0)
            {
                rows = rows.Where(r =>
                    r.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    r.Email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    r.Phone.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    r.PlanName.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.Equals(status, "All", StringComparison.Ordinal))
                rows = rows.Where(r => string.Equals(r.MembershipStatus, status, StringComparison.Ordinal));

            var filtered = rows.ToList();
            gridOverview.DataSource = filtered;

            var active = _overview.Count(r => r.MembershipStatus == "Active");
            var soon = _overview.Count(r => r.MembershipStatus == "Expiring Soon");
            var expired = _overview.Count(r => r.MembershipStatus == "Expired");

            lblCounts.Text =
                $"Showing {filtered.Count} of {_overview.Count}   |   Active {active}   Expiring soon {soon}   Expired {expired}";
        }

        // Colours the membership and payment columns so lapsed members stand out.
        private void GridOverview_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= gridOverview.Rows.Count) return;

            var column = gridOverview.Columns[e.ColumnIndex];
            var value = e.Value as string;
            if (value == null) return;

            if (column.DataPropertyName == nameof(MembershipView.MembershipStatus))
            {
                e.CellStyle.ForeColor = value switch
                {
                    "Active" => UiTheme.Success,
                    "Expiring Soon" => UiTheme.Warning,
                    "Expired" => UiTheme.Danger,
                    "Cancelled" => UiTheme.Danger,
                    _ => UiTheme.Neutral
                };
                e.CellStyle.Font = new Font(gridOverview.Font, FontStyle.Bold);
            }
            else if (column.DataPropertyName == nameof(MembershipView.PaymentStatus))
            {
                e.CellStyle.ForeColor = value switch
                {
                    "Paid" => UiTheme.Success,
                    "Partial" => UiTheme.Warning,
                    "Unpaid" => UiTheme.Danger,
                    _ => UiTheme.Neutral
                };
            }
        }
    }
}
