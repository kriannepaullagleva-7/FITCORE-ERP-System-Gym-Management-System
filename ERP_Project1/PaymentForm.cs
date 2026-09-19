using System.Drawing;
using ERP_infrastructure.services;

namespace ERP_Project1
{
    // The Payment module, backed by the tenant database.
    //
    // A payment settles a membership subscription or nothing in particular. Reversing one
    // marks it Refunded through IPaymentService.VoidPaymentAsync rather than deleting the row,
    // because a settled financial record that disappears leaves a ledger that cannot explain
    // its own totals.
    public partial class PaymentForm : Form
    {
        private static readonly string[] Methods = { "Cash", "Card", "Transfer", "Check", "GCash" };
        private static readonly string[] Statuses = { "Completed", "Pending", "Failed", "Refunded" };

        private readonly IPaymentService _paymentService;
        private readonly IMemberService _memberService;
        private readonly ISubscriptionService _subscriptionService;

        private ComboBox cmbMember = null!, cmbMembership = null!, cmbMethod = null!, cmbStatus = null!;
        private TextBox txtAmount = null!, txtReference = null!, txtNotes = null!;
        private DateTimePicker dtPaymentDate = null!;
        private CheckBox chkOnlySelectedMember = null!;
        private Button btnRecord = null!, btnUpdate = null!, btnVoid = null!, btnClear = null!, btnRefresh = null!;
        private DataGridView gridPayments = null!;
        private Label lblSummary = null!;

        private List<PaymentView> _payments = new();
        private int _selectedPaymentId = -1;
        private bool _isBusy;

        public PaymentForm(
            IPaymentService paymentService,
            IMemberService memberService,
            ISubscriptionService subscriptionService)
        {
            _paymentService = paymentService;
            _memberService = memberService;
            _subscriptionService = subscriptionService;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.BackColor = UiTheme.Canvas;
            this.Name = "PaymentForm";

            var heading = UiTheme.CreateHeading("Payments", 16, 10, 300);

            var box = new GroupBox
            {
                Text = "Record / Edit Payment",
                Location = new Point(16, 40),
                Size = new Size(950, 170),
                BackColor = UiTheme.PaleBlue,
                ForeColor = UiTheme.TextBlue,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            box.Controls.Add(UiTheme.CreateLabel("Member:", 16, 32, 60));
            cmbMember = new ComboBox
            {
                Location = new Point(84, 29),
                Size = new Size(230, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                ValueMember = "Value"
            };
            cmbMember.SelectedIndexChanged += CmbMember_SelectedIndexChanged;
            box.Controls.Add(cmbMember);

            box.Controls.Add(UiTheme.CreateLabel("Membership:", 330, 32, 80));
            cmbMembership = new ComboBox
            {
                Location = new Point(416, 29),
                Size = new Size(260, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                ValueMember = "Value"
            };
            box.Controls.Add(cmbMembership);

            box.Controls.Add(UiTheme.CreateLabel("Amount:", 692, 32, 58));
            txtAmount = new TextBox { Location = new Point(756, 29), Size = new Size(110, 25) };
            box.Controls.Add(txtAmount);

            box.Controls.Add(UiTheme.CreateLabel("Date:", 16, 68, 60));
            dtPaymentDate = new DateTimePicker
            {
                Location = new Point(84, 65),
                Size = new Size(130, 25),
                Format = DateTimePickerFormat.Short
            };
            box.Controls.Add(dtPaymentDate);

            box.Controls.Add(UiTheme.CreateLabel("Method:", 230, 68, 58));
            cmbMethod = new ComboBox
            {
                Location = new Point(292, 65),
                Size = new Size(130, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbMethod.Items.AddRange(Methods);
            cmbMethod.SelectedIndex = 0;
            box.Controls.Add(cmbMethod);

            box.Controls.Add(UiTheme.CreateLabel("Reference:", 438, 68, 68));
            txtReference = new TextBox { Location = new Point(510, 65), Size = new Size(160, 25) };
            box.Controls.Add(txtReference);

            box.Controls.Add(UiTheme.CreateLabel("Status:", 692, 68, 58));
            cmbStatus = new ComboBox
            {
                Location = new Point(756, 65),
                Size = new Size(140, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbStatus.Items.AddRange(Statuses);
            cmbStatus.SelectedIndex = 0;
            box.Controls.Add(cmbStatus);

            box.Controls.Add(UiTheme.CreateLabel("Notes:", 16, 104, 60));
            txtNotes = new TextBox { Location = new Point(84, 101), Size = new Size(812, 25) };
            box.Controls.Add(txtNotes);

            btnRecord = UiTheme.CreateButton("Record", 84, 130, UiTheme.Success, BtnRecord_Click);
            btnUpdate = UiTheme.CreateButton("Update", 202, 130, UiTheme.Warning, BtnUpdate_Click);
            btnVoid = UiTheme.CreateButton("Void", 320, 130, UiTheme.Danger, BtnVoid_Click);
            btnClear = UiTheme.CreateButton("Clear", 438, 130, UiTheme.Neutral, BtnClear_Click);
            btnRefresh = UiTheme.CreateButton("Refresh", 556, 130, UiTheme.Primary, BtnRefresh_Click);

            box.Controls.Add(btnRecord);
            box.Controls.Add(btnUpdate);
            box.Controls.Add(btnVoid);
            box.Controls.Add(btnClear);
            box.Controls.Add(btnRefresh);

            chkOnlySelectedMember = new CheckBox
            {
                Text = "Show only the selected member",
                Location = new Point(680, 134),
                Size = new Size(216, 24),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(55, 71, 79)
            };
            chkOnlySelectedMember.CheckedChanged += (_, _) => BindPayments();
            box.Controls.Add(chkOnlySelectedMember);

            lblSummary = UiTheme.CreateLabel("", 18, 218, 700);
            lblSummary.ForeColor = UiTheme.Neutral;

            gridPayments = UiTheme.CreateGrid(0, 0, 100, 100);
            gridPayments.Dock = DockStyle.Fill;
            gridPayments.SelectionChanged += GridPayments_SelectionChanged;

            var header = UiTheme.CreateHeaderPanel(242);
            header.Controls.Add(heading);
            header.Controls.Add(box);
            header.Controls.Add(lblSummary);

            var body = UiTheme.CreateBodyPanel();
            body.Controls.Add(gridPayments);

            this.Controls.Add(body);
            this.Controls.Add(header);

            this.Load += PaymentForm_Load;
            this.ResumeLayout(false);
        }

        private async void PaymentForm_Load(object? sender, EventArgs e)
        {
            await LoadMembersAsync();
            await LoadPaymentsAsync();
        }

        private async Task LoadMembersAsync()
        {
            SetBusy(true);
            try
            {
                var members = await _memberService.GetAllMembersAsync();

                cmbMember.DataSource = members
                    .OrderBy(m => m.FirstName)
                    .Select(m => new
                    {
                        Text = $"{m.FirstName} {m.LastName} (#{m.MemberId})",
                        Value = m.MemberId
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                ShowError("Error loading members", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        /// <summary>
        /// Loads the memberships the selected member actually holds, so a payment can only be
        /// attached to one of their own subscriptions. The server enforces the same rule.
        /// </summary>
        private async void CmbMember_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cmbMember.SelectedValue is not int memberId) return;

            try
            {
                var subscriptions = await _subscriptionService.GetMemberSubscriptionsAsync(memberId);

                var options = new List<object>
                {
                    new { Text = "- general payment (no membership) -", Value = 0 }
                };

                options.AddRange(subscriptions.Select(s => (object)new
                {
                    Text = $"#{s.SubscriptionId} {s.Plan?.PlanName ?? "plan"} " +
                           $"({s.StartDate:d MMM yyyy} - {s.EndDate:d MMM yyyy}) {s.Status}",
                    Value = s.SubscriptionId
                }));

                cmbMembership.DataSource = options;

                if (chkOnlySelectedMember.Checked) BindPayments();
            }
            catch (Exception ex)
            {
                ShowError("Error loading that member's memberships", ex);
            }
        }

        private async Task LoadPaymentsAsync()
        {
            SetBusy(true);
            try
            {
                _payments = await _paymentService.GetAllPaymentsAsync();
                BindPayments();
            }
            catch (Exception ex)
            {
                gridPayments.DataSource = null;
                ShowError("Error loading payments", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void BindPayments()
        {
            IEnumerable<PaymentView> rows = _payments;

            if (chkOnlySelectedMember.Checked && cmbMember.SelectedValue is int memberId)
                rows = rows.Where(p => p.MemberId == memberId);

            var list = rows.ToList();

            gridPayments.DataSource = list
                .Select(p => new
                {
                    p.PaymentId,
                    Member = p.MemberName,
                    Settles = p.AppliesTo,
                    p.Amount,
                    Date = p.PaymentDate,
                    p.Method,
                    Reference = p.ReferenceNo,
                    p.Status,
                    p.Notes
                })
                .ToList();

            if (gridPayments.Columns.Contains("Amount"))
                gridPayments.Columns["Amount"]!.DefaultCellStyle.Format = "N2";

            var completed = list.Where(p => p.Status == "Completed").Sum(p => p.Amount);
            var pending = list.Where(p => p.Status == "Pending").Sum(p => p.Amount);
            var refunded = list.Where(p => p.Status == "Refunded").Sum(p => p.Amount);

            lblSummary.Text =
                $"{list.Count} payment(s)  |  Completed {completed:N2}   Pending {pending:N2}   Refunded {refunded:N2}";
        }

        private void GridPayments_SelectionChanged(object? sender, EventArgs e)
        {
            if (gridPayments.CurrentRow is not { Selected: true }) return;
            if (gridPayments.CurrentRow.Cells["PaymentId"].Value is not int paymentId) return;

            var payment = _payments.FirstOrDefault(p => p.PaymentId == paymentId);
            if (payment is null) return;

            _selectedPaymentId = paymentId;

            txtAmount.Text = payment.Amount.ToString("0.00");
            dtPaymentDate.Value = payment.PaymentDate.ToLocalTime().Date;
            txtReference.Text = payment.ReferenceNo;
            txtNotes.Text = payment.Notes;

            cmbMethod.SelectedItem = Methods.Contains(payment.Method) ? payment.Method : "Cash";
            cmbStatus.SelectedItem = Statuses.Contains(payment.Status) ? payment.Status : "Completed";
        }

        private bool TryReadForm(out decimal amount)
        {
            amount = 0m;

            if (!decimal.TryParse(txtAmount.Text, out amount) || amount <= 0)
            {
                MessageBox.Show("Enter an amount greater than zero.", "Invalid Amount",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtAmount.Focus();
                return false;
            }

            return true;
        }

        private async void BtnRecord_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;
            if (!TryReadForm(out var amount)) return;

            if (cmbMember.SelectedValue is not int memberId)
            {
                MessageBox.Show("Select the member this payment came from.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Value 0 is the "general payment" entry, which means no subscription is settled.
            var subscriptionId = cmbMembership.SelectedValue is int id && id > 0 ? id : (int?)null;

            SetBusy(true);
            try
            {
                var payment = await _paymentService.RecordPaymentAsync(
                    memberId,
                    subscriptionId,
                    null,
                    amount,
                    dtPaymentDate.Value.Date,
                    cmbMethod.SelectedItem?.ToString() ?? "Cash",
                    txtReference.Text,
                    cmbStatus.SelectedItem?.ToString() ?? "Completed",
                    txtNotes.Text);

                MessageBox.Show($"Payment recorded. ID: {payment.PaymentId}", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                ClearForm();
                await LoadPaymentsAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error recording the payment", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnUpdate_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (_selectedPaymentId == -1)
            {
                MessageBox.Show("Select a payment to update.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryReadForm(out var amount)) return;

            SetBusy(true);
            try
            {
                var updated = await _paymentService.UpdatePaymentAsync(
                    _selectedPaymentId,
                    amount,
                    dtPaymentDate.Value.Date,
                    cmbMethod.SelectedItem?.ToString() ?? "Cash",
                    txtReference.Text,
                    cmbStatus.SelectedItem?.ToString() ?? "Completed",
                    txtNotes.Text);

                if (updated is null)
                {
                    MessageBox.Show("That payment no longer exists.", "Not Found",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show("Payment updated.", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ClearForm();
                }

                await LoadPaymentsAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error updating the payment", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnVoid_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (_selectedPaymentId == -1)
            {
                MessageBox.Show("Select a payment to void.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Void payment #{_selectedPaymentId}?\n\n" +
                "The payment is marked Refunded and kept on file, so the ledger stays auditable. " +
                "Anything it was settling goes back to owing this amount.",
                "Confirm Void", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SetBusy(true);
            try
            {
                var voided = await _paymentService.VoidPaymentAsync(
                    _selectedPaymentId, "Voided from the desktop app");

                if (voided is null)
                {
                    MessageBox.Show("That payment no longer exists.", "Not Found",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show("Payment voided and marked refunded.", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ClearForm();
                }

                await LoadPaymentsAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error voiding the payment", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void BtnClear_Click(object? sender, EventArgs e) => ClearForm();

        private async void BtnRefresh_Click(object? sender, EventArgs e)
        {
            await LoadMembersAsync();
            await LoadPaymentsAsync();
        }

        private void ClearForm()
        {
            _selectedPaymentId = -1;
            txtAmount.Clear();
            txtReference.Clear();
            txtNotes.Clear();
            dtPaymentDate.Value = DateTime.Today;
            cmbMethod.SelectedIndex = 0;
            cmbStatus.SelectedIndex = 0;
            gridPayments.ClearSelection();
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;

            btnRecord.Enabled = !busy;
            btnUpdate.Enabled = !busy;
            btnVoid.Enabled = !busy;
            btnClear.Enabled = !busy;
            btnRefresh.Enabled = !busy;
        }

        private static void ShowError(string context, Exception ex)
        {
            MessageBox.Show($"{context}:\n\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
