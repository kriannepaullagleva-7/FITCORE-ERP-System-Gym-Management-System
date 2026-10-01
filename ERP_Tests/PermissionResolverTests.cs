using ERP_domain.entities;
using ERP_infrastructure.services;
using Xunit;

namespace ERP_Tests
{
    /// <summary>
    /// The access rules, tested where they are decided.
    ///
    /// These encode the three things that must never drift: a company cannot use a module its
    /// enterprise tier does not include, a per-user decision beats the role rather than the
    /// other way round, and a subfeature can never be reached without the module it belongs
    /// underneath. The sidebar, the API authorization filters and the permission editor are all
    /// built on these functions, so a mistake here is a mistake everywhere.
    /// </summary>
    public class PermissionResolverTests
    {
        private static AppUserPermission Override(string module, bool granted) =>
            new() { Module = module, IsGranted = granted };

        private static List<string> ModulesFor(
            EnterpriseTier tier, string role, params AppUserPermission[] overrides) =>
            PermissionResolver.Resolve(tier, ErpRoles.DefaultModulesFor(role), overrides);

        private static List<string> SubmodulesFor(EnterpriseTier tier, string role) =>
            PermissionResolver.ResolveSubmodules(
                tier, ErpRoles.LevelOf(role), ModulesFor(tier, role));

        // ------------------------------------------------------------------ the nine

        /// <summary>
        /// The catalogue is closed at nine. Everything else FitCore does is a subfeature
        /// underneath one of them, which is what stops the sidebar growing a tenth entry every
        /// time a capability is added.
        /// </summary>
        [Fact]
        public void The_catalogue_is_exactly_the_nine_use_case_modules()
        {
            Assert.Equal(
                new[]
                {
                    ErpModules.Membership, ErpModules.Payments, ErpModules.Sales,
                    ErpModules.Inventory, ErpModules.Employees, ErpModules.Payroll,
                    ErpModules.Finance, ErpModules.SystemAdmin, ErpModules.BusinessIntelligence
                },
                ErpModules.All.Select(m => m.Key));
        }

        [Fact]
        public void Every_submodule_belongs_to_one_of_the_nine()
        {
            foreach (var submodule in ErpModules.Submodules)
            {
                Assert.True(
                    ErpModules.IsKnown(submodule.ModuleKey),
                    $"Submodule '{submodule.Key}' names unknown module '{submodule.ModuleKey}'.");
            }
        }

        /// <summary>
        /// A submodule key is always its module's key and a dot, so a reader can tell where a
        /// feature lives from the key alone and no two modules can claim the same subfeature.
        /// </summary>
        [Fact]
        public void Every_submodule_key_is_prefixed_with_its_module()
        {
            foreach (var submodule in ErpModules.Submodules)
            {
                Assert.StartsWith(submodule.ModuleKey + ".", submodule.Key, StringComparison.Ordinal);
            }

            Assert.Equal(
                ErpModules.Submodules.Count,
                ErpModules.Submodules.Select(s => s.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        /// <summary>
        /// A subfeature may sit above its module - Business Intelligence is available to every
        /// tenant while its analytics are Medium - but never below it, which would be a
        /// subfeature reachable on a plan that does not include the module holding it.
        /// </summary>
        [Fact]
        public void No_submodule_is_licensed_below_the_module_it_belongs_to()
        {
            foreach (var submodule in ErpModules.Submodules)
            {
                var module = ErpModules.Find(submodule.ModuleKey)!;

                Assert.True(
                    submodule.MinimumTier >= module.MinimumTier,
                    $"'{submodule.Key}' is {submodule.MinimumTier} but its module is {module.MinimumTier}.");
            }
        }

        // ------------------------------------------------------------------ role defaults

        [Fact]
        public void Staff_get_the_front_desk_modules_and_nothing_of_Intelligence()
        {
            // Read on Small, because the front desk's own module set is wider than the Micro
            // plan: Micro has no till and no stockroom, so a Micro Staff account is narrower
            // still. The tier ceiling is what does the narrowing, not the role default.
            //
            // Business Intelligence is withdrawn from Staff entirely rather than merely
            // narrowed - even its own dashboard, which every other role gets as a landing page,
            // is not theirs. The module reports inside Membership, Sales, Payments and
            // Inventory stay reachable regardless, since each is guarded by its own module.
            var modules = ModulesFor(EnterpriseTier.Small, ErpRoles.Staff);

            Assert.Equal(
                new[]
                {
                    ErpModules.Membership, ErpModules.Payments, ErpModules.Sales,
                    ErpModules.Inventory
                },
                modules);

            Assert.DoesNotContain(ErpModules.BusinessIntelligence, modules);

            Assert.Equal(
                new[] { ErpModules.Membership, ErpModules.Payments },
                ModulesFor(EnterpriseTier.Micro, ErpRoles.Staff));
        }

        /// <summary>
        /// Staff hold no part of Business Intelligence at all, dashboard included - the module
        /// is an Admin/Manager concern, full stop, and a Staff account lands on its first
        /// reachable module (Membership) instead of a dashboard.
        /// </summary>
        [Fact]
        public void Staff_get_no_dashboard_reports_or_analytics()
        {
            var submodules = SubmodulesFor(EnterpriseTier.Small, ErpRoles.Staff);

            Assert.DoesNotContain(ErpModules.Sub.ExecutiveDashboard, submodules);
            Assert.DoesNotContain(ErpModules.Sub.OperationalReports, submodules);
            Assert.DoesNotContain(ErpModules.Sub.KpiDashboard, submodules);
            Assert.DoesNotContain(ErpModules.Sub.SalesReports, submodules);
        }

        [Fact]
        public void Admin_on_a_small_tenant_gets_employees_and_payroll()
        {
            var modules = ModulesFor(EnterpriseTier.Small, ErpRoles.Admin);

            Assert.Contains(ErpModules.Employees, modules);
            Assert.Contains(ErpModules.Payroll, modules);
        }

        [Fact]
        public void Staff_do_not_get_employees_or_payroll_by_default()
        {
            var modules = ModulesFor(EnterpriseTier.Small, ErpRoles.Staff);

            Assert.DoesNotContain(ErpModules.Employees, modules);
            Assert.DoesNotContain(ErpModules.Payroll, modules);
            Assert.DoesNotContain(ErpModules.SystemAdmin, modules);
        }

        /// <summary>
        /// A manager runs the gym; the owner administers it. Accounts, roles and the audit
        /// trail stay with the owner, so System Administration is withheld from Manager at the
        /// module level rather than screen by screen.
        /// </summary>
        [Fact]
        public void Manager_does_not_get_system_administration()
        {
            var manager = ModulesFor(EnterpriseTier.Medium, ErpRoles.Manager);
            var admin = ModulesFor(EnterpriseTier.Medium, ErpRoles.Admin);

            Assert.DoesNotContain(ErpModules.SystemAdmin, manager);
            Assert.Contains(ErpModules.SystemAdmin, admin);

            // Finance is ordinary management work and stays with the manager.
            Assert.Contains(ErpModules.Finance, manager);
        }

        // ------------------------------------------------------------------ the tier gate

        /// <summary>
        /// Micro is the front desk alone: memberships and the money taken for them. Everything
        /// else - the till, the stockroom, the staff, the books - is the Small expansion, and a
        /// Micro tenant must not reach any of it however senior the account is. This is what
        /// stops Tenant A being served the Tenant B feature set.
        /// </summary>
        [Fact]
        public void A_micro_tenant_gets_membership_and_payments_and_nothing_else()
        {
            var modules = ModulesFor(EnterpriseTier.Micro, ErpRoles.Admin);

            Assert.Contains(ErpModules.Membership, modules);
            Assert.Contains(ErpModules.Payments, modules);

            Assert.DoesNotContain(ErpModules.Sales, modules);
            Assert.DoesNotContain(ErpModules.Inventory, modules);
            Assert.DoesNotContain(ErpModules.Employees, modules);
            Assert.DoesNotContain(ErpModules.Payroll, modules);
            Assert.DoesNotContain(ErpModules.Finance, modules);
            Assert.DoesNotContain(ErpModules.SystemAdmin, modules);
            Assert.DoesNotContain(ErpModules.BusinessIntelligence, modules);
        }

        /// <summary>
        /// A Micro tenant has no Business Intelligence module, but is not left without reports.
        /// Each module report is guarded by the module it reports on, so the two modules Micro
        /// does hold bring their own reporting with them.
        /// </summary>
        [Fact]
        public void A_micro_manager_still_reaches_membership_and_payment_reports()
        {
            var submodules = SubmodulesFor(EnterpriseTier.Micro, ErpRoles.Manager);

            Assert.Contains(ErpModules.Sub.MembershipReports, submodules);
            Assert.Contains(ErpModules.Sub.PaymentReports, submodules);

            Assert.DoesNotContain(ErpModules.Sub.SalesReports, submodules);
            Assert.DoesNotContain(ErpModules.Sub.InventoryReports, submodules);
        }

        [Fact]
        public void An_override_cannot_grant_a_module_outside_the_tier()
        {
            var modules = ModulesFor(
                EnterpriseTier.Micro, ErpRoles.Staff, Override(ErpModules.Payroll, granted: true));

            Assert.DoesNotContain(ErpModules.Payroll, modules);
        }

        /// <summary>
        /// The FitCore licensing matrix in full. Micro is the two front-desk modules and
        /// nothing else - no till, no stockroom, no books, and no administration screens, all
        /// of which a Micro tenant has provisioned for it rather than running itself.
        /// </summary>
        [Fact]
        public void A_micro_tenant_gets_exactly_the_micro_matrix()
        {
            Assert.Equal(
                new[] { ErpModules.Membership, ErpModules.Payments },
                ModulesFor(EnterpriseTier.Micro, ErpRoles.Admin));
        }

        /// <summary>
        /// Small is a whole single-site gym: everything except System Administration, which is
        /// where accounts, roles and the branch network are administered.
        /// </summary>
        [Fact]
        public void A_small_tenant_gets_exactly_the_small_matrix()
        {
            Assert.Equal(
                new[]
                {
                    ErpModules.Membership, ErpModules.Payments, ErpModules.Sales,
                    ErpModules.Inventory, ErpModules.Employees, ErpModules.Payroll,
                    ErpModules.Finance, ErpModules.BusinessIntelligence
                },
                ModulesFor(EnterpriseTier.Small, ErpRoles.Admin));
        }

        [Fact]
        public void A_medium_tenant_gets_all_nine()
        {
            Assert.Equal(
                ErpModules.All.Select(m => m.Key),
                ModulesFor(EnterpriseTier.Medium, ErpRoles.Admin));
        }

        /// <summary>
        /// System Administration is the one module the Medium tier adds, and branching is the
        /// reason it matters: a branch divides the company, so the screen that creates one
        /// belongs with the screens that create accounts and roles.
        /// </summary>
        [Fact]
        public void System_administration_is_the_one_module_that_needs_the_medium_tier()
        {
            var mediumOnly = ErpModules.All
                .Where(m => m.MinimumTier == EnterpriseTier.Medium)
                .Select(m => m.Key)
                .ToArray();

            Assert.Equal(new[] { ErpModules.SystemAdmin }, mediumOnly);
        }

        [Fact]
        public void The_small_expansion_is_the_shop_the_workforce_the_books_and_insight()
        {
            var smallOnly = ErpModules.All
                .Where(m => m.MinimumTier == EnterpriseTier.Small)
                .Select(m => m.Key)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                new[]
                {
                    ErpModules.BusinessIntelligence, ErpModules.Employees, ErpModules.Finance,
                    ErpModules.Inventory, ErpModules.Payroll, ErpModules.Sales
                }.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
                smallOnly);
        }

        /// <summary>
        /// No grant of any kind reaches a Medium module on a smaller plan - not an override,
        /// not the most senior role, not both together. The ceiling is the company's, not the
        /// person's.
        /// </summary>
        [Fact]
        public void No_medium_module_reaches_a_micro_or_small_tenant()
        {
            foreach (var tier in new[] { EnterpriseTier.Micro, EnterpriseTier.Small })
            {
                var modules = ModulesFor(
                    tier, ErpRoles.SuperAdmin, Override(ErpModules.SystemAdmin, granted: true));

                Assert.DoesNotContain(ErpModules.SystemAdmin, modules);
            }
        }

        /// <summary>
        /// And nothing above Micro reaches a Micro tenant either. Sales and Inventory moved up
        /// to Small, so the same ceiling that has always held Finance out now holds those two.
        /// </summary>
        [Fact]
        public void No_small_module_reaches_a_micro_tenant()
        {
            foreach (var module in new[]
                     {
                         ErpModules.Sales, ErpModules.Inventory, ErpModules.Employees,
                         ErpModules.Payroll, ErpModules.Finance, ErpModules.BusinessIntelligence
                     })
            {
                var modules = ModulesFor(
                    EnterpriseTier.Micro, ErpRoles.SuperAdmin, Override(module, granted: true));

                Assert.DoesNotContain(module, modules);
            }
        }

        // ------------------------------------------------------------------ subfeature gate

        /// <summary>
        /// System Administration is Medium, so a Small owner reaches none of it - not user
        /// management, not the audit trail, not the branch network. They run a whole gym,
        /// including its books; what they do not do is administer the installation.
        /// </summary>
        [Fact]
        public void A_small_owner_runs_the_gym_but_administers_nothing()
        {
            var submodules = SubmodulesFor(EnterpriseTier.Small, ErpRoles.Admin);

            Assert.Contains(ErpModules.Sub.FinanceOverview, submodules);
            Assert.Contains(ErpModules.Sub.FinancialPeriods, submodules);
            Assert.Contains(ErpModules.Sub.PayrollRecords, submodules);

            Assert.DoesNotContain(ErpModules.Sub.Users, submodules);
            Assert.DoesNotContain(ErpModules.Sub.Roles, submodules);
            Assert.DoesNotContain(ErpModules.Sub.Permissions, submodules);
            Assert.DoesNotContain(ErpModules.Sub.AuditLogs, submodules);
            Assert.DoesNotContain(ErpModules.Sub.Settings, submodules);
            Assert.DoesNotContain(ErpModules.Sub.Branches, submodules);
        }

        /// <summary>
        /// Branching is Medium and the Admin/Owner's alone. A Manager runs a branch; they do
        /// not decide that it exists, and they certainly do not decide which branch they are
        /// in - their account is bound to one.
        /// </summary>
        [Fact]
        public void Branch_administration_is_medium_and_admin_only()
        {
            Assert.Contains(
                ErpModules.Sub.Branches, SubmodulesFor(EnterpriseTier.Medium, ErpRoles.Admin));

            Assert.DoesNotContain(
                ErpModules.Sub.Branches, SubmodulesFor(EnterpriseTier.Medium, ErpRoles.Manager));
            Assert.DoesNotContain(
                ErpModules.Sub.Branches, SubmodulesFor(EnterpriseTier.Medium, ErpRoles.Staff));
            Assert.DoesNotContain(
                ErpModules.Sub.Branches, SubmodulesFor(EnterpriseTier.Small, ErpRoles.Admin));
        }

        /// <summary>
        /// The same for the branch comparison: it reads across every branch on purpose, so
        /// showing it to somebody bound to one branch would hand them their siblings' figures.
        /// </summary>
        [Fact]
        public void Branch_performance_is_medium_and_admin_only()
        {
            Assert.Contains(
                ErpModules.Sub.BranchPerformance,
                SubmodulesFor(EnterpriseTier.Medium, ErpRoles.Admin));

            Assert.DoesNotContain(
                ErpModules.Sub.BranchPerformance,
                SubmodulesFor(EnterpriseTier.Medium, ErpRoles.Manager));
            Assert.DoesNotContain(
                ErpModules.Sub.BranchPerformance,
                SubmodulesFor(EnterpriseTier.Small, ErpRoles.Admin));
        }

        [Fact]
        public void A_medium_owner_gets_user_management()
        {
            var submodules = SubmodulesFor(EnterpriseTier.Medium, ErpRoles.Admin);

            Assert.Contains(ErpModules.Sub.Users, submodules);
            Assert.Contains(ErpModules.Sub.Roles, submodules);
            Assert.Contains(ErpModules.Sub.Permissions, submodules);
        }

        /// <summary>
        /// Platform administration belongs to the Super Admin alone. An owner runs a company;
        /// only the platform account runs the tenants - on any tier, at any seniority below it.
        /// </summary>
        [Theory]
        [InlineData(ErpModules.Sub.PlatformTenants)]
        [InlineData(ErpModules.Sub.PlatformSubscriptions)]
        [InlineData(ErpModules.Sub.PlatformUsers)]
        [InlineData(ErpModules.Sub.PlatformSettings)]
        [InlineData(ErpModules.Sub.PlatformDashboard)]
        public void Platform_administration_is_reserved_for_the_super_admin(string submodule)
        {
            foreach (var tier in new[] { EnterpriseTier.Micro, EnterpriseTier.Small, EnterpriseTier.Medium })
            {
                Assert.DoesNotContain(submodule, SubmodulesFor(tier, ErpRoles.Admin));
                Assert.DoesNotContain(submodule, SubmodulesFor(tier, ErpRoles.Manager));
                Assert.DoesNotContain(submodule, SubmodulesFor(tier, ErpRoles.Staff));
            }

            // The Super Admin reaches them on Medium, which is the tier the platform company is
            // registered on. Not on a smaller plan: the tier ceiling applies to the platform
            // account exactly as it applies to everybody else, and that is the whole reason the
            // platform company is Medium rather than Micro.
            Assert.Contains(submodule, SubmodulesFor(EnterpriseTier.Medium, ErpRoles.SuperAdmin));

            Assert.DoesNotContain(submodule, SubmodulesFor(EnterpriseTier.Micro, ErpRoles.SuperAdmin));
            Assert.DoesNotContain(submodule, SubmodulesFor(EnterpriseTier.Small, ErpRoles.SuperAdmin));
        }

        /// <summary>
        /// A subfeature is derived from its module, so withdrawing the module takes every
        /// screen underneath it. There is no way to hold one without the other.
        /// </summary>
        [Fact]
        public void Withdrawing_a_module_removes_every_subfeature_beneath_it()
        {
            var withheld = PermissionResolver.Resolve(
                EnterpriseTier.Medium,
                ErpRoles.DefaultModulesFor(ErpRoles.Admin),
                new[] { Override(ErpModules.Finance, granted: false) });

            var submodules = PermissionResolver.ResolveSubmodules(
                EnterpriseTier.Medium, ErpRoles.LevelOf(ErpRoles.Admin), withheld);

            Assert.DoesNotContain(ErpModules.Finance, withheld);
            Assert.DoesNotContain(submodules, s => s.StartsWith("finance.", StringComparison.Ordinal));

            // Its neighbours are untouched.
            Assert.Contains(ErpModules.Sub.Members, submodules);
        }

        [Fact]
        public void A_micro_tenant_reaches_no_finance_or_analytics_subfeature()
        {
            var submodules = SubmodulesFor(EnterpriseTier.Micro, ErpRoles.Admin);

            Assert.DoesNotContain(ErpModules.Sub.Expenses, submodules);
            Assert.DoesNotContain(ErpModules.Sub.GeneralLedger, submodules);
            Assert.DoesNotContain(ErpModules.Sub.KpiDashboard, submodules);
            Assert.DoesNotContain(ErpModules.Sub.FinanceAnalytics, submodules);

            // Nor the executive dashboard, which now belongs to Business Intelligence and so
            // begins at Small. What a Micro tenant keeps is the reporting that belongs to the
            // two modules it actually holds.
            Assert.DoesNotContain(ErpModules.Sub.ExecutiveDashboard, submodules);
            Assert.DoesNotContain(ErpModules.Sub.OperationalReports, submodules);

            Assert.Contains(ErpModules.Sub.MembershipReports, submodules);
            Assert.Contains(ErpModules.Sub.PaymentReports, submodules);
        }

        [Fact]
        public void IsSubmoduleAllowed_agrees_with_the_resolved_list()
        {
            foreach (var tier in new[] { EnterpriseTier.Micro, EnterpriseTier.Small, EnterpriseTier.Medium })
            {
                foreach (var role in new[] { ErpRoles.SuperAdmin, ErpRoles.Admin, ErpRoles.Manager, ErpRoles.Staff })
                {
                    var modules = ModulesFor(tier, role);
                    var resolved = PermissionResolver.ResolveSubmodules(tier, ErpRoles.LevelOf(role), modules);

                    foreach (var definition in ErpModules.Submodules)
                    {
                        var allowed = ErpModules.IsSubmoduleAllowed(
                            definition.Key, tier, ErpRoles.LevelOf(role),
                            m => modules.Contains(m, StringComparer.OrdinalIgnoreCase));

                        Assert.Equal(resolved.Contains(definition.Key), allowed);
                    }
                }
            }
        }

        [Fact]
        public void An_unknown_submodule_is_refused_rather_than_waved_through()
        {
            Assert.False(ErpModules.IsSubmoduleAllowed(
                "finance.does-not-exist", EnterpriseTier.Medium, 0, _ => true));

            Assert.False(ErpModules.IsSubmoduleAllowed(
                null, EnterpriseTier.Medium, 0, _ => true));
        }

        // ------------------------------------------------------------------ legacy keys

        /// <summary>
        /// Permission rows written before the catalogue was consolidated still name modules
        /// that are now subfeatures. An administrator's deliberate grant must keep meaning what
        /// they intended rather than silently becoming nothing.
        /// </summary>
        [Theory]
        [InlineData("dashboard", ErpModules.BusinessIntelligence)]
        [InlineData("reports", ErpModules.BusinessIntelligence)]
        [InlineData("expenses", ErpModules.Finance)]
        [InlineData("useraccess", ErpModules.SystemAdmin)]
        public void A_retired_module_key_maps_to_the_module_that_absorbed_it(string legacy, string current)
        {
            Assert.Equal(current, ErpModules.Normalise(legacy));
            Assert.True(ErpModules.IsLegacyKey(legacy));
            Assert.True(ErpModules.IsKnown(legacy));
        }

        [Fact]
        public void A_role_granted_only_retired_keys_still_resolves_to_the_new_modules()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Medium,
                new[] { "dashboard", "reports", "expenses", "useraccess" },
                Array.Empty<AppUserPermission>());

            Assert.Contains(ErpModules.BusinessIntelligence, modules);
            Assert.Contains(ErpModules.Finance, modules);
            Assert.Contains(ErpModules.SystemAdmin, modules);
        }

        /// <summary>
        /// Two retired keys can land on the same module - "dashboard" and "reports" both became
        /// Business Intelligence. If one was granted and the other explicitly withheld, the
        /// withholding wins: an administrator who took something away must not have it handed
        /// back by a key they never touched.
        /// </summary>
        [Fact]
        public void Conflicting_retired_overrides_resolve_to_the_withholding()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Medium,
                ErpRoles.DefaultModulesFor(ErpRoles.Admin),
                new[] { Override("dashboard", true), Override("reports", false) });

            Assert.DoesNotContain(ErpModules.BusinessIntelligence, modules);
        }

        [Fact]
        public void A_retired_key_is_not_offered_as_a_module_in_its_own_right()
        {
            Assert.DoesNotContain("dashboard", ErpModules.All.Select(m => m.Key));
            Assert.DoesNotContain("reports", ErpModules.All.Select(m => m.Key));
            Assert.DoesNotContain("expenses", ErpModules.All.Select(m => m.Key));
            Assert.DoesNotContain("useraccess", ErpModules.All.Select(m => m.Key));
        }

        // ------------------------------------------------------------------ overrides

        [Fact]
        public void An_override_grants_a_module_the_role_withholds()
        {
            var modules = ModulesFor(
                EnterpriseTier.Small, ErpRoles.Staff, Override(ErpModules.Employees, granted: true));

            Assert.Contains(ErpModules.Employees, modules);

            // Granting one module must not quietly bring its neighbours with it.
            Assert.DoesNotContain(ErpModules.Payroll, modules);
        }

        [Fact]
        public void An_override_withholds_a_module_the_role_grants()
        {
            var modules = ModulesFor(
                EnterpriseTier.Small, ErpRoles.Manager, Override(ErpModules.Payroll, granted: false));

            Assert.DoesNotContain(ErpModules.Payroll, modules);
            Assert.Contains(ErpModules.Employees, modules);
        }

        [Fact]
        public void Module_keys_are_matched_without_regard_to_case()
        {
            var modules = PermissionResolver.Resolve(
                EnterpriseTier.Small,
                new[] { "MEMBERSHIP", "Employees" },
                new[] { Override("PAYROLL", granted: true) });

            Assert.Contains(ErpModules.Membership, modules);
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

        /// <summary>
        /// The subfeature editor explains a refusal rather than merely showing an empty box:
        /// the plan, the seniority and the parent module are reported separately so an
        /// administrator can see which one to change.
        /// </summary>
        [Fact]
        public void DescribeSubmodules_separates_the_three_reasons_a_feature_is_unavailable()
        {
            var rows = PermissionResolver.DescribeSubmodules(
                EnterpriseTier.Small,
                ErpRoles.LevelOf(ErpRoles.Staff),
                ModulesFor(EnterpriseTier.Small, ErpRoles.Staff));

            Assert.Equal(ErpModules.Submodules.Count, rows.Count);

            // Held outright.
            var members = rows.Single(r => r.Submodule == ErpModules.Sub.Members);
            Assert.True(members.Effective);

            // The plan has it and the module is held; the role is what stops it. Purchasing
            // rather than the Business Intelligence reports, now that Staff hold no part of
            // Business Intelligence at all and so would fail on the module instead.
            var purchases = rows.Single(r => r.Submodule == ErpModules.Sub.Purchases);
            Assert.True(purchases.AvailableInTier);
            Assert.True(purchases.HoldsParentModule);
            Assert.False(purchases.AllowedForRole);
            Assert.False(purchases.Effective);

            // The plan is what stops this one.
            var branches = rows.Single(r => r.Submodule == ErpModules.Sub.Branches);
            Assert.False(branches.AvailableInTier);
            Assert.Equal(nameof(EnterpriseTier.Medium), branches.MinimumTierName);

            // And this one is simply not their module.
            var attendance = rows.Single(r => r.Submodule == ErpModules.Sub.Attendance);
            Assert.False(attendance.HoldsParentModule);
        }

        // ------------------------------------------------------------------ catalogue

        [Fact]
        public void The_role_hierarchy_runs_admin_then_manager_then_staff()
        {
            Assert.True(ErpRoles.LevelOf(ErpRoles.SuperAdmin) < ErpRoles.LevelOf(ErpRoles.Admin));
            Assert.True(ErpRoles.LevelOf(ErpRoles.Admin) < ErpRoles.LevelOf(ErpRoles.Manager));
            Assert.True(ErpRoles.LevelOf(ErpRoles.Manager) < ErpRoles.LevelOf(ErpRoles.Staff));
        }

        [Fact]
        public void Every_role_default_names_a_module_that_actually_exists()
        {
            foreach (var role in new[] { ErpRoles.SuperAdmin, ErpRoles.Admin, ErpRoles.Manager, ErpRoles.Staff })
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
