using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Blazored.LocalStorage;
using ERP_UI.DTOs;
using Microsoft.AspNetCore.Components.Authorization;

namespace ERP_UI.Services
{
    /// <summary>
    /// Sign-in, sign-out and the signed-in user, from the browser side.
    ///
    /// The token is the only thing kept in local storage. Everything the UI shows about the
    /// user - their name, role and which modules they can open - is read back from
    /// <c>/api/auth/me</c> on start-up rather than decoded from the token, so an account that
    /// has been changed or deactivated since the token was issued is noticed on the next page
    /// load instead of at the end of the token lifetime.
    /// </summary>
    public class AuthApiService : ApiServiceBase
    {
        private const string TokenKey = "fitcore.token";

        private readonly ILocalStorageService _storage;
        private readonly AuthenticationStateProvider _stateProvider;

        public AuthApiService(
            HttpClient http,
            ILocalStorageService storage,
            AuthenticationStateProvider stateProvider) : base(http)
        {
            _storage = storage;
            _stateProvider = stateProvider;
        }

        /// <summary>The user currently signed in, or null. Read by the shell and the pages.</summary>
        public CurrentUserDto? CurrentUser { get; private set; }

        public bool IsSignedIn => CurrentUser is not null;

        /// <summary>Raised when the signed-in user changes, so the shell can redraw.</summary>
        public event Action? Changed;

        public async Task<ApiResult<LoginResponseDto>> LoginAsync(string username, string password)
        {
            var result = await SendAsync<LoginResponseDto>(() =>
                Http.PostAsJsonAsync("api/auth/login", new LoginRequestDto
                {
                    Username = username,
                    Password = password
                }));

            if (!result.IsSuccess || result.Value is null) return result;

            await _storage.SetItemAsStringAsync(TokenKey, result.Value.Token);
            ApplyToken(result.Value.Token);

            CurrentUser = result.Value.User;
            NotifyChanged();

            return result;
        }

        /// <summary>
        /// Restores a session from local storage on start-up.
        ///
        /// The stored token is offered to the API rather than trusted on its own: if it has
        /// expired, been issued to an account that is now deactivated, or simply is not valid,
        /// the call fails and the session is cleared. Returns true only when a real, current
        /// user came back.
        /// </summary>
        public async Task<bool> RestoreSessionAsync()
        {
            var token = await _storage.GetItemAsStringAsync(TokenKey);

            if (string.IsNullOrWhiteSpace(token))
            {
                CurrentUser = null;
                NotifyChanged();
                return false;
            }

            ApplyToken(token);

            var me = await SendAsync<CurrentUserDto>(() => Http.GetAsync("api/auth/me"));

            if (!me.IsSuccess || me.Value is null)
            {
                await ClearAsync();
                return false;
            }

            CurrentUser = me.Value;
            NotifyChanged();
            return true;
        }

        /// <summary>
        /// Picks up a permission change without making the user sign out and back in.
        /// Called after an administrator edits their own access.
        /// </summary>
        public async Task<bool> RefreshAsync()
        {
            if (CurrentUser is null) return false;

            var result = await SendAsync<LoginResponseDto>(() =>
                Http.PostAsync("api/auth/refresh", null));

            if (!result.IsSuccess || result.Value is null) return false;

            await _storage.SetItemAsStringAsync(TokenKey, result.Value.Token);
            ApplyToken(result.Value.Token);

            CurrentUser = result.Value.User;
            NotifyChanged();
            return true;
        }

        public async Task<ApiResult<object>> ChangePasswordAsync(string current, string replacement)
        {
            var result = await SendAsync<object>(() =>
                Http.PostAsJsonAsync("api/auth/change-password", new ChangePasswordRequestDto
                {
                    CurrentPassword = current,
                    NewPassword = replacement
                }));

            if (result.IsSuccess && CurrentUser is not null)
            {
                CurrentUser.MustChangePassword = false;
                NotifyChanged();
            }

            return result;
        }

        public async Task LogoutAsync()
        {
            // Best effort: the token is a bearer token with no server session, so the sign-out
            // that matters is discarding it here. The call exists so the event is logged.
            if (CurrentUser is not null)
            {
                try { await Http.PostAsync("api/auth/logout", null); }
                catch (HttpRequestException) { /* signing out must work offline too */ }
            }

            await ClearAsync();
        }

        private async Task ClearAsync()
        {
            await _storage.RemoveItemAsync(TokenKey);
            Http.DefaultRequestHeaders.Authorization = null;
            CurrentUser = null;
            NotifyChanged();
        }

        private void ApplyToken(string token) =>
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        private void NotifyChanged()
        {
            if (_stateProvider is FitCoreAuthStateProvider provider)
            {
                provider.SetUser(CurrentUser);
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// True when the user may open the named module. The sidebar and the route guards use
        /// this; it is a convenience, not a security boundary - the API enforces the same rule
        /// again on every request and answers 403 regardless of what the client believes.
        /// </summary>
        public bool Can(string module) => CurrentUser?.Can(module) == true;
    }
}
