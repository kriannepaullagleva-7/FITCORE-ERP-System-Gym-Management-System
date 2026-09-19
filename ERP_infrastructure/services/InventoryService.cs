using ERP_domain.entities;
using ERP_infrastructure.data;
using ERP_infrastructure.repositories;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class InventoryService : IInventoryService
    {
        private readonly IInventoryRepository _inventoryRepo;
        private readonly IProductRepository _productRepo;
        private readonly TenantErpDbContext _context;

        public InventoryService(
            IInventoryRepository inventoryRepo,
            IProductRepository productRepo,
            TenantErpDbContext context)
        {
            _inventoryRepo = inventoryRepo;
            _productRepo = productRepo;
            _context = context;
        }

        /// <summary>
        /// The single definition of low stock, used by the grid, the badges, the filters and
        /// the dashboard so they can never disagree.
        /// </summary>
        public static string ResolveStockStatus(decimal quantityOnHand, decimal reorderLevel)
        {
            if (quantityOnHand <= 0) return "Out of Stock";
            if (reorderLevel > 0 && quantityOnHand <= reorderLevel) return "Low Stock";
            return "In Stock";
        }

        private static InventoryView ToView(Inventory inventory)
        {
            var product = inventory.Product;
            return new InventoryView
            {
                InventoryId = inventory.InventoryId,
                ProductId = inventory.ProductId,
                ProductCode = product?.ProductCode ?? string.Empty,
                ProductName = product?.ProductName ?? $"Product #{inventory.ProductId}",
                Category = product?.Category ?? "Other",
                CostPrice = product?.CostPrice ?? 0m,
                UnitPrice = product?.UnitPrice ?? 0m,
                IsActive = product?.IsActive ?? false,
                QuantityOnHand = inventory.QuantityOnHand,
                ReorderLevel = inventory.ReorderLevel,
                StockStatus = ResolveStockStatus(inventory.QuantityOnHand, inventory.ReorderLevel),
                LastUpdatedAt = inventory.LastUpdatedAt
            };
        }

        public async Task<List<InventoryView>> GetInventoryAsync()
        {
            // Backfill first so a product added outside this service still shows a stock row.
            await BackfillMissingInventoryRowsAsync();

            var rows = await _inventoryRepo.GetAllWithProductAsync();
            return rows.Select(ToView).ToList();
        }

        public async Task<InventoryView?> GetByProductIdAsync(int productId)
        {
            var inventory = await _inventoryRepo.GetByProductIdAsync(productId);
            return inventory == null ? null : ToView(inventory);
        }

        public async Task<InventorySummary> GetSummaryAsync()
        {
            await BackfillMissingInventoryRowsAsync();

            // Aggregated in the database over the joined rows, so a large catalogue is never
            // pulled into memory just to be counted.
            var stats = await _context.Inventories
                .Select(i => new
                {
                    i.QuantityOnHand,
                    i.ReorderLevel,
                    IsActive = i.Product != null && i.Product.IsActive,
                    CostPrice = i.Product != null ? i.Product.CostPrice : 0m,
                    UnitPrice = i.Product != null ? i.Product.UnitPrice : 0m
                })
                .GroupBy(_ => 1)
                .Select(g => new InventorySummary
                {
                    TotalProducts = g.Count(),
                    ActiveProducts = g.Count(x => x.IsActive),
                    OutOfStock = g.Count(x => x.QuantityOnHand <= 0),
                    LowStock = g.Count(x =>
                        x.QuantityOnHand > 0 &&
                        x.ReorderLevel > 0 &&
                        x.QuantityOnHand <= x.ReorderLevel),
                    InStock = g.Count(x =>
                        x.QuantityOnHand > 0 &&
                        (x.ReorderLevel <= 0 || x.QuantityOnHand > x.ReorderLevel)),
                    TotalUnits = g.Sum(x => x.QuantityOnHand),
                    StockValue = g.Sum(x =>
                        x.QuantityOnHand * (x.CostPrice > 0 ? x.CostPrice : x.UnitPrice)),
                    RetailValue = g.Sum(x => x.QuantityOnHand * x.UnitPrice)
                })
                .FirstOrDefaultAsync();

            return stats ?? new InventorySummary();
        }

        public async Task<List<StockMovementView>> GetMovementsAsync(int? productId = null, int take = 200)
        {
            var movements = await _inventoryRepo.GetMovementsAsync(productId, take);
            return movements.Select(m => new StockMovementView
            {
                StockMovementId = m.StockMovementId,
                ProductId = m.ProductId,
                ProductCode = m.Product?.ProductCode ?? string.Empty,
                ProductName = m.Product?.ProductName ?? $"Product #{m.ProductId}",
                MovementType = m.MovementType,
                Quantity = m.Quantity,
                BalanceBefore = m.BalanceBefore,
                BalanceAfter = m.BalanceAfter,
                Reference = m.Reference,
                Notes = m.Notes,
                RecordedByEmployeeId = m.RecordedByEmployeeId,
                RecordedByName = m.RecordedByEmployee == null
                    ? "- unassigned -"
                    : $"{m.RecordedByEmployee.FirstName} {m.RecordedByEmployee.LastName}".Trim(),
                MovementDate = m.MovementDate
            }).ToList();
        }

        public async Task<Inventory> EnsureInventoryAsync(
            int productId, decimal openingStock = 0m, decimal reorderLevel = 0m)
        {
            var existing = await _inventoryRepo.GetByProductIdAsync(productId);
            if (existing != null) return existing;

            var inventory = new Inventory
            {
                ProductId = productId,
                QuantityOnHand = openingStock,
                ReorderLevel = reorderLevel,
                LastUpdatedAt = DateTime.UtcNow
            };

            await _inventoryRepo.AddAsync(inventory);

            if (openingStock > 0)
            {
                await _inventoryRepo.AddMovementAsync(new StockMovement
                {
                    ProductId = productId,
                    MovementType = "In",
                    Quantity = openingStock,
                    BalanceBefore = 0m,
                    BalanceAfter = openingStock,
                    Reference = "OPENING",
                    Notes = "Opening stock",
                    MovementDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
                await _inventoryRepo.SaveChangesAsync();
            }

            return inventory;
        }

        private async Task BackfillMissingInventoryRowsAsync()
        {
            var missing = await _context.Products
                .Where(p => !_context.Inventories.Any(i => i.ProductId == p.ProductId))
                .Select(p => p.ProductId)
                .ToListAsync();

            if (missing.Count == 0) return;

            foreach (var productId in missing)
            {
                _context.Inventories.Add(new Inventory
                {
                    ProductId = productId,
                    QuantityOnHand = 0m,
                    ReorderLevel = 0m,
                    LastUpdatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }

        public async Task<InventoryView> StockInAsync(
            int productId, decimal quantity, string reference, string notes, int? recordedByEmployeeId = null)
        {
            return await ApplyMovementAsync(productId, "In", quantity, reference, notes, recordedByEmployeeId);
        }

        public async Task<InventoryView> StockOutAsync(
            int productId, decimal quantity, string reference, string notes, int? recordedByEmployeeId = null)
        {
            return await ApplyMovementAsync(productId, "Out", quantity, reference, notes, recordedByEmployeeId);
        }

        // Records the delta needed to reach an absolute counted quantity.
        public async Task<InventoryView> AdjustAsync(
            int productId, decimal newQuantity, string notes, int? recordedByEmployeeId = null)
        {
            if (newQuantity < 0)
                throw new InvalidOperationException("Adjusted quantity cannot be negative.");

            await ValidateEmployeeAsync(recordedByEmployeeId);

            var inventory = await RequireInventoryAsync(productId);

            var before = inventory.QuantityOnHand;

            inventory.QuantityOnHand = newQuantity;
            inventory.LastUpdatedAt = DateTime.UtcNow;

            await _inventoryRepo.AddMovementAsync(new StockMovement
            {
                ProductId = productId,
                MovementType = "Adjustment",
                // The magnitude of the correction, which is what an audit actually asks about.
                Quantity = Math.Abs(newQuantity - before),
                BalanceBefore = before,
                BalanceAfter = newQuantity,
                Reference = "ADJUST",
                Notes = notes ?? string.Empty,
                RecordedByEmployeeId = recordedByEmployeeId,
                MovementDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });

            await _inventoryRepo.SaveChangesAsync();
            return ToView(inventory);
        }

        public async Task<InventoryView> SetReorderLevelAsync(int productId, decimal reorderLevel)
        {
            if (reorderLevel < 0)
                throw new InvalidOperationException("Reorder level cannot be negative.");

            var inventory = await RequireInventoryAsync(productId);
            inventory.ReorderLevel = reorderLevel;
            inventory.LastUpdatedAt = DateTime.UtcNow;

            await _inventoryRepo.SaveChangesAsync();
            return ToView(inventory);
        }

        private async Task<Inventory> RequireInventoryAsync(int productId)
        {
            var product = await _productRepo.GetByIdAsync(productId);
            if (product == null)
                throw new InvalidOperationException($"Product {productId} was not found.");

            var inventory = await _inventoryRepo.GetByProductIdAsync(productId);
            if (inventory == null)
            {
                inventory = await EnsureInventoryAsync(productId);
                inventory = await _inventoryRepo.GetByProductIdAsync(productId)
                            ?? throw new InvalidOperationException(
                                $"Could not create a stock record for product {productId}.");
            }

            return inventory;
        }

        private async Task ValidateEmployeeAsync(int? employeeId)
        {
            if (!employeeId.HasValue) return;

            if (!await _context.Employees.AnyAsync(e => e.EmployeeId == employeeId.Value))
                throw new InvalidOperationException("The selected staff member is not a known employee.");
        }

        private async Task<InventoryView> ApplyMovementAsync(
            int productId,
            string movementType,
            decimal quantity,
            string reference,
            string notes,
            int? recordedByEmployeeId)
        {
            if (quantity <= 0)
                throw new InvalidOperationException("Quantity must be greater than zero.");

            await ValidateEmployeeAsync(recordedByEmployeeId);

            var inventory = await RequireInventoryAsync(productId);

            var before = inventory.QuantityOnHand;
            var delta = movementType == "In" ? quantity : -quantity;
            var newBalance = before + delta;

            if (newBalance < 0)
                throw new InvalidOperationException(
                    $"Cannot remove {quantity:N2} units - only {before:N2} in stock.");

            inventory.QuantityOnHand = newBalance;
            inventory.LastUpdatedAt = DateTime.UtcNow;

            await _inventoryRepo.AddMovementAsync(new StockMovement
            {
                ProductId = productId,
                MovementType = movementType,
                Quantity = quantity,
                BalanceBefore = before,
                BalanceAfter = newBalance,
                Reference = reference ?? string.Empty,
                Notes = notes ?? string.Empty,
                RecordedByEmployeeId = recordedByEmployeeId,
                MovementDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });

            await _inventoryRepo.SaveChangesAsync();
            return ToView(inventory);
        }
    }
}
