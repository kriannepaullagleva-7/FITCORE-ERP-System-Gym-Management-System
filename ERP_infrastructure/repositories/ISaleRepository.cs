using ERP_domain.entities;

namespace ERP_infrastructure.repositories
{
    public interface ISaleRepository : IGenericRepository<Sale>
    {
        Task<List<Sale>> GetAllWithDetailsAsync();
        Task<List<Sale>> GetMemberSalesAsync(int memberId);
        Task<Sale?> GetSaleWithItemsAsync(int saleId);
        Task<decimal> GetTotalSalesAsync(DateTime? fromUtc = null);

        /// <summary>Sales whose date falls inside the range, newest first, with their lines.</summary>
        Task<List<Sale>> GetInRangeAsync(DateTime fromUtc, DateTime toUtc);
    }
}
