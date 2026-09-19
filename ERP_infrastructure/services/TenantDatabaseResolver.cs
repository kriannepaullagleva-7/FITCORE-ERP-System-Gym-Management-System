using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class TenantDatabaseResolver : ITenantDatabaseResolver
    {
        private readonly MasterErpDbContext _masterDb;

        public TenantDatabaseResolver(
            MasterErpDbContext masterDb)
        {
            _masterDb = masterDb;
        }

        public async Task<TenantDatabaseInfo> GetDatabaseInfoAsync(
            int companyId)
        {
            var database =
                await _masterDb.CompanyDatabases
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.CompanyId == companyId &&
                        x.IsActive);

            if (database == null)
            {
                throw new InvalidOperationException(
                    $"No active database configuration found " +
                    $"for company {companyId}.");
            }

            return new TenantDatabaseInfo
            {
                ServerName = database.ServerName,
                DatabaseName = database.DatabaseName,
                CredentialKey = database.CredentialKey
            };
        }
    }
}