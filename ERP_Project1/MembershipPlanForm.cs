using System.Drawing;
using ERP_domain.entities;
using ERP_infrastructure.services;

namespace ERP_Project1
{
    public partial class MembershipPlanForm : Form
    {
        private readonly IMembershipPlanService _service;
        private int _selectedPlanId = -1;
        private bool _isBusy;

        private TextBox txtPlanName = null!, txtDurationMonths = null!, txtPrice = null!, txtDescription = null!;
        private CheckBox chkIsActive = null!;
        private Button btnAdd = null!, btnUpdate = null!, btnDelete = null!, btnClear = null!, btnRefresh = null!;
        private DataGridView gridPlans = null!;
        private Label lblCount = null!;

        public MembershipPlanForm(IMembershipPlanService service)
        {
            _service = service;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.BackColor = UiTheme.Canvas;
            this.Name = "MembershipPlanForm";

            var heading = UiTheme.CreateHeading("Membership Plans / Types", 16, 14, 400);

            var box = new GroupBox
            {
                Text = "Plan Details",
                Location = new Point(16, 46),
                Size = new Size(950, 132),
                BackColor = UiTheme.PaleBlue,
                ForeColor = UiTheme.TextBlue,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            box.Controls.Add(UiTheme.CreateLabel("Plan Name:", 16, 32, 80));
            txtPlanName = new TextBox { Location = new Point(102, 29), Size = new Size(210, 25) };
            box.Controls.Add(txtPlanName);

            box.Controls.Add(UiTheme.CreateLabel("Duration (months):", 330, 32, 115));
            txtDurationMonths = new TextBox { Location = new Point(450, 29), Size = new Size(80, 25) };
            box.Controls.Add(txtDurationMonths);

            box.Controls.Add(UiTheme.CreateLabel("Price:", 552, 32, 45));
            txtPrice = new TextBox { Location = new Point(600, 29), Size = new Size(110, 25) };
            box.Controls.Add(txtPrice);

            chkIsActive = new CheckBox
            {
                Text = "Active",
                Location = new Point(734, 30),
                Size = new Size(80, 24),
                Checked = true,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(55, 71, 79)
            };
            box.Controls.Add(chkIsActive);

            box.Controls.Add(UiTheme.CreateLabel("Description:", 16, 68, 80));
            txtDescription = new TextBox { Location = new Point(102, 65), Size = new Size(712, 25) };
            box.Controls.Add(txtDescription);

            btnAdd = UiTheme.CreateButton("Add Plan", 102, 96, UiTheme.Success, BtnAdd_Click);
            btnUpdate = UiTheme.CreateButton("Update", 220, 96, UiTheme.Warning, BtnUpdate_Click);
            btnDelete = UiTheme.CreateButton("Delete", 338, 96, UiTheme.Danger, BtnDelete_Click);
            btnClear = UiTheme.CreateButton("Clear", 456, 96, UiTheme.Neutral, (_, _) => ClearForm());
            btnRefresh = UiTheme.CreateButton("Refresh", 574, 96, UiTheme.Primary, BtnRefresh_Click);

            box.Controls.Add(btnAdd);
            box.Controls.Add(btnUpdate);
            box.Controls.Add(btnDelete);
            box.Controls.Add(btnClear);
            box.Controls.Add(btnRefresh);

            lblCount = UiTheme.CreateLabel("", 18, 186, 500);
            lblCount.ForeColor = UiTheme.Neutral;

            gridPlans = UiTheme.CreateGrid(0, 0, 100, 100);
            gridPlans.Dock = DockStyle.Fill;
            gridPlans.SelectionChanged += GridPlans_SelectionChanged;

            var header = UiTheme.CreateHeaderPanel(208);
            header.Controls.Add(heading);
            header.Controls.Add(box);
            header.Controls.Add(lblCount);

            var body = UiTheme.CreateBodyPanel();
            body.Controls.Add(gridPlans);

            this.Controls.Add(body);
            this.Controls.Add(header);

            this.Load += MembershipPlanForm_Load;
            this.ResumeLayout(false);
        }

        private async void MembershipPlanForm_Load(object? sender, EventArgs e)
        {
            await LoadPlansAsync();
        }

        private async void BtnRefresh_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            ClearForm();
            await LoadPlansAsync();
        }

        private async Task LoadPlansAsync()
        {
            SetBusy(true);
            try
            {
                var plans = await _service.GetAllPlansAsync();

                gridPlans.DataSource = plans.Select(p => new
                {
                    p.PlanId,
                    p.PlanName,
                    Months = p.DurationMonths,
                    p.Price,
                    p.Description,
                    Status = p.IsActive ? "Active" : "Inactive"
                }).ToList();

                if (gridPlans.Columns.Contains("Price"))
                    gridPlans.Columns["Price"]!.DefaultCellStyle.Format = "N2";

                lblCount.Text = $"{plans.Count} plan(s) - {plans.Count(p => p.IsActive)} active";
            }
            catch (Exception ex)
            {
                gridPlans.DataSource = null;
                ShowError("Error loading membership plans", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnAdd_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (!TryReadForm(out var name, out var months, out var price, out var description))
                return;

            SetBusy(true);
            try
            {
                var plan = await _service.CreatePlanAsync(name, months, price, description);
                MessageBox.Show($"Plan created. ID: {plan.PlanId}", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                ClearForm();
                await LoadPlansAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error creating plan", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnUpdate_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (_selectedPlanId == -1)
            {
                MessageBox.Show("Select a plan to update.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryReadForm(out var name, out var months, out var price, out var description))
                return;

            SetBusy(true);
            try
            {
                var updated = await _service.UpdatePlanAsync(
                    _selectedPlanId, name, months, price, description, chkIsActive.Checked);

                if (updated == null)
                {
                    MessageBox.Show("That plan no longer exists.", "Not Found",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show("Plan updated.", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ClearForm();
                }

                await LoadPlansAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error updating plan", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnDelete_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (_selectedPlanId == -1)
            {
                MessageBox.Show("Select a plan to delete.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete plan '{txtPlanName.Text}'?\n\nPlans already used by a subscription cannot be deleted - " +
                "untick Active instead to retire them.",
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SetBusy(true);
            try
            {
                var result = await _service.DeletePlanAsync(_selectedPlanId);

                switch (result)
                {
                    case PlanDeleteResult.Deleted:
                        MessageBox.Show("Plan deleted.", "Success",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        ClearForm();
                        break;

                    case PlanDeleteResult.InUse:
                        var inUse = await _service.CountSubscriptionsForPlanAsync(_selectedPlanId);
                        MessageBox.Show(
                            $"This plan is used by {inUse} subscription(s), so it cannot be deleted.\n\n" +
                            "Untick Active and press Update to retire it instead - existing subscriptions " +
                            "keep working and the plan stops appearing for new ones.",
                            "Plan In Use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        break;

                    default:
                        MessageBox.Show("That plan no longer exists.", "Not Found",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        ClearForm();
                        break;
                }

                await LoadPlansAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error deleting plan", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void GridPlans_SelectionChanged(object? sender, EventArgs e)
        {
            // ClearSelection also raises SelectionChanged, and CurrentRow survives it - without
            // the Selected check the Clear button would immediately refill the form.
            if (gridPlans.CurrentRow is not { Selected: true }) return;

            var row = gridPlans.CurrentRow;
            if (row.Cells["PlanId"].Value is not int planId) return;

            _selectedPlanId = planId;
            txtPlanName.Text = row.Cells["PlanName"].Value?.ToString() ?? string.Empty;
            txtDurationMonths.Text = row.Cells["Months"].Value?.ToString() ?? string.Empty;
            txtPrice.Text = row.Cells["Price"].Value is decimal price ? price.ToString("0.00") : string.Empty;
            txtDescription.Text = row.Cells["Description"].Value?.ToString() ?? string.Empty;
            chkIsActive.Checked = (row.Cells["Status"].Value?.ToString() ?? "Active") == "Active";
        }

        private bool TryReadForm(out string name, out int months, out decimal price, out string description)
        {
            name = txtPlanName.Text?.Trim() ?? string.Empty;
            description = txtDescription.Text?.Trim() ?? string.Empty;
            months = 0;
            price = 0m;

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Plan name is required.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!int.TryParse(txtDurationMonths.Text?.Trim(), out months) || months <= 0)
            {
                MessageBox.Show("Duration must be a whole number of months greater than zero.",
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!decimal.TryParse(txtPrice.Text?.Trim(), out price) || price < 0)
            {
                MessageBox.Show("Price must be a number of zero or more.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private void ClearForm()
        {
            _selectedPlanId = -1;
            txtPlanName.Clear();
            txtDurationMonths.Clear();
            txtPrice.Clear();
            txtDescription.Clear();
            chkIsActive.Checked = true;
            gridPlans.ClearSelection();
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            btnAdd.Enabled = !busy;
            btnUpdate.Enabled = !busy;
            btnDelete.Enabled = !busy;
            btnClear.Enabled = !busy;
            btnRefresh.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        // Rules the service enforces (blank name, duplicate name, bad duration or price)
        // arrive as InvalidOperationException and read as guidance, not as a failure.
        private static void ShowError(string context, Exception ex)
        {
            if (ex is ValidationException)
            {
                MessageBox.Show(ex.Message, "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show($"{context}: {ex.InnerException?.Message ?? ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
