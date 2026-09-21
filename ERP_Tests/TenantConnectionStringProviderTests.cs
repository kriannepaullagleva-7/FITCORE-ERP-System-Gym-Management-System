using Microsoft.Data.SqlClient;
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

        // Parsed rather than string-matched, so this asserts what SqlClient will actually do
        // with the string rather than how it happens to be spelled.
        var built = new SqlConnectionStringBuilder(result.ConnectionString);

        // The protocol and port are explicit: without them SqlClient can fall back to Named
        // Pipes against a remote host and report the server as not found.
        Assert.Equal("tcp:sql.example.net,1433", built.DataSource);
        Assert.Equal("tenant_a", built.InitialCatalog);
        Assert.Equal("user_a", built.UserID);
        Assert.Equal("secret_a", built.Password);
        Assert.True(built.MultipleActiveResultSets);
    }

    /// <summary>
    /// A password from a hosting control panel can contain a semicolon or a quote. Concatenating
    /// one into a connection string would end it early and silently connect somewhere else, or
    /// fail in a way that looks like a wrong password.
    /// </summary>
    [Fact]
    public async Task A_password_containing_connection_string_syntax_survives_intact()
    {
        const string awkward = "p;a\"s'w=o{rd}";

        var provider = Create(
            StubResolver.Returning("sql.example.net", "tenant_a", "KeyA"),
            Config(("TenantCredentials:KeyA:UserId", "user_a"),
                   ("TenantCredentials:KeyA:Password", awkward)),
            new TenantOptions());

        var result = await provider.GetConnectionAsync(7);

        var built = new SqlConnectionStringBuilder(result.ConnectionString);

        Assert.Equal(awkward, built.Password);
        Assert.Equal("tenant_a", built.InitialCatalog);
    }

    /// <summary>
    /// A registry row that already names a port or an instance is left alone rather than having
    /// a second one bolted on.
    /// </summary>
    [Theory]
    [InlineData("sql.example.net,1433")]
    [InlineData("tcp:sql.example.net,1433")]
    [InlineData("SQLHOST\\SQLEXPRESS")]
    public async Task A_server_name_that_already_names_a_protocol_or_port_is_left_alone(string serverName)
    {
        var provider = Create(
            StubResolver.Returning(serverName, "tenant_a", "KeyA"),
            Config(("TenantCredentials:KeyA:UserId", "user_a"),
                   ("TenantCredentials:KeyA:Password", "secret_a")),
            new TenantOptions());

        var result = await provider.GetConnectionAsync(7);

        Assert.Equal(serverName, new SqlConnectionStringBuilder(result.ConnectionString).DataSource);
    }

    [Fact]
    public async Task Falls_back_to_the_configured_connection_string_when_allowed()
    {
        var provider = Create(
            StubResolver.Failing("no row"),
            Config(("ConnectionStrings:TenantErp", "Server=fallback;Database=fb;")),
            new TenantOptions { AllowConnectionStringFallback = true, FallbackCompanyId = 7 });

        var result = await provider.GetConnectionAsync(7);

        Assert.True(result.UsedFallback);
        Assert.Equal("Server=fallback;Database=fb;", result.ConnectionString);
    }

    /// <summary>
    /// The regression that pooled every tenant into one database.
    ///
    /// With the CompanyDatabases rows broken, every company's registry lookup failed. An
    /// unbound fallback answered all of them with the same connection string, so Small-tier
    /// work was written into the Micro tenant's database. Binding the fallback to one company
    /// means a second company hitting the same failure is refused instead of being handed
    /// somebody else's data.
    /// </summary>
    [Fact]
    public async Task The_fallback_is_refused_for_a_company_it_is_not_bound_to()
    {
        var provider = Create(
            StubResolver.Failing("no row"),
            Config(("ConnectionStrings:TenantErp", "Server=fallback;Database=fb;")),
            new TenantOptions { AllowConnectionStringFallback = true, FallbackCompanyId = 3 });

        // Company 3 is the one the fallback belongs to, so it is still rescued.
        var rescued = await provider.GetConnectionAsync(3);
        Assert.True(rescued.UsedFallback);

        // Company 4 must not be served company 3's database.
        await Assert.ThrowsAsync<TenantResolutionException>(
            () => provider.GetConnectionAsync(4));
    }

    /// <summary>
    /// Leaving FallbackCompanyId unset makes the fallback inert, so enabling the flag without
    /// naming its company cannot silently reopen the hole.
    /// </summary>
    [Fact]
    public async Task An_unbound_fallback_never_applies()
    {
        var provider = Create(
            StubResolver.Failing("no row"),
            Config(("ConnectionStrings:TenantErp", "Server=fallback;Database=fb;")),
            new TenantOptions { AllowConnectionStringFallback = true });

        await Assert.ThrowsAsync<TenantResolutionException>(
            () => provider.GetConnectionAsync(7));
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
            new TenantOptions { AllowConnectionStringFallback = true, FallbackCompanyId = 7 });

        await Assert.ThrowsAsync<TenantResolutionException>(
            () => provider.GetConnectionAsync(7));
    }
}
