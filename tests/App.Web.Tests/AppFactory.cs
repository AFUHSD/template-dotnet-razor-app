using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace App.Web.Tests;

/// <summary>
/// Boots the real app in memory. Runs in the Development environment by default, so it signs in as the
/// development identity from appsettings.Development.json. <see cref="With"/> overrides any setting,
/// using the same keys as configuration ("Auth:UsePlatformLogin").
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action<IServiceCollection>> _services = [];
    private string _environment = "Development";

    public AppFactory With(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    /// <summary>Replaces services after Program.cs has registered its own (for example a recording IEventLog).</summary>
    public AppFactory WithServices(Action<IServiceCollection> configure)
    {
        _services.Add(configure);
        return this;
    }

    public AppFactory InEnvironment(string environment)
    {
        _environment = environment;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        // UseSetting, not ConfigureAppConfiguration: Program.cs reads Auth settings before Build().
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);
        foreach (var configure in _services)
            builder.ConfigureTestServices(configure);
    }
}
