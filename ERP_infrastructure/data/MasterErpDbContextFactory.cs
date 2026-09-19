using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System;

namespace ERP_infrastructure.data
{
    // Design-time factory for creating MasterErpDbContext when running EF tools (migrations/update-database)
    public class MasterErpDbContextFactory : IDesignTimeDbContextFactory<MasterErpDbContext>
    {
        public MasterErpDbContext CreateDbContext(string[] args)
        {
            var builder = new DbContextOptionsBuilder<MasterErpDbContext>();

            // MASTER_ERP_CONNECTION wins, then the MasterErp entry in the application
            // appsettings.json, then a local database as a last resort.
            var conn = DesignTimeConnectionStrings.Resolve(
                "MASTER_ERP_CONNECTION",
                "MasterErp",
                "Server=(localdb)\\mssqllocaldb;Database=MasterErp;Trusted_Connection=True;MultipleActiveResultSets=True;");

            builder.UseSqlServer(conn, b => b.MigrationsAssembly(typeof(MasterErpDbContext).Assembly.FullName));

            return new MasterErpDbContext(builder.Options);
        }
    }
}
