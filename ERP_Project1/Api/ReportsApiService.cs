
namespace ERP_Project1.Api
{
    /// <summary>
    /// Talks to the cross-module report endpoints of ERP_api. Date ranges are passed to the
    /// server, which runs the aggregation against the tenant database.
    /// </summary>
    public class ReportsApiService : ApiServiceBase
    {
        private const string BasePath = "api/reports";

        public ReportsApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures)
        {
        }

        public Task<ApiResult<DashboardSummaryDto>> GetDashboardAsync() =>
            SendAsync<DashboardSummaryDto>(() => Http.GetAsync($"{BasePath}/dashboard"));

        public Task<ApiResult<TenantInfoDto>> GetCurrentTenantAsync() =>
            SendAsync<TenantInfoDto>(() => Http.GetAsync("api/tenant/current"));

        public Task<ApiResult<List<MembershipOverviewDto>>> GetMembershipOverviewAsync() =>
            SendAsync<List<MembershipOverviewDto>>(
                () => Http.GetAsync($"{BasePath}/membership-overview"));

        public Task<ApiResult<SalesReportDto>> GetSalesReportAsync(DateTime? from, DateTime? to) =>
            SendAsync<SalesReportDto>(() => Http.GetAsync(Range("sales", from, to)));

        public Task<ApiResult<PaymentReportDto>> GetPaymentReportAsync(DateTime? from, DateTime? to) =>
            SendAsync<PaymentReportDto>(() => Http.GetAsync(Range("payments", from, to)));

        public Task<ApiResult<InventoryReportDto>> GetInventoryReportAsync(DateTime? from, DateTime? to) =>
            SendAsync<InventoryReportDto>(() => Http.GetAsync(Range("inventory", from, to)));

        public Task<ApiResult<MembershipReportDto>> GetMembershipReportAsync(DateTime? from, DateTime? to) =>
            SendAsync<MembershipReportDto>(() => Http.GetAsync(Range("membership", from, to)));

        public Task<ApiResult<ExpenseReportDto>> GetExpenseReportAsync(DateTime? from, DateTime? to) =>
            SendAsync<ExpenseReportDto>(() => Http.GetAsync(Range("expenses", from, to)));

        private static string Range(string report, DateTime? from, DateTime? to) =>
            QueryString.Build(
                $"{BasePath}/{report}",
                ("from", QueryString.Date(from)),
                ("to", QueryString.Date(to)));
    }
}
