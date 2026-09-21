using ERP_UI.DTOs;

namespace ERP_UI.Services
{
    /// <summary>One entry in the sidebar, and the module a user must hold to see it.</summary>
    public record NavEntry(string Label, string Href, string Icon, string Module);

    public record NavGroup(string Title, IReadOnlyList<NavEntry> Entries);

    /// <summary>
    /// The single description of what the application contains and who may see what.
    ///
    /// The sidebar, the page titles and the route guards are all derived from this one list,
    /// so a page cannot end up reachable but unlisted, or listed but unreachable. Filtering
    /// here is presentation only - the API applies the same rule again and answers 403 for a
    /// module the caller does not hold, whatever the browser decided to draw.
    /// </summary>
    public static class NavigationCatalogue
    {
        public static readonly IReadOnlyList<NavGroup> Groups = new[]
        {
            new NavGroup("Main", new[]
            {
                new NavEntry("Dashboard", "fitcore/dashboard", "dashboard", Modules.Dashboard)
            }),

            new NavGroup("Operations", new[]
            {
                new NavEntry("Members",          "fitcore/members",          "members",      Modules.Membership),
                new NavEntry("Membership Plans", "fitcore/membership-plans", "plan",         Modules.Membership),
                new NavEntry("Subscriptions",    "fitcore/subscriptions",    "subscription", Modules.Membership),
                new NavEntry("New Sale",         "fitcore/pos",              "plus",         Modules.Sales),
                new NavEntry("Sales",            "fitcore/sales",            "sales",        Modules.Sales),
                new NavEntry("Payments",         "fitcore/payments",         "payment",      Modules.Payments),

                // Gated to match the controllers: CustomersController requires Sales and
                // SuppliersController requires Inventory, so the sidebar asks for the same
                // module the API will check.
                new NavEntry("Customers",        "fitcore/customers",        "customer",     Modules.Sales),

                new NavEntry("Products",         "fitcore/products",         "product",      Modules.Inventory),
                new NavEntry("Inventory",        "fitcore/inventory",        "inventory",    Modules.Inventory),
                new NavEntry("Suppliers",        "fitcore/suppliers",        "supplier",     Modules.Inventory)
            }),

            new NavGroup("Small Enterprise", new[]
            {
                new NavEntry("Employees", "fitcore/employees", "employee", Modules.Employees),
                new NavEntry("Payroll",   "fitcore/payroll",   "payroll",  Modules.Payroll)
            }),

            new NavGroup("Insight", new[]
            {
                new NavEntry("Reports", "fitcore/reports", "reports", Modules.Reports)
            }),

            // Medium tier. These entries are invisible to a Micro or Small company because no
            // user there can hold the module, whatever their role - the tier is the ceiling.
            new NavGroup("Finance", new[]
            {
                new NavEntry("Expenses", "fitcore/expenses", "expense", Modules.Expenses)
            }),

            new NavGroup("System", new[]
            {
                new NavEntry("User Access",   "fitcore/user-access",    "shield", Modules.UserAccess),
                new NavEntry("Administration","fitcore/administration", "admin",  Modules.SystemAdmin)
            })
        };

        /// <summary>
        /// The groups this user can actually use, with empty groups dropped so a Staff sidebar
        /// does not show a "Small Enterprise" heading with nothing beneath it.
        /// </summary>
        public static IEnumerable<NavGroup> For(CurrentUserDto? user)
        {
            if (user is null) yield break;

            foreach (var group in Groups)
            {
                var visible = group.Entries.Where(e => user.Can(e.Module)).ToList();

                if (visible.Count > 0)
                {
                    yield return new NavGroup(group.Title, visible);
                }
            }
        }

        private static readonly Dictionary<string, NavEntry> ByPath =
            Groups.SelectMany(g => g.Entries).ToDictionary(e => e.Href, StringComparer.OrdinalIgnoreCase);

        /// <summary>The module a route belongs to, or null for a route outside the catalogue.</summary>
        public static string? ModuleForPath(string path) =>
            ByPath.TryGetValue(path.Trim('/'), out var entry) ? entry.Module : null;

        public static string TitleForPath(string path)
        {
            var trimmed = path.Split('?')[0].Trim('/');

            if (trimmed.Length == 0) return "Dashboard";

            return ByPath.TryGetValue(trimmed, out var entry) ? entry.Label : "FitCore ERP";
        }
    }
}
