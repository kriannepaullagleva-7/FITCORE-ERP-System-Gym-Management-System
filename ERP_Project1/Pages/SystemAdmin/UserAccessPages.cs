using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// User accounts for this company.
    ///
    /// A subfeature of System Administration, available on the Medium plan. Every request sends
    /// no company id: the server takes it from the caller's own token, so this screen can only
    /// ever manage accounts in the signed-in user's own gym, whatever id anybody puts in a URL.
    /// </summary>
    internal sealed class UsersPage : CrudPageBase<UserAccountDto>
    {
        private readonly Label _total;
        private readonly Label _totalHint;
        private readonly Label _active;
        private readonly Label _activeHint;
        private readonly Label _admins;
        private readonly Label _adminsHint;
        private readonly Label _neverSignedIn;
        private readonly Label _neverSignedInHint;

        private List<RoleDto> _roles = new();

        public UsersPage(FitCoreSession session)
            : base(session, "Users",
                   "Who can sign in to this company, and what they are allowed to reach.",
                   "user", "Username, name, email or role")
        {
            AddAction("Permissions", ButtonTone.Secondary, EditPermissionsAsync, 116);
            AddAction("Reset password", ButtonTone.Secondary, ResetPasswordAsync, 128);
            AddAction("Activate", ButtonTone.Secondary, ToggleActiveAsync, 92);

            StatsRow.Controls.Add(UiKit.StatCard("Accounts", out _total, out _totalHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("Active", out _active, out _activeHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Owners", out _admins, out _adminsHint, UiTheme.Info));
            StatsRow.Controls.Add(UiKit.StatCard("Never signed in", out _neverSignedIn, out _neverSignedInHint, UiTheme.Warning));
        }

        protected override bool SupportsDelete => false;

        protected override string EmptyHeadline => "No accounts yet";
        protected override string EmptyDetail =>
            "Add an account for each person who needs to sign in. What they can reach is " +
            "decided by their role, and adjusted per person under Permissions.";

        protected override async Task<List<UserAccountDto>?> FetchAsync()
        {
            if (_roles.Count == 0)
            {
                _roles = Unwrap(await Session.Users.GetRolesAsync()) ?? new List<RoleDto>();
            }

            var users = Unwrap(await Session.Users.GetAllAsync());

            if (users is not null)
            {
                _total.Text = users.Count.ToString("N0");
                _totalHint.Text = "in this company";

                _active.Text = users.Count(u => u.IsActive).ToString("N0");
                _activeHint.Text = "can sign in";

                _admins.Text = users.Count(u => u.RoleLevel <= 1).ToString("N0");
                _adminsHint.Text = "admin or above";

                var never = users.Count(u => u.LastLoginAt is null);
                _neverSignedIn.Text = never.ToString("N0");
                _neverSignedIn.ForeColor = never > 0 ? UiTheme.Warning : UiTheme.TextPrimary;
                _neverSignedInHint.Text = never == 0 ? "everyone has" : "still to set a password";
            }

            return users;
        }

        protected override void DefineColumns()
        {
            Column(nameof(UserAccountDto.Username), "Username", 110);
            Column(nameof(UserAccountDto.FullName), "Name", 150);
            Column(nameof(UserAccountDto.Email), "Email", 170);
            Column(nameof(UserAccountDto.RoleDisplayName), "Role", 120);
            Column(nameof(UserAccountDto.EmployeeName), "Employee", 120);
            Column(nameof(UserAccountDto.LastLoginAt), "Last signed in", 110, "d MMM yyyy HH:mm");
            FlagColumn(nameof(UserAccountDto.IsActive), "Status", 65);
        }

        protected override bool Matches(UserAccountDto u, string term) =>
            u.Username.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            u.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            u.Email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            u.RoleDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override Task<bool> OnAddAsync()
        {
            // A user can never be created at or above the creator's own level, which is what
            // stops a manager promoting themselves to owner. The server enforces it; the
            // dialog simply does not offer what would be refused.
            var level = Session.CurrentUser?.RoleLevel ?? 99;

            var roles = _roles
                .Where(r => r.HierarchyLevel > level)
                .Select(r => new KeyValuePair<string, string>(r.RoleKey, r.DisplayName))
                .ToList();

            if (roles.Count == 0)
            {
                ShowError("Your account cannot create users at any role below its own.");
                return Task.FromResult(false);
            }

            var saved = EditDialog.Run(this, "Add user",
                "The password set here is a first-login credential: the account is required to " +
                "change it the first time they sign in.",
                new List<FieldSpec>
                {
                    new("username", "Username")
                        { Required = true, MaxLength = 100, Hint = "Unique across the platform." },
                    new("fullName", "Full name") { Required = true, MaxLength = 200 },
                    new("email", "Email address", FieldKind.Email) { MaxLength = 200 },
                    new("password", "Temporary password", FieldKind.Password)
                    {
                        Required = true,
                        Validate = f => f.Text.Length < 8
                            ? "A password must be at least 8 characters long."
                            : null
                    },
                    new("role", "Role", FieldKind.Combo) { Required = true, Options = roles }
                },
                async f =>
                {
                    var result = await Session.Users.CreateAsync(new CreateUserRequestDto
                    {
                        Username = f.First(x => x.Key == "username").Text,
                        FullName = f.First(x => x.Key == "fullName").Text,
                        Email = f.First(x => x.Key == "email").Text,
                        Password = f.First(x => x.Key == "password").Text,
                        RoleKey = f.First(x => x.Key == "role").Text
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create user");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(UserAccountDto user)
        {
            var level = Session.CurrentUser?.RoleLevel ?? 99;

            var roles = _roles
                .Where(r => r.HierarchyLevel > level)
                .Select(r => new KeyValuePair<string, string>(r.RoleKey, r.DisplayName))
                .ToList();

            var fields = new List<FieldSpec>
            {
                new("username", "Username") { Value = user.Username, ReadOnly = true },
                new("fullName", "Full name")
                    { Required = true, Value = user.FullName, MaxLength = 200 },
                new("email", "Email address", FieldKind.Email)
                    { Value = user.Email, MaxLength = 200 },
                new("role", "Role", FieldKind.Combo)
                {
                    Value = user.RoleKey,
                    Options = roles,
                    ReadOnly = roles.Count == 0 || user.RoleLevel <= level,
                    Hint = user.RoleLevel <= level
                        ? "This account is at or above your own level, so its role is not yours to change."
                        : null
                }
            };

            var saved = EditDialog.Run(this, $"Edit {user.Username}", "", fields, async f =>
            {
                var update = await Session.Users.UpdateAsync(user.AppUserId, new UpdateUserRequestDto
                {
                    FullName = f.First(x => x.Key == "fullName").Text,
                    Email = f.First(x => x.Key == "email").Text,
                    EmployeeId = user.EmployeeId
                });

                if (!update.IsSuccess) return update.ErrorMessage;

                var role = f.First(x => x.Key == "role").ComboValue;

                if (!string.IsNullOrWhiteSpace(role) && role != user.RoleKey)
                {
                    var changed = await Session.Users.SetRoleAsync(user.AppUserId, role);
                    if (!changed.IsSuccess) return changed.ErrorMessage;
                }

                return null;
            }, "Save changes");

            return Task.FromResult(saved);
        }

        /// <summary>
        /// Changes one person's module access, through the same editor the Permissions tab
        /// opens - so a permission is only ever changed in one place.
        /// </summary>
        private async Task EditPermissionsAsync()
        {
            var user = Selected;

            if (user is null)
            {
                ShowError("Select a user to change their access.");
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

        private async Task ResetPasswordAsync()
        {
            var user = Selected;

            if (user is null)
            {
                ShowError("Select a user whose password should be reset.");
                return;
            }

            var reset = EditDialog.Run(this, $"Reset {user.Username}'s password",
                "They will be required to choose their own the next time they sign in.",
                new List<FieldSpec>
                {
                    new("password", "Temporary password", FieldKind.Password)
                    {
                        Required = true,
                        Validate = f => f.Text.Length < 8
                            ? "A password must be at least 8 characters long."
                            : null
                    }
                },
                async f =>
                {
                    var result = await Session.Users.ResetPasswordAsync(
                        user.AppUserId, f.First(x => x.Key == "password").Text);

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Reset password");

            if (reset) Notify($"{user.Username}'s password has been reset.");
        }

        private async Task ToggleActiveAsync()
        {
            var user = Selected;

            if (user is null)
            {
                ShowError("Select a user to activate or deactivate.");
                return;
            }

            var activating = !user.IsActive;

            if (!activating && !UiKit.ConfirmDelete(this,
                    $"Deactivate {user.FullName}?",
                    "They will be refused at sign-in immediately. Their history and everything " +
                    "they recorded is untouched, and the account can be reactivated later."))
            {
                return;
            }

            await GuardAsync(async () =>
            {
                var result = await Session.Users.SetStatusAsync(user.AppUserId, activating);

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadAsync();
                Notify($"{user.FullName} has been {(activating ? "reactivated" : "deactivated")}.");
            }, "Saving…");
        }
    }

    /// <summary>
    /// Roles: what each one grants before any per-user decision is applied.
    ///
    /// Read-only here on purpose. A role's default module set is a product decision that
    /// reaches every user holding it, and the per-user editor is the right place to depart
    /// from it - changing a role to suit one person is how an access model stops meaning
    /// anything.
    /// </summary>
    internal sealed class RolesPage : CrudPageBase<RoleDto>
    {
        public RolesPage(FitCoreSession session)
            : base(session, "Roles",
                   "What each role grants by default. Per-person exceptions live under Users → Permissions.",
                   "role", "Role or module")
        {
            AddAction("Modules", ButtonTone.Secondary, ShowModulesAsync, 92);
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string EmptyHeadline => "No roles found";
        protected override string EmptyDetail =>
            "FitCore ships with four roles. If this is empty, the master database may not be reachable.";

        protected override async Task<List<RoleDto>?> FetchAsync() =>
            Unwrap(await Session.Users.GetRolesAsync());

        protected override void DefineColumns()
        {
            Column(nameof(RoleDto.DisplayName), "Role", 160);
            Column(nameof(RoleDto.RoleKey), "Key", 100);
            Column(nameof(RoleDto.HierarchyLevel), "Level", 60, rightAlign: true);
            Column(nameof(RoleDto.DefaultModules), "Grants", 300);
        }

        protected override bool Matches(RoleDto r, string term) =>
            r.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            r.RoleKey.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            r.DefaultModules.Any(m => m.Contains(term, StringComparison.OrdinalIgnoreCase));

        protected override void AfterLoad()
        {
            // The grid binds a List<string>, which renders as the type name. Flattening it
            // into a readable sentence is what makes the column worth having.
            Grid.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                if (Grid.Columns[e.ColumnIndex].Name != nameof(RoleDto.DefaultModules)) return;

                if (e.Value is List<string> modules)
                {
                    e.Value = modules.Count == 0 ? "—" : string.Join(", ", modules);
                    e.FormattingApplied = true;
                }
            };
        }

        private Task ShowModulesAsync()
        {
            var role = Selected;

            if (role is null)
            {
                ShowError("Select a role to see what it grants.");
                return Task.CompletedTask;
            }

            ListDialog.Show(this,
                $"{role.DisplayName} · default access",
                $"Level {role.HierarchyLevel}. The company's plan is applied on top of this, " +
                "so a module here is still withheld if the tier does not include it.",
                role.DefaultModules.Select(m => new { Module = m }).ToList());

            return Task.CompletedTask;
        }
    }
}
