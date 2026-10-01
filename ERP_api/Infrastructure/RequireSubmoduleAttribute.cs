using System.Security.Claims;
using ERP_domain.entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP_api.Infrastructure
{
    /// <summary>
    /// Refuses a request unless the signed-in user may use the named subfeature.
    ///
    /// <see cref="RequireModuleAttribute"/> guards one of the nine business modules. This
    /// guards a subfeature underneath one, and exists because a module and its contents do not
    /// always share a licence or a seniority. Business Intelligence is available to every
    /// tenant, but its cross-module analytics are a Medium feature; System Administration is
    /// available to every tenant, but in-app user management is Medium and platform
    /// administration is the Super Admin's alone.
    ///
    /// Nothing extra travels in the token for this. The three facts that decide it - the
    /// parent module, the company's tier and the user's role level - are already claims the
    /// server signed at sign-in, so the check is a pure function of what is already there and
    /// a client cannot add to it.
    ///
    /// Put this on the action, not the controller, when only some of a controller's endpoints
    /// are restricted. It composes with <see cref="RequireModuleAttribute"/>: both run, and
    /// both must pass.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RequireSubmoduleAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string _submodule;

        public RequireSubmoduleAttribute(string submodule)
        {
            _submodule = submodule;
        }

        public string Submodule => _submodule;

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (context.ActionDescriptor.EndpointMetadata
                    .OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any())
            {
                return;
            }

            // The same most-specific-wins rule RequireModule uses: metadata runs least to most
            // specific, so a controller-wide subfeature rule stands aside for the one on the
            // action.
            var rules = context.ActionDescriptor.EndpointMetadata
                .OfType<RequireSubmoduleAttribute>()
                .ToList();

            if (rules.Count > 1 && !ReferenceEquals(rules[^1], this)) return;

            var user = context.HttpContext.User;

            if (user?.Identity?.IsAuthenticated != true)
            {
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

            var definition = ErpModules.FindSubmodule(_submodule);

            if (definition is null)
            {
                // A typo in an attribute must fail closed rather than wave the request through.
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Access denied",
                    Detail = "This feature is not available."
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };

                return;
            }

            var tier = user.GetEnterpriseTier();
            var roleLevel = user.GetRoleLevel();

            if (ErpModules.IsSubmoduleAllowed(_submodule, tier, roleLevel, user.HasModule)) return;

            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<RequireSubmoduleAttribute>();

            logger.LogWarning(
                "Refused {Method} {Path} for {User}: {Submodule} needs {Tier} and role level {Level} or better.",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
                user.FindFirstValue(ClaimTypes.Name) ?? "unknown",
                _submodule,
                definition.MinimumTier,
                definition.MinimumRoleLevel);

            var module = ErpModules.Find(definition.ModuleKey)?.DisplayName ?? definition.ModuleKey;

            // Says which subfeature and why in general terms. It never reveals whether a
            // record exists, nor anything about another tenant.
            var reason = definition.MinimumTier > tier
                ? $"{definition.DisplayName} is part of the {definition.MinimumTier} Enterprise plan."
                : roleLevel > definition.MinimumRoleLevel
                    ? $"{definition.DisplayName} is restricted to " +
                      $"{ErpRoles.DisplayNameOf(RoleKeyForLevel(definition.MinimumRoleLevel))} and above."
                    : $"Your account does not have access to {module}.";

            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Access denied",
                Detail = reason + " Ask an administrator if you need it."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        private static string RoleKeyForLevel(int level) => level switch
        {
            0 => ErpRoles.SuperAdmin,
            1 => ErpRoles.Admin,
            2 => ErpRoles.Manager,
            _ => ErpRoles.Staff
        };
    }
}
