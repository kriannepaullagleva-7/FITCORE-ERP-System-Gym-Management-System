using ERP_infrastructure.services;
using ERP_infrastructure.tenant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ERP_Tests;

public class TenantConnectionStringProviderTests
{
    /// <summary>Stands in for the master registry lookup.</summary>
    private sealed class StubResolver : ITenantDatabaseResolver
    {
        private readonly TenantDatabaseInfo? _info;
        private readonly Exception? _failure;

        private StubResolver(TenantDatabaseInfo? info, Exception? failure)
        {
            _info = info;
            _failure = failure;
        }

        public static StubResolver Returning(string server, string database, string credentialKey) =>
            new(new TenantDatabaseInfo
            {
                ServerName = server,
                DatabaseName = database,
                CredentialKey = credentialKey
            }, null);

        public static StubResolver Failing(string message) =>
            new(null, new InvalidOperationException(message));

        public Task<TenantDatabaseInfo> GetDatabaseInfoAsync(int companyId) =>
            _failure is not null
                ? Task.FromException<TenantDatabaseInfo>(_failure)
                : Task.FromResult(_info!);
    }

    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    private static TenantConnectionStringProvider Create(
        ITenantDatabaseResolver resolver, IConfiguration configuration, TenantOptions options) =>
        new(resolver, configuration, Options.Create(options),
            NullLogger<TenantConnectionStringProvider>.Instance);

    [Fact]
    public async Task Builds_the_connection_string_from_the_registry_and_credential_store()
    {
        var provider = Create(
            StubResolver.Returning("sql.example.net", "tenant_a", "KeyA"),
            Config(("TenantCredentials:KeyA:UserId", "user_a"),
                   ("TenantCredentials:KeyA:Password", "secret_a")),
            new TenantOptions());

        var result = await provider.GetConnectionAsync(7);

        Assert.False(result.UsedFallback);
        Assert.Contains("Server=sql.example.net;", result.ConnectionString);
        Assert.Contains("Database=tenant_a;", result.ConnectionString);
        Assert.Contains("User Id=user_a;", result.ConnectionString);
    }

    [Fact]
    public async Task Falls_back_to_the_configured_connection_string_when_allowed()
    {
        var provider = Create(
            StubResolver.Failing("no row"),
            Config(("ConnectionStrings:TenantErp", "Server=fallback;Database=fb;")),
            new TenantOptions { AllowConnectionStringFallback = true });

        var result = await provider.GetConnectionAsync(7);

        Assert.True(result.UsedFallback);
        Assert.Equal("Server=fallback;Database=fb;", result.ConnectionString);
    }

    [Fact]
    public async Task Throws_instead_of_falling_back_when_the_fallback_is_disabled()
    {
        var provider = Create(
            StubResolver.Failing("no row"),
            Config(("ConnectionStrings:TenantErp", "Server=fallback;Database=fb;")),
            new TenantOptions { AllowConnectionStringFallback = false });

        // Strict tenancy must fail loudly rather than quietly serve the default database.
        await Assert.ThrowsAsync<TenantResolutionException>(
            () => provider.GetConnectionAsync(7));
    }

    [Fact]
    public async Task An_empty_credential_key_is_rejected()
    {
        var provider = Create(
            StubResolver.Returning("sql.example.net", "tenant_a", ""),
            Config(("ConnectionStrings:TenantErp", "Server=fallback;Database=fb;")),
            new TenantOptions { AllowConnectionStringFallback = false });

        await Assert.ThrowsAsync<TenantResolutionException>(
            () => provider.GetConnectionAsync(7));
    }

    [Fact]
    public async Task A_credential_key_with_no_matching_entry_is_rejected()
    {
        var provider = Create(
            StubResolver.Returning("sql.example.net", "tenant_a", "MissingKey"),
            Config(("TenantCredentials:OtherKey:UserId", "u"),
                   ("TenantCredentials:OtherKey:Password", "p")),
            new TenantOptions { AllowConnectionStringFallback = false });

        await Assert.ThrowsAsync<TenantResolutionException>(
            () => provider.GetConnectionAsync(7));
    }

    [Fact]
    public async Task Fails_when_the_fallback_is_allowed_but_not_configured()
    {
        var provider = Create(
            StubResolver.Failing("no row"),
            Config(),
            new TenantOptions { AllowConnectionStringFallback = true });

        await Assert.ThrowsAsync<TenantResolutionException>(
            () => provider.GetConnectionAsync(7));
    }
}
