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

            // Points the master database at nothing reachable, with a one second timeout. Sign-in
            // legitimately queries it, and these tests must not depend on a remote server being
            // up - nor wait a minute to find out that it is not.
            builder.UseSetting(
                "ConnectionStrings:MasterErp",
                "Server=tcp:127.0.0.1,1;Database=none;User Id=none;Password=none;" +
                "Encrypt=False;Connect Timeout=1;");

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
                    // A closed TCP port with a one second timeout, for the same reason the
                    // master connection above uses one: a request that is *supposed* to get
                    // past the filter then tries to open this, and the test only cares that it
                    // was not refused. Pointing at "(localdb)\unused" instead made each of
                    // those wait on SqlClient trying to start a LocalDB instance that does not
                    // exist, which is tens of seconds each and turned a one minute suite into
                    // a twenty minute one.
                    $"Server=tcp:127.0.0.1,1;Database=tenant-{companyId};User Id=none;" +
                    "Password=none;Encrypt=False;Connect Timeout=1;",
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
        EnterpriseTier tier, string roleKey, int companyId = 4, string username = "test.user",
        int? branchId = null)
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

            // A branch-bound account, when the test asks for one. Sign-in reads this from
            // AppUser.BranchId and the token service turns it into a signed claim, which is
            // what makes the scope non-negotiable for the accounts that carry it.
            BranchId = branchId,

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

    /// <summary>
    /// Sign-in has to be reachable without a token, or nobody could ever get one.
    ///
    /// Rejected credentials also answer 401, so the status alone proves nothing. What
    /// distinguishes them is which 401: the pipeline's "you are not signed in" challenge, or the
    /// endpoint's own verdict on the credentials it was given. Only the second means the request
    /// was let through.
    /// </summary>
    [Fact]
    public async Task Sign_in_is_the_one_endpoint_that_does_not_require_a_token()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { Username = "nobody", Password = "wrong" });

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("requires a signed-in FitCore user", body);
        Assert.DoesNotContain("Not signed in", body);
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
    /// Small is a whole single-site gym, books included. What it is refused is System
    /// Administration - accounts, roles, the audit trail and the branch network - which is the
    /// one module the Medium tier adds.
    /// </summary>
    [Theory]
    [InlineData("/api/users")]
    [InlineData("/api/branches")]
    [InlineData("/api/settings")]
    [InlineData("/api/audit-events")]
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
    public async Task Staff_reach_their_own_modules_data_on_a_controller_they_otherwise_cannot_use()
    {
        var client = ClientFor(EnterpriseTier.Small, ErpRoles.Staff);

        // The dashboard is Business Intelligence's landing page, and Staff hold no part of
        // Business Intelligence at all.
        var dashboard = await client.GetAsync("/api/reports/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, dashboard.StatusCode);

        // But this is the Subscriptions tab's own data, not a report - it is guarded on
        // Membership, which Staff hold, so authorization passes even though the rest of the
        // Reports controller is Business Intelligence's. This suite's tenant connection points
        // nowhere on purpose (see StubConnectionStringProvider), so a request cleared to reach
        // the database fails on that fake connection rather than on the authorization filter -
        // neither 401 nor 403 is what proves the point here.
        var overview = await client.GetAsync("/api/reports/membership-overview");
        Assert.NotEqual(HttpStatusCode.Unauthorized, overview.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, overview.StatusCode);
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

    // ------------------------------------------------------------------ the module reports

    /// <summary>
    /// Each module report is guarded by the module it reports on rather than by Business
    /// Intelligence, so that the desktop's per-module Reports tabs and the endpoints behind them
    /// answer to the same subfeature.
    ///
    /// A Micro tenant has no Employees or Payroll, so their reports must be refused however
    /// senior the caller - the report is a way of reading a module, not a way around the tier.
    /// </summary>
    [Theory]
    [InlineData("/api/reports/employees")]
    [InlineData("/api/reports/payroll")]
    [InlineData("/api/reports/expenses")]
    [InlineData("/api/reports/payment-reconciliation")]
    public async Task A_micro_tenant_is_refused_the_reports_of_modules_it_does_not_have(string path)
    {
        var client = ClientFor(EnterpriseTier.Micro, ErpRoles.Admin, companyId: 3);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The reports of the two modules Micro does have stay reachable for a manager. This is the
    /// half of the guard rule that must not have narrowed anything: a module report is gated on
    /// the module it reports on, not on Business Intelligence - which a Micro tenant no longer
    /// holds at all. Without that, moving Business Intelligence up to Small would have taken
    /// every report on a Micro tenant with it.
    /// </summary>
    [Theory]
    [InlineData("/api/reports/payments")]
    [InlineData("/api/reports/membership")]
    public async Task A_micro_tenant_manager_still_reaches_the_reports_of_its_own_modules(string path)
    {
        var client = ClientFor(EnterpriseTier.Micro, ErpRoles.Manager, companyId: 3);

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Reports are Manager and above on every tier. A receptionist holds Sales and Payments, but
    /// not the reporting over them - which is why the module alone is not the whole guard.
    /// </summary>
    [Theory]
    [InlineData("/api/reports/sales")]
    [InlineData("/api/reports/payments")]
    [InlineData("/api/reports/inventory")]
    [InlineData("/api/reports/membership")]
    [InlineData("/api/reports/employees")]
    [InlineData("/api/reports/payroll")]
    public async Task Staff_are_refused_the_module_reports_even_for_modules_they_hold(string path)
    {
        var client = ClientFor(EnterpriseTier.Small, ErpRoles.Staff);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/reports/employees")]
    [InlineData("/api/reports/payroll")]
    public async Task A_small_tenant_manager_reaches_the_workforce_reports(string path)
    {
        var client = ClientFor(EnterpriseTier.Small, ErpRoles.Manager);

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Expenses are a Finance subfeature, so the expense report is gated exactly as
    /// <c>/api/expenses</c> is. Left on the Business Intelligence rule it inherited from its
    /// controller, a manager without Finance could read every expense row through the report
    /// that the expenses endpoint refuses them.
    ///
    /// Asserted in both directions, because the two must move together: Micro has no Finance
    /// and is refused both, Small has Finance and is allowed both.
    /// </summary>
    [Fact]
    public async Task The_expense_report_is_refused_wherever_the_expenses_endpoint_is()
    {
        var micro = ClientFor(EnterpriseTier.Micro, ErpRoles.Admin, companyId: 3);

        Assert.Equal(HttpStatusCode.Forbidden, (await micro.GetAsync("/api/expenses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await micro.GetAsync("/api/reports/expenses")).StatusCode);

        var small = ClientFor(EnterpriseTier.Small, ErpRoles.Admin, companyId: 4);

        Assert.NotEqual(HttpStatusCode.Forbidden, (await small.GetAsync("/api/expenses")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await small.GetAsync("/api/reports/expenses")).StatusCode);
    }

    /// <summary>
    /// Payment reconciliation compares the takings against the general ledger, so it needs the
    /// ledger to exist. Small is where the ledger begins, so that is where reconciliation
    /// begins too - and a Micro tenant, which has neither, is refused it.
    /// </summary>
    [Fact]
    public async Task Payment_reconciliation_needs_the_ledger_and_so_needs_a_small_licence()
    {
        var micro = ClientFor(EnterpriseTier.Micro, ErpRoles.Admin, companyId: 3);
        var refused = await micro.GetAsync("/api/reports/payment-reconciliation");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // The rest of Payment Management is still theirs.
        var payments = await micro.GetAsync("/api/payments");
        Assert.NotEqual(HttpStatusCode.Forbidden, payments.StatusCode);

        var small = ClientFor(EnterpriseTier.Small, ErpRoles.Manager, companyId: 4);
        var allowed = await small.GetAsync("/api/reports/payment-reconciliation");
        Assert.NotEqual(HttpStatusCode.Forbidden, allowed.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, allowed.StatusCode);
    }

    /// <summary>
    /// Member history reads a member's subscriptions, payments and sales through the Members
    /// controller. All three are guarded on Member History rather than only on the module, so
    /// the desktop's History tab and the panels on it cannot disagree about who may open them.
    /// </summary>
    [Theory]
    [InlineData("/api/members/1/subscriptions")]
    [InlineData("/api/members/1/payments")]
    [InlineData("/api/members/1/sales")]
    public async Task Member_history_is_reachable_by_the_front_desk_that_owns_the_member(string path)
    {
        var client = ClientFor(EnterpriseTier.Micro, ErpRoles.Staff, companyId: 3);

        var response = await client.GetAsync(path);

        // Member History is a Micro, Staff-level subfeature: the person at the desk is the one
        // who gets asked when a charge is disputed.
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------------ platform subfeatures

    /// <summary>
    /// Platform settings and monitoring are the Super Admin's alone, at role level 0, which no
    /// tenant role reaches on any tier.
    /// </summary>
    [Theory]
    [InlineData("/api/platform/settings")]
    [InlineData("/api/platform/health")]
    public async Task Platform_settings_and_monitoring_are_refused_to_every_tenant_role(string path)
    {
        foreach (var role in new[] { ErpRoles.Staff, ErpRoles.Manager, ErpRoles.Admin })
        {
            var client = ClientFor(EnterpriseTier.Medium, role, companyId: 7);

            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    // ------------------------------------------------------------------ branching

    /// <summary>
    /// Branch administration is Medium, because System Administration is. A Small owner runs a
    /// whole gym and still cannot create a branch - which is the tier boundary doing its job
    /// rather than an oversight.
    /// </summary>
    [Theory]
    [InlineData(EnterpriseTier.Micro, 3)]
    [InlineData(EnterpriseTier.Small, 4)]
    public async Task Branch_administration_needs_the_medium_tier(EnterpriseTier tier, int companyId)
    {
        var client = ClientFor(tier, ErpRoles.Admin, companyId);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/branches")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/bi/branches")).StatusCode);
    }

    /// <summary>
    /// And it is the Admin/Owner's alone. A Manager runs a branch; they do not decide that it
    /// exists, and the endpoint refuses them whatever the desktop drew.
    /// </summary>
    [Theory]
    [InlineData("/api/branches")]
    [InlineData("/api/bi/branches")]
    public async Task Branch_administration_is_refused_to_a_manager_and_to_staff(string path)
    {
        foreach (var role in new[] { ErpRoles.Manager, ErpRoles.Staff })
        {
            var client = ClientFor(EnterpriseTier.Medium, role, companyId: 7);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }
    }

    /// <summary>
    /// A Medium Admin gets past the filter on both. Past the filter is the whole assertion -
    /// what happens next is a database call these tests deliberately do not make.
    /// </summary>
    [Theory]
    [InlineData("/api/branches")]
    [InlineData("/api/bi/branches")]
    public async Task A_medium_owner_reaches_branch_administration(string path)
    {
        var client = ClientFor(EnterpriseTier.Medium, ErpRoles.Admin, companyId: 7);

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The point of the branch claim: an account bound to a branch cannot widen or move its
    /// own scope by sending a header. The middleware reads the claim first and never consults
    /// the header for a caller that has one, so a branch manager asking for a sibling branch
    /// is served their own - and, here, is refused the screen outright anyway.
    /// </summary>
    [Fact]
    public async Task A_branch_bound_account_cannot_reach_another_branch_with_a_header()
    {
        var client = ClientFor(
            EnterpriseTier.Medium, ErpRoles.Manager, companyId: 7, branchId: 1);

        client.DefaultRequestHeaders.Add("X-Branch-Id", "2");

        // Refused on the subfeature, before the header is ever relevant. Defence in depth: the
        // claim would have overridden the header regardless.
        Assert.Equal(
            HttpStatusCode.Forbidden, (await client.GetAsync("/api/branches")).StatusCode);

        // And on an endpoint they *are* entitled to, the header does not widen anything - the
        // request is still served, scoped by their claim rather than by what they asked for.
        var members = await client.GetAsync("/api/members");
        Assert.NotEqual(HttpStatusCode.Forbidden, members.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, members.StatusCode);
    }
}
