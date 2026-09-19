using System.Security.Claims;
using ERP_api.Tenancy;
using ERP_infrastructure.tenant;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ERP_Tests;

public class TenantResolutionMiddlewareTests
{
    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "ERP_api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class StubConnectionProvider : ITenantConnectionStringProvider
    {
        public List<int> RequestedCompanyIds { get; } = new();

        public Task<TenantConnection> GetConnectionAsync(
            int companyId, CancellationToken cancellationToken = default)
        {
            RequestedCompanyIds.Add(companyId);
            return Task.FromResult(
                new TenantConnection($"Server=s{companyId};Database=d{companyId};", false));
        }
    }

    private static async Task<(TenantContext Tenant, StubConnectionProvider Provider)> RunAsync(
        TenantOptions options,
        string environmentName,
        Action<HttpContext> configureRequest)
    {
        var middleware = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            Options.Create(options),
            new StubEnvironment { EnvironmentName = environmentName },
            NullLogger<TenantResolutionMiddleware>.Instance);

        var httpContext = new DefaultHttpContext();
        configureRequest(httpContext);

        var tenant = new TenantContext();
        var provider = new StubConnectionProvider();

        await middleware.InvokeAsync(httpContext, tenant, provider);

        return (tenant, provider);
    }

    [Fact]
    public async Task Falls_back_to_the_configured_default_company()
    {
        var (tenant, _) = await RunAsync(
            new TenantOptions { DefaultCompanyId = 3 },
            Environments.Production,
            _ => { });

        Assert.True(tenant.IsResolved);
        Assert.Equal(3, tenant.CompanyId);
        Assert.Equal(TenantSource.Default, tenant.Source);
    }

    [Fact]
    public async Task Honours_the_header_in_development()
    {
        var (tenant, _) = await RunAsync(
            new TenantOptions { DefaultCompanyId = 3, AllowHeaderOverride = true },
            Environments.Development,
            ctx => ctx.Request.Headers["X-Company-Id"] = "9");

        Assert.Equal(9, tenant.CompanyId);
        Assert.Equal(TenantSource.Header, tenant.Source);
    }

    [Fact]
    public async Task Ignores_the_header_outside_development()
    {
        // The security property: a client must not be able to select another company's
        // database by sending a header at a deployed environment.
        var (tenant, _) = await RunAsync(
            new TenantOptions { DefaultCompanyId = 3, AllowHeaderOverride = true },
            Environments.Production,
            ctx => ctx.Request.Headers["X-Company-Id"] = "9");

        Assert.Equal(3, tenant.CompanyId);
        Assert.Equal(TenantSource.Default, tenant.Source);
    }

    [Fact]
    public async Task Ignores_the_header_when_the_option_is_off_even_in_development()
    {
        var (tenant, _) = await RunAsync(
            new TenantOptions { DefaultCompanyId = 3, AllowHeaderOverride = false },
            Environments.Development,
            ctx => ctx.Request.Headers["X-Company-Id"] = "9");

        Assert.Equal(3, tenant.CompanyId);
        Assert.Equal(TenantSource.Default, tenant.Source);
    }

    [Fact]
    public async Task A_company_claim_wins_over_the_header_and_the_default()
    {
        var (tenant, _) = await RunAsync(
            new TenantOptions { DefaultCompanyId = 3, AllowHeaderOverride = true },
            Environments.Development,
            ctx =>
            {
                ctx.Request.Headers["X-Company-Id"] = "9";
                ctx.User = new ClaimsPrincipal(
                    new ClaimsIdentity(new[] { new Claim("company_id", "11") }, "TestAuth"));
            });

        Assert.Equal(11, tenant.CompanyId);
        Assert.Equal(TenantSource.Claim, tenant.Source);
    }

    [Fact]
    public async Task A_claim_on_an_unauthenticated_principal_is_ignored()
    {
        var (tenant, _) = await RunAsync(
            new TenantOptions { DefaultCompanyId = 3 },
            Environments.Production,
            ctx => ctx.User = new ClaimsPrincipal(
                // No authentication type means IsAuthenticated is false.
                new ClaimsIdentity(new[] { new Claim("company_id", "11") })));

        Assert.Equal(3, tenant.CompanyId);
        Assert.Equal(TenantSource.Default, tenant.Source);
    }

    [Fact]
    public async Task No_tenant_is_established_when_there_is_no_default()
    {
        var (tenant, provider) = await RunAsync(
            new TenantOptions { DefaultCompanyId = 0 },
            Environments.Production,
            _ => { });

        Assert.False(tenant.IsResolved);
        Assert.Empty(provider.RequestedCompanyIds);
    }
}
