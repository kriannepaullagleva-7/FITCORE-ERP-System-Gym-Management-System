using System.Net.Http.Json;

namespace ERP_Project1.Api
{
    /// <summary>
    /// Talks to the Members endpoints of ERP_api. The company is decided by the server from
    /// the caller context, so no tenant identifier is sent from the browser.
    /// </summary>
    public class MemberApiService : ApiServiceBase
    {
        private const string BasePath = "api/members";

        public MemberApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures)
        {
        }

        public Task<ApiResult<List<MemberDto>>> GetAllAsync() =>
            SendAsync<List<MemberDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<List<MemberDto>>> SearchAsync(string term) =>
            SendAsync<List<MemberDto>>(() =>
                Http.GetAsync($"{BasePath}/search?term={Uri.EscapeDataString(term)}"));

        public Task<ApiResult<MemberDto>> GetByIdAsync(int id) =>
            SendAsync<MemberDto>(() => Http.GetAsync($"{BasePath}/{id}"));

        public Task<ApiResult<MemberDto>> CreateAsync(CreateMemberDto dto) =>
            SendAsync<MemberDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<MemberDto>> UpdateAsync(int id, UpdateMemberDto dto) =>
            SendAsync<MemberDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));

        /// <summary>
        /// Retires a member without touching their subscriptions or payments. This is what the
        /// Members screen offers when a delete is refused because the member has history.
        /// </summary>
        public Task<ApiResult<MemberDto>> ArchiveAsync(int id) =>
            SendAsync<MemberDto>(() => Http.PostAsync($"{BasePath}/{id}/archive", null));

        public Task<ApiResult<MemberDto>> RestoreAsync(int id) =>
            SendAsync<MemberDto>(() => Http.PostAsync($"{BasePath}/{id}/restore", null));

        // ------------------------------------------------------------------ history

        /// <summary>
        /// One member's own records, read through the Members endpoints rather than by asking
        /// for every subscription in the gym and filtering here. The server already knows how
        /// to answer "this member's", and filtering client-side would pull the whole tenant's
        /// history across the wire to show one person's.
        /// </summary>
        public Task<ApiResult<List<SubscriptionDto>>> GetSubscriptionsAsync(int memberId) =>
            SendAsync<List<SubscriptionDto>>(() => Http.GetAsync($"{BasePath}/{memberId}/subscriptions"));

        public Task<ApiResult<List<PaymentViewDto>>> GetPaymentsAsync(int memberId) =>
            SendAsync<List<PaymentViewDto>>(() => Http.GetAsync($"{BasePath}/{memberId}/payments"));

        public Task<ApiResult<List<SaleDto>>> GetSalesAsync(int memberId) =>
            SendAsync<List<SaleDto>>(() => Http.GetAsync($"{BasePath}/{memberId}/sales"));
    }
}
