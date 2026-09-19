using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Applies the tenant migrations to a specific company's database.
    ///
    /// Migrations are idempotent: running this against an already-current database applies
    /// nothing and reports an empty list. It is therefore safe to call repeatedly, and safe to
    /// call as the last step of onboarding a new company.
    /// </summary>
    public class TenantProvisioningService : ITenantProvisioningService
    {
        private readonly ITenantDbContextFactory _tenantDbContextFactory;
        private readonly ILogger<TenantProvisioningService> _logger;

        public TenantProvisioningService(
            ITenantDbContextFactory tenantDbContextFactory,
            ILogger<TenantProvisioningService> logger)
        {
            _tenantDbContextFactory = tenantDbContextFactory;
            _logger = logger;
        }

        public async Task<TenantProvisioningStatus> GetStatusAsync(
            int companyId,
            CancellationToken cancellationToken = default)
        {
            var status = new TenantProvisioningStatus { CompanyId = companyId };

            try
            {
                await using var db = await _tenantDbContextFactory.CreateAsync(companyId);

                status.CanConnect = await db.Database.CanConnectAsync(cancellationToken);

                if (!status.CanConnect)
                {
                    status.Problem = "The tenant database could not be reached.";
                    return status;
                }

                status.DatabaseExists = true;
                status.AppliedMigrations =
                    (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
                status.PendingMigrations =
                    (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                status.IsUpToDate = status.PendingMigrations.Count == 0;

                return status;
            }
            catch (Exception ex)
            {
                // The exception can name servers and databases, so it is logged but not returned.
                _logger.LogError(ex,
                    "Could not read provisioning status for company {CompanyId}.", companyId);

                status.Problem = "The tenant database could not be inspected. " +
                                 "Check the server log for details.";
                return status;
            }
        }

        public async Task<TenantProvisioningResult> ProvisionAsync(
            int companyId,
            CancellationToken cancellationToken = default)
        {
            var result = new TenantProvisioningResult { CompanyId = companyId };

            await using var db = await _tenantDbContextFactory.CreateAsync(companyId);

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            if (pending.Count == 0)
            {
                result.Succeeded = true;
                result.Message = "The tenant database is already up to date.";
                return result;
            }

            _logger.LogInformation(
                "Applying {Count} migration(s) to the database for company {CompanyId}.",
                pending.Count, companyId);

            await db.Database.MigrateAsync(cancellationToken);

            result.Succeeded = true;
            result.MigrationsApplied = pending;
            result.Message = $"Applied {pending.Count} migration(s).";

            _logger.LogInformation(
                "Provisioned the database for company {CompanyId}.", companyId);

            return result;
        }
    }
}
