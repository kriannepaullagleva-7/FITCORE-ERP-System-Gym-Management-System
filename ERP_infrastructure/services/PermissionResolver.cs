using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// The single place where "which modules can this person use?" is answered.
    ///
    /// Two independent facts have to agree before a module is allowed:
    ///
    ///   1. The company's <see cref="EnterpriseTier"/> includes the module at all. Employees
    ///      and Payroll are Small Enterprise features, so a Micro tenant does not have them
    ///      however its users are configured.
    ///   2. The user is granted it - by their role, unless their own override says otherwise.
    ///
    /// Keeping this in one function means the sidebar, the API authorization filter and the
    /// User Access editor cannot drift apart, because all three call it.
    /// </summary>
    public static class PermissionResolver
    {
        public static List<string> Resolve(
            EnterpriseTier tier,
            IEnumerable<string> roleModules,
            IEnumerable<AppUserPermission> userOverrides)
        {
            var tierModules = ErpModules.ForTier(tier).Select(m => m.Key).ToList();

            var roleSet = new HashSet<string>(
                roleModules ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            var overrides = BuildOverrideMap(userOverrides);

            return tierModules
                .Where(module => overrides.TryGetValue(module, out var granted)
                    ? granted
                    : roleSet.Contains(module))
                .ToList();
        }

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
                roleModules ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

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

        private static Dictionary<string, bool> BuildOverrideMap(
            IEnumerable<AppUserPermission>? userOverrides)
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var permission in userOverrides ?? Enumerable.Empty<AppUserPermission>())
            {
                if (!string.IsNullOrWhiteSpace(permission.Module))
                {
                    map[permission.Module] = permission.IsGranted;
                }
            }

            return map;
        }
    }
}
