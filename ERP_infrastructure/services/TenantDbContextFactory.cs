using ERP_infrastructure.data;
using ERP_infrastructure.tenant;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Creates a <see cref="TenantErpDbContext"/> bound to a tenant's own database.
    ///
    /// Connection string construction and the fallback rules live in
    /// <see cref="ITenantConnectionStringProvider"/>; the EF option graphs are cached by
    /// <see cref="ITenantDbContextOptionsFactory"/>. This type just puts the two together.
    ///
    /// The current user is handed to each context it builds so writes can be attributed. It is
    /// passed here, per context, rather than attached to the cached options: those options live
    /// for the life of the process, so anything held on them would outlive the request.
    /// </summary>
    public class TenantDbContextFactory : ITenantDbContextFactory
    {
        private readonly ITenantConnectionStringProvider _connectionStringProvider;
        private readonly ITenantDbContextOptionsFactory _optionsFactory;
        private readonly ITenantContext _tenantContext;
        private readonly ICurrentUserAccessor _actor;
        private readonly IBranchContext _branch;

        public TenantDbContextFactory(
            ITenantConnectionStringProvider connectionStringProvider,
            ITenantDbContextOptionsFactory optionsFactory,
            ITenantContext tenantContext,
            ICurrentUserAccessor actor,
            IBranchContext branch)
        {
            _connectionStringProvider = connectionStringProvider;
            _optionsFactory = optionsFactory;
            _tenantContext = tenantContext;
            _actor = actor;
            _branch = branch;
        }

        public async Task<TenantErpDbContext> CreateAsync(int companyId)
        {
            var connection = await _connectionStringProvider.GetConnectionAsync(companyId);
            return new TenantErpDbContext(
                _optionsFactory.GetOptions(connection.ConnectionString), _actor, _branch);
        }

        public TenantErpDbContext CreateForCurrentTenant()
        {
            // ConnectionString throws a TenantResolutionException when no tenant was resolved,
            // which is what we want: better a clear failure than reading the wrong database.
            //
            // The branch scope is handed over the same way the actor is, and for the same
            // reason: it belongs to this request, and the options it would otherwise hang on
            // are cached for the life of the process.
            return new TenantErpDbContext(
                _optionsFactory.GetOptions(_tenantContext.ConnectionString), _actor, _branch);
        }
    }
}
