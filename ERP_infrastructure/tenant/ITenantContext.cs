namespace ERP_infrastructure.tenant
{
    /// <summary>
    /// How the tenant for the current request was decided. Recorded for logging and
    /// diagnostics so an operator can tell a genuinely authenticated tenant apart from
    /// one that came from the development header or the configured default.
    /// </summary>
    public enum TenantSource
    {
        None = 0,
        Claim = 1,
        Header = 2,
        Default = 3
    }

    /// <summary>
    /// The server-resolved tenant for the current request or scope. Scoped: one instance
    /// per request. The connection string lives here and is never exposed to a client.
    /// </summary>
    public interface ITenantContext
    {
        bool IsResolved { get; }

        int CompanyId { get; }

        TenantSource Source { get; }

        /// <summary>
        /// True when the tenant database could not be resolved from the master registry and
        /// the configured fallback connection string is being used instead.
        /// </summary>
        bool UsedFallback { get; }

        /// <summary>
        /// Connection string for this tenant's database. Throws when the tenant is unresolved,
        /// so that a missing tenant can never silently read the wrong database.
        /// </summary>
        string ConnectionString { get; }
    }

    /// <summary>
    /// Write side of <see cref="ITenantContext"/>. Only the tenant resolution middleware
    /// (or a host that sets the tenant explicitly) should use this.
    /// </summary>
    public interface ITenantContextSetter
    {
        void Set(int companyId, string connectionString, TenantSource source, bool usedFallback);
    }
}
