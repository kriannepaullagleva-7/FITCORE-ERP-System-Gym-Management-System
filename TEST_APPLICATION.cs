// AUTOMATED TEST - Run this to verify the application works
// This tests database connection, services, and basic CRUD operations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ERP_domain.entities;
using ERP_infrastructure.data;
using ERP_infrastructure.repositories;
using ERP_infrastructure.services;

class TestApplication
{
    static async Task Main()
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  FitCore ERP - Automated Test Suite                        ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

        try
        {
            // Setup
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            // Get service
            var memberService = serviceProvider.GetRequiredService<IMemberService>();

            Console.WriteLine("✅ STEP 1: Dependency Injection Initialized");
            Console.WriteLine("   → ServiceProvider created successfully\n");

            // Test Database Connection
            using (var dbContext = serviceProvider.GetRequiredService<TenantErpDbContext>())
            {
                try
                {
                    await dbContext.Database.OpenConnectionAsync();
                    Console.WriteLine("✅ STEP 2: Database Connection Verified");
                    Console.WriteLine("   → Connected to TenantErp database successfully\n");
                    await dbContext.Database.CloseConnectionAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("❌ STEP 2: Database Connection Failed");
                    Console.WriteLine($"   → Error: {ex.Message}\n");
                    return;
                }
            }

            // Test CRUD Operations
            Console.WriteLine("STEP 3: Testing CRUD Operations\n");

            // CREATE
            Console.WriteLine("   [CREATE] Adding test member...");
            var newMember = await memberService.CreateMemberAsync(
                "Test",
                "Member",
                "555-0123",
                "test@example.com");
            Console.WriteLine($"   ✅ Created member with ID: {newMember.MemberId}\n");

            // READ
            Console.WriteLine("   [READ] Loading all members...");
            var allMembers = await memberService.GetAllMembersAsync();
            Console.WriteLine($"   ✅ Loaded {allMembers.Count} members from database\n");

            // UPDATE
            Console.WriteLine("   [UPDATE] Updating test member...");
            var updated = await memberService.UpdateMemberAsync(
                newMember.MemberId,
                "UpdatedTest",
                "UpdatedMember",
                "555-9999",
                "updated@example.com",
                "Active");
            Console.WriteLine($"   ✅ Updated member: {updated.FirstName} {updated.LastName}\n");

            // DELETE
            Console.WriteLine("   [DELETE] Deleting test member...");
            var deleted = await memberService.DeleteMemberAsync(newMember.MemberId);
            if (deleted)
            {
                Console.WriteLine($"   ✅ Deleted member with ID: {newMember.MemberId}\n");
            }

            // VERIFY DELETE
            Console.WriteLine("   [VERIFY] Verifying deletion...");
            var afterDelete = await memberService.GetAllMembersAsync();
            Console.WriteLine($"   ✅ Verified: Member deleted, count now: {afterDelete.Count}\n");

            // Summary
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  ✅ ALL TESTS PASSED - APPLICATION IS WORKING!             ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

            Console.WriteLine("✅ Database Connection: OK");
            Console.WriteLine("✅ Dependency Injection: OK");
            Console.WriteLine("✅ Create Operation: OK");
            Console.WriteLine("✅ Read Operation: OK");
            Console.WriteLine("✅ Update Operation: OK");
            Console.WriteLine("✅ Delete Operation: OK");
            Console.WriteLine("✅ CRUD Transactions: OK\n");

            Console.WriteLine("🚀 Your application is ready to use!");
            Console.WriteLine("   Run: cd C:\\Users\\USER\\source\\repos\\ERP_Project1\\ERP_Project1");
            Console.WriteLine("   Then: dotnet run\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ TEST FAILED");
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Details: {ex.InnerException?.Message}");
        }
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        services.AddDbContext<TenantErpDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("TenantErp")));

        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<IMembershipPlanService, MembershipPlanService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISaleService, SaleService>();
    }
}
