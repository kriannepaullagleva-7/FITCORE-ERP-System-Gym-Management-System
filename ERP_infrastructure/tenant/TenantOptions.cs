namespace ERP_infrastructure.tenant
{
    /// <summary>
    /// Binds the "Tenancy" configuration section. Controls how the server decides which
    /// tenant database a request belongs to. Nothing here is ever sent to a client.
    ///
    /// Every default below is the safe value. Configuration can relax them for a development
    /// machine, but a deployment that ships without a Tenancy section - or with the section
    /// renamed or mistyped - gets strict tenancy rather than a quietly permissive server.
    /// </summary>
    public class TenantOptions
    {
        public const string SectionName = "Tenancy";

        /// <summary>
        /// Company used when a request carries no tenant of its own. Zero means "none", so an
        /// unauthenticated caller is served no company at all. Set this only for a single-tenant
        /// deployment that has no sign-in.
        /// </summary>
        public int DefaultCompanyId { get; set; } = 0;

        /// <summary>
        /// When the master database has no usable CompanyDatabases row for the resolved
        /// company, fall back to ConnectionStrings:TenantErp instead of failing the request.
        ///
        /// Off by default: a company whose registry row is missing or wrong must fail loudly
        /// with a 503 rather than silently being handed the default tenant's database, which
        /// would show one customer another customer's members and takings.
        /// </summary>
        public bool AllowConnectionStringFallback { get; set; } = false;

        /// <summary>
        /// Whether an inbound header may select the tenant. This is a development convenience
        /// only: a client must never be able to pick another company's database. It is ignored
        /// unless the host environment is Development, regardless of this value, so the two
        /// conditions together mean it cannot be switched on in production by configuration.
        /// </summary>
        public bool AllowHeaderOverride { get; set; } = false;

        public string HeaderName { get; set; } = "X-Company-Id";

        /// <summary>
        /// Claim types inspected, in order, on the authenticated user. The first one that parses
        /// as an integer wins. This is the authoritative path: the company comes from a token
        /// the server signed, so a client cannot choose one.
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
        /// tenant. They are off unless explicitly enabled, and additionally require the Admin
        /// role and the System Administration module.
        /// </summary>
        public bool EnableCrossTenantAdminApi { get; set; }
    }
}
