using ERP_domain.entities;
using ERP_infrastructure.data;
using ERP_infrastructure.repositories;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class InventoryService : IInventoryService
    {
        /// <summary>
        /// The only reasons a manual stock-out can carry. A stock-out driven by a sale is
        /// written by SaleService with MovementType "Sale" instead and does not go through
        /// this list.
        /// </summary>
        public static readonly string[] StockOutReasons =
            { "SALE", "DAMAGED", "USED", "REMOVED", "ADJUSTMENT" };

        private readonly IInventoryRepository _inventoryRepo;
        private readonly IProductRepository _productRepo;
        private readonly IGenericRepository<Supplier> _supplierRepo;
        private readonly TenantErpDbContext _context;
        private readonly ICurrentUserAccessor _actor;
        private readonly IAuditService _audit;
        private readonly IFinancePostingService _finance;

        public InventoryService(
            IInventoryRepository inventoryRepo,
            IProductRepository productRepo,
            IGenericRepository<Supplier> supplierRepo,
            TenantErpDbContext context,
            ICurrentUserAccessor actor,
            IAuditService audit,
            IFinancePostingService finance)
        {
            _inventoryRepo = inventoryRepo;
            _productRepo = productRepo;
            _supplierRepo = supplierRepo;
            _context = context;
            _actor = actor;
            _audit = audit;
            _finance = finance;
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

        /// <summary>
        /// What one unit on hand is carried at: the weighted average where stock has actually
        /// been received at a cost, then the catalogue cost, then the selling price.
        /// </summary>
        private static decimal ValuationCostOf(Inventory inventory) =>
            inventory.AverageCost > 0m ? inventory.AverageCost
            : inventory.Product?.CostPrice > 0m ? inventory.Product.CostPrice
            : inventory.Product?.UnitPrice ?? 0m;

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
                LastUpdatedAt = inventory.LastUpdatedAt,

                // What the stock on hand is actually worth, as opposed to what the catalogue
                // says one unit costs. They differ as soon as two deliveries arrive at
                // different prices, and it is this one the balance sheet carries.
                AverageCost = inventory.AverageCost,
                LastUnitCost = inventory.LastUnitCost,

                // Falls back to the catalogue cost, and then to the selling price, so a
                // product that has never been through a costed receipt is not valued at
                // nothing - which would understate the balance sheet rather than admit the
                // figure is an estimate.
                StockValue = Math.Round(
                    inventory.QuantityOnHand * ValuationCostOf(inventory),
                    2, MidpointRounding.AwayFromZero)
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
                    i.AverageCost,
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
                    // Valued at the weighted average where stock has been costed, and at the
                    // catalogue cost otherwise, so a product that has never been through a
                    // purchase is not silently valued at nothing.
                    StockValue = g.Sum(x =>
                        x.QuantityOnHand * (x.AverageCost > 0 ? x.AverageCost
                            : x.CostPrice > 0 ? x.CostPrice : x.UnitPrice)),
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
                UnitCost = m.UnitCost,
                TotalCost = m.TotalCost,
                Reference = m.Reference,
                Notes = m.Notes,
                SupplierId = m.SupplierId,
                SupplierName = m.Supplier?.SupplierName ?? "",
                RecordedByEmployeeId = m.RecordedByEmployeeId,
                RecordedByName = m.RecordedByEmployee == null
                    ? "- unassigned -"
                    : $"{m.RecordedByEmployee.FirstName} {m.RecordedByEmployee.LastName}".Trim(),
                PerformedByUserId = m.PerformedByUserId,
                PerformedBy = string.IsNullOrWhiteSpace(m.PerformedBy) ? "—" : m.PerformedBy,
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
                var actor = _actor.Current;
                await _inventoryRepo.AddMovementAsync(new StockMovement
                {
                    ProductId = productId,
                    MovementType = "In",
                    Quantity = openingStock,
                    BalanceBefore = 0m,
                    BalanceAfter = openingStock,
                    Reference = "OPENING",
                    Notes = "Opening stock",
                    PerformedByUserId = actor.AppUserId,
                    PerformedBy = actor.DisplayName,
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
            int productId, decimal quantity, int supplierId, string reference, string notes,
            int? recordedByEmployeeId = null, decimal unitCost = 0m, int? purchaseId = null)
        {
            var supplier = await _supplierRepo.GetByIdAsync(supplierId);
            if (supplier == null)
                throw new ValidationException("Stock cannot be received without a known supplier.");

            if (!supplier.IsActive)
                throw new ValidationException($"{supplier.SupplierName} is inactive and cannot supply stock.");

            if (unitCost < 0m)
                throw new ValidationException("A unit cost cannot be negative.");

            return await ApplyMovementAsync(
                productId, "In", quantity, supplierId, reference, notes, recordedByEmployeeId,
                unitCost, purchaseId);
        }

        public async Task<InventoryView> StockOutAsync(
            int productId, decimal quantity, string reason, string notes,
            int? recordedByEmployeeId = null)
        {
            var normalisedReason = StockOutReasons.FirstOrDefault(r =>
                string.Equals(r, (reason ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

            if (normalisedReason is null)
            {
                throw new ValidationException(
                    "A reason is required for stock removed manually: " +
                    string.Join(", ", StockOutReasons) + ".");
            }

            return await ApplyMovementAsync(
                productId, "Out", quantity, null, normalisedReason, notes, recordedByEmployeeId);
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

            var actor = _actor.Current;

            var difference = Math.Abs(newQuantity - before);

            // An adjustment corrects an existing balance rather than receiving new stock, so it
            // has no supplier of its own - but the stock being corrected came from somewhere.
            // Carrying the most recent stock-in's supplier forward keeps the adjustment
            // traceable to where that stock entered, rather than breaking the chain the moment
            // a count correction is recorded.
            var lastSupplierId = await _context.StockMovements
                .Where(m => m.ProductId == productId && m.SupplierId != null)
                .OrderByDescending(m => m.MovementDate)
                .Select(m => m.SupplierId)
                .FirstOrDefaultAsync();

            var movement = new StockMovement
            {
                ProductId = productId,
                MovementType = "Adjustment",
                // The magnitude of the correction, which is what an audit actually asks about.
                Quantity = difference,
                BalanceBefore = before,
                BalanceAfter = newQuantity,

                // Valued at the weighted average, so a count that found less than the books
                // said writes off what that stock actually cost.
                UnitCost = inventory.AverageCost,
                TotalCost = Math.Round(difference * inventory.AverageCost, 2, MidpointRounding.AwayFromZero),

                SupplierId = lastSupplierId,
                Reference = "ADJUST",
                Notes = notes ?? string.Empty,
                RecordedByEmployeeId = recordedByEmployeeId,
                PerformedByUserId = actor.AppUserId,
                PerformedBy = actor.DisplayName,
                MovementDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            await _inventoryRepo.AddMovementAsync(movement);
            await _inventoryRepo.SaveChangesAsync();

            await _audit.RecordAsync(
                AuditActions.StockAdjusted, ErpModules.Inventory, nameof(Inventory), productId.ToString(),
                $"{inventory.Product?.ProductName ?? $"Product #{productId}"}: counted {newQuantity:N2}, " +
                $"was {before:N2}.");

            await _finance.PostStockAdjustmentAsync(movement.StockMovementId);

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
            int? supplierId,
            string reference,
            string notes,
            int? recordedByEmployeeId,
            decimal unitCost = 0m,
            int? purchaseId = null)
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

            var movementCost = ResolveUnitCost(inventory, movementType, unitCost);

            if (movementType == "In")
            {
                ApplyWeightedAverage(inventory, quantity, movementCost);
            }

            inventory.QuantityOnHand = newBalance;
            inventory.LastUpdatedAt = DateTime.UtcNow;

            // The signed-in user, never the client, is who a movement is attributed to.
            var actor = _actor.Current;

            var movement = new StockMovement
            {
                ProductId = productId,
                MovementType = movementType,
                Quantity = quantity,
                BalanceBefore = before,
                BalanceAfter = newBalance,
                UnitCost = movementCost,
                TotalCost = Math.Round(quantity * movementCost, 2, MidpointRounding.AwayFromZero),
                SupplierId = supplierId,
                PurchaseId = purchaseId,
                Reference = reference ?? string.Empty,
                Notes = notes ?? string.Empty,
                RecordedByEmployeeId = recordedByEmployeeId,
                PerformedByUserId = actor.AppUserId,
                PerformedBy = actor.DisplayName,
                MovementDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            await _inventoryRepo.AddMovementAsync(movement);
            await _inventoryRepo.SaveChangesAsync();

            var productName = inventory.Product?.ProductName ?? $"Product #{productId}";

            if (movementType == "In")
            {
                await _audit.RecordAsync(
                    AuditActions.StockReceived, ErpModules.Inventory, nameof(Inventory), productId.ToString(),
                    $"{productName}: received {quantity:N2}, now {newBalance:N2} on hand.");

                // A receipt against a purchase is posted by the purchase, which knows what is
                // owed for it. A receipt entered by hand has nothing else behind it, so it is
                // posted here or it would never reach the books at all.
                if (!purchaseId.HasValue)
                {
                    await _finance.PostStockAdjustmentAsync(movement.StockMovementId);
                }
            }
            else
            {
                await _audit.RecordAsync(
                    AuditActions.StockIssued, ErpModules.Inventory, nameof(Inventory), productId.ToString(),
                    $"{productName}: {reference} — issued {quantity:N2}, now {newBalance:N2} on hand.");

                // Stock removed by hand - breakage, expiry, a sample - is a loss, and saying
                // so in the ledger is what keeps the inventory account and the shelf agreeing.
                await _finance.PostStockAdjustmentAsync(movement.StockMovementId);
            }

            return ToView(inventory);
        }

        /// <summary>
        /// What one unit of this movement is worth.
        ///
        /// A receipt is worth what was paid for it, falling back to the catalogue cost when
        /// nobody said. Everything leaving is worth the weighted average of what is on hand -
        /// selling something does not change what the remainder cost.
        /// </summary>
        private static decimal ResolveUnitCost(Inventory inventory, string movementType, decimal unitCost)
        {
            if (movementType != "In") return inventory.AverageCost;

            if (unitCost > 0m) return unitCost;

            return inventory.AverageCost > 0m
                ? inventory.AverageCost
                : inventory.Product?.CostPrice ?? 0m;
        }

        /// <summary>
        /// Folds a receipt into the weighted average cost.
        ///
        /// <c>(value on hand + value received) / (quantity on hand + quantity received)</c>.
        /// Carried to four places, because rounding each receipt to two and then compounding
        /// a few hundred of them produces a valuation that is visibly wrong.
        /// </summary>
        private static void ApplyWeightedAverage(Inventory inventory, decimal quantity, decimal unitCost)
        {
            inventory.LastUnitCost = unitCost;

            var onHand = Math.Max(0m, inventory.QuantityOnHand);
            var total = onHand + quantity;

            if (total <= 0m) return;

            // Stock received before anybody recorded a cost has no value to average in, so the
            // first costed receipt sets the average outright rather than being halved by a
            // notional zero.
            if (inventory.AverageCost <= 0m)
            {
                inventory.AverageCost = unitCost;
                return;
            }

            var value = (onHand * inventory.AverageCost) + (quantity * unitCost);

            inventory.AverageCost = Math.Round(value / total, 4, MidpointRounding.AwayFromZero);
        }
    }
}
