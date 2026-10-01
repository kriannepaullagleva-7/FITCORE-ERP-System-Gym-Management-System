using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class FinanceReportService : IFinanceReportService
    {
        private readonly TenantErpDbContext _context;
        private readonly IAccountService _accounts;

        public FinanceReportService(TenantErpDbContext context, IAccountService accounts)
        {
            _context = context;
            _accounts = accounts;
        }

        // ================================================================== overview

        public async Task<FinanceOverview> GetOverviewAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null)
        {
            await _accounts.EnsureChartOfAccountsAsync();

            var range = ReportRange.Resolve(fromUtc, toUtc);

            var income = await GetIncomeStatementAsync(range.FromUtc, range.ToUtc.AddDays(-1));
            var sheet = await GetBalanceSheetAsync(range.ToUtc.AddDays(-1));

            var view = new FinanceOverview
            {
                FromUtc = range.FromUtc,
                ToUtc = range.ToUtc,

                Revenue = income.NetRevenue,
                CostOfGoodsSold = income.CostOfGoodsSold,
                GrossProfit = income.GrossProfit,
                OperatingExpenses = income.OperatingExpenses,
                PayrollExpenses = income.PayrollExpenses,
                NetIncome = income.NetIncome,

                TotalAssets = sheet.TotalAssets,
                TotalLiabilities = sheet.TotalLiabilities,
                TotalEquity = sheet.TotalEquity
            };

            var balances = await BalancesAsync(range.ToUtc);

            view.CashOnHand = Sum(balances, AccountKeys.Cash) + Sum(balances, AccountKeys.Bank);
            view.AccountsReceivable = Sum(balances, AccountKeys.AccountsReceivable);
            view.AccountsPayable = Sum(balances, AccountKeys.AccountsPayable);
            view.InventoryValue = Sum(balances, AccountKeys.Inventory);

            view.OpenPeriods = await _context.FinancialPeriods
                .CountAsync(p => p.Status == PeriodStatuses.Open);

            // Twelve months back from the end of the window, so the trend has context rather
            // than showing a single bar for the month the operator happens to be looking at.
            var trendStart = new DateTime(range.ToUtc.Year, range.ToUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMonths(-11);

            var monthly = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate >= trendStart &&
                            l.Entry.EntryDate < range.ToUtc &&
                            (l.Account.AccountType == AccountTypes.Revenue ||
                             l.Account.AccountType == AccountTypes.Expense))
                .GroupBy(l => new { l.Entry.PeriodYear, l.Entry.PeriodMonth, l.Account.AccountType })
                .Select(g => new
                {
                    g.Key.PeriodYear,
                    g.Key.PeriodMonth,
                    g.Key.AccountType,
                    Debit = g.Sum(l => l.Debit),
                    Credit = g.Sum(l => l.Credit)
                })
                .ToListAsync();

            for (var offset = 0; offset < 12; offset++)
            {
                var month = trendStart.AddMonths(offset);
                var label = month.ToString("MMM yy");

                var revenue = monthly
                    .Where(m => m.PeriodYear == month.Year && m.PeriodMonth == month.Month &&
                                m.AccountType == AccountTypes.Revenue)
                    .Sum(m => m.Credit - m.Debit);

                var expenses = monthly
                    .Where(m => m.PeriodYear == month.Year && m.PeriodMonth == month.Month &&
                                m.AccountType == AccountTypes.Expense)
                    .Sum(m => m.Debit - m.Credit);

                view.RevenueByMonth.Add(new TrendPoint { Label = label, Date = month, Value = revenue });
                view.ExpensesByMonth.Add(new TrendPoint { Label = label, Date = month, Value = expenses });
                view.NetIncomeByMonth.Add(new TrendPoint
                {
                    Label = label, Date = month, Value = revenue - expenses
                });
            }

            view.RevenueBreakdown = income.RevenueLines
                .Where(l => !l.IsSubtotal && l.Amount != 0m)
                .Select(l => new CategorySlice { Label = l.Label, Value = l.Amount })
                .ToList();

            view.ExpenseBreakdown = income.ExpenseLines
                .Where(l => !l.IsSubtotal && l.Amount != 0m)
                .Select(l => new CategorySlice { Label = l.Label, Value = l.Amount })
                .OrderByDescending(s => s.Value)
                .ToList();

            return view;
        }

        // ================================================================== income statement

        public async Task<IncomeStatementView> GetIncomeStatementAsync(
            DateTime? fromUtc, DateTime? toUtc)
        {
            await _accounts.EnsureChartOfAccountsAsync();

            var range = ReportRange.Resolve(fromUtc, toUtc);

            var rows = await PeriodBalancesAsync(range.FromUtc, range.ToUtc);

            var view = new IncomeStatementView { FromUtc = range.FromUtc, ToUtc = range.ToUtc };

            // Revenue. Contra-revenue accounts are debited, so their balance comes out negative
            // and is reported separately as a deduction rather than buried in the total.
            var revenueRows = rows
                .Where(r => r.AccountType == AccountTypes.Revenue)
                .ToList();

            foreach (var row in revenueRows.Where(r => r.SubType != ChartOfAccounts.ContraRevenue))
            {
                if (row.Balance == 0m) continue;

                view.Revenue += row.Balance;
                view.RevenueLines.Add(Line(row.AccountName, row.AccountCode, row.AccountId, row.Balance));
            }

            foreach (var row in revenueRows.Where(r => r.SubType == ChartOfAccounts.ContraRevenue))
            {
                // Reported as a positive deduction, which is how it reads on a statement.
                var deduction = -row.Balance;
                if (deduction == 0m) continue;

                if (string.Equals(row.SystemKey, AccountKeys.SalesReturns, StringComparison.OrdinalIgnoreCase))
                {
                    view.SalesReturns += deduction;
                }
                else
                {
                    view.SalesDiscounts += deduction;
                }

                view.RevenueLines.Add(
                    Line("Less: " + row.AccountName, row.AccountCode, row.AccountId, -deduction));
            }

            view.NetRevenue = view.Revenue - view.SalesDiscounts - view.SalesReturns;
            view.RevenueLines.Add(Subtotal("Net revenue", view.NetRevenue));

            // Cost of sales, kept above the line so gross profit is visible on its own.
            foreach (var row in rows.Where(r => r.SubType == ChartOfAccounts.CostOfSales))
            {
                if (row.Balance == 0m) continue;

                view.CostOfGoodsSold += row.Balance;
                view.CostOfSalesLines.Add(Line(row.AccountName, row.AccountCode, row.AccountId, row.Balance));
            }

            view.GrossProfit = view.NetRevenue - view.CostOfGoodsSold;
            view.CostOfSalesLines.Add(Subtotal("Gross profit", view.GrossProfit));

            // Everything else the gym spends, grouped so a twenty-four line chart of accounts
            // does not become twenty-four lines on a one-page statement.
            var expenseRows = rows
                .Where(r => r.AccountType == AccountTypes.Expense &&
                            r.SubType != ChartOfAccounts.CostOfSales)
                .ToList();

            foreach (var group in expenseRows
                         .Where(r => r.Balance != 0m)
                         .GroupBy(r => r.SubType.Length == 0 ? "Other" : r.SubType)
                         .OrderBy(g => g.Key))
            {
                view.ExpenseLines.Add(new StatementLine
                {
                    Label = group.Key, Amount = group.Sum(r => r.Balance), Depth = 0, IsSubtotal = true
                });

                foreach (var row in group.OrderByDescending(r => r.Balance))
                {
                    view.ExpenseLines.Add(new StatementLine
                    {
                        Label = row.AccountName,
                        AccountCode = row.AccountCode,
                        AccountId = row.AccountId,
                        Amount = row.Balance,
                        Depth = 1
                    });
                }

                if (string.Equals(group.Key, ChartOfAccounts.PayrollExpense, StringComparison.Ordinal))
                {
                    view.PayrollExpenses += group.Sum(r => r.Balance);
                }
                else
                {
                    view.OperatingExpenses += group.Sum(r => r.Balance);
                }
            }

            view.TotalExpenses = view.PayrollExpenses + view.OperatingExpenses;
            view.ExpenseLines.Add(Subtotal("Total expenses", view.TotalExpenses));

            view.NetIncome = view.GrossProfit - view.TotalExpenses;

            view.GrossMarginPercent = view.NetRevenue == 0m
                ? 0m
                : Math.Round(view.GrossProfit / view.NetRevenue * 100m, 2);

            view.NetMarginPercent = view.NetRevenue == 0m
                ? 0m
                : Math.Round(view.NetIncome / view.NetRevenue * 100m, 2);

            return view;
        }

        // ================================================================== balance sheet

        public async Task<BalanceSheetView> GetBalanceSheetAsync(DateTime? asOfUtc)
        {
            await _accounts.EnsureChartOfAccountsAsync();

            var asOf = (asOfUtc?.Date ?? DateTime.UtcNow.Date).AddDays(1);

            var rows = await PeriodBalancesAsync(DateTime.MinValue, asOf);

            var view = new BalanceSheetView { AsOfUtc = asOf.AddDays(-1) };

            foreach (var row in rows.Where(r => r.AccountType == AccountTypes.Asset && r.Balance != 0m)
                                    .OrderBy(r => r.AccountCode))
            {
                if (string.Equals(row.SubType, ChartOfAccounts.FixedAsset, StringComparison.Ordinal))
                {
                    view.FixedAssets += row.Balance;
                }
                else
                {
                    view.CurrentAssets += row.Balance;
                }

                view.AssetLines.Add(Line(row.AccountName, row.AccountCode, row.AccountId, row.Balance));
            }

            view.TotalAssets = view.CurrentAssets + view.FixedAssets;
            view.AssetLines.Add(Subtotal("Total assets", view.TotalAssets));

            foreach (var row in rows.Where(r => r.AccountType == AccountTypes.Liability && r.Balance != 0m)
                                    .OrderBy(r => r.AccountCode))
            {
                view.CurrentLiabilities += row.Balance;
                view.LiabilityLines.Add(Line(row.AccountName, row.AccountCode, row.AccountId, row.Balance));
            }

            view.TotalLiabilities = view.CurrentLiabilities;
            view.LiabilityLines.Add(Subtotal("Total liabilities", view.TotalLiabilities));

            foreach (var row in rows.Where(r => r.AccountType == AccountTypes.Equity && r.Balance != 0m)
                                    .OrderBy(r => r.AccountCode))
            {
                view.ContributedEquity += row.Balance;
                view.EquityLines.Add(Line(row.AccountName, row.AccountCode, row.AccountId, row.Balance));
            }

            // Nothing closes the books in FitCore, so accumulated profit is not sitting in an
            // equity account waiting to be read. It is every revenue account less every expense
            // account, from the beginning of the records to this date - which is what makes the
            // sheet balance without an operator ever having to run a year-end journal.
            var revenue = rows.Where(r => r.AccountType == AccountTypes.Revenue).Sum(r => r.Balance);
            var expenses = rows.Where(r => r.AccountType == AccountTypes.Expense).Sum(r => r.Balance);

            view.RetainedEarnings = revenue - expenses;
            view.EquityLines.Add(Line("Retained earnings", "", null, view.RetainedEarnings));

            view.TotalEquity = view.ContributedEquity + view.RetainedEarnings;
            view.EquityLines.Add(Subtotal("Total equity", view.TotalEquity));

            view.LiabilitiesAndEquity = view.TotalLiabilities + view.TotalEquity;

            return view;
        }

        // ================================================================== cash flow

        public async Task<CashFlowView> GetCashFlowAsync(DateTime? fromUtc, DateTime? toUtc)
        {
            await _accounts.EnsureChartOfAccountsAsync();

            var range = ReportRange.Resolve(fromUtc, toUtc);

            var cashAccountIds = await _context.Accounts
                .AsNoTracking()
                .Where(a => a.SystemKey == AccountKeys.Cash || a.SystemKey == AccountKeys.Bank)
                .Select(a => a.AccountId)
                .ToListAsync();

            var view = new CashFlowView { FromUtc = range.FromUtc, ToUtc = range.ToUtc };

            if (cashAccountIds.Count == 0) return view;

            var opening = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => cashAccountIds.Contains(l.AccountId) &&
                            l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate < range.FromUtc)
                .GroupBy(_ => 1)
                .Select(g => new { Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
                .FirstOrDefaultAsync();

            view.OpeningCash = (opening?.Debit ?? 0m) - (opening?.Credit ?? 0m);

            // The direct method: every movement through cash, grouped by what caused it. A gym
            // owner wants "what came in from members, what went out to suppliers and staff",
            // not a reconciliation of net income to working capital.
            var movements = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => cashAccountIds.Contains(l.AccountId) &&
                            l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate >= range.FromUtc &&
                            l.Entry.EntryDate < range.ToUtc)
                .Select(l => new
                {
                    l.Entry.EntryDate,
                    l.Entry.Source,
                    Amount = l.Debit - l.Credit
                })
                .ToListAsync();

            foreach (var movement in movements)
            {
                switch (movement.Source)
                {
                    case JournalSources.Payment:
                    case JournalSources.Sale:
                        view.CashFromCustomers += movement.Amount;
                        break;

                    case JournalSources.SaleReturn:
                        view.CashFromCustomers += movement.Amount;
                        break;

                    case JournalSources.SupplierPayment:
                    case JournalSources.Purchase:
                        view.CashToSuppliers += movement.Amount;
                        break;

                    case JournalSources.Payroll:
                        view.CashToEmployees += movement.Amount;
                        break;

                    case JournalSources.Expense:
                        view.CashForExpenses += movement.Amount;
                        break;

                    default:
                        view.OtherMovements += movement.Amount;
                        break;
                }
            }

            view.NetCashFlow = movements.Sum(m => m.Amount);
            view.ClosingCash = view.OpeningCash + view.NetCashFlow;

            view.Lines.Add(Subtotal("Opening cash", view.OpeningCash));
            view.Lines.Add(Line("Received from members and customers", "", null, view.CashFromCustomers));
            view.Lines.Add(Line("Paid to suppliers", "", null, view.CashToSuppliers));
            view.Lines.Add(Line("Paid to employees", "", null, view.CashToEmployees));
            view.Lines.Add(Line("Operating expenses paid", "", null, view.CashForExpenses));

            if (view.OtherMovements != 0m)
            {
                view.Lines.Add(Line("Other movements", "", null, view.OtherMovements));
            }

            view.Lines.Add(Subtotal("Net cash flow", view.NetCashFlow));
            view.Lines.Add(Subtotal("Closing cash", view.ClosingCash));

            var running = view.OpeningCash;

            foreach (var day in movements
                         .GroupBy(m => m.EntryDate.Date)
                         .OrderBy(g => g.Key))
            {
                running += day.Sum(m => m.Amount);

                view.ByDay.Add(new TrendPoint
                {
                    Label = day.Key.ToString("d MMM"),
                    Date = day.Key,
                    Value = running,
                    Count = day.Count()
                });
            }

            return view;
        }

        // ================================================================== receivables

        public async Task<AgedBalanceView> GetReceivablesAsync(DateTime? asOfUtc)
        {
            var asOf = asOfUtc?.Date ?? DateTime.UtcNow.Date;
            var cutoff = asOf.AddDays(1);

            var view = new AgedBalanceView { AsOfUtc = asOf, Title = "Accounts Receivable" };

            // Sales raise a receivable for the whole amount; payments linked to them clear it.
            var sales = await _context.Sales
                .AsNoTracking()
                .Where(s => s.Status == "Completed" && s.SaleDate < cutoff)
                .Select(s => new
                {
                    s.SaleId,
                    s.MemberId,
                    MemberName = s.Member != null
                        ? s.Member.FirstName + " " + s.Member.LastName
                        : (s.WalkInName ?? "Walk-In"),
                    s.SaleDate,
                    s.TotalAmount,
                    Paid = _context.Payments
                        .Where(p => p.SaleId == s.SaleId && p.Status == "Completed" &&
                                    p.PaymentDate < cutoff)
                        .Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            foreach (var sale in sales)
            {
                var balance = sale.TotalAmount - sale.Paid;
                if (balance <= 0.004m) continue;

                Add(view, sale.MemberName, sale.MemberId, $"Sale #{sale.SaleId}", sale.SaleId,
                    sale.SaleDate, sale.TotalAmount, sale.Paid, balance, asOf);
            }

            // Memberships do the same: sold on the day the subscription starts, cleared by the
            // payments attached to it.
            var subscriptions = await _context.Subscriptions
                .AsNoTracking()
                .Where(s => s.Status != "Cancelled" && s.StartDate < cutoff)
                .Select(s => new
                {
                    s.SubscriptionId,
                    s.MemberId,
                    MemberName = s.Member != null
                        ? s.Member.FirstName + " " + s.Member.LastName
                        : (s.WalkInName ?? "Walk-In"),
                    s.StartDate,
                    Price = s.Plan.Price,
                    Paid = _context.Payments
                        .Where(p => p.SubscriptionId == s.SubscriptionId && p.Status == "Completed" &&
                                    p.PaymentDate < cutoff)
                        .Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            foreach (var subscription in subscriptions)
            {
                var balance = subscription.Price - subscription.Paid;
                if (balance <= 0.004m) continue;

                Add(view, subscription.MemberName, subscription.MemberId,
                    $"Membership #{subscription.SubscriptionId}", subscription.SubscriptionId,
                    subscription.StartDate, subscription.Price, subscription.Paid, balance, asOf);
            }

            Finalise(view);
            return view;
        }

        // ================================================================== payables

        public async Task<AgedBalanceView> GetPayablesAsync(DateTime? asOfUtc)
        {
            var asOf = asOfUtc?.Date ?? DateTime.UtcNow.Date;
            var cutoff = asOf.AddDays(1);

            var view = new AgedBalanceView { AsOfUtc = asOf, Title = "Accounts Payable" };

            var purchases = await _context.Purchases
                .AsNoTracking()
                .Where(p => p.OrderDate < cutoff &&
                            (p.Status == PurchaseStatuses.Received ||
                             p.Status == PurchaseStatuses.PartiallyReceived))
                .Select(p => new
                {
                    p.PurchaseId,
                    p.PurchaseNo,
                    p.SupplierId,
                    SupplierName = p.Supplier.SupplierName,
                    Date = p.ReceivedDate ?? p.OrderDate,
                    p.Total,
                    p.AmountPaid
                })
                .ToListAsync();

            foreach (var purchase in purchases)
            {
                var balance = purchase.Total - purchase.AmountPaid;
                if (balance <= 0.004m) continue;

                Add(view, purchase.SupplierName, purchase.SupplierId, purchase.PurchaseNo,
                    purchase.PurchaseId, purchase.Date, purchase.Total, purchase.AmountPaid,
                    balance, asOf);
            }

            // An expense recorded but not yet settled is owed to somebody too, and leaving it
            // out would understate what the gym has to find money for.
            var expenses = await _context.Expenses
                .AsNoTracking()
                .Where(e => e.Status == ExpenseStatuses.Unpaid && e.ExpenseDate < cutoff)
                .Select(e => new
                {
                    e.ExpenseId,
                    e.Amount,
                    e.ExpenseDate,
                    e.Category,
                    e.PaidTo,
                    e.SupplierId,
                    SupplierName = e.Supplier == null ? "" : e.Supplier.SupplierName
                })
                .ToListAsync();

            foreach (var expense in expenses)
            {
                var party = !string.IsNullOrWhiteSpace(expense.SupplierName)
                    ? expense.SupplierName
                    : !string.IsNullOrWhiteSpace(expense.PaidTo)
                        ? expense.PaidTo
                        : expense.Category;

                Add(view, party, expense.SupplierId ?? 0, $"Expense #{expense.ExpenseId}",
                    expense.ExpenseId, expense.ExpenseDate, expense.Amount, 0m, expense.Amount, asOf);
            }

            Finalise(view);
            return view;
        }

        // ================================================================== budgets

        public async Task<BudgetVarianceView?> GetBudgetVarianceAsync(int budgetId, int? month)
        {
            var budget = await _context.Budgets
                .AsNoTracking()
                .Include(b => b.Lines).ThenInclude(l => l.Account)
                .FirstOrDefaultAsync(b => b.BudgetId == budgetId);

            if (budget is null) return null;

            var lines = budget.Lines
                .Where(l => !month.HasValue || l.Month == month.Value)
                .ToList();

            var from = month.HasValue
                ? new DateTime(budget.Year, month.Value, 1, 0, 0, 0, DateTimeKind.Utc)
                : new DateTime(budget.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var to = month.HasValue ? from.AddMonths(1) : from.AddYears(1);

            var actuals = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate >= from && l.Entry.EntryDate < to)
                .GroupBy(l => l.AccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    Debit = g.Sum(l => l.Debit),
                    Credit = g.Sum(l => l.Credit)
                })
                .ToListAsync();

            var actualByAccount = actuals.ToDictionary(a => a.AccountId);

            var view = new BudgetVarianceView
            {
                BudgetId = budget.BudgetId,
                BudgetName = budget.Name,
                Year = budget.Year,
                Month = month
            };

            foreach (var group in lines.GroupBy(l => l.AccountId))
            {
                var account = group.First().Account;
                var budgeted = group.Sum(l => l.Amount);

                var actual = 0m;

                if (actualByAccount.TryGetValue(group.Key, out var posted))
                {
                    actual = AccountTypes.BalanceOf(account.AccountType, posted.Debit, posted.Credit);
                }

                var variance = actual - budgeted;

                // Spending more than planned is bad news; earning more than planned is good.
                // A report that shows both as "+1,200" is not worth reading, so the sign is
                // interpreted against what kind of account it is.
                var favourable = AccountTypes.Normalise(account.AccountType) == AccountTypes.Revenue
                    ? variance >= 0m
                    : variance <= 0m;

                view.Rows.Add(new BudgetVarianceRow
                {
                    AccountId = account.AccountId,
                    AccountCode = account.AccountCode,
                    AccountName = account.AccountName,
                    AccountType = account.AccountType,
                    Budgeted = budgeted,
                    Actual = actual,
                    Variance = variance,
                    VariancePercent = budgeted == 0m
                        ? 0m
                        : Math.Round(variance / Math.Abs(budgeted) * 100m, 2),
                    IsFavourable = favourable
                });
            }

            view.Rows = view.Rows.OrderBy(r => r.AccountCode).ToList();
            view.TotalBudgeted = view.Rows.Sum(r => r.Budgeted);
            view.TotalActual = view.Rows.Sum(r => r.Actual);
            view.TotalVariance = view.TotalActual - view.TotalBudgeted;

            return view;
        }

        // ================================================================== helpers

        private sealed record AccountBalance(
            int AccountId, string AccountCode, string AccountName, string AccountType,
            string SubType, string SystemKey, decimal Balance);

        /// <summary>
        /// Every account's balance over a window, in one query.
        ///
        /// The window is half-open: entries on or after <paramref name="fromUtc"/> and strictly
        /// before <paramref name="toUtc"/>. For a balance sheet the caller passes
        /// <see cref="DateTime.MinValue"/> as the start, which makes it "everything up to".
        /// </summary>
        private async Task<List<AccountBalance>> PeriodBalancesAsync(DateTime fromUtc, DateTime toUtc)
        {
            var totals = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate >= fromUtc &&
                            l.Entry.EntryDate < toUtc)
                .GroupBy(l => l.AccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    Debit = g.Sum(l => l.Debit),
                    Credit = g.Sum(l => l.Credit)
                })
                .ToListAsync();

            var accounts = await _context.Accounts
                .AsNoTracking()
                .Select(a => new
                {
                    a.AccountId, a.AccountCode, a.AccountName,
                    a.AccountType, a.AccountSubType, a.SystemKey
                })
                .ToListAsync();

            var byAccount = totals.ToDictionary(t => t.AccountId);

            return accounts
                .Select(a =>
                {
                    byAccount.TryGetValue(a.AccountId, out var total);

                    return new AccountBalance(
                        a.AccountId, a.AccountCode, a.AccountName, a.AccountType,
                        a.AccountSubType, a.SystemKey,
                        AccountTypes.BalanceOf(a.AccountType, total?.Debit ?? 0m, total?.Credit ?? 0m));
                })
                .OrderBy(a => a.AccountCode)
                .ToList();
        }

        private async Task<List<AccountBalance>> BalancesAsync(DateTime toUtc) =>
            await PeriodBalancesAsync(DateTime.MinValue, toUtc);

        private static decimal Sum(List<AccountBalance> balances, string systemKey) =>
            balances
                .Where(b => string.Equals(b.SystemKey, systemKey, StringComparison.OrdinalIgnoreCase))
                .Sum(b => b.Balance);

        private static StatementLine Line(string label, string code, int? accountId, decimal amount) =>
            new() { Label = label, AccountCode = code, AccountId = accountId, Amount = amount, Depth = 1 };

        private static StatementLine Subtotal(string label, decimal amount) =>
            new() { Label = label, Amount = amount, Depth = 0, IsSubtotal = true };

        private static void Add(
            AgedBalanceView view, string party, int? partyId, string document, int documentId,
            DateTime documentDate, decimal total, decimal settled, decimal balance, DateTime asOf)
        {
            var days = Math.Max(0, (int)(asOf - documentDate.Date).TotalDays);

            view.Rows.Add(new AgedRow
            {
                Party = string.IsNullOrWhiteSpace(party) ? "- unnamed -" : party.Trim(),
                PartyId = partyId,
                Document = document,
                DocumentId = documentId,
                DocumentDate = documentDate,
                DaysOutstanding = days,
                Total = total,
                Settled = settled,
                Balance = balance,
                Bucket = AgedBalanceView.BucketFor(days)
            });
        }

        private static void Finalise(AgedBalanceView view)
        {
            view.Rows = view.Rows
                .OrderByDescending(r => r.DaysOutstanding)
                .ThenBy(r => r.Party)
                .ToList();

            view.Current = view.Rows.Where(r => r.DaysOutstanding <= 0).Sum(r => r.Balance);
            view.Days1To30 = view.Rows.Where(r => r.DaysOutstanding is > 0 and <= 30).Sum(r => r.Balance);
            view.Days31To60 = view.Rows.Where(r => r.DaysOutstanding is > 30 and <= 60).Sum(r => r.Balance);
            view.Days61To90 = view.Rows.Where(r => r.DaysOutstanding is > 60 and <= 90).Sum(r => r.Balance);
            view.Over90Days = view.Rows.Where(r => r.DaysOutstanding > 90).Sum(r => r.Balance);
            view.Total = view.Rows.Sum(r => r.Balance);
        }
    }
}
