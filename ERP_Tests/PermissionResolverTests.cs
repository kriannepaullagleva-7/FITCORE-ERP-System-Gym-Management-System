using ERP_domain.entities;
using ERP_infrastructure.services;
using Xunit;

namespace ERP_Tests
{
    /// <summary>
    /// The access rules, tested where they are decided.
    ///
    /// These encode the two things that must never drift: a company cannot use a module its
    /// enterprise tier does not include, and a per-user decision beats the role rather than
    /// the other way round. Both the sidebar and the API authorization filter are built on
    /// this function, so a mistake here is a mistake everywhere.
    /// </summary>
    public class PermissionResolverTests
    {
        private static AppUserPermission Override(string module, bool granted) =>
            new() { Module = module, IsGranted = granted };

        // ------------------------------------------------------------------ role defaults

        [Fact]
        public void Staff_on_a_micro_tenant_gets_the_four_micro_modules_and_the_dashboard()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Micro,
                ErpRoles.DefaultModulesFor(ErpRoles.Staff),
                Array.Empty<AppUserPermission>());

            Assert.Equal(
                new[]
                {
                    ErpModules.Dashboard, ErpModules.Membership, ErpModules.Sales,
                    ErpModules.Payments, ErpModules.Inventory
                },
                modules);
        }

        [Fact]
        public void Admin_on_a_small_tenant_gets_employees_and_payroll()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Small,
                ErpRoles.DefaultModulesFor(ErpRoles.Admin),
                Array.Empty<AppUserPermission>());

            Assert.Contains(ErpModules.Employees, modules);
            Assert.Contains(ErpModules.Payroll, modules);

            // User Access is a Medium module, so even the owner of a Small tenant does not
            // reach it. Accounts for Micro and Small are provisioned by the Bootstrap seeder.
            Assert.DoesNotContain(ErpModules.UserAccess, modules);
        }

        [Fact]
        public void Staff_does_not_get_employees_payroll_or_user_access_by_default()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Small,
                ErpRoles.DefaultModulesFor(ErpRoles.Staff),
                Array.Empty<AppUserPermission>());

            Assert.DoesNotContain(ErpModules.Employees, modules);
            Assert.DoesNotContain(ErpModules.Payroll, modules);
            Assert.DoesNotContain(ErpModules.UserAccess, modules);
        }

        // ------------------------------------------------------------------ the tier gate

        /// <summary>
        /// The Small Enterprise expansion is exactly Employees and Payroll, so a Micro tenant
        /// must not reach them however senior the account is. This is what stops Tenant A
        /// being served the Tenant B feature set.
        /// </summary>
        [Fact]
        public void A_micro_tenant_never_gets_employees_or_payroll_even_for_an_admin()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Micro,
                ErpRoles.DefaultModulesFor(ErpRoles.Admin),
                Array.Empty<AppUserPermission>());

            Assert.DoesNotContain(ErpModules.Employees, modules);
            Assert.DoesNotContain(ErpModules.Payroll, modules);

            // The micro modules are untouched by the gate.
            Assert.Contains(ErpModules.Membership, modules);
            Assert.Contains(ErpModules.Sales, modules);
            Assert.Contains(ErpModules.Payments, modules);
            Assert.Contains(ErpModules.Inventory, modules);
        }

        [Fact]
        public void An_override_cannot_grant_a_module_outside_the_tier()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Micro,
                ErpRoles.DefaultModulesFor(ErpRoles.Staff),
                new[] { Override(ErpModules.Payroll, granted: true) });

            Assert.DoesNotContain(ErpModules.Payroll, modules);
        }

        /// <summary>
        /// The FitCore licensing matrix in full. Micro is the four operational modules plus
        /// Dashboard and Reports and nothing else - in particular neither Expenses nor User
        /// Access, which are Medium.
        /// </summary>
        [Fact]
        public void A_micro_tenant_gets_exactly_the_micro_matrix()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Micro,
                ErpRoles.DefaultModulesFor(ErpRoles.Admin),
                Array.Empty<AppUserPermission>());

            Assert.Equal(
                new[]
                {
                    ErpModules.Dashboard, ErpModules.Membership, ErpModules.Sales,
                    ErpModules.Payments, ErpModules.Inventory, ErpModules.Reports
                },
                modules);
        }

        /// <summary>
        /// Small is Micro plus exactly the workforce pair. Nothing from the Medium tier leaks in.
        /// </summary>
        [Fact]
        public void A_small_tenant_gets_exactly_the_small_matrix()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Small,
                ErpRoles.DefaultModulesFor(ErpRoles.Admin),
                Array.Empty<AppUserPermission>());

            Assert.Equal(
                new[]
                {
                    ErpModules.Dashboard, ErpModules.Membership, ErpModules.Sales,
                    ErpModules.Payments, ErpModules.Inventory,
                    ErpModules.Employees, ErpModules.Payroll, ErpModules.Reports
                },
                modules);
        }

        [Theory]
        [InlineData(ErpModules.Expenses)]
        [InlineData(ErpModules.Finance)]
        [InlineData(ErpModules.BusinessIntelligence)]
        [InlineData(ErpModules.UserAccess)]
        [InlineData(ErpModules.SystemAdmin)]
        public void No_medium_module_reaches_a_micro_or_small_tenant(string mediumModule)
        {
            foreach (var tier in new[] { EnterpriseTier.Micro, EnterpriseTier.Small })
            {
                // Senior role, and an explicit grant on top. Neither may defeat the tier.
                var modules = PermissionResolver.Resolve(
                    tier,
                    ErpRoles.DefaultModulesFor(ErpRoles.SuperAdmin),
                    new[] { Override(mediumModule, granted: true) });

                Assert.DoesNotContain(mediumModule, modules);
            }
        }

        [Fact]
        public void A_medium_tenant_reaches_the_medium_modules()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Medium,
                ErpRoles.DefaultModulesFor(ErpRoles.SuperAdmin),
                Array.Empty<AppUserPermission>());

            Assert.Contains(ErpModules.Expenses, modules);
            Assert.Contains(ErpModules.Finance, modules);
            Assert.Contains(ErpModules.BusinessIntelligence, modules);
            Assert.Contains(ErpModules.UserAccess, modules);
            Assert.Contains(ErpModules.SystemAdmin, modules);
        }

        /// <summary>
        /// System Administration is the Super Admin's alone, even on a Medium tenant where the
        /// tier allows it. An owner runs their company; a super admin runs the platform.
        /// </summary>
        [Fact]
        public void System_administration_is_reserved_for_super_admin()
        {
            var admin = PermissionResolver.Resolve(
                EnterpriseTier.Medium,
                ErpRoles.DefaultModulesFor(ErpRoles.Admin),
                Array.Empty<AppUserPermission>());

            var superAdmin = PermissionResolver.Resolve(
                EnterpriseTier.Medium,
                ErpRoles.DefaultModulesFor(ErpRoles.SuperAdmin),
                Array.Empty<AppUserPermission>());

            Assert.DoesNotContain(ErpModules.SystemAdmin, admin);
            Assert.Contains(ErpModules.SystemAdmin, superAdmin);
        }

        // ------------------------------------------------------------------ overrides

        [Fact]
        public void An_override_grants_a_module_the_role_withholds()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Small,
                ErpRoles.DefaultModulesFor(ErpRoles.Staff),
                new[] { Override(ErpModules.Employees, granted: true) });

            Assert.Contains(ErpModules.Employees, modules);

            // Granting one module must not quietly bring its neighbours with it.
            Assert.DoesNotContain(ErpModules.Payroll, modules);
        }

        [Fact]
        public void An_override_withholds_a_module_the_role_grants()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Small,
                ErpRoles.DefaultModulesFor(ErpRoles.Manager),
                new[] { Override(ErpModules.Payroll, granted: false) });

            Assert.DoesNotContain(ErpModules.Payroll, modules);
            Assert.Contains(ErpModules.Employees, modules);
        }

        [Fact]
        public void Module_keys_are_matched_without_regard_to_case()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Small,
                new[] { "DASHBOARD", "Employees" },
                new[] { Override("PAYROLL", granted: true) });

            Assert.Contains(ErpModules.Dashboard, modules);
            Assert.Contains(ErpModules.Employees, modules);
            Assert.Contains(ErpModules.Payroll, modules);
        }

        // ------------------------------------------------------------------ the editor view

        [Fact]
        public void Describe_reports_every_module_and_marks_the_ones_outside_the_tier()
        {
            var rows = PermissionResolver.Describe(
                EnterpriseTier.Micro,
                ErpRoles.DefaultModulesFor(ErpRoles.Admin),
                Array.Empty<AppUserPermission>());

            Assert.Equal(ErpModules.All.Count, rows.Count);

            var payroll = rows.Single(r => r.Module == ErpModules.Payroll);

            Assert.False(payroll.AvailableInTier);
            Assert.False(payroll.Effective);

            // The role still grants it; it is the plan that withholds it. Showing both is what
            // lets an administrator understand why the box is locked.
            Assert.True(payroll.GrantedByRole);
        }

        [Fact]
        public void Describe_distinguishes_following_the_role_from_an_explicit_decision()
        {
            var rows = PermissionResolver.Describe(
                EnterpriseTier.Small,
                ErpRoles.DefaultModulesFor(ErpRoles.Staff),
                new[] { Override(ErpModules.Employees, granted: true) });

            var employees = rows.Single(r => r.Module == ErpModules.Employees);
            Assert.True(employees.UserOverride);
            Assert.False(employees.GrantedByRole);
            Assert.True(employees.Effective);

            var sales = rows.Single(r => r.Module == ErpModules.Sales);
            Assert.Null(sales.UserOverride);
            Assert.True(sales.GrantedByRole);
            Assert.True(sales.Effective);
        }

        // ------------------------------------------------------------------ catalogue

        [Fact]
        public void The_role_hierarchy_runs_admin_then_manager_then_staff()
        {
            Assert.True(ErpRoles.LevelOf(ErpRoles.Admin) < ErpRoles.LevelOf(ErpRoles.Manager));
            Assert.True(ErpRoles.LevelOf(ErpRoles.Manager) < ErpRoles.LevelOf(ErpRoles.Staff));
        }

        [Fact]
        public void Only_employees_and_payroll_require_the_small_enterprise_tier()
        {
            var smallOnly = ErpModules.All
                .Where(m => m.MinimumTier == EnterpriseTier.Small)
                .Select(m => m.Key)
                .OrderBy(k => k)
                .ToArray();

            Assert.Equal(new[] { ErpModules.Employees, ErpModules.Payroll }, smallOnly);
        }

        [Fact]
        public void Every_role_default_names_a_module_that_actually_exists()
        {
            foreach (var role in new[] { ErpRoles.Admin, ErpRoles.Manager, ErpRoles.Staff })
            {
                foreach (var module in ErpRoles.DefaultModulesFor(role))
                {
                    Assert.True(
                        ErpModules.IsKnown(module),
                        $"Role '{role}' grants unknown module '{module}'.");
                }
            }
        }
    }
}
