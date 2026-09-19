using System.Net.Http.Json;
using ERP_UI.DTOs;

namespace ERP_UI.Services
{
    /// <summary>
    /// Talks to the Membership Plans endpoints of ERP_api.
    /// </summary>
    public class MembershipPlanApiService : ApiServiceBase
    {
        private const string BasePath = "api/membership-plans";

        public MembershipPlanApiService(HttpClient http) : base(http)
        {
        }

        public Task<ApiResult<List<MembershipPlanDto>>> GetAllAsync() =>
            SendAsync<List<MembershipPlanDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<List<MembershipPlanDto>>> GetActiveAsync() =>
            SendAsync<List<MembershipPlanDto>>(() => Http.GetAsync($"{BasePath}/active"));

        public Task<ApiResult<MembershipPlanDto>> GetByIdAsync(int id) =>
            SendAsync<MembershipPlanDto>(() => Http.GetAsync($"{BasePath}/{id}"));

        public Task<ApiResult<MembershipPlanDto>> CreateAsync(CreateMembershipPlanDto dto) =>
            SendAsync<MembershipPlanDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<MembershipPlanDto>> UpdateAsync(int id, UpdateMembershipPlanDto dto) =>
            SendAsync<MembershipPlanDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));
    }
}
