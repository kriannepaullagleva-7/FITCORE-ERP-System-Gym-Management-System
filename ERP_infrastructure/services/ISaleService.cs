using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    public interface ISaleService
    {
        Task<Sale?> GetSaleByIdAsync(int id);
        Task<List<SaleView>> GetAllSalesAsync();
        Task<List<SaleView>> GetSalesInRangeAsync(DateTime fromUtc, DateTime toUtc);
        Task<List<SaleView>> GetSalesForMemberAsync(int memberId);
        Task<SaleView?> GetSaleViewAsync(int id);
        Task<SaleDetailView?> GetSaleDetailAsync(int id);
        Task<List<SaleLineView>> GetSaleLinesAsync(int saleId);
        Task<List<Sale>> GetMemberSalesAsync(int memberId);

        /// <summary>
        /// Commits the sale, its lines and the matching stock deductions as one unit of work.
        /// Unit prices are read from the product records rather than taken from the caller.
        /// </summary>
        Task<Sale> CreateSaleAsync(
            int memberId,
            List<SaleLineRequest> items,
            decimal discount = 0m,
            int? cashierEmployeeId = null,
            string notes = "");

        /// <summary>
        /// Reverses a sale: stock goes back, the sale is marked Cancelled and the record stays
        /// for audit. Preferred over deletion for anything with money attached to it.
        /// </summary>
        Task<SaleView?> CancelSaleAsync(int id, string reason = "");

        /// <summary>
        /// Permanently removes a sale and returns its items to stock. Refused once payments
        /// have been recorded against it.
        /// </summary>
        Task<bool> DeleteSaleAsync(int id);
    }
}
