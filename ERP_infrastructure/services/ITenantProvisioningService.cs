namespace ERP_infrastructure.services
{
    /// <summary>
    /// State of a tenant database: whether it can be reached and whether its schema is current.
    /// Deliberately carries no connection detail, so it is safe to return from an API.
    /// </summary>
    public class TenantProvisioningStatus
    {
        public int CompanyId { get; set; }
        public bool CanConnect { get; set; }
        public bool DatabaseExists { get; set; }
        public bool IsUpToDate { get; set; }
        public List<string> AppliedMigrations { get; set; } = new();
        public List<string> PendingMigrations { get; set; } = new();

        /// <summary>
        /// Safe, human-readable reason when the database cannot be reached.
        /// </summary>
        public string? Problem { get; set; }
    }

    public class TenantProvisioningResult
    {
        public int CompanyId { get; set; }
        public bool Succeeded { get; set; }
        public List<string> MigrationsApplied { get; set; } = new();
        public string? Message { get; set; }
    }

    /// <summary>
    /// Brings a tenant database up to the current schema.
    ///
    /// Registering a company in the master registry only says where its database lives; the
    /// database itself still starts empty. This service applies the existing EF Core
    /// migrations to it, which is the onboarding step between "tenant exists" and "tenant
    /// works". It creates no new migrations and never drops anything.
    /// </summary>
    public interface ITenantProvisioningService
    {
        Task<TenantProvisioningStatus> GetStatusAsync(int companyId, CancellationToken cancellationToken = default);

        Task<TenantProvisioningResult> ProvisionAsync(int companyId, CancellationToken cancellationToken = default);
    }
}
