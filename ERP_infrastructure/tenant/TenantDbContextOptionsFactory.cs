using System.Collections.Concurrent;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
                    // Single query is the right default against a remote database: the
                    // collections loaded alongside a parent here are small, and one round
                    // trip at this latency beats three. Saying so explicitly also stops EF
                    // warning about it on every query that includes two collections.
                    .UseSqlServer(cs, sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery))
                    .UseLoggerFactory(_loggerFactory)
                    .ConfigureWarnings(w =>
                        // A sale line's navigation to its sale is required, and the sale is
                        // branch-filtered, so EF warns that the navigation could come back null.
                        // That is the intended behaviour here rather than a mistake: a line
                        // whose sale belongs to another branch should not be reachable, and the
                        // warning would otherwise be logged on every query in the application.
                        w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
                    .Options);
        }
    }
}
