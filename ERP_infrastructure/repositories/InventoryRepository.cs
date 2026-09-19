using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.repositories
{
    public class InventoryRepository : GenericRepository<Inventory>, IInventoryRepository
    {
        public InventoryRepository(TenantErpDbContext context) : base(context) { }

        public async Task<List<Inventory>> GetAllWithProductAsync()
        {
            return await _dbSet
                .Include(i => i.Product)
                .OrderBy(i => i.Product!.ProductName)
                .ToListAsync();
        }

        public async Task<Inventory?> GetByProductIdAsync(int productId)
        {
            return await _dbSet
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.ProductId == productId);
        }

        public async Task<List<StockMovement>> GetMovementsAsync(int? productId = null, int take = 200)
        {
            var query = _context.StockMovements
                .Include(m => m.Product)
                .Include(m => m.RecordedByEmployee)
                .AsQueryable();

            if (productId.HasValue)
                query = query.Where(m => m.ProductId == productId.Value);

            return await query
                .OrderByDescending(m => m.MovementDate)
                .ThenByDescending(m => m.StockMovementId)
                .Take(take)
                .ToListAsync();
        }

        public async Task AddMovementAsync(StockMovement movement)
        {
            await _context.StockMovements.AddAsync(movement);
        }
    }
}
