using ERP_domain.entities;
using ERP_infrastructure.repositories;
using ERP_infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP_Tests;

/// <summary>
/// The relationships between records, asserted against a real relational database.
///
/// These are the rules a foreign key exists to enforce, and they are tested here rather than
/// trusted because a delete behaviour is invisible until somebody deletes something. SQLite is
/// used for the same reason the other service tests use it: cascade rules, restrict rules and
/// referential integrity are genuinely executed, which the EF in-memory provider does not do.
///
/// The property under test throughout is that **history survives its subject**. A gym can stop
/// stocking a product, stop buying from a supplier and stop employing somebody, and none of
/// those should be able to take the record of what already happened with them.
/// </summary>
public class RelationalIntegrityTests
{
    private static List<SaleLineRequest> Lines(int productId, int quantity) =>
        new() { new SaleLineRequest { ProductId = productId, Quantity = quantity } };

    private sealed class Harness : IDisposable
    {
        public TenantDbFixture Db { get; }
        public IProductService Products { get; }
        public IInventoryService Inventory { get; }
        public ISupplierService Suppliers { get; }
        public ISaleService Sales { get; }
        public IMemberService Members { get; }

        public Harness()
        {
            Db = new TenantDbFixture();

            var actor = new FakeCurrentUserAccessor(roleKey: ErpRoles.Admin);

            var memberRepo = new MemberRepository(Db.Context);
            var productRepo = new ProductRepository(Db.Context);
            var inventoryRepo = new InventoryRepository(Db.Context);
            var paymentRepo = new PaymentRepository(Db.Context);
            var saleRepo = new SaleRepository(Db.Context);
            var supplierRepo = new GenericRepository<Supplier>(Db.Context);
            var subscriptionRepo = new SubscriptionRepository(Db.Context);

            var audit = new TenantAuditService(Db.Context, actor);

            Members = new MemberService(memberRepo, paymentRepo, subscriptionRepo);
            Inventory = new InventoryService(
                inventoryRepo, productRepo, supplierRepo, Db.Context, actor, audit, Db.Finance);
            Products = new ProductService(productRepo, Inventory, Db.Context);
            Suppliers = new SupplierService(supplierRepo, Db.Context);
            Sales = new SaleService(
                saleRepo, memberRepo, productRepo, paymentRepo, Db.Context, actor, audit, Db.Finance);
        }

        public void Dispose() => Db.Dispose();

        public async Task<Supplier> SupplierAsync(string code = "SUP-1")
        {
            var supplier = new Supplier
            {
                SupplierCode = code,
                SupplierName = "Test Supplier",
                IsActive = true
            };

            Db.Context.Suppliers.Add(supplier);
            await Db.Context.SaveChangesAsync();
            return supplier;
        }
    }

    // ------------------------------------------------------------------ products

    /// <summary>
    /// A product that has been stocked carries a movement history, and that history is what
    /// explains the inventory figure on the balance sheet. Deleting the product must not be
    /// allowed to take it: the asset would still be in the ledger with nothing left to justify
    /// it, and no report could ever reconcile again.
    /// </summary>
    [Fact]
    public async Task A_product_with_stock_movements_cannot_be_deleted()
    {
        using var h = new Harness();

        var supplier = await h.SupplierAsync();

        var product = await h.Products.CreateProductAsync(
            "PRD-1", "Protein", "Supplements", costPrice: 100m, unitPrice: 150m,
            openingStock: 0m, reorderLevel: 5m);

        await h.Inventory.StockInAsync(
            product.ProductId, quantity: 10m, supplierId: supplier.SupplierId,
            reference: "PO-1", notes: "", unitCost: 100m);

        var movementsBefore = await h.Db.Context.StockMovements
            .CountAsync(m => m.ProductId == product.ProductId);

        Assert.True(movementsBefore > 0, "the stock-in should have recorded a movement");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Products.DeleteProductAsync(product.ProductId));

        Assert.Contains("stock", refused.Message, StringComparison.OrdinalIgnoreCase);

        // The point of the guard: the ledger of movements is still there.
        var movementsAfter = await h.Db.Context.StockMovements
            .CountAsync(m => m.ProductId == product.ProductId);

        Assert.Equal(movementsBefore, movementsAfter);
        Assert.NotNull(await h.Db.Context.Products.FindAsync(product.ProductId));
    }

    /// <summary>
    /// A product nothing has ever happened to is genuinely disposable - there is no history to
    /// protect. Without this, the guard above would be indistinguishable from "products can
    /// never be deleted", which is a different and worse rule.
    /// </summary>
    [Fact]
    public async Task A_product_with_no_history_can_still_be_deleted()
    {
        using var h = new Harness();

        var product = await h.Products.CreateProductAsync(
            "PRD-2", "Unused", "Other", costPrice: 10m, unitPrice: 20m,
            openingStock: 0m, reorderLevel: 0m);

        var deleted = await h.Products.DeleteProductAsync(product.ProductId);

        Assert.True(deleted);
        Assert.Null(await h.Db.Context.Products.FindAsync(product.ProductId));
    }

    [Fact]
    public async Task A_product_that_has_been_sold_cannot_be_deleted()
    {
        using var h = new Harness();

        var member = await h.Members.CreateMemberAsync("Buy", "Er", "555", "b@example.com");

        var product = await h.Products.CreateProductAsync(
            "PRD-3", "Shaker", "Accessories", costPrice: 50m, unitPrice: 80m,
            openingStock: 5m, reorderLevel: 1m);

        await h.Sales.CreateSaleAsync(
            member.MemberId,
            Lines(product.ProductId, 1), discount: 0m, notes: "", settleNow: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Products.DeleteProductAsync(product.ProductId));
    }

    // ------------------------------------------------------------------ suppliers

    /// <summary>
    /// A supplier that has supplied something is part of the purchase record. Refusing the
    /// delete in the service means the operator is told why in a sentence; without it the
    /// foreign key still refuses, but as a database error that reaches the desktop as
    /// "server problem" - true, useless, and indistinguishable from an outage.
    /// </summary>
    [Fact]
    public async Task A_supplier_with_history_is_refused_with_a_business_message()
    {
        using var h = new Harness();

        var supplier = await h.SupplierAsync("SUP-HIST");

        var product = await h.Products.CreateProductAsync(
            "PRD-4", "Bar", "Supplements", costPrice: 30m, unitPrice: 60m,
            openingStock: 0m, reorderLevel: 0m);

        await h.Inventory.StockInAsync(
            product.ProductId, quantity: 5m, supplierId: supplier.SupplierId,
            reference: "PO-2", notes: "", unitCost: 30m);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Suppliers.DeleteSupplierAsync(supplier.SupplierId));

        Assert.Contains("cannot be deleted", refused.Message, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(await h.Db.Context.Suppliers.FindAsync(supplier.SupplierId));
    }

    [Fact]
    public async Task A_supplier_never_used_can_be_deleted()
    {
        using var h = new Harness();

        var supplier = await h.SupplierAsync("SUP-NEW");

        Assert.True(await h.Suppliers.DeleteSupplierAsync(supplier.SupplierId));
        Assert.Null(await h.Db.Context.Suppliers.FindAsync(supplier.SupplierId));
    }

    // ------------------------------------------------------------------ the stock invariant

    /// <summary>
    /// The invariant the whole inventory module rests on:
    ///
    ///     opening + in - out ± adjustments = quantity on hand
    ///
    /// Asserted here through the movement ledger rather than by re-running the arithmetic the
    /// service used, so the two have to agree. Every movement also carries the balance before
    /// and after it, and those have to form an unbroken chain - a gap means a movement was
    /// written without the balance it produced, and the ledger would no longer explain the
    /// stock figure.
    /// </summary>
    [Fact]
    public async Task Stock_on_hand_is_always_explained_by_the_movement_ledger()
    {
        using var h = new Harness();

        var supplier = await h.SupplierAsync("SUP-CHAIN");
        var member = await h.Members.CreateMemberAsync("Chain", "Tester", "555", "c@example.com");

        var product = await h.Products.CreateProductAsync(
            "PRD-5", "Creatine", "Supplements", costPrice: 100m, unitPrice: 180m,
            openingStock: 0m, reorderLevel: 2m);

        await h.Inventory.StockInAsync(product.ProductId, 10m, supplier.SupplierId, "PO-3", "", unitCost: 100m);
        await h.Inventory.StockInAsync(product.ProductId, 10m, supplier.SupplierId, "PO-4", "", unitCost: 200m);

        await h.Sales.CreateSaleAsync(
            member.MemberId,
            Lines(product.ProductId, 4), discount: 0m, notes: "", settleNow: true);

        await h.Inventory.AdjustAsync(product.ProductId, newQuantity: 15m, notes: "Stock count");

        var movements = await h.Db.Context.StockMovements
            .AsNoTracking()
            .Where(m => m.ProductId == product.ProductId)
            .OrderBy(m => m.StockMovementId)
            .ToListAsync();

        Assert.NotEmpty(movements);

        // The chain: each movement starts where the previous one ended.
        for (var i = 1; i < movements.Count; i++)
        {
            Assert.Equal(movements[i - 1].BalanceAfter, movements[i].BalanceBefore);
        }

        var inventory = await h.Db.Context.Inventories
            .AsNoTracking()
            .FirstAsync(i => i.ProductId == product.ProductId);

        // The last movement's closing balance is the stock on hand.
        Assert.Equal(movements[^1].BalanceAfter, inventory.QuantityOnHand);
        Assert.Equal(15m, inventory.QuantityOnHand);
    }

    /// <summary>
    /// Weighted average, proved with the textbook case: ten at 100 and ten at 200 average to
    /// 150, and selling from that pool costs 150 a unit whatever it originally cost. Buying
    /// again afterwards must not reach back and change what the earlier sale cost - the cost is
    /// frozen onto the sale line when the goods go out, which is what keeps a restated purchase
    /// price from silently rewriting last month's gross profit.
    /// </summary>
    [Fact]
    public async Task Weighted_average_cost_is_frozen_onto_a_sale_and_later_purchases_do_not_move_it()
    {
        using var h = new Harness();

        var supplier = await h.SupplierAsync("SUP-WAC");
        var member = await h.Members.CreateMemberAsync("Wac", "Tester", "555", "w@example.com");

        var product = await h.Products.CreateProductAsync(
            "PRD-6", "Whey", "Supplements", costPrice: 100m, unitPrice: 300m,
            openingStock: 0m, reorderLevel: 0m);

        await h.Inventory.StockInAsync(product.ProductId, 10m, supplier.SupplierId, "PO-5", "", unitCost: 100m);
        await h.Inventory.StockInAsync(product.ProductId, 10m, supplier.SupplierId, "PO-6", "", unitCost: 200m);

        var afterTwo = await h.Db.Context.Inventories
            .AsNoTracking().FirstAsync(i => i.ProductId == product.ProductId);

        // (10 x 100 + 10 x 200) / 20
        Assert.Equal(150m, afterTwo.AverageCost);

        var sale = await h.Sales.CreateSaleAsync(
            member.MemberId,
            Lines(product.ProductId, 5), discount: 0m, notes: "", settleNow: true);

        var line = await h.Db.Context.SaleItems
            .AsNoTracking().FirstAsync(i => i.SaleId == sale.SaleId);

        var costAtSale = line.UnitCost;
        Assert.Equal(150m, costAtSale);

        // A later, dearer purchase moves the average for what is still on the shelf...
        await h.Inventory.StockInAsync(product.ProductId, 15m, supplier.SupplierId, "PO-7", "", unitCost: 400m);

        var afterThree = await h.Db.Context.Inventories
            .AsNoTracking().FirstAsync(i => i.ProductId == product.ProductId);

        Assert.True(afterThree.AverageCost > 150m,
            "a dearer purchase should raise the weighted average of the remaining stock");

        // ...but it must not reach back into what the earlier sale cost.
        var lineAgain = await h.Db.Context.SaleItems
            .AsNoTracking().FirstAsync(i => i.SaleId == sale.SaleId);

        Assert.Equal(costAtSale, lineAgain.UnitCost);
    }

    // ------------------------------------------------------------------ the integrity sweep

    /// <summary>
    /// A database built entirely through the services has nothing wrong with it. This is the
    /// result the sweep will give almost every time, so it is the one worth pinning down: if a
    /// clean tenant ever reports a finding, the check is wrong rather than the data.
    /// </summary>
    [Fact]
    public async Task The_integrity_sweep_reports_nothing_on_a_database_built_through_the_services()
    {
        using var h = new Harness();

        var supplier = await h.SupplierAsync("SUP-OK");
        var member = await h.Members.CreateMemberAsync("Clean", "Books", "555", "cb@example.com");

        var product = await h.Products.CreateProductAsync(
            "PRD-OK", "Protein", "Supplements", costPrice: 100m, unitPrice: 200m,
            openingStock: 0m, reorderLevel: 2m);

        await h.Inventory.StockInAsync(
            product.ProductId, 20m, supplier.SupplierId, "PO-OK", "", unitCost: 100m);

        await h.Sales.CreateSaleAsync(
            member.MemberId, Lines(product.ProductId, 3),
            discount: 0m, notes: "", settleNow: true);

        var integrity = new DataIntegrityService(h.Db.Context);
        var report = await integrity.RunAsync();

        Assert.True(report.ChecksRun > 0, "the sweep should actually run some checks");

        // Named individually so a failure says which invariant broke rather than only a count.
        var failed = report.Checks.Where(c => !c.Passed).Select(c => $"{c.Key} ({c.IssueCount})");

        Assert.True(report.IsClean,
            "expected a clean sweep, but these checks found issues: " + string.Join(", ", failed));

        Assert.Equal(report.ChecksRun, report.ChecksPassed);
        Assert.Equal(0, report.CriticalCount);
    }

    /// <summary>
    /// The check that matters most, proved by breaking it. Stock on hand and the movement
    /// ledger are two columns in two tables and nothing in the schema ties them together, so
    /// this is exactly the class of drift a foreign key cannot catch.
    ///
    /// The write below goes around the service deliberately - that is the only way this state
    /// can arise, and the sweep exists to find it when it does.
    /// </summary>
    [Fact]
    public async Task The_integrity_sweep_catches_stock_that_disagrees_with_its_movement_ledger()
    {
        using var h = new Harness();

        var supplier = await h.SupplierAsync("SUP-DRIFT");

        var product = await h.Products.CreateProductAsync(
            "PRD-DRIFT", "Drifty", "Supplements", costPrice: 10m, unitPrice: 20m,
            openingStock: 0m, reorderLevel: 0m);

        await h.Inventory.StockInAsync(
            product.ProductId, 10m, supplier.SupplierId, "PO-DRIFT", "", unitCost: 10m);

        var inventory = await h.Db.Context.Inventories
            .FirstAsync(i => i.ProductId == product.ProductId);

        inventory.QuantityOnHand += 7m;          // nothing recorded this
        await h.Db.Context.SaveChangesAsync();

        var report = await new DataIntegrityService(h.Db.Context).RunAsync();

        var check = Assert.Single(report.Checks, c => c.Key == "stock.ledger");

        Assert.False(check.Passed);
        Assert.Equal(1, check.IssueCount);
        Assert.Equal(IntegritySeverities.Critical, check.Severity);
        Assert.Contains(product.ProductId.ToString(), check.Examples);

        Assert.False(report.IsClean);
        Assert.True(report.CriticalCount > 0);
    }

    /// <summary>
    /// Double entry, asserted as a property of the stored data rather than of the code path
    /// that wrote it. An entry whose debits and credits differ puts the trial balance out by
    /// its own error, and every statement is derived from the trial balance.
    /// </summary>
    [Fact]
    public async Task The_integrity_sweep_catches_a_journal_entry_that_does_not_balance()
    {
        using var h = new Harness();

        var supplier = await h.SupplierAsync("SUP-JE");
        var member = await h.Members.CreateMemberAsync("Ledger", "Tester", "555", "lt@example.com");

        var product = await h.Products.CreateProductAsync(
            "PRD-JE", "Posted", "Supplements", costPrice: 40m, unitPrice: 90m,
            openingStock: 0m, reorderLevel: 0m);

        await h.Inventory.StockInAsync(
            product.ProductId, 5m, supplier.SupplierId, "PO-JE", "", unitCost: 40m);

        await h.Sales.CreateSaleAsync(
            member.MemberId, Lines(product.ProductId, 1),
            discount: 0m, notes: "", settleNow: true);

        var clean = await new DataIntegrityService(h.Db.Context).RunAsync();
        Assert.True(clean.Checks.Single(c => c.Key == "ledger.balanced").Passed);

        // Knock one line out of balance, again going around the service on purpose.
        var line = await h.Db.Context.JournalEntryLines
            .OrderBy(l => l.JournalEntryLineId)
            .FirstAsync(l => l.Debit > 0m);

        line.Debit += 5m;
        await h.Db.Context.SaveChangesAsync();

        var report = await new DataIntegrityService(h.Db.Context).RunAsync();

        var balanced = report.Checks.Single(c => c.Key == "ledger.balanced");

        Assert.False(balanced.Passed);
        Assert.Equal(IntegritySeverities.Critical, balanced.Severity);
        Assert.False(report.IsClean);
    }

    /// <summary>
    /// A tenant with no chart of accounts is a Micro or Small gym, which has no ledger by
    /// design. The finance checks must stay silent for them: reporting "0 entries balanced" as
    /// a finding would turn a tier boundary into a fault on every sweep they ever run.
    /// </summary>
    [Fact]
    public async Task The_finance_checks_do_not_run_for_a_tenant_with_no_ledger()
    {
        using var h = new Harness();

        // A tenant that has never touched Finance has no accounts at all.
        Assert.False(await h.Db.Context.Accounts.AnyAsync());

        var report = await new DataIntegrityService(h.Db.Context).RunAsync();

        Assert.True(report.IsClean);

        // The ledger *arithmetic* is what is skipped: with no accounts there are no entries to
        // balance, and reporting "0 of 0 balanced" as a result would be noise on every sweep a
        // Micro tenant ever runs.
        Assert.DoesNotContain(report.Checks, c => c.Key == "ledger.balanced");
        Assert.DoesNotContain(report.Checks, c => c.Key == "ledger.header");
        Assert.DoesNotContain(report.Checks, c => c.Key.StartsWith("ledger.unposted"));

        // The journal-line orphan checks still run, and should. They are cheap, they are about
        // referential integrity rather than about accounting, and on a tenant with no ledger
        // the correct answer - nothing there, nothing orphaned - is worth establishing rather
        // than assuming.
        Assert.Contains(report.Checks, c => c.Key == "orphan.journalline.entry");

        // The checks that apply to every tier still ran.
        Assert.Contains(report.Checks, c => c.Area == "Inventory");
        Assert.Contains(report.Checks, c => c.Area == "Sales");
    }

    /// <summary>
    /// A deduction total that is right but unattributed is a Warning, not a Critical.
    ///
    /// The distinction is the whole point of the check: money that was correctly deducted but
    /// is not filed under a named heading is an explainability gap, and the net pay - and so
    /// the journal entry behind it - is untouched. Reporting that at the same severity as a
    /// payslip whose net does not follow from its own gross would make the severe case
    /// invisible among the harmless ones.
    /// </summary>
    [Fact]
    public async Task An_unattributed_deduction_is_a_warning_while_a_broken_net_is_critical()
    {
        using var h = new Harness();

        // A run whose total is right but sits in no named column - the shape of a row written
        // before the statutory breakdown columns existed.
        h.Db.Context.Employees.Add(new Employee
        {
            EmployeeCode = "EMP-LEGACY",
            FirstName = "Legacy",
            LastName = "Row",
            Position = EmployeePositions.Staff,
            HireDate = new DateTime(2026, 1, 1)
        });
        await h.Db.Context.SaveChangesAsync();

        var employeeId = h.Db.Context.Employees.Single().EmployeeId;

        h.Db.Context.Payrolls.Add(new Payroll
        {
            EmployeeId = employeeId,
            PeriodStart = new DateTime(2026, 3, 1),
            PeriodEnd = new DateTime(2026, 3, 15),
            GrossPay = 10_000m,
            Deductions = 200m,          // taken...
            OtherDeductions = 0m,       // ...but filed nowhere
            NetPay = 9_800m,            // and the net still follows from the gross
            Status = "Paid"
        });
        await h.Db.Context.SaveChangesAsync();

        var report = await new DataIntegrityService(h.Db.Context).RunAsync();

        var parts = report.Checks.Single(c => c.Key == "payroll.deductions");
        var net = report.Checks.Single(c => c.Key == "payroll.net");

        Assert.False(parts.Passed);
        Assert.Equal(IntegritySeverities.Warning, parts.Severity);

        // The severe check is untouched: the net is right, so nothing contradicts itself.
        Assert.True(net.Passed);

        // And the sweep as a whole is not reported as critical for this alone.
        Assert.Equal(0, report.CriticalCount);
        Assert.False(report.IsClean);
    }

    // ------------------------------------------------------------------ concurrency

    /// <summary>
    /// Two people selling the last of the stock at the same moment.
    ///
    /// Both requests read the same quantity on hand, both find it sufficient, and both write
    /// back what they calculated. Under read-committed - which is what an ordinary transaction
    /// gets - nothing in the database stops the second write from overwriting the first, and
    /// the gym sells stock it does not have. The stock ledger records it too: two movements
    /// that both claim to have started from the same balance, which breaks the chain that is
    /// supposed to explain the quantity on hand.
    ///
    /// The fix is a concurrency token on the quantity itself, so the second update carries
    /// "...and the quantity is still what I read" and affects no rows when it is not. The
    /// second caller is then refused rather than silently winning.
    /// </summary>
    [Fact]
    public async Task Two_concurrent_writers_cannot_both_take_the_same_stock()
    {
        using var h = new Harness();

        var supplier = await h.SupplierAsync("SUP-RACE");

        var product = await h.Products.CreateProductAsync(
            "PRD-RACE", "Last One", "Supplements", costPrice: 10m, unitPrice: 30m,
            openingStock: 0m, reorderLevel: 0m);

        await h.Inventory.StockInAsync(
            product.ProductId, 5m, supplier.SupplierId, "PO-RACE", "", unitCost: 10m);

        // Two callers, each with their own context, exactly as two HTTP requests would be.
        await using var first = h.Db.NewContext();
        await using var second = h.Db.NewContext();

        var a = await first.Inventories.FirstAsync(i => i.ProductId == product.ProductId);
        var b = await second.Inventories.FirstAsync(i => i.ProductId == product.ProductId);

        Assert.Equal(5m, a.QuantityOnHand);
        Assert.Equal(5m, b.QuantityOnHand);   // both saw the same stock

        // The first sale of 3 succeeds and leaves 2.
        a.QuantityOnHand -= 3m;
        await first.SaveChangesAsync();

        // The second, still believing there are 5, tries to take another 3. It must not be
        // allowed to write 2 over the 2 that is already there and call six units sold from five.
        b.QuantityOnHand -= 3m;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        // The database still reflects only the sale that actually went through.
        await using var check = h.Db.NewContext();
        var settled = await check.Inventories.FirstAsync(i => i.ProductId == product.ProductId);

        Assert.Equal(2m, settled.QuantityOnHand);
    }
}
