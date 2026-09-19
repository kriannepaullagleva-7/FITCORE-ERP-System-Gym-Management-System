using ERP_domain.entities;
using ERP_infrastructure.services;

namespace ERP_Project1
{
    // Member CRUD against the live TenantErp database. Every handler awaits the service
    // layer on the UI thread, so there is no cross-thread marshalling to do - the buttons
    // are simply disabled for the duration of the call.
    public partial class Form1 : Form
    {
        private readonly IMemberService _memberService;

        private int _selectedMemberId = -1;
        private bool _isBusy;

        public Form1(IMemberService memberService)
        {
            _memberService = memberService;
            InitializeComponent();
        }

        private async void Form1_Load(object? sender, EventArgs e)
        {
            await LoadMembersAsync();
        }

        private async Task LoadMembersAsync(string? searchTerm = null)
        {
            SetBusy(true);
            try
            {
                var members = await _memberService.GetAllMembersAsync();

                var term = searchTerm?.Trim();
                if (!string.IsNullOrEmpty(term))
                {
                    members = members.Where(m =>
                        Contains(m.FirstName, term) ||
                        Contains(m.LastName, term) ||
                        Contains(m.Email, term) ||
                        Contains(m.Phone, term))
                        .ToList();
                }

                Bind(members);

                var active = members.Count(m => m.Status == "Active");
                lblCount.Text = string.IsNullOrEmpty(term)
                    ? $"{members.Count} member(s) - {active} active"
                    : $"{members.Count} member(s) matching '{term}' - {active} active";
            }
            catch (Exception ex)
            {
                Bind(new List<Member>());
                lblCount.Text = "Members could not be loaded.";
                ShowError("Error loading members", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        // The grid is bound to a flat projection: binding Member directly would also surface
        // its Subscriptions, Sales and Payments collections as unusable columns.
        private void Bind(List<Member> members)
        {
            gridMembers.DataSource = members
                .Select(m => new
                {
                    m.MemberId,
                    First = m.FirstName,
                    Last = m.LastName,
                    m.Phone,
                    m.Email,
                    Joined = m.JoinDate,
                    m.Status
                })
                .ToList();

            if (gridMembers.Columns.Contains("Joined"))
                gridMembers.Columns["Joined"]!.DefaultCellStyle.Format = "dd MMM yyyy";

            gridMembers.ClearSelection();
            _selectedMemberId = -1;
        }

        private static bool Contains(string? value, string term)
            => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

        private void gridMembers_SelectionChanged(object? sender, EventArgs e)
        {
            // ClearSelection also raises SelectionChanged, and CurrentRow survives it - without
            // the Selected check the Clear button would immediately refill the form.
            if (gridMembers.CurrentRow is not { Selected: true }) return;
            if (gridMembers.CurrentRow.Cells["MemberId"].Value is not int memberId) return;

            var row = gridMembers.CurrentRow;

            _selectedMemberId = memberId;
            txtFirstName.Text = row.Cells["First"].Value?.ToString() ?? string.Empty;
            txtLastName.Text = row.Cells["Last"].Value?.ToString() ?? string.Empty;
            txtPhone.Text = row.Cells["Phone"].Value?.ToString() ?? string.Empty;
            txtEmail.Text = row.Cells["Email"].Value?.ToString() ?? string.Empty;

            var status = row.Cells["Status"].Value?.ToString() ?? "Active";
            cmbStatus.SelectedIndex = cmbStatus.Items.Contains(status)
                ? cmbStatus.Items.IndexOf(status)
                : 0;
        }

        private async void btnAdd_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;
            if (!TryReadForm(out var firstName, out var lastName, out var phone, out var email))
                return;

            var saved = false;

            SetBusy(true);
            try
            {
                var member = await _memberService.CreateMemberAsync(firstName, lastName, phone, email);

                MessageBox.Show($"Member created. ID: {member.MemberId}", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                ClearForm();
                saved = true;
            }
            catch (Exception ex)
            {
                ShowError("Error creating member", ex);
            }
            finally
            {
                SetBusy(false);
            }

            if (saved) await LoadMembersAsync();
        }

        private async void btnUpdate_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (_selectedMemberId == -1)
            {
                MessageBox.Show("Select a member in the grid to update.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryReadForm(out var firstName, out var lastName, out var phone, out var email))
                return;

            var status = cmbStatus.SelectedItem?.ToString() ?? "Active";

            var saved = false;

            SetBusy(true);
            try
            {
                var updated = await _memberService.UpdateMemberAsync(
                    _selectedMemberId, firstName, lastName, phone, email, status);

                if (updated == null)
                {
                    MessageBox.Show("That member no longer exists - the list has been refreshed.",
                        "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show("Member updated.", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                ClearForm();
                saved = true;
            }
            catch (Exception ex)
            {
                ShowError("Error updating member", ex);
            }
            finally
            {
                SetBusy(false);
            }

            if (saved) await LoadMembersAsync();
        }

        private async void btnDelete_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (_selectedMemberId == -1)
            {
                MessageBox.Show("Select a member in the grid to delete.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete member '{txtFirstName.Text} {txtLastName.Text}'?\n\n" +
                "Only a member with no subscriptions, payments or sales can be deleted. " +
                "Anyone with history is retired by setting their status to Inactive instead.",
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            var changed = false;

            SetBusy(true);
            try
            {
                var result = await _memberService.DeleteMemberAsync(_selectedMemberId);

                switch (result)
                {
                    case MemberDeleteResult.Deleted:
                        MessageBox.Show("Member deleted.", "Success",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        ClearForm();
                        changed = true;
                        break;

                    case MemberDeleteResult.HasHistory:
                        var history = await _memberService.GetMemberHistoryCountsAsync(_selectedMemberId);
                        MessageBox.Show(
                            $"This member has {history.Describe()} on record, so they cannot be deleted - " +
                            "removing them would take that history with them.\n\n" +
                            "Set their status to Inactive and press Update instead. They stop counting as " +
                            "active and everything they are attached to stays intact.",
                            "Member Has History", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        break;

                    default:
                        MessageBox.Show("That member no longer exists.", "Not Found",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        ClearForm();
                        changed = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                ShowError("Error deleting member", ex);
            }
            finally
            {
                SetBusy(false);
            }

            if (changed) await LoadMembersAsync();
        }

        private async void btnSearch_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;
            await LoadMembersAsync(txtSearch.Text);
        }

        private async void btnRefresh_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;
            txtSearch.Clear();
            ClearForm();
            await LoadMembersAsync();
        }

        private void btnClear_Click(object? sender, EventArgs e)
        {
            ClearForm();
        }

        private async void txtSearch_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;

            e.SuppressKeyPress = true;
            if (_isBusy) return;

            await LoadMembersAsync(txtSearch.Text);
        }

        private bool TryReadForm(out string firstName, out string lastName, out string phone, out string email)
        {
            firstName = txtFirstName.Text?.Trim() ?? string.Empty;
            lastName = txtLastName.Text?.Trim() ?? string.Empty;
            phone = txtPhone.Text?.Trim() ?? string.Empty;
            email = txtEmail.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                MessageBox.Show("First name and last name are both required.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (email.Length > 0 && (!email.Contains('@') || email.StartsWith('@') || email.EndsWith('@')))
            {
                MessageBox.Show("Enter a valid email address, or leave the field empty.",
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private void ClearForm()
        {
            _selectedMemberId = -1;
            txtFirstName.Clear();
            txtLastName.Clear();
            txtPhone.Clear();
            txtEmail.Clear();
            cmbStatus.SelectedIndex = 0;
            gridMembers.ClearSelection();
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            btnAdd.Enabled = !busy;
            btnUpdate.Enabled = !busy;
            btnDelete.Enabled = !busy;
            btnClear.Enabled = !busy;
            btnSearch.Enabled = !busy;
            btnRefresh.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

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
