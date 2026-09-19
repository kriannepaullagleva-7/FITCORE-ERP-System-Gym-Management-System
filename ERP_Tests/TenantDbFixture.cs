using ERP_infrastructure.data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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

    public TenantDbFixture()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TenantErpDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new TenantErpDbContext(options);
        Context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
