using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System;

namespace ERP_infrastructure.data
{
    // Design-time factory for creating TenantErpDbContext when running EF tools (migrations/update-database)
    public class TenantErpDbContextFactory : IDesignTimeDbContextFactory<TenantErpDbContext>
    {
        public TenantErpDbContext CreateDbContext(string[] args)
        {
            var builder = new DbContextOptionsBuilder<TenantErpDbContext>();

            // There is one tenant schema but several tenant databases, so "update the tenant
            // database" is ambiguous. Left alone this targets TenantErp, which is tenant_a;
            // set TENANT_ERP_CONNECTION_NAME to migrate a different one, for example
            // TenantErpB. The resolved server and catalogue are printed either way, because a
            // migration applied to the wrong tenant is the expensive mistake here.
            var connectionName =
                Environment.GetEnvironmentVariable("TENANT_ERP_CONNECTION_NAME") ?? "TenantErp";

            // TENANT_ERP_CONNECTION (a full connection string) wins over the named lookup.
            var conn = DesignTimeConnectionStrings.Resolve(
                "TENANT_ERP_CONNECTION",
                connectionName,
                "Server=(localdb)\\mssqllocaldb;Database=TenantErp;Trusted_Connection=True;MultipleActiveResultSets=true;");

            builder.UseSqlServer(conn, b => b.MigrationsAssembly(typeof(TenantErpDbContext).Assembly.FullName));

            return new TenantErpDbContext(builder.Options);
        }
    }
}
