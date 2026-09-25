using App.Web.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace App.Web.Services.Health;

/// <summary>
/// Fails readiness when a setting the app cannot work without is missing, and names the KEY (never the
/// value). Without it, an unset setting is silently absent and a deploy gate goes green on an install that
/// cannot sign anyone in or reach its database.
/// </summary>
public sealed class ConfigurationHealthCheck(
    IConfiguration configuration,
    IOptions<AuthOptions> auth,
    IOptions<AccessOptions> access) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Default")))
            missing.Add("ConnectionStrings__Default");

        if (auth.Value.UsePlatformLogin && string.IsNullOrWhiteSpace(auth.Value.KeyRingPath))
            missing.Add("Auth__KeyRingPath");

        // TODO: when you replace the example access rule, check its settings here instead.
        if (string.IsNullOrWhiteSpace(access.Value.AllowedEmailDomain))
            missing.Add("Access__AllowedEmailDomain");

        if (missing.Count > 0)
            return Task.FromResult(HealthCheckResult.Unhealthy("Missing configuration: " + string.Join(", ", missing)));

        return Task.FromResult(auth.Value.UsePlatformLogin
            ? HealthCheckResult.Healthy("Required configuration is present.")
            : HealthCheckResult.Degraded("Auth__UsePlatformLogin is false: a fixed development identity is in use."));
    }
}
