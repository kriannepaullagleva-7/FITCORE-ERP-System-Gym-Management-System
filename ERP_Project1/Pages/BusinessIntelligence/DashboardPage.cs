using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Live figures for the signed-in company. Every number comes from
    /// GET /api/reports/dashboard, which aggregates in the tenant database - nothing here is
    /// computed in the client and nothing is hardcoded.
    ///
    /// The cards, the quick actions and the alert panels are all chosen from what the
    /// signed-in user actually holds, so a Micro receptionist sees a smaller, honest
    /// dashboard rather than a full one with half of it greyed out.
    /// </summary>
    internal sealed class DashboardPage : ModulePageBase
    {
        /// <summary>A quick action: where it goes, and whether it opens the create form.</summary>
        private sealed record QuickAction(
            string Label, string Module, string Tab, bool StartCreate, string RequiredModule);

        private readonly Func<string, string, bool, Task>? _go;
        private readonly Dictionary<string, Label> _values = new();
        private readonly Dictionary<string, Label> _hints = new();

        private DataGridView _activity = null!;
        private DataGridView _alerts = null!;
        private Label _alertsCaption = null!;
        private Label _updated = null!;
        private FlowLayoutPanel _actions = null!;

        protected override bool UsesGrid => false;

        public DashboardPage(FitCoreSession session, Func<string, string, bool, Task>? go = null)
            : base(session, "Dashboard", "Live figures from your tenant database.")
        {
            _go = go;

            AddAction("Refresh", ButtonTone.Secondary, () => LoadAsync(), 90);

            BuildCards();
            BuildBody();
        }

        // ------------------------------------------------------------------ cards

        private void BuildCards()
        {
            void Card(string key, string label, Color accent)
            {
                var panel = UiKit.StatCard(label, out var value, out var hint, accent);
                _values[key] = value;
                _hints[key] = hint;
                StatsRow.Controls.Add(panel);
            }

            if (Session.Can(Modules.Membership))
            {
                Card("members", "Total members", UiTheme.Primary);
                Card("active", "Active memberships", UiTheme.Success);
                Card("expiring", "Expiring soon", UiTheme.Warning);
            }

            if (Session.Can(Modules.Sales))
            {
                Card("salesMonth", "Sales this month", UiTheme.Primary);
            }

            if (Session.Can(Modules.Payments))
            {
                Card("collected", "Payments collected", UiTheme.Success);
                Card("outstanding", "Outstanding", UiTheme.Danger);
            }

            if (Session.Can(Modules.Inventory))
            {
                Card("stockValue", "Stock value", UiTheme.Neutral);
                Card("lowStock", "Inventory alerts", UiTheme.Warning);
            }

            // Small Enterprise and above. A Micro tenant has no employees to count.
            if (Session.Can(Modules.Employees))
            {
                Card("employees", "Employees", UiTheme.Primary);
            }

            if (Session.Can(Modules.Payroll))
            {
                Card("payroll", "Payroll this month", UiTheme.Success);
            }
        }

        // ------------------------------------------------------------------ body

        private void BuildBody()
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };

            // ---- quick actions ----
            var actionCard = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(16, 12, 16, 12),
                Margin = new Padding(0, 0, 0, UiTheme.SpaceM)
            };
            actionCard.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, actionCard.Width, actionCard.Height),
                UiTheme.Surface, UiTheme.Border);

            _actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                BackColor = Color.Transparent
            };

            _actions.Controls.Add(new Label
            {
                Text = "QUICK ACTIONS",
                Font = UiTheme.Overline,
                ForeColor = UiTheme.TextMuted,
                AutoSize = false,
                Size = new Size(110, 34),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 10, 0),
                UseMnemonic = false
            });

            foreach (var action in AvailableActions())
            {
                var button = UiKit.Action(action.Label, ButtonTone.Secondary, null,
                    Math.Max(112, TextRenderer.MeasureText(action.Label, UiTheme.BodyStrong).Width + 30));

                var target = action;
                button.Click += async (_, _) =>
                {
                    if (_go is null) return;
                    await _go(target.Module, target.Tab, target.StartCreate);
                };

                _actions.Controls.Add(button);
            }

            actionCard.Controls.Add(_actions);

            // ---- recent activity + alerts, side by side ----
            var columns = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.Canvas,
                Margin = new Padding(0, UiTheme.SpaceM, 0, 0)
            };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

            columns.Controls.Add(BuildActivityCard(), 0, 0);
            columns.Controls.Add(BuildAlertsCard(), 1, 0);

            _updated = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };

            host.Controls.Add(columns);
            host.Controls.Add(_updated);

            if (_actions.Controls.Count > 1)
            {
                var spacer = new Panel { Dock = DockStyle.Top, Height = UiTheme.SpaceM, BackColor = UiTheme.Canvas };
                host.Controls.Add(spacer);
                host.Controls.Add(actionCard);
            }

            SetBody(host);
        }

        private IEnumerable<QuickAction> AvailableActions()
        {
            var all = new[]
            {
                new QuickAction("＋ Add member",   "membership", "members",      true,  Modules.Membership),
                new QuickAction("＋ New sale",     "sales",      "pos",          false, Modules.Sales),
                new QuickAction("＋ Record payment","payments",  "transactions", true,  Modules.Payments),
                new QuickAction("＋ Add product",  "inventory",  "products",     true,  Modules.Inventory),
                new QuickAction("↑ Stock in",      "inventory",  "stock",        false, Modules.Inventory),
                new QuickAction("＋ Add employee", "employees",  "records",      true,  Modules.Employees),
                new QuickAction("＋ Create payroll","payroll",   "records",      true,  Modules.Payroll)
            };

            // Only actions the signed-in user is permitted to reach. The API would refuse the
            // rest anyway; offering them would just be a button that fails.
            return all.Where(a => Session.Can(a.RequiredModule));
        }

        private Panel BuildActivityCard()
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(1),
                Margin = new Padding(0, 0, UiTheme.SpaceM, 0)
            };
            card.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, card.Width, card.Height), UiTheme.Surface, UiTheme.Border);

            var caption = new Label
            {
                Text = "   Recent activity",
                Dock = DockStyle.Top,
                Height = 44,
                Font = UiTheme.SectionTitle,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                UseMnemonic = false
            };

            _activity = UiKit.Grid();
            _activity.AutoGenerateColumns = false;
            _activity.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "Type", DataPropertyName = "Type", HeaderText = "Type", FillWeight = 55 });
            _activity.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = "Title", HeaderText = "Activity", FillWeight = 120 });
            _activity.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = "Detail", HeaderText = "Detail", FillWeight = 120 });
            _activity.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Amount",
                HeaderText = "Amount",
                FillWeight = 60,
                DefaultCellStyle = new DataGridViewCellStyle
                { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
            });
            _activity.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "OccurredAt",
                HeaderText = "When",
                FillWeight = 85,
                DefaultCellStyle = new DataGridViewCellStyle { Format = "d MMM yyyy HH:mm" }
            });

            UiKit.PaintStatusColumns(_activity, "Type");
            UiKit.SetMinimumColumnWidths(_activity);

            card.Controls.Add(_activity);
            card.Controls.Add(caption);
            return card;
        }

        private Panel BuildAlertsCard()
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(1)
            };
            card.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, card.Width, card.Height), UiTheme.Surface, UiTheme.Border);

            _alertsCaption = new Label
            {
                Text = "   Low stock",
                Dock = DockStyle.Top,
                Height = 44,
                Font = UiTheme.SectionTitle,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                UseMnemonic = false
            };

            _alerts = UiKit.Grid();
            _alerts.AutoGenerateColumns = false;
            _alerts.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = nameof(InventoryDto.ProductName), HeaderText = "Product", FillWeight = 130 });
            _alerts.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(InventoryDto.QuantityOnHand),
                HeaderText = "On hand",
                FillWeight = 55,
                DefaultCellStyle = new DataGridViewCellStyle
                { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }
            });
            _alerts.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = nameof(InventoryDto.StockStatus),
                DataPropertyName = nameof(InventoryDto.StockStatus),
                HeaderText = "Status",
                FillWeight = 75
            });

            UiKit.PaintStatusColumns(_alerts, nameof(InventoryDto.StockStatus));
            UiKit.SetMinimumColumnWidths(_alerts);

            card.Controls.Add(_alerts);
            card.Controls.Add(_alertsCaption);
            return card;
        }

        // ------------------------------------------------------------------ data

        public override Task LoadAsync() => GuardAsync(async () =>
        {
            var summary = Unwrap(await Session.Reports.GetDashboardAsync());
            if (summary is null) return;

            Set("members", summary.TotalMembers.ToString("N0"),
                $"{summary.NewMembersThisMonth:N0} joined this month");

            Set("active", summary.ActiveSubscriptions.ToString("N0"),
                $"{summary.ActiveMembers:N0} active member(s)");

            Set("expiring", summary.ExpiringSoon.ToString("N0"),
                $"{summary.ExpiredSubscriptions:N0} expired · {summary.ExpiringWithin1Day:N0} in 1 day · " +
                $"{summary.ExpiringWithin3Days:N0} in 3 days · {summary.ExpiringWithin7Days:N0} in 7 days");

            Set("salesMonth", UiKit.Money(summary.SalesThisMonth),
                $"{summary.TransactionsThisMonth:N0} transaction(s) · {UiKit.Money(summary.SalesToday)} today");

            Set("collected", UiKit.Money(summary.PaymentsThisMonth),
                $"this month · {UiKit.Money(summary.PaymentsTotal)} all time");

            Set("outstanding", UiKit.Money(summary.OutstandingBalance),
                $"{summary.UnpaidSales:N0} unpaid · {summary.PartiallyPaidSales:N0} part paid");

            Set("stockValue", UiKit.Money(summary.InventoryValue),
                $"{summary.TotalProducts:N0} product(s)");

            Set("lowStock", $"{summary.LowStockProducts + summary.OutOfStockProducts:N0}",
                $"{summary.LowStockProducts:N0} low · {summary.OutOfStockProducts:N0} out of stock");

            Set("employees", summary.ActiveEmployees.ToString("N0"),
                $"{summary.TotalEmployees:N0} on record");

            Set("payroll", UiKit.Money(summary.PayrollThisMonth),
                (summary.PayrollOutstanding > 0
                    ? $"{UiKit.Money(summary.PayrollOutstanding)} still to pay"
                    : "nothing outstanding") +
                (summary.DraftPayrollRuns > 0 ? $" · {summary.DraftPayrollRuns:N0} draft" : ""));

            // The activity feed travels on the same summary, so the dashboard is one round
            // trip rather than two.
            var activity = summary.RecentActivity ?? new List<ActivityItemDto>();
            _activity.DataSource = activity;

            await LoadAlertsAsync();

            _updated.Text = $"  Updated {DateTime.Now:HH:mm:ss} · {Session.CurrentUser?.CompanyName}";

            SetStatus($"{Session.CurrentUser?.EnterpriseTier} Enterprise   ·   " +
                      $"signed in as {Session.CurrentUser?.FullName} ({Session.CurrentUser?.RoleDisplayName})");
        }, "Loading your dashboard…");

        /// <summary>
        /// The low-stock panel needs the product rows themselves, which the summary does not
        /// carry. Skipped entirely for a user without Inventory - the API would answer 403.
        /// </summary>
        private async Task LoadAlertsAsync()
        {
            if (!Session.Can(Modules.Inventory))
            {
                _alertsCaption.Text = "   Membership snapshot";
                _alerts.Visible = false;
                return;
            }

            var stock = Unwrap(await Session.Inventory.GetAllAsync());
            if (stock is null) return;

            var alerts = stock
                .Where(s => s.IsActive)
                .Where(s => s.QuantityOnHand <= s.ReorderLevel)
                .OrderBy(s => s.QuantityOnHand)
                .Take(25)
                .ToList();

            _alerts.DataSource = alerts;

            _alertsCaption.Text = alerts.Count == 0
                ? "   Low stock — all clear"
                : $"   Low stock — {alerts.Count} product(s) need attention";
        }

        private void Set(string key, string value, string hint)
        {
            if (_values.TryGetValue(key, out var v)) v.Text = value;
            if (_hints.TryGetValue(key, out var h)) h.Text = hint;
        }
    }
}
