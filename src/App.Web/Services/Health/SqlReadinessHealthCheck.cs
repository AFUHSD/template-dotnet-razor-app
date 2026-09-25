using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace App.Web.Services.Health;

/// <summary>
/// Can the app reach its database: open a connection and run <c>SELECT 1</c>.
///
/// The failure detail goes to the diagnostic log only. A SQL exception message names the server, and the
/// readiness endpoint is reachable without signing in, so the public description stays generic.
/// </summary>
public sealed partial class SqlReadinessHealthCheck(IConfiguration configuration, ILogger<SqlReadinessHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            return HealthCheckResult.Unhealthy("ConnectionStrings__Default is not set.");

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 5;
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("Database reachable.");
        }
        // Every exception, deliberately: one that escaped would be reported by the health check service with its
        // message as the description, and SQL client messages name the server.
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogUnreachable(logger, ex);
            return HealthCheckResult.Unhealthy("Database unreachable; see the application log.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Readiness: database unreachable")]
    private static partial void LogUnreachable(ILogger logger, Exception ex);
}
