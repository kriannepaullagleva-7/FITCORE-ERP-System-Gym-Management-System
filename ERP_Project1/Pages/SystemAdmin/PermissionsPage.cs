using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Who can reach what, as a matrix of every account against the nine modules.
    ///
    /// Users → Permissions already edits one person's access. This answers the opposite
    /// question - "who can reach Payroll?" - which a per-person dialog cannot, because it would
    /// mean opening every account in turn and remembering the answers.
    ///
    /// The matrix is built from the effective module list the server already sends with each
    /// account, so the whole screen is one request rather than one per user. What it shows is
    /// therefore the same answer the API enforces, not a second calculation of it: the desktop
    /// holds no copy of the tier or role rules.
    ///
    /// Editing still goes through the same dialog Users uses, so there is one place where a
    /// permission is actually changed.
    /// </summary>
    internal sealed class PermissionsPage : CrudPageBase<UserAccountDto>
    {
        /// <summary>
        /// The nine modules, in catalogue order, with the column header each gets.
        ///
        /// Abbreviated because nine full module names will not fit across a grid, and a header
        /// nobody can read is worse than a short one. The module key is what the lookup uses,
        /// so the abbreviation is presentation only.
        /// </summary>
        private static readonly (string Key, string Header)[] Columns =
        {
            (Modules.Membership, "Member"),
            (Modules.Payments, "Pay"),
            (Modules.Sales, "Sales"),
            (Modules.Inventory, "Stock"),
            (Modules.Employees, "Staff"),
            (Modules.Payroll, "Payroll"),
            (Modules.Finance, "Finance"),
            (Modules.SystemAdmin, "Admin"),
            (Modules.BusinessIntelligence, "Insight")
        };

        private readonly ComboBox _module;
        private readonly Label _summary;

        public PermissionsPage(FitCoreSession session)
            : base(session, "Permissions",
                   "Who can reach what. Per-person exceptions to a role, within the company's plan.",
                   "account", "Name, username or role")
        {
            AddAction("Edit access", ButtonTone.Primary, () => EditAsync(), 116);

            FilterBar.Controls.Add(UiKit.FilterLabel("Holds"));

            _module = UiKit.Select(190);
            _module.DisplayMember = "Value";
            _module.ValueMember = "Key";
            _module.DataSource = new List<KeyValuePair<string, string>>
                {
                    new("", "Any module")
                }
                .Concat(Columns.Select(c => new KeyValuePair<string, string>(c.Key, c.Header)))
                .ToList();
            _module.SelectedIndexChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_module);

            _summary = new Label
            {
                AutoSize = true,
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                Margin = new Padding(16, 10, 0, 0),
                UseMnemonic = false
            };
            FilterBar.Controls.Add(_summary);

            // Painted on every rebind rather than after a load, because searching re-binds the
            // grid without reloading - and cells filled only after a load would blank out the
            // moment somebody typed in the search box.
            Grid.DataBindingComplete += (_, _) => PaintMatrix();
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string EmptyHeadline => "No accounts to show";
        protected override string EmptyDetail =>
            "Accounts are created under Users. Clear the module filter to see everybody.";

        protected override async Task<List<UserAccountDto>?> FetchAsync()
        {
            var all = Unwrap(await Session.Users.GetAllAsync());
            if (all is null) return null;

            var wanted = _module.SelectedValue?.ToString();

            var rows = string.IsNullOrWhiteSpace(wanted)
                ? all
                : all.Where(u => Holds(u, wanted)).ToList();

            // Most senior first, then by name: the people with the widest access are the ones
            // an access review is about.
            return rows
                .OrderBy(u => u.RoleLevel)
                .ThenBy(u => u.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static bool Holds(UserAccountDto user, string moduleKey) =>
            user.Modules.Any(m => string.Equals(m, moduleKey, StringComparison.OrdinalIgnoreCase));

        protected override void DefineColumns()
        {
            Column(nameof(UserAccountDto.FullName), "Name", 150);
            Column(nameof(UserAccountDto.Username), "Username", 110);
            Column(nameof(UserAccountDto.RoleDisplayName), "Role", 110);
            FlagColumn(nameof(UserAccountDto.IsActive), "Active", 55);

            // One column per module, filled in AfterLoad. They are unbound because the effective
            // list is a collection on the row rather than nine properties, and the grid binds to
            // properties.
            foreach (var (key, header) in Columns)
            {
                var column = new DataGridViewTextBoxColumn
                {
                    Name = $"module_{key}",
                    HeaderText = header,
                    FillWeight = 58,
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                    DefaultCellStyle = new DataGridViewCellStyle
                    {
                        Alignment = DataGridViewContentAlignment.MiddleCenter,
                        Font = UiTheme.BodyStrong
                    }
                };

                Grid.Columns.Add(column);
            }
        }

        protected override bool Matches(UserAccountDto u, string term) =>
            (u.FullName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (u.Username ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (u.Email ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (u.RoleDisplayName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override void AfterLoad()
        {
            var counts = Columns
                .Select(c => $"{c.Header} {Items.Count(u => Holds(u, c.Key)):N0}")
                .ToList();

            _summary.Text = $"{Items.Count:N0} account(s)      " + string.Join("   ", counts);
        }

        /// <summary>
        /// Fills the nine module cells for each drawn row.
        ///
        /// A tick means the account can reach the module today - role, per-person override and
        /// the company's plan already resolved by the server. A blank rather than a cross keeps
        /// the eye on what is granted, which is what an access review is looking for.
        /// </summary>
        private void PaintMatrix()
        {
            // The module columns only exist once DefineColumns has run, and the first bind
            // happens before that on a page whose first load fails.
            if (!Grid.Columns.Contains($"module_{Columns[0].Key}")) return;

            foreach (DataGridViewRow row in Grid.Rows)
            {
                if (row.DataBoundItem is not UserAccountDto user) continue;

                foreach (var (key, _) in Columns)
                {
                    var cell = row.Cells[$"module_{key}"];
                    var held = Holds(user, key);

                    cell.Value = held ? "✓" : "";
                    cell.Style.ForeColor = held ? UiTheme.Success : UiTheme.TextMuted;
                }
            }
        }

        /// <summary>
        /// Opens the same editor Users uses, so a permission is only ever changed in one place.
        /// </summary>
        private async Task EditAsync()
        {
            var user = Selected;

            if (user is null)
            {
                ShowError("Select an account to change its access.");
                return;
            }

            var changed = await UserAccessEditor.EditModulesAsync(this, Session, user.AppUserId, ShowError);
            if (!changed) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"{user.FullName}'s access has been updated.");
            }, "Refreshing…");
        }
    }
}
