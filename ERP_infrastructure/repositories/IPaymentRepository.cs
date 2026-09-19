using ERP_domain.entities;

namespace ERP_infrastructure.repositories
{
    public interface IPaymentRepository : IGenericRepository<Payment>
    {
        Task<List<Payment>> GetAllWithDetailsAsync();
        Task<Payment?> GetWithDetailsAsync(int paymentId);
        Task<List<Payment>> GetMemberPaymentsAsync(int memberId);
        Task<List<Payment>> GetSubscriptionPaymentsAsync(int subscriptionId);
        Task<List<Payment>> GetSalePaymentsAsync(int saleId);
        Task<decimal> GetSubscriptionPaidTotalAsync(int subscriptionId);
        Task<decimal> GetSalePaidTotalAsync(int saleId);

        /// <summary>
        /// Completed payment totals for many sales in a single query, so a sales grid can show
        /// a balance per row without issuing one query per sale.
        /// </summary>
        Task<Dictionary<int, decimal>> GetPaidTotalsBySaleAsync(IEnumerable<int> saleIds);

        /// <summary>Payments whose date falls inside the range, newest first.</summary>
        Task<List<Payment>> GetInRangeAsync(DateTime fromUtc, DateTime toUtc);

        /// <summary>
        /// Completed payment totals for many subscriptions in a single query. Callers that
        /// need a total per row should use this instead of calling
        /// <see cref="GetSubscriptionPaidTotalAsync"/> in a loop.
        /// </summary>
        Task<Dictionary<int, decimal>> GetPaidTotalsBySubscriptionAsync(IEnumerable<int> subscriptionIds);
    }
}
