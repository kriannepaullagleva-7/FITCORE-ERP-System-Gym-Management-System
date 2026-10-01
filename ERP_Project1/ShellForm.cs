using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The FitCore desktop shell: branded sidebar, topbar and a content host.
    ///
    /// The sidebar lists the nine FitCore business modules - Membership, Payment, Sales,
    /// Inventory, Employee, Payroll, Finance, System Administration and Business Intelligence -
    /// and nothing else. Everything the product does sits underneath one of them as a tab, so
    /// the sidebar stays the shape of the business rather than growing an entry per screen.
    ///
    /// Two independent narrowings decide what is drawn. A module appears only if the signed-in
    /// user holds it, which the tier and their role together decide; and a tab inside it
    /// appears only if they hold that subfeature, which is how one catalogue serves a Micro
    /// gym's four screens and a Medium tenant's general ledger. Both lists arrive from the
    /// server on <c>/api/auth/me</c> - the desktop holds no copy of the licensing rules.
    ///
    /// All of that is presentation. Every screen behind it calls ERP_api, which re-checks and
    /// answers 403 regardless of what was drawn here.
    /// </summary>
    internal sealed class ShellForm : Form
    {
        /// <summary>A module in the sidebar, and the subfeatures it opens onto.</summary>
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
        private ComboBox _branchPicker = null!;
        private Label _branchLabel = null!;
        private ModuleWorkspace? _current;
        private string? _currentKey;
        private bool _expiryHandled;
        private bool _switchingBranch;

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
            // administrator since the token was minted is reflected immediately - and so is a
            // tier change, which moves whole groups of tabs.
            await _session.RefreshCurrentUserAsync();
            BuildSidebar();
            await RefreshBranchPickerAsync();

            if (_session.CurrentUser?.MustChangePassword == true)
            {
                PromptPasswordChange();
            }

            var first = Catalogue()
                .SelectMany(g => g.Modules)
                .FirstOrDefault(m => _session.Can(m.RequiredModule) && m.Tabs(this).Length > 0);

            if (first is null)
            {
                ShowNoModules();
                return;
            }

            await NavigateAsync(first.Key);
        }

        // ------------------------------------------------------------------ catalogue

        /// <summary>
        /// The navigation: exactly the nine modules, in the order the business runs.
        ///
        /// Membership brings people in; payments and sales take their money; inventory supplies
        /// the goods; employees and payroll run the staff; finance records the consequences;
        /// system administration controls who may do any of it; and business intelligence
        /// reports on all of it.
        ///
        /// Every tab names the subfeature it belongs to and is drawn only if the server said
        /// the signed-in user may open it. That is what lets the same catalogue serve all three
        /// tiers: a Micro gym sees four tabs under Business Intelligence' one, a Medium tenant
        /// sees ten, and neither list is written twice.
        /// </summary>
        private NavGroup[] Catalogue() =>
            _session.CurrentUser?.IsPlatformAdministrator == true
                ? PlatformCatalogue()
                : TenantCatalogue();

        /// <summary>
        /// The Super Admin's panel: Dashboard, Subscription, System Administration and Business
        /// Intelligence, and nothing else.
        ///
        /// These are not four new modules - there are still exactly nine, and every entry here
        /// names one of them in <c>RequiredModule</c> and opens onto subfeatures that already
        /// exist. What changes is only how they are grouped for an account whose workspace is
        /// the platform rather than a gym: the platform dashboard and the subscription book are
        /// the two things a platform operator opens most, so they get their own entries instead
        /// of being buried as the fifth tab of something else.
        ///
        /// The seven operational modules are absent because the platform company is not a gym.
        /// It has no members, no stock and no till, and drawing Membership for the Super Admin
        /// would offer a screen that can only report that the platform has no members.
        /// Branch administration is absent for a different reason: a branch belongs to a
        /// tenant's own company, so it stays with that tenant's Admin/Owner.
        /// </summary>
        private NavGroup[] PlatformCatalogue() => new[]
        {
            new NavGroup("PLATFORM", new[]
            {
                new ModuleEntry("platformDashboard", "Dashboard", "◉", Modules.BusinessIntelligence,
                    "Platform Dashboard",
                    "Tenants, subscriptions, accounts and activity across the installation.",
                    s => s.Tabs(
                        (Submodules.PlatformDashboard, "dashboard", "Dashboard",
                            () => new AnalyticsPage(s._session, "Platform Dashboard",
                                "Tenants, subscriptions, accounts and activity across the installation.",
                                s._session.Platform.GetDashboardAsync)))),

                new ModuleEntry("platformSubscription", "Subscription", "▦", Modules.SystemAdmin,
                    "Subscription Management",
                    "The plans FitCore sells, and which tier each tenant is licensed for.",
                    s => s.Tabs(
                        (Submodules.PlatformPlans, "plans", "Plans",
                            () => new SubscriptionPlansPage(s._session)),
                        (Submodules.PlatformSubscriptions, "subscriptions", "Subscriptions & Tiers",
                            () => new PlatformSubscriptionsPage(s._session))))
            }),

            new NavGroup("SYSTEM", new[]
            {
                new ModuleEntry("systemadmin", "Administration", "⚙", Modules.SystemAdmin,
                    "System Administration",
                    "The tenants themselves, the accounts across them, and the platform trail.",
                    s => s.Tabs(
                        (Submodules.PlatformTenants, "tenants", "Tenants",
                            () => new TenantsPage(s._session)),
                        (Submodules.PlatformUsers, "platformUsers", "Platform Users",
                            () => new PlatformUsersPage(s._session)),
                        (Submodules.PlatformSettings, "platformSettings", "Platform Settings",
                            () => new SettingsPage(s._session, "Platform Settings",
                                "Configuration that applies to the whole installation.",
                                s._session.Platform.GetSettingsAsync,
                                s._session.Platform.UpdateSettingsAsync)),
                        (Submodules.PlatformAudit, "platformAudit", "Platform Audit",
                            () => new PlatformAuditPage(s._session)),
                        (Submodules.PlatformMonitoring, "monitoring", "Monitoring",
                            () => new PlatformHealthPage(s._session))))
            }),

            new NavGroup("INSIGHT", new[]
            {
                new ModuleEntry("businessintelligence", "Intelligence", "▤", Modules.BusinessIntelligence,
                    "Business Intelligence",
                    "Company growth, tier mix, subscription revenue and usage across the platform.",
                    s => s.Tabs(
                        (Submodules.PlatformAnalytics, "platformAnalytics", "Platform Analytics",
                            () => new AnalyticsPage(s._session, "Platform Analytics",
                                "Company growth, tier mix, subscription revenue and usage.",
                                s._session.Platform.GetAnalyticsAsync))))
            })
        };

        private NavGroup[] TenantCatalogue() => new[]
        {
            new NavGroup("OPERATIONS", new[]
            {
                new ModuleEntry("membership", "Membership", "●", Modules.Membership,
                    "Membership Management",
                    "Members, plans, subscriptions and the history behind each one.",
                    s => s.Tabs(
                        (Submodules.Members, "members", "Members",
                            () => new MembersPage(s._session)),
                        (Submodules.MembershipPlans, "plans", "Membership Plans",
                            () => new PlansPage(s._session)),
                        (Submodules.Subscriptions, "subscriptions", "Subscriptions",
                            () => new SubscriptionsPage(s._session)),
                        (Submodules.MemberHistory, "history", "History",
                            () => new MemberHistoryPage(s._session)),
                        (Submodules.MembershipReports, "reports", "Reports",
                            () => new ReportsPage(s._session, "Membership Reports",
                                "Joins, renewals, expiries and membership revenue.",
                                ReportKind.Membership)))),

                new ModuleEntry("payments", "Payments", "▮", Modules.Payments,
                    "Payment Management",
                    "Record what has been received, and see what is still owed.",
                    s => s.Tabs(
                        (Submodules.PaymentTransactions, "transactions", "Payment Transactions",
                            () => new PaymentsPage(s._session)),
                        (Submodules.Receivables, "outstanding", "Outstanding",
                            () => new ReceivablesPage(s._session)),
                        (Submodules.PaymentReconciliation, "reconciliation", "Reconciliation",
                            () => new PaymentReconciliationPage(s._session)),
                        (Submodules.PaymentReports, "reports", "Reports",
                            () => new ReportsPage(s._session, "Payment Reports",
                                "Collections by day, method and category.",
                                ReportKind.Payments)))),

                new ModuleEntry("sales", "Sales", "▲", Modules.Sales,
                    "Sales Management",
                    "Ring up a sale, review the day's takings and handle returns - independent " +
                    "of Membership, so a sale never has to name a member or a customer record.",
                    s => s.Tabs(
                        (Submodules.PointOfSale, "pos", "New Sale",
                            () => new PosPage(s._session)),
                        (Submodules.SalesHistory, "history", "Sales History",
                            () => new SalesPage(s._session)),
                        (Submodules.SalesReturns, "returns", "Returns & Refunds",
                            () => new ReturnsPage(s._session)),
                        (Submodules.SalesReports, "reports", "Reports",
                            () => new ReportsPage(s._session, "Sales Reports",
                                "Revenue, units, top products and margin.",
                                ReportKind.Sales)))),

                new ModuleEntry("inventory", "Inventory", "▣", Modules.Inventory,
                    "Inventory Management",
                    "Products, stock on hand, the suppliers you buy from and the orders you place.",
                    s => s.Tabs(
                        (Submodules.Products, "products", "Products",
                            () => new ProductsPage(s._session)),
                        (Submodules.Stock, "stock", "Stock",
                            () => new InventoryPage(s._session)),
                        (Submodules.Purchases, "purchases", "Purchases",
                            () => new PurchasesPage(s._session)),
                        (Submodules.Suppliers, "suppliers", "Suppliers",
                            () => new SuppliersPage(s._session)),
                        (Submodules.StockMovements, "movements", "Stock Movements",
                            () => new StockMovementsPage(s._session)),
                        (Submodules.InventoryValuation, "valuation", "Valuation",
                            () => new ValuationPage(s._session)),
                        (Submodules.InventoryReports, "reports", "Reports",
                            () => new ReportsPage(s._session, "Inventory Reports",
                                "Low stock, movement and value by category.",
                                ReportKind.Inventory)))),

                new ModuleEntry("employees", "Employees", "◍", Modules.Employees,
                    "Employee Management",
                    "Your staff, their attendance and their leave.",
                    s => s.Tabs(
                        (Submodules.EmployeeRecords, "records", "Employee Records",
                            () => new EmployeeRecordsPage(s._session)),
                        (Submodules.Attendance, "attendance", "Attendance",
                            () => new AttendancePage(s._session)),
                        (Submodules.Leave, "leave", "Leave",
                            () => new LeavePage(s._session)),
                        (Submodules.EmployeeReports, "reports", "Reports",
                            () => new ReportsPage(s._session, "Employee Reports",
                                "Headcount, attendance rate and overtime by employee.",
                                ReportKind.Employees)))),

                new ModuleEntry("payroll", "Payroll", "◈", Modules.Payroll,
                    "Payroll Management",
                    "Pay runs generated from attendance, with gross, deductions and net calculated by the server.",
                    s => s.Tabs(
                        (Submodules.PayrollRecords, "records", "Payroll Records",
                            () => new PayrollRecordsPage(s._session)),
                        (Submodules.PayrollAttendance, "summary", "Attendance Summary",
                            () => new AttendanceSummaryPage(s._session)),
                        (Submodules.PayrollCalculation, "calculation", "Payroll Calculation",
                            () => new PayrollCalculationPage(s._session)),
                        (Submodules.Payslips, "payslips", "Payslips",
                            () => new PayslipsPage(s._session)),
                        (Submodules.PayrollHistory, "history", "Payroll History",
                            () => new PayrollHistoryPage(s._session)),
                        (Submodules.PayrollReports, "reports", "Reports",
                            () => new ReportsPage(s._session, "Payroll Reports",
                                "Salary cost, overtime and statutory contributions by period.",
                                ReportKind.Payroll))))
            }),

            new NavGroup("FINANCE", new[]
            {
                new ModuleEntry("finance", "Finance", "₱", Modules.Finance,
                    "Finance Management",
                    "The books: expenses, the ledger, what is owed either way, and the statements.",
                    s => s.Tabs(
                        (Submodules.FinanceOverview, "overview", "Overview",
                            () => new FinanceOverviewPage(s._session)),
                        (Submodules.Expenses, "expenses", "Expenses",
                            () => new ExpensesPage(s._session)),
                        (Submodules.ChartOfAccounts, "accounts", "Chart of Accounts",
                            () => new ChartOfAccountsPage(s._session)),
                        (Submodules.JournalEntries, "journal", "Journal Entries",
                            () => new JournalPage(s._session)),
                        (Submodules.GeneralLedger, "ledger", "General Ledger",
                            () => new GeneralLedgerPage(s._session)),
                        (Submodules.AccountsReceivable, "receivable", "Receivable",
                            () => new ReceivablesPage(s._session)),
                        (Submodules.AccountsPayable, "payable", "Payable",
                            () => new PayablesPage(s._session)),
                        (Submodules.Banking, "banking", "Cash & Bank",
                            () => new BankAccountsPage(s._session)),
                        (Submodules.BankReconciliation, "reconciliation", "Reconciliation",
                            () => new ReconciliationPage(s._session)),
                        (Submodules.Budgets, "budgets", "Budgets",
                            () => new BudgetsPage(s._session)),
                        (Submodules.FinancialPeriods, "periods", "Periods",
                            () => new FinancialPeriodsPage(s._session)),
                        (Submodules.FinancialReports, "reports", "Financial Reports",
                            () => new FinancialReportsPage(s._session))))
            }),

            new NavGroup("SYSTEM", new[]
            {
                new ModuleEntry("systemadmin", "Administration", "⚙", Modules.SystemAdmin,
                    "System Administration",
                    "Accounts, roles, settings, the audit trail - and, for the platform account, the tenants themselves.",
                    s => s.Tabs(
                        (Submodules.Users, "users", "Users",
                            () => new UsersPage(s._session)),
                        (Submodules.Roles, "roles", "Roles",
                            () => new RolesPage(s._session)),
                        (Submodules.Permissions, "permissions", "Permissions",
                            () => new PermissionsPage(s._session)),

                        // Branching. Medium and Admin only, so for every other account this tab
                        // simply is not drawn - and the endpoints behind it refuse regardless.
                        (Submodules.Branches, "branches", "Branches",
                            () => new BranchesPage(s._session)),

                        (Submodules.Settings, "settings", "Settings",
                            () => new SettingsPage(s._session)),
                        (Submodules.AuditLogs, "audit", "Audit Logs",
                            () => new AuditHistoryPage(s._session)),
                        (Submodules.Security, "security", "Security",
                            () => new SecurityPage(s._session)),
                        (Submodules.DataIntegrity, "integrity", "Data Integrity",
                            () => new DataIntegrityPage(s._session))))

                // The six platform subfeatures are not listed here. They belong to the Super
                // Admin, whose sidebar is PlatformCatalogue() above, so a copy of them in the
                // tenant catalogue would be drawn for nobody.
            }),

            new NavGroup("INSIGHT", new[]
            {
                new ModuleEntry("businessintelligence", "Intelligence", "▤", Modules.BusinessIntelligence,
                    "Business Intelligence",
                    "The dashboard, the reports, and the analytics behind every module.",
                    s => s.Tabs(
                        (Submodules.ExecutiveDashboard, "dashboard", "Dashboard",
                            () => new DashboardPage(s._session, s.DashboardActionAsync)),
                        (Submodules.OperationalReports, "reports", "Reports",
                            () => new ReportsPage(s._session)),

                        (Submodules.KpiDashboard, "kpi", "KPIs",
                            () => new AnalyticsPage(s._session, "Key Performance Indicators",
                                "Every figure FitCore measures, across all nine modules.",
                                s._session.Analytics.GetKpiDashboardAsync)),

                        (Submodules.MembershipAnalytics, "membership", "Membership",
                            () => s.Area("membership", "Membership Analytics",
                                "Growth, retention, renewals and plan mix.")),
                        (Submodules.SalesAnalytics, "sales", "Sales",
                            () => s.Area("sales", "Sales Analytics",
                                "Revenue, basket size, product mix and growth.")),
                        (Submodules.PaymentAnalytics, "payments", "Payments",
                            () => s.Area("payments", "Payment Analytics",
                                "Collection rate, method mix and what is still owed.")),
                        (Submodules.InventoryAnalytics, "inventory", "Inventory",
                            () => s.Area("inventory", "Inventory Analytics",
                                "Value, movement, turnover and stock risk.")),
                        (Submodules.WorkforceAnalytics, "workforce", "Workforce",
                            () => s.Area("workforce", "Workforce Analytics",
                                "Headcount, attendance, overtime and payroll cost.")),
                        (Submodules.FinanceAnalytics, "finance", "Finance",
                            () => s.Area("finance", "Finance Analytics",
                                "Revenue, expenses, cash flow and net income over time.")),
                        (Submodules.ProfitabilityAnalytics, "profitability", "Profitability",
                            () => s.Area("profitability", "Profitability",
                                "Gross margin, cost of goods sold and net margin.")),

                        // The company beside each of its branches. Unaffected by the branch
                        // picker on purpose - a comparison that only showed one branch would
                        // not be one.
                        (Submodules.BranchPerformance, "branches", "Branch Performance",
                            () => new BranchPerformancePage(s._session))))
            })
        };

        /// <summary>
        /// Keeps only the tabs this user may open.
        ///
        /// The server decides: it sends the list of permitted subfeatures with the signed-in
        /// user, so the desktop is filtering against an answer rather than working one out.
        /// That is what stops the client holding a second, drifting copy of the tier rules.
        /// </summary>
        private WorkspaceTab[] Tabs(
            params (string Submodule, string Key, string Label, Func<ModulePageBase> Create)[] tabs)
        {
            var user = _session.CurrentUser;

            return tabs
                .Where(t => user?.CanUse(t.Submodule) == true)
                .Select(t => new WorkspaceTab(t.Key, t.Label, t.Create))
                .ToArray();
        }

        /// <summary>One Business Intelligence area, rendered by the shared analytics screen.</summary>
        private AnalyticsPage Area(string area, string title, string subtitle) =>
            new(_session, title, subtitle,
                (from, to) => _session.Analytics.GetAreaAsync(area, from, to));

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
                // A module with no reachable tab is not drawn at all. That is the case for a
                // Manager and System Administration on a Micro tenant: they hold the module by
                // role, but every subfeature inside it is Admin-only, so an entry that opened
                // onto nothing would be worse than no entry.
                var permitted = group.Modules
                    .Where(m => _session.Can(m.RequiredModule) && m.Tabs(this).Length > 0)
                    .ToArray();

                if (permitted.Length == 0) continue;

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

            // The platform account gets its own chip rather than a tier. A Super Admin is not
            // on a plan - they administer the plans - and labelling them "MEDIUM" would suggest
            // they are one of the tenants.
            var chipText = _session.CurrentUser?.IsPlatformAdministrator == true
                ? "PLATFORM"
                : (_session.CurrentUser?.EnterpriseTier ?? "").ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(chipText))
            {
                var chip = new Label
                {
                    Text = chipText,
                    Font = new Font(UiTheme.FamilySemibold, 7F),
                    ForeColor = Color.White,
                    AutoSize = false,
                    Size = new Size(66, 20),
                    Location = new Point(SidebarWidth - 88, 30),
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

            // The branch picker. Drawn only for an account the server would actually honour it
            // for - the Admin/Owner of a Medium tenant - because for anybody else the header is
            // ignored and a control that appears to do nothing is worse than no control.
            _branchLabel = new Label
            {
                Text = "Branch",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Visible = false,
                UseMnemonic = false
            };

            _branchPicker = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = UiTheme.Body,
                Width = 210,
                Visible = false,
                DisplayMember = "Value",
                ValueMember = "Key"
            };

            _branchPicker.SelectedIndexChanged += async (_, _) => await OnBranchPickedAsync();

            void Reflow()
            {
                avatar.Location = new Point(bar.Width - 62, 14);
                identity.Location = new Point(bar.Width - 74 - identity.Width, 14);
                role.Location = new Point(bar.Width - 74 - role.Width, 34);

                var pickerRight = bar.Width - 74 - Math.Max(identity.Width, role.Width) - 24;
                _branchPicker.Location = new Point(pickerRight - _branchPicker.Width, 30);
                _branchLabel.Location = new Point(pickerRight - _branchPicker.Width, 12);
            }

            bar.Resize += (_, _) => Reflow();

            bar.Controls.Add(_crumb);
            bar.Controls.Add(_crumbHint);
            bar.Controls.Add(_branchLabel);
            bar.Controls.Add(_branchPicker);
            bar.Controls.Add(identity);
            bar.Controls.Add(role);
            bar.Controls.Add(avatar);

            Reflow();
            return bar;
        }

        /// <summary>
        /// Fills the branch picker for an Admin/Owner of a branched company.
        ///
        /// "All branches" is the first entry and the default: an owner opening the application
        /// should see the whole company, not whichever branch happens to sort first. The list
        /// comes from the server, and the server re-checks the chosen branch on every request
        /// that carries it - this control only asks.
        /// </summary>
        private async Task RefreshBranchPickerAsync()
        {
            var user = _session.CurrentUser;

            if (user?.CanManageBranches != true)
            {
                _branchPicker.Visible = false;
                _branchLabel.Visible = false;
                return;
            }

            var branches = (await _session.Branches.GetAllAsync(includeInactive: false)).Value;

            // A Medium tenant that has not created any branches yet is a single-site company in
            // practice, so there is nothing to pick between.
            if (branches is null || branches.Count == 0)
            {
                _branchPicker.Visible = false;
                _branchLabel.Visible = false;
                return;
            }

            var entries = new List<KeyValuePair<int, string>> { new(0, "All branches") };
            entries.AddRange(branches.Select(b => new KeyValuePair<int, string>(b.BranchId, b.Name)));

            _switchingBranch = true;
            try
            {
                _branchPicker.DataSource = entries;
                _branchPicker.SelectedValue = _session.SelectedBranchId ?? 0;
            }
            finally
            {
                _switchingBranch = false;
            }

            _branchPicker.Visible = true;
            _branchLabel.Visible = true;
        }

        /// <summary>
        /// Switches branch and rebuilds every open screen.
        ///
        /// The workspaces are discarded rather than told to reload: each one caches the rows it
        /// last fetched, and those rows belong to the branch that was selected when they were
        /// read. Rebuilding is what guarantees no screen is left showing another branch's data.
        /// </summary>
        private async Task OnBranchPickedAsync()
        {
            if (_switchingBranch) return;
            if (_branchPicker.SelectedValue is not int chosen) return;

            var branchId = chosen > 0 ? chosen : (int?)null;
            if (_session.SelectedBranchId == branchId) return;

            _session.SelectBranch(branchId);

            foreach (var workspace in _workspaces.Values) workspace.Dispose();
            _workspaces.Clear();
            _current = null;

            var key = _currentKey;
            _currentKey = null;

            if (key is not null) await NavigateAsync(key);

            _crumbHint.Text = branchId is null
                ? "Showing every branch"
                : $"Showing {_branchPicker.Text}";
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
                var tabs = entry.Tabs(this);

                if (tabs.Length == 0)
                {
                    UiKit.Info(
                        $"Your account holds {entry.Title} but none of the screens inside it. " +
                        "Ask an administrator if you need one of them.",
                        entry.Title);
                    return;
                }

                workspace = new ModuleWorkspace(tabs);
                workspace.NavigationRequested += async (m, t) => await NavigateAsync(m, t);

                // A write reaches further than the module that made it: a sale moves stock and
                // the ledger, a payment moves the books and every dashboard that counts them.
                // The workspace that did the writing has already dealt with its own tabs, so
                // only the others are dropped here.
                var writingModule = entry.Key;
                workspace.DataChanged += () =>
                {
                    foreach (var (key, other) in _workspaces)
                    {
                        if (!string.Equals(key, writingModule, StringComparison.OrdinalIgnoreCase))
                        {
                            other.InvalidateCaches();
                        }
                    }
                };

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
