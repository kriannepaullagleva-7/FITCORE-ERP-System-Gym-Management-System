using System.ComponentModel;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Cash and bank accounts: the till, the bank, the e-wallet, and what has moved through
    /// each.
    ///
    /// Every one is backed by a ledger account, so the balance shown here and the balance the
    /// trial balance reports are the same number arrived at two ways. When they differ, the
    /// difference is shown rather than hidden - that is what reconciliation is for.
    /// </summary>
    internal sealed class BankAccountsPage : CrudPageBase<BankAccountDto>
    {
        public BankAccountsPage(FitCoreSession session)
            : base(session, "Cash & Bank",
                   "Where the gym's money physically sits, and what the ledger says about each.",
                   "account", "Name, bank or kind")
        {
            AddAction("Movements", ButtonTone.Secondary, ShowMovementsAsync, 106);
            AddAction("Record", ButtonTone.Primary, RecordMovementAsync, 92);
        }

        protected override string DeleteConsequence =>
            "The account is removed. This is only possible while nothing has been recorded " +
            "against it - otherwise deactivate it instead, which keeps the history.";

        protected override string EmptyHeadline => "No cash or bank accounts yet";
        protected override string EmptyDetail =>
            "Add the till and each bank account, so money coming in and going out can be " +
            "reconciled against what the ledger says.";

        protected override async Task<List<BankAccountDto>?> FetchAsync() =>
            Unwrap(await Session.Finance.GetBankAccountsAsync());

        protected override void DefineColumns()
        {
            Column(nameof(BankAccountDto.AccountName), "Account", 150);
            Column(nameof(BankAccountDto.Kind), "Kind", 70);
            Column(nameof(BankAccountDto.BankName), "Bank", 120);
            Column(nameof(BankAccountDto.AccountNumber), "Number", 80);
            MoneyColumn(nameof(BankAccountDto.CurrentBalance), "Balance", 90);
            MoneyColumn(nameof(BankAccountDto.LedgerBalance), "Per ledger", 90);
            MoneyColumn(nameof(BankAccountDto.Difference), "Difference", 85);
            Column(nameof(BankAccountDto.UnreconciledCount), "Unreconciled", 70, rightAlign: true);
            FlagColumn(nameof(BankAccountDto.IsActive), "Status", 60);
        }

        protected override bool Matches(BankAccountDto a, string term) =>
            a.AccountName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            a.BankName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            a.Kind.Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(BankAccountDto a) => $"“{a.AccountName}”";

        private static List<FieldSpec> Fields(BankAccountDto? account) => new()
        {
            new("name", "Account name")
                { Required = true, Value = account?.AccountName, MaxLength = 150 },

            new("kind", "Kind", FieldKind.Combo)
            {
                Required = true,
                Value = account?.Kind ?? CashAccountKinds.Bank,
                Options = CashAccountKinds.All
                    .Select(k => new KeyValuePair<string, string>(k, k)).ToList(),
                Hint = "Cash is the till; bank and e-wallet are reconciled against a statement."
            },

            new("bank", "Bank") { Value = account?.BankName, MaxLength = 150 },

            new("number", "Account number")
            {
                Value = account?.AccountNumber, MaxLength = 60,

                // FitCore never needs the whole number, and asking for one invites it into a
                // database that has no use for it.
                Hint = "The last four digits are enough."
            },

            new("notes", "Notes", FieldKind.Multiline) { Value = account?.Notes, MaxLength = 300 }
        };

        protected override Task<bool> OnAddAsync()
        {
            var fields = Fields(null);

            fields.Insert(4, new FieldSpec("opening", "Opening balance", FieldKind.Money)
            {
                Hint = "What is in the account today. Set once, when the account is added."
            });

            var saved = EditDialog.Run(this, "Add cash or bank account", "", fields, async f =>
            {
                var result = await Session.Finance.CreateBankAccountAsync(new CreateBankAccountDto
                {
                    AccountName = f.First(x => x.Key == "name").Text,
                    Kind = f.First(x => x.Key == "kind").Text,
                    BankName = f.First(x => x.Key == "bank").Text,
                    AccountNumber = f.First(x => x.Key == "number").Text,
                    OpeningBalance = f.First(x => x.Key == "opening").Decimal,
                    Notes = f.First(x => x.Key == "notes").Text
                });

                return result.IsSuccess ? null : result.ErrorMessage;
            }, "Create account");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(BankAccountDto account)
        {
            var fields = Fields(account);
            fields.Add(new FieldSpec("active", "Active", FieldKind.Check) { Value = account.IsActive });

            var saved = EditDialog.Run(this, $"Edit {account.AccountName}", "", fields, async f =>
            {
                var result = await Session.Finance.UpdateBankAccountAsync(
                    account.BankAccountId, new UpdateBankAccountDto
                    {
                        AccountName = f.First(x => x.Key == "name").Text,
                        Kind = f.First(x => x.Key == "kind").Text,
                        BankName = f.First(x => x.Key == "bank").Text,
                        AccountNumber = f.First(x => x.Key == "number").Text,
                        Notes = f.First(x => x.Key == "notes").Text,
                        IsActive = f.First(x => x.Key == "active").Flag
                    });

                return result.IsSuccess ? null : result.ErrorMessage;
            }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(BankAccountDto account)
        {
            var result = await Session.Finance.DeleteBankAccountAsync(account.BankAccountId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        private async Task ShowMovementsAsync()
        {
            var account = Selected;

            if (account is null)
            {
                ShowError("Select an account to see its movements.");
                return;
            }

            await GuardAsync(async () =>
            {
                var movements = Unwrap(await Session.Finance.GetBankTransactionsAsync(
                    account.BankAccountId));

                if (movements is null) return;

                if (movements.Count == 0)
                {
                    UiKit.Info($"Nothing has been recorded against {account.AccountName} yet.",
                        account.AccountName);
                    return;
                }

                ListDialog.Show(this,
                    $"{account.AccountName} · movements",
                    $"Balance {account.CurrentBalance:N2} · " +
                    $"{account.UnreconciledCount} not yet reconciled",
                    movements.Select(m => new
                    {
                        Date = m.TransactionDate.ToLocalTime().ToString("d MMM yyyy"),
                        m.Direction,
                        Amount = m.Amount.ToString("N2"),
                        Balance = m.BalanceAfter.ToString("N2"),
                        m.Reference,
                        m.Description,
                        Reconciled = m.IsReconciled ? "Yes" : "No"
                    }).ToList());
            }, "Loading movements…");
        }

        private async Task RecordMovementAsync()
        {
            var account = Selected;

            if (account is null)
            {
                ShowError("Select the account the money moved through.");
                return;
            }

            var saved = EditDialog.Run(this, $"Record a movement · {account.AccountName}",
                "For money that reaches the account outside the till - a bank transfer, a fee, " +
                "an owner's contribution.",
                new List<FieldSpec>
                {
                    new("date", "Date", FieldKind.Date) { Required = true, Value = DateTime.Today },
                    new("direction", "Direction", FieldKind.Combo)
                    {
                        Required = true,
                        Value = "In",
                        Options = new List<KeyValuePair<string, string>>
                        {
                            new("In", "Money in"),
                            new("Out", "Money out")
                        }
                    },
                    new("amount", "Amount", FieldKind.Money) { Required = true, Minimum = 0.01m },
                    new("reference", "Reference") { MaxLength = 60 },
                    new("description", "Description", FieldKind.Multiline) { MaxLength = 300 }
                },
                async f =>
                {
                    var result = await Session.Finance.RecordBankTransactionAsync(
                        account.BankAccountId, new RecordBankTransactionDto
                        {
                            TransactionDate = f.First(x => x.Key == "date").Date,
                            Direction = f.First(x => x.Key == "direction").Text,
                            Amount = f.First(x => x.Key == "amount").Decimal,
                            Reference = f.First(x => x.Key == "reference").Text,
                            Description = f.First(x => x.Key == "description").Text
                        });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Record movement");

            if (!saved) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify("Movement recorded.");
            }, "Refreshing…");
        }
    }

    /// <summary>
    /// Bank reconciliation: agreeing what the statement says with what the ledger says.
    ///
    /// The difference between the two is the whole report. A reconciliation that comes to zero
    /// is a quiet confirmation; one that does not is the gym's earliest warning that something
    /// was recorded twice, or not at all.
    /// </summary>
    internal sealed class ReconciliationPage : ModulePageBase
    {
        private readonly ComboBox _account;
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;

        private readonly Label _statement;
        private readonly Label _statementHint;
        private readonly Label _ledger;
        private readonly Label _ledgerHint;
        private readonly Label _difference;
        private readonly Label _differenceHint;
        private readonly Label _outstanding;
        private readonly Label _outstandingHint;

        private List<BankAccountDto> _accounts = new();

        public ReconciliationPage(FitCoreSession session)
            : base(session, "Bank Reconciliation",
                   "Agreeing the statement with the ledger, and finding out why they differ.")
        {
            var today = DateTime.Today;

            _account = UiKit.Select(240);
            _account.SelectedIndexChanged += async (_, _) => await LoadReconciliationAsync();

            _from = UiKit.DatePicker(new DateTime(today.Year, today.Month, 1));
            _to = UiKit.DatePicker(today);

            FilterBar.Controls.Add(UiKit.FilterLabel("Account"));
            FilterBar.Controls.Add(_account);
            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);

            AddAction("Apply", ButtonTone.Secondary, LoadReconciliationAsync, 86);
            AddAction("Reconcile", ButtonTone.Success, ReconcileAsync, 106);

            StatsRow.Controls.Add(UiKit.StatCard("Per the account", out _statement, out _statementHint, UiTheme.Info));
            StatsRow.Controls.Add(UiKit.StatCard("Per the ledger", out _ledger, out _ledgerHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("Difference", out _difference, out _differenceHint, UiTheme.Warning));
            StatsRow.Controls.Add(UiKit.StatCard("Not yet agreed", out _outstanding, out _outstandingHint, UiTheme.Neutral));

            Grid.AutoGenerateColumns = false;
            Grid.MultiSelect = true;
            Grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

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

            Column(nameof(BankTransactionDto.TransactionDate), "Date", 75, format: "d MMM yyyy");
            Column(nameof(BankTransactionDto.Direction), "Direction", 60);
            Column(nameof(BankTransactionDto.Amount), "Amount", 85, right: true, format: "N2");
            Column(nameof(BankTransactionDto.Reference), "Reference", 90);
            Column(nameof(BankTransactionDto.Description), "Description", 200);
            Column(nameof(BankTransactionDto.PerformedBy), "Recorded by", 100);

            UiKit.SetMinimumColumnWidths(Grid);
        }

        public override async Task LoadAsync()
        {
            await GuardAsync(async () =>
            {
                var accounts = Unwrap(await Session.Finance.GetBankAccountsAsync(includeInactive: false));
                if (accounts is null) return;

                _accounts = accounts;

                if (_accounts.Count == 0)
                {
                    ShowEmptyState("No cash or bank accounts yet",
                        "Add an account under Cash & Bank before reconciling anything.");
                    SetStatus("Nothing to reconcile.");
                    return;
                }

                _account.DisplayMember = "Value";
                _account.ValueMember = "Key";
                _account.DataSource = _accounts
                    .Select(a => new KeyValuePair<int, string>(a.BankAccountId, a.AccountName))
                    .ToList();

                await LoadReconciliationAsync();
            }, "Loading accounts…");
        }

        private async Task LoadReconciliationAsync()
        {
            if (_account.SelectedValue is not int accountId) return;

            await GuardAsync(async () =>
            {
                var view = Unwrap(await Session.Finance.GetReconciliationAsync(
                    accountId, _from.Value.Date, _to.Value.Date));

                if (view is null) return;

                Grid.DataSource = new BindingList<BankTransactionDto>(view.Unreconciled);

                _statement.Text = view.StatementBalance.ToString("N2");
                _statementHint.Text = "running balance";

                _ledger.Text = view.LedgerBalance.ToString("N2");
                _ledgerHint.Text = "posted to the ledger";

                _difference.Text = view.Difference.ToString("N2");
                _difference.ForeColor = view.Difference == 0m ? UiTheme.Success : UiTheme.Danger;
                _differenceHint.Text = view.Difference == 0m
                    ? "the two agree"
                    : "worth explaining";

                _outstanding.Text = view.UnreconciledTotal.ToString("N2");
                _outstandingHint.Text = $"{UiKit.Plural(view.UnreconciledCount, "movement")}";

                if (view.UnreconciledCount == 0)
                {
                    ShowEmptyState("Everything is reconciled",
                        $"All {view.ReconciledCount} movement(s) in this period have been agreed " +
                        "against the ledger.");
                }
                else
                {
                    HideEmptyState();
                }

                SetStatus(
                    $"{view.UnreconciledCount} movement(s) waiting, " +
                    $"{view.ReconciledCount} already agreed. " +
                    "Select the ones that match your statement and choose Reconcile.");
            }, "Reconciling…");
        }

        private async Task ReconcileAsync()
        {
            var selected = Grid.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(r => r.DataBoundItem as BankTransactionDto)
                .Where(t => t is not null)
                .Select(t => t!.BankTransactionId)
                .ToList();

            if (selected.Count == 0)
            {
                ShowError("Select the movements that appear on your statement.");
                return;
            }

            await GuardAsync(async () =>
            {
                var result = await Session.Finance.ReconcileManyAsync(selected);

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadReconciliationAsync();

                Notify($"{UiKit.Plural(selected.Count, "movement")} marked as reconciled.");
            }, "Reconciling…");
        }
    }
}
