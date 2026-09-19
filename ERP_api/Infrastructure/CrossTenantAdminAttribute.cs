using ERP_infrastructure.tenant;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ERP_api.Infrastructure
{
    /// <summary>
    /// Marks an action that addresses a tenant by id taken from the URL rather than from the
    /// server-resolved tenant context.
    ///
    /// Ordinary business endpoints must never do this: a caller changing a number in a URL must
    /// not be able to read another company's data. These endpoints exist for SaaS
    /// administration and diagnostics, so they are refused unless
    /// Tenancy:EnableCrossTenantAdminApi is turned on. When authentication is added, this
    /// should be replaced by, or combined with, an administrator authorization policy.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class CrossTenantAdminAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            var options = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<TenantOptions>>().Value;

            if (!options.EnableCrossTenantAdminApi)
            {
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Cross-tenant administration is disabled",
                    Detail = "This endpoint addresses a tenant by id and is disabled. " +
                             "Enable Tenancy:EnableCrossTenantAdminApi on the server to use it."
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };

                return;
            }

            await next();
        }
    }
}
