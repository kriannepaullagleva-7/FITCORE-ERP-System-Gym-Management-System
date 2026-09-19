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

            // TENANT_ERP_CONNECTION wins, then the TenantErp entry in the application
            // appsettings.json, then a local database as a last resort.
            var conn = DesignTimeConnectionStrings.Resolve(
                "TENANT_ERP_CONNECTION",
                "TenantErp",
                "Server=(localdb)\\mssqllocaldb;Database=TenantErp;Trusted_Connection=True;MultipleActiveResultSets=true;");

            builder.UseSqlServer(conn, b => b.MigrationsAssembly(typeof(TenantErpDbContext).Assembly.FullName));

            return new TenantErpDbContext(builder.Options);
        }
    }
}
