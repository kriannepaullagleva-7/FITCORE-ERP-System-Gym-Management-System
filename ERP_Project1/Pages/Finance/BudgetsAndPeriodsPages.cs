using System.ComponentModel;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Budgets: what the gym plans to earn and spend, by account and month, against what it
    /// actually did.
    ///
    /// A budget is never posted. It expresses intent rather than a transaction, so it sits
    /// beside the ledger and Budget vs Actual reads both - the ledger is never adjusted to
    /// match the plan.
    /// </summary>
    internal sealed class BudgetsPage : CrudPageBase<BudgetDto>
    {
        private readonly ComboBox _year;
        private List<AccountDto> _accounts = new();

        public BudgetsPage(FitCoreSession session)
            : base(session, "Budgets",
                   "What the gym plans to earn and spend, and how the year is running against it.",
                   "budget", "Name or status")
        {
            _year = UiKit.Select(110);

            for (var year = DateTime.Today.Year + 1; year >= DateTime.Today.Year - 3; year--)
            {
                _year.Items.Add(year);
            }

            _year.SelectedItem = DateTime.Today.Year;
            _year.SelectedIndexChanged += async (_, _) => await LoadAsync();

            FilterBar.Controls.Add(UiKit.FilterLabel("Year"));
            FilterBar.Controls.Add(_year);

            AddAction("Lines", ButtonTone.Secondary, ShowLinesAsync, 82);
            AddAction("vs Actual", ButtonTone.Secondary, ShowVarianceAsync, 96);
            AddAction("Budget an account", ButtonTone.Primary, BudgetAccountAsync, 148);
            AddAction("Approve", ButtonTone.Success, ApproveAsync, 92);
        }

        protected override string DeleteConsequence =>
            "The budget and all of its lines are removed. Nothing in the ledger changes - a " +
            "budget is a plan, not a posting. This cannot be undone.";

        protected override string EmptyHeadline => "No budget for this year";
        protected override string EmptyDetail =>
            "Create one, then set a figure per account per month. Budget vs Actual compares it " +
            "against what was actually posted.";

        protected override async Task<List<BudgetDto>?> FetchAsync()
        {
            if (_accounts.Count == 0)
            {
                var accounts = Unwrap(await Session.Finance.GetAccountsAsync(withBalances: false));

                // Only revenue and expense accounts can be budgeted. Budgeting a bank balance
                // is a category error - it is a consequence of the plan, not the plan.
                _accounts = (accounts ?? new List<AccountDto>())
                    .Where(a => a.IsActive &&
                                a.AccountType is AccountTypes.Revenue or AccountTypes.Expense)
                    .OrderBy(a => a.AccountCode)
                    .ToList();
            }

            return Unwrap(await Session.Finance.GetBudgetsAsync((int)_year.SelectedItem!));
        }

        protected override void DefineColumns()
        {
            Column(nameof(BudgetDto.Name), "Budget", 190);
            Column(nameof(BudgetDto.Year), "Year", 55, rightAlign: true);
            StatusColumn(nameof(BudgetDto.Status), "Status", 75);
            MoneyColumn(nameof(BudgetDto.TotalBudgeted), "Budgeted", 95);
            Column(nameof(BudgetDto.LineCount), "Lines", 55, rightAlign: true);
            Column(nameof(BudgetDto.ApprovedBy), "Approved by", 110);
            DateColumn(nameof(BudgetDto.CreatedAt), "Created", 80);
        }

        protected override bool Matches(BudgetDto b, string term) =>
            b.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            b.Status.Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(BudgetDto b) => $"“{b.Name}” ({b.Year})";

        protected override Task<bool> OnAddAsync()
        {
            var saved = EditDialog.Run(this, "Create a budget",
                "Set figures per account afterwards, then approve it - only an approved budget " +
                "is what Budget vs Actual reports against.",
                new List<FieldSpec>
                {
                    new("name", "Budget name")
                        { Required = true, MaxLength = 150, Value = $"{_year.SelectedItem} plan" },
                    new("year", "Year", FieldKind.Integer)
                        { Required = true, Value = _year.SelectedItem, Minimum = 2000, Maximum = 2100 },
                    new("notes", "Notes", FieldKind.Multiline) { MaxLength = 300 }
                },
                async f =>
                {
                    var result = await Session.Finance.CreateBudgetAsync(new CreateBudgetDto
                    {
                        Name = f.First(x => x.Key == "name").Text,
                        Year = f.First(x => x.Key == "year").Int,
                        Notes = f.First(x => x.Key == "notes").Text
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create budget");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(BudgetDto budget)
        {
            var saved = EditDialog.Run(this, $"Edit {budget.Name}", "",
                new List<FieldSpec>
                {
                    new("name", "Budget name")
                        { Required = true, Value = budget.Name, MaxLength = 150 },
                    new("year", "Year", FieldKind.Integer)
                        { Required = true, Value = budget.Year, Minimum = 2000, Maximum = 2100 },
                    new("notes", "Notes", FieldKind.Multiline)
                        { Value = budget.Notes, MaxLength = 300 }
                },
                async f =>
                {
                    var result = await Session.Finance.UpdateBudgetAsync(
                        budget.BudgetId, new CreateBudgetDto
                        {
                            Name = f.First(x => x.Key == "name").Text,
                            Year = f.First(x => x.Key == "year").Int,
                            Notes = f.First(x => x.Key == "notes").Text
                        });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(BudgetDto budget)
        {
            var result = await Session.Finance.DeleteBudgetAsync(budget.BudgetId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        /// <summary>
        /// Sets an account's figure for the year, spread evenly across the months.
        ///
        /// That is how a budget actually starts life. Adjusting an individual month afterwards
        /// is a second, rarer act, and making the common case twelve dialogs would mean nobody
        /// ever finishes a budget.
        /// </summary>
        private async Task BudgetAccountAsync()
        {
            var budget = Selected;

            if (budget is null)
            {
                ShowError("Select the budget to add a line to.");
                return;
            }

            if (_accounts.Count == 0)
            {
                ShowError("There are no revenue or expense accounts to budget against yet.");
                return;
            }

            var saved = EditDialog.Run(this, $"Budget an account · {budget.Name}",
                "The annual figure is spread evenly across the twelve months. Adjust individual " +
                "months afterwards if the year is not flat.",
                new List<FieldSpec>
                {
                    new("account", "Account", FieldKind.Combo)
                    {
                        Required = true,
                        Options = _accounts
                            .Select(a => new KeyValuePair<string, string>(
                                a.AccountId.ToString(), $"{a.Display}  ({a.AccountType})"))
                            .ToList()
                    },
                    new("amount", "Annual amount", FieldKind.Money)
                        { Required = true, Minimum = 0m }
                },
                async f =>
                {
                    var result = await Session.Finance.SpreadBudgetAsync(
                        budget.BudgetId, new SpreadBudgetDto
                        {
                            AccountId = int.Parse(f.First(x => x.Key == "account").ComboValue!),
                            AnnualAmount = f.First(x => x.Key == "amount").Decimal
                        });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Set budget");

            if (!saved) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify("Budget updated.");
            }, "Refreshing…");
        }

        private async Task ShowLinesAsync()
        {
            var budget = Selected;

            if (budget is null)
            {
                ShowError("Select a budget to see its lines.");
                return;
            }

            await GuardAsync(async () =>
            {
                var detail = Unwrap(await Session.Finance.GetBudgetAsync(budget.BudgetId));
                if (detail is null) return;

                if (detail.Lines.Count == 0)
                {
                    UiKit.Info("Nothing has been budgeted yet. Use “Budget an account” to start.",
                        budget.Name);
                    return;
                }

                ListDialog.Show(this,
                    $"{detail.Name} · lines",
                    $"{detail.TotalBudgeted:N2} budgeted across {detail.LineCount} line(s)",
                    detail.Lines.Select(l => new
                    {
                        Account = $"{l.AccountCode} {l.AccountName}",
                        Type = l.AccountType,
                        Month = new DateTime(detail.Year, Math.Clamp(l.Month, 1, 12), 1)
                            .ToString("MMMM"),
                        Amount = l.Amount.ToString("N2")
                    }).ToList());
            }, "Loading…");
        }

        private async Task ShowVarianceAsync()
        {
            var budget = Selected;

            if (budget is null)
            {
                ShowError("Select a budget to compare against actuals.");
                return;
            }

            await GuardAsync(async () =>
            {
                var variance = Unwrap(await Session.Finance.GetBudgetVarianceAsync(budget.BudgetId));
                if (variance is null) return;

                if (variance.Rows.Count == 0)
                {
                    UiKit.Info("Nothing has been budgeted yet, so there is nothing to compare.",
                        budget.Name);
                    return;
                }

                ListDialog.Show(this,
                    $"{variance.BudgetName} · budget vs actual",
                    $"Budgeted {variance.TotalBudgeted:N2} · actual {variance.TotalActual:N2} · " +
                    $"variance {variance.TotalVariance:N2}",
                    variance.Rows.Select(r => new
                    {
                        Account = $"{r.AccountCode} {r.AccountName}",
                        Budgeted = r.Budgeted.ToString("N2"),
                        Actual = r.Actual.ToString("N2"),
                        Variance = r.Variance.ToString("N2"),
                        Percent = r.VariancePercent.ToString("N1") + "%",

                        // Spending more than planned is bad news and earning more is good, so
                        // the sign alone does not tell the reader which they are looking at.
                        Verdict = r.IsFavourable ? "Favourable" : "Unfavourable"
                    }).ToList());
            }, "Comparing…");
        }

        private async Task ApproveAsync()
        {
            var budget = Selected;

            if (budget is null)
            {
                ShowError("Select a budget to approve.");
                return;
            }

            if (UiKit.Confirm(
                    $"Approve “{budget.Name}” as the budget for {budget.Year}?\r\n\r\n" +
                    "Budget vs Actual will report against it, and any other approved budget for " +
                    "the same year is archived.")
                != DialogResult.Yes)
            {
                return;
            }

            await GuardAsync(async () =>
            {
                var result = await Session.Finance.ApproveBudgetAsync(budget.BudgetId);

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadAsync();
                Notify($"“{budget.Name}” approved.");
            }, "Approving…");
        }
    }

    /// <summary>
    /// Financial periods: the months the ledger will accept postings into.
    ///
    /// A month with no row is open, so a tenant that never uses this feature is unaffected.
    /// Closing one is what stops last quarter's figures moving after they have been reported,
    /// and it has to be done in order - closing April while March is still open would leave
    /// March editable behind a report that already went out.
    /// </summary>
    internal sealed class FinancialPeriodsPage : ModulePageBase
    {
        private readonly ComboBox _year;

        public FinancialPeriodsPage(FitCoreSession session)
            : base(session, "Financial Periods",
                   "The months the ledger accepts postings into. Closing one freezes its figures.")
        {
            _year = UiKit.Select(110);

            for (var year = DateTime.Today.Year + 1; year >= DateTime.Today.Year - 3; year--)
            {
                _year.Items.Add(year);
            }

            _year.SelectedItem = DateTime.Today.Year;
            _year.SelectedIndexChanged += async (_, _) => await LoadAsync();

            FilterBar.Controls.Add(UiKit.FilterLabel("Year"));
            FilterBar.Controls.Add(_year);

            AddAction("Close", ButtonTone.Warning, ClosePeriodAsync, 86);
            AddAction("Reopen", ButtonTone.Secondary, ReopenPeriodAsync, 92);

            Grid.AutoGenerateColumns = false;

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

            Column(nameof(FinancialPeriodDto.DisplayName), "Period", 120);
            Column(nameof(FinancialPeriodDto.Status), "Status", 80);
            Column(nameof(FinancialPeriodDto.EntryCount), "Postings", 70, right: true, format: "N0");
            Column(nameof(FinancialPeriodDto.TotalPosted), "Value posted", 100, right: true, format: "N2");
            Column(nameof(FinancialPeriodDto.ClosedBy), "Closed by", 100);
            Column(nameof(FinancialPeriodDto.Notes), "Notes", 180);

            UiKit.PaintStatusColumns(Grid, nameof(FinancialPeriodDto.Status));
            UiKit.SetMinimumColumnWidths(Grid);
        }

        private FinancialPeriodDto? Selected => Grid.CurrentRow?.DataBoundItem as FinancialPeriodDto;

        public override async Task LoadAsync()
        {
            await GuardAsync(async () =>
            {
                var periods = Unwrap(await Session.Finance.GetPeriodsAsync((int)_year.SelectedItem!));
                if (periods is null) return;

                Grid.DataSource = new BindingList<FinancialPeriodDto>(periods);

                var closed = periods.Count(p => p.Status == "Closed");

                SetStatus(closed == 0
                    ? $"All twelve months of {_year.SelectedItem} are open."
                    : $"{closed} of 12 months closed. Closed months refuse new postings.");
            }, "Loading periods…");
        }

        private async Task ClosePeriodAsync()
        {
            var period = Selected;

            if (period is null)
            {
                ShowError("Select the month to close.");
                return;
            }

            if (period.Status == "Closed")
            {
                ShowError($"{period.DisplayName} is already closed.");
                return;
            }

            if (!UiKit.ConfirmDelete(this,
                    $"Close {period.DisplayName}?",
                    $"{period.EntryCount} posting(s) worth {period.TotalPosted:N2} are in this " +
                    "month. Once closed, nothing new can be posted into it - including a sale " +
                    "or payment back-dated into it - until it is reopened.",
                    "Close period"))
            {
                return;
            }

            await GuardAsync(async () =>
            {
                var result = await Session.Finance.ClosePeriodAsync(
                    period.Year, period.Month, "Closed from the Financial Periods screen.");

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadAsync();
                Notify($"{period.DisplayName} closed.");
            }, "Closing…");
        }

        private async Task ReopenPeriodAsync()
        {
            var period = Selected;

            if (period is null)
            {
                ShowError("Select the month to reopen.");
                return;
            }

            if (period.Status != "Closed")
            {
                ShowError($"{period.DisplayName} is already open.");
                return;
            }

            var reopened = EditDialog.Run(this, $"Reopen {period.DisplayName}",
                "Reopening a closed month lets its figures change after they have been " +
                "reported. Say why, so the reason is on record.",
                new List<FieldSpec>
                {
                    new("reason", "Why is this being reopened?", FieldKind.Multiline)
                        { Required = true, MaxLength = 300 }
                },
                async f =>
                {
                    var result = await Session.Finance.ReopenPeriodAsync(
                        period.Year, period.Month, f.First(x => x.Key == "reason").Text);

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Reopen period");

            if (!reopened) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"{period.DisplayName} reopened.");
            }, "Refreshing…");
        }
    }
}
