namespace ERP_infrastructure.tenant
{
    /// <summary>
    /// Scoped holder for the tenant resolved by the middleware. Deliberately a plain mutable
    /// object written exactly once per scope, so that every service and repository resolved
    /// later in the same scope observes the same tenant.
    /// </summary>
    public class TenantContext : ITenantContext, ITenantContextSetter
    {
        private string? _connectionString;

        public bool IsResolved { get; private set; }

        public int CompanyId { get; private set; }

        public TenantSource Source { get; private set; } = TenantSource.None;

        public bool UsedFallback { get; private set; }

        public string ConnectionString
        {
            get
            {
                if (!IsResolved || string.IsNullOrWhiteSpace(_connectionString))
                {
                    throw new TenantResolutionException(
                        "No tenant has been resolved for this request, so no tenant database " +
                        "can be opened. This endpoint requires a tenant context.");
                }

                return _connectionString;
            }
        }

        public void Set(int companyId, string connectionString, TenantSource source, bool usedFallback)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException(
                    "A tenant connection string is required.", nameof(connectionString));
            }

            CompanyId = companyId;
            _connectionString = connectionString;
            Source = source;
            UsedFallback = usedFallback;
            IsResolved = true;
        }
    }
}
