using App.Web.Options;
using App.Web.Services.Access;
using App.Web.Services.Events;
using App.Web.Services.Health;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Every environment-specific value comes from configuration: appsettings.json holds safe defaults only,
// appsettings.Example.json documents every key, and the host sets real values as environment variables
// (Section__Key). Nothing about a particular server or organization belongs in source.
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<AccessOptions>(builder.Configuration.GetSection(AccessOptions.SectionName));
var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

// --- Authentication: identity only, from the platform cookie ------------------------------------------------
if (auth.UsePlatformLogin)
{
    var dataProtection = builder.Services.AddDataProtection().SetApplicationName(auth.ApplicationName);
    if (!string.IsNullOrWhiteSpace(auth.KeyRingPath))
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(auth.KeyRingPath));
    // A missing key ring is not a startup failure: /health/ready reports it by name, which is easier to
    // diagnose than a process that will not start.

    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.Cookie.Name = auth.CookieName;
            options.Cookie.Path = "/"; // Scoped to this app's sub-path it would be invisible to its siblings.
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(auth.SessionHours);

            // This app never signs anyone in; it sends them to the platform sign-in. LoginPath cannot express
            // that (it is PathBase-relative), so the redirect is built by hand with an origin-absolute path.
            options.Events.OnRedirectToLogin = ctx =>
            {
                var here = ctx.Request.PathBase + ctx.Request.Path + ctx.Request.QueryString;
                ctx.Response.Redirect($"{auth.PlatformLoginPath}?returnUrl={Uri.EscapeDataString(here)}");
                return Task.CompletedTask;
            };
            // Signed in but no access: this app's Denied page, not the sign-in page (that would loop).
            options.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.Redirect(ctx.Request.PathBase + "/denied");
                return Task.CompletedTask;
            };
        });
}
else
{
    // Fail closed: an app that is not reading the platform cookie must never serve outside Development.
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "Auth:UsePlatformLogin is false outside the Development environment. Set Auth__UsePlatformLogin=true.");

    builder.Services.AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
            DevelopmentAuthenticationHandler.SchemeName, null);
}

// --- Authorization: re-resolved on every request by IAccessResolver ----------------------------------------
builder.Services.AddScoped<IAccessResolver, DomainAccessResolver>(); // TODO: the app's own rule.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(AppRoles.User).Build());

builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToPage("/Denied");
    options.Conventions.AllowAnonymousToPage("/Error");
});

// --- Audit events: a central store behind an interface; the no-op is the default -------------------------
builder.Services.AddSingleton<IEventLog, NoOpEventLog>();

// --- Health ----------------------------------------------------------------------------------------------
builder.Services.AddHealthChecks()
    .AddCheck<ConfigurationHealthCheck>("configuration", tags: [HealthEndpoints.ReadyTag])
    .AddCheck<SqlReadinessHealthCheck>("database", tags: [HealthEndpoints.ReadyTag]);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// The app is hosted under a sub-path (/{app}). IIS sets PathBase for a sub-application; this lets Kestrel
// simulate it locally (--PathBase=my-app) so a link that escapes to the parent site is caught in development.
var pathBase = builder.Configuration["PathBase"];
if (!string.IsNullOrWhiteSpace(pathBase))
    app.UsePathBase("/" + pathBase.Trim().Trim('/'));

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<AccessResolutionMiddleware>();
app.UseAuthorization();

app.MapAppHealthChecks();
app.MapRazorPages();

app.Run();

/// <summary>Visible to the test project's WebApplicationFactory.</summary>
public partial class Program;
