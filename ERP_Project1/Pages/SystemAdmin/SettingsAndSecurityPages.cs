using System.ComponentModel;
using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The settings a gym administers for itself: its own name on a receipt, its shift length,
    /// its reorder level, how long a session lasts.
    ///
    /// Only departures from the shipped default are stored, which is why the screen shows both
    /// - an operator can see at a glance which settings somebody has deliberately changed and
    /// which are simply what FitCore ships with.
    /// </summary>
    internal sealed class SettingsPage : ModulePageBase
    {
        private readonly Panel _body;
        private readonly Dictionary<string, Control> _editors = new(StringComparer.OrdinalIgnoreCase);
        private List<SettingDto> _settings = new();

        private readonly Func<Task<ApiResult<List<SettingDto>>>> _load;
        private readonly Func<Dictionary<string, string>, Task<ApiResult<List<SettingDto>>>> _save;

        /// <summary>This tenant's own settings.</summary>
        public SettingsPage(FitCoreSession session)
            : this(session, "Settings",
                   "How FitCore behaves for this company. Anything not changed here uses the shipped default.",
                   session.Settings.GetAllAsync, session.Settings.UpdateAsync)
        {
        }

        /// <summary>
        /// A settings screen over any key/value store the server describes the same way.
        ///
        /// Tenant settings and platform settings are the same shape - a key, a value, a
        /// category, a kind and the shipped default - so they are the same screen with a
        /// different pair of endpoints behind it. Two copies would have drifted the moment
        /// either grew a new editor kind.
        /// </summary>
        public SettingsPage(
            FitCoreSession session, string title, string subtitle,
            Func<Task<ApiResult<List<SettingDto>>>> load,
            Func<Dictionary<string, string>, Task<ApiResult<List<SettingDto>>>> save)
            : base(session, title, subtitle)
        {
            _load = load;
            _save = save;
            _body = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.Canvas
            };

            SetBody(_body);

            AddAction("Save changes", ButtonTone.Primary, SaveAsync, 126);
        }

        protected override bool UsesGrid => false;

        public override async Task LoadAsync()
        {
            await GuardAsync(async () =>
            {
                var settings = Unwrap(await _load());
                if (settings is null) return;

                _settings = settings;
                Render();

                var overridden = settings.Count(s => s.IsOverridden);

                SetStatus(overridden == 0
                    ? $"{settings.Count} settings, all at their shipped defaults."
                    : $"{overridden} of {settings.Count} settings have been changed for this company.");
            }, "Loading settings…");
        }

        private void Render()
        {
            _body.SuspendLayout();

            foreach (Control child in _body.Controls) child.Dispose();
            _body.Controls.Clear();
            _editors.Clear();

            // Added in reverse: a Top-docked stack draws last-added first, so building the page
            // backwards is what puts Business above Operations above Security above Finance.
            foreach (var group in _settings
                         .GroupBy(s => s.Category)
                         .Reverse())
            {
                _body.Controls.Add(BuildGroup(group.Key, group.ToList()));
            }

            _body.ResumeLayout();
        }

        private Control BuildGroup(string title, List<SettingDto> settings)
        {
            var card = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Surface,
                Padding = new Padding(UiTheme.SpaceL, UiTheme.SpaceM, UiTheme.SpaceL, UiTheme.SpaceL),
                Margin = new Padding(0, 0, UiTheme.SpaceM, UiTheme.SpaceM)
            };

            card.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, card.Width, card.Height),
                UiTheme.Surface, UiTheme.Border);

            var rows = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = UiTheme.Surface
            };

            foreach (var setting in settings)
            {
                rows.Controls.Add(BuildEditor(setting));
            }

            var heading = UiKit.SectionHeading(title);
            heading.Dock = DockStyle.Top;
            heading.AutoSize = false;
            heading.Height = 30;

            card.Controls.Add(rows);
            card.Controls.Add(heading);

            return card;
        }

        private Control BuildEditor(SettingDto setting)
        {
            var host = new Panel
            {
                Width = 320,
                Height = 78,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, 0, UiTheme.SpaceL, UiTheme.SpaceM)
            };

            var label = UiKit.FieldLabel(setting.DisplayName);
            label.Location = new Point(0, 0);

            Control editor;

            if (setting.Kind == "Boolean")
            {
                var check = new CheckBox
                {
                    Text = "",
                    Checked = bool.TryParse(setting.Value, out var flag) && flag,
                    Location = new Point(2, 22),
                    AutoSize = true,
                    BackColor = UiTheme.Surface
                };

                editor = check;
            }
            else
            {
                var box = new TextBox
                {
                    Text = setting.Value,
                    Font = UiTheme.Body,
                    BorderStyle = BorderStyle.None
                };

                var shell = UiKit.InputShell(box, 300);
                shell.Location = new Point(0, 20);

                editor = box;
                host.Controls.Add(shell);
            }

            if (editor is CheckBox) host.Controls.Add(editor);

            var hint = new Label
            {
                // The default is shown beside every setting, so an operator can tell a
                // deliberate choice from what FitCore happens to ship with.
                Text = setting.IsOverridden
                    ? $"{setting.Description} · default {setting.DefaultValue}"
                    : setting.Description,
                Font = UiTheme.Small,
                ForeColor = setting.IsOverridden ? UiTheme.TextSecondary : UiTheme.TextMuted,
                AutoSize = false,
                Size = new Size(310, 28),
                Location = new Point(1, 56),
                BackColor = UiTheme.Surface,
                UseMnemonic = false
            };

            host.Controls.Add(label);
            host.Controls.Add(hint);

            _editors[setting.Key] = editor;

            return host;
        }

        private async Task SaveAsync()
        {
            var values = new Dictionary<string, string>();

            foreach (var setting in _settings)
            {
                if (!_editors.TryGetValue(setting.Key, out var editor)) continue;

                var value = editor switch
                {
                    CheckBox check => check.Checked ? "true" : "false",
                    TextBox box => box.Text.Trim(),
                    _ => setting.Value
                };

                // Only what actually moved is sent. Posting every setting would rewrite the
                // "changed by" stamp on ones nobody touched.
                if (!string.Equals(value, setting.Value, StringComparison.Ordinal))
                {
                    values[setting.Key] = value;
                }
            }

            if (values.Count == 0)
            {
                ShowError("Nothing has changed.");
                return;
            }

            await GuardAsync(async () =>
            {
                var result = await _save(values);

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadAsync();
                Notify($"{UiKit.Plural(values.Count, "setting")} saved.");
            }, "Saving…");
        }
    }

    /// <summary>
    /// Security: sign-in activity, failed attempts and password changes.
    ///
    /// The same trail the audit screen reads, narrowed to the events that concern access. Kept
    /// as its own screen because "is somebody trying to get in?" is a different question from
    /// "who changed this record?", and mixing them buries the first under the second.
    /// </summary>
    internal sealed class SecurityPage : CrudPageBase<AuditEventDto>
    {
        private static readonly string[] AccessActions =
            { "Login", "LoginFailed", "Logout", "PasswordChanged", "PasswordReset", "PermissionsChanged" };

        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly ComboBox _action;

        private readonly Label _signIns;
        private readonly Label _signInsHint;
        private readonly Label _failures;
        private readonly Label _failuresHint;
        private readonly Label _passwordChanges;
        private readonly Label _passwordChangesHint;
        private readonly Label _permissionChanges;
        private readonly Label _permissionChangesHint;

        public SecurityPage(FitCoreSession session)
            : base(session, "Security",
                   "Sign-in activity, failed attempts and changes to who can reach what.",
                   "event", "Account, action or summary")
        {
            _from = UiKit.DatePicker(DateTime.Today.AddDays(-30));
            _from.ValueChanged += async (_, _) => await LoadAsync();

            _to = UiKit.DatePicker(DateTime.Today);
            _to.ValueChanged += async (_, _) => await LoadAsync();

            _action = UiKit.Select(170);
            _action.Items.Add("All access events");
            _action.Items.AddRange(AccessActions.Cast<object>().ToArray());
            _action.SelectedIndex = 0;
            _action.SelectedIndexChanged += async (_, _) => await LoadAsync();

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);
            FilterBar.Controls.Add(UiKit.FilterLabel("Event"));
            FilterBar.Controls.Add(_action);

            StatsRow.Controls.Add(UiKit.StatCard("Sign-ins", out _signIns, out _signInsHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Failed attempts", out _failures, out _failuresHint, UiTheme.Danger));
            StatsRow.Controls.Add(UiKit.StatCard("Password changes", out _passwordChanges, out _passwordChangesHint, UiTheme.Info));
            StatsRow.Controls.Add(UiKit.StatCard("Access changes", out _permissionChanges, out _permissionChangesHint, UiTheme.Warning));
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string EmptyHeadline => "No access events in this period";
        protected override string EmptyDetail =>
            "Sign-ins and password changes appear here as they happen. Widen the date range to " +
            "see more.";

        protected override async Task<List<AuditEventDto>?> FetchAsync()
        {
            var chosen = _action.SelectedIndex <= 0 ? null : (string)_action.SelectedItem!;

            var events = Unwrap(await Session.Audit.SearchAsync(
                from: _from.Value.Date,
                to: _to.Value.Date.AddDays(1).AddTicks(-1),
                action: chosen,
                take: 500));

            if (events is null) return null;

            // Narrowed here as well as on the server, because the audit endpoint takes one
            // action at a time and this screen is about the whole access category.
            var access = chosen is null
                ? events.Where(e => AccessActions.Contains(e.Action ?? "",
                    StringComparer.OrdinalIgnoreCase)).ToList()
                : events;

            _signIns.Text = access.Count(e => e.Action == "Login").ToString("N0");
            _signInsHint.Text = "successful";

            var failed = access.Count(e => e.Action == "LoginFailed");
            _failures.Text = failed.ToString("N0");
            _failures.ForeColor = failed > 0 ? UiTheme.Danger : UiTheme.TextPrimary;
            _failuresHint.Text = failed == 0 ? "none in this period" : "worth a look";

            _passwordChanges.Text = access
                .Count(e => e.Action is "PasswordChanged" or "PasswordReset").ToString("N0");
            _passwordChangesHint.Text = "changed or reset";

            _permissionChanges.Text = access
                .Count(e => e.Action == "PermissionsChanged").ToString("N0");
            _permissionChangesHint.Text = "module access edited";

            return access;
        }

        protected override void DefineColumns()
        {
            Column(nameof(AuditEventDto.OccurredAt), "When", 110, "d MMM yyyy HH:mm");
            StatusColumn(nameof(AuditEventDto.Action), "Event", 110);
            Column(nameof(AuditEventDto.Username), "Account", 120);
            Column(nameof(AuditEventDto.RoleKey), "Role", 80);
            Column(nameof(AuditEventDto.IpAddress), "From", 110);
            Column(nameof(AuditEventDto.Summary), "Detail", 260);
        }

        protected override bool Matches(AuditEventDto e, string term) =>
            (e.Username ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (e.Action ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (e.Summary ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
