namespace ERP_domain.entities
{
    /// <summary>
    /// A role in the master registry.
    ///
    /// Roles are rows rather than hard-coded names so that what a role grants is a database
    /// fact, which is what lets the permission editor change it and have the change survive a
    /// sign-out. <see cref="ErpRoles"/> only supplies the values the seeder writes on a fresh
    /// installation.
    /// </summary>
    public class AppRole
    {
        public int RoleId { get; set; }

        /// <summary>Stable lowercase key: admin, manager, staff. Unique.</summary>
        public string RoleKey { get; set; } = "";

        public string DisplayName { get; set; } = "";

        /// <summary>
        /// Seniority, lower being more senior. A user may never grant a permission to someone
        /// at or above their own level, which stops a manager promoting themselves to owner.
        /// </summary>
        public int HierarchyLevel { get; set; }

        /// <summary>Shipped with the product, so it cannot be deleted.</summary>
        public bool IsSystemRole { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<AppRolePermission> Permissions { get; set; } = new List<AppRolePermission>();
        public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    }

    /// <summary>
    /// A module a role grants by default. The starting point for every user holding the role,
    /// before their own overrides are applied.
    /// </summary>
    public class AppRolePermission
    {
        public int AppRolePermissionId { get; set; }

        public int RoleId { get; set; }
        public AppRole Role { get; set; } = null!;

        /// <summary>A key from <see cref="ErpModules"/>.</summary>
        public string Module { get; set; } = "";
    }
}
