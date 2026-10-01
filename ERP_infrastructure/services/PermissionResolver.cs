using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// The single place where "which modules can this person use?" is answered.
    ///
    /// Two independent facts have to agree before a module is allowed:
    ///
    ///   1. The company's <see cref="EnterpriseTier"/> includes the module at all. Employees
    ///      and Payroll are Small Enterprise features and Finance is a Medium one, so a Micro
    ///      tenant does not have them however its users are configured.
    ///   2. The user is granted it - by their role, unless their own override says otherwise.
    ///
    /// A third fact narrows what they can do *within* a module: <see cref="ResolveSubmodules"/>
    /// applies the subfeature's own tier and role floor. That is how one catalogue of nine
    /// modules can describe a Micro gym's four screens and a Medium tenant's general ledger
    /// without either seeing the other's.
    ///
    /// Keeping this in one place means the sidebar, the API authorization filters and the
    /// permission editor cannot drift apart, because all of them call it.
    /// </summary>
    public static class PermissionResolver
    {
        public static List<string> Resolve(
            EnterpriseTier tier,
            IEnumerable<string> roleModules,
            IEnumerable<AppUserPermission> userOverrides)
        {
            var tierModules = ErpModules.ForTier(tier).Select(m => m.Key).ToList();

            // Normalised on the way in: a permission row may still name a module key from
            // before the catalogue was consolidated to nine, and a grant an administrator made
            // deliberately must not quietly evaporate.
            var roleSet = new HashSet<string>(
                (roleModules ?? Enumerable.Empty<string>()).Select(ErpModules.Normalise),
                StringComparer.OrdinalIgnoreCase);

            var overrides = BuildOverrideMap(userOverrides);

            return tierModules
                .Where(module => overrides.TryGetValue(module, out var granted)
                    ? granted
                    : roleSet.Contains(module))
                .ToList();
        }

        /// <summary>
        /// The subfeatures this person may actually open, given the modules they hold.
        ///
        /// Submodules are derived rather than granted. They are never stored, never travel in
        /// the token as claims of their own, and cannot be edited individually - which means
        /// there is no way to hold a subfeature without its module, or one above the company's
        /// plan. The client uses this to build its tab strips; the server re-derives it for
        /// every request that needs it.
        /// </summary>
        public static List<string> ResolveSubmodules(
            EnterpriseTier tier, int roleLevel, IEnumerable<string> effectiveModules) =>
            ErpModules.ResolveSubmodules(tier, roleLevel, effectiveModules);

        /// <summary>
        /// Builds the editor rows for one user: role default, override and effective value for
        /// every module in the catalogue, including the ones the tier withholds so an
        /// administrator can see that they exist but are not part of this plan.
        /// </summary>
        public static List<ModulePermissionView> Describe(
            EnterpriseTier tier,
            IEnumerable<string> roleModules,
            IEnumerable<AppUserPermission> userOverrides)
        {
            var roleSet = new HashSet<string>(
                (roleModules ?? Enumerable.Empty<string>()).Select(ErpModules.Normalise),
                StringComparer.OrdinalIgnoreCase);

            var overrides = BuildOverrideMap(userOverrides);

            return ErpModules.All.Select(definition =>
            {
                var availableInTier = definition.MinimumTier <= tier;
                var grantedByRole = roleSet.Contains(definition.Key);

                bool? userOverride = overrides.TryGetValue(definition.Key, out var value)
                    ? value
                    : null;

                var effective = availableInTier && (userOverride ?? grantedByRole);

                return new ModulePermissionView
                {
                    Module = definition.Key,
                    DisplayName = definition.DisplayName,
                    Group = definition.Group,
                    AvailableInTier = availableInTier,
                    GrantedByRole = grantedByRole,
                    UserOverride = userOverride,
                    Effective = effective
                };
            }).ToList();
        }

        /// <summary>
        /// Every subfeature in the catalogue with the reason it is or is not available to this
        /// user, so the permission editor can show an administrator the whole shape of a plan
        /// rather than nine checkboxes.
        /// </summary>
        public static List<SubmodulePermissionView> DescribeSubmodules(
            EnterpriseTier tier, int roleLevel, IEnumerable<string> effectiveModules)
        {
            var held = new HashSet<string>(
                (effectiveModules ?? Enumerable.Empty<string>()).Select(ErpModules.Normalise),
                StringComparer.OrdinalIgnoreCase);

            return ErpModules.Submodules.Select(definition =>
            {
                var availableInTier = definition.MinimumTier <= tier;
                var allowedForRole = roleLevel <= definition.MinimumRoleLevel;
                var holdsModule = held.Contains(definition.ModuleKey);

                return new SubmodulePermissionView
                {
                    Submodule = definition.Key,
                    Module = definition.ModuleKey,
                    ModuleDisplayName = ErpModules.Find(definition.ModuleKey)?.DisplayName ?? definition.ModuleKey,
                    DisplayName = definition.DisplayName,
                    Description = definition.Description,
                    MinimumTierName = definition.MinimumTier.ToString(),
                    AvailableInTier = availableInTier,
                    AllowedForRole = allowedForRole,
                    HoldsParentModule = holdsModule,
                    Effective = availableInTier && allowedForRole && holdsModule
                };
            }).ToList();
        }

        private static Dictionary<string, bool> BuildOverrideMap(
            IEnumerable<AppUserPermission>? userOverrides)
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var permission in userOverrides ?? Enumerable.Empty<AppUserPermission>())
            {
                if (!string.IsNullOrWhiteSpace(permission.Module))
                {
                    // Two legacy rows can normalise onto the same module - "dashboard" and
                    // "reports" both became Business Intelligence. A withheld one must not be
                    // resurrected by a granted one it happens to be listed after, so an
                    // explicit withholding wins.
                    var key = ErpModules.Normalise(permission.Module);

                    if (map.TryGetValue(key, out var existing) && existing != permission.IsGranted)
                    {
                        map[key] = false;
                        continue;
                    }

                    map[key] = permission.IsGranted;
                }
            }

            return map;
        }
    }
}
