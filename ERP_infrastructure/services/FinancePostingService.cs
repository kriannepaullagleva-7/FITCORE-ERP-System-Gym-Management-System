using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Turns operational events into double-entry postings.
    ///
    /// This is the one place in FitCore that knows both halves of the language: what a sale is,
    /// and what a sale means to the books. Keeping it in a single service is what stops a
    /// debit-and-credit pair being written by hand in five different modules, each with its own
    /// idea of which account cash lives in.
    ///
    /// Every method here is safe to call twice. The entry records the source record that caused
    /// it, and a second attempt finds the existing one and does nothing - so retrying a failed
    /// operation, or running the catch-up sweep over a period that was already posted, cannot
    /// double the ledger.
    /// </summary>
    public class FinancePostingService : IFinancePostingService
    {
        private readonly TenantErpDbContext _context;
        private readonly IJournalService _journal;
        private readonly IAccountService _accounts;
        private readonly ITenantSettingsService _settings;
        private readonly ILogger<FinancePostingService> _logger;

        /// <summary>
        /// Resolved account ids, cached for the life of the request.
        ///
        /// A sale posting looks up four accounts and a pay run nine. Without this the catch-up
        /// sweep would issue a query per account per transaction, which against a remote
        /// database is the difference between seconds and minutes.
        /// </summary>
        private readonly Dictionary<string, int?> _accountCache = new(StringComparer.OrdinalIgnoreCase);

        private bool _chartEnsured;

        public FinancePostingService(
            TenantErpDbContext context,
            IJournalService journal,
            IAccountService accounts,
            ITenantSettingsService settings,
            ILogger<FinancePostingService> logger)
        {
            _context = context;
            _journal = journal;
            _accounts = accounts;
            _settings = settings;
            _logger = logger;
        }

        public Task<bool> IsEnabledAsync() =>
            _settings.GetBoolAsync(TenantSettingKeys.AutoPostToLedger, fallback: true);

        // ================================================================== sales

        public Task<PostingResult> PostSaleAsync(int saleId) =>
            GuardedAsync(nameof(Sale), saleId, async () =>
            {
                var sale = await _context.Sales
                    .AsNoTracking()
                    .Include(s => s.Items)
                    .FirstOrDefaultAsync(s => s.SaleId == saleId);

                if (sale is null) return PostingResult.Skipped("The sale no longer exists.");

                if (string.Equals(sale.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    return PostingResult.Skipped("A cancelled sale is not posted.");
                }

                var gross = sale.Items.Sum(i => i.Quantity * i.UnitPrice);
                var cost = sale.Items.Sum(i => i.Quantity * i.UnitCost);

                if (gross <= 0m) return PostingResult.Skipped("The sale has nothing on it.");

                var lines = new List<JournalLineRequest>
                {
                    // The whole sale becomes a receivable. Its payments settle it separately,
                    // which is what lets a partially paid sale show a real outstanding balance
                    // rather than being invisible until it is settled in full.
                    Debit(await AccountAsync(AccountKeys.AccountsReceivable),
                        sale.TotalAmount, $"Sale #{sale.SaleId}", memberId: sale.MemberId),

                    Credit(await AccountAsync(AccountKeys.ProductRevenue),
                        gross, $"Goods sold on sale #{sale.SaleId}")
                };

                if (sale.Discount > 0m)
                {
                    // Contra-revenue: a debit to a revenue account, so total revenue reads net
                    // of discount without the discount disappearing from the record.
                    lines.Add(Debit(await AccountAsync(AccountKeys.SalesDiscounts),
                        sale.Discount, $"Discount on sale #{sale.SaleId}"));
                }

                if (cost > 0m)
                {
                    lines.Add(Debit(await AccountAsync(AccountKeys.CostOfGoodsSold),
                        cost, $"Cost of goods on sale #{sale.SaleId}"));

                    lines.Add(Credit(await AccountAsync(AccountKeys.Inventory),
                        cost, $"Stock issued for sale #{sale.SaleId}"));
                }

                var entry = await _journal.CreateEntryAsync(
                    sale.SaleDate,
                    $"Sale #{sale.SaleId}",
                    lines,
                    JournalSources.Sale,
                    ErpModules.Sales,
                    nameof(Sale),
                    sale.SaleId.ToString(),
                    reference: $"SALE-{sale.SaleId}");

                return PostingResult.Ok(entry.JournalEntryId);
            });

        public Task<PostingResult> ReverseSaleAsync(int saleId, string reason) =>
            ReverseSourceAsync(nameof(Sale), saleId, reason);

        public Task<PostingResult> PostSaleReturnAsync(int saleReturnId) =>
            GuardedAsync(nameof(SaleReturn), saleReturnId, async () =>
            {
                var saleReturn = await _context.SaleReturns
                    .AsNoTracking()
                    .Include(r => r.Items)
                    .FirstOrDefaultAsync(r => r.SaleReturnId == saleReturnId);

                if (saleReturn is null) return PostingResult.Skipped("The return no longer exists.");

                if (!string.Equals(saleReturn.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    return PostingResult.Skipped("A cancelled return is not posted.");
                }

                var cost = saleReturn.Items.Sum(i => i.Quantity * i.UnitCost);

                var lines = new List<JournalLineRequest>
                {
                    Debit(await AccountAsync(AccountKeys.SalesReturns),
                        saleReturn.RefundAmount, $"Return {saleReturn.ReturnNo}",
                        memberId: saleReturn.MemberId),

                    Credit(await AccountAsync(AccountKeys.Cash),
                        saleReturn.RefundAmount, $"Refund on {saleReturn.ReturnNo}")
                };

                if (cost > 0m)
                {
                    // Goods fit to sell again go back into inventory. Goods that came back
                    // damaged are a loss, and saying so is the difference between a stock
                    // figure that matches the shelf and one that does not.
                    var landingAccount = saleReturn.RestockedToInventory
                        ? await AccountAsync(AccountKeys.Inventory)
                        : await AccountAsync(AccountKeys.InventoryShrinkage);

                    lines.Add(Debit(landingAccount, cost,
                        saleReturn.RestockedToInventory
                            ? $"Stock restored by {saleReturn.ReturnNo}"
                            : $"Damaged stock written off on {saleReturn.ReturnNo}"));

                    lines.Add(Credit(await AccountAsync(AccountKeys.CostOfGoodsSold),
                        cost, $"Cost reversed by {saleReturn.ReturnNo}"));
                }

                var entry = await _journal.CreateEntryAsync(
                    saleReturn.ReturnDate,
                    $"Return {saleReturn.ReturnNo} against sale #{saleReturn.SaleId}",
                    lines,
                    JournalSources.SaleReturn,
                    ErpModules.Sales,
                    nameof(SaleReturn),
                    saleReturn.SaleReturnId.ToString(),
                    reference: saleReturn.ReturnNo);

                return PostingResult.Ok(entry.JournalEntryId);
            });

        public Task<PostingResult> ReverseSaleReturnAsync(int saleReturnId, string reason) =>
            ReverseSourceAsync(nameof(SaleReturn), saleReturnId, reason);

        // ================================================================== memberships

        public Task<PostingResult> PostSubscriptionAsync(int subscriptionId) =>
            GuardedAsync(nameof(Subscription), subscriptionId, async () =>
            {
                var subscription = await _context.Subscriptions
                    .AsNoTracking()
                    .Include(s => s.Plan)
                    .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId);

                if (subscription is null) return PostingResult.Skipped("The subscription no longer exists.");

                if (string.Equals(subscription.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    return PostingResult.Skipped("A cancelled subscription is not posted.");
                }

                var price = subscription.Plan?.Price ?? 0m;
                if (price <= 0m) return PostingResult.Skipped("The plan is free, so there is nothing to post.");

                var lines = new List<JournalLineRequest>
                {
                    Debit(await AccountAsync(AccountKeys.AccountsReceivable), price,
                        $"Membership #{subscription.SubscriptionId}", memberId: subscription.MemberId),

                    Credit(await AccountAsync(AccountKeys.MembershipRevenue), price,
                        $"{subscription.Plan?.PlanName} membership", memberId: subscription.MemberId)
                };

                // Dated when the membership was sold, not when its term begins - the same rule
                // as a renewal, for the same reason. A membership sold today that starts on
                // Monday is revenue earned today; dating the entry forward leaves the member's
                // payment on the books with no receivable against it, so receivables go
                // negative until the term starts. Where the two differ, the earlier is the
                // transaction, so a backdated membership keeps its own start date.
                var entry = await _journal.CreateEntryAsync(
                    subscription.StartDate < DateTime.UtcNow ? subscription.StartDate : DateTime.UtcNow,
                    $"Membership #{subscription.SubscriptionId} - {subscription.Plan?.PlanName}",
                    lines,
                    JournalSources.Payment,
                    ErpModules.Membership,
                    nameof(Subscription),
                    subscription.SubscriptionId.ToString(),
                    reference: $"SUB-{subscription.SubscriptionId}");

                return PostingResult.Ok(entry.JournalEntryId);
            });

        public async Task<PostingResult> PostSubscriptionRenewalAsync(
            int subscriptionId, DateTime termStart)
        {
            // Keyed by the term rather than by the subscription, so each renewal is its own
            // revenue event and posting it twice still does nothing.
            var sourceId = $"{subscriptionId}:{termStart:yyyyMMdd}";

            try
            {
                if (!await IsEnabledAsync())
                {
                    return PostingResult.Skipped("Automatic posting is switched off for this company.");
                }

                var existing = await _journal.GetEntryForSourceAsync("SubscriptionRenewal", sourceId);
                if (existing is not null) return PostingResult.AlreadyPosted(existing.JournalEntryId);

                await EnsureChartAsync();

                var subscription = await _context.Subscriptions
                    .AsNoTracking()
                    .Include(s => s.Plan)
                    .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId);

                if (subscription is null) return PostingResult.Skipped("The subscription no longer exists.");

                var price = subscription.Plan?.Price ?? 0m;
                if (price <= 0m) return PostingResult.Skipped("The plan is free, so there is nothing to post.");

                var lines = new List<JournalLineRequest>
                {
                    Debit(await AccountAsync(AccountKeys.AccountsReceivable), price,
                        $"Membership #{subscriptionId} renewal", memberId: subscription.MemberId),

                    Credit(await AccountAsync(AccountKeys.MembershipRevenue), price,
                        $"{subscription.Plan?.PlanName} renewal", memberId: subscription.MemberId)
                };

                // Dated when the renewal was transacted, not when the new term begins.
                //
                // Those are the same day for a lapsed membership and a year apart for one
                // renewed early, and using the term start put the entry a year into the future:
                // the revenue and the receivable were invisible to every report while the
                // member's payment was recorded today. Receivables went negative - the books
                // said the gym owed its members money - and the trial balance still balanced,
                // because both halves of the entry were equally misdated, so nothing flagged it.
                //
                // The term start is still what identifies the renewal, which is why it remains
                // the idempotency key above. FitCore does not defer revenue over a term - a new
                // subscription recognises the whole plan price too - so recognising a renewal
                // when it is sold is also what keeps the two consistent.
                var entry = await _journal.CreateEntryAsync(
                    DateTime.UtcNow,
                    $"Membership #{subscriptionId} renewed - {subscription.Plan?.PlanName}",
                    lines,
                    JournalSources.Payment,
                    ErpModules.Membership,
                    "SubscriptionRenewal",
                    sourceId,
                    reference: $"SUB-{subscriptionId}-R");

                return PostingResult.Ok(entry.JournalEntryId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Could not post the renewal of subscription {Id} to the ledger.", subscriptionId);

                return PostingResult.Failed(
                    $"The renewal was saved, but it could not be posted to the ledger: {ex.Message}");
            }
        }

        public Task<PostingResult> ReverseSubscriptionAsync(int subscriptionId, string reason) =>
            ReverseSourceAsync(nameof(Subscription), subscriptionId, reason);

        // ================================================================== payments

        public Task<PostingResult> PostPaymentAsync(int paymentId) =>
            GuardedAsync(nameof(Payment), paymentId, async () =>
            {
                var payment = await _context.Payments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

                if (payment is null) return PostingResult.Skipped("The payment no longer exists.");

                if (!string.Equals(payment.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    return PostingResult.Skipped(
                        $"A {payment.Status.ToLowerInvariant()} payment is not posted until it completes.");
                }

                if (payment.Amount <= 0m) return PostingResult.Skipped("The payment is for nothing.");

                var cashAccount = await CashAccountForMethodAsync(payment.Method);

                // What the money settles decides the credit. A sale and a subscription each
                // raised a receivable when they happened, so their payments clear it. Money
                // that settles neither is income earned at the moment it is taken.
                var settlesADebt = payment.SaleId.HasValue || payment.SubscriptionId.HasValue;

                var creditAccount = settlesADebt
                    ? await AccountAsync(AccountKeys.AccountsReceivable)
                    : await AccountAsync(AccountKeys.OtherRevenue);

                var what = payment.SaleId.HasValue
                    ? $"sale #{payment.SaleId}"
                    : payment.SubscriptionId.HasValue
                        ? $"subscription #{payment.SubscriptionId}"
                        : "account";

                var lines = new List<JournalLineRequest>
                {
                    Debit(cashAccount, payment.Amount,
                        $"Payment #{payment.PaymentId} by {payment.Method}", memberId: payment.MemberId),

                    Credit(creditAccount, payment.Amount,
                        $"Payment #{payment.PaymentId} against {what}", memberId: payment.MemberId)
                };

                var entry = await _journal.CreateEntryAsync(
                    payment.PaymentDate,
                    $"Payment #{payment.PaymentId} - {payment.Category}",
                    lines,
                    JournalSources.Payment,
                    ErpModules.Payments,
                    nameof(Payment),
                    payment.PaymentId.ToString(),
                    reference: string.IsNullOrWhiteSpace(payment.ReferenceNo)
                        ? $"PAY-{payment.PaymentId}"
                        : payment.ReferenceNo);

                return PostingResult.Ok(entry.JournalEntryId);
            });

        public Task<PostingResult> ReversePaymentAsync(int paymentId, string reason) =>
            ReverseSourceAsync(nameof(Payment), paymentId, reason);

        // ================================================================== purchasing

        public Task<PostingResult> PostPurchaseReceiptAsync(int purchaseId) =>
            GuardedAsync(nameof(Purchase), purchaseId, async () =>
            {
                var purchase = await _context.Purchases
                    .AsNoTracking()
                    .Include(p => p.Items)
                    .FirstOrDefaultAsync(p => p.PurchaseId == purchaseId);

                if (purchase is null) return PostingResult.Skipped("The purchase no longer exists.");

                if (!PurchaseStatuses.HasReceipts(purchase.Status))
                {
                    return PostingResult.Skipped(
                        "Nothing has been received yet, so nothing is owed and nothing is posted.");
                }

                var received = purchase.Items.Sum(i => i.QuantityReceived * i.UnitCost);
                if (received <= 0m) return PostingResult.Skipped("Nothing has been received yet.");

                // Discount and tax are apportioned onto the goods rather than posted
                // separately, because they are part of what the stock cost. The inventory
                // account and the payable therefore agree with what the supplier will invoice.
                var adjustment = purchase.Subtotal > 0m
                    ? (purchase.Total - purchase.Subtotal) * (received / purchase.Subtotal)
                    : 0m;

                var value = Math.Round(received + adjustment, 2, MidpointRounding.AwayFromZero);

                var lines = new List<JournalLineRequest>
                {
                    Debit(await AccountAsync(AccountKeys.Inventory), value,
                        $"Stock received on {purchase.PurchaseNo}", supplierId: purchase.SupplierId),

                    Credit(await AccountAsync(AccountKeys.AccountsPayable), value,
                        $"Owed to supplier for {purchase.PurchaseNo}", supplierId: purchase.SupplierId)
                };

                var entry = await _journal.CreateEntryAsync(
                    purchase.ReceivedDate ?? purchase.OrderDate,
                    $"Purchase {purchase.PurchaseNo} received",
                    lines,
                    JournalSources.Purchase,
                    ErpModules.Inventory,
                    nameof(Purchase),
                    purchase.PurchaseId.ToString(),
                    reference: purchase.PurchaseNo);

                return PostingResult.Ok(entry.JournalEntryId);
            });

        public Task<PostingResult> PostSupplierPaymentAsync(int supplierPaymentId) =>
            GuardedAsync(nameof(SupplierPayment), supplierPaymentId, async () =>
            {
                var payment = await _context.SupplierPayments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.SupplierPaymentId == supplierPaymentId);

                if (payment is null) return PostingResult.Skipped("The payment no longer exists.");
                if (payment.Amount <= 0m) return PostingResult.Skipped("The payment is for nothing.");

                var lines = new List<JournalLineRequest>
                {
                    Debit(await AccountAsync(AccountKeys.AccountsPayable), payment.Amount,
                        "Supplier payment", supplierId: payment.SupplierId),

                    Credit(await CashAccountForMethodAsync(payment.Method), payment.Amount,
                        $"Paid by {payment.Method}", supplierId: payment.SupplierId)
                };

                var entry = await _journal.CreateEntryAsync(
                    payment.PaymentDate,
                    payment.PurchaseId.HasValue
                        ? $"Supplier payment against purchase #{payment.PurchaseId}"
                        : "Supplier payment on account",
                    lines,
                    JournalSources.SupplierPayment,
                    ErpModules.Inventory,
                    nameof(SupplierPayment),
                    payment.SupplierPaymentId.ToString(),
                    reference: string.IsNullOrWhiteSpace(payment.ReferenceNo)
                        ? $"SP-{payment.SupplierPaymentId}"
                        : payment.ReferenceNo);

                return PostingResult.Ok(entry.JournalEntryId);
            });

        // ================================================================== payroll

        public Task<PostingResult> PostPayrollAsync(int payrollId) =>
            GuardedAsync(nameof(Payroll), payrollId, async () =>
            {
                var run = await _context.Payrolls
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PayrollId == payrollId);

                if (run is null) return PostingResult.Skipped("The pay run no longer exists.");

                if (!string.Equals(run.Status, "Paid", StringComparison.OrdinalIgnoreCase))
                {
                    return PostingResult.Skipped(
                        "A pay run reaches the ledger when it is paid, not when it is drafted.");
                }

                var salaryCost = run.BasicSalary + run.RegularPay + run.Allowances;

                var lines = new List<JournalLineRequest>();

                if (salaryCost > 0m)
                {
                    lines.Add(Debit(await AccountAsync(AccountKeys.SalariesExpense), salaryCost,
                        $"Basic and regular pay, {Period(run)}", employeeId: run.EmployeeId));
                }

                if (run.OvertimePay > 0m)
                {
                    lines.Add(Debit(await AccountAsync(AccountKeys.OvertimeExpense), run.OvertimePay,
                        $"Overtime, {Period(run)}", employeeId: run.EmployeeId));
                }

                if (run.EmployerContributions > 0m)
                {
                    // The employer share is a cost the gym has incurred and a liability it has
                    // not yet remitted, at the same moment. It appears on both sides of the
                    // entry for exactly that reason.
                    lines.Add(Debit(await AccountAsync(AccountKeys.EmployerContributions),
                        run.EmployerContributions,
                        $"Employer contributions, {Period(run)}", employeeId: run.EmployeeId));
                }

                await AddStatutoryCreditAsync(lines, AccountKeys.SssPayable,
                    run.SssDeduction + run.SssEmployerShare, "SSS", run);
                await AddStatutoryCreditAsync(lines, AccountKeys.PhilHealthPayable,
                    run.PhilHealthDeduction + run.PhilHealthEmployerShare, "PhilHealth", run);
                await AddStatutoryCreditAsync(lines, AccountKeys.PagIbigPayable,
                    run.PagIbigDeduction + run.PagIbigEmployerShare, "Pag-IBIG", run);
                await AddStatutoryCreditAsync(lines, AccountKeys.WithholdingTaxPayable,
                    run.WithholdingTax, "Withholding tax", run);

                if (run.OtherDeductions > 0m)
                {
                    // Anything withheld outside the statutory set is money the gym keeps -
                    // a recovered advance, a loss charged back - so it reduces the cost rather
                    // than becoming a liability to anyone.
                    lines.Add(Credit(await AccountAsync(AccountKeys.SalariesExpense),
                        run.OtherDeductions,
                        $"Other deductions, {Period(run)}", employeeId: run.EmployeeId));
                }

                if (run.NetPay > 0m)
                {
                    lines.Add(Credit(await AccountAsync(AccountKeys.Cash), run.NetPay,
                        $"Net pay, {Period(run)}", employeeId: run.EmployeeId));
                }

                if (lines.Count < 2)
                {
                    return PostingResult.Skipped("The pay run has no amounts to post.");
                }

                var entry = await _journal.CreateEntryAsync(
                    run.PaidDate ?? run.PeriodEnd,
                    $"Payroll for {Period(run)}",
                    lines,
                    JournalSources.Payroll,
                    ErpModules.Payroll,
                    nameof(Payroll),
                    run.PayrollId.ToString(),
                    reference: $"PR-{run.PayrollId}");

                return PostingResult.Ok(entry.JournalEntryId);
            });

        public Task<PostingResult> ReversePayrollAsync(int payrollId, string reason) =>
            ReverseSourceAsync(nameof(Payroll), payrollId, reason);

        // ================================================================== expenses

        public Task<PostingResult> PostExpenseAsync(int expenseId) =>
            GuardedAsync(nameof(Expense), expenseId, async () =>
            {
                var expense = await _context.Expenses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.ExpenseId == expenseId);

                if (expense is null) return PostingResult.Skipped("The expense no longer exists.");

                if (string.Equals(expense.Status, ExpenseStatuses.Void, StringComparison.OrdinalIgnoreCase))
                {
                    return PostingResult.Skipped("A voided expense is not posted.");
                }

                if (expense.Amount <= 0m) return PostingResult.Skipped("The expense is for nothing.");

                // The account named on the expense wins; otherwise the category's own account;
                // otherwise General Expense. A tenant who has never opened the chart still
                // produces a complete profit and loss.
                var expenseAccount = expense.AccountId
                    ?? await TryAccountAsync(ChartOfAccounts.ExpenseAccountKey(expense.Category))
                    ?? await AccountAsync(AccountKeys.GeneralExpense);

                var fundingAccount =
                    string.Equals(expense.Status, ExpenseStatuses.Unpaid, StringComparison.OrdinalIgnoreCase)
                        ? await AccountAsync(AccountKeys.AccountsPayable)
                        : await CashAccountForMethodAsync(expense.PaymentMethod);

                var description = string.IsNullOrWhiteSpace(expense.Description)
                    ? expense.Category
                    : expense.Description;

                var lines = new List<JournalLineRequest>
                {
                    Debit(expenseAccount, expense.Amount, description, supplierId: expense.SupplierId),
                    Credit(fundingAccount, expense.Amount,
                        string.Equals(expense.Status, ExpenseStatuses.Unpaid, StringComparison.OrdinalIgnoreCase)
                            ? $"Owed for {description}"
                            : $"Paid by {expense.PaymentMethod}",
                        supplierId: expense.SupplierId)
                };

                var entry = await _journal.CreateEntryAsync(
                    expense.ExpenseDate,
                    $"{expense.Category}: {description}",
                    lines,
                    JournalSources.Expense,
                    ErpModules.Finance,
                    nameof(Expense),
                    expense.ExpenseId.ToString(),
                    reference: string.IsNullOrWhiteSpace(expense.ReferenceNo)
                        ? $"EXP-{expense.ExpenseId}"
                        : expense.ReferenceNo);

                return PostingResult.Ok(entry.JournalEntryId);
            });

        public Task<PostingResult> ReverseExpenseAsync(int expenseId, string reason) =>
            ReverseSourceAsync(nameof(Expense), expenseId, reason);

        // ================================================================== inventory

        public Task<PostingResult> PostStockAdjustmentAsync(int stockMovementId) =>
            GuardedAsync(nameof(StockMovement), stockMovementId, async () =>
            {
                var movement = await _context.StockMovements
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.StockMovementId == stockMovementId);

                if (movement is null) return PostingResult.Skipped("The movement no longer exists.");

                // Receipts and issues are posted by the purchase and the sale that caused
                // them. Only an adjustment - a count that disagreed with the books, breakage,
                // theft - has no other transaction behind it.
                var isAdjustment =
                    string.Equals(movement.MovementType, "Adjustment", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(movement.MovementType, "Out", StringComparison.OrdinalIgnoreCase);

                if (!isAdjustment)
                {
                    return PostingResult.Skipped(
                        "This movement is posted by the transaction that caused it.");
                }

                var delta = movement.BalanceAfter - movement.BalanceBefore;
                var value = Math.Round(Math.Abs(delta) * movement.UnitCost, 2, MidpointRounding.AwayFromZero);

                if (value <= 0m) return PostingResult.Skipped("The adjustment has no value.");

                var inventory = await AccountAsync(AccountKeys.Inventory);
                var shrinkage = await AccountAsync(AccountKeys.InventoryShrinkage);

                var lines = delta < 0m
                    // Less on the shelf than the books said: the difference is a loss.
                    ? new List<JournalLineRequest>
                    {
                        Debit(shrinkage, value, $"Stock written off - {movement.Reference}"),
                        Credit(inventory, value, $"Stock adjustment {movement.StockMovementId}")
                    }
                    // More than the books said: the loss previously recognised was overstated.
                    : new List<JournalLineRequest>
                    {
                        Debit(inventory, value, $"Stock found on count - {movement.Reference}"),
                        Credit(shrinkage, value, $"Stock adjustment {movement.StockMovementId}")
                    };

                var entry = await _journal.CreateEntryAsync(
                    movement.MovementDate,
                    $"Stock adjustment: {movement.Reference}",
                    lines,
                    JournalSources.StockAdjustment,
                    ErpModules.Inventory,
                    nameof(StockMovement),
                    movement.StockMovementId.ToString(),
                    reference: movement.Reference);

                return PostingResult.Ok(entry.JournalEntryId);
            });

        // ================================================================== catch-up

        public async Task<int> CountOutstandingAsync(DateTime? fromUtc = null)
        {
            var work = await FindOutstandingAsync(fromUtc);

            return work.Subscriptions.Count + work.Sales.Count + work.Payments.Count +
                   work.Purchases.Count + work.Payrolls.Count + work.Expenses.Count;
        }

        public async Task<int> PostOutstandingAsync(DateTime? fromUtc = null)
        {
            await EnsureChartAsync();

            var work = await FindOutstandingAsync(fromUtc);

            var posted = 0;

            // Ordered so that the transaction raising a balance is posted before the one
            // settling it. Out of order the ledger still ends up right, but a general ledger
            // read halfway through would show a payment against a receivable that does not
            // exist yet.
            foreach (var id in work.Purchases) posted += (await PostPurchaseReceiptAsync(id)).Posted ? 1 : 0;
            foreach (var id in work.Subscriptions) posted += (await PostSubscriptionAsync(id)).Posted ? 1 : 0;
            foreach (var id in work.Sales) posted += (await PostSaleAsync(id)).Posted ? 1 : 0;
            foreach (var id in work.Payments) posted += (await PostPaymentAsync(id)).Posted ? 1 : 0;
            foreach (var id in work.Payrolls) posted += (await PostPayrollAsync(id)).Posted ? 1 : 0;
            foreach (var id in work.Expenses) posted += (await PostExpenseAsync(id)).Posted ? 1 : 0;

            return posted;
        }

        /// <summary>Completed transactions with no live journal entry naming them.</summary>
        private sealed record OutstandingWork(
            List<int> Subscriptions, List<int> Sales, List<int> Payments,
            List<int> Purchases, List<int> Payrolls, List<int> Expenses);

        private async Task<OutstandingWork> FindOutstandingAsync(DateTime? fromUtc)
        {
            var from = fromUtc ?? DateTime.UtcNow.AddYears(-2);

            // One query per source type returning only the ids, rather than loading the rows.
            // The anti-join is done in the database: "every sale with no live journal entry
            // naming it".
            var posted = _context.JournalEntries
                .AsNoTracking()
                .Where(e => e.Status != JournalStatuses.Void && e.SourceEntityId != null);

            var subscriptions = await _context.Subscriptions
                .AsNoTracking()
                .Where(s => s.StartDate >= from && s.Status != "Cancelled")
                .Where(s => !posted.Any(e =>
                    e.SourceEntityName == "Subscription" &&
                    e.SourceEntityId == s.SubscriptionId.ToString()))
                .Select(s => s.SubscriptionId)
                .ToListAsync();

            var sales = await _context.Sales
                .AsNoTracking()
                .Where(s => s.SaleDate >= from && s.Status == "Completed")
                .Where(s => !posted.Any(e =>
                    e.SourceEntityName == "Sale" && e.SourceEntityId == s.SaleId.ToString()))
                .Select(s => s.SaleId)
                .ToListAsync();

            var payments = await _context.Payments
                .AsNoTracking()
                .Where(p => p.PaymentDate >= from && p.Status == "Completed")
                .Where(p => !posted.Any(e =>
                    e.SourceEntityName == "Payment" && e.SourceEntityId == p.PaymentId.ToString()))
                .Select(p => p.PaymentId)
                .ToListAsync();

            var purchases = await _context.Purchases
                .AsNoTracking()
                .Where(p => p.OrderDate >= from &&
                            (p.Status == PurchaseStatuses.Received ||
                             p.Status == PurchaseStatuses.PartiallyReceived))
                .Where(p => !posted.Any(e =>
                    e.SourceEntityName == "Purchase" && e.SourceEntityId == p.PurchaseId.ToString()))
                .Select(p => p.PurchaseId)
                .ToListAsync();

            var payrolls = await _context.Payrolls
                .AsNoTracking()
                .Where(p => p.PeriodEnd >= from && p.Status == "Paid")
                .Where(p => !posted.Any(e =>
                    e.SourceEntityName == "Payroll" && e.SourceEntityId == p.PayrollId.ToString()))
                .Select(p => p.PayrollId)
                .ToListAsync();

            var expenses = await _context.Expenses
                .AsNoTracking()
                .Where(x => x.ExpenseDate >= from && x.Status != ExpenseStatuses.Void)
                .Where(x => !posted.Any(e =>
                    e.SourceEntityName == "Expense" && e.SourceEntityId == x.ExpenseId.ToString()))
                .Select(x => x.ExpenseId)
                .ToListAsync();

            return new OutstandingWork(subscriptions, sales, payments, purchases, payrolls, expenses);
        }

        // ================================================================== plumbing

        /// <summary>
        /// The shared shape of every posting: check it is switched on, check it has not already
        /// been done, do it, and never let a failure escape into the operation that asked.
        /// </summary>
        private async Task<PostingResult> GuardedAsync(
            string entityName, int entityId, Func<Task<PostingResult>> post)
        {
            try
            {
                if (!await IsEnabledAsync())
                {
                    return PostingResult.Skipped(
                        "Automatic posting is switched off for this company. " +
                        "The transaction can be posted from the Finance module.");
                }

                var existing = await _journal.GetEntryForSourceAsync(entityName, entityId.ToString());

                if (existing is not null) return PostingResult.AlreadyPosted(existing.JournalEntryId);

                await EnsureChartAsync();

                return await post();
            }
            catch (Exception ex)
            {
                // The operation that triggered this has already committed. Refusing to record
                // the sale because the books could not be written would be the wrong way round:
                // the sale happened. The failure is logged and reported, and the catch-up sweep
                // on the Finance screen will offer to post it again.
                _logger.LogError(ex,
                    "Could not post {Entity} {Id} to the ledger. The transaction itself is unaffected.",
                    entityName, entityId);

                return PostingResult.Failed(
                    $"The transaction was saved, but it could not be posted to the ledger: {ex.Message}");
            }
        }

        private async Task<PostingResult> ReverseSourceAsync(string entityName, int entityId, string reason)
        {
            try
            {
                var existing = await _journal.GetEntryForSourceAsync(entityName, entityId.ToString());

                if (existing is null)
                {
                    return PostingResult.Skipped("There is no ledger entry for this transaction.");
                }

                if (existing.IsReversed)
                {
                    return PostingResult.Skipped("The ledger entry has already been reversed.");
                }

                if (!JournalStatuses.AffectsBalances(existing.Status))
                {
                    return PostingResult.Skipped(
                        "The ledger entry was never posted, so there is nothing to reverse.");
                }

                var reversal = await _journal.ReverseEntryAsync(existing.JournalEntryId, reason);

                return PostingResult.Ok(reversal.JournalEntryId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Could not reverse the ledger entry for {Entity} {Id}.", entityName, entityId);

                return PostingResult.Failed(
                    $"The change was saved, but the ledger entry could not be reversed: {ex.Message}");
            }
        }

        private async Task EnsureChartAsync()
        {
            if (_chartEnsured) return;

            await _accounts.EnsureChartOfAccountsAsync();
            _chartEnsured = true;
        }

        /// <summary>
        /// The account id for a system key, cached per request and never null by the time a
        /// posting uses it - the chart is seeded first, so a missing account means the operator
        /// deleted something that cannot be deleted, and failing loudly is correct.
        /// </summary>
        private async Task<int> AccountAsync(string systemKey)
        {
            var id = await TryAccountAsync(systemKey);
            if (id.HasValue) return id.Value;

            var seed = ChartOfAccounts.FindByKey(systemKey);

            throw new ValidationException(
                $"The chart of accounts has no '{seed?.Name ?? systemKey}' account, so this " +
                "transaction cannot be posted. Restore it from the Chart of Accounts screen.");
        }

        /// <summary>
        /// The account for a system key, or null when the chart has none. Used where a missing
        /// account is a legitimate answer - an expense category the standard chart does not
        /// name falls back to General Expense rather than refusing to post.
        /// </summary>
        private async Task<int?> TryAccountAsync(string systemKey)
        {
            if (_accountCache.TryGetValue(systemKey, out var cached)) return cached;

            var id = await _accounts.ResolveSystemAccountIdAsync(systemKey);
            _accountCache[systemKey] = id;

            return id;
        }

        /// <summary>
        /// Whether money taken by this method sits in the till or in a bank.
        ///
        /// Cash and cheques are cash on hand until they are banked; a card, transfer or
        /// e-wallet payment lands in the bank. Getting this wrong does not change the totals,
        /// but it makes bank reconciliation impossible.
        /// </summary>
        private Task<int> CashAccountForMethodAsync(string? method) =>
            AccountAsync((method ?? "").Trim().ToLowerInvariant() switch
            {
                "card" or "transfer" or "gcash" or "bank" or "e-wallet" => AccountKeys.Bank,
                _ => AccountKeys.Cash
            });

        private async Task AddStatutoryCreditAsync(
            List<JournalLineRequest> lines, string accountKey, decimal amount, string label, Payroll run)
        {
            if (amount <= 0m) return;

            lines.Add(Credit(await AccountAsync(accountKey), amount,
                $"{label}, {Period(run)}", employeeId: run.EmployeeId));
        }

        private static string Period(Payroll run) =>
            $"{run.PeriodStart:d MMM} - {run.PeriodEnd:d MMM yyyy}";

        private static JournalLineRequest Debit(
            int accountId, decimal amount, string description,
            int? memberId = null, int? supplierId = null, int? employeeId = null) => new()
            {
                AccountId = accountId,
                Debit = amount,
                Description = description,
                MemberId = memberId,
                SupplierId = supplierId,
                EmployeeId = employeeId
            };

        private static JournalLineRequest Credit(
            int accountId, decimal amount, string description,
            int? memberId = null, int? supplierId = null, int? employeeId = null) => new()
            {
                AccountId = accountId,
                Credit = amount,
                Description = description,
                MemberId = memberId,
                SupplierId = supplierId,
                EmployeeId = employeeId
            };
    }
}
