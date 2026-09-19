using System.Reflection;
using ERP_api.Infrastructure;
using Xunit;
using ValidationException = ERP_infrastructure.services.ValidationException;
using ERP_infrastructure.tenant;

namespace ERP_Tests;

/// <summary>
/// Exercises the private translation and scrubbing logic of the exception handler. These are
/// the rules that decide what a client is told when something goes wrong, so they are worth
/// pinning down: a wrong answer here either loses a useful message or leaks a credential.
/// </summary>
public class ApiExceptionHandlerTests
{
    private static (int Status, string Title, string? Detail) Translate(Exception exception)
    {
        var method = typeof(ApiExceptionHandler)
            .GetMethod("Translate", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = method.Invoke(null, new object[] { exception })!;
        var type = result.GetType();

        return ((int)type.GetField("Item1")!.GetValue(result)!,
                (string)type.GetField("Item2")!.GetValue(result)!,
                (string?)type.GetField("Item3")!.GetValue(result));
    }

    [Fact]
    public void Validation_errors_become_400_and_keep_their_message()
    {
        var (status, _, detail) = Translate(new ValidationException("A customer code is required."));

        Assert.Equal(400, status);
        Assert.Equal("A customer code is required.", detail);
    }

    [Fact]
    public void Business_rule_errors_become_400_and_keep_their_message()
    {
        var (status, _, detail) = Translate(
            new InvalidOperationException("Product code 'TWL-001' is already in use."));

        Assert.Equal(400, status);
        Assert.Contains("already in use", detail);
    }

    [Fact]
    public void Tenant_resolution_failures_become_503_without_any_detail_from_the_exception()
    {
        var (status, _, detail) = Translate(
            new TenantResolutionException(
                "No TenantCredentials entry named 'FitcoreCredential' was found for company 1."));

        Assert.Equal(503, status);
        Assert.NotNull(detail);
        Assert.DoesNotContain("FitcoreCredential", detail);
        Assert.DoesNotContain("company 1", detail);
    }

    [Fact]
    public void Unexpected_errors_become_500_with_a_generic_message()
    {
        var (status, _, detail) = Translate(new Exception("object reference not set"));

        Assert.Equal(500, status);
        Assert.DoesNotContain("object reference", detail);
    }

    [Theory]
    [InlineData("Login failed for Server=db1.example.net;Database=d1;User Id=u1;Password=hunter2;")]
    [InlineData("Cannot open database \"d1\" requested by the login. Password=hunter2")]
    [InlineData("pwd=hunter2 uid=u1")]
    public void Connection_detail_is_scrubbed_out_of_messages_returned_to_a_client(string message)
    {
        var (_, _, detail) = Translate(new InvalidOperationException(message));

        Assert.NotNull(detail);
        Assert.DoesNotContain("hunter2", detail);
        Assert.DoesNotContain("db1.example.net", detail);
    }

    [Fact]
    public void An_ordinary_business_message_survives_scrubbing_unchanged()
    {
        var (_, _, detail) = Translate(
            new InvalidOperationException("Cannot remove 999 units - only 1 in stock."));

        Assert.Equal("Cannot remove 999 units - only 1 in stock.", detail);
    }
}
