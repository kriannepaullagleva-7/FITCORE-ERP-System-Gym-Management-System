using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The tenant's audit trail, read back.
    ///
    /// A subfeature of System Administration rather than a module of its own, and available on
    /// every tier - every gym needs to be able to answer "who changed this?" - but restricted
    /// to an owner, which the server enforces regardless of what the sidebar drew.
    /// </summary>
    internal sealed class AuditHistoryPage : CrudPageBase<AuditEventDto>
    {
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly ComboBox _moduleFilter;
        private readonly Label _summary;

        public AuditHistoryPage(FitCoreSession session)
            : base(session, "Audit History", "Who did what, and when — for administrators only.",
                   "event", "Actor, action, module or entity")
        {
            AddAction("View details", ButtonTone.Secondary, () => ShowDetailsAsync(), 116);

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            _from = UiKit.DatePicker(DateTime.Today.AddDays(-7));
            _from.ValueChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_from);

            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            _to = UiKit.DatePicker(DateTime.Today);
            _to.ValueChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_to);

            FilterBar.Controls.Add(UiKit.FilterLabel("Module"));
            _moduleFilter = UiKit.Select(150);
            _moduleFilter.DisplayMember = "Value";
            _moduleFilter.ValueMember = "Key";
            _moduleFilter.DataSource = new List<KeyValuePair<string, string>>
            {
                new("", "All modules"),
                new("membership", "Membership"),
                new("sales", "Sales"),
                new("payments", "Payments"),
                new("inventory", "Inventory"),
                new("employees", "Employees"),
                new("payroll", "Payroll"),
                new("finance", "Finance"),
                new("systemadmin", "System Administration"),
                new("businessintelligence", "Business Intelligence")
            };
            _moduleFilter.SelectedIndexChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_moduleFilter);

            _summary = new Label
            {
                AutoSize = true,
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                Margin = new Padding(16, 10, 0, 0),
                UseMnemonic = false
            };
            FilterBar.Controls.Add(_summary);
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string EmptyHeadline => "No audit events in this period";
        protected override string EmptyDetail =>
            "Widen the date range, or clear the module filter, to see more history.";

        protected override async Task<List<AuditEventDto>?> FetchAsync()
        {
            var module = _moduleFilter.SelectedValue?.ToString();

            return Unwrap(await Session.Audit.SearchAsync(
                from: _from.Value.Date,
                to: _to.Value.Date.AddDays(1).AddTicks(-1),
                module: string.IsNullOrWhiteSpace(module) ? null : module,
                take: 500));
        }

        protected override void DefineColumns()
        {
            Column(nameof(AuditEventDto.OccurredAt), "When", 100, "d MMM yyyy HH:mm");
            Column(nameof(AuditEventDto.Username), "Actor", 100);
            Column(nameof(AuditEventDto.RoleKey), "Role", 70);
            StatusColumn(nameof(AuditEventDto.Action), "Action", 90);
            Column(nameof(AuditEventDto.Module), "Module", 90);
            Column(nameof(AuditEventDto.EntityName), "Entity", 90);
            Column(nameof(AuditEventDto.EntityId), "Entity ID", 70);
            Column(nameof(AuditEventDto.Summary), "Summary", 220);
        }

        protected override bool Matches(AuditEventDto e, string term) =>
            (e.Username ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (e.Action ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (e.Module ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (e.EntityName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (e.Summary ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override void AfterLoad()
        {
            _summary.Text = $"{Items.Count:N0} event(s) between {UiKit.Date(_from.Value)} and {UiKit.Date(_to.Value)}";
        }

        /// <summary>Shows the raw before/after field values behind a row - the detail a one-line summary can't carry.</summary>
        private Task ShowDetailsAsync()
        {
            var e = Selected;
            if (e is null) { ShowError("Select an event to view its details."); return Task.CompletedTask; }

            var text =
                $"{e.OccurredAt:d MMM yyyy HH:mm}  ·  {e.Username} ({e.RoleKey})\r\n" +
                $"{e.Action}  ·  {e.Module}  ·  {e.EntityName} #{e.EntityId ?? "—"}\r\n\r\n" +
                (string.IsNullOrWhiteSpace(e.Summary) ? "" : $"{e.Summary}\r\n\r\n") +
                (string.IsNullOrWhiteSpace(e.OldValues) ? "" : $"Before:\r\n{e.OldValues}\r\n\r\n") +
                (string.IsNullOrWhiteSpace(e.NewValues) ? "" : $"After:\r\n{e.NewValues}");

            UiKit.Info(text, "Audit event");
            return Task.CompletedTask;
        }
    }
}
