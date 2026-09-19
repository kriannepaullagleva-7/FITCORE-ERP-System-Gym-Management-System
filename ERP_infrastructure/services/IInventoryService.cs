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

        Task<InventoryView> StockInAsync(
            int productId, decimal quantity, string reference, string notes, int? recordedByEmployeeId = null);

        Task<InventoryView> StockOutAsync(
            int productId, decimal quantity, string reference, string notes, int? recordedByEmployeeId = null);

        /// <summary>Corrects stock to an absolute counted quantity and records the delta.</summary>
        Task<InventoryView> AdjustAsync(
            int productId, decimal newQuantity, string notes, int? recordedByEmployeeId = null);

        Task<InventoryView> SetReorderLevelAsync(int productId, decimal reorderLevel);

        // Ensures every product has a stock row, so new products appear in the inventory grid.
        Task<Inventory> EnsureInventoryAsync(int productId, decimal openingStock = 0m, decimal reorderLevel = 0m);
    }
}
