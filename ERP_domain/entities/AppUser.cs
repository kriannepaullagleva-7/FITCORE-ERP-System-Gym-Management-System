namespace ERP_domain.entities
{
    /// <summary>
    /// A person who signs in to FitCore.
    ///
    /// Users live in the master database, not in a tenant database, because a user is the
    /// thing that *selects* a tenant: the company recorded here is what the server turns into
    /// a connection string. A user therefore belongs to exactly one company and can never
    /// address another one, whatever a request header says.
    ///
    /// <see cref="EmployeeId"/> points at an Employee row inside that company's own tenant
    /// database. It is a plain integer rather than a navigation property on purpose - the two
    /// records live in different physical databases, so a foreign key between them cannot and
    /// must not exist.
    /// </summary>
    public class AppUser
    {
        public int AppUserId { get; set; }

        /// <summary>The tenant this user is bound to. Authoritative for tenant resolution.</summary>
        public int CompanyId { get; set; }
        public Company Company { get; set; } = null!;

        /// <summary>Sign-in name. Unique across the platform so a login needs no company picker.</summary>
        public string Username { get; set; } = "";

        public string Email { get; set; } = "";

        public string FullName { get; set; } = "";

        /// <summary>
        /// PBKDF2 hash produced by ASP.NET Core's PasswordHasher. A plaintext password is never
        /// stored, logged or returned.
        /// </summary>
        public string PasswordHash { get; set; } = "";

        public int RoleId { get; set; }
        public AppRole Role { get; set; } = null!;

        /// <summary>A deactivated user is refused at sign-in by the server.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Optional link to this person's Employee record in their own tenant database.
        /// Cross-database, so deliberately not a foreign key.
        /// </summary>
        public int? EmployeeId { get; set; }

        /// <summary>
        /// The branch this account is bound to, or null for somebody who works across the whole
        /// company. A Manager or Staff account belongs to exactly one branch and can never read
        /// another; an Admin/Owner has none and chooses which branch to look at.
        ///
        /// Like <see cref="EmployeeId"/> this points into the tenant's own database and is
        /// deliberately not a foreign key - the two rows live in different physical databases.
        /// It is stored here for the same reason <see cref="CompanyId"/> is: the account is what
        /// selects the scope, so the scope has to be known before the tenant database is opened.
        /// Only the identifier is held; the branch's name, address and history stay tenant-side.
        /// </summary>
        public int? BranchId { get; set; }

        /// <summary>Forces a password change on next sign-in. Set on seeded accounts.</summary>
        public bool MustChangePassword { get; set; }

        public DateTime? LastLoginAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<AppUserPermission> Permissions { get; set; } = new List<AppUserPermission>();
    }

    /// <summary>
    /// A departure from what this user's role grants.
    ///
    /// Storing overrides rather than the whole effective set means a change to a role still
    /// reaches every user who has not been singled out, while an explicit decision about one
    /// person is never silently undone. <see cref="IsGranted"/> distinguishes "additionally
    /// allowed" from "specifically withheld".
    /// </summary>
    public class AppUserPermission
    {
        public int AppUserPermissionId { get; set; }

        public int AppUserId { get; set; }
        public AppUser User { get; set; } = null!;

        /// <summary>A key from <see cref="ErpModules"/>.</summary>
        public string Module { get; set; } = "";

        /// <summary>True grants the module, false withholds it, overriding the role either way.</summary>
        public bool IsGranted { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Username of whoever last changed this, for an audit trail.</summary>
        public string UpdatedBy { get; set; } = "";
    }
}
