using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Operating expenses: rent, utilities, repairs, fees - money out that is neither payroll
    /// nor stock.
    ///
    /// Note what does not belong here. Stock bought for resale is a purchase, not an expense:
    /// goods acquired to sell are an asset until they are sold, and recording a delivery as an
    /// expense makes the month it arrived look like a loss and the month it sells look like a
    /// windfall. The screen says so where the operator would otherwise guess.
    /// </summary>
    internal sealed class ExpensesPage : CrudPageBase<ExpenseDto>
    {
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly ComboBox _category;
        private readonly ComboBox _status;

        private readonly Label _total;
        private readonly Label _totalHint;
        private readonly Label _thisMonth;
        private readonly Label _thisMonthHint;
        private readonly Label _unpaid;
        private readonly Label _unpaidHint;
        private readonly Label _largest;
        private readonly Label _largestHint;

        private List<string> _categories = new();
        private List<SupplierDto> _suppliers = new();
        private List<BankAccountDto> _bankAccounts = new();

        public ExpensesPage(FitCoreSession session)
            : base(session, "Expenses",
                   "Rent, utilities, repairs and fees. Stock bought for resale is a purchase, not an expense.",
                   "expense", "Description, category, reference or payee")
        {
            var today = DateTime.Today;

            _from = UiKit.DatePicker(new DateTime(today.Year, today.Month, 1).AddMonths(-2));
            _to = UiKit.DatePicker(today);

            _category = UiKit.Select(170);
            _category.Items.Add("All categories");
            _category.SelectedIndex = 0;
            _category.SelectedIndexChanged += async (_, _) => await LoadAsync();

            _status = UiKit.Select(120);
            _status.Items.AddRange(new object[] { "All", "Paid", "Unpaid" });
            _status.SelectedIndex = 0;
            _status.SelectedIndexChanged += async (_, _) => await LoadAsync();

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);
            FilterBar.Controls.Add(UiKit.FilterLabel("Category"));
            FilterBar.Controls.Add(_category);
            FilterBar.Controls.Add(UiKit.FilterLabel("Status"));
            FilterBar.Controls.Add(_status);

            AddAction("Settle", ButtonTone.Success, SettleAsync, 86);

            StatsRow.Controls.Add(UiKit.StatCard("Total in range", out _total, out _totalHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("This month", out _thisMonth, out _thisMonthHint, UiTheme.Info));
            StatsRow.Controls.Add(UiKit.StatCard("Still owed", out _unpaid, out _unpaidHint, UiTheme.Danger));
            StatsRow.Controls.Add(UiKit.StatCard("Biggest category", out _largest, out _largestHint, UiTheme.Warning));
        }

        protected override string DeleteConsequence =>
            "The expense is removed and its ledger posting is reversed, so the books stay " +
            "consistent. Both the reversal and the original stay visible in the journal.";

        protected override string EmptyHeadline => "No expenses recorded";
        protected override string EmptyDetail =>
            "Record rent, utilities, repairs and fees here so they reach the profit and loss " +
            "account. Stock you buy to sell goes under Inventory → Purchases instead.";

        protected override async Task<List<ExpenseDto>?> FetchAsync()
        {
            if (_categories.Count == 0)
            {
                _categories = Unwrap(await Session.Expenses.GetCategoriesAsync()) ?? new List<string>();
                _category.Items.AddRange(_categories.Cast<object>().ToArray());
            }

            if (_suppliers.Count == 0)
            {
                _suppliers = Unwrap(await Session.Suppliers.GetAllAsync()) ?? new List<SupplierDto>();
            }

            if (_bankAccounts.Count == 0)
            {
                _bankAccounts = Unwrap(await Session.Finance.GetBankAccountsAsync(includeInactive: false))
                                ?? new List<BankAccountDto>();
            }

            var summary = Unwrap(await Session.Expenses.GetSummaryAsync(_from.Value.Date, _to.Value.Date));

            if (summary is not null)
            {
                _total.Text = summary.Total.ToString("N2");
                _totalHint.Text = $"{UiKit.Plural(summary.Count, "expense")}";

                _thisMonth.Text = summary.ThisMonth.ToString("N2");
                _thisMonthHint.Text = DateTime.Today.ToString("MMMM yyyy");

                _unpaid.Text = summary.Unpaid.ToString("N2");
                _unpaid.ForeColor = summary.Unpaid > 0m ? UiTheme.Danger : UiTheme.TextPrimary;
                _unpaidHint.Text = summary.UnpaidCount == 0
                    ? "everything settled"
                    : $"{UiKit.Plural(summary.UnpaidCount, "bill")} outstanding";

                var biggest = summary.ByGroup.FirstOrDefault();
                _largest.Text = biggest is null ? "—" : biggest.Value.ToString("N2");
                _largestHint.Text = biggest?.Label ?? "nothing yet";
            }

            return Unwrap(await Session.Expenses.GetAllAsync(
                _from.Value.Date,
                _to.Value.Date,
                _category.SelectedIndex <= 0 ? null : (string)_category.SelectedItem!,
                _status.SelectedIndex <= 0 ? null : (string)_status.SelectedItem!));
        }

        protected override void DefineColumns()
        {
            DateColumn(nameof(ExpenseDto.ExpenseDate), "Date", 75);
            Column(nameof(ExpenseDto.Category), "Category", 110);
            Column(nameof(ExpenseDto.Description), "Description", 190);
            Column(nameof(ExpenseDto.PaidTo), "Paid to", 110);
            MoneyColumn(nameof(ExpenseDto.Amount), "Amount", 85);
            Column(nameof(ExpenseDto.PaymentMethod), "Method", 70);
            StatusColumn(nameof(ExpenseDto.Status), "Status", 65);
            Column(nameof(ExpenseDto.AccountName), "Posted to", 130);
            Column(nameof(ExpenseDto.ReferenceNo), "Reference", 85);
        }

        protected override bool Matches(ExpenseDto e, string term) =>
            e.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.ReferenceNo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.PaidTo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.SupplierName.Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(ExpenseDto e) =>
            $"the {e.Amount:N2} {e.Category.ToLowerInvariant()} expense of {e.ExpenseDate:d MMM yyyy}";

        private List<FieldSpec> Fields(ExpenseDto? expense)
        {
            var suppliers = _suppliers
                .Where(s => s.IsActive)
                .Select(s => new KeyValuePair<string, string>(s.SupplierId.ToString(), s.SupplierName))
                .ToList();

            suppliers.Insert(0, new KeyValuePair<string, string>("", "— not a supplier —"));

            var accounts = _bankAccounts
                .Select(a => new KeyValuePair<string, string>(a.BankAccountId.ToString(), a.AccountName))
                .ToList();

            accounts.Insert(0, new KeyValuePair<string, string>("", "— not specified —"));

            return new List<FieldSpec>
            {
                new("category", "Category", FieldKind.Combo)
                {
                    Required = true,
                    Value = expense?.Category ?? "Other",
                    Options = _categories.Select(c => new KeyValuePair<string, string>(c, c)).ToList(),
                    Hint = "Decides which account this posts to on the profit and loss."
                },
                new("description", "What was it for?")
                    { Required = true, Value = expense?.Description, MaxLength = 300 },
                new("amount", "Amount", FieldKind.Money)
                    { Required = true, Value = expense?.Amount, Minimum = 0.01m },
                new("date", "Date", FieldKind.Date)
                    { Required = true, Value = expense?.ExpenseDate ?? DateTime.Today },

                new("status", "Status", FieldKind.Combo)
                {
                    Required = true,
                    Value = expense?.Status ?? "Paid",
                    Options = new List<KeyValuePair<string, string>>
                    {
                        new("Paid", "Paid — the money has gone"),
                        new("Unpaid", "Unpaid — owed, and shows in payables")
                    }
                },

                new("method", "Payment method", FieldKind.Combo)
                {
                    Value = expense?.PaymentMethod ?? "Cash",
                    Options = new List<KeyValuePair<string, string>>
                    {
                        new("Cash", "Cash"), new("Card", "Card"), new("Transfer", "Transfer"),
                        new("Check", "Check"), new("GCash", "GCash")
                    }
                },

                new("paidTo", "Paid to")
                {
                    Value = expense?.PaidTo, MaxLength = 150,
                    Hint = "The landlord, the electricity company - anyone who is not a supplier."
                },

                new("supplier", "Supplier", FieldKind.Combo)
                {
                    Value = expense?.SupplierId?.ToString() ?? "",
                    Options = suppliers
                },

                new("bank", "Paid from", FieldKind.Combo)
                {
                    Value = expense?.BankAccountId?.ToString() ?? "",
                    Options = accounts
                },

                new("reference", "Reference") { Value = expense?.ReferenceNo, MaxLength = 60 }
            };
        }

        protected override Task<bool> OnAddAsync()
        {
            var saved = EditDialog.Run(this, "Record an expense",
                "Recording it as unpaid puts it in accounts payable until it is settled.",
                Fields(null), async f =>
                {
                    var result = await Session.Expenses.CreateAsync(Build(f));
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Record expense");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(ExpenseDto expense)
        {
            var saved = EditDialog.Run(this, "Edit expense",
                "The existing ledger posting is reversed and a fresh one written, so the " +
                "correction is visible rather than the history being rewritten.",
                Fields(expense), async f =>
                {
                    var result = await Session.Expenses.UpdateAsync(
                        expense.ExpenseId, BuildUpdate(f));
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        private static CreateExpenseDto Build(IList<FieldSpec> f) => Fill(new CreateExpenseDto(), f);

        private static UpdateExpenseDto BuildUpdate(IList<FieldSpec> f) =>
            (UpdateExpenseDto)Fill(new UpdateExpenseDto(), f);

        private static CreateExpenseDto Fill(CreateExpenseDto dto, IList<FieldSpec> f)
        {
            dto.Category = f.First(x => x.Key == "category").Text;
            dto.Description = f.First(x => x.Key == "description").Text;
            dto.Amount = f.First(x => x.Key == "amount").Decimal;
            dto.ExpenseDate = f.First(x => x.Key == "date").Date;
            dto.Status = f.First(x => x.Key == "status").Text;
            dto.PaymentMethod = f.First(x => x.Key == "method").Text;
            dto.PaidTo = f.First(x => x.Key == "paidTo").Text;
            dto.ReferenceNo = f.First(x => x.Key == "reference").Text;
            dto.SupplierId = ParseId(f.First(x => x.Key == "supplier").ComboValue);
            dto.BankAccountId = ParseId(f.First(x => x.Key == "bank").ComboValue);

            return dto;
        }

        protected override async Task<string?> OnDeleteAsync(ExpenseDto expense)
        {
            var result = await Session.Expenses.DeleteAsync(expense.ExpenseId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        private async Task SettleAsync()
        {
            var expense = Selected;

            if (expense is null)
            {
                ShowError("Select the expense that has been paid.");
                return;
            }

            if (expense.Status != "Unpaid")
            {
                ShowError("That expense has already been settled.");
                return;
            }

            var accounts = _bankAccounts
                .Select(a => new KeyValuePair<string, string>(a.BankAccountId.ToString(), a.AccountName))
                .ToList();

            accounts.Insert(0, new KeyValuePair<string, string>("", "— not specified —"));

            var settled = EditDialog.Run(this, "Settle expense",
                $"{expense.Description} · {expense.Amount:N2}. This clears it from accounts " +
                "payable and moves the money out of cash.",
                new List<FieldSpec>
                {
                    new("method", "Paid by", FieldKind.Combo)
                    {
                        Required = true,
                        Value = expense.PaymentMethod,
                        Options = new List<KeyValuePair<string, string>>
                        {
                            new("Cash", "Cash"), new("Card", "Card"), new("Transfer", "Transfer"),
                            new("Check", "Check"), new("GCash", "GCash")
                        }
                    },
                    new("bank", "From account", FieldKind.Combo)
                    {
                        Value = expense.BankAccountId?.ToString() ?? "",
                        Options = accounts
                    }
                },
                async f =>
                {
                    var result = await Session.Expenses.SettleAsync(expense.ExpenseId,
                        f.First(x => x.Key == "method").Text,
                        ParseId(f.First(x => x.Key == "bank").ComboValue));

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Settle");

            if (!settled) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify("Expense settled.");
            }, "Refreshing…");
        }

        private static int? ParseId(string? value) =>
            int.TryParse(value, out var id) && id > 0 ? id : null;
    }
}
