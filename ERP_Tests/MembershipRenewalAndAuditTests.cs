using ERP_domain.entities;
using ERP_infrastructure.repositories;
using ERP_infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace ERP_Tests;

/// <summary>
/// Membership renewal: how the new period is calculated, and that renewing (and cancelling)
/// leaves a readable trace behind, since renewal extends the existing subscription row in
/// place rather than writing a new one.
/// </summary>
public class MembershipRenewalTests
{
    private sealed class Harness : IDisposable
    {
        public TenantDbFixture Db { get; }
        public IMemberService Members { get; }
        public ISubscriptionService Subscriptions { get; }
        public ICurrentUserAccessor Actor { get; }

        public Harness(ICurrentUserAccessor? actor = null, bool bindActorToContext = false)
        {
            Actor = actor ?? new FakeCurrentUserAccessor(roleKey: ErpRoles.Manager);
            Db = new TenantDbFixture(bindActorToContext ? Actor : null);

            var memberRepo = new MemberRepository(Db.Context);
            var paymentRepo = new PaymentRepository(Db.Context);
            var planRepo = new GenericRepository<MembershipPlan>(Db.Context);
            var subscriptionRepo = new SubscriptionRepository(Db.Context);

            Members = new MemberService(memberRepo, paymentRepo, subscriptionRepo);
            Subscriptions = new SubscriptionService(
                subscriptionRepo, planRepo, memberRepo, new TenantAuditService(Db.Context, Actor), Db.Finance);
        }

        public void Dispose() => Db.Dispose();
    }

    private static async Task<(int MemberId, MembershipPlan Plan)> SeedAsync(Harness h)
    {
        var member = await h.Members.CreateMemberAsync("Ada", "Lovelace", "555", "ada@example.com");

        var plan = new MembershipPlan
        {
            PlanName = "Monthly", DurationMonths = 1, Price = 1000m, IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        h.Db.Context.MembershipPlans.Add(plan);
        await h.Db.Context.SaveChangesAsync();

        return (member.MemberId, plan);
    }

    [Fact]
    public async Task Renewing_an_active_subscription_extends_from_its_existing_end_date()
    {
        using var h = new Harness();
        var (memberId, plan) = await SeedAsync(h);

        var subscription = await h.Subscriptions.CreateSubscriptionAsync(memberId, plan.PlanId, DateTime.UtcNow);
        var originalEndDate = subscription.EndDate;

        var renewed = await h.Subscriptions.RenewSubscriptionAsync(subscription.SubscriptionId);

        Assert.NotNull(renewed);
        // Extended from the paid-for end date, not from today - no paid-for days are lost.
        Assert.Equal(originalEndDate.AddMonths(1), renewed!.EndDate, TimeSpan.FromSeconds(2));
        Assert.Equal("Active", renewed.Status);
    }

    [Fact]
    public async Task Renewing_an_expired_subscription_starts_from_now()
    {
        using var h = new Harness();
        var (memberId, plan) = await SeedAsync(h);

        // Sold three months ago on a one-month plan, so it lapsed two months ago.
        var subscription = await h.Subscriptions.CreateSubscriptionAsync(
            memberId, plan.PlanId, DateTime.UtcNow.AddMonths(-3));
        Assert.True(subscription.EndDate < DateTime.UtcNow);

        var renewed = await h.Subscriptions.RenewSubscriptionAsync(subscription.SubscriptionId);

        Assert.NotNull(renewed);
        Assert.Equal(DateTime.UtcNow, renewed!.StartDate, TimeSpan.FromSeconds(5));
        Assert.Equal(DateTime.UtcNow.AddMonths(1), renewed.EndDate, TimeSpan.FromSeconds(5));
        Assert.Equal("Active", renewed.Status);
    }

    [Fact]
    public async Task Renewing_records_a_named_audit_event_naming_the_old_and_new_end_dates()
    {
        var actor = new FakeCurrentUserAccessor(roleKey: ErpRoles.Manager);
        using var h = new Harness(actor);
        var (memberId, plan) = await SeedAsync(h);

        var subscription = await h.Subscriptions.CreateSubscriptionAsync(memberId, plan.PlanId, DateTime.UtcNow);
        var originalEndDate = subscription.EndDate;

        var renewed = await h.Subscriptions.RenewSubscriptionAsync(subscription.SubscriptionId);

        var events = await h.Db.Context.AuditEvents
            .Where(e => e.Action == AuditActions.SubscriptionRenewed &&
                        e.EntityId == subscription.SubscriptionId.ToString())
            .ToListAsync();

        var recorded = Assert.Single(events);
        Assert.Equal(ErpModules.Membership, recorded.Module);
        Assert.Contains(originalEndDate.ToString("d MMM yyyy"), recorded.Summary);
        Assert.Contains(renewed!.EndDate.ToString("d MMM yyyy"), recorded.Summary);

        // Attributed to the real, server-side actor - never anything a caller could supply.
        Assert.Equal("test.user", recorded.Username);
    }

    [Fact]
    public async Task Cancelling_records_a_named_audit_event()
    {
        using var h = new Harness();
        var (memberId, plan) = await SeedAsync(h);

        var subscription = await h.Subscriptions.CreateSubscriptionAsync(memberId, plan.PlanId, DateTime.UtcNow);
        await h.Subscriptions.CancelSubscriptionAsync(subscription.SubscriptionId);

        var recorded = await h.Db.Context.AuditEvents.SingleOrDefaultAsync(e =>
            e.Action == AuditActions.SubscriptionCancelled &&
            e.EntityId == subscription.SubscriptionId.ToString());

        Assert.NotNull(recorded);
        Assert.Equal(ErpModules.Membership, recorded!.Module);
    }

    /// <summary>
    /// Renewal mutates the existing subscription row rather than writing a new one, so the
    /// audit trail's automatic before/after diff is the membership's only renewal history -
    /// this pins down that the generic sweep really does capture it, alongside the named event.
    /// </summary>
    [Fact]
    public async Task The_change_tracker_sweep_captures_the_old_and_new_dates_independently_of_the_named_event()
    {
        var actor = new FakeCurrentUserAccessor(roleKey: ErpRoles.Manager);
        using var h = new Harness(actor, bindActorToContext: true);
        var (memberId, plan) = await SeedAsync(h);

        var subscription = await h.Subscriptions.CreateSubscriptionAsync(memberId, plan.PlanId, DateTime.UtcNow);
        await h.Subscriptions.RenewSubscriptionAsync(subscription.SubscriptionId);

        var genericUpdate = await h.Db.Context.AuditEvents.SingleOrDefaultAsync(e =>
            e.Action == AuditActions.Update &&
            e.EntityName == nameof(Subscription) &&
            e.EntityId == subscription.SubscriptionId.ToString());

        Assert.NotNull(genericUpdate);
        Assert.Contains("EndDate", genericUpdate!.OldValues);
        Assert.Contains("EndDate", genericUpdate.NewValues);
    }
}

/// <summary>
/// Reading the audit trail back: administrator-only, and attributed to the real actor that
/// wrote each row - never anything a caller could influence after the fact.
/// </summary>
public class AuditQueryServiceTests
{
    private static TenantAuditQueryService BuildQueryService(TenantDbFixture db, ICurrentUserAccessor actor) =>
        new(db.Context, actor);

    [Fact]
    public async Task Only_an_administrator_can_search_the_audit_trail()
    {
        using var db = new TenantDbFixture();

        var staffQuery = BuildQueryService(db, new FakeCurrentUserAccessor(roleKey: ErpRoles.Staff));
        var managerQuery = BuildQueryService(db, new FakeCurrentUserAccessor(roleKey: ErpRoles.Manager));
        var adminQuery = BuildQueryService(db, new FakeCurrentUserAccessor(roleKey: ErpRoles.Admin));

        await Assert.ThrowsAsync<ForbiddenOperationException>(
            () => staffQuery.SearchAsync(new AuditEventQuery()));

        await Assert.ThrowsAsync<ForbiddenOperationException>(
            () => managerQuery.SearchAsync(new AuditEventQuery()));

        // Does not throw.
        await adminQuery.SearchAsync(new AuditEventQuery());
    }

    [Fact]
    public async Task Events_are_attributed_to_the_actor_who_recorded_them_not_a_spoofed_identity()
    {
        using var db = new TenantDbFixture();

        var recordingActor = new FakeCurrentUserAccessor(
            appUserId: 42, username: "real.manager", roleKey: ErpRoles.Manager, fullName: "Real Manager");
        var audit = new TenantAuditService(db.Context, recordingActor);

        // Nothing here accepts a caller-supplied identity - RecordAsync only ever reads the
        // actor injected into the service, which is exactly the protection being tested.
        await audit.RecordAsync(AuditActions.StockAdjusted, ErpModules.Inventory, "Inventory", "1", "test event");

        var adminQuery = BuildQueryService(db, new FakeCurrentUserAccessor(roleKey: ErpRoles.Admin));
        var results = await adminQuery.SearchAsync(new AuditEventQuery());

        var recorded = Assert.Single(results);
        Assert.Equal("real.manager", recorded.Username);
        Assert.Equal(ErpRoles.Manager, recorded.RoleKey);
    }

    [Fact]
    public async Task Filters_narrow_by_module_action_and_entity()
    {
        using var db = new TenantDbFixture();
        var actor = new FakeCurrentUserAccessor(roleKey: ErpRoles.Admin);
        var audit = new TenantAuditService(db.Context, actor);

        await audit.RecordAsync(AuditActions.StockReceived, ErpModules.Inventory, "Inventory", "1");
        await audit.RecordAsync(AuditActions.SubscriptionRenewed, ErpModules.Membership, "Subscription", "5");

        var query = BuildQueryService(db, actor);

        var inventoryOnly = await query.SearchAsync(new AuditEventQuery(Module: ErpModules.Inventory));
        Assert.Single(inventoryOnly);
        Assert.Equal(ErpModules.Inventory, inventoryOnly[0].Module);

        var byEntity = await query.SearchAsync(new AuditEventQuery(EntityName: "Subscription", EntityId: "5"));
        Assert.Single(byEntity);
        Assert.Equal(AuditActions.SubscriptionRenewed, byEntity[0].Action);
    }

    /// <summary>
    /// Two tenants never share a database, so this is really asserting that the query service
    /// reads only the context it was given - each tenant's trail travels with its own data, as
    /// architecture.md requires.
    /// </summary>
    [Fact]
    public async Task Audit_events_do_not_cross_between_tenant_databases()
    {
        using var dbA = new TenantDbFixture();
        using var dbB = new TenantDbFixture();

        var actor = new FakeCurrentUserAccessor(roleKey: ErpRoles.Admin);

        await new TenantAuditService(dbA.Context, actor)
            .RecordAsync(AuditActions.SaleCompleted, ErpModules.Sales, "Sale", "100");
        await new TenantAuditService(dbB.Context, actor)
            .RecordAsync(AuditActions.SaleCompleted, ErpModules.Sales, "Sale", "200");

        var resultsA = await BuildQueryService(dbA, actor).SearchAsync(new AuditEventQuery());
        var resultsB = await BuildQueryService(dbB, actor).SearchAsync(new AuditEventQuery());

        Assert.Equal("100", Assert.Single(resultsA).EntityId);
        Assert.Equal("200", Assert.Single(resultsB).EntityId);
    }
}

/// <summary>
/// The payroll workflow's named audit events: generating a run and finalizing it are the two
/// steps that deserve a readable sentence rather than a raw field diff.
/// </summary>
public class PayrollWorkflowAuditTests
{
    private sealed class Harness : IDisposable
    {
        public TenantDbFixture Db { get; }
        public IEmployeeService Employees { get; }
        public IAttendanceService Attendance { get; }
        public IPayrollService Payroll { get; }
        public IPayrollService PayrollAsAdmin { get; }

        public Harness()
        {
            Db = new TenantDbFixture();
            var employeeRepo = new EmployeeRepository(Db.Context);
            var payrollRepo = new PayrollRepository(Db.Context);
            var attendanceRepo = new AttendanceRepository(Db.Context);
            var deductionCalculator = new PayrollDeductionCalculator(
                Options.Create(new PayrollDeductionOptions()));

            var managerActor = new FakeCurrentUserAccessor(roleKey: ErpRoles.Manager);
            var adminActor = new FakeCurrentUserAccessor(
                appUserId: 2, username: "admin.actor", roleKey: ErpRoles.Admin, fullName: "Admin Actor");

            Employees = new EmployeeService(
                employeeRepo, new NullCurrentUserAccessor(), new NoOpUserAccountService());
            Attendance = new AttendanceService(attendanceRepo, employeeRepo, managerActor);
            Payroll = new PayrollService(
                payrollRepo, employeeRepo, attendanceRepo, deductionCalculator, managerActor,
                new TenantAuditService(Db.Context, managerActor), Db.Finance);
            PayrollAsAdmin = new PayrollService(
                payrollRepo, employeeRepo, attendanceRepo, deductionCalculator, adminActor,
                new TenantAuditService(Db.Context, adminActor), Db.Finance);
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task Generating_and_finalizing_a_run_each_record_a_named_audit_event()
    {
        using var h = new Harness();

        var employee = await h.Employees.CreateEmployeeAsync(
            "EMP-9", "Liza", "Manalo", EmployeePositions.Staff, "Ops",
            "555", "liza@example.com", new DateTime(2026, 1, 1), 0m, hourlyRate: 70m);

        var periodStart = new DateTime(2026, 4, 1);
        await h.Attendance.CreateAsync(
            employee.EmployeeId, periodStart, periodStart.AddHours(8), periodStart.AddHours(16), "Present", "");

        var run = await h.Payroll.GenerateFromAttendanceAsync(
            employee.EmployeeId, periodStart, periodStart, 0m, 0m, "");

        var generated = await h.Db.Context.AuditEvents.SingleOrDefaultAsync(e =>
            e.Action == AuditActions.PayrollGenerated && e.EntityId == run.PayrollId.ToString());
        Assert.NotNull(generated);
        Assert.Equal(ErpModules.Payroll, generated!.Module);

        await h.PayrollAsAdmin.SetStatusAsync(run.PayrollId, "Paid");

        var paid = await h.Db.Context.AuditEvents.SingleOrDefaultAsync(e =>
            e.Action == AuditActions.PayrollPaid && e.EntityId == run.PayrollId.ToString());
        Assert.NotNull(paid);
        Assert.Equal("admin.actor", paid!.Username);
    }
}
