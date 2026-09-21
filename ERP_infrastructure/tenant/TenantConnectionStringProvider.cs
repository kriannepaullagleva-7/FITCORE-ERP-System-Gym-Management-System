using Microsoft.Data.SqlClient;
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

                // The fallback names exactly one company. Serving any other company from it
                // would hand one tenant another tenant's database - which is precisely how
                // Small-tier data ended up written into the Micro tenant.
                if (_options.FallbackCompanyId != companyId)
                {
                    throw new TenantResolutionException(
                        $"The database for company {companyId} could not be resolved. The fallback " +
                        $"connection string is bound to company {_options.FallbackCompanyId}, so it " +
                        "was not used: serving one company from another company's database would " +
                        "leak tenant data. Fix the CompanyDatabases row for this company.", ex);
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

            // Built rather than concatenated. A password containing a semicolon or a quote would
            // otherwise end the string early or corrupt it, and these passwords are generated by
            // the hosting panel rather than chosen to be safe in a connection string.
            var builder = new SqlConnectionStringBuilder
            {
                // The tcp: prefix and explicit port matter. Without them SqlClient may try Named
                // Pipes against a remote host and fail with "server was not found" even though
                // the name resolves and the port is open.
                DataSource = FormatDataSource(databaseInfo.ServerName),
                InitialCatalog = databaseInfo.DatabaseName,
                UserID = userId,
                Password = password,
                MultipleActiveResultSets = true,

                Encrypt = _options.EncryptTenantConnections,
                TrustServerCertificate = _options.TrustServerCertificate,

                // Shared database hosting can be slow to hand out a connection; the default of
                // fifteen seconds is not always enough on a cold start.
                ConnectTimeout = _options.ConnectTimeoutSeconds
            };

            return builder.ConnectionString;
        }

        /// <summary>
        /// Turns a bare host name from the registry into an explicit TCP endpoint, leaving any
        /// value that already names a protocol, a port or a named instance alone.
        /// </summary>
        private static string FormatDataSource(string serverName)
        {
            var trimmed = serverName.Trim();

            if (trimmed.Contains(':') || trimmed.Contains(',') || trimmed.Contains('\\'))
            {
                return trimmed;
            }

            return $"tcp:{trimmed},1433";
        }
    }
}
