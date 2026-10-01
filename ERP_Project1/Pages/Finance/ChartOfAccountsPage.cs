using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The chart of accounts: every account the gym's money is tracked against, and what has
    /// been posted to each.
    ///
    /// The standard chart is seeded on first use, so this screen is never empty and the
    /// posting rules always have somewhere to post. Those seeded accounts can be renamed and
    /// recoded - how the chart reads belongs to the operator - but not deleted or retyped,
    /// because a sale has to credit product revenue whatever anyone calls it.
    /// </summary>
    internal sealed class ChartOfAccountsPage : CrudPageBase<AccountDto>
    {
        private readonly ComboBox _type;
        private List<AccountDto> _all = new();

        public ChartOfAccountsPage(FitCoreSession session)
            : base(session, "Chart of Accounts",
                   "Every account the books are kept in, with what has been posted to each.",
                   "account", "Code, name or type")
        {
            _type = UiKit.Select(150);
            _type.Items.Add("All types");
            _type.Items.AddRange(AccountTypes.All.Cast<object>().ToArray());
            _type.SelectedIndex = 0;
            _type.SelectedIndexChanged += (_, _) => ApplyTypeFilter();

            FilterBar.Controls.Add(UiKit.FilterLabel("Type"));
            FilterBar.Controls.Add(_type);

            AddAction("Ledger", ButtonTone.Secondary, OpenLedgerAsync, 90);
        }

        protected override string DeleteConsequence =>
            "The account is removed from the chart. This is only possible while nothing has " +
            "ever been posted to it - otherwise deactivate it instead, which stops it being " +
            "offered without disturbing the history.";

        protected override string EmptyHeadline => "No accounts yet";
        protected override string EmptyDetail =>
            "The standard chart is created the first time Finance is opened. If this stays " +
            "empty, the tenant database may not be reachable.";

        protected override async Task<List<AccountDto>?> FetchAsync()
        {
            var accounts = Unwrap(await Session.Finance.GetAccountsAsync());
            if (accounts is null) return null;

            _all = accounts;
            return Filtered();
        }

        private List<AccountDto> Filtered() =>
            _type.SelectedIndex <= 0
                ? _all
                : _all.Where(a => a.AccountType == (string)_type.SelectedItem!).ToList();

        private void ApplyTypeFilter()
        {
            Items = Filtered();
            ApplyFilter();
        }

        protected override void DefineColumns()
        {
            Column(nameof(AccountDto.AccountCode), "Code", 50);
            Column(nameof(AccountDto.AccountName), "Account", 170);
            Column(nameof(AccountDto.AccountType), "Type", 70);
            Column(nameof(AccountDto.AccountSubType), "Group", 100);
            MoneyColumn(nameof(AccountDto.TotalDebit), "Debits", 80);
            MoneyColumn(nameof(AccountDto.TotalCredit), "Credits", 80);
            MoneyColumn(nameof(AccountDto.Balance), "Balance", 85);
            Column(nameof(AccountDto.EntryCount), "Postings", 55, rightAlign: true);
            FlagColumn(nameof(AccountDto.IsActive), "Status", 60);
        }

        protected override bool Matches(AccountDto a, string term) =>
            a.AccountCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            a.AccountName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            a.AccountType.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            a.AccountSubType.Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(AccountDto a) =>
            $"“{a.AccountCode} {a.AccountName}”";

        private List<FieldSpec> Fields(AccountDto? account)
        {
            var parents = _all
                .Where(a => account is null || a.AccountId != account.AccountId)
                .OrderBy(a => a.AccountCode)
                .Select(a => new KeyValuePair<string, string>(a.AccountId.ToString(), a.Display))
                .ToList();

            parents.Insert(0, new KeyValuePair<string, string>("", "— none —"));

            var fields = new List<FieldSpec>
            {
                new("code", "Account code")
                {
                    Required = true, Value = account?.AccountCode, MaxLength = 20,
                    Hint = "Conventionally 1000s assets, 2000s liabilities, 3000s equity, " +
                           "4000s revenue, 5000s expenses."
                },
                new("name", "Account name") { Required = true, Value = account?.AccountName, MaxLength = 150 },
                new("type", "Account type", FieldKind.Combo)
                {
                    Required = true,
                    Value = account?.AccountType ?? AccountTypes.Expense,
                    Options = AccountTypes.All
                        .Select(t => new KeyValuePair<string, string>(t, t)).ToList(),

                    // A seeded account's type is what the posting rules depend on. The server
                    // refuses to change it, so the dialog does not invite the attempt.
                    ReadOnly = account?.IsSystemAccount == true
                },
                new("subtype", "Group", FieldKind.Combo)
                {
                    Value = account?.AccountSubType ?? "",
                    Hint = "Decides where the account appears on the statements.",
                    Options = new List<KeyValuePair<string, string>>
                    {
                        new("", "— none —"),
                        new("Current Asset", "Current Asset"),
                        new("Fixed Asset", "Fixed Asset"),
                        new("Current Liability", "Current Liability"),
                        new("Owner's Equity", "Owner's Equity"),
                        new("Operating Revenue", "Operating Revenue"),
                        new("Contra Revenue", "Contra Revenue"),
                        new("Cost of Sales", "Cost of Sales"),
                        new("Payroll Expense", "Payroll Expense"),
                        new("Operating Expense", "Operating Expense")
                    }
                },
                new("parent", "Sits under", FieldKind.Combo)
                {
                    Value = account?.ParentAccountId?.ToString() ?? "",
                    Options = parents,
                    Hint = "Optional. A parent must be the same type."
                },
                new("description", "Description", FieldKind.Multiline)
                    { Value = account?.Description, MaxLength = 300 }
            };

            return fields;
        }

        protected override Task<bool> OnAddAsync()
        {
            var saved = EditDialog.Run(this, "Add account",
                "Accounts you add are yours: nothing posts to them automatically, and they can " +
                "be removed again while they are still empty.",
                Fields(null), async f =>
                {
                    var result = await Session.Finance.CreateAccountAsync(new CreateAccountDto
                    {
                        AccountCode = f.First(x => x.Key == "code").Text,
                        AccountName = f.First(x => x.Key == "name").Text,
                        AccountType = f.First(x => x.Key == "type").Text,
                        AccountSubType = f.First(x => x.Key == "subtype").Text,
                        Description = f.First(x => x.Key == "description").Text,
                        ParentAccountId = ParseId(f.First(x => x.Key == "parent").ComboValue)
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create account");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(AccountDto account)
        {
            var fields = Fields(account);

            fields.Add(new FieldSpec("active", "Active", FieldKind.Check)
            {
                Value = account.IsActive,
                ReadOnly = account.IsSystemAccount,
                Hint = account.IsSystemAccount
                    ? "This account is used by automatic posting and cannot be switched off."
                    : "An inactive account stops being offered but keeps its history."
            });

            var saved = EditDialog.Run(this, $"Edit {account.AccountName}",
                account.IsSystemAccount
                    ? "This is a standard account. You can rename and recode it; its type and " +
                      "availability are fixed because the posting rules depend on them."
                    : "",
                fields, async f =>
                {
                    var result = await Session.Finance.UpdateAccountAsync(
                        account.AccountId, new UpdateAccountDto
                        {
                            AccountCode = f.First(x => x.Key == "code").Text,
                            AccountName = f.First(x => x.Key == "name").Text,
                            AccountType = f.First(x => x.Key == "type").Text,
                            AccountSubType = f.First(x => x.Key == "subtype").Text,
                            Description = f.First(x => x.Key == "description").Text,
                            ParentAccountId = ParseId(f.First(x => x.Key == "parent").ComboValue),
                            IsActive = f.First(x => x.Key == "active").Flag
                        });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(AccountDto account)
        {
            var result = await Session.Finance.DeleteAccountAsync(account.AccountId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        /// <summary>Opens the selected account's postings, with a running balance.</summary>
        private async Task OpenLedgerAsync()
        {
            var account = Selected;

            if (account is null)
            {
                ShowError("Select an account to see its ledger.");
                return;
            }

            await GuardAsync(async () =>
            {
                var ledger = Unwrap(await Session.Finance.GetLedgerAsync(account.AccountId));
                if (ledger is null) return;

                if (ledger.Rows.Count == 0)
                {
                    UiKit.Info(
                        $"Nothing has been posted to {account.AccountName} in this period.",
                        account.Display);
                    return;
                }

                ListDialog.Show(this,
                    $"{account.AccountCode} {account.AccountName}",
                    $"Opening balance {ledger.OpeningBalance:N2} · closing {ledger.ClosingBalance:N2}",
                    ledger.Rows.Select(r => new
                    {
                        Date = r.EntryDate.ToLocalTime().ToString("d MMM yyyy"),
                        Entry = r.EntryNo,
                        r.Description,
                        Debit = r.Debit == 0m ? "" : r.Debit.ToString("N2"),
                        Credit = r.Credit == 0m ? "" : r.Credit.ToString("N2"),
                        Balance = r.Balance.ToString("N2")
                    }).ToList());
            }, "Reading the ledger…");
        }

        private static int? ParseId(string? value) =>
            int.TryParse(value, out var id) && id > 0 ? id : null;
    }
}
