using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    public interface IInventoryService
    {
        Task<List<InventoryView>> GetInventoryAsync();
        Task<InventoryView?> GetByProductIdAsync(int productId);
        Task<List<StockMovementView>> GetMovementsAsync(int? productId = null, int take = 200);

        /// <summary>Headline stock counts and valuations for the Inventory KPIs.</summary>
        Task<InventorySummary> GetSummaryAsync();

        /// <summary>
        /// Receives stock from a supplier. A supplier is required: stock cannot enter the
        /// ledger with no answer to "who supplied this?".
        /// </summary>
        /// <param name="unitCost">
        /// What one unit cost on this delivery. Zero falls back to the product's catalogue cost
        /// price. Either way it goes into the weighted average the next sale will charge to
        /// cost of goods sold, which is why receiving stock without a cost is worth avoiding.
        /// </param>
        Task<InventoryView> StockInAsync(
            int productId, decimal quantity, int supplierId, string reference, string notes,
            int? recordedByEmployeeId = null, decimal unitCost = 0m, int? purchaseId = null);

        /// <summary>
        /// Removes stock for a reason from the fixed catalogue
        /// (<see cref="InventoryService.StockOutReasons"/>).
        /// </summary>
        Task<InventoryView> StockOutAsync(
            int productId, decimal quantity, string reason, string notes,
            int? recordedByEmployeeId = null);

        /// <summary>Corrects stock to an absolute counted quantity and records the delta.</summary>
        Task<InventoryView> AdjustAsync(
            int productId, decimal newQuantity, string notes, int? recordedByEmployeeId = null);

        Task<InventoryView> SetReorderLevelAsync(int productId, decimal reorderLevel);

        // Ensures every product has a stock row, so new products appear in the inventory grid.
        Task<Inventory> EnsureInventoryAsync(int productId, decimal openingStock = 0m, decimal reorderLevel = 0m);
    }
}
