using System.Security.Claims;
using ERP_infrastructure.tenant;
using Microsoft.Extensions.Options;

namespace ERP_api.Tenancy
{
    /// <summary>
    /// Decides, once per request and entirely on the server, which tenant database the request
    /// will be served from.
    ///
    /// Order of precedence:
    ///   1. A company claim on the authenticated user. This is the authoritative source and
    ///      becomes the only one that matters once JWT authentication is switched on.
    ///   2. A request header, but only while running in Development. A client must never be
    ///      able to pick another company's database in a deployed environment, so this is
    ///      hard-gated on the environment as well as on configuration.
    ///   3. The configured default company, which is what keeps the current single-tenant
    ///      deployment working.
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

            var connection = await connectionStringProvider.GetConnectionAsync(
                companyId, context.RequestAborted);

            tenantSetter.Set(companyId, connection.ConnectionString, source, connection.UsedFallback);

            _logger.LogDebug(
                "Request {Path} resolved to company {CompanyId} via {Source} (fallback: {UsedFallback}).",
                context.Request.Path, companyId, source, connection.UsedFallback);

            await _next(context);
        }

        private (int CompanyId, TenantSource Source) DetermineCompany(HttpContext context)
        {
            var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;

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
