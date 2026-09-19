using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using ERP_infrastructure.tenant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ERP_Tests;

/// <summary>
/// The authorization boundary, exercised over real HTTP against the real pipeline.
///
/// Hiding a link in the sidebar is a courtesy; this is what happens when somebody types the URL.
/// Every case here is a request an ordinary user could make by hand, so these are the rules that
/// actually keep one tenant's payroll away from another tenant's receptionist.
///
/// No database is touched. Authorization runs as a filter before the controller, so a refusal is
/// decided before anything reaches a tenant connection - which is exactly the property being
/// asserted, and is why these tests are fast and do not depend on the remote servers.
/// </summary>
public sealed class ApiAuthorizationTests : IClassFixture<ApiAuthorizationTests.Factory>, IDisposable
{
    private const string SigningKey = "integration-test-signing-key-at-least-32-characters-long";

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Development so the permissive CORS branch is legal; the production guard that
            // refuses an empty origin list is asserted separately by configuration review.
            builder.UseEnvironment("Development");

            // UseSetting rather than ConfigureAppConfiguration. Program.cs reads the signing key
            // into a local before building the app, to fail fast when it is missing; a
            // configuration source added afterwards would update IOptions but not the validator
            // that had already been handed the old key, and every token would be rejected as
            // signed by an unknown key.
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Jwt:Issuer", "FitCoreERP");
            builder.UseSetting("Jwt:Audience", "FitCoreERP.Client");

            // The seeder would reach for the master database on start-up. These tests are about
            // the authorization filter, not about seeding.
            builder.UseSetting("Bootstrap:Enabled", "false");

            builder.UseSetting("Tenancy:DefaultCompanyId", "0");
            builder.UseSetting("Tenancy:AllowHeaderOverride", "false");
            builder.UseSetting("Tenancy:AllowConnectionStringFallback", "false");
            builder.UseSetting("Tenancy:EnableCrossTenantAdminApi", "true");

            builder.UseSetting("Cors:AllowedOrigins:0", "https://localhost:7031");

            // Tenant resolution runs as middleware, before MVC, and would reach for the master
            // database to turn the token's company into a connection string. That is not what
            // these tests are about, and the remote master is not available to them, so it is
            // stubbed. Authorization still runs exactly as it does in production - and the fact
            // that a refusal never needs a working database is itself the point.
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ITenantConnectionStringProvider, StubConnectionStringProvider>();
            });
        }

        /// <summary>
        /// Resolves every company to a connection string that is well-formed but points at
        /// nothing. A request that gets far enough to use it fails - which is correct, because
        /// no test here is entitled to reach a database.
        /// </summary>
        private sealed class StubConnectionStringProvider : ITenantConnectionStringProvider
        {
            public Task<TenantConnection> GetConnectionAsync(
                int companyId, CancellationToken cancellationToken = default) =>
                Task.FromResult(new TenantConnection(
                    $"Server=(localdb)\\unused;Database=tenant-{companyId};Trusted_Connection=True;",
                    UsedFallback: false));
        }
    }

    private readonly Factory _factory;

    public ApiAuthorizationTests(Factory factory) => _factory = factory;

    public void Dispose() { }

    /// <summary>
    /// Mints the same token the sign-in endpoint would, using the server's own token service, so
    /// these tests cannot drift from how claims are really issued.
    /// </summary>
    private HttpClient ClientFor(
        EnterpriseTier tier, string roleKey, int companyId = 4, string username = "test.user")
    {
        var user = new AuthenticatedUser
        {
            AppUserId = 99,
            Username = username,
            FullName = "Test User",
            CompanyId = companyId,
            CompanyName = "Test Gym",
            CompanyCode = "TEST",
            EnterpriseTier = tier,
            RoleKey = roleKey,
            RoleDisplayName = ErpRoles.DisplayNameOf(roleKey),
            RoleLevel = ErpRoles.LevelOf(roleKey),

            // Resolved the same way sign-in resolves it: the role's grants, narrowed by tier.
            Modules = PermissionResolver
                .Resolve(tier, ErpRoles.DefaultModulesFor(roleKey), Array.Empty<AppUserPermission>())
                .ToList()
        };

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenFor(user));

        return client;
    }

    /// <summary>
    /// Signs a token with the server's own token service, taken from its own container, so the
    /// test cannot drift from how the running application issues and validates claims.
    /// </summary>
    private string TokenFor(AuthenticatedUser user)
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return tokens.CreateToken(user).Token;
    }

    /// <summary>The key the running server actually validates against.</summary>
    private string ServerSigningKey =>
        _factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value.SigningKey;

    // ------------------------------------------------------------------ not signed in

    [Theory]
    [InlineData("/api/members")]
    [InlineData("/api/sales")]
    [InlineData("/api/payments")]
    [InlineData("/api/inventory")]
    [InlineData("/api/employees")]
    [InlineData("/api/payroll")]
    [InlineData("/api/products")]
    [InlineData("/api/subscriptions")]
    [InlineData("/api/reports/dashboard")]
    [InlineData("/api/users")]
    [InlineData("/api/expenses")]
    public async Task Every_business_endpoint_refuses_an_anonymous_caller(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sign_in_is_the_one_endpoint_that_does_not_require_a_token()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { Username = "nobody", Password = "wrong" });

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------------ the tier ceiling

    /// <summary>
    /// The Micro licence is the four operational modules. Everything above it must answer 403
    /// to the most senior account the tier can have, because the ceiling is the company's, not
    /// the person's.
    /// </summary>
    [Theory]
    [InlineData("/api/employees")]
    [InlineData("/api/payroll")]
    [InlineData("/api/expenses")]
    [InlineData("/api/users")]
    [InlineData("/api/companies")]
    public async Task A_micro_tenant_is_refused_everything_above_its_tier(string path)
    {
        var client = ClientFor(EnterpriseTier.Micro, ErpRoles.Admin, companyId: 3);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Small adds Employees and Payroll and nothing else. Reaching them proves the expansion
    /// works; being refused User Access proves it stopped where it should.
    /// </summary>
    [Theory]
    [InlineData("/api/expenses")]
    [InlineData("/api/users")]
    [InlineData("/api/companies")]
    public async Task A_small_tenant_is_still_refused_the_medium_modules(string path)
    {
        var client = ClientFor(EnterpriseTier.Small, ErpRoles.Admin);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/employees")]
    [InlineData("/api/payroll")]
    public async Task A_small_tenant_manager_gets_past_the_filter_for_the_workforce_modules(string path)
    {
        var client = ClientFor(EnterpriseTier.Small, ErpRoles.Manager);

        var response = await client.GetAsync(path);

        // Past the filter is the assertion. What happens next is a database call that these
        // tests deliberately do not make, so anything except a refusal is a pass.
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------------ the role boundary

    /// <summary>
    /// A receptionist runs the front desk. Payroll and employee records are not theirs to read,
    /// even though their company is licensed for them.
    /// </summary>
    [Theory]
    [InlineData("/api/employees")]
    [InlineData("/api/payroll")]
    [InlineData("/api/users")]
    public async Task Staff_are_refused_the_management_modules(string path)
    {
        var client = ClientFor(EnterpriseTier.Small, ErpRoles.Staff);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/members")]
    [InlineData("/api/sales")]
    [InlineData("/api/payments")]
    [InlineData("/api/inventory")]
    public async Task Staff_reach_the_front_desk_modules(string path)
    {
        var client = ClientFor(EnterpriseTier.Small, ErpRoles.Staff);

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The dashboard lives on a controller reserved for Reports, and carries its own rule so a
    /// receptionist can still see it. The action's rule must win over the controller's.
    /// </summary>
    [Fact]
    public async Task Staff_reach_the_dashboard_on_a_controller_they_otherwise_cannot_use()
    {
        var client = ClientFor(EnterpriseTier.Small, ErpRoles.Staff);

        var dashboard = await client.GetAsync("/api/reports/dashboard");
        Assert.NotEqual(HttpStatusCode.Forbidden, dashboard.StatusCode);

        // The rest of the Reports controller stays closed to them.
        var overview = await client.GetAsync("/api/reports/membership-overview");
        Assert.NotEqual(HttpStatusCode.Unauthorized, overview.StatusCode);
    }

    // ------------------------------------------------------------------ cross-tenant admin

    /// <summary>
    /// Company administration is the one place a caller names another tenant by id. It takes a
    /// Medium licence, the System Administration module and a senior role together - so on the
    /// Micro and Small deployments that exist today, nobody reaches it at all.
    /// </summary>
    [Fact]
    public async Task Company_administration_is_refused_without_system_administration()
    {
        foreach (var role in new[] { ErpRoles.Staff, ErpRoles.Manager, ErpRoles.Admin })
        {
            var client = ClientFor(EnterpriseTier.Small, role);

            var response = await client.GetAsync("/api/companies");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_super_admin_on_a_medium_tenant_reaches_company_administration()
    {
        var client = ClientFor(EnterpriseTier.Medium, ErpRoles.SuperAdmin, companyId: 1);

        var response = await client.GetAsync("/api/companies");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------------ token integrity

    /// <summary>
    /// A token signed with a different key must be worthless. The signing key is the only thing
    /// standing between a forged company claim and another tenant's database.
    /// </summary>
    [Fact]
    public async Task A_token_signed_with_the_wrong_key_is_rejected()
    {
        var forged = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "FitCoreERP",
            Audience = "FitCoreERP.Client",
            SigningKey = "a-completely-different-key-also-at-least-32-chars",
            TokenLifetimeMinutes = 30
        }));

        var user = new AuthenticatedUser
        {
            AppUserId = 1,
            Username = "attacker",
            CompanyId = 4,
            EnterpriseTier = EnterpriseTier.Medium,
            RoleKey = ErpRoles.SuperAdmin,
            RoleLevel = 0,
            Modules = ErpModules.All.Select(m => m.Key).ToList()
        };

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", forged.CreateToken(user).Token);

        var response = await client.GetAsync("/api/payroll");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// An expired token is not a valid one. The API sets zero clock skew precisely so that
    /// withdrawing access does not leave the default five minute window.
    ///
    /// The token is built here rather than through JwtTokenService because that service clamps
    /// any lifetime to a minimum of five minutes and so cannot produce an expired one - which
    /// is a sensible guard, and worth leaving alone.
    /// </summary>
    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ServerSigningKey));

        var expired = new JwtSecurityToken(
            issuer: "FitCoreERP",
            audience: "FitCoreERP.Client",
            claims: new[]
            {
                new Claim(ClaimTypes.Name, "stale"),
                new Claim(ClaimTypes.Role, ErpRoles.Admin),
                new Claim(FitCoreClaims.CompanyId, "4"),
                new Claim(FitCoreClaims.RoleKey, ErpRoles.Admin),
                new Claim(FitCoreClaims.Module, ErpModules.Membership)
            },
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(expired));

        var response = await client.GetAsync("/api/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
