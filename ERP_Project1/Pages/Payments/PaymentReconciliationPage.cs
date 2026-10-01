using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The takings, the ledger and the bank, set against each other.
    ///
    /// Three records of the same money, written by three different paths, so the number worth
    /// looking at is not any one of them but where they disagree.
    ///
    /// A gap is possible by design rather than by accident. <c>FinancePostingService</c> never
    /// fails the operation that triggered it: a payment is always taken even when the journal
    /// entry behind it could not be written. That is the right trade - a till that refuses money
    /// because the books are busy is worse than books that need catching up - but it means
    /// something has to find the arrears afterwards. This is that something, and the grid names
    /// the individual payments that are missing a posting.
    ///
    /// So a variance here is a finding, not an error, and the repair is the same catch-up sweep
    /// the Finance module offers. The button for it only appears for somebody who holds Journal
    /// Entries, because that is what the endpoint behind it is guarded on - offering it to a
    /// manager who has Payments but not Finance would be offering a 403.
    /// </summary>
    internal sealed class PaymentReconciliationPage : ModulePageBase
    {
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;

        private readonly DataGridView _grid;
        private readonly FlowLayoutPanel _headline;
        private readonly Label _caption;

        private PaymentReconciliationDto? _view;

        protected override bool UsesGrid => false;

        public PaymentReconciliationPage(FitCoreSession session)
            : base(session, "Reconciliation",
                   "Takings against the ledger and the bank, and anything still unposted.")
        {
            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            _from = UiKit.DatePicker(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
            FilterBar.Controls.Add(_from);

            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            _to = UiKit.DatePicker(DateTime.Today);
            FilterBar.Controls.Add(_to);

            FilterBar.Controls.Add(UiKit.Action("Apply", ButtonTone.Primary,
                async (_, _) => await LoadAsync(), 92));

            AddAction("Refresh", ButtonTone.Secondary, () => LoadAsync(), 90);

            // Only for somebody who may actually post. The tab itself is Payments; the sweep is
            // a Finance action, and the two are granted separately.
            if (session.CurrentUser?.CanUse(Submodules.JournalEntries) == true)
            {
                AddAction("Post unposted", ButtonTone.Warning, () => PostOutstandingAsync(), 126);
            }

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

        public override Task LoadAsync() => GuardAsync(async () =>
        {
            var from = _from.Value.Date;
            var to = _to.Value.Date;

            if (to < from)
            {
                ShowError("The “to” date cannot be before the “from” date.");
                return;
            }

            var view = Unwrap(await Session.Reports.GetPaymentReconciliationAsync(from, to));
            if (view is null) return;

            _view = view;
            Render(view);
        }, "Reconciling…");

        private void Render(PaymentReconciliationDto view)
        {
            _headline.Controls.Clear();

            Figure("Takings", UiKit.Money(view.Takings));
            Figure("Posted to ledger", UiKit.Money(view.PostedToLedger));

            // The one figure the screen exists for, so it is coloured by what it means rather
            // than by its sign: zero is the answer everybody wants, either direction is a gap.
            Figure("Variance", UiKit.Money(view.Variance),
                view.IsBalanced ? UiTheme.Success : UiTheme.Danger);

            Figure("Unposted", $"{view.UnpostedPaymentCount:N0}",
                view.UnpostedPaymentCount > 0 ? UiTheme.Danger : UiTheme.Success);
            Figure("Banked in", UiKit.Money(view.BankedIn));
            Figure("Unreconciled bank", $"{view.UnreconciledBankCount:N0}",
                view.UnreconciledBankCount > 0 ? UiTheme.Warning : UiTheme.Success);

            var range = $"{UiKit.Date(view.FromUtc)} to {UiKit.Date(view.ToUtc.AddDays(-1))}";

            // The unposted payments are the actionable list, so they win the grid when there are
            // any. With none, the day-by-day comparison is what is worth reading.
            if (view.UnpostedPayments.Count > 0)
            {
                _grid.DataSource = view.UnpostedPayments;
                _caption.Text = $"Payments with no ledger entry — {range}";

                SetStatus(
                    $"{UiKit.Plural(view.UnpostedPaymentCount, "payment")} worth " +
                    $"{UiKit.Money(view.UnpostedPaymentAmount)} never reached the books. " +
                    "Posting them is safe to repeat.");
                return;
            }

            _grid.DataSource = view.ByDay;
            _caption.Text = $"Takings against the ledger, by day — {range}";

            SetStatus(view.IsBalanced
                ? $"Balanced: {UiKit.Money(view.Takings)} taken and {UiKit.Money(view.PostedToLedger)} posted."
                : $"Out by {UiKit.Money(Math.Abs(view.Variance))}. Every payment has an entry, so " +
                  "the difference is in the amounts rather than a missing posting.");
        }

        /// <summary>
        /// Runs the same catch-up sweep the Finance module offers, scoped from the start of the
        /// period on screen. Safe to repeat: posting is keyed by source, so anything already in
        /// the books is skipped rather than doubled.
        /// </summary>
        private async Task PostOutstandingAsync()
        {
            if (_view is null)
            {
                ShowError("Reconcile a period first, so there is something to post.");
                return;
            }

            if (_view.UnpostedPaymentCount == 0)
            {
                ShowError("Every payment in this period already has a ledger entry.");
                return;
            }

            var confirmed = UiKit.ConfirmDelete(this,
                $"Post {UiKit.Plural(_view.UnpostedPaymentCount, "missing entry")} to the ledger?",
                $"This writes journal entries worth {UiKit.Money(_view.UnpostedPaymentAmount)} for " +
                "payments that were taken but never posted. Anything already posted is skipped, " +
                "so nothing is double-counted, and a closed period is refused.");

            if (!confirmed) return;

            await GuardAsync(async () =>
            {
                var result = Unwrap(await Session.Finance.PostOutstandingAsync(_from.Value.Date));
                if (result is null) return;

                await LoadAsync();

                Notify(result.Posted == 0
                    ? "Nothing needed posting."
                    : $"Posted {UiKit.Plural(result.Posted, "entry", "entries")}." +
                      (result.Remaining > 0
                          ? $" {result.Remaining:N0} still outstanding - the server log has why."
                          : ""));
            }, "Posting…");
        }

        /// <summary>One headline figure on the strip above the grid.</summary>
        private void Figure(string label, string value, Color? accent = null)
        {
            var box = new Panel
            {
                Width = 168,
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
                Text = value,
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
