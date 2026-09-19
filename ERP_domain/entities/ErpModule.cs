namespace ERP_domain.entities
{
    /// <summary>
    /// The enterprise tier a company is licensed for.
    ///
    /// The tier is what decides which modules a company's database is expected to serve, and
    /// it is stored on the company in the master registry rather than inferred from the
    /// connection string, so a tenant can be promoted without touching any code.
    /// </summary>
    public enum EnterpriseTier
    {
        /// <summary>Membership, Sales, Payments, Inventory - plus Dashboard and Reports.</summary>
        Micro = 1,

        /// <summary>Everything in Micro, plus Employee Management and Payroll.</summary>
        Small = 2,

        /// <summary>
        /// Everything in Small, plus Expenses, Finance Management, Business Intelligence,
        /// User Access and System Administration. The tier is defined and enforced here; the
        /// Medium-only features themselves are not built yet.
        /// </summary>
        Medium = 3
    }

    /// <summary>
    /// The module catalogue.
    ///
    /// A module is the unit a permission is granted over, and the unit the sidebar is built
    /// from. Keys are stable strings because they are stored in the database and travel in a
    /// token; the display names are presentation only.
    ///
    /// <see cref="ErpModuleDefinition.MinimumTier"/> is the second half of an access decision.
    /// A permission row says the user is allowed the module; the tier says the company has it
    /// at all. Both must agree, which is what stops a Micro tenant from reaching Employees or
    /// Payroll even if someone grants the permission by mistake.
    /// </summary>
    public sealed record ErpModuleDefinition(
        string Key,
        string DisplayName,
        EnterpriseTier MinimumTier,
        string Group);

    public static class ErpModules
    {
        public const string Dashboard  = "dashboard";
        public const string Membership = "membership";
        public const string Sales      = "sales";
        public const string Payments   = "payments";
        public const string Inventory  = "inventory";
        public const string Employees  = "employees";
        public const string Payroll    = "payroll";
        public const string Reports    = "reports";
        public const string Expenses   = "expenses";
        public const string Finance    = "finance";
        public const string BusinessIntelligence = "businessintelligence";
        public const string UserAccess = "useraccess";
        public const string SystemAdmin = "systemadmin";

        /// <summary>
        /// Every module the product knows about, in the order the sidebar presents them.
        ///
        /// The tier boundaries are the FitCore licensing matrix: Micro is the four operational
        /// modules a single-branch gym runs on, Small adds the workforce pair, and Medium adds
        /// finance, business intelligence and administration.
        /// </summary>
        public static readonly IReadOnlyList<ErpModuleDefinition> All = new[]
        {
            new ErpModuleDefinition(Dashboard,  "Dashboard",             EnterpriseTier.Micro, "Main"),

            new ErpModuleDefinition(Membership, "Membership Management", EnterpriseTier.Micro, "Operations"),
            new ErpModuleDefinition(Sales,      "Sales Management",      EnterpriseTier.Micro, "Operations"),
            new ErpModuleDefinition(Payments,   "Payment Management",    EnterpriseTier.Micro, "Operations"),
            new ErpModuleDefinition(Inventory,  "Inventory Management",  EnterpriseTier.Micro, "Operations"),

            new ErpModuleDefinition(Employees,  "Employee Management",   EnterpriseTier.Small, "Small Enterprise"),
            new ErpModuleDefinition(Payroll,    "Payroll Management",    EnterpriseTier.Small, "Small Enterprise"),

            new ErpModuleDefinition(Reports,    "Reports",               EnterpriseTier.Micro, "Insight"),

            new ErpModuleDefinition(Expenses,   "Expenses",              EnterpriseTier.Medium, "Finance"),
            new ErpModuleDefinition(Finance,    "Finance Management",    EnterpriseTier.Medium, "Finance"),
            new ErpModuleDefinition(BusinessIntelligence,
                                                "Business Intelligence", EnterpriseTier.Medium, "Insight"),

            new ErpModuleDefinition(UserAccess, "User Access",           EnterpriseTier.Medium, "System"),
            new ErpModuleDefinition(SystemAdmin,"System Administration", EnterpriseTier.Medium, "System")
        };

        public static bool IsKnown(string? key) =>
            !string.IsNullOrWhiteSpace(key) &&
            All.Any(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

        public static ErpModuleDefinition? Find(string? key) =>
            All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>Modules a company on this tier is entitled to at all.</summary>
        public static IEnumerable<ErpModuleDefinition> ForTier(EnterpriseTier tier) =>
            All.Where(m => m.MinimumTier <= tier);
    }

    /// <summary>
    /// The roles FitCore ships with. Stored as rows in AppRoles, so the defaults below are only
    /// the seed - an administrator can change what a role grants, and a per-user override can
    /// depart from it entirely. What no role or override can do is exceed the company's tier.
    /// </summary>
    public static class ErpRoles
    {
        public const string SuperAdmin = "superadmin";
        public const string Admin      = "admin";
        public const string Manager    = "manager";
        public const string Staff      = "staff";

        /// <summary>Lower number is more senior. Used to stop a user editing their senior.</summary>
        public static int LevelOf(string roleKey) => roleKey?.ToLowerInvariant() switch
        {
            SuperAdmin => 0,
            Admin      => 1,
            Manager    => 2,
            Staff      => 3,
            _          => 99
        };

        public static string DisplayNameOf(string roleKey) => roleKey?.ToLowerInvariant() switch
        {
            SuperAdmin => "Super Admin",
            Admin      => "Admin / Owner",
            Manager    => "Manager",
            Staff      => "Receptionist / Staff",
            _          => roleKey ?? ""
        };

        /// <summary>
        /// The default module set for each role.
        ///
        /// These are computed from the catalogue rather than listed by hand, so adding a module
        /// cannot silently leave a role behind. The tier ceiling is applied afterwards by
        /// PermissionResolver, so naming a Medium module here has no effect on a Micro or Small
        /// company.
        ///
        /// Staff deliberately has neither Employees nor Payroll; an administrator grants those
        /// individually through the permission editor. System Administration is reserved for
        /// Super Admin, and User Access is withheld from Manager, so managing accounts stays
        /// with the owner.
        /// </summary>
        public static IReadOnlyList<string> DefaultModulesFor(string roleKey) =>
            roleKey?.ToLowerInvariant() switch
            {
                SuperAdmin => ErpModules.All.Select(m => m.Key).ToArray(),

                Admin => ErpModules.All
                    .Where(m => m.Key != ErpModules.SystemAdmin)
                    .Select(m => m.Key).ToArray(),

                Manager => ErpModules.All
                    .Where(m => m.Key != ErpModules.SystemAdmin && m.Key != ErpModules.UserAccess)
                    .Select(m => m.Key).ToArray(),

                Staff => new[]
                {
                    ErpModules.Dashboard, ErpModules.Membership, ErpModules.Sales,
                    ErpModules.Payments, ErpModules.Inventory
                },

                _ => Array.Empty<string>()
            };
    }
}
