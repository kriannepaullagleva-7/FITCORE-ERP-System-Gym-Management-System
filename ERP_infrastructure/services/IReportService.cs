namespace ERP_infrastructure.services
{
    /// <summary>
    /// Read-only cross-module reporting. Every figure is queried from the tenant database for
    /// the requested window; nothing is cached and nothing is precomputed.
    /// </summary>
    public interface IReportService
    {
        Task<SalesReport> GetSalesReportAsync(DateTime? from, DateTime? to);
        Task<PaymentReport> GetPaymentReportAsync(DateTime? from, DateTime? to);
        Task<InventoryReport> GetInventoryReportAsync(DateTime? from, DateTime? to);
        Task<MembershipReport> GetMembershipReportAsync(DateTime? from, DateTime? to);
        Task<ExpenseReport> GetExpenseReportAsync(DateTime? from, DateTime? to);
    }
}
