using System.Drawing;

namespace ERP_Project1
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        private Label lblCount;

        private TextBox txtFirstName;
        private TextBox txtLastName;
        private TextBox txtPhone;
        private TextBox txtEmail;
        private TextBox txtSearch;

        private ComboBox cmbStatus;

        private Button btnAdd;
        private Button btnUpdate;
        private Button btnDelete;
        private Button btnClear;
        private Button btnRefresh;
        private Button btnSearch;

        private DataGridView gridMembers;

        private GroupBox grpMemberForm;
        private GroupBox grpSearch;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        // The form is docked into the navigation shell, so the layout is built from a docked
        // header panel over a filling grid. Absolute sizes measured against a standalone
        // 1200x700 window would be clipped once the form is hosted.
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            this.SuspendLayout();

            var heading = UiTheme.CreateHeading("Members", 16, 10, 300);

            // Search
            grpSearch = new GroupBox
            {
                Text = "Search Members",
                Location = new Point(16, 40),
                Size = new Size(1000, 62),
                BackColor = UiTheme.PaleBlue,
                ForeColor = UiTheme.TextBlue,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                TabStop = false
            };

            grpSearch.Controls.Add(UiTheme.CreateLabel("Search:", 16, 27, 55));
            txtSearch = new TextBox { Location = new Point(76, 24), Size = new Size(260, 25) };
            txtSearch.KeyDown += txtSearch_KeyDown;
            grpSearch.Controls.Add(txtSearch);

            btnSearch = UiTheme.CreateButton("Search", 350, 22, UiTheme.Primary, btnSearch_Click);
            btnRefresh = UiTheme.CreateButton("Refresh", 468, 22, UiTheme.PrimaryDark, btnRefresh_Click);
            grpSearch.Controls.Add(btnSearch);
            grpSearch.Controls.Add(btnRefresh);

            grpSearch.Controls.Add(UiTheme.CreateLabel(
                "Searches name, email and phone. An empty search shows everyone.", 600, 27, 380));

            // Member details
            grpMemberForm = new GroupBox
            {
                Text = "Member Information",
                Location = new Point(16, 110),
                Size = new Size(1000, 134),
                BackColor = UiTheme.PaleBlue,
                ForeColor = UiTheme.TextBlue,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                TabStop = false
            };

            grpMemberForm.Controls.Add(UiTheme.CreateLabel("First Name:", 16, 32, 76));
            txtFirstName = new TextBox { Location = new Point(96, 29), Size = new Size(180, 25) };
            grpMemberForm.Controls.Add(txtFirstName);

            grpMemberForm.Controls.Add(UiTheme.CreateLabel("Last Name:", 288, 32, 74));
            txtLastName = new TextBox { Location = new Point(366, 29), Size = new Size(180, 25) };
            grpMemberForm.Controls.Add(txtLastName);

            grpMemberForm.Controls.Add(UiTheme.CreateLabel("Phone:", 558, 32, 50));
            txtPhone = new TextBox { Location = new Point(612, 29), Size = new Size(160, 25) };
            grpMemberForm.Controls.Add(txtPhone);

            grpMemberForm.Controls.Add(UiTheme.CreateLabel("Email:", 16, 68, 76));
            txtEmail = new TextBox { Location = new Point(96, 65), Size = new Size(180, 25) };
            grpMemberForm.Controls.Add(txtEmail);

            grpMemberForm.Controls.Add(UiTheme.CreateLabel("Status:", 288, 68, 74));
            cmbStatus = new ComboBox
            {
                Location = new Point(366, 65),
                Size = new Size(180, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbStatus.Items.AddRange(new object[] { "Active", "Inactive", "Suspended" });
            cmbStatus.SelectedIndex = 0;
            grpMemberForm.Controls.Add(cmbStatus);

            grpMemberForm.Controls.Add(UiTheme.CreateLabel(
                "Select a row in the grid to load it here, then Update or Delete.", 558, 68, 420));

            btnAdd = UiTheme.CreateButton("Add New", 96, 96, UiTheme.Success, btnAdd_Click);
            btnUpdate = UiTheme.CreateButton("Update", 214, 96, UiTheme.Warning, btnUpdate_Click);
            btnDelete = UiTheme.CreateButton("Delete", 332, 96, UiTheme.Danger, btnDelete_Click);
            btnClear = UiTheme.CreateButton("Clear", 450, 96, UiTheme.Neutral, btnClear_Click);

            grpMemberForm.Controls.Add(btnAdd);
            grpMemberForm.Controls.Add(btnUpdate);
            grpMemberForm.Controls.Add(btnDelete);
            grpMemberForm.Controls.Add(btnClear);

            lblCount = UiTheme.CreateLabel("", 18, 252, 700);
            lblCount.ForeColor = UiTheme.Neutral;

            // Grid
            gridMembers = UiTheme.CreateGrid(0, 0, 100, 100);
            gridMembers.Dock = DockStyle.Fill;
            gridMembers.SelectionChanged += gridMembers_SelectionChanged;

            var header = UiTheme.CreateHeaderPanel(276);
            header.Controls.Add(heading);
            header.Controls.Add(grpSearch);
            header.Controls.Add(grpMemberForm);
            header.Controls.Add(lblCount);

            var body = UiTheme.CreateBodyPanel();
            body.Controls.Add(gridMembers);

            this.Controls.Add(body);
            this.Controls.Add(header);

            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(1040, 700);
            this.Name = "Form1";
            this.Text = "Member Management";
            this.BackColor = UiTheme.Canvas;
            this.Load += Form1_Load;

            this.ResumeLayout(false);
        }
    }
}
