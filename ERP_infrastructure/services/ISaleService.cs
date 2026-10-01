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
        /// <param name="memberId">
        /// Null for a walk-in sale - a normal sale names neither a member nor a customer record.
        /// Exactly one of <paramref name="memberId"/> or <paramref name="walkInName"/> is used;
        /// when both are absent the sale is recorded simply as "Walk-In".
        /// </param>
        /// <param name="settleNow">
        /// When true the sale is paid in full as part of the same database transaction, so a
        /// completed counter sale and its payment can never exist without each other. Left
        /// false for a sale put on account, which is settled later from Payments.
        /// </param>
        /// <param name="paymentMethod">How the money was taken, when settling immediately.</param>
        /// <param name="amountTendered">
        /// Cash handed over, when <paramref name="paymentMethod"/> is Cash and
        /// <paramref name="settleNow"/> is true. The change given is computed from this, never
        /// accepted directly.
        /// </param>
        Task<Sale> CreateSaleAsync(
            int? memberId,
            List<SaleLineRequest> items,
            decimal discount = 0m,
            int? cashierEmployeeId = null,
            string notes = "",
            bool settleNow = false,
            string paymentMethod = "Cash",
            string? walkInName = null,
            decimal? amountTendered = null);

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
