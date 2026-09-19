using System.Net.Http.Json;
using ERP_UI.DTOs;

namespace ERP_UI.Services
{
    /// <summary>
    /// Talks to the Members endpoints of ERP_api. The company is decided by the server from
    /// the caller context, so no tenant identifier is sent from the browser.
    /// </summary>
    public class MemberApiService : ApiServiceBase
    {
        private const string BasePath = "api/members";

        public MemberApiService(HttpClient http) : base(http)
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
    }
}
