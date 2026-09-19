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
        public static IServiceCollection AddErpApplicationServices(this IServiceCollection services)
        {
            services.AddScoped<IMemberRepository, MemberRepository>();
            services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
            services.AddScoped<ISaleRepository, SaleRepository>();
            services.AddScoped<IProductRepository, ProductRepository>();
            services.AddScoped<IInventoryRepository, InventoryRepository>();
            services.AddScoped<IPaymentRepository, PaymentRepository>();
            services.AddScoped<IEmployeeRepository, EmployeeRepository>();
            services.AddScoped<IPayrollRepository, PayrollRepository>();
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
            services.AddScoped<ICustomerService, CustomerService>();
            services.AddScoped<ISupplierService, SupplierService>();
            services.AddScoped<IEmployeeService, EmployeeService>();
            services.AddScoped<IPayrollService, PayrollService>();
            services.AddScoped<IExpenseService, ExpenseService>();

            // Audit. The accessor is a TryAdd so a host that knows about HTTP - the API - can
            // register its own first and have that one win, while the WinForms application and
            // the tests fall back to attributing work to the system.
            services.TryAddScoped<ICurrentUserAccessor, NullCurrentUserAccessor>();
            services.AddScoped<IAuditService, TenantAuditService>();

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

            // Sign-in events are recorded against the master database, because a failed attempt
            // has no company and therefore no tenant database to write to.
            services.TryAddScoped<ICurrentUserAccessor, NullCurrentUserAccessor>();
            services.AddScoped<IAuthAuditService, AuthAuditService>();

            return services;
        }
    }
}
