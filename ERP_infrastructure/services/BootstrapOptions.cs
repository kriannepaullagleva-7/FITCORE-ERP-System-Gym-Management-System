namespace ERP_infrastructure.services
{
    /// <summary>
    /// Binds the "Bootstrap" configuration section.
    ///
    /// This is what turns a bare master database into a working installation: the three roles,
    /// the company-to-tenant registry rows, and one account per role so somebody can sign in
    /// and create the rest. It is declarative configuration rather than code so that adding a
    /// tenant never means editing and redeploying C#.
    ///
    /// Everything it does is idempotent and additive. It matches companies by code and users by
    /// username, and it never deletes, never overwrites a password that is already set, and
    /// never touches business data.
    /// </summary>
    public class BootstrapOptions
    {
        public const string SectionName = "Bootstrap";

        /// <summary>Set to false to skip seeding entirely on a given deployment.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Password given to accounts this seeder creates. They are all marked
        /// <see cref="ERP_domain.entities.AppUser.MustChangePassword"/>, so it is a first-login
        /// credential rather than a permanent one.
        /// </summary>
        public string SeedPassword { get; set; } = "";

        public List<BootstrapTenant> Tenants { get; set; } = new();
    }

    public class BootstrapTenant
    {
        /// <summary>Matches an existing Companies row by code, or creates one.</summary>
        public string CompanyCode { get; set; } = "";

        public string CompanyName { get; set; } = "";

        /// <summary>Micro, Small or Medium.</summary>
        public string Tier { get; set; } = "Micro";

        /// <summary>Where this tenant database lives. Written to CompanyDatabases.</summary>
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";

        /// <summary>
        /// Names an entry under TenantCredentials in server configuration. The master registry
        /// stores this key, never the password behind it.
        /// </summary>
        public string CredentialKey { get; set; } = "";

        /// <summary>
        /// False for a tenant that is registered but not yet in service, such as the reserved
        /// Medium Enterprise slot. No accounts are created for it.
        /// </summary>
        public bool Enabled { get; set; } = true;

        public List<BootstrapUser> Users { get; set; } = new();
    }

    public class BootstrapUser
    {
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";

        /// <summary>admin, manager or staff.</summary>
        public string Role { get; set; } = "";
    }
}
