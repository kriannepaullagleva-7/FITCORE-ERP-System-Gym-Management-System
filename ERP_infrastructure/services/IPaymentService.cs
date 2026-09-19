using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    public interface IPaymentService
    {
        Task<Payment?> GetPaymentByIdAsync(int id);
        Task<List<PaymentView>> GetAllPaymentsAsync();
        Task<List<PaymentView>> GetPaymentsInRangeAsync(DateTime fromUtc, DateTime toUtc);
        Task<List<PaymentView>> GetMemberPaymentsAsync(int memberId);
        Task<List<PaymentView>> GetSalePaymentsAsync(int saleId);
        Task<List<Payment>> GetSubscriptionPaymentsAsync(int subscriptionId);
        Task<decimal> GetSubscriptionPaidTotalAsync(int subscriptionId);
        Task<decimal> GetSalePaidTotalAsync(int saleId);

        /// <summary>Headline collection and outstanding-balance figures.</summary>
        Task<PaymentSummary> GetSummaryAsync();

        // Full form used by the Payment module. A payment may settle a subscription or a sale,
        // but not both.
        Task<Payment> RecordPaymentAsync(
            int memberId,
            int? subscriptionId,
            int? saleId,
            decimal amount,
            DateTime paymentDate,
            string method,
            string referenceNo,
            string status,
            string notes);

        // Subscription-scoped shorthand kept for the existing API and Subscription screen.
        Task<Payment> RecordPaymentAsync(int subscriptionId, decimal amount, string method);

        Task<Payment?> UpdatePaymentAsync(
            int id,
            decimal amount,
            DateTime paymentDate,
            string method,
            string referenceNo,
            string status,
            string notes);

        Task<Payment?> UpdatePaymentStatusAsync(int id, string status);

        /// <summary>
        /// Marks the payment Refunded and keeps the record. This is how a settled payment is
        /// reversed; the row is never destroyed.
        /// </summary>
        Task<Payment?> VoidPaymentAsync(int id, string reason = "");

        Task<bool> DeletePaymentAsync(int id);
    }
}
