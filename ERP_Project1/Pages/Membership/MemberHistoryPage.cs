using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Everything on record for one member: their subscriptions, payments, sales and notes.
    ///
    /// The Members tab answers "who is this?"; this answers "what has happened with them?", which
    /// is the question at the desk when somebody disputes a charge or asks when their membership
    /// actually lapsed. Four separate screens would each need the member chosen again.
    ///
    /// Each list is fetched from that member's own sub-resource rather than by pulling the whole
    /// gym's history and filtering here. The server already knows how to answer "this member's",
    /// and the alternative sends every subscription in the tenant across the wire to show one
    /// person's four.
    /// </summary>
    internal sealed class MemberHistoryPage : ModulePageBase
    {
        /// <summary>Which of the member's four records the grid is showing.</summary>
        private enum Record
        {
            Subscriptions,
            Payments,
            Sales,
            Notes
        }

        private readonly TextBox _search;
        private readonly ComboBox _member;
        private readonly ComboBox _which;

        private readonly DataGridView _grid;
        private readonly FlowLayoutPanel _headline;
        private readonly Label _caption;

        private List<MemberDto> _members = new();

        /// <summary>Set while the member list is being repopulated, so it does not reload per item.</summary>
        private bool _loadingMembers;

        protected override bool UsesGrid => false;

        public MemberHistoryPage(FitCoreSession session)
            : base(session, "Member History",
                   "One member's subscriptions, payments, sales and notes.")
        {
            FilterBar.Controls.Add(UiKit.FilterLabel("Find"));

            _search = new TextBox { Font = UiTheme.Body, BorderStyle = BorderStyle.None };
            var searchShell = UiKit.InputShell(_search, 190);
            searchShell.Margin = new Padding(0, 4, UiTheme.SpaceS, 0);
            _search.TextChanged += (_, _) => FillMembers();
            FilterBar.Controls.Add(searchShell);

            FilterBar.Controls.Add(UiKit.FilterLabel("Member"));

            _member = UiKit.Select(230);
            _member.DisplayMember = "Value";
            _member.ValueMember = "Key";
            _member.SelectedIndexChanged += async (_, _) =>
            {
                if (_loadingMembers) return;
                await LoadAsync();
            };
            FilterBar.Controls.Add(_member);

            FilterBar.Controls.Add(UiKit.FilterLabel("Showing"));

            _which = UiKit.Select(150);
            _which.Items.AddRange(new object[] { "Subscriptions", "Payments", "Sales", "Notes" });
            _which.SelectedIndex = 0;
            _which.SelectedIndexChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_which);

            AddAction("Refresh", ButtonTone.Secondary, () => ReloadMembersAsync(), 90);

            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(1)
            };
            card.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, card.Width, card.Height),
                UiTheme.Surface, UiTheme.Border);

            _headline = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 78,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.Surface,
                Padding = new Padding(14, 14, 14, 10)
            };

            _caption = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                BackColor = UiTheme.Surface,
                UseMnemonic = false
            };

            _grid = UiKit.Grid();
            _grid.AutoGenerateColumns = true;
            UiKit.HumaniseColumns(_grid);

            card.Controls.Add(_grid);
            card.Controls.Add(_caption);
            card.Controls.Add(_headline);

            SetBody(card);
        }

        private Record Current => (Record)Math.Max(0, _which.SelectedIndex);

        private int? SelectedMemberId =>
            _member.SelectedValue is int id && id > 0 ? id : null;

        /// <summary>
        /// A member's display name. Members carry no code of their own - the id is their
        /// identity - so the picker shows the name with the id beside it rather than inventing
        /// a reference number that nothing else in the system would recognise.
        /// </summary>
        private static string MemberName(MemberDto member) =>
            $"{member.FirstName} {member.LastName}".Trim();

        // ------------------------------------------------------------------ the member list

        /// <summary>
        /// Reads the roll once and keeps it, so narrowing the picker is a local filter rather
        /// than a request per keystroke.
        /// </summary>
        private async Task ReloadMembersAsync()
        {
            await GuardAsync(async () =>
            {
                var members = Unwrap(await Session.Members.GetAllAsync());
                if (members is null) return;

                _members = members
                    .OrderBy(MemberName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                FillMembers();

                await LoadHistoryAsync();
            }, "Loading members…");
        }

        private void FillMembers()
        {
            var term = _search.Text.Trim();

            var matches = string.IsNullOrWhiteSpace(term)
                ? _members
                : _members.Where(m =>
                        MemberName(m).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        (m.Email ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        (m.Phone ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        m.MemberId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            var previous = SelectedMemberId;

            _loadingMembers = true;

            _member.DataSource = matches
                .Select(m => new KeyValuePair<int, string>(
                    m.MemberId, $"{MemberName(m)} (#{m.MemberId})"))
                .ToList();

            // Stay on the same member across a re-filter where that is still possible, so
            // typing does not silently move the operator onto somebody else's history.
            if (previous is not null)
            {
                var index = matches.FindIndex(m => m.MemberId == previous.Value);
                if (index >= 0) _member.SelectedIndex = index;
            }

            _loadingMembers = false;
        }

        // ------------------------------------------------------------------ loading

        public override async Task LoadAsync()
        {
            if (_members.Count == 0)
            {
                await ReloadMembersAsync();
                return;
            }

            await GuardAsync(LoadHistoryAsync, "Loading history…");
        }

        private async Task LoadHistoryAsync()
        {
            _grid.DataSource = null;
            _headline.Controls.Clear();

            var memberId = SelectedMemberId;

            if (memberId is null)
            {
                _caption.Text = "No member selected";

                ShowEmptyState(
                    _members.Count == 0 ? "No members yet" : "No member matches that search",
                    _members.Count == 0
                        ? "Register a member on the Members tab, and their history appears here."
                        : "Clear the search box to see the whole roll.",
                    _members.Count == 0 ? null : "Clear search",
                    _members.Count == 0 ? null : (_, _) => _search.Clear());

                SetStatus("No member selected");
                return;
            }

            HideEmptyState();

            var member = _members.First(m => m.MemberId == memberId.Value);

            Figure("Status", member.Status);
            Figure("Joined", UiKit.Date(member.JoinDate));
            Figure("Member no.", $"#{member.MemberId}");

            switch (Current)
            {
                case Record.Subscriptions:
                    await LoadSubscriptionsAsync(member);
                    break;
                case Record.Payments:
                    await LoadPaymentsAsync(member);
                    break;
                case Record.Sales:
                    await LoadSalesAsync(member);
                    break;
                case Record.Notes:
                    await LoadNotesAsync(member);
                    break;
            }
        }

        private async Task LoadSubscriptionsAsync(MemberDto member)
        {
            var rows = Unwrap(await Session.Members.GetSubscriptionsAsync(member.MemberId));
            if (rows is null) return;

            Figure("Subscriptions", rows.Count.ToString("N0"));
            Figure("Active", rows.Count(s =>
                string.Equals(s.Status, "Active", StringComparison.OrdinalIgnoreCase)).ToString("N0"),
                UiTheme.Success);

            _grid.DataSource = rows
                .OrderByDescending(s => s.StartDate)
                .ToList();

            _caption.Text = $"{MemberName(member)} · subscriptions";
            SetStatus(Count(rows.Count, "subscription"));
        }

        private async Task LoadPaymentsAsync(MemberDto member)
        {
            var rows = Unwrap(await Session.Members.GetPaymentsAsync(member.MemberId));
            if (rows is null) return;

            var collected = rows
                .Where(p => string.Equals(p.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                .Sum(p => p.Amount);

            Figure("Payments", rows.Count.ToString("N0"));
            Figure("Collected", UiKit.Money(collected), UiTheme.Success);

            _grid.DataSource = rows
                .OrderByDescending(p => p.PaymentDate)
                .ToList();

            _caption.Text = $"{MemberName(member)} · payments";
            SetStatus(Count(rows.Count, "payment"));
        }

        private async Task LoadSalesAsync(MemberDto member)
        {
            var rows = Unwrap(await Session.Members.GetSalesAsync(member.MemberId));
            if (rows is null) return;

            var live = rows
                .Where(s => !string.Equals(s.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Figure("Sales", live.Count.ToString("N0"));
            Figure("Spent", UiKit.Money(live.Sum(s => s.TotalAmount)), UiTheme.Primary);

            _grid.DataSource = rows
                .OrderByDescending(s => s.SaleDate)
                .ToList();

            _caption.Text = $"{MemberName(member)} · sales";
            SetStatus(Count(rows.Count, "sale"));
        }

        private async Task LoadNotesAsync(MemberDto member)
        {
            var rows = Unwrap(await Session.Notes.GetNotesAsync(member.MemberId));
            if (rows is null) return;

            Figure("Notes", rows.Count.ToString("N0"));
            Figure("Pinned", rows.Count(n => n.IsPinned).ToString("N0"));

            // Pinned first, then newest: a pinned note is pinned because it should be read
            // before the rest, and sorting it back into date order defeats the pin.
            _grid.DataSource = rows
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.CreatedAt)
                .ToList();

            _caption.Text = $"{MemberName(member)} · notes";
            SetStatus(Count(rows.Count, "note"));
        }

        private static string Count(int count, string noun) =>
            count == 0 ? $"No {noun}s on record" : UiKit.Plural(count, noun);

        /// <summary>One headline figure on the strip above the grid.</summary>
        private void Figure(string label, string value, Color? accent = null)
        {
            var box = new Panel
            {
                Width = 150,
                Height = 50,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, 0, 10, 0)
            };

            box.Controls.Add(new Label
            {
                Text = label.ToUpperInvariant(),
                Font = UiTheme.Overline,
                ForeColor = UiTheme.TextMuted,
                Location = new Point(0, 0),
                AutoSize = true,
                UseMnemonic = false
            });

            box.Controls.Add(new Label
            {
                Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                Font = new Font(UiTheme.FamilySemibold, 13F),
                ForeColor = accent ?? UiTheme.TextPrimary,
                Location = new Point(0, 18),
                AutoSize = true,
                UseMnemonic = false
            });

            _headline.Controls.Add(box);
        }
    }
}
