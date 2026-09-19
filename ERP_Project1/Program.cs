using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ERP_infrastructure.data;
using ERP_infrastructure.repositories;
using ERP_infrastructure.services;
using ERP_infrastructure.tenant;

namespace ERP_Project1
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                ApplicationConfiguration.Initialize();

                var services = new ServiceCollection();
                ConfigureServices(services);
                var serviceProvider = services.BuildServiceProvider();

                // Forms that are opened outside the navigation shell resolve their own
                // services through this provider.
                AppContext.ServiceProvider = serviceProvider;

                var shell = serviceProvider.GetRequiredService<MainForm>();
                Application.Run(shell);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fatal Error: {ex.Message}\n\n{ex.InnerException?.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ConfigureServices(ServiceCollection services)
        {
            // The app reads its settings from next to the executable, so it behaves the
            // same whether it is launched by dotnet run or from the publish folder.
            var configuration = new ConfigurationBuilder()
                .SetBasePath(System.AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            services.AddSingleton<IConfiguration>(configuration);

            services.AddDbContext<TenantErpDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("TenantErp")));

            // One shared registration for every repository and service, so the desktop app and
            // the Web API cannot drift apart. Previously this was a second hand-maintained
            // list that had already fallen behind the API's.
            services.AddErpApplicationServices();

            // The shell lives for the whole session; every module form is built fresh from
            // its own scope on navigation so each one queries the database through a new
            // DbContext instead of reusing stale tracked entities.
            services.AddSingleton<MainForm>();

            services.AddScoped<Form1>();
            services.AddScoped<DashboardForm>();
            services.AddScoped<MembershipForm>();
            services.AddScoped<MembershipPlanForm>();
            services.AddScoped<SubscriptionForm>();
            services.AddScoped<SalesForm>();
            services.AddScoped<PaymentForm>();
            services.AddScoped<InventoryForm>();
        }
    }
}
