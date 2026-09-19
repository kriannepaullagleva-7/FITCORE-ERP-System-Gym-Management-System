using ERP_domain.entities;

namespace ERP_infrastructure.repositories
{
    public interface IProductRepository : IGenericRepository<Product>
    {
        Task<List<Product>> GetActiveProductsAsync();
        Task<Product?> GetByCodeAsync(string productCode);
        Task<bool> CodeExistsAsync(string productCode, int? excludeProductId = null);
        Task<bool> IsReferencedBySalesAsync(int productId);
    }
}
