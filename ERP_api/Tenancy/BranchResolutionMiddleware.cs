using System.Security.Claims;
using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using ERP_infrastructure.tenant;

namespace ERP_api.Tenancy
{
    /// <summary>
    /// Decides, once per request and entirely on the server, which branch the caller is looking
    /// at. Runs after tenant resolution, because a branch only means anything inside a tenant.
    ///
    /// Order of precedence, and the first rule is the load-bearing one:
    ///
    ///   1. The branch claim on the token. An account bound to a branch is narrowed to it, full
    ///      stop. The header is not consulted, so a branch manager cannot widen their own view
    ///      by sending one, and cannot read a sibling branch by naming it.
    ///   2. The <c>X-Branch-Id</c> header, for an Admin/Owner of a Medium tenant whose own
    ///      account is not bound to a branch. This is the branch picker in the desktop topbar.
    ///      The value is checked against the tenant's own open branches, so a header naming a
    ///      branch that does not exist is refused rather than silently showing nothing.
    ///   3. Nothing, which means the whole company - every branch and the records that belong to
    ///      none. This is what a single-site tenant always gets, and what an Admin sees until
    ///      they pick a branch.
    ///
    /// Whatever is decided here goes into the scoped <see cref="IBranchContext"/>, which
    /// <c>TenantErpDbContext</c> turns into a query filter on every branch-scoped entity. No
    /// controller, service or repository participates in the narrowing, and none of them can
    /// forget to.
    /// </summary>
    public class BranchResolutionMiddleware
    {
        /// <summary>The branch picker's header. Only ever honoured for an unbound Admin/Owner.</summary>
        public const string HeaderName = "X-Branch-Id";

        private readonly RequestDelegate _next;
        private readonly ILogger<BranchResolutionMiddleware> _logger;

        public BranchResolutionMiddleware(
            RequestDelegate next, ILogger<BranchResolutionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            IBranchContextSetter branchSetter,
            ITenantContext tenantContext)
        {
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            // 1. Bound to a branch by the account itself. Signed, so not negotiable.
            if (TryGetInt(context.User, FitCoreClaims.BranchId, out var claimBranchId))
            {
                branchSetter.Set(claimBranchId, BranchSource.Claim);
                await _next(context);
                return;
            }

            // 2. A selection, but only from somebody entitled to make one.
            if (!context.Request.Headers.TryGetValue(HeaderName, out var raw) ||
                !int.TryParse(raw.FirstOrDefault(), out var requested))
            {
                await _next(context);
                return;
            }

            // A header asking for "the whole company" is the picker's "All branches" entry.
            if (requested <= 0)
            {
                await _next(context);
                return;
            }

            if (!MaySelectBranch(context.User))
            {
                _logger.LogWarning(
                    "{User} sent {Header} for {Path} but is not entitled to select a branch. " +
                    "The request was served unscoped.",
                    context.User.Identity?.Name, HeaderName, context.Request.Path);

                await _next(context);
                return;
            }

            if (!tenantContext.IsResolved)
            {
                await _next(context);
                return;
            }

            // Checked against the tenant's own branches rather than trusted. This is the only
            // database read this middleware ever does, and it happens solely on a request that
            // asked for a branch - a request that sends no header never pays for it, and never
            // causes the tenant DbContext to be constructed.
            var branches = context.RequestServices.GetRequiredService<IBranchService>();
            var selectable = await branches.GetSelectableBranchIdsAsync();

            if (!selectable.Contains(requested))
            {
                _logger.LogWarning(
                    "{User} asked for branch {BranchId}, which is not an open branch of company " +
                    "{CompanyId}.",
                    context.User.Identity?.Name, requested, tenantContext.CompanyId);

                await Problem(context,
                    "That branch does not belong to this company, or has been closed.");
                return;
            }

            branchSetter.Set(requested, BranchSource.Selection);

            await _next(context);
        }

        /// <summary>
        /// Who may point the whole application at one branch: an Admin/Owner, on a tier that has
        /// branches. The role check is the same one <c>[RequireSubmodule]</c> applies to branch
        /// administration, so the picker and the screen behind it cannot disagree.
        /// </summary>
        private static bool MaySelectBranch(ClaimsPrincipal user)
        {
            if (!TryGetInt(user, FitCoreClaims.RoleLevel, out var roleLevel)) return false;
            if (roleLevel > ErpRoles.LevelOf(ErpRoles.Admin)) return false;

            var tierName = user.FindFirstValue(FitCoreClaims.EnterpriseTier);

            return Enum.TryParse<EnterpriseTier>(tierName, ignoreCase: true, out var tier)
                && tier >= EnterpriseTier.Medium;
        }

        private static bool TryGetInt(ClaimsPrincipal? user, string claimType, out int value)
        {
            value = 0;

            var raw = user?.FindFirstValue(claimType);

            return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out value);
        }

        private static async Task Problem(HttpContext context, string detail)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                title = "Branch not available",
                status = StatusCodes.Status400BadRequest,
                detail
            });
        }
    }

    public static class BranchResolutionMiddlewareExtensions
    {
        public static IApplicationBuilder UseBranchResolution(this IApplicationBuilder app)
            => app.UseMiddleware<BranchResolutionMiddleware>();
    }
}
