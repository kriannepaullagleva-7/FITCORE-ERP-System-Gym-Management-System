using System.Security.Claims;
using ERP_domain.entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP_api.Infrastructure
{
    /// <summary>
    /// Refuses a request unless the signed-in user holds the named module.
    ///
    /// This is the half of the permission system that actually matters. Hiding a link in the
    /// sidebar is a courtesy to the user; this is what happens when somebody calls
    /// <c>GET /api/payroll</c> by hand. A user without the module gets 403 whatever their
    /// client shows them.
    ///
    /// The module claims are read from the bearer token, which the server signed at sign-in
    /// after resolving the role, the per-user overrides and the company tier together. A client
    /// cannot add one.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RequireModuleAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string _module;

        public RequireModuleAttribute(string module)
        {
            _module = module;
        }

        /// <summary>The module this rule demands, exposed so the most-specific check can read it.</summary>
        public string Module => _module;

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            // An action can opt out, which is how the sign-in endpoints stay reachable on a
            // controller that is otherwise locked down.
            if (context.ActionDescriptor.EndpointMetadata
                    .OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any())
            {
                return;
            }

            // Both the controller's rule and the action's rule are registered as filters, and
            // both would otherwise run - so a controller marked Reports would still block an
            // action marked Dashboard, and the more specific rule would never be reached.
            //
            // Endpoint metadata is ordered least to most specific, so the last rule on the
            // endpoint is the action's. Every earlier one stands aside for it. That gives the
            // rule people expect when they read the code: the attribute nearest the method wins.
            var rules = context.ActionDescriptor.EndpointMetadata
                .OfType<RequireModuleAttribute>()
                .ToList();

            if (rules.Count > 1 && !ReferenceEquals(rules[^1], this))
            {
                return;
            }

            var user = context.HttpContext.User;

            if (user?.Identity?.IsAuthenticated != true)
            {
                // 401 rather than 403: the caller has not said who they are yet, so the right
                // answer is "sign in", not "you may not".
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Not signed in",
                    Detail = "This endpoint requires a signed-in FitCore user."
                })
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };

                return;
            }

            var hasModule = user.FindAll(FitCoreClaims.Module)
                .Any(claim => string.Equals(claim.Value, _module, StringComparison.OrdinalIgnoreCase));

            if (hasModule) return;

            var definition = ErpModules.Find(_module);
            var moduleName = definition?.DisplayName ?? _module;

            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<RequireModuleAttribute>();

            logger.LogWarning(
                "Refused {Method} {Path} for {User}: the {Module} module is not in their access.",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
                user.FindFirstValue(ClaimTypes.Name) ?? "unknown",
                _module);

            // Deliberately says which module is missing and nothing else. It never reveals
            // whether the record exists, nor anything about another tenant.
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Access denied",
                Detail = $"Your account does not have access to {moduleName}. " +
                         "Ask an administrator to grant it on the User Access screen."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }

    /// <summary>
    /// Convenience accessors for the FitCore claims, so controllers read the signed-in user the
    /// same way everywhere instead of each one parsing claims by hand.
    /// </summary>
    public static class ClaimsPrincipalExtensions
    {
        public static int GetAppUserId(this ClaimsPrincipal user) =>
            int.TryParse(user.FindFirstValue(FitCoreClaims.AppUserId), out var id) ? id : 0;

        public static int GetCompanyId(this ClaimsPrincipal user) =>
            int.TryParse(user.FindFirstValue(FitCoreClaims.CompanyId), out var id) ? id : 0;

        public static int GetRoleLevel(this ClaimsPrincipal user) =>
            int.TryParse(user.FindFirstValue(FitCoreClaims.RoleLevel), out var level) ? level : 99;

        public static string GetRoleKey(this ClaimsPrincipal user) =>
            user.FindFirstValue(FitCoreClaims.RoleKey) ?? "";

        public static string GetUsername(this ClaimsPrincipal user) =>
            user.FindFirstValue(ClaimTypes.Name) ?? "";

        public static bool HasModule(this ClaimsPrincipal user, string module) =>
            user.FindAll(FitCoreClaims.Module)
                .Any(c => string.Equals(c.Value, module, StringComparison.OrdinalIgnoreCase));
    }
}
