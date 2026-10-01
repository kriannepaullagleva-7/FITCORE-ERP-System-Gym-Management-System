using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The journal: every double-entry posting, automatic and manual.
    ///
    /// Most of what appears here was raised by another module - a sale, a payment, a pay run -
    /// and is not editable, because the ledger is the financial record of something that
    /// happened rather than a view of what somebody currently believes. Correcting an entry
    /// means reversing it, which leaves both on record.
    /// </summary>
    internal sealed class JournalPage : CrudPageBase<JournalEntryDto>
    {
        private readonly ComboBox _source;
        private readonly ComboBox _status;
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;

        private List<AccountDto> _accounts = new();

        public JournalPage(FitCoreSession session)
            : base(session, "Journal Entries",
                   "Every posting in the ledger. Automatic entries are corrected by reversing them, never by editing.",
                   "journal entry", "Entry number, memo, reference or account")
        {
            var today = DateTime.Today;

            _from = UiKit.DatePicker(new DateTime(today.Year, today.Month, 1).AddMonths(-2));
            _to = UiKit.DatePicker(today);

            _source = UiKit.Select(150);
            _source.Items.Add("All sources");
            _source.SelectedIndex = 0;
            _source.SelectedIndexChanged += async (_, _) => await LoadAsync();

            _status = UiKit.Select(130);
            _status.Items.AddRange(new object[] { "All statuses", "Posted", "Draft", "Void" });
            _status.SelectedIndex = 0;
            _status.SelectedIndexChanged += async (_, _) => await LoadAsync();

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);
            FilterBar.Controls.Add(UiKit.FilterLabel("Source"));
            FilterBar.Controls.Add(_source);
            FilterBar.Controls.Add(UiKit.FilterLabel("Status"));
            FilterBar.Controls.Add(_status);

            AddAction("Lines", ButtonTone.Secondary, ShowLinesAsync, 80);
            AddAction("Reverse", ButtonTone.Warning, ReverseAsync, 92);
        }

        protected override bool SupportsDelete => false;

        protected override string CreatedMessage => "Journal entry posted.";
        protected override string UpdatedMessage => "Draft entry saved.";

        protected override string EmptyHeadline => "No entries in this period";
        protected override string EmptyDetail =>
            "Sales, payments, purchases and pay runs post here automatically as they happen. " +
            "You can also write an entry by hand.";

        protected override async Task<List<JournalEntryDto>?> FetchAsync()
        {
            if (_accounts.Count == 0)
            {
                _accounts = Unwrap(await Session.Finance.GetAccountsAsync(withBalances: false))
                            ?? new List<AccountDto>();
            }

            if (_source.Items.Count == 1)
            {
                var sources = Unwrap(await Session.Finance.GetJournalSourcesAsync());

                if (sources is not null)
                {
                    _source.Items.AddRange(sources.Cast<object>().ToArray());
                }
            }

            return Unwrap(await Session.Finance.GetJournalAsync(
                _from.Value.Date,
                _to.Value.Date,
                _source.SelectedIndex <= 0 ? null : (string)_source.SelectedItem!,
                _status.SelectedIndex <= 0 ? null : (string)_status.SelectedItem!));
        }

        protected override void DefineColumns()
        {
            Column(nameof(JournalEntryDto.EntryNo), "Entry", 70);
            DateColumn(nameof(JournalEntryDto.EntryDate), "Date", 75);
            Column(nameof(JournalEntryDto.Source), "Source", 75);
            Column(nameof(JournalEntryDto.Memo), "Memo", 190);
            Column(nameof(JournalEntryDto.Summary), "Accounts", 160);
            MoneyColumn(nameof(JournalEntryDto.TotalDebit), "Amount", 85);
            StatusColumn(nameof(JournalEntryDto.DisplayStatus), "Status", 70);
            Column(nameof(JournalEntryDto.ProcessedBy), "By", 90);
        }

        protected override bool Matches(JournalEntryDto e, string term) =>
            e.EntryNo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.Memo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.Reference.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.Summary.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.Lines.Any(l => l.AccountName.Contains(term, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ writing

        /// <summary>
        /// A hand-written entry, restricted to two lines.
        ///
        /// That is the shape of nearly every manual correction - move this much from that
        /// account to this one - and it keeps the dialog to something an operator can fill in
        /// without a spreadsheet. Anything more complicated is several entries, which is also
        /// easier to read back a year later.
        /// </summary>
        protected override Task<bool> OnAddAsync()
        {
            var options = _accounts
                .Where(a => a.IsActive)
                .OrderBy(a => a.AccountCode)
                .Select(a => new KeyValuePair<string, string>(a.AccountId.ToString(), a.Display))
                .ToList();

            if (options.Count < 2)
            {
                ShowError("At least two active accounts are needed before an entry can be written.");
                return Task.FromResult(false);
            }

            var fields = new List<FieldSpec>
            {
                new("date", "Entry date", FieldKind.Date) { Required = true, Value = DateTime.Today },
                new("memo", "What is this for?")
                    { Required = true, MaxLength = 300, Hint = "Read back later as the reason for the entry." },
                new("reference", "Reference") { MaxLength = 60 },

                new("debit", "Debit account", FieldKind.Combo)
                    { Required = true, Options = options, Hint = "The account receiving the value." },
                new("credit", "Credit account", FieldKind.Combo)
                    { Required = true, Options = options, Hint = "The account giving it up." },

                new("amount", "Amount", FieldKind.Money)
                    { Required = true, Minimum = 0.01m, Hint = "Both sides move by this much." },

                new("post", "Post immediately", FieldKind.Check)
                {
                    Value = true,
                    Hint = "A draft is excluded from every balance until it is posted."
                }
            };

            var saved = EditDialog.Run(this, "New journal entry",
                "Every entry moves the same amount out of one account and into another.",
                fields, async f =>
                {
                    var debit = f.First(x => x.Key == "debit").ComboValue;
                    var credit = f.First(x => x.Key == "credit").ComboValue;

                    if (debit == credit)
                    {
                        return "The two accounts must be different - an entry that debits and " +
                               "credits the same account moves nothing.";
                    }

                    var amount = f.First(x => x.Key == "amount").Decimal;

                    var result = await Session.Finance.CreateJournalEntryAsync(new CreateJournalEntryDto
                    {
                        EntryDate = f.First(x => x.Key == "date").Date,
                        Memo = f.First(x => x.Key == "memo").Text,
                        Reference = f.First(x => x.Key == "reference").Text,
                        Post = f.First(x => x.Key == "post").Flag,
                        Lines = new List<JournalLineDto>
                        {
                            new() { AccountId = int.Parse(debit!), Debit = amount },
                            new() { AccountId = int.Parse(credit!), Credit = amount }
                        }
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create entry");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(JournalEntryDto entry)
        {
            if (!entry.IsEditable)
            {
                ShowError(entry.Status == "Posted"
                    ? $"{entry.EntryNo} has been posted. Reverse it and write the correction, " +
                      "so both stay on record."
                    : $"{entry.EntryNo} was raised automatically by {entry.SourceModule}. " +
                      "Correct it in the module that produced it.");

                return Task.FromResult(false);
            }

            var fields = new List<FieldSpec>
            {
                new("date", "Entry date", FieldKind.Date) { Required = true, Value = entry.EntryDate },
                new("memo", "What is this for?")
                    { Required = true, Value = entry.Memo, MaxLength = 300 },
                new("reference", "Reference") { Value = entry.Reference, MaxLength = 60 }
            };

            var saved = EditDialog.Run(this, $"Edit {entry.EntryNo}",
                "Only the date, memo and reference can be changed here. To change the amounts, " +
                "discard this draft and write it again.",
                fields, async f =>
                {
                    var result = await Session.Finance.UpdateJournalEntryAsync(
                        entry.JournalEntryId, new UpdateJournalEntryDto
                        {
                            EntryDate = f.First(x => x.Key == "date").Date,
                            Memo = f.First(x => x.Key == "memo").Text,
                            Reference = f.First(x => x.Key == "reference").Text,
                            Lines = entry.Lines
                        });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save draft");

            return Task.FromResult(saved);
        }

        private async Task ShowLinesAsync()
        {
            var entry = Selected;

            if (entry is null)
            {
                ShowError("Select an entry to see its lines.");
                return;
            }

            await GuardAsync(async () =>
            {
                var detail = Unwrap(await Session.Finance.GetJournalEntryAsync(entry.JournalEntryId));
                if (detail is null) return;

                ListDialog.Show(this,
                    $"{detail.EntryNo} · {detail.Memo}",
                    $"Debits {detail.TotalDebit:N2} · credits {detail.TotalCredit:N2} · " +
                    $"{detail.Status.ToLowerInvariant()} by {detail.ProcessedBy}",
                    detail.Lines.Select(l => new
                    {
                        Account = $"{l.AccountCode} {l.AccountName}",
                        Type = l.AccountType,
                        l.Description,
                        Debit = l.Debit == 0m ? "" : l.Debit.ToString("N2"),
                        Credit = l.Credit == 0m ? "" : l.Credit.ToString("N2")
                    }).ToList());
            }, "Loading…");
        }

        private async Task ReverseAsync()
        {
            var entry = Selected;

            if (entry is null)
            {
                ShowError("Select an entry to reverse.");
                return;
            }

            if (entry.IsReversed)
            {
                ShowError($"{entry.EntryNo} has already been reversed.");
                return;
            }

            if (entry.Status != "Posted")
            {
                ShowError($"{entry.EntryNo} is {entry.Status.ToLowerInvariant()}, so there is " +
                          "nothing in the ledger to reverse.");
                return;
            }

            var reversed = EditDialog.Run(this, $"Reverse {entry.EntryNo}",
                "A mirror-image entry is written. Both stay visible, so the books show what was " +
                "believed at the time as well as the correction.",
                new List<FieldSpec>
                {
                    new("reason", "Why is this being reversed?", FieldKind.Multiline)
                        { Required = true, MaxLength = 300 }
                },
                async f =>
                {
                    var result = await Session.Finance.ReverseJournalEntryAsync(
                        entry.JournalEntryId, f.First(x => x.Key == "reason").Text);

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Reverse entry");

            if (!reversed) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"{entry.EntryNo} reversed.");
            }, "Refreshing…");
        }
    }
}
