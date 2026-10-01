namespace ERP_Project1.Api
{
    /// <summary>
    /// Client-side copies of the authentication contracts. As with the other DTOs here, they
    /// are duplicated rather than shared so the Blazor client keeps zero project references.
    /// </summary>
    public class LoginRequestDto
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class LoginResponseDto
    {
        public string Token { get; set; } = "";
        public DateTime ExpiresAtUtc { get; set; }
        public CurrentUserDto User { get; set; } = new();
    }

    public class CurrentUserDto
    {
        public int AppUserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";

        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = "";
        public string EnterpriseTier { get; set; } = "";

        public string RoleKey { get; set; } = "";
        public string RoleDisplayName { get; set; } = "";
        public int RoleLevel { get; set; }

        public int? EmployeeId { get; set; }
        public bool MustChangePassword { get; set; }

        public List<string> Modules { get; set; } = new();

        /// <summary>
        /// The subfeatures this user may open, sent by the server alongside the modules. The
        /// desktop builds its tab strips from this rather than reasoning about tiers, so the
        /// client holds no copy of the licensing rules and cannot get them wrong.
        /// </summary>
        public List<string> Submodules { get; set; } = new();

        /// <summary>True for the Super Admin, whose workspace is the platform rather than a gym.</summary>
        public bool IsPlatformAdministrator { get; set; }

        /// <summary>
        /// The branch this account is bound to, or null for somebody who works across the whole
        /// company. Set for a branch Manager or Staff; never set for an Admin/Owner.
        /// </summary>
        public int? BranchId { get; set; }

        /// <summary>
        /// Whether the topbar draws a branch picker. Presentation only: the server refuses the
        /// branch header regardless if the account is not entitled to select one.
        /// </summary>
        public bool CanManageBranches { get; set; }

        /// <summary>Initials for the avatar, e.g. "Kris Santos" becomes "KS".</summary>
        public string Initials
        {
            get
            {
                var parts = (FullName ?? "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                return parts.Length switch
                {
                    0 => "FC",
                    1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
                    _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
                };
            }
        }

        public bool Can(string module) =>
            Modules.Any(m => string.Equals(m, module, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Whether this user may open a subfeature. Presentation only, exactly as
        /// <see cref="Can"/> is: the API re-derives the same answer for every request and
        /// answers 403 whatever the desktop drew.
        /// </summary>
        public bool CanUse(string submodule) =>
            Submodules.Any(s => string.Equals(s, submodule, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Mirrors the server's own seniority scale (0 Super Admin, 1 Admin, 2 Manager,
        /// 3 Staff - lower is more senior) so the desktop can offer the same restrictions the
        /// API enforces, rather than offering a control the server is only going to refuse.
        /// This is a courtesy: the API re-checks every one of these regardless.
        /// </summary>
        public bool IsAdminOrAbove => RoleLevel <= 1;

        public bool IsManagerOrAbove => RoleLevel <= 2;
    }

    public class ChangePasswordRequestDto
    {
        public string CurrentPassword { get; set; } = "";
        public string NewPassword { get; set; } = "";
    }

    // ----------------------------------------------------------------------------------
    // User Access
    // ----------------------------------------------------------------------------------

    public class UserAccountDto
    {
        public int AppUserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string RoleKey { get; set; } = "";
        public string RoleDisplayName { get; set; } = "";
        public int RoleLevel { get; set; }
        public bool IsActive { get; set; }
        public int? EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = "";
        public List<string> Modules { get; set; } = new();
    }

    public class ModulePermissionDto
    {
        public string Module { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Group { get; set; } = "";
        public bool AvailableInTier { get; set; }
        public bool GrantedByRole { get; set; }
        public bool? UserOverride { get; set; }
        public bool Effective { get; set; }
    }

    /// <summary>
    /// One subfeature as the permission editor shows it. There is no override column: a
    /// subfeature is derived from its module, the plan and the role rather than granted, so
    /// what the editor offers is the explanation of why it is or is not available.
    /// </summary>
    public class SubmodulePermissionDto
    {
        public string Submodule { get; set; } = "";
        public string Module { get; set; } = "";
        public string ModuleDisplayName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string MinimumTierName { get; set; } = "";
        public bool AvailableInTier { get; set; }
        public bool AllowedForRole { get; set; }
        public bool HoldsParentModule { get; set; }
        public bool Effective { get; set; }
    }

    public class UserPermissionEditorDto
    {
        public int AppUserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string RoleKey { get; set; } = "";
        public string RoleDisplayName { get; set; } = "";
        public bool IsActive { get; set; }
        public string EnterpriseTierName { get; set; } = "";
        public int RoleLevel { get; set; }
        public List<ModulePermissionDto> Modules { get; set; } = new();
        public List<SubmodulePermissionDto> Submodules { get; set; } = new();
    }

    public class RoleDto
    {
        public int RoleId { get; set; }
        public string RoleKey { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int HierarchyLevel { get; set; }
        public List<string> DefaultModules { get; set; } = new();
    }

    public class CreateUserRequestDto
    {
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string RoleKey { get; set; } = "";
        public int? EmployeeId { get; set; }
    }

    public class UpdateUserRequestDto
    {
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public int? EmployeeId { get; set; }
    }

    public class UpdateModuleAccessRequestDto
    {
        public List<string> Modules { get; set; } = new();
    }

    public class UpdateUserRoleRequestDto
    {
        public string RoleKey { get; set; } = "";
    }

    public class UpdateUserStatusRequestDto
    {
        public bool IsActive { get; set; }
    }

    public class ResetPasswordRequestDto
    {
        public string NewPassword { get; set; } = "";
    }

    /// <summary>
    /// The nine FitCore business modules, mirrored from <c>ERP_domain.entities.ErpModules</c>
    /// so the sidebar and the pages agree with the API about what a permission is called.
    ///
    /// Duplicated rather than shared for the same reason every other contract here is: the
    /// desktop client keeps zero project references and cannot reach EF Core. The server is
    /// authoritative and answers 403 regardless of what this client believes.
    /// </summary>
    public static class Modules
    {
        public const string Membership = "membership";
        public const string Payments   = "payments";
        public const string Sales      = "sales";
        public const string Inventory  = "inventory";
        public const string Employees  = "employees";
        public const string Payroll    = "payroll";
        public const string Finance    = "finance";
        public const string SystemAdmin = "systemadmin";
        public const string BusinessIntelligence = "businessintelligence";
    }

    /// <summary>
    /// The subfeature keys, mirrored from <c>ErpModules.Sub</c>.
    ///
    /// Every screen in the desktop names the subfeature it belongs to, and the shell shows a
    /// tab only when the server said the signed-in user may open it. Nothing is decided here -
    /// the list of permitted keys arrives on <see cref="CurrentUserDto.Submodules"/>.
    /// </summary>
    public static class Submodules
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
        public const string Branches           = "systemadmin.branches";
        public const string Settings           = "systemadmin.settings";
        public const string AuditLogs          = "systemadmin.audit";
        public const string Security           = "systemadmin.security";
        public const string DataIntegrity      = "systemadmin.integrity";

        public const string PlatformTenants       = "systemadmin.platform.tenants";
        public const string PlatformSubscriptions = "systemadmin.platform.subscriptions";
        public const string PlatformPlans         = "systemadmin.platform.plans";
        public const string PlatformUsers         = "systemadmin.platform.users";
        public const string PlatformSettings      = "systemadmin.platform.settings";
        public const string PlatformAudit         = "systemadmin.platform.audit";
        public const string PlatformMonitoring    = "systemadmin.platform.monitoring";

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
        public const string BranchPerformance   = "businessintelligence.branches";

        public const string PlatformDashboard  = "businessintelligence.platform.dashboard";
        public const string PlatformAnalytics  = "businessintelligence.platform.analytics";
    }

    /// <summary>
    /// Mirrors <c>ERP_domain.entities.EmployeePositions</c>. Duplicated rather than shared for
    /// the same reason every other contract here is: the desktop client keeps zero project
    /// references. The server is authoritative and re-validates this regardless of what the
    /// dialog offers. Deliberately just the two operational posts - System Admin is an
    /// application access-control role granted through User Access, not an employee position.
    /// </summary>
    public static class EmployeePositions
    {
        public const string Staff = "Staff";
        public const string Manager = "Manager";

        public static readonly string[] All = { Staff, Manager };
    }
}
