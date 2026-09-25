using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace App.Web.Services.Health;

public static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// <c>/health</c>: liveness. Runs no checks, so a database outage never makes the host recycle a healthy
    /// process. <c>/health/ready</c>: readiness. Configuration and database; what a deploy gate waits on.
    /// </summary>
    public static void MapAppHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WritePlainText,
        }).AllowAnonymous();
    }

    // The default writer prints one word ("Unhealthy") exactly when someone needs to know which check failed.
    // This prints each check and its description. Descriptions name configuration keys, never values.
    private static Task WritePlainText(HttpContext context, HealthReport report)
    {
        var text = new StringBuilder().Append(report.Status).Append('\n');
        foreach (var (name, entry) in report.Entries)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {name}: {entry.Status}\n");
            if (!string.IsNullOrWhiteSpace(entry.Description))
                text.Append(CultureInfo.InvariantCulture, $"    {entry.Description}\n");
        }

        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync(text.ToString());
    }
}
