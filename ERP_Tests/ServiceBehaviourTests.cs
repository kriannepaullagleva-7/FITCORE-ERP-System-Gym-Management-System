using ERP_domain.entities;
using ERP_infrastructure.repositories;
using ERP_infrastructure.services;
using Xunit;

namespace ERP_Tests;

/// <summary>
/// Covers the business rules that the API relies on for its 400 and 409 responses. These run
/// against a real relational database so the repository queries are genuinely executed.
/// </summary>
public class ServiceBehaviourTests
{
    private static (IMemberService Members, TenantDbFixture Db) CreateMemberService()
    {
        var db = new TenantDbFixture();
        var memberRepo = new MemberRepository(db.Context);
        var paymentRepo = new PaymentRepository(db.Context);
        return (new MemberService(memberRepo, paymentRepo), db);
    }

    [Fact]
    public async Task A_member_with_no_history_can_be_deleted()
    {
        var (members, db) = CreateMemberService();
        using var _ = db;

        var member = await members.CreateMemberAsync("Ada", "Lovelace", "555", "ada@example.com");

        var result = await members.DeleteMemberAsync(member.MemberId);

        Assert.Equal(MemberDeleteResult.Deleted, result);
        Assert.Null(await members.GetMemberByIdAsync(member.MemberId));
    }

    [Fact]
    public async Task Deleting_an_unknown_member_reports_not_found()
    {
        var (members, db) = CreateMemberService();
        using var _ = db;

        Assert.Equal(MemberDeleteResult.NotFound, await members.DeleteMemberAsync(4242));
    }

    [Fact]
    public async Task A_member_with_a_payment_is_refused_deletion()
    {
        var (members, db) = CreateMemberService();
        using var _ = db;

        var member = await members.CreateMemberAsync("Grace", "Hopper", "555", "grace@example.com");

        db.Context.Payments.Add(new Payment
        {
            MemberId = member.MemberId,
            Amount = 100m,
            Method = "Cash",
            Status = "Completed"
        });
        await db.Context.SaveChangesAsync();

        // This is what produces the 409 the API returns, rather than destroying financial history.
        Assert.Equal(MemberDeleteResult.HasHistory, await members.DeleteMemberAsync(member.MemberId));
    }

    [Fact]
    public async Task Searching_matches_name_email_and_phone()
    {
        var (members, db) = CreateMemberService();
        using var _ = db;

        await members.CreateMemberAsync("Ada", "Lovelace", "555-0001", "ada@example.com");
        await members.CreateMemberAsync("Alan", "Turing", "555-0002", "alan@example.com");

        Assert.Single(await members.SearchMembersAsync("Lovelace"));
        Assert.Single(await members.SearchMembersAsync("alan@"));
        Assert.Single(await members.SearchMembersAsync("555-0002"));
        Assert.Equal(2, (await members.SearchMembersAsync("")).Count);
    }

    [Fact]
    public async Task A_duplicate_product_code_is_rejected()
    {
        using var db = new TenantDbFixture();
        var productRepo = new ProductRepository(db.Context);
        var inventoryRepo = new InventoryRepository(db.Context);
        var inventory = new InventoryService(inventoryRepo, productRepo, db.Context);
        var products = new ProductService(productRepo, inventory, db.Context);

        await products.CreateProductAsync("TWL-001", "Gym Towel", "Apparel", 150m, 250m, 10m, 5m);

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => products.CreateProductAsync("TWL-001", "Another Towel", "Apparel", 200m, 300m, 1m, 1m));

        Assert.Contains("TWL-001", failure.Message);
    }

    [Fact]
    public async Task Creating_a_product_opens_an_inventory_row_with_the_opening_stock()
    {
        using var db = new TenantDbFixture();
        var productRepo = new ProductRepository(db.Context);
        var inventoryRepo = new InventoryRepository(db.Context);
        var inventory = new InventoryService(inventoryRepo, productRepo, db.Context);
        var products = new ProductService(productRepo, inventory, db.Context);

        var product = await products.CreateProductAsync("SHK-001", "Shake", "Supplements", 120m, 180m, 20m, 5m);

        var row = await inventory.GetByProductIdAsync(product.ProductId);

        Assert.NotNull(row);
        Assert.Equal(20m, row!.QuantityOnHand);
        Assert.Equal(5m, row.ReorderLevel);
    }

    [Fact]
    public async Task Stock_cannot_be_taken_out_beyond_the_quantity_on_hand()
    {
        using var db = new TenantDbFixture();
        var productRepo = new ProductRepository(db.Context);
        var inventoryRepo = new InventoryRepository(db.Context);
        var inventory = new InventoryService(inventoryRepo, productRepo, db.Context);
        var products = new ProductService(productRepo, inventory, db.Context);

        var product = await products.CreateProductAsync("BTL-001", "Bottle", "Accessories", 60m, 100m, 3m, 1m);

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => inventory.StockOutAsync(product.ProductId, 10m, "REF", "too many"));

        Assert.Contains("stock", failure.Message, StringComparison.OrdinalIgnoreCase);

        var row = await inventory.GetByProductIdAsync(product.ProductId);
        Assert.Equal(3m, row!.QuantityOnHand);
    }

    [Fact]
    public async Task Stock_in_then_stock_out_returns_to_the_original_balance()
    {
        using var db = new TenantDbFixture();
        var productRepo = new ProductRepository(db.Context);
        var inventoryRepo = new InventoryRepository(db.Context);
        var inventory = new InventoryService(inventoryRepo, productRepo, db.Context);
        var products = new ProductService(productRepo, inventory, db.Context);

        var product = await products.CreateProductAsync("MAT-001", "Mat", "Equipment", 320m, 500m, 4m, 2m);

        await inventory.StockInAsync(product.ProductId, 6m, "PO-1", "restock");
        var afterIn = await inventory.GetByProductIdAsync(product.ProductId);
        Assert.Equal(10m, afterIn!.QuantityOnHand);

        await inventory.StockOutAsync(product.ProductId, 6m, "SO-1", "sold");
        var afterOut = await inventory.GetByProductIdAsync(product.ProductId);
        Assert.Equal(4m, afterOut!.QuantityOnHand);

        // Every movement is recorded, including the ones that cancel out.
        var movements = await inventory.GetMovementsAsync(product.ProductId);
        Assert.True(movements.Count >= 2);
    }
}
