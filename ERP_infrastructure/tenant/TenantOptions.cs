namespace ERP_infrastructure.tenant
{
    /// <summary>
    /// Binds the "Tenancy" configuration section. Controls how the server decides which
    /// tenant database a request belongs to. Nothing here is ever sent to a client.
    /// </summary>
    public class TenantOptions
    {
        public const string SectionName = "Tenancy";

        /// <summary>
        /// Company used when a request carries no tenant of its own. This is what keeps the
        /// existing single-tenant deployment working while authentication is not yet in place.
        /// </summary>
        public int DefaultCompanyId { get; set; } = 3;

        /// <summary>
        /// When the master database has no usable CompanyDatabases row for the resolved
        /// company, fall back to ConnectionStrings:TenantErp instead of failing the request.
        /// Turn this off once the CompanyDatabases rows are correct, so that a bad tenant
        /// configuration fails loudly instead of silently serving the default database.
        /// </summary>
        public bool AllowConnectionStringFallback { get; set; } = true;

        /// <summary>
        /// Whether an inbound header may select the tenant. This is a development convenience
        /// only: a client must never be able to pick another company's database. It is ignored
        /// unless the host environment is Development, regardless of this value.
        /// </summary>
        public bool AllowHeaderOverride { get; set; } = true;

        public string HeaderName { get; set; } = "X-Company-Id";

        /// <summary>
        /// Claim types inspected, in order, on the authenticated user. The first one that parses
        /// as an integer wins. This is the path that becomes authoritative once JWT lands.
        /// </summary>
        public string[] CompanyClaimTypes { get; set; } =
            new[] { "company_id", "companyId", "CompanyId", "tenant_id" };

        /// <summary>
        /// Name of the fallback connection string in the ConnectionStrings section.
        /// </summary>
        public string FallbackConnectionStringName { get; set; } = "TenantErp";

        /// <summary>
        /// Whether the cross-tenant SaaS administration endpoints are reachable. These accept a
        /// company id in the URL, so they are the one place a caller can address another
        /// tenant. They are off unless explicitly enabled, and must be placed behind an
        /// administrator authorization policy once authentication exists.
        /// </summary>
        public bool EnableCrossTenantAdminApi { get; set; }
    }
}
