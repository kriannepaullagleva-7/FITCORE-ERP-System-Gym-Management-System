using ERP_domain.entities;
using ERP_infrastructure.data;
using ERP_infrastructure.repositories;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ERP_infrastructure.tenant
{
    /// <summary>
    /// Wires the infrastructure layer into a dependency injection container.
    /// </summary>
    public static class InfrastructureServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the tenant resolution chain and a request-scoped
        /// <see cref="TenantErpDbContext"/> that is bound to whichever tenant the server
        /// resolved for the current request.
        ///
        /// This is the hinge of the multi-tenant design. Every repository and service already
        /// takes a <see cref="TenantErpDbContext"/> through its constructor, so pointing that
        /// one registration at the tenant factory makes the whole stack tenant-aware without
        /// changing a single repository or service.
        /// </summary>
        public static IServiceCollection AddErpTenancy(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<TenantOptions>(configuration.GetSection(TenantOptions.SectionName));

            // Option graphs are expensive to rebuild, and there are only as many as there are
            // tenant databases, so they are cached for the lifetime of the process.
            services.AddSingleton<ITenantDbContextOptionsFactory, TenantDbContextOptionsFactory>();

            // One TenantContext per scope, surfaced through a read interface for consumers and
            // a write interface for the resolution middleware. Both must be the same instance.
            services.AddScoped<TenantContext>();
            services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
            services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());

            // The branch scope for this request, set by the branch resolution middleware after
            // the tenant is known. Same shape as TenantContext: one instance per scope, a read
            // interface for consumers and a write interface for the middleware.
            services.AddScoped<BranchContext>();
            services.AddScoped<IBranchContext>(sp => sp.GetRequiredService<BranchContext>());
            services.AddScoped<IBranchContextSetter>(sp => sp.GetRequiredService<BranchContext>());

            services.AddScoped<ITenantDatabaseResolver, TenantDatabaseResolver>();
            services.AddScoped<ITenantConnectionStringProvider, TenantConnectionStringProvider>();
            services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
            services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();

            // Resolved lazily: endpoints that never touch tenant data (health, OpenAPI, SaaS
            // administration) never construct this, so they never need a tenant. The container
            // owns the instance and disposes it when the request scope ends.
            services.AddScoped<TenantErpDbContext>(sp =>
                sp.GetRequiredService<ITenantDbContextFactory>().CreateForCurrentTenant());

            return services;
        }

        /// <summary>
        /// Registers the repositories and application services. These are unchanged from the
        /// existing wiring; they are collected here so the API and any other host register the
        /// same set instead of keeping two lists in step by hand.
        /// </summary>
        public static IServiceCollection AddErpApplicationServices(
            this IServiceCollection services, IConfiguration? configuration = null)
        {
            services.AddScoped<IMemberRepository, MemberRepository>();
            services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
            services.AddScoped<ISaleRepository, SaleRepository>();
            services.AddScoped<IProductRepository, ProductRepository>();
            services.AddScoped<IInventoryRepository, InventoryRepository>();
            services.AddScoped<IPaymentRepository, PaymentRepository>();
            services.AddScoped<IEmployeeRepository, EmployeeRepository>();
            services.AddScoped<IPayrollRepository, PayrollRepository>();
            services.AddScoped<IAttendanceRepository, AttendanceRepository>();
            services.AddScoped<IExpenseRepository, ExpenseRepository>();
            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

            services.AddScoped<IMemberService, MemberService>();
            services.AddScoped<IMembershipPlanService, MembershipPlanService>();
            services.AddScoped<ISubscriptionService, SubscriptionService>();
            services.AddScoped<IPaymentService, PaymentService>();
            services.AddScoped<ISaleService, SaleService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddScoped<IDashboardService, DashboardService>();
            services.AddScoped<IReportService, ReportService>();
            services.AddScoped<ISupplierService, SupplierService>();
            services.AddScoped<IEmployeeService, EmployeeService>();
            services.AddScoped<IPayrollService, PayrollService>();
            services.AddScoped<IAttendanceService, AttendanceService>();
            services.AddScoped<ILeaveService, LeaveService>();
            services.AddScoped<IMemberNoteService, MemberNoteService>();

            // Purchasing and returns: the two transactions that make Inventory and Sales
            // financially complete rather than merely quantitative.
            services.AddScoped<IPurchaseService, PurchaseService>();
            services.AddScoped<ISaleReturnService, SaleReturnService>();

            // Finance Management. FinancePostingService is what every operational service calls
            // after it commits, so it is registered alongside them rather than behind a flag -
            // a tenant switches automatic posting off in Settings, not by unregistering a
            // service, and the catch-up sweep still needs to exist either way.
            services.AddScoped<IAccountService, AccountService>();
            services.AddScoped<IJournalService, JournalService>();
            services.AddScoped<IFinancePostingService, FinancePostingService>();
            services.AddScoped<IFinanceReportService, FinanceReportService>();
            services.AddScoped<IFinancialPeriodService, FinancialPeriodService>();
            services.AddScoped<IBudgetService, BudgetService>();
            services.AddScoped<IBankingService, BankingService>();
            services.AddScoped<IExpenseService, ExpenseService>();

            // System Administration and Business Intelligence, tenant side.
            services.AddScoped<ITenantSettingsService, TenantSettingsService>();
            services.AddScoped<IBusinessIntelligenceService, BusinessIntelligenceService>();

            // Payroll's statutory deduction reference. Bound from the "Payroll:Deductions"
            // configuration section when present; the options class supplies FitCore's default
            // table otherwise, so a fresh install works without any extra configuration.
            var deductionsSection = configuration?.GetSection(PayrollDeductionOptions.SectionName);
            if (deductionsSection is not null && deductionsSection.Exists())
            {
                services.Configure<PayrollDeductionOptions>(deductionsSection);
            }
            else
            {
                services.AddOptions<PayrollDeductionOptions>();
            }
            services.AddSingleton<IPayrollDeductionCalculator, PayrollDeductionCalculator>();

            // Audit. The accessor is a TryAdd so a host that knows about HTTP - the API - can
            // register its own first and have that one win, while the WinForms application and
            // the tests fall back to attributing work to the system.
            services.TryAddScoped<ICurrentUserAccessor, NullCurrentUserAccessor>();
            services.AddScoped<IAuditService, TenantAuditService>();
            services.AddScoped<IAuditQueryService, TenantAuditQueryService>();

            // Branching. The context is a TryAdd for the same reason the actor is: the API
            // registers a request-scoped one in AddErpTenancy and that wins, while a host with
            // no request - the tests, a design-time tool - falls back to seeing the whole
            // company, which is the correct scope for something that has not been narrowed.
            services.TryAddScoped<IBranchContext, NullBranchContext>();
            services.AddScoped<IBranchService, BranchService>();

            // Read-only consistency sweep over the tenant database. Never repairs anything.
            services.AddScoped<IDataIntegrityService, DataIntegrityService>();

            return services;
        }

        /// <summary>
        /// Registers sign-in, the User Access screen and the start-up bootstrapper.
        ///
        /// These all work against the master database rather than a tenant one, because a user
        /// is what selects a tenant: the company on the account is what the server turns into a
        /// connection string. Keeping them on this side of the line is what makes it impossible
        /// for a request to pick its own tenant.
        /// </summary>
        public static IServiceCollection AddErpAccessControl(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<BootstrapOptions>(
                configuration.GetSection(BootstrapOptions.SectionName));

            // ASP.NET Core's PBKDF2 hasher: 100,000 iterations of HMAC-SHA512 with a random
            // per-password salt, and a constant-time comparison. No plaintext is ever stored.
            services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

            services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
            services.AddScoped<IUserAccountService, UserAccountService>();
            services.AddScoped<IMasterBootstrapper, MasterBootstrapper>();

            // Branch seeding. Runs after the master bootstrap because it needs the companies and
            // roles that one creates, and it is the only bootstrap step that opens a tenant
            // database - the Branch and Employee rows it writes are tenant data and stay there.
            services.AddScoped<ITenantBranchBootstrapper, TenantBranchBootstrapper>();

            // Sign-in events are recorded against the master database, because a failed attempt
            // has no company and therefore no tenant database to write to.
            services.TryAddScoped<ICurrentUserAccessor, NullCurrentUserAccessor>();
            services.AddScoped<IAuthAuditService, AuthAuditService>();

            // Platform administration: the Super Admin's own workspace, entirely master-side.
            // Registered here rather than with the application services because none of it
            // touches a tenant database, and a host that only serves tenants does not need it.
            services.AddScoped<IPlatformAdminService, PlatformAdminService>();
            services.AddScoped<IPlatformAnalyticsService, PlatformAnalyticsService>();

            return services;
        }
    }
}
