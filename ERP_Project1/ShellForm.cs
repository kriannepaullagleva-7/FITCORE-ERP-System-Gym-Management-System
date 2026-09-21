using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The FitCore desktop shell: branded sidebar, topbar and a content host.
    ///
    /// The sidebar lists the modules the signed-in user actually holds, so a Micro tenant
    /// never sees Employees or Payroll - and when a Small tenant does see them, they sit in
    /// OPERATIONS beside Membership, Payments, Sales and Inventory, because for that tenant
    /// they are ordinary day-to-day work rather than a bolt-on.
    ///
    /// All of that is presentation. Every screen behind it calls ERP_api, which re-checks and
    /// answers 403 regardless of what was drawn here.
    /// </summary>
    internal sealed class ShellForm : Form
    {
        /// <summary>A module in the sidebar, and the submodules it opens onto.</summary>
        private sealed record ModuleEntry(
            string Key,
            string Label,
            string Glyph,
            string RequiredModule,
            string Title,
            string Description,
            Func<ShellForm, WorkspaceTab[]> Tabs);

        private sealed record NavGroup(string Title, ModuleEntry[] Modules);

        private const int SidebarWidth = 236;

        private readonly FitCoreSession _session;
        private readonly Panel _content;
        private readonly Panel _sidebar;
        private readonly Dictionary<string, Button> _navButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ModuleWorkspace> _workspaces = new(StringComparer.OrdinalIgnoreCase);

        private Label _crumb = null!;
        private Label _crumbHint = null!;
        private ModuleWorkspace? _current;
        private string? _currentKey;
        private bool _expiryHandled;

        public ShellForm(FitCoreSession session)
        {
            _session = session;

            Text = "FitCore ERP";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1200, 740);
            ClientSize = new Size(1420, 860);
            BackColor = UiTheme.Canvas;
            WindowState = FormWindowState.Maximized;
            DoubleBuffered = true;

            _sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = SidebarWidth,
                BackColor = UiTheme.Navy,
                AutoScroll = true
            };

            var topbar = BuildTopBar();

            _content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Canvas
            };

            Controls.Add(_content);
            Controls.Add(topbar);
            Controls.Add(_sidebar);

            BuildSidebar();

            _session.SessionExpired += OnSessionExpired;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Permissions are re-read from the server on open, so a module withdrawn by an
            // administrator since the token was minted is reflected immediately.
            await _session.RefreshCurrentUserAsync();
            BuildSidebar();

            if (_session.CurrentUser?.MustChangePassword == true)
            {
                PromptPasswordChange();
            }

            var first = Catalogue()
                .SelectMany(g => g.Modules)
                .FirstOrDefault(m => _session.Can(m.RequiredModule));

            if (first is null)
            {
                ShowNoModules();
                return;
            }

            await NavigateAsync(first.Key);
        }

        // ------------------------------------------------------------------ catalogue

        /// <summary>
        /// The navigation, exactly as the tiers define it.
        ///
        /// Employees and Payroll are listed inside OPERATIONS rather than in a group of their
        /// own: whether they appear at all is the tier's decision, but once they do they are
        /// peers of the other four operational modules.
        /// </summary>
        private NavGroup[] Catalogue() => new[]
        {
            new NavGroup("MAIN", new[]
            {
                new ModuleEntry("dashboard", "Dashboard", "◆", Modules.Dashboard,
                    "Dashboard", "Today's figures for your gym, straight from the tenant database.",
                    s => new[]
                    {
                        new WorkspaceTab("overview", "Overview",
                            () => new DashboardPage(s._session, s.DashboardActionAsync))
                    })
            }),

            new NavGroup("OPERATIONS", new[]
            {
                new ModuleEntry("membership", "Membership", "●", Modules.Membership,
                    "Membership", "Manage members, plans and subscriptions.",
                    s => new[]
                    {
                        new WorkspaceTab("members", "Members", () => new MembersPage(s._session)),
                        new WorkspaceTab("plans", "Membership Plans", () => new PlansPage(s._session)),
                        new WorkspaceTab("subscriptions", "Subscriptions", () => new SubscriptionsPage(s._session))
                    }),

                new ModuleEntry("payments", "Payments", "▮", Modules.Payments,
                    "Payments", "Record what has been received and track what is still owed.",
                    s => new[]
                    {
                        new WorkspaceTab("transactions", "Payment Transactions", () => new PaymentsPage(s._session))
                    }),

                new ModuleEntry("sales", "Sales", "▲", Modules.Sales,
                    "Sales", "Ring up a sale, review the day's transactions and keep customer records.",
                    s => new[]
                    {
                        new WorkspaceTab("pos", "New Sale", () => new PosPage(s._session)),
                        new WorkspaceTab("history", "Sales History", () => new SalesPage(s._session)),
                        new WorkspaceTab("customers", "Customers", () => new CustomersPage(s._session))
                    }),

                new ModuleEntry("inventory", "Inventory", "▣", Modules.Inventory,
                    "Inventory", "Products, stock on hand and the suppliers you buy from.",
                    s => new[]
                    {
                        new WorkspaceTab("products", "Products", () => new ProductsPage(s._session)),
                        new WorkspaceTab("stock", "Stock", () => new InventoryPage(s._session)),
                        new WorkspaceTab("suppliers", "Suppliers", () => new SuppliersPage(s._session))
                    }),

                new ModuleEntry("employees", "Employees", "◍", Modules.Employees,
                    "Employees", "Your staff, and the salary payroll is calculated from.",
                    s => new[]
                    {
                        new WorkspaceTab("records", "Employee Records", () => new EmployeesPage(s._session))
                    }),

                new ModuleEntry("payroll", "Payroll", "◈", Modules.Payroll,
                    "Payroll", "Pay runs, with gross and net calculated by the server.",
                    s => new[]
                    {
                        new WorkspaceTab("records", "Payroll Records", () => new PayrollPage(s._session))
                    })
            }),

            new NavGroup("INSIGHT", new[]
            {
                new ModuleEntry("reports", "Reports", "▤", Modules.Reports,
                    "Reports", "Sales, payments, inventory and membership, filtered by date.",
                    s => new[]
                    {
                        new WorkspaceTab("reports", "Reports", () => new ReportsPage(s._session))
                    })
            })
        };

        private ModuleEntry? Find(string key) =>
            Catalogue().SelectMany(g => g.Modules)
                       .FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ sidebar

        private void BuildSidebar()
        {
            _sidebar.SuspendLayout();
            _sidebar.Controls.Clear();
            _navButtons.Clear();

            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                BackColor = UiTheme.Navy,
                Padding = new Padding(0, 0, 0, 18)
            };

            stack.Controls.Add(BuildBrand());

            foreach (var group in Catalogue())
            {
                var permitted = group.Modules.Where(m => _session.Can(m.RequiredModule)).ToArray();
                if (permitted.Length == 0) continue;   // the whole group is above this tier

                stack.Controls.Add(GroupLabel(group.Title));

                foreach (var module in permitted)
                {
                    var button = NavButton(module.Label, module.Glyph);
                    button.Tag = module.Key;
                    button.Click += async (_, _) => await NavigateAsync(module.Key);

                    _navButtons[module.Key] = button;
                    stack.Controls.Add(button);
                }
            }

            // ACCOUNT is always present: signing out is not a module and is never withheld.
            stack.Controls.Add(GroupLabel("ACCOUNT"));

            var password = NavButton("Change Password", "⚿");
            password.Click += (_, _) => PromptPasswordChange();
            stack.Controls.Add(password);

            var signOut = NavButton("Sign Out", "⏻");
            signOut.Click += async (_, _) => await SignOutAsync();
            stack.Controls.Add(signOut);

            _sidebar.Controls.Add(stack);
            _sidebar.ResumeLayout();

            Highlight(_currentKey);
        }

        private Control BuildBrand()
        {
            var brand = new Panel
            {
                Width = SidebarWidth,
                Height = 98,
                BackColor = UiTheme.Navy,
                Margin = new Padding(0)
            };

            brand.Paint += (_, e) =>
            {
                // A keyline under the wordmark separates brand from navigation without
                // spending a whole row of empty space on it.
                using var pen = new Pen(UiTheme.NavyRaised);
                e.Graphics.DrawLine(pen, 18, brand.Height - 1, SidebarWidth - 18, brand.Height - 1);
            };

            brand.Controls.Add(new Label
            {
                Text = "FitCore",
                Font = new Font(UiTheme.FamilySemibold, 17F),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(20, 26),
                BackColor = Color.Transparent,
                UseMnemonic = false
            });

            brand.Controls.Add(new Label
            {
                Text = "ERP SYSTEM",
                Font = new Font(UiTheme.FamilySemibold, 7.5F),
                ForeColor = UiTheme.TextOnDarkMuted,
                AutoSize = true,
                Location = new Point(22, 54),
                BackColor = Color.Transparent,
                UseMnemonic = false
            });

            var tier = (_session.CurrentUser?.EnterpriseTier ?? "").ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(tier))
            {
                var chip = new Label
                {
                    Text = tier,
                    Font = new Font(UiTheme.FamilySemibold, 7F),
                    ForeColor = Color.White,
                    AutoSize = false,
                    Size = new Size(58, 20),
                    Location = new Point(SidebarWidth - 80, 30),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.Transparent,
                    UseMnemonic = false
                };
                chip.Paint += (_, e) =>
                {
                    UiTheme.PaintCard(e.Graphics, new Rectangle(0, 0, chip.Width, chip.Height),
                        UiTheme.NavyActive, null, 10);
                    TextRenderer.DrawText(e.Graphics, chip.Text, chip.Font,
                        new Rectangle(0, 0, chip.Width, chip.Height), Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                brand.Controls.Add(chip);
            }

            return brand;
        }

        private static Label GroupLabel(string text) => new()
        {
            Text = "   " + text,
            Font = new Font(UiTheme.FamilySemibold, 7F),
            ForeColor = UiTheme.TextOnDarkMuted,
            AutoSize = false,
            Size = new Size(SidebarWidth, 26),
            Margin = new Padding(0, 16, 0, 4),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = UiTheme.Navy,
            UseMnemonic = false
        };

        private static Button NavButton(string label, string glyph)
        {
            var button = new Button
            {
                Text = "     " + glyph + "    " + label,
                AutoSize = false,
                Size = new Size(SidebarWidth, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = UiTheme.Navy,
                ForeColor = UiTheme.TextOnDark,
                Font = UiTheme.Body,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand,
                Margin = new Padding(0),
                TabStop = true,
                UseVisualStyleBackColor = false
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = UiTheme.NavyRaised;
            button.FlatAppearance.MouseDownBackColor = UiTheme.NavyRaised;

            return button;
        }

        private void Highlight(string? key)
        {
            foreach (var (moduleKey, button) in _navButtons)
            {
                var isActive = string.Equals(moduleKey, key, StringComparison.OrdinalIgnoreCase);

                button.BackColor = isActive ? UiTheme.NavyActive : UiTheme.Navy;
                button.ForeColor = isActive ? Color.White : UiTheme.TextOnDark;
                button.Font = isActive ? UiTheme.BodyStrong : UiTheme.Body;
                button.FlatAppearance.MouseOverBackColor =
                    isActive ? UiTheme.NavyActive : UiTheme.NavyRaised;
            }
        }

        // ------------------------------------------------------------------ topbar

        private Panel BuildTopBar()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = UiTheme.Surface };
            bar.Paint += (_, e) =>
            {
                using var pen = new Pen(UiTheme.Border);
                e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            };

            _crumb = new Label
            {
                Text = "FitCore ERP",
                Font = new Font(UiTheme.FamilySemibold, 11.5F),
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(24, 15),
                UseMnemonic = false
            };

            _crumbHint = new Label
            {
                Text = "",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Location = new Point(25, 37),
                UseMnemonic = false
            };

            var user = _session.CurrentUser;

            var identity = new Label
            {
                Text = user is null ? "" : $"{user.FullName}",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                UseMnemonic = false
            };

            var role = new Label
            {
                Text = user is null ? "" : $"{user.RoleDisplayName} · {user.CompanyName}",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                UseMnemonic = false
            };

            var avatar = new Label
            {
                Text = user?.Initials ?? "FC",
                Font = new Font(UiTheme.FamilySemibold, 10F),
                ForeColor = Color.White,
                AutoSize = false,
                Size = new Size(38, 38),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = UiTheme.Surface,
                UseMnemonic = false
            };
            avatar.Paint += (_, e) =>
            {
                UiTheme.PaintCard(e.Graphics, new Rectangle(0, 0, avatar.Width, avatar.Height),
                    UiTheme.Primary, null, avatar.Width / 2);
                TextRenderer.DrawText(e.Graphics, avatar.Text, avatar.Font,
                    new Rectangle(0, 0, avatar.Width, avatar.Height), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            void Reflow()
            {
                avatar.Location = new Point(bar.Width - 62, 14);
                identity.Location = new Point(bar.Width - 74 - identity.Width, 14);
                role.Location = new Point(bar.Width - 74 - role.Width, 34);
            }

            bar.Resize += (_, _) => Reflow();

            bar.Controls.Add(_crumb);
            bar.Controls.Add(_crumbHint);
            bar.Controls.Add(identity);
            bar.Controls.Add(role);
            bar.Controls.Add(avatar);

            Reflow();
            return bar;
        }

        // ------------------------------------------------------------------ navigation

        private async Task NavigateAsync(string moduleKey, string? tabKey = null)
        {
            var entry = Find(moduleKey);
            if (entry is null) return;

            // Belt and braces: even if a button were somehow drawn, refuse to open a module
            // this user does not hold. The API refuses it too.
            if (!_session.Can(entry.RequiredModule))
            {
                UiKit.Error($"Your account does not include {entry.Label}.", "Access denied");
                return;
            }

            if (!_workspaces.TryGetValue(entry.Key, out var workspace))
            {
                workspace = new ModuleWorkspace(entry.Tabs(this));
                workspace.NavigationRequested += async (m, t) => await NavigateAsync(m, t);
                _workspaces[entry.Key] = workspace;
            }

            _currentKey = entry.Key;
            _crumb.Text = entry.Title;
            _crumbHint.Text = entry.Description;
            Highlight(entry.Key);

            _content.SuspendLayout();
            foreach (Control child in _content.Controls) child.Visible = false;
            if (!_content.Controls.Contains(workspace)) _content.Controls.Add(workspace);
            workspace.Visible = true;
            workspace.BringToFront();
            _content.ResumeLayout();

            _current = workspace;

            await workspace.OpenAsync(tabKey);
        }

        /// <summary>
        /// Opens a module and immediately starts its "create" flow. This is what the
        /// dashboard's quick actions do, so an operator is two clicks from a new member.
        /// </summary>
        private async Task QuickActionAsync(string moduleKey, string tabKey)
        {
            await NavigateAsync(moduleKey, tabKey);

            if (_current?.ActivePage is IQuickAddPage page)
            {
                await page.QuickAddAsync();
            }
        }

        /// <summary>
        /// What a dashboard quick action does: open the module and, for a "create" action,
        /// start its add flow straight away.
        /// </summary>
        private Task DashboardActionAsync(string moduleKey, string tabKey, bool startCreate) =>
            startCreate ? QuickActionAsync(moduleKey, tabKey) : NavigateAsync(moduleKey, tabKey);

        private void ShowNoModules()
        {
            _content.Controls.Clear();
            _content.Controls.Add(UiKit.EmptyState(
                "No modules are enabled for your account",
                "Ask an administrator to grant you access to at least one module, then sign in again."));
        }

        // ------------------------------------------------------------------ account

        private void PromptPasswordChange()
        {
            var fields = new List<FieldSpec>
            {
                new("current", "Current password", FieldKind.Password) { Required = true },
                new("next", "New password", FieldKind.Password)
                {
                    Required = true,
                    Hint = "At least 8 characters.",
                    Validate = f => f.Text.Length < 8
                        ? "The new password must be at least 8 characters."
                        : null
                },
                new("confirm", "Confirm new password", FieldKind.Password) { Required = true }
            };

            var changed = EditDialog.Run(this, "Change your password",
                _session.CurrentUser?.MustChangePassword == true
                    ? "You are signed in with a password that was set for you. Choose your own to continue."
                    : "Choose a new password for your FitCore account.",
                fields,
                async f =>
                {
                    var current = f.First(x => x.Key == "current").Text;
                    var next = f.First(x => x.Key == "next").Text;
                    var confirm = f.First(x => x.Key == "confirm").Text;

                    if (next != confirm) return "The two new passwords do not match.";
                    if (next == current) return "The new password must be different from the current one.";

                    var result = await _session.ChangePasswordAsync(current, next);
                    return result.IsSuccess ? null : result.ErrorMessage ?? "Could not change the password.";
                },
                "Change password");

            if (changed) UiKit.Success("Password changed successfully.");
        }

        private void OnSessionExpired()
        {
            if (_expiryHandled) return;
            _expiryHandled = true;

            // The 401 arrives on a background thread; the message box and the close must not.
            void Handle()
            {
                UiKit.Info(ApiErrorText.SessionExpired, "Signed out");
                DialogResult = DialogResult.Retry;   // Program shows the sign-in window again
                Close();
            }

            if (InvokeRequired) BeginInvoke(Handle);
            else Handle();
        }

        private async Task SignOutAsync()
        {
            if (UiKit.Confirm("Sign out of FitCore?\r\n\r\nAny unsaved work in an open dialog will be lost.")
                != DialogResult.Yes) return;

            await _session.SignOutAsync();
            DialogResult = DialogResult.Retry;   // Program treats this as "show the login form again"
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _session.SessionExpired -= OnSessionExpired;

                foreach (var workspace in _workspaces.Values) workspace.Dispose();
                _workspaces.Clear();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// The dashboard asks for these so its quick-action buttons can open the right module
        /// and start the right flow. Exposed as a method rather than a field so the dashboard
        /// never holds a reference to the shell.
        /// </summary>
        internal Func<string, string, Task> QuickAction => QuickActionAsync;

        internal Func<string, string?, Task> Navigate => NavigateAsync;
    }
}
