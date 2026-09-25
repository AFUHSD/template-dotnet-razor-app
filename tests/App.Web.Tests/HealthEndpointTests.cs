using System.Net;

namespace App.Web.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Liveness_is_healthy_without_sign_in_or_database()
    {
        using var factory = new AppFactory().With("Auth:UsePlatformLogin", "true");
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_fails_and_names_the_missing_key()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.StartsWith("Unhealthy", body, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__Default", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_does_not_leak_the_server_name_when_the_database_is_unreachable()
    {
        // Nothing listens on port 1. The server name must reach the log, never the anonymous endpoint.
        using var factory = new AppFactory().With("ConnectionStrings:Default",
            "Server=tcp:127.0.0.1,1;Database=unreachable;Integrated Security=True;Connect Timeout=2;Encrypt=False");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("database: Unhealthy", body, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
    }
}
