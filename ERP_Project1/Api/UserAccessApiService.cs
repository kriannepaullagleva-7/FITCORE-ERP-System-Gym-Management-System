using System.Net.Http.Json;

namespace ERP_Project1.Api
{
    /// <summary>
    /// Talks to the User Access endpoints. No company id is sent: the server takes it from the
    /// caller's own token, so this screen can only ever manage accounts in the signed-in
    /// user's own company.
    /// </summary>
    public class UserAccessApiService : ApiServiceBase
    {
        private const string BasePath = "api/users";

        public UserAccessApiService(HttpClient http, IApiFailureSink? failures = null) : base(http, failures) { }

        public Task<ApiResult<List<UserAccountDto>>> GetAllAsync() =>
            SendAsync<List<UserAccountDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<UserAccountDto>> GetByIdAsync(int id) =>
            SendAsync<UserAccountDto>(() => Http.GetAsync($"{BasePath}/{id}"));

        public Task<ApiResult<List<RoleDto>>> GetRolesAsync() =>
            SendAsync<List<RoleDto>>(() => Http.GetAsync($"{BasePath}/roles"));

        public Task<ApiResult<UserPermissionEditorDto>> GetPermissionsAsync(int id) =>
            SendAsync<UserPermissionEditorDto>(() => Http.GetAsync($"{BasePath}/{id}/permissions"));

        public Task<ApiResult<UserPermissionEditorDto>> SetPermissionsAsync(int id, List<string> modules) =>
            SendAsync<UserPermissionEditorDto>(() => Http.PutAsJsonAsync(
                $"{BasePath}/{id}/permissions", new UpdateModuleAccessRequestDto { Modules = modules }));

        public Task<ApiResult<UserAccountDto>> CreateAsync(CreateUserRequestDto dto) =>
            SendAsync<UserAccountDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<UserAccountDto>> UpdateAsync(int id, UpdateUserRequestDto dto) =>
            SendAsync<UserAccountDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<UserAccountDto>> SetRoleAsync(int id, string roleKey) =>
            SendAsync<UserAccountDto>(() => Http.PatchAsJsonAsync(
                $"{BasePath}/{id}/role", new UpdateUserRoleRequestDto { RoleKey = roleKey }));

        public Task<ApiResult<UserAccountDto>> SetStatusAsync(int id, bool isActive) =>
            SendAsync<UserAccountDto>(() => Http.PatchAsJsonAsync(
                $"{BasePath}/{id}/status", new UpdateUserStatusRequestDto { IsActive = isActive }));

        public Task<ApiResult<object>> ResetPasswordAsync(int id, string newPassword) =>
            SendAsync<object>(() => Http.PostAsJsonAsync(
                $"{BasePath}/{id}/reset-password", new ResetPasswordRequestDto { NewPassword = newPassword }));
    }
}
