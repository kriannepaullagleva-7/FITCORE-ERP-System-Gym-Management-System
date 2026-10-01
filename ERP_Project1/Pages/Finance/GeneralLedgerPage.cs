using System.ComponentModel;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The general ledger: one account at a time, every posting against it, with a running
    /// balance.
    ///
    /// This is the screen an accountant actually works in - "show me everything that hit cash
    /// in March" - and it is the one place the running balance is shown, because a balance is
    /// only meaningful in the order the postings happened.
    /// </summary>
    internal sealed class GeneralLedgerPage : ModulePageBase
    {
        private readonly ComboBox _account;
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;

        private readonly Label _opening;
        private readonly Label _openingHint;
        private readonly Label _debits;
        private readonly Label _debitsHint;
        private readonly Label _credits;
        private readonly Label _creditsHint;
        private readonly Label _closing;
        private readonly Label _closingHint;

        private List<AccountDto> _accounts = new();

        public GeneralLedgerPage(FitCoreSession session)
            : base(session, "General Ledger",
                   "Every posting against one account, in the order they happened.")
        {
            var today = DateTime.Today;

            _account = UiKit.Select(300);
            _account.SelectedIndexChanged += async (_, _) => await LoadLedgerAsync();

            _from = UiKit.DatePicker(new DateTime(today.Year, today.Month, 1));
            _to = UiKit.DatePicker(today);

            FilterBar.Controls.Add(UiKit.FilterLabel("Account"));
            FilterBar.Controls.Add(_account);
            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);

            AddAction("Apply", ButtonTone.Secondary, LoadLedgerAsync, 86);

            StatsRow.Controls.Add(UiKit.StatCard("Opening balance", out _opening, out _openingHint, UiTheme.Neutral));
            StatsRow.Controls.Add(UiKit.StatCard("Debits", out _debits, out _debitsHint, UiTheme.Info));
            StatsRow.Controls.Add(UiKit.StatCard("Credits", out _credits, out _creditsHint, UiTheme.Warning));
            StatsRow.Controls.Add(UiKit.StatCard("Closing balance", out _closing, out _closingHint, UiTheme.Primary));

            Grid.AutoGenerateColumns = false;
            DefineColumns();
        }

        private void DefineColumns()
        {
            void Column(string property, string header, int fill, bool right = false, string? format = null)
            {
                var column = new DataGridViewTextBoxColumn
                {
                    Name = property,
                    DataPropertyName = property,
                    HeaderText = header,
                    FillWeight = fill
                };

                if (right) column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                if (format is not null) column.DefaultCellStyle.Format = format;

                Grid.Columns.Add(column);
            }

            Column(nameof(LedgerRowDto.EntryDate), "Date", 70, format: "d MMM yyyy");
            Column(nameof(LedgerRowDto.EntryNo), "Entry", 65);
            Column(nameof(LedgerRowDto.Source), "Source", 70);
            Column(nameof(LedgerRowDto.Reference), "Reference", 80);
            Column(nameof(LedgerRowDto.Description), "Description", 200);
            Column(nameof(LedgerRowDto.Debit), "Debit", 80, right: true, format: "N2");
            Column(nameof(LedgerRowDto.Credit), "Credit", 80, right: true, format: "N2");
            Column(nameof(LedgerRowDto.Balance), "Balance", 85, right: true, format: "N2");

            UiKit.SetMinimumColumnWidths(Grid);
        }

        public override async Task LoadAsync()
        {
            await GuardAsync(async () =>
            {
                var accounts = Unwrap(await Session.Finance.GetAccountsAsync(withBalances: false));
                if (accounts is null) return;

                _accounts = accounts.Where(a => a.IsActive).OrderBy(a => a.AccountCode).ToList();

                if (_accounts.Count == 0)
                {
                    ShowEmptyState("No accounts yet",
                        "The standard chart is created the first time Finance is opened.");
                    return;
                }

                _account.DisplayMember = "Value";
                _account.ValueMember = "Key";
                _account.DataSource = _accounts
                    .Select(a => new KeyValuePair<int, string>(a.AccountId, a.Display))
                    .ToList();

                await LoadLedgerAsync();
            }, "Loading accounts…");
        }

        private async Task LoadLedgerAsync()
        {
            if (_account.SelectedValue is not int accountId) return;

            await GuardAsync(async () =>
            {
                var ledger = Unwrap(await Session.Finance.GetLedgerAsync(
                    accountId, _from.Value.Date, _to.Value.Date));

                if (ledger is null) return;

                Grid.DataSource = new BindingList<LedgerRowDto>(ledger.Rows);

                _opening.Text = ledger.OpeningBalance.ToString("N2");
                _openingHint.Text = $"before {ledger.FromUtc:d MMM yyyy}";

                _debits.Text = ledger.TotalDebit.ToString("N2");
                _debitsHint.Text = "in this period";

                _credits.Text = ledger.TotalCredit.ToString("N2");
                _creditsHint.Text = "in this period";

                _closing.Text = ledger.ClosingBalance.ToString("N2");
                _closingHint.Text = $"{ledger.AccountType.ToLowerInvariant()} account";

                if (ledger.Rows.Count == 0)
                {
                    ShowEmptyState(
                        $"Nothing posted to {ledger.AccountName}",
                        "No entry touched this account in the period selected. Try a wider date range.");
                }
                else
                {
                    HideEmptyState();
                }

                SetStatus(
                    $"{UiKit.Plural(ledger.Rows.Count, "posting")} against " +
                    $"{ledger.AccountCode} {ledger.AccountName}.");
            }, "Reading the ledger…");
        }
    }
}
