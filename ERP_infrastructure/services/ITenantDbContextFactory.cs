using ERP_infrastructure.data;

namespace ERP_infrastructure.services
{
    public interface ITenantDbContextFactory
    {
        /// <summary>
        /// Opens a context against a specific company's database. The caller owns the returned
        /// context and must dispose it. Used by SaaS administration code that legitimately acts
        /// across tenants; request-scoped business code should use
        /// <see cref="CreateForCurrentTenant"/> instead.
        /// </summary>
        Task<TenantErpDbContext> CreateAsync(int companyId);

        /// <summary>
        /// Opens a context against the tenant already resolved for the current request. The
        /// tenant comes from the server-side tenant context, never from client input.
        /// </summary>
        TenantErpDbContext CreateForCurrentTenant();
    }
}
