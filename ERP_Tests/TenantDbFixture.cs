using ERP_infrastructure.tenant;
using ERP_infrastructure.data;
using ERP_infrastructure.services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ERP_Tests;

/// <summary>
/// A throwaway tenant database backed by SQLite in memory.
///
/// SQLite is used rather than the EF in-memory provider because these tests exercise real
/// relational behaviour: foreign keys, cascade rules and query translation. The connection is
/// held open for the lifetime of the fixture, since an in-memory SQLite database disappears
/// when its last connection closes.
/// </summary>
public sealed class TenantDbFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public TenantErpDbContext Context { get; }

    /// <summary>
    /// A second context over the same database, for tests about two callers touching one row.
    ///
    /// Concurrency cannot be exercised through a single context: it tracks entities, so the
    /// second read returns the first read's instance and the conflict never happens. Two
    /// contexts on one connection reproduce what two HTTP requests actually do - each loads
    /// its own copy, and whichever saves second is the one that has to be stopped.
    /// </summary>
    public TenantErpDbContext NewContext(ICurrentUserAccessor? actor = null) =>
        new(new DbContextOptionsBuilder<TenantErpDbContext>()
                .UseSqlite(_connection)
                .Options,
            actor);

    /// <summary>
    /// A context over the same database, narrowed to one branch - which is what the API hands
    /// every service once the branch middleware has run.
    ///
    /// Null is the whole company, and is what <see cref="Context"/> itself is, so a test that
    /// says nothing about branches sees exactly what it always did.
    /// </summary>
    public TenantErpDbContext ScopedTo(int? branchId, ICurrentUserAccessor? actor = null) =>
        new(new DbContextOptionsBuilder<TenantErpDbContext>()
                .UseSqlite(_connection)
                .Options,
            actor,
            new StubBranchContext(branchId));

    private sealed class StubBranchContext : IBranchContext
    {
        public StubBranchContext(int? branchId)
        {
            BranchId = branchId;
        }

        public int? BranchId { get; }

        public BranchSource Source =>
            BranchId.HasValue ? BranchSource.Selection : BranchSource.None;
    }

    /// <param name="actor">
    /// Left null by default, matching every existing test: without one, SaveChanges records
    /// nothing to AuditEvents, which is what a test not concerned with the trail wants. Pass
    /// one only to exercise the automatic change-tracker sweep itself.
    /// </param>
    public TenantDbFixture(ICurrentUserAccessor? actor = null)
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TenantErpDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new TenantErpDbContext(options, actor);
        Context.Database.EnsureCreated();

        // The real finance stack over the same database, not a stub.
        //
        // Every operational service now posts to the ledger after it commits, so a fake here
        // would mean the tests exercise a code path the application never runs. Wiring the
        // genuine services instead means a sale in a test writes the same journal entries a
        // sale in production does - and the ledger assertions can check that it did.
        var settings = new TenantSettingsService(Context, actor ?? new NullCurrentUserAccessor());

        Accounts = new AccountService(Context);
        Journal = new JournalService(Context, actor ?? new NullCurrentUserAccessor());

        Finance = new FinancePostingService(
            Context, Journal, Accounts, settings,
            NullLogger<FinancePostingService>.Instance);

        FinanceReports = new FinanceReportService(Context, Accounts);
    }

    /// <summary>The chart of accounts, over this fixture's database.</summary>
    public IAccountService Accounts { get; }

    /// <summary>The ledger, over this fixture's database.</summary>
    public IJournalService Journal { get; }

    /// <summary>
    /// The real posting service. Passed to every operational service a test constructs, so the
    /// tests run the same path the application does.
    /// </summary>
    public IFinancePostingService Finance { get; }

    /// <summary>The financial statements, over this fixture's database.</summary>
    public IFinanceReportService FinanceReports { get; }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}

/// <summary>
/// A real, non-system actor for tests that assert on who a transaction was attributed to.
/// <see cref="NullCurrentUserAccessor"/> always returns the system actor, which is exactly
/// what these tests need to NOT use.
/// </summary>
public sealed class FakeCurrentUserAccessor : ICurrentUserAccessor
{
    public FakeCurrentUserAccessor(
        int appUserId = 1, string username = "test.user", string roleKey = "manager",
        int companyId = 1, string fullName = "Test User", int? employeeId = null)
    {
        Current = new AuditActor(
            appUserId, username, roleKey, companyId, null, null, fullName, employeeId);
    }

    public AuditActor Current { get; }
}

/// <summary>
/// A stand-in for <see cref="IUserAccountService"/> in tests that construct
/// <see cref="EmployeeService"/> but never call <c>EnsureAccountAsync</c> - the payroll and
/// employee CRUD tests exercise the tenant database only, and account creation is a master-
/// database concern with its own coverage in <c>ApiAuthorizationTests</c>. Every member is a
/// deliberate failure rather than a silent no-op, so a test that starts relying on it fails
/// loudly instead of passing against fake data.
/// </summary>
public sealed class NoOpUserAccountService : IUserAccountService
{
    private static Exception NotUsed([System.Runtime.CompilerServices.CallerMemberName] string member = "") =>
        new NotSupportedException($"{member} is not exercised by this test and has no fake behaviour.");

    public Task<IEnumerable<UserAccountView>> GetUsersAsync(
        int companyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Enumerable.Empty<UserAccountView>());

    public Task<UserAccountView?> GetUserAsync(
        int companyId, int appUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult<UserAccountView?>(null);

    public Task<UserPermissionEditorView?> GetPermissionEditorAsync(
        int companyId, int appUserId, CancellationToken cancellationToken = default) =>
        throw NotUsed();

    public Task<UserPermissionEditorView?> SetModuleAccessAsync(
        int companyId, int appUserId, IEnumerable<string> grantedModules, int actingRoleLevel,
        string actingUsername, CancellationToken cancellationToken = default) =>
        throw NotUsed();

    public Task<UserAccountView?> SetActiveAsync(
        int companyId, int appUserId, bool isActive, int actingRoleLevel, int actingUserId,
        CancellationToken cancellationToken = default) =>
        throw NotUsed();

    public Task<UserAccountView?> SetRoleAsync(
        int companyId, int appUserId, string roleKey, int actingRoleLevel, int actingUserId,
        CancellationToken cancellationToken = default) =>
        throw NotUsed();

    public Task<UserAccountView> CreateUserAsync(
        int companyId, string username, string fullName, string email, string password,
        string roleKey, int? employeeId, int actingRoleLevel,
        CancellationToken cancellationToken = default) =>
        throw NotUsed();

    public Task<UserAccountView?> UpdateUserAsync(
        int companyId, int appUserId, string fullName, string email, int? employeeId,
        int actingRoleLevel, CancellationToken cancellationToken = default) =>
        throw NotUsed();

    public Task<bool> ResetPasswordAsync(
        int companyId, int appUserId, string newPassword, int actingRoleLevel,
        CancellationToken cancellationToken = default) =>
        throw NotUsed();

    public Task<IEnumerable<RoleView>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Enumerable.Empty<RoleView>());
}
