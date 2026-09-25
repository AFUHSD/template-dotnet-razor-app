using System.Net;
using System.Text.RegularExpressions;

namespace App.Web.Tests;

/// <summary>
/// The example page, requested through the whole pipeline under a sub-path, as it is hosted.
/// Replace these with tests of the app's first real page. Never delete a test without its replacement.
/// </summary>
public sealed partial class IndexPageTests
{
    private const string PathBase = "/my-app";

    private static AppFactory Hosted() => new AppFactory().With("PathBase", "my-app");

    [Fact]
    public async Task Home_page_renders_for_a_permitted_user()
    {
        using var factory = Hosted();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(PathBase + "/", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Welcome, <strong>developer@example.org</strong>", html, StringComparison.Ordinal); // OnGet ran.
    }

    [Fact]
    public async Task Home_page_meets_the_accessibility_baseline()
    {
        using var factory = Hosted();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri(PathBase + "/", UriKind.Relative));

        Assert.Contains("<html lang=\"en\">", html, StringComparison.Ordinal);
        Assert.Contains("<a class=\"skip-link\" href=\"#main\">", html, StringComparison.Ordinal);
        Assert.Contains("<main id=\"main\"", html, StringComparison.Ordinal);
        Assert.Contains("<header", html, StringComparison.Ordinal);
        Assert.Contains("<nav aria-label=\"Main\">", html, StringComparison.Ordinal);
        Assert.Contains("<footer", html, StringComparison.Ordinal);
        Assert.Single(H1().Matches(html));
    }

    [Fact]
    public async Task Every_link_and_asset_stays_inside_the_sub_path()
    {
        using var factory = Hosted();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri(PathBase + "/", UriKind.Relative));
        var urls = RootRelativeUrl().Matches(html).Select(m => m.Groups["url"].Value).ToList();
        var escaping = urls
            .Where(url => !url.StartsWith(PathBase + "/", StringComparison.Ordinal) && url != PathBase)
            .ToList();

        Assert.NotEmpty(urls);
        Assert.True(escaping.Count == 0, "Links that escape to the parent site: " + string.Join(", ", escaping));
    }

    [Fact]
    public async Task A_person_the_access_rule_rejects_is_sent_to_the_denied_page()
    {
        using var factory = Hosted().With("Auth:DevelopmentEmail", "someone@elsewhere.test");
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(new Uri(PathBase + "/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(PathBase + "/denied", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Without_a_platform_cookie_the_page_redirects_to_the_platform_sign_in()
    {
        using var factory = Hosted().With("Auth:UsePlatformLogin", "true");
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(new Uri(PathBase + "/staff/help", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?returnUrl=%2Fmy-app%2Fstaff%2Fhelp", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public void The_development_identity_is_refused_outside_Development()
    {
        using var factory = new AppFactory().InEnvironment("Production").With("Auth:UsePlatformLogin", "false");

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("UsePlatformLogin", ex.ToString(), StringComparison.Ordinal);
    }

    [GeneratedRegex("<h1[ >]")]
    private static partial Regex H1();

    [GeneratedRegex("(?:href|src|action)=\"(?<url>/[^\"]*)\"")]
    private static partial Regex RootRelativeUrl();
}
