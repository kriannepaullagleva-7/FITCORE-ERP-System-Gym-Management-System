using ERP_domain.entities;
using ERP_infrastructure.repositories;
using ERP_infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP_Tests;

/// <summary>
/// The ledger, exercised through the operational services that feed it.
///
/// These are the tests that matter most about Finance, because the ledger's whole value is
/// that it agrees with the operations it describes. Asserting on journal entries written by
/// hand would prove only that the journal service can add up; asserting on the entries a
/// *sale* produces proves that the books and the till tell the same story.
///
/// Everything runs against real SQLite, so the queries, the foreign keys and the transactions
/// are genuinely exercised.
/// </summary>
public class LedgerTests
{
    private sealed class Harness : IDisposable
    {
        public TenantDbFixture Db { get; }
        public ISaleService Sales { get; }
        public IPaymentService Payments { get; }
        public IProductService Products { get; }
        public IInventoryService Inventory { get; }
        public IMemberService Members { get; }
        public IPurchaseService Purchases { get; }
        public ISaleReturnService Returns { get; }
        public IExpenseService Expenses { get; }
        public IFinancialPeriodService Periods { get; }

        public Harness()
        {
            Db = new TenantDbFixture();

            var actor = new FakeCurrentUserAccessor(roleKey: ErpRoles.Admin);

            var memberRepo = new MemberRepository(Db.Context);
            var productRepo = new ProductRepository(Db.Context);
            var inventoryRepo = new InventoryRepository(Db.Context);
            var paymentRepo = new PaymentRepository(Db.Context);
            var saleRepo = new SaleRepository(Db.Context);
            var subscriptionRepo = new SubscriptionRepository(Db.Context);
            var supplierRepo = new GenericRepository<Supplier>(Db.Context);
            var employeeRepo = new EmployeeRepository(Db.Context);
            var expenseRepo = new ExpenseRepository(Db.Context);

            var audit = new TenantAuditService(Db.Context, actor);

            Members = new MemberService(memberRepo, paymentRepo, subscriptionRepo);
            Inventory = new InventoryService(
                inventoryRepo, productRepo, supplierRepo, Db.Context, actor, audit, Db.Finance);
            Products = new ProductService(productRepo, Inventory, Db.Context);
            Payments = new PaymentService(
                paymentRepo, subscriptionRepo, memberRepo, Db.Context, actor, Db.Finance);
            Sales = new SaleService(
                saleRepo, memberRepo, productRepo, paymentRepo, Db.Context, actor, audit, Db.Finance);
            Purchases = new PurchaseService(Db.Context, Inventory, Db.Finance, actor, audit);
            Returns = new SaleReturnService(Db.Context, Db.Finance, actor, audit);
            Expenses = new ExpenseService(
                expenseRepo, employeeRepo, Db.Context, Db.Accounts, Db.Finance, actor);
            Periods = new FinancialPeriodService(Db.Context, actor);
        }

        public void Dispose() => Db.Dispose();

        /// <summary>The posted balance of an account, by the role it plays in the chart.</summary>
        public async Task<decimal> BalanceAsync(string systemKey)
        {
            var account = await Db.Context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.SystemKey == systemKey);

            if (account is null) return 0m;

            var totals = await Db.Context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.AccountId == account.AccountId &&
                            l.Entry.Status == JournalStatuses.Posted)
                .GroupBy(_ => 1)
                .Select(g => new { Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
                .FirstOrDefaultAsync();

            return AccountTypes.BalanceOf(
                account.AccountType, totals?.Debit ?? 0m, totals?.Credit ?? 0m);
        }
    }

    private static async Task<(int MemberId, int ProductId, int SupplierId)> SeedAsync(
        Harness h, decimal stock = 20m, decimal cost = 60m, decimal price = 100m)
    {
        var member = await h.Members.CreateMemberAsync("Test", "Buyer", "555", "buyer@example.com");

        var supplier = new Supplier
        {
            SupplierCode = "SUP-1",
            SupplierName = "Test Supplier",
            IsActive = true
        };

        h.Db.Context.Suppliers.Add(supplier);
        await h.Db.Context.SaveChangesAsync();

        var product = await h.Products.CreateProductAsync(
            "PRD-1", "Protein", "Supplements", cost, price, stock, 5m);

        return (member.MemberId, product.ProductId, supplier.SupplierId);
    }

    /// <summary>One line on a sale, which is all any of these tests needs.</summary>
    private static List<SaleLineRequest> Lines(int productId, int quantity) =>
        new() { new SaleLineRequest { ProductId = productId, Quantity = quantity } };

    // ================================================================== posting a sale

    /// <summary>
    /// A completed sale is four postings, not one: the money owed, the revenue earned, the
    /// cost of what left the shelf, and the stock it came out of. Getting any of them wrong
    /// makes the income statement and the balance sheet disagree.
    /// </summary>
    [Fact]
    public async Task A_sale_debits_receivables_and_credits_revenue_and_charges_cost_of_sales()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 2), discount: 0m, notes: "", settleNow: false);

        Assert.Equal(200m, await h.BalanceAsync(AccountKeys.AccountsReceivable));
        Assert.Equal(200m, await h.BalanceAsync(AccountKeys.ProductRevenue));

        // Cost is the weighted average of the stock on hand, which for opening stock is the
        // product's catalogue cost.
        Assert.Equal(120m, await h.BalanceAsync(AccountKeys.CostOfGoodsSold));
        Assert.Equal(-120m, await h.BalanceAsync(AccountKeys.Inventory));
    }

    [Fact]
    public async Task A_discount_is_posted_as_contra_revenue_rather_than_reducing_the_sale()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 1), discount: 25m, notes: "", settleNow: false);

        // Gross revenue stays at the full line value; the discount is its own debit. That is
        // what lets a gym see how much it is giving away rather than only what it took.
        Assert.Equal(100m, await h.BalanceAsync(AccountKeys.ProductRevenue));
        Assert.Equal(-25m, await h.BalanceAsync(AccountKeys.SalesDiscounts));
        Assert.Equal(75m, await h.BalanceAsync(AccountKeys.AccountsReceivable));
    }

    [Fact]
    public async Task Settling_a_sale_moves_the_balance_from_receivables_to_cash()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 1), discount: 0m, notes: "", settleNow: true,
            paymentMethod: "Cash");

        Assert.Equal(0m, await h.BalanceAsync(AccountKeys.AccountsReceivable));
        Assert.Equal(100m, await h.BalanceAsync(AccountKeys.Cash));
    }

    /// <summary>
    /// Cash and bank are separate accounts, or the cash flow statement cannot tell the till
    /// from the bank and reconciliation becomes impossible.
    /// </summary>
    [Fact]
    public async Task A_card_payment_lands_in_the_bank_rather_than_the_till()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 1), discount: 0m, notes: "", settleNow: true,
            paymentMethod: "Card");

        Assert.Equal(0m, await h.BalanceAsync(AccountKeys.Cash));
        Assert.Equal(100m, await h.BalanceAsync(AccountKeys.Bank));
    }

    // ================================================================== idempotency

    /// <summary>
    /// Posting is keyed to the record that caused it, so a retry cannot double the books.
    /// This is the property that makes the catch-up sweep safe to run whenever.
    /// </summary>
    [Fact]
    public async Task Posting_the_same_sale_twice_does_nothing_the_second_time()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        var sale = await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 1), discount: 0m, notes: "", settleNow: false);

        var before = await h.Db.Context.JournalEntries.CountAsync();

        var again = await h.Db.Finance.PostSaleAsync(sale.SaleId);

        Assert.False(again.Posted);
        Assert.Equal(before, await h.Db.Context.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task The_catch_up_sweep_finds_nothing_when_everything_posted_as_it_happened()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 1), discount: 0m, notes: "", settleNow: true);

        Assert.Equal(0, await h.Db.Finance.CountOutstandingAsync());
        Assert.Equal(0, await h.Db.Finance.PostOutstandingAsync());
    }

    // ================================================================== cancelling

    /// <summary>
    /// A cancelled sale is corrected by reversal, never by deleting the posting. Both entries
    /// stay visible, and the balances come back to where they started.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_sale_reverses_its_postings_rather_than_deleting_them()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        var sale = await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 2), discount: 0m, notes: "", settleNow: true);

        await h.Sales.CancelSaleAsync(sale.SaleId, "Wrong member");

        Assert.Equal(0m, await h.BalanceAsync(AccountKeys.ProductRevenue));
        Assert.Equal(0m, await h.BalanceAsync(AccountKeys.CostOfGoodsSold));
        Assert.Equal(0m, await h.BalanceAsync(AccountKeys.Cash));

        // The original and its reversal are both on record, and both still posted - they net
        // to zero between them rather than one of them being taken out of the ledger.
        var entries = await h.Db.Context.JournalEntries.AsNoTracking().ToListAsync();

        Assert.Contains(entries, e => e.ReversedByEntryId != null);
        Assert.Contains(entries, e => e.Source == JournalSources.Reversal);
        Assert.All(entries, e => Assert.Equal(JournalStatuses.Posted, e.Status));
    }

    // ================================================================== purchasing

    /// <summary>
    /// Receiving stock is <c>Dr Inventory / Cr Accounts Payable</c> and nothing else. Nothing
    /// is expensed: goods bought for resale are an asset until they are sold, and treating a
    /// delivery as an expense is what makes the month it arrives look like a loss.
    /// </summary>
    [Fact]
    public async Task Receiving_a_purchase_raises_an_asset_and_a_payable_not_an_expense()
    {
        using var h = new Harness();
        var (_, productId, supplierId) = await SeedAsync(h, stock: 0m, cost: 0m);

        var purchase = await h.Purchases.CreatePurchaseAsync(
            supplierId, DateTime.UtcNow, null, "INV-1", 0m, 0m, "",
            new[] { new PurchaseLineRequest { ProductId = productId, Quantity = 10m, UnitCost = 50m } });

        await h.Purchases.MarkOrderedAsync(purchase.PurchaseId);
        await h.Purchases.ReceiveAsync(purchase.PurchaseId);

        Assert.Equal(500m, await h.BalanceAsync(AccountKeys.Inventory));
        Assert.Equal(500m, await h.BalanceAsync(AccountKeys.AccountsPayable));
        Assert.Equal(0m, await h.BalanceAsync(AccountKeys.GeneralExpense));
        Assert.Equal(0m, await h.BalanceAsync(AccountKeys.CostOfGoodsSold));
    }

    [Fact]
    public async Task Paying_a_supplier_clears_the_payable_and_moves_cash()
    {
        using var h = new Harness();
        var (_, productId, supplierId) = await SeedAsync(h, stock: 0m, cost: 0m);

        var purchase = await h.Purchases.CreatePurchaseAsync(
            supplierId, DateTime.UtcNow, null, "", 0m, 0m, "",
            new[] { new PurchaseLineRequest { ProductId = productId, Quantity = 4m, UnitCost = 25m } });

        await h.Purchases.MarkOrderedAsync(purchase.PurchaseId);
        await h.Purchases.ReceiveAsync(purchase.PurchaseId);

        await h.Purchases.PaySupplierAsync(
            supplierId, purchase.PurchaseId, 100m, DateTime.UtcNow, "Cash", "", "", null);

        Assert.Equal(0m, await h.BalanceAsync(AccountKeys.AccountsPayable));
        Assert.Equal(-100m, await h.BalanceAsync(AccountKeys.Cash));
    }

    /// <summary>
    /// Weighted average cost: ten at 50 then ten at 70 is twenty at 60. Getting this wrong
    /// misstates both the inventory on the balance sheet and the gross profit on every later
    /// sale, and it compounds.
    /// </summary>
    [Fact]
    public async Task Receiving_at_a_new_price_moves_the_weighted_average_cost()
    {
        using var h = new Harness();
        var (_, productId, supplierId) = await SeedAsync(h, stock: 0m, cost: 0m);

        await h.Inventory.StockInAsync(productId, 10m, supplierId, "A", "", unitCost: 50m);
        await h.Inventory.StockInAsync(productId, 10m, supplierId, "B", "", unitCost: 70m);

        var inventory = await h.Db.Context.Inventories
            .AsNoTracking()
            .FirstAsync(i => i.ProductId == productId);

        Assert.Equal(20m, inventory.QuantityOnHand);
        Assert.Equal(60m, inventory.AverageCost);
    }

    [Fact]
    public async Task A_sale_is_costed_at_the_average_when_it_was_sold_not_at_a_later_price()
    {
        using var h = new Harness();
        var (memberId, productId, supplierId) = await SeedAsync(h, stock: 0m, cost: 0m);

        await h.Inventory.StockInAsync(productId, 10m, supplierId, "A", "", unitCost: 50m);

        await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 2), discount: 0m, notes: "", settleNow: false);

        // Stock arrives later at a much higher price. The sale already made must not be
        // recosted - last month's gross profit does not change because this month's delivery
        // was expensive.
        await h.Inventory.StockInAsync(productId, 10m, supplierId, "B", "", unitCost: 200m);

        Assert.Equal(100m, await h.BalanceAsync(AccountKeys.CostOfGoodsSold));
    }

    // ================================================================== returns

    /// <summary>
    /// A return reverses both halves of a sale: the money and the goods. Reversing only the
    /// money would leave the gym believing it had sold stock that is back on the shelf.
    /// </summary>
    [Fact]
    public async Task A_return_reduces_revenue_and_puts_the_cost_back_into_inventory()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        var sale = await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 3), discount: 0m, notes: "", settleNow: true);

        var line = await h.Db.Context.SaleItems.AsNoTracking()
            .FirstAsync(i => i.SaleId == sale.SaleId);

        await h.Returns.CreateReturnAsync(
            sale.SaleId,
            new[] { new ReturnLineRequest { SaleItemId = line.SaleItemId, Quantity = 1 } },
            ReturnReasons.CustomerChangedMind, null, "Cash", restockToInventory: true, "");

        // 300 taken, 100 given back.
        Assert.Equal(-100m, await h.BalanceAsync(AccountKeys.SalesReturns));
        Assert.Equal(200m, await h.BalanceAsync(AccountKeys.Cash));

        // 180 of cost charged out, 60 of it back.
        Assert.Equal(120m, await h.BalanceAsync(AccountKeys.CostOfGoodsSold));
        Assert.Equal(-120m, await h.BalanceAsync(AccountKeys.Inventory));

        var stock = await h.Db.Context.Inventories.AsNoTracking()
            .FirstAsync(i => i.ProductId == productId);

        Assert.Equal(18m, stock.QuantityOnHand);
    }

    /// <summary>
    /// Damaged goods do not go back on the shelf whatever the operator ticked, and the cost
    /// lands in shrinkage rather than inventory - stock that cannot be sold is a loss, not an
    /// asset.
    /// </summary>
    [Fact]
    public async Task A_damaged_return_is_written_off_rather_than_restocked()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        var sale = await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 2), discount: 0m, notes: "", settleNow: true);

        var line = await h.Db.Context.SaleItems.AsNoTracking()
            .FirstAsync(i => i.SaleId == sale.SaleId);

        await h.Returns.CreateReturnAsync(
            sale.SaleId,
            new[] { new ReturnLineRequest { SaleItemId = line.SaleItemId, Quantity = 1 } },
            ReturnReasons.Damaged, null, "Cash", restockToInventory: true, "");

        Assert.Equal(60m, await h.BalanceAsync(AccountKeys.InventoryShrinkage));

        var stock = await h.Db.Context.Inventories.AsNoTracking()
            .FirstAsync(i => i.ProductId == productId);

        Assert.Equal(18m, stock.QuantityOnHand);
    }

    [Fact]
    public async Task The_same_line_cannot_be_returned_more_times_than_it_was_sold()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        var sale = await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 2), discount: 0m, notes: "", settleNow: true);

        var line = await h.Db.Context.SaleItems.AsNoTracking()
            .FirstAsync(i => i.SaleId == sale.SaleId);

        await h.Returns.CreateReturnAsync(
            sale.SaleId,
            new[] { new ReturnLineRequest { SaleItemId = line.SaleItemId, Quantity = 2 } },
            ReturnReasons.Other, null, "Cash", true, "");

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            h.Returns.CreateReturnAsync(
                sale.SaleId,
                new[] { new ReturnLineRequest { SaleItemId = line.SaleItemId, Quantity = 1 } },
                ReturnReasons.Other, null, "Cash", true, ""));

        Assert.Contains("can still be returned", refused.Message);
    }

    // ================================================================== the statements

    /// <summary>
    /// The single most important property of a set of books: whatever happened, the two sides
    /// agree. A trial balance that does not add up means something was written outside the
    /// journal service, which is the one thing the ledger cannot survive.
    /// </summary>
    [Fact]
    public async Task The_trial_balance_agrees_after_a_full_day_of_trading()
    {
        using var h = new Harness();
        var (memberId, productId, supplierId) = await SeedAsync(h, stock: 0m, cost: 0m);

        var purchase = await h.Purchases.CreatePurchaseAsync(
            supplierId, DateTime.UtcNow, null, "", 0m, 0m, "",
            new[] { new PurchaseLineRequest { ProductId = productId, Quantity = 20m, UnitCost = 55m } });

        await h.Purchases.MarkOrderedAsync(purchase.PurchaseId);
        await h.Purchases.ReceiveAsync(purchase.PurchaseId);
        await h.Purchases.PaySupplierAsync(
            supplierId, purchase.PurchaseId, 600m, DateTime.UtcNow, "Transfer", "", "", null);

        await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 5), discount: 40m, notes: "", settleNow: true);

        await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 3), discount: 0m, notes: "", settleNow: false);

        await h.Expenses.CreateExpenseAsync(
            ExpenseCategories.Rent, "March rent", 8000m, DateTime.UtcNow,
            "Transfer", "", null);

        var balance = await h.Db.Journal.GetTrialBalanceAsync();

        Assert.True(balance.IsBalanced,
            $"Trial balance out by {balance.Difference:N2}.");
    }

    /// <summary>
    /// Gross profit is net revenue less cost of sales, and net income is that less everything
    /// else. Cost of goods sold must sit above the line, or a gym cannot tell "we are not
    /// making enough margin" from "we are spending too much on rent".
    /// </summary>
    [Fact]
    public async Task The_income_statement_separates_cost_of_sales_from_operating_expenses()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h, stock: 20m, cost: 60m, price: 100m);

        await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 10), discount: 0m, notes: "", settleNow: true);

        await h.Expenses.CreateExpenseAsync(
            ExpenseCategories.Rent, "Rent", 300m, DateTime.UtcNow, "Cash", "", null);

        var statement = await h.Db.FinanceReports.GetIncomeStatementAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        Assert.Equal(1000m, statement.NetRevenue);
        Assert.Equal(600m, statement.CostOfGoodsSold);
        Assert.Equal(400m, statement.GrossProfit);
        Assert.Equal(300m, statement.OperatingExpenses);
        Assert.Equal(100m, statement.NetIncome);
        Assert.Equal(40m, statement.GrossMarginPercent);
    }

    /// <summary>
    /// Assets equal liabilities plus equity - and they do without anybody running a year-end
    /// journal, because retained earnings are computed from the revenue and expense accounts
    /// rather than stored.
    /// </summary>
    [Fact]
    public async Task The_balance_sheet_balances_without_closing_the_books()
    {
        using var h = new Harness();
        var (memberId, productId, supplierId) = await SeedAsync(h, stock: 0m, cost: 0m);

        var purchase = await h.Purchases.CreatePurchaseAsync(
            supplierId, DateTime.UtcNow, null, "", 0m, 0m, "",
            new[] { new PurchaseLineRequest { ProductId = productId, Quantity = 10m, UnitCost = 40m } });

        await h.Purchases.MarkOrderedAsync(purchase.PurchaseId);
        await h.Purchases.ReceiveAsync(purchase.PurchaseId);

        await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 4), discount: 0m, notes: "", settleNow: true);

        await h.Expenses.CreateExpenseAsync(
            ExpenseCategories.Electricity, "Power", 150m, DateTime.UtcNow, "Cash", "", null);

        var sheet = await h.Db.FinanceReports.GetBalanceSheetAsync(DateTime.UtcNow.AddDays(1));

        Assert.True(sheet.IsBalanced,
            $"Assets {sheet.TotalAssets:N2} against liabilities and equity " +
            $"{sheet.LiabilitiesAndEquity:N2}.");
    }

    // ================================================================== journal rules

    [Fact]
    public async Task An_entry_whose_sides_differ_is_refused()
    {
        using var h = new Harness();
        await h.Db.Accounts.EnsureChartOfAccountsAsync();

        var cash = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.Cash);
        var revenue = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.OtherRevenue);

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            h.Db.Journal.CreateEntryAsync(DateTime.UtcNow, "Lopsided", new[]
            {
                new JournalLineRequest { AccountId = cash!.Value, Debit = 100m },
                new JournalLineRequest { AccountId = revenue!.Value, Credit = 90m }
            }));

        Assert.Contains("does not balance", refused.Message);
    }

    [Fact]
    public async Task A_posted_entry_cannot_be_edited()
    {
        using var h = new Harness();
        await h.Db.Accounts.EnsureChartOfAccountsAsync();

        var cash = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.Cash);
        var revenue = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.OtherRevenue);

        var entry = await h.Db.Journal.CreateEntryAsync(DateTime.UtcNow, "Takings", new[]
        {
            new JournalLineRequest { AccountId = cash!.Value, Debit = 100m },
            new JournalLineRequest { AccountId = revenue!.Value, Credit = 100m }
        });

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            h.Db.Journal.UpdateDraftAsync(entry.JournalEntryId, DateTime.UtcNow, "Changed", "",
                entry.Lines.Select(l => new JournalLineRequest
                {
                    AccountId = l.AccountId, Debit = l.Debit, Credit = l.Credit
                })));

        Assert.Contains("reverse it", refused.Message);
    }

    /// <summary>
    /// A reversal is the mirror image, and it leaves the original visible. Deleting the entry
    /// instead would make the ledger a record of current belief rather than of what happened.
    /// </summary>
    [Fact]
    public async Task Reversing_an_entry_writes_its_mirror_and_leaves_both_on_record()
    {
        using var h = new Harness();
        await h.Db.Accounts.EnsureChartOfAccountsAsync();

        var cash = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.Cash);
        var revenue = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.OtherRevenue);

        var entry = await h.Db.Journal.CreateEntryAsync(DateTime.UtcNow, "Takings", new[]
        {
            new JournalLineRequest { AccountId = cash!.Value, Debit = 100m },
            new JournalLineRequest { AccountId = revenue!.Value, Credit = 100m }
        });

        var reversal = await h.Db.Journal.ReverseEntryAsync(entry.JournalEntryId, "Entered twice");

        Assert.Equal(entry.JournalEntryId, reversal.ReversesEntryId);
        Assert.Equal(100m, reversal.Lines.Single(l => l.AccountId == cash.Value).Credit);
        Assert.Equal(100m, reversal.Lines.Single(l => l.AccountId == revenue.Value).Debit);

        // The original stays posted and is marked as reversed. Both entries count, so the
        // account comes back to zero rather than being left holding only the reversal.
        var original = await h.Db.Journal.GetEntryAsync(entry.JournalEntryId);

        Assert.Equal(JournalStatuses.Posted, original!.Status);
        Assert.True(original.IsReversed);
        Assert.Equal("Reversed", original.DisplayStatus);

        Assert.Equal(0m, await h.BalanceAsync(AccountKeys.Cash));
    }

    [Fact]
    public async Task An_entry_cannot_be_reversed_twice()
    {
        using var h = new Harness();
        await h.Db.Accounts.EnsureChartOfAccountsAsync();

        var cash = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.Cash);
        var revenue = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.OtherRevenue);

        var entry = await h.Db.Journal.CreateEntryAsync(DateTime.UtcNow, "Takings", new[]
        {
            new JournalLineRequest { AccountId = cash!.Value, Debit = 50m },
            new JournalLineRequest { AccountId = revenue!.Value, Credit = 50m }
        });

        await h.Db.Journal.ReverseEntryAsync(entry.JournalEntryId, "First");

        await Assert.ThrowsAsync<ValidationException>(() =>
            h.Db.Journal.ReverseEntryAsync(entry.JournalEntryId, "Second"));
    }

    // ================================================================== periods

    /// <summary>
    /// Closing a month is what stops last quarter's figures moving after they have been
    /// reported, so a posting dated into it has to be refused rather than quietly accepted.
    /// </summary>
    [Fact]
    public async Task A_closed_period_refuses_a_new_posting()
    {
        using var h = new Harness();
        await h.Db.Accounts.EnsureChartOfAccountsAsync();

        var when = DateTime.UtcNow.AddMonths(-1);

        await h.Periods.EnsureYearAsync(when.Year);
        await h.Periods.CloseAsync(when.Year, when.Month, "Reported");

        var cash = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.Cash);
        var revenue = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.OtherRevenue);

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            h.Db.Journal.CreateEntryAsync(when, "Late entry", new[]
            {
                new JournalLineRequest { AccountId = cash!.Value, Debit = 10m },
                new JournalLineRequest { AccountId = revenue!.Value, Credit = 10m }
            }));

        Assert.Contains("closed", refused.Message);
    }

    [Fact]
    public async Task Reopening_a_period_lets_it_accept_postings_again()
    {
        using var h = new Harness();
        await h.Db.Accounts.EnsureChartOfAccountsAsync();

        var when = DateTime.UtcNow.AddMonths(-1);

        await h.Periods.EnsureYearAsync(when.Year);
        await h.Periods.CloseAsync(when.Year, when.Month, "Reported");
        await h.Periods.ReopenAsync(when.Year, when.Month, "A late supplier invoice arrived");

        var cash = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.Cash);
        var revenue = await h.Db.Accounts.ResolveSystemAccountIdAsync(AccountKeys.OtherRevenue);

        var entry = await h.Db.Journal.CreateEntryAsync(when, "Late entry", new[]
        {
            new JournalLineRequest { AccountId = cash!.Value, Debit = 10m },
            new JournalLineRequest { AccountId = revenue!.Value, Credit = 10m }
        });

        Assert.Equal(JournalStatuses.Posted, entry.Status);
    }

    // ================================================================== the chart

    /// <summary>
    /// A system account is the target of a posting rule, so removing it or switching it off
    /// would stop sales reaching the books. Renaming it is fine - the rules look it up by a
    /// stable key, not by what the operator calls it.
    /// </summary>
    [Fact]
    public async Task A_system_account_can_be_renamed_but_not_deleted_or_deactivated()
    {
        using var h = new Harness();
        await h.Db.Accounts.EnsureChartOfAccountsAsync();

        var accounts = await h.Db.Accounts.GetAccountsAsync();
        var cash = accounts.Single(a => a.SystemKey == AccountKeys.Cash);

        var renamed = await h.Db.Accounts.UpdateAccountAsync(
            cash.AccountId, cash.AccountCode, "The Till", cash.AccountType,
            cash.AccountSubType, "", null, isActive: true);

        Assert.Equal("The Till", renamed!.AccountName);

        await Assert.ThrowsAsync<ValidationException>(() =>
            h.Db.Accounts.DeleteAccountAsync(cash.AccountId));

        await Assert.ThrowsAsync<ValidationException>(() =>
            h.Db.Accounts.UpdateAccountAsync(
                cash.AccountId, cash.AccountCode, "The Till", cash.AccountType,
                cash.AccountSubType, "", null, isActive: false));
    }

    [Fact]
    public async Task Seeding_the_chart_twice_does_not_duplicate_it()
    {
        using var h = new Harness();

        var first = await h.Db.Accounts.EnsureChartOfAccountsAsync();
        var second = await h.Db.Accounts.EnsureChartOfAccountsAsync();

        Assert.True(first > 0);
        Assert.Equal(0, second);

        Assert.Equal(
            ChartOfAccounts.Standard.Count,
            await h.Db.Context.Accounts.CountAsync());
    }

    // ================================================================== receivables

    /// <summary>
    /// Ageing is what makes a receivables report worth reading: the total says nothing, and
    /// "this has been outstanding for four months" is the part somebody can act on.
    /// </summary>
    [Fact]
    public async Task Receivables_are_aged_from_the_date_of_the_document()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        var sale = await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 1), discount: 0m, notes: "", settleNow: false);

        // Back-dated so it falls into a real ageing bucket rather than "current".
        var row = await h.Db.Context.Sales.FirstAsync(s => s.SaleId == sale.SaleId);
        row.SaleDate = DateTime.UtcNow.AddDays(-45);
        await h.Db.Context.SaveChangesAsync();

        var aged = await h.Db.FinanceReports.GetReceivablesAsync(DateTime.UtcNow);

        Assert.Equal(100m, aged.Total);
        Assert.Equal(100m, aged.Days31To60);
        Assert.Equal(0m, aged.Current);
    }

    [Fact]
    public async Task A_settled_sale_does_not_appear_in_receivables()
    {
        using var h = new Harness();
        var (memberId, productId, _) = await SeedAsync(h);

        await h.Sales.CreateSaleAsync(
            memberId, Lines(productId, 1), discount: 0m, notes: "", settleNow: true);

        var aged = await h.Db.FinanceReports.GetReceivablesAsync(DateTime.UtcNow);

        Assert.Equal(0m, aged.Total);
        Assert.Empty(aged.Rows);
    }

    // ================================================================== renewals

    /// <summary>A membership whose current term still has a year left to run.</summary>
    private static async Task<int> SeedRenewableSubscriptionAsync(Harness h, decimal price = 12000m)
    {
        var member = await h.Members.CreateMemberAsync("Renewing", "Member", "555", "r@example.com");

        var plan = new MembershipPlan
        {
            PlanName = "Annual",
            DurationMonths = 12,
            Price = price,
            IsActive = true
        };

        h.Db.Context.MembershipPlans.Add(plan);
        await h.Db.Context.SaveChangesAsync();

        var subscription = new Subscription
        {
            MemberId = member.MemberId,
            PlanId = plan.PlanId,
            StartDate = DateTime.UtcNow.AddMonths(-11),
            EndDate = DateTime.UtcNow.AddMonths(1),
            Status = "Active"
        };

        h.Db.Context.Subscriptions.Add(subscription);
        await h.Db.Context.SaveChangesAsync();

        return subscription.SubscriptionId;
    }

    /// <summary>
    /// A renewal is dated when it was sold, not when the term it buys begins.
    ///
    /// Renewing early extends from the existing end date, so the new term can start a year
    /// away. Posting the entry under that date put the revenue and the receivable a year into
    /// the future: every current report missed them while the member's payment was recorded
    /// today, which drove receivables negative - the books saying the gym owed its members
    /// money. The trial balance still balanced, because both halves were equally misdated, so
    /// nothing caught it.
    /// </summary>
    [Fact]
    public async Task A_renewal_is_posted_on_the_day_it_is_sold_not_when_the_term_starts()
    {
        using var h = new Harness();
        var subscriptionId = await SeedRenewableSubscriptionAsync(h);

        // The term starts when the current one ends - a month away here, and up to a full plan
        // duration away in general.
        var termStart = DateTime.UtcNow.AddMonths(1);

        var result = await h.Db.Finance.PostSubscriptionRenewalAsync(subscriptionId, termStart);
        Assert.True(result.Posted);

        var entry = await h.Db.Context.JournalEntries
            .AsNoTracking()
            .FirstAsync(e => e.SourceEntityName == "SubscriptionRenewal");

        Assert.Equal(DateTime.UtcNow.Date, entry.EntryDate.Date);
        Assert.Equal(DateTime.UtcNow.Year, entry.PeriodYear);
        Assert.Equal(DateTime.UtcNow.Month, entry.PeriodMonth);
    }

    /// <summary>
    /// The consequence the date bug had, asserted on directly: a renewal earns revenue in the
    /// period it was sold, and a report for that period has to show it.
    /// </summary>
    [Fact]
    public async Task A_renewal_appears_in_this_period_revenue_and_receivables()
    {
        using var h = new Harness();
        var subscriptionId = await SeedRenewableSubscriptionAsync(h);

        await h.Db.Finance.PostSubscriptionRenewalAsync(
            subscriptionId, DateTime.UtcNow.AddMonths(1));

        Assert.Equal(12000m, await h.BalanceAsync(AccountKeys.AccountsReceivable));
        Assert.Equal(12000m, await h.BalanceAsync(AccountKeys.MembershipRevenue));

        var statement = await h.Db.FinanceReports.GetIncomeStatementAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        Assert.Equal(12000m, statement.NetRevenue);
    }

    /// <summary>
    /// The term is still what identifies a renewal, so posting the same one twice does nothing
    /// while a second term posts again. This is the property that kept the term in the key when
    /// it stopped being the entry date.
    /// </summary>
    [Fact]
    public async Task Each_term_posts_once_however_many_times_it_is_offered()
    {
        using var h = new Harness();
        var subscriptionId = await SeedRenewableSubscriptionAsync(h);

        var firstTerm = DateTime.UtcNow.AddMonths(1);
        var secondTerm = firstTerm.AddMonths(12);

        Assert.True((await h.Db.Finance.PostSubscriptionRenewalAsync(subscriptionId, firstTerm)).Posted);

        var again = await h.Db.Finance.PostSubscriptionRenewalAsync(subscriptionId, firstTerm);
        Assert.False(again.Posted);

        Assert.True((await h.Db.Finance.PostSubscriptionRenewalAsync(subscriptionId, secondTerm)).Posted);

        var entries = await h.Db.Context.JournalEntries
            .AsNoTracking()
            .CountAsync(e => e.SourceEntityName == "SubscriptionRenewal");

        Assert.Equal(2, entries);
        Assert.Equal(24000m, await h.BalanceAsync(AccountKeys.MembershipRevenue));
    }

    /// <summary>A membership sold today whose term has not started yet.</summary>
    private static async Task<int> SeedSubscriptionAsync(Harness h, DateTime startDate)
    {
        var member = await h.Members.CreateMemberAsync("New", "Joiner", "555", "n@example.com");

        var plan = new MembershipPlan
        {
            PlanName = "Annual", DurationMonths = 12, Price = 12000m, IsActive = true
        };

        h.Db.Context.MembershipPlans.Add(plan);
        await h.Db.Context.SaveChangesAsync();

        var subscription = new Subscription
        {
            MemberId = member.MemberId,
            PlanId = plan.PlanId,
            StartDate = startDate,
            EndDate = startDate.AddMonths(12),
            Status = "Active"
        };

        h.Db.Context.Subscriptions.Add(subscription);
        await h.Db.Context.SaveChangesAsync();

        return subscription.SubscriptionId;
    }

    /// <summary>
    /// A membership that starts next week was still sold today, so that is when it is posted.
    ///
    /// Dating it by the term start put the receivable and the revenue in the future while the
    /// joining member's payment was recorded today, which is what drove receivables negative.
    /// </summary>
    [Fact]
    public async Task A_membership_starting_later_is_still_posted_today()
    {
        using var h = new Harness();
        var subscriptionId = await SeedSubscriptionAsync(h, DateTime.UtcNow.AddDays(7));

        Assert.True((await h.Db.Finance.PostSubscriptionAsync(subscriptionId)).Posted);

        var entry = await h.Db.Context.JournalEntries
            .AsNoTracking()
            .FirstAsync(e => e.SourceEntityName == nameof(Subscription));

        Assert.Equal(DateTime.UtcNow.Date, entry.EntryDate.Date);
        Assert.Equal(12000m, await h.BalanceAsync(AccountKeys.AccountsReceivable));
    }

    /// <summary>
    /// A membership entered after the fact keeps its own start date, because there the start
    /// date is the earlier of the two and is what actually happened. Only dates in the future
    /// are pulled back to today.
    /// </summary>
    [Fact]
    public async Task A_backdated_membership_keeps_its_own_start_date()
    {
        using var h = new Harness();

        var backdated = DateTime.UtcNow.AddDays(-10);
        var subscriptionId = await SeedSubscriptionAsync(h, backdated);

        Assert.True((await h.Db.Finance.PostSubscriptionAsync(subscriptionId)).Posted);

        var entry = await h.Db.Context.JournalEntries
            .AsNoTracking()
            .FirstAsync(e => e.SourceEntityName == nameof(Subscription));

        Assert.Equal(backdated.Date, entry.EntryDate.Date);
    }

    /// <summary>
    /// The invariant the two date fixes exist to protect: receivables is an asset, and a gym
    /// does not owe its members money on it. A membership sold and paid for on the same day
    /// nets to nothing there - it does not go negative because only one half was dated forward.
    /// </summary>
    [Fact]
    public async Task Selling_and_settling_a_membership_leaves_receivables_flat()
    {
        using var h = new Harness();
        var subscriptionId = await SeedSubscriptionAsync(h, DateTime.UtcNow.AddDays(7));

        var subscription = await h.Db.Context.Subscriptions
            .AsNoTracking()
            .FirstAsync(s => s.SubscriptionId == subscriptionId);

        await h.Db.Finance.PostSubscriptionAsync(subscriptionId);

        await h.Payments.RecordPaymentAsync(
            subscription.MemberId, subscriptionId, null, 12000m,
            DateTime.UtcNow, "Cash", "", "Completed", "");

        Assert.Equal(0m, await h.BalanceAsync(AccountKeys.AccountsReceivable));
    }
}
