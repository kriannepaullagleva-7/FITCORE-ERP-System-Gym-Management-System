using System.Collections.Concurrent;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERP_infrastructure.tenant
{
    /// <summary>
    /// Builds and caches the EF Core options for each tenant database.
    /// </summary>
    public interface ITenantDbContextOptionsFactory
    {
        DbContextOptions<TenantErpDbContext> GetOptions(string connectionString);
    }

    /// <summary>
    /// Singleton cache of <see cref="DbContextOptions{TContext}"/> keyed by connection string.
    ///
    /// Rebuilding the options on every request would make EF Core re-resolve its internal
    /// service provider each time, so the options are built once per distinct tenant database
    /// and reused. The number of keys is bounded by the number of tenants.
    ///
    /// The application logger factory is attached here. Without it a tenant context created
    /// outside the DI container logs nothing, which would leave every tenant query invisible
    /// while the master database queries were still traced.
    /// </summary>
    public class TenantDbContextOptionsFactory : ITenantDbContextOptionsFactory
    {
        private readonly ConcurrentDictionary<string, DbContextOptions<TenantErpDbContext>> _cache =
            new(StringComparer.Ordinal);

        private readonly ILoggerFactory _loggerFactory;

        public TenantDbContextOptionsFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public DbContextOptions<TenantErpDbContext> GetOptions(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException(
                    "A tenant connection string is required.", nameof(connectionString));
            }

            return _cache.GetOrAdd(connectionString, cs =>
                new DbContextOptionsBuilder<TenantErpDbContext>()
                    .UseSqlServer(cs)
                    .UseLoggerFactory(_loggerFactory)
                    .Options);
        }
    }
}
