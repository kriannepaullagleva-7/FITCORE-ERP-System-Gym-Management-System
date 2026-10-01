using System.Security.Claims;
using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.tenant;
using Microsoft.Extensions.Options;

namespace ERP_api.Tenancy
{
    /// <summary>
    /// Decides, once per request and entirely on the server, which tenant database the request
    /// will be served from.
    ///
    /// Order of precedence:
    ///   1. A Super Admin naming a tenant explicitly. Platform administration has to be able
    ///      to look inside a tenant - that is most of what it is for - and this is the only
    ///      supported way to do it. It takes the Super Admin role, which no tenant user can
    ///      hold, and the cross-tenant flag, which is off by default. Every use is logged.
    ///   2. The company claim on the authenticated user. Authoritative for everybody else, and
    ///      the reason an ordinary user cannot reach another tenant whatever they send.
    ///   3. A request header, but only while running in Development and only for an
    ///      unauthenticated caller. This is the OpenAPI-reference convenience.
    ///   4. The configured default company, which is what keeps a single-tenant deployment
    ///      working.
    ///
    /// The resolved connection string is placed in the scoped tenant context and never leaves
    /// the server.
    /// </summary>
    public class TenantResolutionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly TenantOptions _options;
        private readonly bool _isDevelopment;
        private readonly ILogger<TenantResolutionMiddleware> _logger;

        public TenantResolutionMiddleware(
            RequestDelegate next,
            IOptions<TenantOptions> options,
            IHostEnvironment environment,
            ILogger<TenantResolutionMiddleware> logger)
        {
            _next = next;
            _options = options.Value;
            _isDevelopment = environment.IsDevelopment();
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantContextSetter tenantSetter,
            ITenantConnectionStringProvider connectionStringProvider)
        {
            var (companyId, source) = DetermineCompany(context);

            if (source == TenantSource.None)
            {
                // No tenant could be determined. The request is allowed through: endpoints that
                // do not touch tenant data still work, and any that do will fail with a clear
                // TenantResolutionException rather than silently reading the wrong database.
                _logger.LogDebug(
                    "No tenant could be determined for {Path}; continuing without a tenant context.",
                    context.Request.Path);

                await _next(context);
                return;
            }

            try
            {
                var connection = await connectionStringProvider.GetConnectionAsync(
                    companyId, context.RequestAborted);

                tenantSetter.Set(companyId, connection.ConnectionString, source, connection.UsedFallback);

                _logger.LogDebug(
                    "Request {Path} resolved to company {CompanyId} via {Source} (fallback: {UsedFallback}).",
                    context.Request.Path, companyId, source, connection.UsedFallback);
            }
            catch (TenantResolutionException ex)
            {
                // A company with no usable database registration must not take down the
                // endpoints that never needed one.
                //
                // Resolution runs for every request, before the router knows which endpoint
                // was asked for, so failing here would answer 503 to sign-in, to the platform
                // administration screens, and to anything else that reads only the master
                // database. Carrying on without a tenant context keeps those working, and an
                // endpoint that genuinely needs tenant data still fails the moment it asks for
                // the DbContext - with the same message, from the same exception type.
                _logger.LogWarning(ex,
                    "Company {CompanyId} could not be resolved to a database for {Path}. " +
                    "Continuing without a tenant context; any endpoint that needs one will refuse.",
                    companyId, context.Request.Path);
            }

            await _next(context);
        }

        private (int CompanyId, TenantSource Source) DetermineCompany(HttpContext context)
        {
            var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;

            if (TryGetSuperAdminTenant(context, out var chosenCompanyId))
            {
                return (chosenCompanyId, TenantSource.Header);
            }

            if (TryGetFromClaims(context.User, out var claimCompanyId))
            {
                return (claimCompanyId, TenantSource.Claim);
            }

            // Once a caller has identified themselves, their token is the only thing that may
            // choose a tenant. Falling through to the header here would let a signed-in user of
            // one company read another company's database by adding a request header, which is
            // precisely the attack the claim-first ordering exists to prevent. A token with no
            // company claim is malformed, so it resolves to no tenant at all rather than to a
            // guess.
            if (isAuthenticated)
            {
                _logger.LogWarning(
                    "An authenticated request to {Path} carried no company claim. " +
                    "No tenant was resolved; header and default sources are not consulted for " +
                    "authenticated callers.",
                    context.Request.Path);

                return (0, TenantSource.None);
            }

            // Development-only escape hatch, for exercising the API against a chosen tenant from
            // the OpenAPI reference. It is unreachable for an authenticated caller because of
            // the check above, and requires both the environment and the option to agree, so it
            // cannot be switched on in production by configuration alone.
            if (_isDevelopment && _options.AllowHeaderOverride &&
                context.Request.Headers.TryGetValue(_options.HeaderName, out var headerValues) &&
                int.TryParse(headerValues.FirstOrDefault(), out var headerCompanyId))
            {
                _logger.LogWarning(
                    "Tenant for {Path} was taken from the {Header} header. This is a development " +
                    "convenience and must never be enabled in a deployed environment.",
                    context.Request.Path, _options.HeaderName);

                return (headerCompanyId, TenantSource.Header);
            }

            // Kept for single-tenant deployments and for tooling. It is configured to 0 - off -
            // now that every business endpoint requires a signed-in user, so an anonymous
            // request resolves to no tenant instead of silently being served a real company.
            if (_options.DefaultCompanyId > 0)
            {
                return (_options.DefaultCompanyId, TenantSource.Default);
            }

            return (0, TenantSource.None);
        }

        /// <summary>
        /// The Super Admin's controlled route into a tenant.
        ///
        /// Platform administration exists to look after tenants, so it has to be able to look
        /// inside one - to diagnose a gym's problem, to check a migration landed, to answer a
        /// support question. The alternative designs are worse: copying tenant data into the
        /// master database duplicates it and breaks isolation, and giving the Super Admin a
        /// second account per tenant means a password per tenant to manage and leak.
        ///
        /// Three things must all hold, and the role is the one that matters. Super Admin is
        /// level 0, is never a default for any tenant role, and cannot be reached by a
        /// permission grant - so no amount of misconfiguration inside a gym produces an account
        /// that can do this. The flag is a deployment-level switch, off by default. And every
        /// use is logged with the account that did it, because a platform operator reading
        /// somebody's member list should leave a trace.
        /// </summary>
        private bool TryGetSuperAdminTenant(HttpContext context, out int companyId)
        {
            companyId = 0;

            if (!_options.EnableCrossTenantAdminApi) return false;
            if (context.User?.Identity?.IsAuthenticated != true) return false;

            var roleKey = context.User.FindFirst(FitCoreClaims.RoleKey)?.Value;

            if (!string.Equals(roleKey, ErpRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!context.Request.Headers.TryGetValue(_options.HeaderName, out var headerValues) ||
                !int.TryParse(headerValues.FirstOrDefault(), out var requested) ||
                requested <= 0)
            {
                return false;
            }

            _logger.LogWarning(
                "Platform administrator {User} is reading company {CompanyId} at {Path}.",
                context.User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown",
                requested,
                context.Request.Path);

            companyId = requested;
            return true;
        }

        private bool TryGetFromClaims(ClaimsPrincipal? user, out int companyId)
        {
            companyId = 0;

            if (user?.Identity?.IsAuthenticated != true)
            {
                return false;
            }

            foreach (var claimType in _options.CompanyClaimTypes)
            {
                var value = user.FindFirst(claimType)?.Value;

                if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, out companyId))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static class TenantResolutionMiddlewareExtensions
    {
        /// <summary>
        /// Must run after authentication (so user claims are available) and before the
        /// endpoints that read tenant data.
        /// </summary>
        public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
            => app.UseMiddleware<TenantResolutionMiddleware>();
    }
}
