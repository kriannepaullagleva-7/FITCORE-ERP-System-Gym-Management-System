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
        /// The single company the fallback connection string belongs to.
        ///
        /// Zero - the default - means the fallback is bound to no company and therefore never
        /// applies, however <see cref="AllowConnectionStringFallback"/> is set.
        ///
        /// This binding exists because an unbound fallback is what pooled every tenant into one
        /// database: when the CompanyDatabases rows were wrong, every company's lookup failed
        /// and every company was handed the same default connection string, so Small-tier work
        /// was written into the Micro tenant's database. A fallback that names its company can
        /// still rescue the single-tenant deployment it was written for, but a second company
        /// hitting the same failure now gets a 503 instead of another tenant's data.
        /// </summary>
        public int FallbackCompanyId { get; set; } = 0;

        /// <summary>
        /// Whether tenant connections negotiate TLS.
        ///
        /// This should be true, and is the right setting for any host that supports it. It is a
        /// setting rather than a constant because the current shared hosting refuses an
        /// encrypted connection, and an ERP that cannot reach its database is worse than one
        /// reaching it in clear text on a trusted path. Turn it on as soon as the host allows,
        /// and treat it as a deployment requirement rather than a preference.
        /// </summary>
        public bool EncryptTenantConnections { get; set; } = false;

        /// <summary>
        /// Accept the server's certificate without validating the chain. Only meaningful when
        /// <see cref="EncryptTenantConnections"/> is on; shared hosting rarely presents a
        /// certificate that chains to a public root.
        /// </summary>
        public bool TrustServerCertificate { get; set; } = true;

        /// <summary>
        /// Seconds to wait for a tenant database connection. The fifteen second default is not
        /// always enough for shared hosting on a cold start.
        /// </summary>
        public int ConnectTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Whether the cross-tenant SaaS administration endpoints are reachable. These accept a
        /// company id in the URL, so they are the one place a caller can address another
        /// tenant. They are off unless explicitly enabled, and additionally require the Admin
        /// role and the System Administration module.
        /// </summary>
        public bool EnableCrossTenantAdminApi { get; set; }
    }
}
