using System.Net.Http.Json;

namespace ERP_Project1.Api
{
    /// <summary>Purchasing: orders, receiving, and what is owed to suppliers.</summary>
    public class PurchaseApiService : ApiServiceBase
    {
        private const string BasePath = "api/purchases";

        public PurchaseApiService(HttpClient http, IApiFailureSink? failures = null)
            : base(http, failures) { }

        public Task<ApiResult<List<PurchaseDto>>> GetAllAsync(
            DateTime? from = null, DateTime? to = null,
            string? status = null, int? supplierId = null) =>
            SendAsync<List<PurchaseDto>>(() => Http.GetAsync(QueryString.Build(
                BasePath,
                ("from", QueryString.Date(from)),
                ("to", QueryString.Date(to)),
                ("status", status),
                ("supplierId", supplierId?.ToString()))));

        public Task<ApiResult<PurchaseSummaryDto>> GetSummaryAsync() =>
            SendAsync<PurchaseSummaryDto>(() => Http.GetAsync($"{BasePath}/summary"));

        public Task<ApiResult<PurchaseDto>> GetByIdAsync(int id) =>
            SendAsync<PurchaseDto>(() => Http.GetAsync($"{BasePath}/{id}"));

        public Task<ApiResult<PurchaseDto>> CreateAsync(CreatePurchaseDto dto) =>
            SendAsync<PurchaseDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<PurchaseDto>> UpdateAsync(int id, UpdatePurchaseDto dto) =>
            SendAsync<PurchaseDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<PurchaseDto>> MarkOrderedAsync(int id) =>
            SendAsync<PurchaseDto>(() => Http.PostAsync($"{BasePath}/{id}/order", null));

        /// <summary>
        /// Receives the goods. Passing no quantities takes everything still outstanding, which
        /// is what a complete delivery means.
        /// </summary>
        public Task<ApiResult<PurchaseDto>> ReceiveAsync(
            int id, Dictionary<int, decimal>? receivedByItemId = null) =>
            SendAsync<PurchaseDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/{id}/receive",
                new ReceivePurchaseDto { ReceivedByItemId = receivedByItemId }));

        public Task<ApiResult<PurchaseDto>> CancelAsync(int id, string reason) =>
            SendAsync<PurchaseDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/{id}/cancel", new ReasonDto { Reason = reason }));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));

        public Task<ApiResult<List<SupplierPaymentDto>>> GetSupplierPaymentsAsync(
            int? supplierId = null, int? purchaseId = null,
            DateTime? from = null, DateTime? to = null) =>
            SendAsync<List<SupplierPaymentDto>>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/payments",
                ("supplierId", supplierId?.ToString()),
                ("purchaseId", purchaseId?.ToString()),
                ("from", QueryString.Date(from)),
                ("to", QueryString.Date(to)))));

        public Task<ApiResult<SupplierPaymentDto>> PaySupplierAsync(PaySupplierDto dto) =>
            SendAsync<SupplierPaymentDto>(() => Http.PostAsJsonAsync($"{BasePath}/payments", dto));
    }

    /// <summary>Returns and refunds.</summary>
    public class SaleReturnApiService : ApiServiceBase
    {
        private const string BasePath = "api/returns";

        public SaleReturnApiService(HttpClient http, IApiFailureSink? failures = null)
            : base(http, failures) { }

        public Task<ApiResult<List<SaleReturnDto>>> GetAllAsync(
            DateTime? from = null, DateTime? to = null, int? saleId = null) =>
            SendAsync<List<SaleReturnDto>>(() => Http.GetAsync(QueryString.Build(
                BasePath,
                ("from", QueryString.Date(from)),
                ("to", QueryString.Date(to)),
                ("saleId", saleId?.ToString()))));

        public Task<ApiResult<List<string>>> GetReasonsAsync() =>
            SendAsync<List<string>>(() => Http.GetAsync($"{BasePath}/reasons"));

        /// <summary>What is still returnable on a sale, so the till offers only what can come back.</summary>
        public Task<ApiResult<List<ReturnableLineDto>>> GetReturnableAsync(int saleId) =>
            SendAsync<List<ReturnableLineDto>>(() => Http.GetAsync($"{BasePath}/returnable/{saleId}"));

        public Task<ApiResult<SaleReturnDto>> GetByIdAsync(int id) =>
            SendAsync<SaleReturnDto>(() => Http.GetAsync($"{BasePath}/{id}"));

        public Task<ApiResult<SaleReturnDto>> CreateAsync(CreateSaleReturnDto dto) =>
            SendAsync<SaleReturnDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<SaleReturnDto>> CancelAsync(int id, string reason) =>
            SendAsync<SaleReturnDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/{id}/cancel", new ReasonDto { Reason = reason }));
    }

    /// <summary>Leave requests, and the balance an employee has taken.</summary>
    public class LeaveApiService : ApiServiceBase
    {
        private const string BasePath = "api/leave";

        public LeaveApiService(HttpClient http, IApiFailureSink? failures = null)
            : base(http, failures) { }

        public Task<ApiResult<List<LeaveRequestDto>>> GetAllAsync(
            int? employeeId = null, DateTime? from = null, DateTime? to = null,
            string? status = null) =>
            SendAsync<List<LeaveRequestDto>>(() => Http.GetAsync(QueryString.Build(
                BasePath,
                ("employeeId", employeeId?.ToString()),
                ("from", QueryString.Date(from)),
                ("to", QueryString.Date(to)),
                ("status", status))));

        public Task<ApiResult<List<string>>> GetTypesAsync() =>
            SendAsync<List<string>>(() => Http.GetAsync($"{BasePath}/types"));

        public Task<ApiResult<LeaveBalanceDto>> GetBalanceAsync(int employeeId, int? year = null) =>
            SendAsync<LeaveBalanceDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/balance/{employeeId}", ("year", year?.ToString()))));

        public Task<ApiResult<LeaveRequestDto>> CreateAsync(CreateLeaveRequestDto dto) =>
            SendAsync<LeaveRequestDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<LeaveRequestDto>> UpdateAsync(int id, UpdateLeaveRequestDto dto) =>
            SendAsync<LeaveRequestDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<LeaveRequestDto>> DecideAsync(int id, bool approve, string notes) =>
            SendAsync<LeaveRequestDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/{id}/decide",
                new DecideLeaveRequestDto { Approve = approve, Notes = notes }));

        public Task<ApiResult<LeaveRequestDto>> CancelAsync(int id, string reason) =>
            SendAsync<LeaveRequestDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/{id}/cancel", new ReasonDto { Reason = reason }));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));
    }

    /// <summary>Notes kept against a member, and suspending a membership.</summary>
    public class MemberNoteApiService : ApiServiceBase
    {
        private const string BasePath = "api/members";

        public MemberNoteApiService(HttpClient http, IApiFailureSink? failures = null)
            : base(http, failures) { }

        public Task<ApiResult<List<MemberNoteDto>>> GetNotesAsync(int memberId) =>
            SendAsync<List<MemberNoteDto>>(() => Http.GetAsync($"{BasePath}/{memberId}/notes"));

        public Task<ApiResult<MemberNoteDto>> AddNoteAsync(int memberId, CreateMemberNoteDto dto) =>
            SendAsync<MemberNoteDto>(() => Http.PostAsJsonAsync($"{BasePath}/{memberId}/notes", dto));

        public Task<ApiResult<MemberNoteDto>> UpdateNoteAsync(int noteId, CreateMemberNoteDto dto) =>
            SendAsync<MemberNoteDto>(() => Http.PutAsJsonAsync($"{BasePath}/notes/{noteId}", dto));

        public Task<ApiResult<object>> DeleteNoteAsync(int noteId) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/notes/{noteId}"));

        public Task<ApiResult<MemberDto>> SuspendAsync(int memberId, SuspendMemberDto dto) =>
            SendAsync<MemberDto>(() => Http.PostAsJsonAsync($"{BasePath}/{memberId}/suspend", dto));

        public Task<ApiResult<MemberDto>> ReactivateAsync(int memberId) =>
            SendAsync<MemberDto>(() => Http.PostAsync($"{BasePath}/{memberId}/reactivate", null));
    }

    /// <summary>
    /// Platform administration, for the Super Admin alone.
    ///
    /// Every endpoint behind this is guarded three times over on the server - the module, the
    /// Super Admin role, and the deployment's cross-tenant flag - so a tenant user who somehow
    /// reached this code would still be refused.
    /// </summary>
    public class PlatformApiService : ApiServiceBase
    {
        private const string BasePath = "api/platform";

        public PlatformApiService(HttpClient http, IApiFailureSink? failures = null)
            : base(http, failures) { }

        // ------------------------------------------------------------------ tenants

        public Task<ApiResult<List<TenantDto>>> GetTenantsAsync() =>
            SendAsync<List<TenantDto>>(() => Http.GetAsync($"{BasePath}/tenants"));

        public Task<ApiResult<TenantDto>> CreateTenantAsync(CreateTenantDto dto) =>
            SendAsync<TenantDto>(() => Http.PostAsJsonAsync($"{BasePath}/tenants", dto));

        public Task<ApiResult<TenantDto>> UpdateTenantAsync(int companyId, UpdateTenantDto dto) =>
            SendAsync<TenantDto>(() => Http.PutAsJsonAsync($"{BasePath}/tenants/{companyId}", dto));

        public Task<ApiResult<TenantDto>> SetTierAsync(int companyId, int tier, string reason) =>
            SendAsync<TenantDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/tenants/{companyId}/tier",
                new SetTierDto { Tier = tier, Reason = reason }));

        public Task<ApiResult<TenantDto>> SetTenantStatusAsync(
            int companyId, bool isActive, string reason) =>
            SendAsync<TenantDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/tenants/{companyId}/status",
                new SetTenantStatusDto { IsActive = isActive, Reason = reason }));

        public Task<ApiResult<ProvisioningResultDto>> ProvisionAsync(int companyId) =>
            SendAsync<ProvisioningResultDto>(() => Http.PostAsync(
                $"{BasePath}/tenants/{companyId}/provision", null));

        // ------------------------------------------------------------------ subscriptions

        public Task<ApiResult<List<SubscriptionPlanDto>>> GetPlansAsync() =>
            SendAsync<List<SubscriptionPlanDto>>(() => Http.GetAsync($"{BasePath}/plans"));

        public Task<ApiResult<SubscriptionPlanDto>> CreatePlanAsync(CreateSubscriptionPlanDto dto) =>
            SendAsync<SubscriptionPlanDto>(() => Http.PostAsJsonAsync($"{BasePath}/plans", dto));

        public Task<ApiResult<SubscriptionPlanDto>> UpdatePlanAsync(
            int planId, UpdateSubscriptionPlanDto dto) =>
            SendAsync<SubscriptionPlanDto>(() => Http.PutAsJsonAsync($"{BasePath}/plans/{planId}", dto));

        public Task<ApiResult<object>> DeletePlanAsync(int planId) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/plans/{planId}"));

        public Task<ApiResult<List<CompanySubscriptionDto>>> GetSubscriptionsAsync(
            int? companyId = null, string? status = null) =>
            SendAsync<List<CompanySubscriptionDto>>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/subscriptions",
                ("companyId", companyId?.ToString()), ("status", status))));

        public Task<ApiResult<CompanySubscriptionDto>> SubscribeAsync(SubscribeDto dto) =>
            SendAsync<CompanySubscriptionDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/subscriptions", dto));

        public Task<ApiResult<CompanySubscriptionDto>> RenewSubscriptionAsync(int id) =>
            SendAsync<CompanySubscriptionDto>(() => Http.PostAsync(
                $"{BasePath}/subscriptions/{id}/renew", null));

        public Task<ApiResult<CompanySubscriptionDto>> CancelSubscriptionAsync(int id, string reason) =>
            SendAsync<CompanySubscriptionDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/subscriptions/{id}/cancel", new ReasonDto { Reason = reason }));

        // ------------------------------------------------------------------ accounts

        public Task<ApiResult<List<PlatformUserDto>>> GetUsersAsync(
            int? companyId = null, string? search = null) =>
            SendAsync<List<PlatformUserDto>>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/users",
                ("companyId", companyId?.ToString()), ("search", search))));

        public Task<ApiResult<PlatformUserDto>> SetUserStatusAsync(int appUserId, bool isActive) =>
            SendAsync<PlatformUserDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/users/{appUserId}/status",
                new UpdateUserStatusRequestDto { IsActive = isActive }));

        public Task<ApiResult<PlatformUserDto>> ResetPasswordAsync(int appUserId, string password) =>
            SendAsync<PlatformUserDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/users/{appUserId}/reset-password",
                new ResetPasswordRequestDto { NewPassword = password }));

        // ------------------------------------------------------------------ settings and health

        public Task<ApiResult<List<SettingDto>>> GetSettingsAsync() =>
            SendAsync<List<SettingDto>>(() => Http.GetAsync($"{BasePath}/settings"));

        public Task<ApiResult<List<SettingDto>>> UpdateSettingsAsync(Dictionary<string, string> values) =>
            SendAsync<List<SettingDto>>(() => Http.PutAsJsonAsync(
                $"{BasePath}/settings", new UpdateSettingsDto { Values = values }));

        public Task<ApiResult<List<TenantHealthDto>>> GetHealthAsync() =>
            SendAsync<List<TenantHealthDto>>(() => Http.GetAsync($"{BasePath}/health"));

        public Task<ApiResult<List<AuditEventDto>>> GetAuditAsync(
            DateTime? from = null, DateTime? to = null,
            string? action = null, int? companyId = null, int take = 300) =>
            SendAsync<List<AuditEventDto>>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/audit",
                ("from", QueryString.Date(from)),
                ("to", QueryString.Date(to)),
                ("action", action),
                ("companyId", companyId?.ToString()),
                ("take", take.ToString()))));

        // ------------------------------------------------------------------ analytics

        public Task<ApiResult<AnalyticsViewDto>> GetDashboardAsync(
            DateTime? from = null, DateTime? to = null) =>
            SendAsync<AnalyticsViewDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/dashboard",
                ("from", QueryString.Date(from)), ("to", QueryString.Date(to)))));

        public Task<ApiResult<AnalyticsViewDto>> GetAnalyticsAsync(
            DateTime? from = null, DateTime? to = null) =>
            SendAsync<AnalyticsViewDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/analytics",
                ("from", QueryString.Date(from)), ("to", QueryString.Date(to)))));
    }

    /// <summary>Reads the tenant's own consistency sweep. There is nothing to write here.</summary>
    public class DataIntegrityApiService : ApiServiceBase
    {
        private const string BasePath = "api/data-integrity";

        public DataIntegrityApiService(HttpClient http, IApiFailureSink? failures = null)
            : base(http, failures) { }

        public Task<ApiResult<IntegrityReportDto>> RunAsync() =>
            SendAsync<IntegrityReportDto>(() => Http.GetAsync(BasePath));
    }
}
