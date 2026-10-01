namespace ERP_api.Infrastructure
{
    /// <summary>
    /// Binds the "Jwt" configuration section.
    ///
    /// The signing key is what makes the company claim trustworthy: the tenant middleware reads
    /// the company out of the token and turns it straight into a connection string, so anyone
    /// who can mint a token can read any tenant. It therefore has no default and the API refuses
    /// to start without one.
    /// </summary>
    public class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; set; } = "FitCoreERP";
        public string Audience { get; set; } = "FitCoreERP.Client";
        public string SigningKey { get; set; } = "";
        public int TokenLifetimeMinutes { get; set; } = 480;
    }

    /// <summary>
    /// The claim types FitCore issues. <c>company_id</c> is deliberately the first of the types
    /// <see cref="ERP_infrastructure.tenant.TenantOptions.CompanyClaimTypes"/> already looked
    /// for, so tenant resolution needed no change to start trusting a real sign-in.
    /// </summary>
    public static class FitCoreClaims
    {
        public const string CompanyId = "company_id";
        public const string CompanyName = "company_name";
        public const string EnterpriseTier = "enterprise_tier";
        public const string AppUserId = "app_user_id";
        public const string RoleKey = "role_key";
        public const string RoleLevel = "role_level";
        public const string FullName = "full_name";
        public const string EmployeeId = "employee_id";

        /// <summary>
        /// The branch this account is bound to, present only when it is bound to one.
        ///
        /// Signed, so it is the one branch statement a caller cannot argue with: a branch
        /// manager carries this and is narrowed to it for the life of the token. An Admin/Owner
        /// has no such claim, which is what makes them free to select a branch instead.
        /// </summary>
        public const string BranchId = "branch_id";

        /// <summary>One claim of this type per module the user may use.</summary>
        public const string Module = "module";
    }
}
