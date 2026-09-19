using System.Drawing;
using ERP_domain.entities;
using ERP_infrastructure.services;

namespace ERP_Project1
{
    public partial class SubscriptionForm : Form
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IMemberService _memberService;
        private readonly IMembershipPlanService _planService;

        private int _selectedSubscriptionId = -1;

        private ComboBox cmbMember = null!, cmbPlan = null!;
        private DateTimePicker dtStart = null!;
        private Button btnCreate = null!, btnRenew = null!, btnCancel = null!, btnDelete = null!, btnRefresh = null!;
        private DataGridView gridSubscriptions = null!;
        private Label lblCount = null!;

        public SubscriptionForm(
            ISubscriptionService subscriptionService,
            IMemberService memberService,
            IMembershipPlanService planService)
        {
            _subscriptionService = subscriptionService;
            _memberService = memberService;
            _planService = planService;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.BackColor = UiTheme.Canvas;
            this.Name = "SubscriptionForm";

            var heading = UiTheme.CreateHeading("Subscriptions - Status & Expiration", 16, 14, 460);

            var box = new GroupBox
            {
                Text = "New Subscription",
                Location = new Point(16, 46),
                Size = new Size(950, 100),
                BackColor = UiTheme.PaleBlue,
                ForeColor = UiTheme.TextBlue,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            box.Controls.Add(UiTheme.CreateLabel("Member:", 16, 32, 60));
            cmbMember = new ComboBox
            {
                Location = new Point(82, 29),
                Size = new Size(230, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            box.Controls.Add(cmbMember);

            box.Controls.Add(UiTheme.CreateLabel("Plan:", 330, 32, 40));
            cmbPlan = new ComboBox
            {
                Location = new Point(374, 29),
                Size = new Size(230, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            box.Controls.Add(cmbPlan);

            box.Controls.Add(UiTheme.CreateLabel("Start date:", 622, 32, 70));
            dtStart = new DateTimePicker
            {
                Location = new Point(696, 29),
                Size = new Size(130, 25),
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today
            };
            box.Controls.Add(dtStart);

            btnCreate = UiTheme.CreateButton("Create", 82, 62, UiTheme.Success, BtnCreate_Click);
            btnRenew = UiTheme.CreateButton("Renew", 200, 62, UiTheme.Primary, BtnRenew_Click);
            btnCancel = UiTheme.CreateButton("Cancel Sub", 318, 62, UiTheme.Warning, BtnCancel_Click);
            btnDelete = UiTheme.CreateButton("Delete", 436, 62, UiTheme.Danger, BtnDelete_Click);
            btnRefresh = UiTheme.CreateButton("Refresh", 554, 62, UiTheme.Neutral, BtnRefresh_Click);

            box.Controls.Add(btnCreate);
            box.Controls.Add(btnRenew);
            box.Controls.Add(btnCancel);
            box.Controls.Add(btnDelete);
            box.Controls.Add(btnRefresh);

            lblCount = UiTheme.CreateLabel("", 18, 154, 600);
            lblCount.ForeColor = UiTheme.Neutral;

            gridSubscriptions = UiTheme.CreateGrid(0, 0, 100, 100);
            gridSubscriptions.Dock = DockStyle.Fill;
            gridSubscriptions.SelectionChanged += GridSubscriptions_SelectionChanged;

            var header = UiTheme.CreateHeaderPanel(176);
            header.Controls.Add(heading);
            header.Controls.Add(box);
            header.Controls.Add(lblCount);

            var body = UiTheme.CreateBodyPanel();
            body.Controls.Add(gridSubscriptions);

            this.Controls.Add(body);
            this.Controls.Add(header);

            this.Load += SubscriptionForm_Load;
            this.ResumeLayout(false);
        }

        private async void SubscriptionForm_Load(object? sender, EventArgs e)
        {
            await LoadLookupsAsync();
            await LoadSubscriptionsAsync();
        }

        private async Task LoadLookupsAsync()
        {
            try
            {
                var members = await _memberService.GetAllMembersAsync();
                cmbMember.DisplayMember = "Display";
                cmbMember.ValueMember = "MemberId";
                cmbMember.DataSource = members
                    .Select(m => new { m.MemberId, Display = $"{m.FirstName} {m.LastName} (#{m.MemberId})" })
                    .ToList();

                var plans = await _planService.GetActivePlansAsync();
                cmbPlan.DisplayMember = "Display";
                cmbPlan.ValueMember = "PlanId";
                cmbPlan.DataSource = plans
                    .Select(p => new { p.PlanId, Display = $"{p.PlanName} - {p.DurationMonths} mo - {p.Price:C2}" })
                    .ToList();

                if (plans.Count == 0)
                    lblCount.Text = "No active membership plans yet - add one on the Membership Plans tab first.";
            }
            catch (Exception ex)
            {
                ShowError("Error loading members and plans", ex);
            }
        }

        private async Task LoadSubscriptionsAsync()
        {
            SetBusy(true);
            try
            {
                // Settle lapsed memberships before listing, so Status reflects today.
                await _subscriptionService.ExpireOverdueSubscriptionsAsync();

                var subscriptions = await _subscriptionService.GetAllSubscriptionsAsync();
                var today = DateTime.UtcNow.Date;

                gridSubscriptions.DataSource = subscriptions.Select(s => new
                {
                    s.SubscriptionId,
                    Member = s.Member == null
                        ? $"#{s.MemberId}"
                        : $"{s.Member.FirstName} {s.Member.LastName}",
                    Plan = s.Plan?.PlanName ?? $"#{s.PlanId}",
                    Price = s.Plan?.Price ?? 0m,
                    Start = s.StartDate,
                    Expires = s.EndDate,
                    DaysLeft = (int)Math.Ceiling((s.EndDate.Date - today).TotalDays),
                    s.Status,
                    Paid = s.Payments.Where(p => p.Status == "Completed").Sum(p => p.Amount)
                }).ToList();

                var active = subscriptions.Count(s => s.Status == "Active");
                var expired = subscriptions.Count(s => s.Status == "Expired");
                var cancelled = subscriptions.Count(s => s.Status == "Cancelled");
                lblCount.Text = $"{subscriptions.Count} subscription(s) - Active {active}, Expired {expired}, Cancelled {cancelled}";
            }
            catch (Exception ex)
            {
                gridSubscriptions.DataSource = null;
                ShowError("Error loading subscriptions", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnRefresh_Click(object? sender, EventArgs e)
        {
            _selectedSubscriptionId = -1;
            await LoadLookupsAsync();
            await LoadSubscriptionsAsync();
        }

        private async void BtnCreate_Click(object? sender, EventArgs e)
        {
            if (cmbMember.SelectedValue is not int memberId)
            {
                MessageBox.Show("Select a member.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (cmbPlan.SelectedValue is not int planId)
            {
                MessageBox.Show("Select a membership plan.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true);
            try
            {
                var subscription = await _subscriptionService.CreateSubscriptionAsync(
                    memberId, planId, DateTime.SpecifyKind(dtStart.Value.Date, DateTimeKind.Utc));

                MessageBox.Show(
                    $"Subscription #{subscription.SubscriptionId} created.\nExpires {subscription.EndDate:dd MMM yyyy}.",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                await LoadSubscriptionsAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error creating subscription", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnRenew_Click(object? sender, EventArgs e)
        {
            if (!RequireSelection()) return;

            SetBusy(true);
            try
            {
                var renewed = await _subscriptionService.RenewSubscriptionAsync(_selectedSubscriptionId);

                if (renewed == null)
                {
                    MessageBox.Show("That subscription or its plan no longer exists.", "Not Found",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show($"Renewed. New expiry: {renewed.EndDate:dd MMM yyyy}.",
                        "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                await LoadSubscriptionsAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error renewing subscription", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnCancel_Click(object? sender, EventArgs e)
        {
            if (!RequireSelection()) return;

            var confirm = MessageBox.Show(
                $"Cancel subscription #{_selectedSubscriptionId}?",
                "Confirm Cancel", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SetBusy(true);
            try
            {
                var cancelled = await _subscriptionService.CancelSubscriptionAsync(_selectedSubscriptionId);

                MessageBox.Show(
                    cancelled == null ? "That subscription no longer exists." : "Subscription cancelled.",
                    cancelled == null ? "Not Found" : "Success",
                    MessageBoxButtons.OK,
                    cancelled == null ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

                await LoadSubscriptionsAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error cancelling subscription", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnDelete_Click(object? sender, EventArgs e)
        {
            if (!RequireSelection()) return;

            var confirm = MessageBox.Show(
                $"Delete subscription #{_selectedSubscriptionId}?\n\n" +
                "Payments recorded against it will be kept but will no longer point at a membership.",
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SetBusy(true);
            try
            {
                var deleted = await _subscriptionService.DeleteSubscriptionAsync(_selectedSubscriptionId);

                MessageBox.Show(
                    deleted ? "Subscription deleted." : "That subscription no longer exists.",
                    deleted ? "Success" : "Not Found",
                    MessageBoxButtons.OK,
                    deleted ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

                _selectedSubscriptionId = -1;
                await LoadSubscriptionsAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error deleting subscription", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void GridSubscriptions_SelectionChanged(object? sender, EventArgs e)
        {
            if (gridSubscriptions.CurrentRow?.Cells["SubscriptionId"].Value is int id)
                _selectedSubscriptionId = id;
        }

        private bool RequireSelection()
        {
            if (_selectedSubscriptionId != -1) return true;

            MessageBox.Show("Select a subscription first.", "Selection Required",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void SetBusy(bool busy)
        {
            btnCreate.Enabled = !busy;
            btnRenew.Enabled = !busy;
            btnCancel.Enabled = !busy;
            btnDelete.Enabled = !busy;
            btnRefresh.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private static void ShowError(string context, Exception ex)
        {
            MessageBox.Show($"{context}: {ex.InnerException?.Message ?? ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
