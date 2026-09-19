using ERP_domain.entities;

namespace ERP_infrastructure.repositories
{
    public interface IInventoryRepository : IGenericRepository<Inventory>
    {
        Task<List<Inventory>> GetAllWithProductAsync();
        Task<Inventory?> GetByProductIdAsync(int productId);
        Task<List<StockMovement>> GetMovementsAsync(int? productId = null, int take = 200);
        Task AddMovementAsync(StockMovement movement);
    }
}
