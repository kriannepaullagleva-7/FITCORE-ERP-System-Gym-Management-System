using ERP_infrastructure.tenant;
using Xunit;

namespace ERP_Tests;

public class TenantContextTests
{
    [Fact]
    public void Unresolved_context_reports_not_resolved()
    {
        var context = new TenantContext();

        Assert.False(context.IsResolved);
        Assert.Equal(TenantSource.None, context.Source);
    }

    [Fact]
    public void Reading_the_connection_string_before_resolution_throws()
    {
        var context = new TenantContext();

        // This is the safety property that matters: code must never be able to open a
        // database when no tenant was established, because it would read the wrong one.
        Assert.Throws<TenantResolutionException>(() => _ = context.ConnectionString);
    }

    [Fact]
    public void Set_records_the_company_source_and_fallback_flag()
    {
        var context = new TenantContext();

        context.Set(42, "Server=x;Database=y;", TenantSource.Claim, usedFallback: true);

        Assert.True(context.IsResolved);
        Assert.Equal(42, context.CompanyId);
        Assert.Equal(TenantSource.Claim, context.Source);
        Assert.True(context.UsedFallback);
        Assert.Equal("Server=x;Database=y;", context.ConnectionString);
    }

    [Fact]
    public void Set_rejects_an_empty_connection_string()
    {
        var context = new TenantContext();

        Assert.Throws<ArgumentException>(
            () => context.Set(1, "  ", TenantSource.Default, usedFallback: false));
    }
}
