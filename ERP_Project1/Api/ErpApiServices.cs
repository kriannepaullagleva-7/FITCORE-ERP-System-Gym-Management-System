using System.Net.Http.Json;

namespace ERP_Project1.Api
{
    // Every service here talks to ERP_api over HTTPS. None of them knows anything about a
    // database or a connection string, and none of them sends a company id: the server
    // decides which tenant the caller belongs to.

    /// <summary>Builds a query string from the parts that actually have a value.</summary>
    internal static class QueryString
    {
        public static string Build(string basePath, params (string Key, string? Value)[] parts)
        {
            var query = parts
                .Where(p => !string.IsNullOrWhiteSpace(p.Value))
                .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}")
                .ToList();

            return query.Count == 0 ? basePath : $"{basePath}?{string.Join("&", query)}";
        }

        public static string? Date(DateTime? value) => value?.ToString("yyyy-MM-dd");
    }

    public class ProductApiService : ApiServiceBase
    {
        private const string BasePath = "api/products";

        public ProductApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        public Task<ApiResult<List<ProductDto>>> GetAllAsync() =>
            SendAsync<List<ProductDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<List<ProductDto>>> GetActiveAsync() =>
            SendAsync<List<ProductDto>>(() => Http.GetAsync($"{BasePath}/active"));

        public Task<ApiResult<List<string>>> GetCategoriesAsync() =>
            SendAsync<List<string>>(() => Http.GetAsync($"{BasePath}/categories"));

        public Task<ApiResult<ProductDto>> CreateAsync(CreateProductDto dto) =>
            SendAsync<ProductDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<ProductDto>> UpdateAsync(int id, UpdateProductDto dto) =>
            SendAsync<ProductDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));
    }

    public class CustomerApiService : ApiServiceBase
    {
        private const string BasePath = "api/customers";

        public CustomerApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        public Task<ApiResult<List<CustomerDto>>> GetAllAsync() =>
            SendAsync<List<CustomerDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<CustomerDto>> CreateAsync(CreateCustomerDto dto) =>
            SendAsync<CustomerDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<CustomerDto>> UpdateAsync(int id, UpdateCustomerDto dto) =>
            SendAsync<CustomerDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));
    }

    public class SupplierApiService : ApiServiceBase
    {
        private const string BasePath = "api/suppliers";

        public SupplierApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        public Task<ApiResult<List<SupplierDto>>> GetAllAsync() =>
            SendAsync<List<SupplierDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<SupplierDto>> CreateAsync(CreateSupplierDto dto) =>
            SendAsync<SupplierDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<SupplierDto>> UpdateAsync(int id, UpdateSupplierDto dto) =>
            SendAsync<SupplierDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));
    }

    public class InventoryApiService : ApiServiceBase
    {
        private const string BasePath = "api/inventory";

        public InventoryApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        public Task<ApiResult<List<InventoryDto>>> GetAllAsync() =>
            SendAsync<List<InventoryDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<InventorySummaryDto>> GetSummaryAsync() =>
            SendAsync<InventorySummaryDto>(() => Http.GetAsync($"{BasePath}/summary"));

        public Task<ApiResult<List<StockMovementDto>>> GetMovementsAsync(int? productId = null, int take = 100) =>
            SendAsync<List<StockMovementDto>>(() => Http.GetAsync(
                productId.HasValue
                    ? $"{BasePath}/movements?productId={productId.Value}&take={take}"
                    : $"{BasePath}/movements?take={take}"));

        public Task<ApiResult<InventoryDto>> StockInAsync(int productId, StockMovementRequestDto dto) =>
            SendAsync<InventoryDto>(() => Http.PostAsJsonAsync($"{BasePath}/{productId}/stock-in", dto));

        public Task<ApiResult<InventoryDto>> StockOutAsync(int productId, StockMovementRequestDto dto) =>
            SendAsync<InventoryDto>(() => Http.PostAsJsonAsync($"{BasePath}/{productId}/stock-out", dto));

        public Task<ApiResult<InventoryDto>> AdjustAsync(int productId, StockAdjustmentDto dto) =>
            SendAsync<InventoryDto>(() => Http.PostAsJsonAsync($"{BasePath}/{productId}/adjust", dto));

        public Task<ApiResult<InventoryDto>> SetReorderLevelAsync(int productId, ReorderLevelDto dto) =>
            SendAsync<InventoryDto>(() => Http.PutAsJsonAsync($"{BasePath}/{productId}/reorder-level", dto));
    }

    public class SubscriptionApiService : ApiServiceBase
    {
        private const string BasePath = "api/subscriptions";

        public SubscriptionApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        public Task<ApiResult<List<SubscriptionDto>>> GetAllAsync() =>
            SendAsync<List<SubscriptionDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<List<SubscriptionDto>>> GetActiveAsync() =>
            SendAsync<List<SubscriptionDto>>(() => Http.GetAsync($"{BasePath}/active"));

        public Task<ApiResult<List<SubscriptionDto>>> GetForMemberAsync(int memberId) =>
            SendAsync<List<SubscriptionDto>>(() => Http.GetAsync($"api/members/{memberId}/subscriptions"));

        public Task<ApiResult<SubscriptionDto>> CreateAsync(CreateSubscriptionDto dto) =>
            SendAsync<SubscriptionDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<SubscriptionDto>> RenewAsync(int id) =>
            SendAsync<SubscriptionDto>(() => Http.PostAsync($"{BasePath}/{id}/renew", null));

        public Task<ApiResult<SubscriptionDto>> CancelAsync(int id) =>
            SendAsync<SubscriptionDto>(() => Http.PostAsync($"{BasePath}/{id}/cancel", null));

        public Task<ApiResult<object>> ExpireOverdueAsync() =>
            SendAsync<object>(() => Http.PostAsync($"{BasePath}/expire-overdue", null));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));
    }

    public class PaymentApiService : ApiServiceBase
    {
        private const string BasePath = "api/payments";

        public PaymentApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        /// <summary>
        /// Date filtering is done by the server, so a long ledger is never shipped to the
        /// browser just to be narrowed down here.
        /// </summary>
        public Task<ApiResult<List<PaymentViewDto>>> GetAllAsync(DateTime? from = null, DateTime? to = null) =>
            SendAsync<List<PaymentViewDto>>(() => Http.GetAsync(
                QueryString.Build(BasePath, ("from", QueryString.Date(from)), ("to", QueryString.Date(to)))));

        public Task<ApiResult<PaymentSummaryDto>> GetSummaryAsync() =>
            SendAsync<PaymentSummaryDto>(() => Http.GetAsync($"{BasePath}/summary"));

        public Task<ApiResult<PaymentViewDto>> RecordAsync(RecordPaymentDto dto) =>
            SendAsync<PaymentViewDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<PaymentViewDto>> UpdateAsync(int id, UpdatePaymentDto dto) =>
            SendAsync<PaymentViewDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<PaymentViewDto>> SetStatusAsync(int id, UpdatePaymentStatusDto dto) =>
            SendAsync<PaymentViewDto>(() => Http.PatchAsJsonAsync($"{BasePath}/{id}/status", dto));

        public Task<ApiResult<PaymentViewDto>> VoidAsync(int id, VoidPaymentDto dto) =>
            SendAsync<PaymentViewDto>(() => Http.PostAsJsonAsync($"{BasePath}/{id}/void", dto));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));
    }

    public class SaleApiService : ApiServiceBase
    {
        private const string BasePath = "api/sales";

        public SaleApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        public Task<ApiResult<List<SaleViewDto>>> GetAllAsync(DateTime? from = null, DateTime? to = null) =>
            SendAsync<List<SaleViewDto>>(() => Http.GetAsync(
                QueryString.Build(BasePath, ("from", QueryString.Date(from)), ("to", QueryString.Date(to)))));

        public Task<ApiResult<SaleDetailDto>> GetDetailAsync(int saleId) =>
            SendAsync<SaleDetailDto>(() => Http.GetAsync($"{BasePath}/{saleId}/detail"));

        public Task<ApiResult<List<SaleLineDto>>> GetItemsAsync(int saleId) =>
            SendAsync<List<SaleLineDto>>(() => Http.GetAsync($"{BasePath}/{saleId}/items"));

        public Task<ApiResult<SaleDto>> CreateAsync(CreateSaleDto dto) =>
            SendAsync<SaleDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<SaleViewDto>> CancelAsync(int id, CancelSaleDto dto) =>
            SendAsync<SaleViewDto>(() => Http.PostAsJsonAsync($"{BasePath}/{id}/cancel", dto));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));
    }

    public class EmployeeApiService : ApiServiceBase
    {
        private const string BasePath = "api/employees";

        public EmployeeApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        public Task<ApiResult<List<EmployeeDto>>> GetAllAsync() =>
            SendAsync<List<EmployeeDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<List<EmployeeDto>>> GetActiveAsync() =>
            SendAsync<List<EmployeeDto>>(() => Http.GetAsync($"{BasePath}/active"));

        public Task<ApiResult<List<EmployeeDto>>> SearchAsync(string term) =>
            SendAsync<List<EmployeeDto>>(() =>
                Http.GetAsync($"{BasePath}/search?term={Uri.EscapeDataString(term)}"));

        public Task<ApiResult<EmployeeDto>> CreateAsync(CreateEmployeeDto dto) =>
            SendAsync<EmployeeDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<EmployeeDto>> UpdateAsync(int id, UpdateEmployeeDto dto) =>
            SendAsync<EmployeeDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));
    }

    public class PayrollApiService : ApiServiceBase
    {
        private const string BasePath = "api/payroll";

        public PayrollApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        public Task<ApiResult<List<PayrollDto>>> GetAllAsync() =>
            SendAsync<List<PayrollDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<PayrollSummaryDto>> GetSummaryAsync() =>
            SendAsync<PayrollSummaryDto>(() => Http.GetAsync($"{BasePath}/summary"));

        public Task<ApiResult<PayrollDto>> CreateAsync(CreatePayrollDto dto) =>
            SendAsync<PayrollDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<PayrollDto>> UpdateAsync(int id, UpdatePayrollDto dto) =>
            SendAsync<PayrollDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<PayrollDto>> SetStatusAsync(int id, UpdatePayrollStatusDto dto) =>
            SendAsync<PayrollDto>(() => Http.PatchAsJsonAsync($"{BasePath}/{id}/status", dto));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));
    }

    public class ExpenseApiService : ApiServiceBase
    {
        private const string BasePath = "api/expenses";

        public ExpenseApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        /// <summary>
        /// Date and category filtering is done by the server, so a long history is never
        /// shipped to the browser just to be narrowed down here.
        /// </summary>
        public Task<ApiResult<List<ExpenseDto>>> GetAllAsync(
            DateTime? from = null, DateTime? to = null, string? category = null) =>
            SendAsync<List<ExpenseDto>>(() => Http.GetAsync(QueryString.Build(
                BasePath,
                ("from", QueryString.Date(from)),
                ("to", QueryString.Date(to)),
                ("category", category))));

        public Task<ApiResult<ExpenseSummaryDto>> GetSummaryAsync() =>
            SendAsync<ExpenseSummaryDto>(() => Http.GetAsync($"{BasePath}/summary"));

        public Task<ApiResult<List<string>>> GetCategoriesAsync() =>
            SendAsync<List<string>>(() => Http.GetAsync($"{BasePath}/categories"));

        public Task<ApiResult<ExpenseDto>> CreateAsync(CreateExpenseDto dto) =>
            SendAsync<ExpenseDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<ExpenseDto>> UpdateAsync(int id, UpdateExpenseDto dto) =>
            SendAsync<ExpenseDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));
    }

    /// <summary>
    /// SaaS administration. These endpoints are guarded server-side by
    /// Tenancy:EnableCrossTenantAdminApi and answer 403 when it is off, which the page
    /// surfaces rather than hiding.
    /// </summary>
    public class AdminApiService : ApiServiceBase
    {
        private const string BasePath = "api/companies";

        public AdminApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        public Task<ApiResult<List<CompanyDto>>> GetCompaniesAsync() =>
            SendAsync<List<CompanyDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<List<CompanyDatabaseDto>>> GetDatabasesAsync() =>
            SendAsync<List<CompanyDatabaseDto>>(() => Http.GetAsync($"{BasePath}/databases"));

        public Task<ApiResult<ProvisioningStatusDto>> GetProvisioningAsync(int companyId) =>
            SendAsync<ProvisioningStatusDto>(() => Http.GetAsync($"{BasePath}/{companyId}/provisioning"));

        public Task<ApiResult<object>> ProvisionAsync(int companyId) =>
            SendAsync<object>(() => Http.PostAsync($"{BasePath}/{companyId}/provision", null));

        public Task<ApiResult<CompanyDto>> CreateCompanyAsync(CreateCompanyDto dto) =>
            SendAsync<CompanyDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<CompanyDatabaseDto>> CreateDatabaseAsync(CreateCompanyDatabaseDto dto) =>
            SendAsync<CompanyDatabaseDto>(() => Http.PostAsJsonAsync($"{BasePath}/databases", dto));
    }
}
