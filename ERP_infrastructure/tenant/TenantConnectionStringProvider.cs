using ERP_infrastructure.services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP_infrastructure.tenant
{
    /// <summary>
    /// Single place where a company id becomes a tenant connection string.
    ///
    /// The master database stores only where the tenant database lives plus a credential
    /// *key*; the matching user id and password are read from server-side configuration under
    /// "TenantCredentials:{key}". That indirection is what keeps tenant passwords out of the
    /// master database.
    ///
    /// When the registry has no usable row and the fallback is enabled, this falls back to the
    /// configured single-tenant connection string and says so loudly in the log. That keeps an
    /// existing deployment working while the CompanyDatabases rows are still being filled in.
    /// </summary>
    public class TenantConnectionStringProvider : ITenantConnectionStringProvider
    {
        private readonly ITenantDatabaseResolver _resolver;
        private readonly IConfiguration _configuration;
        private readonly TenantOptions _options;
        private readonly ILogger<TenantConnectionStringProvider> _logger;

        public TenantConnectionStringProvider(
            ITenantDatabaseResolver resolver,
            IConfiguration configuration,
            IOptions<TenantOptions> options,
            ILogger<TenantConnectionStringProvider> logger)
        {
            _resolver = resolver;
            _configuration = configuration;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<TenantConnection> GetConnectionAsync(
            int companyId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var connectionString = await BuildFromRegistryAsync(companyId);
                return new TenantConnection(connectionString, UsedFallback: false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TenantResolutionException)
            {
                if (!_options.AllowConnectionStringFallback)
                {
                    throw new TenantResolutionException(
                        $"The database for company {companyId} could not be resolved and the " +
                        "configured fallback is disabled.", ex);
                }

                var fallback = _configuration.GetConnectionString(_options.FallbackConnectionStringName);

                if (string.IsNullOrWhiteSpace(fallback))
                {
                    throw new TenantResolutionException(
                        $"The database for company {companyId} could not be resolved, and no " +
                        $"'{_options.FallbackConnectionStringName}' connection string is configured " +
                        "to fall back to.", ex);
                }

                // Deliberately logs the reason but never the connection string itself.
                _logger.LogWarning(
                    "Tenant registry lookup failed for company {CompanyId} ({Reason}). Falling back " +
                    "to the '{FallbackName}' connection string. Fix the CompanyDatabases row and set " +
                    "Tenancy:AllowConnectionStringFallback to false to enforce strict tenancy.",
                    companyId,
                    ex.Message,
                    _options.FallbackConnectionStringName);

                return new TenantConnection(fallback, UsedFallback: true);
            }
        }

        private async Task<string> BuildFromRegistryAsync(int companyId)
        {
            var databaseInfo = await _resolver.GetDatabaseInfoAsync(companyId);

            if (string.IsNullOrWhiteSpace(databaseInfo.ServerName) ||
                string.IsNullOrWhiteSpace(databaseInfo.DatabaseName))
            {
                throw new TenantResolutionException(
                    $"The CompanyDatabases row for company {companyId} is missing a server or " +
                    "database name.");
            }

            if (string.IsNullOrWhiteSpace(databaseInfo.CredentialKey))
            {
                throw new TenantResolutionException(
                    $"The CompanyDatabases row for company {companyId} has no CredentialKey, so " +
                    "its login cannot be looked up in the TenantCredentials configuration.");
            }

            var userId = _configuration[$"TenantCredentials:{databaseInfo.CredentialKey}:UserId"];
            var password = _configuration[$"TenantCredentials:{databaseInfo.CredentialKey}:Password"];

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
            {
                throw new TenantResolutionException(
                    $"No TenantCredentials entry named '{databaseInfo.CredentialKey}' was found " +
                    $"for company {companyId}.");
            }

            return
                $"Server={databaseInfo.ServerName};" +
                $"Database={databaseInfo.DatabaseName};" +
                $"User Id={userId};" +
                $"Password={password};" +
                "Encrypt=True;" +
                "TrustServerCertificate=True;" +
                "MultipleActiveResultSets=True;";
        }
    }
}
