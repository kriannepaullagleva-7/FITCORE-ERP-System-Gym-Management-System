using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    public interface IProductService
    {
        Task<List<Product>> GetAllProductsAsync();
        Task<List<Product>> GetActiveProductsAsync();
        Task<Product?> GetProductByIdAsync(int id);

        /// <summary>The distinct categories currently in use, for the filter dropdowns.</summary>
        Task<List<string>> GetCategoriesAsync();

        Task<Product> CreateProductAsync(
            string productCode,
            string productName,
            string category,
            decimal costPrice,
            decimal unitPrice,
            decimal openingStock,
            decimal reorderLevel);

        Task<Product?> UpdateProductAsync(
            int id,
            string productCode,
            string productName,
            string category,
            decimal costPrice,
            decimal unitPrice,
            bool isActive);

        Task<bool> DeleteProductAsync(int id);
    }
}
