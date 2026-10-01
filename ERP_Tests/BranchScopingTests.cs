using ERP_domain.entities;
using ERP_infrastructure.services;
using Xunit;

namespace ERP_Tests;

/// <summary>
/// Branching, tested where it is enforced.
///
/// The claim these tests exist to defend is that branch isolation is a property of the data
/// access layer rather than of the desktop or of any one service. Every query in the
/// application goes through <c>TenantErpDbContext</c>, so a global query filter there is the
/// only kind of filtering that cannot be forgotten by the next endpoint somebody writes. These
/// tests read through ordinary DbSets and through a real service, and expect both to be
/// narrowed without either having asked to be.
/// </summary>
public class BranchScopingTests : IDisposable
{
    private readonly TenantDbFixture _fixture = new();

    private int _branchA;
    private int _branchB;

    public void Dispose() => _fixture.Dispose();

    private async Task<(int A, int B)> TwoBranchesAsync()
    {
        var a = new Branch { Code = "BRA", Name = "Branch A", IsPrimary = true };
        var b = new Branch { Code = "BRB", Name = "Branch B" };

        _fixture.Context.Branches.AddRange(a, b);
        await _fixture.Context.SaveChangesAsync();

        _branchA = a.BranchId;
        _branchB = b.BranchId;

        return (a.BranchId, b.BranchId);
    }

    private static Member MemberNamed(string first, int? branchId) => new()
    {
        FirstName = first,
        LastName = "Test",
        JoinDate = DateTime.UtcNow.Date,
        CreatedAt = DateTime.UtcNow,
        BranchId = branchId
    };

    // ------------------------------------------------------------------ reads

    /// <summary>
    /// The core promise. A caller scoped to a branch reads that branch and nothing else, on an
    /// ordinary DbSet query that says nothing whatsoever about branches.
    /// </summary>
    [Fact]
    public async Task A_scoped_caller_reads_only_their_own_branch()
    {
        var (a, b) = await TwoBranchesAsync();

        _fixture.Context.Members.AddRange(
            MemberNamed("Ana", a), MemberNamed("Ben", b), MemberNamed("Cara", b));
        await _fixture.Context.SaveChangesAsync();

        using var scopedToA = _fixture.ScopedTo(a);
        using var scopedToB = _fixture.ScopedTo(b);

        Assert.Equal(new[] { "Ana" }, scopedToA.Members.Select(m => m.FirstName).OrderBy(n => n));
        Assert.Equal(new[] { "Ben", "Cara" }, scopedToB.Members.Select(m => m.FirstName).OrderBy(n => n));
    }

    /// <summary>
    /// An unscoped caller - the Admin/Owner who has not picked a branch - sees the whole
    /// company, including any record that belongs to no branch at all.
    /// </summary>
    [Fact]
    public async Task An_unscoped_caller_sees_every_branch_and_the_unassigned()
    {
        var (a, b) = await TwoBranchesAsync();

        _fixture.Context.Members.AddRange(
            MemberNamed("Ana", a), MemberNamed("Ben", b), MemberNamed("Company", null));
        await _fixture.Context.SaveChangesAsync();

        using var unscoped = _fixture.ScopedTo(null);

        Assert.Equal(3, unscoped.Members.Count());
    }

    /// <summary>
    /// A record that belongs to no branch is *not* lent to whichever branch happens to ask.
    /// Showing it in every branch would double-count it in the branch comparison, which is the
    /// one screen the whole feature exists to make trustworthy.
    /// </summary>
    [Fact]
    public async Task An_unassigned_record_belongs_to_the_company_not_to_every_branch()
    {
        // Written before the company had any branches, which is the only way a row ends up
        // unassigned on a branched tenant: with branches in place the stamp would put it in
        // the primary one.
        _fixture.Context.Members.Add(MemberNamed("Company", null));
        await _fixture.Context.SaveChangesAsync();

        var (a, _) = await TwoBranchesAsync();

        Assert.Null(_fixture.Context.Members.Single().BranchId);

        using var scoped = _fixture.ScopedTo(a);

        Assert.Empty(scoped.Members);
    }

    /// <summary>
    /// The filter is not a property of one entity. Everything implementing
    /// <c>IBranchScoped</c> gets it, because the sweep in OnModelCreating is over the model
    /// rather than over a list somebody maintains by hand.
    /// </summary>
    [Fact]
    public async Task Every_branch_scoped_entity_is_filtered_not_just_members()
    {
        var (a, b) = await TwoBranchesAsync();

        _fixture.Context.Employees.AddRange(
            new Employee { EmployeeCode = "A1", FirstName = "Ana", BranchId = a },
            new Employee { EmployeeCode = "B1", FirstName = "Ben", BranchId = b });

        _fixture.Context.Customers.AddRange(
            new Customer { CustomerCode = "CA", CustomerName = "A Co", BranchId = a },
            new Customer { CustomerCode = "CB", CustomerName = "B Co", BranchId = b });

        await _fixture.Context.SaveChangesAsync();

        using var scoped = _fixture.ScopedTo(a);

        Assert.Single(scoped.Employees);
        Assert.Single(scoped.Customers);
        Assert.Equal("A1", scoped.Employees.Single().EmployeeCode);
        Assert.Equal("CA", scoped.Customers.Single().CustomerCode);
    }

    /// <summary>
    /// Products and stock are deliberately company-wide: a branch sells from one catalogue at
    /// one set of prices. Asserting it here means the decision is recorded rather than
    /// rediscovered by somebody wondering why the filter "missed" them.
    /// </summary>
    [Fact]
    public async Task The_product_catalogue_is_shared_across_branches()
    {
        var (a, _) = await TwoBranchesAsync();

        _fixture.Context.Products.Add(new Product
        {
            ProductCode = "P1", ProductName = "Protein", UnitPrice = 100m
        });
        await _fixture.Context.SaveChangesAsync();

        using var scoped = _fixture.ScopedTo(a);

        Assert.Single(scoped.Products);
    }

    // ------------------------------------------------------------------ writes

    /// <summary>
    /// A scoped caller writes to their own branch without saying so. This is the other half of
    /// the isolation: filtering reads alone would let a branch manager create a record that
    /// then disappeared from their own view.
    /// </summary>
    [Fact]
    public async Task A_write_by_a_scoped_caller_lands_in_their_branch()
    {
        var (_, b) = await TwoBranchesAsync();

        using (var scoped = _fixture.ScopedTo(b))
        {
            scoped.Members.Add(MemberNamed("Ben", branchId: null));
            await scoped.SaveChangesAsync();
        }

        var written = _fixture.Context.Members.Single();
        Assert.Equal(b, written.BranchId);
    }

    /// <summary>
    /// An unscoped caller on a branched company writes to the primary branch rather than to no
    /// branch. An owner looking at the whole company is not a reason to create a record that
    /// appears in nobody's books.
    /// </summary>
    [Fact]
    public async Task An_unscoped_write_falls_to_the_primary_branch()
    {
        var (a, _) = await TwoBranchesAsync();

        using (var unscoped = _fixture.ScopedTo(null))
        {
            unscoped.Members.Add(MemberNamed("Owner", branchId: null));
            await unscoped.SaveChangesAsync();
        }

        Assert.Equal(a, _fixture.Context.Members.Single().BranchId);
    }

    /// <summary>
    /// And on a company with no branches at all - every Micro and Small tenant - nothing is
    /// stamped and nothing is filtered. The single-site case is exactly what it was before
    /// branching existed.
    /// </summary>
    [Fact]
    public async Task A_tenant_with_no_branches_is_completely_unaffected()
    {
        _fixture.Context.Members.Add(MemberNamed("Solo", branchId: null));
        await _fixture.Context.SaveChangesAsync();

        var member = _fixture.Context.Members.Single();

        Assert.Null(member.BranchId);
        Assert.Single(_fixture.ScopedTo(null).Members);
    }

    /// <summary>
    /// A branch stated explicitly by the caller is not overwritten by the stamp. The stamp
    /// fills a gap; it does not override a decision.
    /// </summary>
    [Fact]
    public async Task An_explicit_branch_survives_the_stamp()
    {
        var (a, b) = await TwoBranchesAsync();

        using (var scoped = _fixture.ScopedTo(a))
        {
            scoped.Members.Add(MemberNamed("Stated", branchId: b));
            await scoped.SaveChangesAsync();
        }

        Assert.Equal(b, _fixture.Context.Members.Single().BranchId);
    }

    // ------------------------------------------------------------------ the service

    /// <summary>
    /// Branch administration reads across branches on purpose, so it must not be narrowed by
    /// the caller's own selection. If it were, an Admin who had picked Branch A would be told
    /// their company had one branch.
    /// </summary>
    [Fact]
    public async Task Branch_administration_sees_every_branch_whatever_is_selected()
    {
        var (a, _) = await TwoBranchesAsync();

        using var scoped = _fixture.ScopedTo(a);
        var service = new BranchService(scoped);

        var branches = await service.GetBranchesAsync();

        Assert.Equal(2, branches.Count);
    }

    /// <summary>
    /// A branch that has traded cannot be deleted, for the same reason a member who has paid
    /// cannot be: the branch is what explains which till took the money. Closing it is the
    /// supported alternative, and the message has to say so.
    /// </summary>
    [Fact]
    public async Task A_branch_with_history_cannot_be_deleted()
    {
        var (_, b) = await TwoBranchesAsync();

        _fixture.Context.Members.Add(MemberNamed("Ben", b));
        await _fixture.Context.SaveChangesAsync();

        var service = new BranchService(_fixture.Context);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.DeleteBranchAsync(b));

        Assert.Contains("cannot be deleted", error.Message);
        Assert.Contains("Close the branch instead", error.Message);

        // And the history it was protecting is still there.
        Assert.Single(_fixture.Context.Members);
        Assert.NotNull(await _fixture.Context.Branches.FindAsync(b));
    }

    [Fact]
    public async Task A_branch_that_has_never_traded_can_be_deleted()
    {
        var (_, b) = await TwoBranchesAsync();

        var service = new BranchService(_fixture.Context);

        Assert.True(await service.DeleteBranchAsync(b));
        Assert.Single(_fixture.Context.Branches);
    }

    /// <summary>The primary branch is where unassigned records land, so it cannot be removed.</summary>
    [Fact]
    public async Task The_primary_branch_cannot_be_deleted_or_closed()
    {
        var (a, _) = await TwoBranchesAsync();

        var service = new BranchService(_fixture.Context);

        await Assert.ThrowsAsync<ValidationException>(() => service.DeleteBranchAsync(a));

        await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateBranchAsync(a, "BRA", "Branch A", "", "", "", isActive: false));
    }

    /// <summary>
    /// Transferring moves the people and leaves the transactions where they happened. Moving a
    /// sale would change two branches' recorded revenue after the fact, and the ledger - which
    /// is company-wide - would then agree with neither.
    /// </summary>
    [Fact]
    public async Task A_transfer_moves_people_and_leaves_transactions_where_they_happened()
    {
        var (a, b) = await TwoBranchesAsync();

        var member = MemberNamed("Ana", a);
        _fixture.Context.Members.Add(member);
        _fixture.Context.Employees.Add(
            new Employee { EmployeeCode = "A1", FirstName = "Ana", BranchId = a });
        await _fixture.Context.SaveChangesAsync();

        _fixture.Context.Sales.Add(new Sale
        {
            MemberId = member.MemberId,
            SaleDate = DateTime.UtcNow,
            TotalAmount = 500m,
            BranchId = a
        });
        await _fixture.Context.SaveChangesAsync();

        var service = new BranchService(_fixture.Context);

        var result = await service.TransferAsync(a, b, BranchTransferScope.AllStandingRecords);

        Assert.Equal(1, result.MembersMoved);
        Assert.Equal(1, result.EmployeesMoved);

        Assert.Equal(b, _fixture.Context.Members.Single().BranchId);
        Assert.Equal(b, _fixture.Context.Employees.Single().BranchId);

        // The sale stayed at the branch that rang it.
        Assert.Equal(a, _fixture.Context.Sales.Single().BranchId);
    }

    [Fact]
    public async Task A_transfer_to_the_same_branch_is_refused()
    {
        var (a, _) = await TwoBranchesAsync();

        var service = new BranchService(_fixture.Context);

        await Assert.ThrowsAsync<ValidationException>(
            () => service.TransferAsync(a, a, BranchTransferScope.Members));
    }

    [Fact]
    public async Task A_duplicate_branch_code_is_refused_with_a_sentence()
    {
        await TwoBranchesAsync();

        var service = new BranchService(_fixture.Context);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateBranchAsync(new Branch { Code = "bra", Name = "Another" }));

        Assert.Contains("already in use", error.Message);
    }

    /// <summary>
    /// The comparison counts each branch separately and the company as the whole of them, so
    /// the parts add up to the total. A reader checking one against the other is the main use
    /// of the screen, and it only works if both are measured the same way.
    /// </summary>
    [Fact]
    public async Task The_comparison_measures_each_branch_and_the_company_consistently()
    {
        var (a, b) = await TwoBranchesAsync();

        var ana = MemberNamed("Ana", a);
        var ben = MemberNamed("Ben", b);
        _fixture.Context.Members.AddRange(ana, ben);
        await _fixture.Context.SaveChangesAsync();

        var today = DateTime.UtcNow.Date;

        _fixture.Context.Sales.AddRange(
            new Sale { MemberId = ana.MemberId, SaleDate = today, TotalAmount = 300m, BranchId = a },
            new Sale { MemberId = ben.MemberId, SaleDate = today, TotalAmount = 700m, BranchId = b });
        await _fixture.Context.SaveChangesAsync();

        var service = new BranchService(_fixture.Context);

        var comparison = await service.CompareAsync(today.AddDays(-1), today);

        Assert.Equal(1000m, comparison.Company.SalesRevenue);

        var rowA = comparison.Branches.Single(r => r.BranchId == a);
        var rowB = comparison.Branches.Single(r => r.BranchId == b);

        Assert.Equal(300m, rowA.SalesRevenue);
        Assert.Equal(700m, rowB.SalesRevenue);
        Assert.Equal(comparison.Company.SalesRevenue, rowA.SalesRevenue + rowB.SalesRevenue);

        Assert.Equal(30m, rowA.ShareOfCompanyRevenue);
        Assert.Equal(70m, rowB.ShareOfCompanyRevenue);

        Assert.Equal("Branch B", comparison.TopBranchByRevenue);
        Assert.Equal("Branch A", comparison.LowestBranchByRevenue);
    }

    /// <summary>
    /// And the comparison is not narrowed by whatever branch the Admin happened to have
    /// selected when they opened it. A comparison that showed one branch would not be one.
    /// </summary>
    [Fact]
    public async Task The_comparison_ignores_the_callers_own_branch_selection()
    {
        var (a, b) = await TwoBranchesAsync();

        var ana = MemberNamed("Ana", a);
        var ben = MemberNamed("Ben", b);
        _fixture.Context.Members.AddRange(ana, ben);
        await _fixture.Context.SaveChangesAsync();

        using var scoped = _fixture.ScopedTo(a);
        var service = new BranchService(scoped);

        var comparison = await service.CompareAsync(
            DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date);

        Assert.Equal(2, comparison.Branches.Count);
        Assert.Equal(2, comparison.Company.Members);
    }
}
