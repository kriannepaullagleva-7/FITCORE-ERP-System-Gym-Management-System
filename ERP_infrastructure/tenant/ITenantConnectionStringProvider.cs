namespace ERP_infrastructure.tenant
{
    /// <summary>
    /// Result of turning a company id into a usable tenant database connection string.
    /// </summary>
    public readonly record struct TenantConnection(string ConnectionString, bool UsedFallback);

    /// <summary>
    /// Turns a company id into the connection string for that company's database, using the
    /// master registry plus the server-side credential store. The connection string never
    /// leaves the server.
    /// </summary>
    public interface ITenantConnectionStringProvider
    {
        Task<TenantConnection> GetConnectionAsync(int companyId, CancellationToken cancellationToken = default);
    }
}
