using System.Security.Claims;
using ERP_UI.DTOs;
using Microsoft.AspNetCore.Components.Authorization;

namespace ERP_UI.Services
{
    /// <summary>
    /// Publishes the signed-in user to Blazor's authorization system, so
    /// <c>&lt;AuthorizeView&gt;</c> and <c>[Authorize]</c> on a page work the usual way.
    ///
    /// The principal is built from what <c>/api/auth/me</c> returned rather than by decoding
    /// the token in the browser. The difference matters: the browser copy is only ever used to
    /// decide what to draw, and the server rebuilds the same facts from the database on every
    /// request before it will act on them.
    /// </summary>
    public class FitCoreAuthStateProvider : AuthenticationStateProvider
    {
        private static readonly AuthenticationState SignedOut =
            new(new ClaimsPrincipal(new ClaimsIdentity()));

        private AuthenticationState _state = SignedOut;

        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(_state);

        public void SetUser(CurrentUserDto? user)
        {
            _state = user is null ? SignedOut : new AuthenticationState(Build(user));
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }

        private static ClaimsPrincipal Build(CurrentUserDto user)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.AppUserId.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Role, user.RoleKey),
                new("full_name", user.FullName),
                new("company_id", user.CompanyId.ToString()),
                new("company_name", user.CompanyName),
                new("enterprise_tier", user.EnterpriseTier),
                new("role_key", user.RoleKey),
                new("role_display", user.RoleDisplayName)
            };

            claims.AddRange(user.Modules.Select(module => new Claim("module", module)));

            // The authentication type must be non-empty or ClaimsIdentity reports the user as
            // unauthenticated however many claims it carries.
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "FitCore"));
        }
    }
}
