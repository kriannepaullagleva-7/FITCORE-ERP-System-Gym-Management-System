using ERP_domain.entities;
using ERP_infrastructure.data;
using ERP_infrastructure.repositories;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class ProductService : IProductService
    {
        /// <summary>
        /// The categories offered in the UI. Anything else is accepted and stored as typed, so
        /// a gym with an unusual catalogue is not boxed in.
        /// </summary>
        public static readonly string[] KnownCategories =
        {
            "Supplements", "Beverages", "Food", "Apparel", "Equipment", "Accessories", "Other"
        };

        private readonly IProductRepository _repository;
        private readonly IInventoryService _inventoryService;
        private readonly TenantErpDbContext _context;

        public ProductService(
            IProductRepository repository,
            IInventoryService inventoryService,
            TenantErpDbContext context)
        {
            _repository = repository;
            _inventoryService = inventoryService;
            _context = context;
        }

        public async Task<List<Product>> GetAllProductsAsync()
        {
            return await _repository.GetAllAsync();
        }

        public async Task<List<Product>> GetActiveProductsAsync()
        {
            return await _repository.GetActiveProductsAsync();
        }

        public async Task<Product?> GetProductByIdAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }

        public async Task<List<string>> GetCategoriesAsync()
        {
            var inUse = await _context.Products
                .AsNoTracking()
                .Select(p => p.Category)
                .Distinct()
                .ToListAsync();

            return KnownCategories
                .Union(inUse.Where(c => !string.IsNullOrWhiteSpace(c)), StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
        }

        public async Task<Product> CreateProductAsync(
            string productCode,
            string productName,
            string category,
            decimal costPrice,
            decimal unitPrice,
            decimal openingStock,
            decimal reorderLevel)
        {
            productCode = (productCode ?? string.Empty).Trim();
            productName = (productName ?? string.Empty).Trim();

            Validate(productCode, productName, costPrice, unitPrice);

            if (openingStock < 0)
                throw new InvalidOperationException("Opening stock cannot be negative.");
            if (reorderLevel < 0)
                throw new InvalidOperationException("Reorder level cannot be negative.");

            if (await _repository.CodeExistsAsync(productCode))
                throw new InvalidOperationException($"Product code '{productCode}' is already in use.");

            var product = new Product
            {
                ProductCode = productCode,
                ProductName = productName,
                Category = NormaliseCategory(category),
                CostPrice = costPrice,
                UnitPrice = unitPrice,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _repository.AddAsync(product);

            // A product is only usable once it has a stock record behind it.
            await _inventoryService.EnsureInventoryAsync(product.ProductId, openingStock, reorderLevel);

            return product;
        }

        public async Task<Product?> UpdateProductAsync(
            int id,
            string productCode,
            string productName,
            string category,
            decimal costPrice,
            decimal unitPrice,
            bool isActive)
        {
            productCode = (productCode ?? string.Empty).Trim();
            productName = (productName ?? string.Empty).Trim();

            Validate(productCode, productName, costPrice, unitPrice);

            var product = await _repository.GetByIdAsync(id);
            if (product == null) return null;

            if (await _repository.CodeExistsAsync(productCode, id))
                throw new InvalidOperationException($"Product code '{productCode}' is already in use.");

            product.ProductCode = productCode;
            product.ProductName = productName;
            product.Category = NormaliseCategory(category);
            product.CostPrice = costPrice;
            product.UnitPrice = unitPrice;
            product.IsActive = isActive;

            return await _repository.UpdateAsync(product);
        }

        public async Task<bool> DeleteProductAsync(int id)
        {
            var product = await _repository.GetByIdAsync(id);
            if (product == null) return false;

            // Sale history must stay intact, so a sold product is retired instead of removed.
            if (await _repository.IsReferencedBySalesAsync(id))
                throw new InvalidOperationException(
                    "This product appears on one or more sales and cannot be deleted. Mark it inactive instead.");

            // Stock history must stay intact for a different and sharper reason. Inventory and
            // StockMovement both cascade from Product, so deleting a product does not fail - it
            // silently takes the movement ledger with it. That ledger is what explains the
            // inventory figure on the balance sheet: a received purchase posts an asset, and if
            // the movements behind it vanish, the asset is still in the books with nothing left
            // to justify it and no report can reconcile again.
            //
            // The foreign keys cannot be relied on to stop this, because they are the thing
            // doing it. So the guard lives here.
            if (await _context.StockMovements.AnyAsync(m => m.ProductId == id))
                throw new InvalidOperationException(
                    "This product has stock movements on record and cannot be deleted, because " +
                    "removing it would take the stock history that explains the inventory value " +
                    "with it. Mark it inactive instead.");

            // Purchases and returns are restricted by their own foreign keys, so the database
            // would refuse these anyway - but it would do it as a provider error that reaches
            // the desktop as "server problem". Saying it here makes the refusal a sentence the
            // operator can act on.
            if (await _context.PurchaseItems.AnyAsync(i => i.ProductId == id))
                throw new InvalidOperationException(
                    "This product appears on one or more purchase orders and cannot be deleted. " +
                    "Mark it inactive instead.");

            if (await _context.SaleReturnItems.AnyAsync(i => i.ProductId == id))
                throw new InvalidOperationException(
                    "This product appears on one or more returns and cannot be deleted. " +
                    "Mark it inactive instead.");

            return await _repository.DeleteAsync(id);
        }

        private static void Validate(string productCode, string productName, decimal costPrice, decimal unitPrice)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                throw new InvalidOperationException("Product code is required.");
            if (string.IsNullOrWhiteSpace(productName))
                throw new InvalidOperationException("Product name is required.");
            if (costPrice < 0)
                throw new InvalidOperationException("Cost price cannot be negative.");
            if (unitPrice < 0)
                throw new InvalidOperationException("Selling price cannot be negative.");
        }

        private static string NormaliseCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return "Other";

            var trimmed = category.Trim();

            // Match a known category regardless of casing, so "apparel" files under "Apparel".
            var known = KnownCategories.FirstOrDefault(
                c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase));

            return known ?? trimmed;
        }
    }
}
