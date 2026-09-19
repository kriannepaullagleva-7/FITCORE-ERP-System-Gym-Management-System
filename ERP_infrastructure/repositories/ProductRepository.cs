using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.repositories
{
    public class ProductRepository : GenericRepository<Product>, IProductRepository
    {
        public ProductRepository(TenantErpDbContext context) : base(context) { }

        public override async Task<List<Product>> GetAllAsync()
        {
            return await _dbSet.OrderBy(p => p.ProductName).ToListAsync();
        }

        public async Task<List<Product>> GetActiveProductsAsync()
        {
            return await _dbSet
                .Where(p => p.IsActive)
                .OrderBy(p => p.ProductName)
                .ToListAsync();
        }

        public async Task<Product?> GetByCodeAsync(string productCode)
        {
            return await _dbSet.FirstOrDefaultAsync(p => p.ProductCode == productCode);
        }

        public async Task<bool> CodeExistsAsync(string productCode, int? excludeProductId = null)
        {
            return await _dbSet.AnyAsync(p =>
                p.ProductCode == productCode &&
                (excludeProductId == null || p.ProductId != excludeProductId));
        }

        public async Task<bool> IsReferencedBySalesAsync(int productId)
        {
            return await _context.SaleItems.AnyAsync(si => si.ProductId == productId);
        }
    }
}
