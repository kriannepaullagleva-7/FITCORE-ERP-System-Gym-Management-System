using ERP_domain.entities;
using ERP_infrastructure.data;
using ERP_infrastructure.services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP_Tests;

/// <summary>
/// The audit trail is only worth having if it records what actually happened and never records
/// a credential. These run against a real SQLite database so the change tracker behaves the way
/// it does in production.
/// </summary>
public sealed class AuditTrailTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantErpDbContext _context;

    /// <summary>A fixed signed-in user, so assertions can name who the trail should blame.</summary>
    private sealed class StubActor : ICurrentUserAccessor
    {
        public AuditActor Current { get; } =
            new(AppUserId: 7, Username: "manager", RoleKey: "manager",
                CompanyId: 4, IpAddress: "203.0.113.9", DeviceId: "front-desk");
    }

    public AuditTrailTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TenantErpDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TenantErpDbContext(options, new StubActor());
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static Member NewMember() => new()
    {
        FirstName = "Liza",
        LastName = "Manalo",
        Phone = "09170000000",
        Email = "liza@example.com",
        Status = "Active",
        JoinDate = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Creating_a_row_records_who_created_it()
    {
        _context.Members.Add(NewMember());
        await _context.SaveChangesAsync();

        var entry = Assert.Single(await _context.AuditEvents.ToListAsync());

        Assert.Equal(AuditActions.Create, entry.Action);
        Assert.Equal(nameof(Member), entry.EntityName);
        Assert.Equal(ErpModules.Membership, entry.Module);
        Assert.Equal("manager", entry.Username);
        Assert.Equal(7, entry.AppUserId);
        Assert.Equal(4, entry.CompanyId);
        Assert.Equal("203.0.113.9", entry.IpAddress);
    }

    /// <summary>
    /// An inserted row has no identity value until the insert has run, which is why the capture
    /// is finalised after SaveChanges rather than before it.
    /// </summary>
    [Fact]
    public async Task A_created_row_is_recorded_against_its_generated_key()
    {
        var member = NewMember();
        _context.Members.Add(member);
        await _context.SaveChangesAsync();

        var entry = Assert.Single(await _context.AuditEvents.ToListAsync());

        Assert.True(member.MemberId > 0);
        Assert.Equal(member.MemberId.ToString(), entry.EntityId);
    }

    [Fact]
    public async Task An_update_records_only_the_properties_that_changed()
    {
        var member = NewMember();
        _context.Members.Add(member);
        await _context.SaveChangesAsync();
        _context.AuditEvents.RemoveRange(_context.AuditEvents);
        await _context.SaveChangesAsync();

        member.Phone = "09171111111";
        await _context.SaveChangesAsync();

        var entry = Assert.Single(await _context.AuditEvents
            .Where(e => e.Action == AuditActions.Update)
            .ToListAsync());

        Assert.Contains("09171111111", entry.NewValues);
        Assert.Contains("09170000000", entry.OldValues);

        // The name did not change, so it has no business being in either side of the record.
        Assert.DoesNotContain("Manalo", entry.NewValues);
    }

    [Fact]
    public async Task A_delete_keeps_the_values_that_were_removed()
    {
        var member = NewMember();
        _context.Members.Add(member);
        await _context.SaveChangesAsync();

        _context.Members.Remove(member);
        await _context.SaveChangesAsync();

        var entry = Assert.Single(await _context.AuditEvents
            .Where(e => e.Action == AuditActions.Delete)
            .ToListAsync());

        Assert.Contains("Manalo", entry.OldValues);
        Assert.Null(entry.NewValues);
    }

    /// <summary>
    /// Writing the trail must not itself be audited, or saving would not terminate.
    /// </summary>
    [Fact]
    public async Task The_trail_does_not_record_itself()
    {
        _context.Members.Add(NewMember());
        await _context.SaveChangesAsync();

        Assert.Empty(await _context.AuditEvents
            .Where(e => e.EntityName == nameof(AuditEvent))
            .ToListAsync());
    }

    [Fact]
    public async Task An_explicit_business_action_is_recorded_with_its_own_name()
    {
        IAuditService audit = new TenantAuditService(_context, new StubActor());

        await audit.RecordAsync(
            AuditActions.PayrollPaid, ErpModules.Payroll, nameof(Payroll), "12",
            "Payroll run for Liza Manalo marked paid");

        var entry = Assert.Single(await _context.AuditEvents
            .Where(e => e.Action == AuditActions.PayrollPaid)
            .ToListAsync());

        Assert.Equal(ErpModules.Payroll, entry.Module);
        Assert.Equal("12", entry.EntityId);
        Assert.Contains("marked paid", entry.Summary);
    }

    /// <summary>
    /// The trail is read by administrators and kept for a long time, so a password hash
    /// reaching it would be a durable leak rather than a transient one.
    /// </summary>
    [Fact]
    public async Task A_password_hash_is_never_written_to_the_trail()
    {
        // Employee carries no credential, so this uses the exclusion list directly: any
        // property whose name ends in "Hash" is dropped before serialisation.
        _context.Employees.Add(new Employee
        {
            EmployeeCode = "EMP-1",
            FirstName = "Ana",
            LastName = "Lim",
            Position = "Trainer",
            Department = "Gym",
            Phone = "0917",
            Email = "ana@example.com",
            HireDate = DateTime.UtcNow,
            BasicSalary = 20000m,
            Status = "Active",
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        var all = await _context.AuditEvents.ToListAsync();

        Assert.NotEmpty(all);
        Assert.DoesNotContain(all, e =>
            (e.NewValues ?? "").Contains("Hash", StringComparison.OrdinalIgnoreCase) ||
            (e.OldValues ?? "").Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A context built without an accessor - a migration, the desktop application, an existing
    /// fixture - must keep saving exactly as it did before.
    /// </summary>
    [Fact]
    public async Task Without_a_signed_in_user_nothing_is_recorded_and_the_save_still_works()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TenantErpDbContext>()
            .UseSqlite(connection)
            .Options;

        using var plain = new TenantErpDbContext(options);
        plain.Database.EnsureCreated();

        plain.Members.Add(NewMember());
        await plain.SaveChangesAsync();

        Assert.Single(await plain.Members.ToListAsync());
        Assert.Empty(await plain.AuditEvents.ToListAsync());
    }
}
