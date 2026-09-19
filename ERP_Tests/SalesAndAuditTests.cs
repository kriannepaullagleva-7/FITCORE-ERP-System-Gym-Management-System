using ERP_domain.entities;
using ERP_infrastructure.repositories;
using ERP_infrastructure.services;
using ERP_UI.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP_Tests;

/// <summary>
/// Covers the money and stock rules added alongside the Sales-to-Payment link: how a sale's
/// payment status is derived, that cancelling one puts the stock back, and that the audit
/// timestamp is stamped centrally rather than by each service.
///
/// These run against a real relational database, so the transactions, foreign keys and
/// generated SQL are all genuinely exercised.
/// </summary>
public class SalesAndAuditTests
{
    private sealed class Harness : IDisposable
    {
        public TenantDbFixture Db { get; }
        public ISaleService Sales { get; }
        public IPaymentService Payments { get; }
        public IProductService Products { get; }
        public IInventoryService Inventory { get; }
        public IMemberService Members { get; }

        public Harness()
        {
            Db = new TenantDbFixture();

            var memberRepo = new MemberRepository(Db.Context);
            var productRepo = new ProductRepository(Db.Context);
            var inventoryRepo = new InventoryRepository(Db.Context);
            var paymentRepo = new PaymentRepository(Db.Context);
            var saleRepo = new SaleRepository(Db.Context);
            var subscriptionRepo = new SubscriptionRepository(Db.Context);

            Members = new MemberService(memberRepo, paymentRepo);
            Inventory = new InventoryService(inventoryRepo, productRepo, Db.Context);
            Products = new ProductService(productRepo, Inventory, Db.Context);
            Payments = new PaymentService(paymentRepo, subscriptionRepo, memberRepo, Db.Context);
            Sales = new SaleService(saleRepo, memberRepo, productRepo, paymentRepo, Db.Context);
        }

        public void Dispose() => Db.Dispose();
    }

    private static async Task<(int MemberId, int ProductId)> SeedAsync(Harness h, decimal stock = 20m)
    {
        var member = await h.Members.CreateMemberAsync("Test", "Buyer", "555-0100", "buyer@example.com");
        var product = await h.Products.CreateProductAsync(
            "SKU-1", "Protein Bar", "Supplements", 40m, 100m, stock, 5m);

        return (member.MemberId, product.ProductId);
    }

    // ------------------------------------------------------------------ Payment status

    [Theory]
    [InlineData("Completed", 100, 0, "Unpaid")]
    [InlineData("Completed", 100, 40, "Partially Paid")]
    [InlineData("Completed", 100, 100, "Paid")]
    // Overpayment still reads as Paid rather than something stranger.
    [InlineData("Completed", 100, 120, "Paid")]
    // A cancelled sale is not a debt, whatever was paid against it.
    [InlineData("Cancelled", 100, 0, "Cancelled")]
    [InlineData("Cancelled", 100, 100, "Cancelled")]
    public void Sale_payment_status_is_derived_from_what_was_actually_paid(
        string saleStatus, decimal total, decimal paid, string expected)
    {
        Assert.Equal(expected, SaleService.ResolvePaymentStatus(saleStatus, total, paid));
    }

    [Fact]
    public async Task A_part_payment_moves_a_sale_to_partially_paid_then_paid()
    {
        using var h = new Harness();
        var (memberId, productId) = await SeedAsync(h);

        var sale = await h.Sales.CreateSaleAsync(
            memberId, new List<SaleLineRequest> { new() { ProductId = productId, Quantity = 3 } });

        // 3 x 100 with no discount.
        Assert.Equal(300m, sale.TotalAmount);

        var unpaid = await h.Sales.GetSaleViewAsync(sale.SaleId);
        Assert.Equal("Unpaid", unpaid!.PaymentStatus);
        Assert.Equal(300m, unpaid.Balance);

        await h.Payments.RecordPaymentAsync(
            memberId, null, sale.SaleId, 120m, DateTime.UtcNow, "Cash", "", "Completed", "");

        var partial = await h.Sales.GetSaleViewAsync(sale.SaleId);
        Assert.Equal("Partially Paid", partial!.PaymentStatus);
        Assert.Equal(180m, partial.Balance);

        await h.Payments.RecordPaymentAsync(
            memberId, null, sale.SaleId, 180m, DateTime.UtcNow, "Cash", "", "Completed", "");

        var settled = await h.Sales.GetSaleViewAsync(sale.SaleId);
        Assert.Equal("Paid", settled!.PaymentStatus);
        Assert.Equal(0m, settled.Balance);
    }

    [Fact]
    public async Task A_payment_cannot_exceed_what_the_sale_still_owes()
    {
        using var h = new Harness();
        var (memberId, productId) = await SeedAsync(h);

        var sale = await h.Sales.CreateSaleAsync(
            memberId, new List<SaleLineRequest> { new() { ProductId = productId, Quantity = 1 } });

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => h.Payments.RecordPaymentAsync(
            memberId, null, sale.SaleId, 150m, DateTime.UtcNow, "Cash", "", "Completed", ""));

        Assert.Contains("outstanding balance", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ Stock

    [Fact]
    public async Task Completing_a_sale_deducts_stock_and_records_the_movement()
    {
        using var h = new Harness();
        var (memberId, productId) = await SeedAsync(h, stock: 20m);

        await h.Sales.CreateSaleAsync(
            memberId, new List<SaleLineRequest> { new() { ProductId = productId, Quantity = 3 } });

        var stock = await h.Inventory.GetByProductIdAsync(productId);
        Assert.Equal(17m, stock!.QuantityOnHand);

        var movement = (await h.Inventory.GetMovementsAsync(productId))
            .First(m => m.MovementType == "Sale");

        // The ledger has to explain the whole of the change, not just where it landed.
        Assert.Equal(3m, movement.Quantity);
        Assert.Equal(20m, movement.BalanceBefore);
        Assert.Equal(17m, movement.BalanceAfter);
    }

    [Fact]
    public async Task A_sale_cannot_be_created_for_more_stock_than_exists()
    {
        using var h = new Harness();
        var (memberId, productId) = await SeedAsync(h, stock: 2m);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => h.Sales.CreateSaleAsync(
            memberId, new List<SaleLineRequest> { new() { ProductId = productId, Quantity = 5 } }));

        Assert.Contains("stock", failure.Message, StringComparison.OrdinalIgnoreCase);

        // The failed sale must leave nothing behind.
        var stock = await h.Inventory.GetByProductIdAsync(productId);
        Assert.Equal(2m, stock!.QuantityOnHand);
        Assert.Empty(await h.Sales.GetAllSalesAsync());
    }

    [Fact]
    public async Task Cancelling_a_sale_returns_its_stock_and_refunds_its_payments()
    {
        using var h = new Harness();
        var (memberId, productId) = await SeedAsync(h, stock: 20m);

        var sale = await h.Sales.CreateSaleAsync(
            memberId, new List<SaleLineRequest> { new() { ProductId = productId, Quantity = 4 } });

        await h.Payments.RecordPaymentAsync(
            memberId, null, sale.SaleId, 400m, DateTime.UtcNow, "Cash", "", "Completed", "");

        var cancelled = await h.Sales.CancelSaleAsync(sale.SaleId, "customer changed their mind");

        Assert.Equal("Cancelled", cancelled!.Status);

        var stock = await h.Inventory.GetByProductIdAsync(productId);
        Assert.Equal(20m, stock!.QuantityOnHand);

        // The money is reversed rather than deleted, so the ledger still shows it happened.
        var payments = await h.Payments.GetSalePaymentsAsync(sale.SaleId);
        Assert.All(payments, p => Assert.Equal("Refunded", p.Status));
    }

    [Fact]
    public async Task A_sale_with_payments_cannot_be_deleted()
    {
        using var h = new Harness();
        var (memberId, productId) = await SeedAsync(h);

        var sale = await h.Sales.CreateSaleAsync(
            memberId, new List<SaleLineRequest> { new() { ProductId = productId, Quantity = 1 } });

        await h.Payments.RecordPaymentAsync(
            memberId, null, sale.SaleId, 100m, DateTime.UtcNow, "Cash", "", "Completed", "");

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => h.Sales.DeleteSaleAsync(sale.SaleId));

        Assert.Contains("cancel", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_discount_cannot_exceed_the_sale_subtotal()
    {
        using var h = new Harness();
        var (memberId, productId) = await SeedAsync(h);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => h.Sales.CreateSaleAsync(
            memberId,
            new List<SaleLineRequest> { new() { ProductId = productId, Quantity = 1 } },
            discount: 500m));

        Assert.Contains("discount", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_sale_line_price_comes_from_the_product_not_the_caller()
    {
        using var h = new Harness();
        var (memberId, productId) = await SeedAsync(h);

        // The request carries only a product and a quantity, so there is no price to tamper
        // with; the line must be priced from the catalogue.
        var sale = await h.Sales.CreateSaleAsync(
            memberId, new List<SaleLineRequest> { new() { ProductId = productId, Quantity = 2 } });

        var lines = await h.Sales.GetSaleLinesAsync(sale.SaleId);

        Assert.Equal(100m, Assert.Single(lines).UnitPrice);
        Assert.Equal(200m, sale.Subtotal);
    }

    // ------------------------------------------------------------------ Audit stamping

    [Fact]
    public async Task UpdatedAt_is_null_until_a_record_is_changed()
    {
        using var h = new Harness();

        var member = await h.Members.CreateMemberAsync("Ada", "Lovelace", "555-1", "ada@example.com");

        var stored = await h.Db.Context.Members.AsNoTracking()
            .FirstAsync(m => m.MemberId == member.MemberId);

        Assert.Null(stored.UpdatedAt);
    }

    [Fact]
    public async Task Changing_a_record_stamps_UpdatedAt_and_leaves_CreatedAt_alone()
    {
        using var h = new Harness();

        var member = await h.Members.CreateMemberAsync("Ada", "Lovelace", "555-1", "ada@example.com");

        var createdAt = (await h.Db.Context.Members.AsNoTracking()
            .FirstAsync(m => m.MemberId == member.MemberId)).CreatedAt;

        await h.Members.UpdateMemberAsync(
            member.MemberId, "Ada", "Byron", "555-2", "ada@example.com", "Active");

        var updated = await h.Db.Context.Members.AsNoTracking()
            .FirstAsync(m => m.MemberId == member.MemberId);

        Assert.NotNull(updated.UpdatedAt);

        // CreatedAt records when the row was written and must survive every later edit.
        Assert.Equal(createdAt, updated.CreatedAt);
    }
}

/// <summary>
/// The sorting and paging the data tables share. This is pure logic with no database behind
/// it, but it decides which rows a user actually sees, so it is worth pinning down.
/// </summary>
public class TableStateTests
{
    private sealed record Row(int Id, string Name);

    private static TableState<Row> Build(int pageSize = 2) =>
        new TableState<Row>(defaultSort: "Id", pageSize: pageSize)
            .Column("Id", r => r.Id)
            .Column("Name", r => r.Name);

    private static readonly List<Row> Rows = new()
    {
        new(3, "Charlie"), new(1, "Alice"), new(4, "Dana"), new(2, "Bob"), new(5, "Eve")
    };

    [Fact]
    public void It_sorts_by_the_default_column_and_pages_the_result()
    {
        var table = Build();

        var page = table.Apply(Rows);

        Assert.Equal(new[] { 1, 2 }, page.Select(r => r.Id));
        Assert.Equal(5, table.TotalCount);
        Assert.Equal(3, table.TotalPages);
        Assert.Equal(1, table.FirstRowOnPage);
        Assert.Equal(2, table.LastRowOnPage);
    }

    [Fact]
    public void Toggling_the_same_column_reverses_the_direction()
    {
        var table = Build();

        table.ToggleSort("Id");

        Assert.True(table.Descending);
        Assert.Equal(new[] { 5, 4 }, table.Apply(Rows).Select(r => r.Id));

        table.ToggleSort("Id");

        Assert.False(table.Descending);
        Assert.Equal(new[] { 1, 2 }, table.Apply(Rows).Select(r => r.Id));
    }

    [Fact]
    public void Sorting_a_different_column_starts_ascending_and_resets_to_the_first_page()
    {
        var table = Build();
        table.GoToPage(3);

        table.ToggleSort("Name");

        Assert.Equal(1, table.Page);
        Assert.Equal(new[] { "Alice", "Bob" }, table.Apply(Rows).Select(r => r.Name));
    }

    [Fact]
    public void An_unsortable_column_is_ignored()
    {
        var table = Build();

        table.ToggleSort("Actions");

        Assert.Equal("Id", table.SortColumn);
        Assert.False(table.IsSortable("Actions"));
    }

    [Fact]
    public void A_filter_that_shrinks_the_list_cannot_strand_the_user_on_a_missing_page()
    {
        var table = Build();
        table.GoToPage(3);

        // The list drops to two rows, so page three no longer exists.
        var page = table.Apply(Rows.Take(2).ToList());

        Assert.Equal(1, table.Page);
        Assert.NotEmpty(page);
    }

    [Fact]
    public void An_empty_list_reports_one_page_and_no_rows()
    {
        var table = Build();

        var page = table.Apply(new List<Row>());

        Assert.Empty(page);
        Assert.Equal(0, table.TotalCount);
        Assert.Equal(1, table.TotalPages);
        Assert.Equal(0, table.FirstRowOnPage);
    }
}

/// <summary>
/// Payroll arithmetic. Gross and net are never accepted from a caller, so these pin down what
/// the server calculates from the inputs it is given.
/// </summary>
public class PayrollCalculationTests
{
    private sealed class Harness : IDisposable
    {
        public TenantDbFixture Db { get; }
        public IPayrollService Payroll { get; }
        public IEmployeeService Employees { get; }

        public Harness()
        {
            Db = new TenantDbFixture();

            var employeeRepo = new EmployeeRepository(Db.Context);
            var payrollRepo = new PayrollRepository(Db.Context);

            Employees = new EmployeeService(employeeRepo);
            Payroll = new PayrollService(payrollRepo, employeeRepo);
        }

        public void Dispose() => Db.Dispose();
    }

    private static Task<Employee> SeedEmployeeAsync(Harness h, decimal salary = 20000m) =>
        h.Employees.CreateEmployeeAsync(
            "EMP-1", "Maria", "Santos", "Front Desk", "Operations",
            "555-1", "maria@example.com", new DateTime(2026, 1, 15), salary);

    [Fact]
    public async Task Gross_is_basic_plus_allowances_plus_overtime_and_net_takes_off_deductions()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h);

        var run = await h.Payroll.CreatePayrollAsync(
            employee.EmployeeId,
            new DateTime(2026, 11, 1), new DateTime(2026, 11, 30),
            basicSalary: null,          // falls back to the employee's salary
            allowances: 1000m,
            deductions: 500m,
            overtimeHours: 10m,
            overtimeRate: 150m,
            notes: "");

        Assert.Equal(20000m, run.BasicSalary);
        Assert.Equal(1500m, run.OvertimePay);
        Assert.Equal(22500m, run.GrossPay);
        Assert.Equal(22000m, run.NetPay);
    }

    [Fact]
    public async Task Overtime_hours_without_a_rate_are_refused()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => h.Payroll.CreatePayrollAsync(
            employee.EmployeeId,
            new DateTime(2026, 11, 1), new DateTime(2026, 11, 30),
            null, 0m, 0m, overtimeHours: 8m, overtimeRate: 0m, notes: ""));

        Assert.Contains("overtime rate", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deductions_cannot_exceed_gross_pay()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h, salary: 1000m);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => h.Payroll.CreatePayrollAsync(
            employee.EmployeeId,
            new DateTime(2026, 11, 1), new DateTime(2026, 11, 30),
            null, 0m, deductions: 5000m, overtimeHours: 0m, overtimeRate: 0m, notes: ""));

        Assert.Contains("deductions", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_same_employee_and_period_cannot_be_run_twice()
    {
        using var h = new Harness();
        var employee = await SeedEmployeeAsync(h);

        var start = new DateTime(2026, 11, 1);
        var end = new DateTime(2026, 11, 30);

        await h.Payroll.CreatePayrollAsync(employee.EmployeeId, start, end, null, 0m, 0m, 0m, 0m, "");

        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            h.Payroll.CreatePayrollAsync(employee.EmployeeId, start, end, null, 0m, 0m, 0m, 0m, ""));

        Assert.Contains("already exists", failure.Message, StringComparison.OrdinalIgnoreCase);
    }
}
