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
        /// <summary>
        /// The front desk and nothing else: Membership Management, Payment Management, and the
        /// reports that read those two. A Micro gym takes memberships and takes money; it does
        /// not run a shop, so it has neither Sales nor Inventory.
        /// </summary>
        Micro = 1,

        /// <summary>
        /// A single-site gym running as a business: everything in Micro plus Sales, Inventory,
        /// Employee Management, Payroll Management, Finance Management and Business
        /// Intelligence. System Administration is not included - a Small tenant's accounts are
        /// provisioned for it rather than administered by it.
        /// </summary>
        Small = 2,

        /// <summary>
        /// Everything, including System Administration and the branch network underneath it,
        /// the full cross-module analytics set, and the branch-aware Business Intelligence
        /// built on top of both.
        /// </summary>
        Medium = 3
    }

    /// <summary>
    /// One of the nine FitCore business modules.
    ///
    /// A module is the unit a permission is granted over, the unit the sidebar is built from,
    /// and the unit <c>[RequireModule]</c> guards. Keys are stable strings because they are
    /// stored in the database and travel in a token; the display names are presentation only.
    ///
    /// <see cref="MinimumTier"/> is the second half of an access decision. A permission row
    /// says the user is allowed the module; the tier says the company has it at all. Both must
    /// agree, which is what stops a Micro tenant from reaching Employees or Payroll even if
    /// someone grants the permission by mistake.
    /// </summary>
    public sealed record ErpModuleDefinition(
        string Key,
        string DisplayName,
        EnterpriseTier MinimumTier,
        string Group);

    /// <summary>
    /// A subfeature that lives underneath one of the nine modules.
    ///
    /// Submodules are never top-level. They exist so the module catalogue can describe a whole
    /// ERP - purchasing, the general ledger, payslips, platform administration - without the
    /// navigation growing a tenth, eleventh and twelfth main entry. Everything a FitCore user
    /// can do belongs to exactly one of the nine.
    ///
    /// A submodule is *not* separately granted. It is derived from three facts the token
    /// already carries: the parent module the user holds, the company's tier, and the user's
    /// role level. That keeps the token small and means a submodule can never be granted
    /// without its module, or above the company's plan.
    /// </summary>
    /// <param name="Key">Stable key, always <c>module.submodule</c>.</param>
    /// <param name="ModuleKey">The main module this belongs to. Always one of the nine.</param>
    /// <param name="MinimumTier">
    /// The plan a company needs before this subfeature appears. A submodule may sit above its
    /// parent - Business Intelligence is available to every tenant, but its cross-module
    /// analytics are a Medium feature.
    /// </param>
    /// <param name="MinimumRoleLevel">
    /// The least senior role that may use it, on <see cref="ErpRoles.LevelOf"/>'s scale where
    /// a lower number is more senior. 3 admits everybody down to Staff; 0 is Super Admin alone.
    /// </param>
    public sealed record ErpSubmoduleDefinition(
        string Key,
        string ModuleKey,
        string DisplayName,
        EnterpriseTier MinimumTier,
        int MinimumRoleLevel,
        string Description);

    public static class ErpModules
    {
        // ------------------------------------------------------------------ the nine

        public const string Membership = "membership";
        public const string Payments   = "payments";
        public const string Sales      = "sales";
        public const string Inventory  = "inventory";
        public const string Employees  = "employees";
        public const string Payroll    = "payroll";
        public const string Finance    = "finance";
        public const string SystemAdmin = "systemadmin";
        public const string BusinessIntelligence = "businessintelligence";

        /// <summary>
        /// The nine FitCore business modules, in the order the sidebar presents them.
        ///
        /// This list is closed. Every feature the product grows belongs underneath one of
        /// these as a submodule - see <see cref="Submodules"/> - rather than beside them as a
        /// tenth entry. The operational flow reads down the list: membership brings people in,
        /// payments and sales take their money, inventory supplies the goods, employees and
        /// payroll run the staff, finance records the consequences, system administration
        /// controls who may do any of it, and business intelligence reports on all of it.
        ///
        /// The tier boundaries are the FitCore licensing matrix. Micro is the front desk alone -
        /// memberships and the money taken for them. Small is the whole of a single gym as a
        /// business: the shop, the stockroom, the staff, the payroll and the books. Medium adds
        /// System Administration, and with it the branch network and the cross-branch analytics
        /// that only mean anything once a company runs more than one site.
        ///
        /// Note that Sales and Inventory are Small rather than Micro: a Micro tenant sells
        /// memberships, not merchandise, so a till and a stockroom it has no goods for would be
        /// two modules that can only report being empty.
        /// </summary>
        public static readonly IReadOnlyList<ErpModuleDefinition> All = new[]
        {
            new ErpModuleDefinition(Membership, "Membership Management", EnterpriseTier.Micro, "Operations"),
            new ErpModuleDefinition(Payments,   "Payment Management",    EnterpriseTier.Micro, "Operations"),
            new ErpModuleDefinition(Sales,      "Sales Management",      EnterpriseTier.Small, "Operations"),
            new ErpModuleDefinition(Inventory,  "Inventory Management",  EnterpriseTier.Small, "Operations"),
            new ErpModuleDefinition(Employees,  "Employee Management",   EnterpriseTier.Small, "Operations"),
            new ErpModuleDefinition(Payroll,    "Payroll Management",    EnterpriseTier.Small, "Operations"),

            new ErpModuleDefinition(Finance,    "Finance Management",    EnterpriseTier.Small, "Finance"),

            // System Administration is Medium alone. It is where accounts, roles, permissions
            // and - since branching - the company's branch network are administered, and all
            // four are things a Micro or Small tenant has provisioned for it rather than
            // running itself. It is also where the Super Admin's platform panel lives.
            new ErpModuleDefinition(SystemAdmin, "System Administration", EnterpriseTier.Medium, "System"),

            // Business Intelligence starts at Small, where there is finally more than one kind
            // of number to put beside another. A Micro tenant still gets the reports for the two
            // modules it holds, because a module report is guarded by the module it reports on.
            new ErpModuleDefinition(BusinessIntelligence,
                                    "Business Intelligence", EnterpriseTier.Small, "Insight")
        };

        // ------------------------------------------------------------------ submodule keys

        /// <summary>Every submodule key, grouped by the module it belongs to.</summary>
        public static class Sub
        {
            // Membership Management
            public const string Members            = "membership.members";
            public const string MembershipPlans    = "membership.plans";
            public const string Subscriptions      = "membership.subscriptions";
            public const string MemberHistory      = "membership.history";
            public const string MembershipReports  = "membership.reports";

            // Payment Management
            public const string PaymentTransactions = "payments.transactions";
            public const string Receivables         = "payments.receivables";
            public const string PaymentReconciliation = "payments.reconciliation";
            public const string PaymentReports      = "payments.reports";

            // Sales Management
            public const string PointOfSale        = "sales.pos";
            public const string SalesHistory       = "sales.history";
            public const string SalesReturns       = "sales.returns";
            public const string Customers          = "sales.customers";
            public const string SalesReports       = "sales.reports";

            // Inventory Management
            public const string Products           = "inventory.products";
            public const string Stock              = "inventory.stock";
            public const string StockMovements     = "inventory.movements";
            public const string Suppliers          = "inventory.suppliers";
            public const string Purchases          = "inventory.purchases";
            public const string InventoryValuation = "inventory.valuation";
            public const string InventoryReports   = "inventory.reports";

            // Employee Management
            public const string EmployeeRecords    = "employees.records";
            public const string Attendance         = "employees.attendance";
            public const string Leave              = "employees.leave";
            public const string EmployeeReports    = "employees.reports";

            // Payroll Management
            public const string PayrollRecords     = "payroll.records";
            public const string PayrollAttendance  = "payroll.attendance";
            public const string PayrollCalculation = "payroll.calculation";
            public const string Payslips           = "payroll.payslips";
            public const string PayrollHistory     = "payroll.history";
            public const string PayrollReports     = "payroll.reports";

            // Finance Management
            public const string FinanceOverview    = "finance.overview";
            public const string Expenses           = "finance.expenses";
            public const string ChartOfAccounts    = "finance.accounts";
            public const string JournalEntries     = "finance.journal";
            public const string GeneralLedger      = "finance.ledger";
            public const string AccountsReceivable = "finance.receivable";
            public const string AccountsPayable    = "finance.payable";
            public const string Banking            = "finance.banking";
            public const string BankReconciliation = "finance.reconciliation";
            public const string Budgets            = "finance.budgets";
            public const string FinancialPeriods   = "finance.periods";
            public const string FinancialReports   = "finance.reports";

            // System Administration
            public const string Users              = "systemadmin.users";
            public const string Roles              = "systemadmin.roles";
            public const string Permissions        = "systemadmin.permissions";

            /// <summary>
            /// The company's branch network. Medium only, and the Admin/Owner's alone: a branch
            /// is a division of the company itself, so who may create one is the same question
            /// as who may create a user.
            /// </summary>
            public const string Branches           = "systemadmin.branches";
            public const string Settings           = "systemadmin.settings";
            public const string AuditLogs          = "systemadmin.audit";
            public const string Security           = "systemadmin.security";
            public const string DataIntegrity      = "systemadmin.integrity";

            // System Administration - platform level, Super Admin only.
            public const string PlatformTenants      = "systemadmin.platform.tenants";
            public const string PlatformSubscriptions = "systemadmin.platform.subscriptions";
            public const string PlatformPlans        = "systemadmin.platform.plans";
            public const string PlatformUsers        = "systemadmin.platform.users";
            public const string PlatformSettings     = "systemadmin.platform.settings";
            public const string PlatformAudit        = "systemadmin.platform.audit";
            public const string PlatformMonitoring   = "systemadmin.platform.monitoring";

            // Business Intelligence
            public const string ExecutiveDashboard = "businessintelligence.dashboard";
            public const string OperationalReports = "businessintelligence.reports";
            public const string KpiDashboard       = "businessintelligence.kpi";
            public const string MembershipAnalytics = "businessintelligence.membership";
            public const string SalesAnalytics     = "businessintelligence.sales";
            public const string PaymentAnalytics   = "businessintelligence.payments";
            public const string InventoryAnalytics = "businessintelligence.inventory";
            public const string WorkforceAnalytics = "businessintelligence.workforce";
            public const string FinanceAnalytics   = "businessintelligence.finance";
            public const string ProfitabilityAnalytics = "businessintelligence.profitability";

            /// <summary>
            /// The whole company beside each of its branches. Admin only, because a Manager is
            /// bound to one branch and a comparison is by definition everybody else's figures.
            /// </summary>
            public const string BranchPerformance   = "businessintelligence.branches";

            // Business Intelligence - platform level, Super Admin only.
            public const string PlatformDashboard  = "businessintelligence.platform.dashboard";
            public const string PlatformAnalytics  = "businessintelligence.platform.analytics";
        }

        private const int Staff = 3;
        private const int Manager = 2;
        private const int Admin = 1;
        private const int SuperAdminOnly = 0;

        /// <summary>
        /// Every subfeature FitCore offers, and the module it belongs underneath.
        ///
        /// Nothing here is reachable on its own: a submodule is only ever offered to somebody
        /// who already holds its parent module, on a plan that includes it, at a senior enough
        /// role. See <see cref="IsSubmoduleAllowed"/>, which is the single place that decides.
        /// </summary>
        public static readonly IReadOnlyList<ErpSubmoduleDefinition> Submodules = new[]
        {
            // -------------------------------------------------------------- 1. Membership
            new ErpSubmoduleDefinition(Sub.Members, Membership, "Members",
                EnterpriseTier.Micro, Staff, "Registration, profiles, status, search and archiving."),
            new ErpSubmoduleDefinition(Sub.MembershipPlans, Membership, "Membership Plans",
                EnterpriseTier.Micro, Staff, "The plans a membership can be bought on."),
            new ErpSubmoduleDefinition(Sub.Subscriptions, Membership, "Subscriptions",
                EnterpriseTier.Micro, Staff, "Activation, renewal, suspension, expiry and cancellation."),
            new ErpSubmoduleDefinition(Sub.MemberHistory, Membership, "Member History",
                EnterpriseTier.Micro, Staff, "One member's subscriptions, payments, sales and notes."),
            new ErpSubmoduleDefinition(Sub.MembershipReports, Membership, "Membership Reports",
                EnterpriseTier.Micro, Manager, "Joins, renewals, expiries and membership revenue."),

            // -------------------------------------------------------------- 2. Payments
            new ErpSubmoduleDefinition(Sub.PaymentTransactions, Payments, "Payment Transactions",
                EnterpriseTier.Micro, Staff, "Recording, methods, references, status and refunds."),
            new ErpSubmoduleDefinition(Sub.Receivables, Payments, "Outstanding Balances",
                EnterpriseTier.Micro, Manager, "What is still owed on live sales and memberships."),
            // Small, because reconciliation compares the takings against the ledger and Small
            // is where the ledger begins.
            new ErpSubmoduleDefinition(Sub.PaymentReconciliation, Payments, "Reconciliation",
                EnterpriseTier.Small, Manager, "Matching takings against the bank and the ledger."),
            new ErpSubmoduleDefinition(Sub.PaymentReports, Payments, "Payment Reports",
                EnterpriseTier.Micro, Manager, "Collections by day, method and category."),

            // -------------------------------------------------------------- 3. Sales
            new ErpSubmoduleDefinition(Sub.PointOfSale, Sales, "New Sale",
                EnterpriseTier.Small, Staff, "The till: lines, discounts, stock check and receipt."),
            new ErpSubmoduleDefinition(Sub.SalesHistory, Sales, "Sales History",
                EnterpriseTier.Small, Staff, "Every transaction, searchable and filterable by date."),
            new ErpSubmoduleDefinition(Sub.SalesReturns, Sales, "Returns & Refunds",
                EnterpriseTier.Small, Staff, "Returning goods, restoring stock and refunding money."),
            new ErpSubmoduleDefinition(Sub.SalesReports, Sales, "Sales Reports",
                EnterpriseTier.Small, Manager, "Revenue, units, top products and margin."),

            // -------------------------------------------------------------- 4. Inventory
            new ErpSubmoduleDefinition(Sub.Products, Inventory, "Products",
                EnterpriseTier.Small, Staff, "The catalogue, its categories and its prices."),
            new ErpSubmoduleDefinition(Sub.Stock, Inventory, "Stock",
                EnterpriseTier.Small, Staff, "Quantity on hand, reorder levels and adjustments."),
            new ErpSubmoduleDefinition(Sub.StockMovements, Inventory, "Stock Movements",
                EnterpriseTier.Small, Staff, "The ledger of every in, out and adjustment."),
            new ErpSubmoduleDefinition(Sub.Suppliers, Inventory, "Suppliers",
                EnterpriseTier.Small, Staff, "Who the gym buys from."),
            new ErpSubmoduleDefinition(Sub.Purchases, Inventory, "Purchases",
                EnterpriseTier.Small, Manager, "Purchase orders, receiving, and the payable they raise."),
            new ErpSubmoduleDefinition(Sub.InventoryValuation, Inventory, "Valuation",
                EnterpriseTier.Small, Manager, "What the stock on hand is worth, at weighted average cost."),
            new ErpSubmoduleDefinition(Sub.InventoryReports, Inventory, "Inventory Reports",
                EnterpriseTier.Small, Manager, "Low stock, dead stock, movement and value by category."),

            // -------------------------------------------------------------- 5. Employees
            new ErpSubmoduleDefinition(Sub.EmployeeRecords, Employees, "Employee Records",
                EnterpriseTier.Small, Manager, "Profiles, positions, departments, status and pay basis."),
            new ErpSubmoduleDefinition(Sub.Attendance, Employees, "Attendance",
                EnterpriseTier.Small, Manager, "Time in, time out, regular hours and overtime."),
            new ErpSubmoduleDefinition(Sub.Leave, Employees, "Leave",
                EnterpriseTier.Small, Manager, "Leave requests, approval and the balance remaining."),
            new ErpSubmoduleDefinition(Sub.EmployeeReports, Employees, "Employee Reports",
                EnterpriseTier.Small, Manager, "Headcount, attendance rate and overtime by employee."),

            // -------------------------------------------------------------- 6. Payroll
            new ErpSubmoduleDefinition(Sub.PayrollRecords, Payroll, "Payroll Records",
                EnterpriseTier.Small, Manager, "Pay runs, their status and their approval."),
            new ErpSubmoduleDefinition(Sub.PayrollAttendance, Payroll, "Attendance Summary",
                EnterpriseTier.Small, Manager, "The hours a run will be calculated from."),
            new ErpSubmoduleDefinition(Sub.PayrollCalculation, Payroll, "Payroll Calculation",
                EnterpriseTier.Small, Manager, "Gross, statutory deductions, employer share and net."),
            new ErpSubmoduleDefinition(Sub.Payslips, Payroll, "Payslips",
                EnterpriseTier.Small, Manager, "The printable payslip for a completed run."),
            new ErpSubmoduleDefinition(Sub.PayrollHistory, Payroll, "Payroll History",
                EnterpriseTier.Small, Manager, "Every run an employee has ever been paid."),
            new ErpSubmoduleDefinition(Sub.PayrollReports, Payroll, "Payroll Reports",
                EnterpriseTier.Small, Manager, "Salary cost, overtime cost and contributions by period."),

            // -------------------------------------------------------------- 7. Finance
            //
            // Small, matching the module. Closing a period stays Admin: it locks the books
            // against any further posting, which is an owner's decision rather than a
            // manager's, and that is true on every plan that has books at all.
            new ErpSubmoduleDefinition(Sub.FinanceOverview, Finance, "Finance Overview",
                EnterpriseTier.Small, Manager, "Revenue, cost of sales, expenses and net income at a glance."),
            new ErpSubmoduleDefinition(Sub.Expenses, Finance, "Expenses",
                EnterpriseTier.Small, Manager, "Operating expenses, their categories and their posting."),
            new ErpSubmoduleDefinition(Sub.ChartOfAccounts, Finance, "Chart of Accounts",
                EnterpriseTier.Small, Manager, "The accounts every financial transaction is posted to."),
            new ErpSubmoduleDefinition(Sub.JournalEntries, Finance, "Journal Entries",
                EnterpriseTier.Small, Manager, "Manual and automatic double-entry postings."),
            new ErpSubmoduleDefinition(Sub.GeneralLedger, Finance, "General Ledger",
                EnterpriseTier.Small, Manager, "Every posting against an account, with a running balance."),
            new ErpSubmoduleDefinition(Sub.AccountsReceivable, Finance, "Accounts Receivable",
                EnterpriseTier.Small, Manager, "What customers and members owe, aged."),
            new ErpSubmoduleDefinition(Sub.AccountsPayable, Finance, "Accounts Payable",
                EnterpriseTier.Small, Manager, "What the gym owes suppliers and statutory bodies, aged."),
            new ErpSubmoduleDefinition(Sub.Banking, Finance, "Cash & Bank",
                EnterpriseTier.Small, Manager, "Cash and bank accounts and the movements through them."),
            new ErpSubmoduleDefinition(Sub.BankReconciliation, Finance, "Bank Reconciliation",
                EnterpriseTier.Small, Manager, "Matching bank transactions against the ledger."),
            new ErpSubmoduleDefinition(Sub.Budgets, Finance, "Budgets",
                EnterpriseTier.Small, Manager, "Budget by account and period, against actuals."),
            new ErpSubmoduleDefinition(Sub.FinancialPeriods, Finance, "Financial Periods",
                EnterpriseTier.Small, Admin, "Opening and closing the periods postings are allowed into."),
            new ErpSubmoduleDefinition(Sub.FinancialReports, Finance, "Financial Reports",
                EnterpriseTier.Small, Manager, "Trial balance, income statement, balance sheet and cash flow."),

            // -------------------------------------------------------------- 8. System Administration
            new ErpSubmoduleDefinition(Sub.Users, SystemAdmin, "Users",
                EnterpriseTier.Medium, Admin, "Accounts, activation and password resets."),
            new ErpSubmoduleDefinition(Sub.Roles, SystemAdmin, "Roles",
                EnterpriseTier.Medium, Admin, "What each role grants before a user override."),
            new ErpSubmoduleDefinition(Sub.Permissions, SystemAdmin, "Permissions",
                EnterpriseTier.Medium, Admin, "Per-user departures from the role, within the tier."),

            // Branching. A branch divides the company, and every operational record written
            // afterwards belongs to one, so creating and merging branches is the Admin/Owner's
            // alone - a Manager runs a branch, they do not decide that it exists.
            new ErpSubmoduleDefinition(Sub.Branches, SystemAdmin, "Branches",
                EnterpriseTier.Medium, Admin, "The company's branches, and moving records between them."),

            new ErpSubmoduleDefinition(Sub.Settings, SystemAdmin, "Settings",
                EnterpriseTier.Medium, Admin, "Company, business and security settings for this tenant."),
            new ErpSubmoduleDefinition(Sub.AuditLogs, SystemAdmin, "Audit Logs",
                EnterpriseTier.Medium, Admin, "Who did what, to which record, when."),
            new ErpSubmoduleDefinition(Sub.Security, SystemAdmin, "Security",
                EnterpriseTier.Medium, Admin, "Sign-in activity, failed attempts and password changes."),
            new ErpSubmoduleDefinition(Sub.DataIntegrity, SystemAdmin, "Data Integrity",
                EnterpriseTier.Medium, Admin, "Whether this tenant's records still agree with each other."),

            new ErpSubmoduleDefinition(Sub.PlatformTenants, SystemAdmin, "Tenant Management",
                EnterpriseTier.Medium, SuperAdminOnly, "Companies, their databases and their provisioning."),
            new ErpSubmoduleDefinition(Sub.PlatformSubscriptions, SystemAdmin, "Subscriptions & Tiers",
                EnterpriseTier.Medium, SuperAdminOnly, "Subscription plans, terms and the tier each tenant is licensed for."),
            new ErpSubmoduleDefinition(Sub.PlatformPlans, SystemAdmin, "Plans",
                EnterpriseTier.Medium, SuperAdminOnly, "The Micro, Small and Medium price list - what each tier costs and includes."),
            new ErpSubmoduleDefinition(Sub.PlatformUsers, SystemAdmin, "Platform Users",
                EnterpriseTier.Medium, SuperAdminOnly, "Every account on the platform, across all tenants."),
            new ErpSubmoduleDefinition(Sub.PlatformSettings, SystemAdmin, "Platform Settings",
                EnterpriseTier.Medium, SuperAdminOnly, "Configuration that applies to the whole installation."),
            new ErpSubmoduleDefinition(Sub.PlatformAudit, SystemAdmin, "Platform Audit",
                EnterpriseTier.Medium, SuperAdminOnly, "The master trail: sign-in, permission and company changes."),
            new ErpSubmoduleDefinition(Sub.PlatformMonitoring, SystemAdmin, "Platform Monitoring",
                EnterpriseTier.Medium, SuperAdminOnly, "Tenant database reachability and platform health."),

            // -------------------------------------------------------------- 9. Business Intelligence
            new ErpSubmoduleDefinition(Sub.ExecutiveDashboard, BusinessIntelligence, "Dashboard",
                EnterpriseTier.Small, Staff, "Today's figures, read live from the tenant database."),
            new ErpSubmoduleDefinition(Sub.OperationalReports, BusinessIntelligence, "Reports",
                EnterpriseTier.Small, Manager, "Sales, payments, inventory and membership, filtered by date."),
            new ErpSubmoduleDefinition(Sub.KpiDashboard, BusinessIntelligence, "KPIs",
                EnterpriseTier.Medium, Manager, "The whole KPI set across all nine modules."),
            new ErpSubmoduleDefinition(Sub.MembershipAnalytics, BusinessIntelligence, "Membership Analytics",
                EnterpriseTier.Medium, Manager, "Growth, retention, renewals and plan mix over time."),
            new ErpSubmoduleDefinition(Sub.SalesAnalytics, BusinessIntelligence, "Sales Analytics",
                EnterpriseTier.Medium, Manager, "Revenue trend, basket size, product mix and growth."),
            new ErpSubmoduleDefinition(Sub.PaymentAnalytics, BusinessIntelligence, "Payment Analytics",
                EnterpriseTier.Medium, Manager, "Collection rate, method mix and ageing."),
            new ErpSubmoduleDefinition(Sub.InventoryAnalytics, BusinessIntelligence, "Inventory Analytics",
                EnterpriseTier.Medium, Manager, "Value, turnover, movement and stock risk."),
            new ErpSubmoduleDefinition(Sub.WorkforceAnalytics, BusinessIntelligence, "Workforce Analytics",
                EnterpriseTier.Medium, Manager, "Headcount, attendance, overtime and payroll cost."),
            new ErpSubmoduleDefinition(Sub.FinanceAnalytics, BusinessIntelligence, "Finance Analytics",
                EnterpriseTier.Medium, Manager, "Revenue, expenses, cash flow and net income over time."),
            new ErpSubmoduleDefinition(Sub.ProfitabilityAnalytics, BusinessIntelligence, "Profitability",
                EnterpriseTier.Medium, Manager, "Gross margin, cost of goods sold and net margin."),

            new ErpSubmoduleDefinition(Sub.BranchPerformance, BusinessIntelligence, "Branch Performance",
                EnterpriseTier.Medium, Admin,
                "The company overall and each branch beside it, with a branch-to-branch comparison."),

            new ErpSubmoduleDefinition(Sub.PlatformDashboard, BusinessIntelligence, "Platform Dashboard",
                EnterpriseTier.Medium, SuperAdminOnly, "Tenants, subscriptions, users and activity across the platform."),
            new ErpSubmoduleDefinition(Sub.PlatformAnalytics, BusinessIntelligence, "Platform Analytics",
                EnterpriseTier.Medium, SuperAdminOnly, "Company growth, tier mix, subscription revenue and usage.")
        };

        // ------------------------------------------------------------------ lookups

        public static bool IsKnown(string? key) =>
            !string.IsNullOrWhiteSpace(key) &&
            All.Any(m => string.Equals(m.Key, Normalise(key), StringComparison.OrdinalIgnoreCase));

        public static ErpModuleDefinition? Find(string? key) =>
            All.FirstOrDefault(m => string.Equals(m.Key, Normalise(key), StringComparison.OrdinalIgnoreCase));

        public static ErpSubmoduleDefinition? FindSubmodule(string? key) =>
            Submodules.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<ErpSubmoduleDefinition> SubmodulesOf(string moduleKey) =>
            Submodules.Where(s => string.Equals(s.ModuleKey, Normalise(moduleKey), StringComparison.OrdinalIgnoreCase));

        /// <summary>Modules a company on this tier is entitled to at all.</summary>
        public static IEnumerable<ErpModuleDefinition> ForTier(EnterpriseTier tier) =>
            All.Where(m => m.MinimumTier <= tier);

        /// <summary>
        /// Whether a subfeature is available, given everything that decides it.
        ///
        /// The three conditions are independent and all must hold: the user holds the parent
        /// module, the company's plan includes the subfeature, and the user is senior enough.
        /// Every caller - the sidebar, <c>[RequireSubmodule]</c>, the permission editor - goes
        /// through here, so none of them can disagree.
        /// </summary>
        public static bool IsSubmoduleAllowed(
            string? submoduleKey,
            EnterpriseTier tier,
            int roleLevel,
            Func<string, bool> holdsModule)
        {
            var definition = FindSubmodule(submoduleKey);
            if (definition is null) return false;

            if (definition.MinimumTier > tier) return false;
            if (roleLevel > definition.MinimumRoleLevel) return false;

            return holdsModule(definition.ModuleKey);
        }

        /// <summary>Every subfeature this person can actually reach, in catalogue order.</summary>
        public static List<string> ResolveSubmodules(
            EnterpriseTier tier, int roleLevel, IEnumerable<string> modules)
        {
            var held = new HashSet<string>(
                (modules ?? Enumerable.Empty<string>()).Select(Normalise),
                StringComparer.OrdinalIgnoreCase);

            return Submodules
                .Where(s => s.MinimumTier <= tier
                         && roleLevel <= s.MinimumRoleLevel
                         && held.Contains(s.ModuleKey))
                .Select(s => s.Key)
                .ToList();
        }

        // ------------------------------------------------------------------ legacy keys

        /// <summary>
        /// Module keys that used to be top-level and are now subfeatures.
        ///
        /// These are still sitting in <c>AppRolePermissions</c> and <c>AppUserPermissions</c>
        /// rows written before the catalogue was consolidated to nine, and in audit rows that
        /// must not be rewritten at all. Mapping them to the module that absorbed them means an
        /// existing grant keeps meaning what the administrator intended rather than silently
        /// becoming nothing.
        /// </summary>
        private static readonly Dictionary<string, string> LegacyKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["dashboard"]  = BusinessIntelligence,
                ["reports"]    = BusinessIntelligence,
                ["expenses"]   = Finance,
                ["useraccess"] = SystemAdmin
            };

        /// <summary>
        /// The current key for a permission value, which may have been written before the
        /// catalogue was consolidated. Unknown values are returned untouched so the caller can
        /// still reject them.
        /// </summary>
        public static string Normalise(string? key)
        {
            var value = (key ?? "").Trim();
            if (value.Length == 0) return value;

            return LegacyKeys.TryGetValue(value, out var current) ? current : value;
        }

        /// <summary>True when this value names a module that no longer exists in its own right.</summary>
        public static bool IsLegacyKey(string? key) =>
            !string.IsNullOrWhiteSpace(key) && LegacyKeys.ContainsKey(key.Trim());
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
        /// Note that since the catalogue was consolidated to nine modules, seniority is
        /// expressed *inside* a module rather than by withholding the module itself. Admin and
        /// Manager both hold System Administration, for instance; what they may do with it -
        /// user management, platform administration - is decided by the submodule's own role
        /// level. Staff deliberately still has neither Employees nor Payroll, because those
        /// concern other people's pay rather than the front desk.
        /// </summary>
        public static IReadOnlyList<string> DefaultModulesFor(string roleKey) =>
            roleKey?.ToLowerInvariant() switch
            {
                SuperAdmin => ErpModules.All.Select(m => m.Key).ToArray(),

                Admin => ErpModules.All.Select(m => m.Key).ToArray(),

                // A manager runs the gym's day-to-day finances but does not administer the
                // system: accounts, roles and the audit trail stay with the owner.
                Manager => ErpModules.All
                    .Where(m => m.Key != ErpModules.SystemAdmin)
                    .Select(m => m.Key).ToArray(),

                // The front desk: people, money and goods. Business Intelligence - including its
                // dashboard - is a Manager-and-above concern, so Staff do not hold the module at
                // all; the module reports inside Membership, Sales, Payments and Inventory stay
                // reachable regardless, since each is guarded by its own module rather than by
                // Business Intelligence.
                Staff => new[]
                {
                    ErpModules.Membership, ErpModules.Sales,
                    ErpModules.Payments, ErpModules.Inventory
                },

                _ => Array.Empty<string>()
            };
    }
}
