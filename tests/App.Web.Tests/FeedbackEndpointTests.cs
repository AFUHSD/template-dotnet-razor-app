using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using App.Web.Services.Events;
using App.Web.Services.Feedback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Web.Tests;

/// <summary>
/// The embedded feedback control through the whole pipeline, with the logging sink: the page carries the
/// control, the endpoint checks anti-forgery, size, shape and rate, and what reaches the event log is
/// redacted. The test client keeps cookies, so the anti-forgery pair (cookie + header token) round-trips
/// the way a browser's would.
/// </summary>
public sealed partial class FeedbackEndpointTests
{
    private const string PathBase = "/my-app";
    private const string Endpoint = PathBase + FeedbackEndpoints.Route;

    private static readonly string TinyPng = "data:image/png;base64," + Convert.ToBase64String(PngBytes(64));

    private static (AppFactory Factory, RecordingEventLog Events) Hosted()
    {
        var events = new RecordingEventLog();
        var factory = new AppFactory()
            .With("PathBase", "my-app")
            .WithServices(services => services.AddSingleton<IEventLog>(events));
        return (factory, events);
    }

    [Fact]
    public async Task The_control_is_on_the_page_when_enabled_and_absent_when_disabled()
    {
        var (enabled, _) = Hosted();
        using var enabledFactory = enabled;
        using var enabledClient = enabledFactory.CreateClient();
        var on = await enabledClient.GetStringAsync(new Uri(PathBase + "/", UriKind.Relative));

        Assert.Contains("id=\"feedback-open\"", on, StringComparison.Ordinal);
        Assert.Contains("<dialog id=\"feedback-dialog\"", on, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"feedback-title\"", on, StringComparison.Ordinal);
        Assert.Contains("<label for=\"feedback-message\">", on, StringComparison.Ordinal);
        Assert.Contains("<label for=\"feedback-screenshot\">", on, StringComparison.Ordinal);
        Assert.Contains("student data", on, StringComparison.Ordinal); // the screenshot warning
        Assert.Matches("<input type=\"checkbox\" id=\"feedback-screenshot\"[^>]*>", on);
        Assert.DoesNotMatch("<input type=\"checkbox\" id=\"feedback-screenshot\"[^>]*checked", on); // off by default
        Assert.Contains($"action=\"{Endpoint}\"", on, StringComparison.Ordinal); // stays inside the sub-path

        using var disabledFactory = new AppFactory().With("PathBase", "my-app").With("Feedback:Enabled", "false");
        using var disabledClient = disabledFactory.CreateClient();
        var off = await disabledClient.GetStringAsync(new Uri(PathBase + "/", UriKind.Relative));

        Assert.DoesNotContain("feedback-open", off, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outside_Development_the_control_is_off_unless_the_flag_says_otherwise()
    {
        // Production needs the platform cookie, which the test cannot mint, so this checks the flag's
        // resolution directly rather than rendering a page.
        var options = new Options.FeedbackOptions();
        Assert.True(options.IsEnabled(new FakeEnvironment("Development")));
        Assert.False(options.IsEnabled(new FakeEnvironment("Production")));
        Assert.True(new Options.FeedbackOptions { Enabled = true }.IsEnabled(new FakeEnvironment("Production")));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        var (factory, events) = Hosted();
        using var f = factory;
        using var client = f.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri(Endpoint, UriKind.Relative), new { message = "hello" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(events.Written);
    }

    [Fact]
    public async Task A_valid_post_writes_one_audit_event_with_the_route_redacted_and_no_message_text()
    {
        var (factory, events) = Hosted();
        using var f = factory;
        using var client = f.CreateClient();
        var token = await AntiForgeryTokenAsync(client);

        using var request = Post(token, new { message = "The Save button does nothing. Student 12345 is on screen." });
        request.Headers.Referrer = new Uri("http://localhost/my-app/students/12345/edit?access_token=abc123&tab=2");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LoggingFeedbackSink.Name, body.GetProperty("sink").GetString());
        Assert.Equal("logged", body.GetProperty("reference").GetString());

        var audit = Assert.Single(events.Written);
        Assert.Equal("feedback", audit.Action);
        Assert.Equal("Sent", audit.Status);
        Assert.Equal("developer@example.org", audit.Identity);
        var detail = JsonSerializer.Serialize(audit.Detail);
        Assert.Contains("/my-app/students/[number]/edit?", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("12345", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", detail, StringComparison.Ordinal);
        Assert.Contains("tab=2", detail, StringComparison.Ordinal);
        Assert.Contains("\"app\":\"My App\"", detail, StringComparison.Ordinal);
        Assert.Contains("\"sink\":\"log\"", detail, StringComparison.Ordinal);
        Assert.Contains("\"messageLength\":", detail, StringComparison.Ordinal);
        // The audit trail is contractually free of student data and the redactor cannot see a name, so the
        // message never goes there (Codex review on #5). It goes to the diagnostic log with the fallback sink.
        Assert.DoesNotContain("Save button", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("on screen", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fallback_sink_writes_the_whole_ticket_to_the_diagnostic_log()
    {
        var lines = new List<string>();
        using var loggers = LoggerFactory.Create(b => b.AddProvider(new CapturingLoggerProvider(lines)));
        var sink = new LoggingFeedbackSink(loggers.CreateLogger<LoggingFeedbackSink>());
        var ticket = new FeedbackTicket("My App", "1.0.0+abc", "/my-app/page?tab=2", "person@example.org", "TestBrowser/1.0",
            "The Save button does nothing.", new byte[64], DateTimeOffset.UnixEpoch);

        var result = await sink.SendAsync(ticket);

        Assert.True(result.Accepted);
        Assert.Equal("logged", result.Reference);
        var line = Assert.Single(lines);
        Assert.Contains("My App 1.0.0+abc", line, StringComparison.Ordinal);
        Assert.Contains("person@example.org", line, StringComparison.Ordinal);
        Assert.Contains("/my-app/page?tab=2", line, StringComparison.Ordinal);
        Assert.Contains("screenshot 64 bytes", line, StringComparison.Ordinal);
        Assert.Contains("The Save button does nothing.", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_zero_rate_limit_window_is_refused_at_startup_rather_than_silently_disabling_the_limit()
    {
        using var factory = new AppFactory().With("Feedback:WindowMinutes", "0");

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Feedback__WindowMinutes", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_valid_screenshot_is_accepted_and_anything_else_is_not()
    {
        var (factory, events) = Hosted();
        using var f = factory;
        using var client = f.CreateClient();
        var token = await AntiForgeryTokenAsync(client);

        using var ok = Post(token, new { message = "see picture", screenshotDataUrl = TinyPng });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ok)).StatusCode);
        Assert.Contains("\"screenshot\":true", JsonSerializer.Serialize(events.Written.Single(e => e.Action == "feedback").Detail), StringComparison.Ordinal);

        var jpeg = "data:image/jpeg;base64," + Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6 });
        using var wrongType = Post(token, new { message = "see picture", screenshotDataUrl = jpeg });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(wrongType)).StatusCode);

        var notPng = "data:image/png;base64," + Convert.ToBase64String(Encoding.ASCII.GetBytes("<script>alert(1)</script>"));
        using var fakePng = Post(token, new { message = "see picture", screenshotDataUrl = notPng });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(fakePng)).StatusCode);

        // One byte over the screenshot cap but under the body cap: refused as a bad screenshot, with the reason.
        var oversize = "data:image/png;base64," + Convert.ToBase64String(PngBytes(FeedbackLimits.MaxScreenshotBytes + 1));
        using var tooBig = Post(token, new { message = "see picture", screenshotDataUrl = oversize });
        var tooBigResponse = await client.SendAsync(tooBig);
        Assert.Equal(HttpStatusCode.BadRequest, tooBigResponse.StatusCode);
        Assert.Contains("1 MB", await tooBigResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(events.Written, e => e.Action == "feedback"); // only the first one was filed
    }

    [Fact]
    public async Task Screenshots_can_be_switched_off_by_configuration()
    {
        var events = new RecordingEventLog();
        using var factory = new AppFactory().With("PathBase", "my-app").With("Feedback:AllowScreenshots", "false")
            .WithServices(s => s.AddSingleton<IEventLog>(events));
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync(new Uri(PathBase + "/", UriKind.Relative));
        Assert.DoesNotContain("feedback-screenshot", html, StringComparison.Ordinal);

        var token = await AntiForgeryTokenAsync(client);
        using var request = Post(token, new { message = "see picture", screenshotDataUrl = TinyPng });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task The_message_and_the_body_are_capped()
    {
        var (factory, _) = Hosted();
        using var f = factory;
        using var client = f.CreateClient();
        var token = await AntiForgeryTokenAsync(client);

        using var empty = Post(token, new { message = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(empty)).StatusCode);

        using var long2001 = Post(token, new { message = new string('x', FeedbackLimits.MaxMessageChars + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(long2001)).StatusCode);

        using var exactly2000 = Post(token, new { message = new string('x', FeedbackLimits.MaxMessageChars) });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(exactly2000)).StatusCode);

        var huge = "{\"message\":\"" + new string('x', (int)FeedbackLimits.MaxBodyBytes) + "\"}";
        using var tooBig = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, UriKind.Relative))
        {
            Content = new StringContent(huge, Encoding.UTF8, "application/json"),
        };
        tooBig.Headers.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(tooBig)).StatusCode);

        using var garbage = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, UriKind.Relative))
        {
            Content = new StringContent("not json", Encoding.UTF8, "application/json"),
        };
        garbage.Headers.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(garbage)).StatusCode);
    }

    [Fact]
    public async Task The_sixth_submission_in_the_window_is_rate_limited()
    {
        var (factory, events) = Hosted();
        using var f = factory;
        using var client = f.CreateClient();
        var token = await AntiForgeryTokenAsync(client);

        for (var i = 0; i < 5; i++)
        {
            using var ok = Post(token, new { message = $"message {i}" });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ok)).StatusCode);
        }

        using var sixth = Post(token, new { message = "one more" });
        var response = await client.SendAsync(sixth);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(5, events.Written.Count(e => e.Action == "feedback" && e.Status == "Sent")); // the sixth never reached the sink
    }

    [Fact]
    public async Task When_disabled_the_endpoint_does_not_exist()
    {
        using var factory = new AppFactory().With("PathBase", "my-app").With("Feedback:Enabled", "false");
        using var client = factory.CreateClient();

        // No token to fetch: a disabled control renders nothing. The flag is checked before anti-forgery, so
        // the answer is 404 either way.
        var response = await client.PostAsJsonAsync(new Uri(Endpoint, UriKind.Relative), new { message = "hello" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_platform_cookie_the_endpoint_sends_people_to_sign_in()
    {
        using var factory = new AppFactory().With("PathBase", "my-app").With("Auth:UsePlatformLogin", "true").With("Feedback:Enabled", "true");
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.PostAsJsonAsync(new Uri(Endpoint, UriKind.Relative), new { message = "hello" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login?returnUrl=", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failing_sink_is_reported_and_audited_without_losing_the_person()
    {
        var events = new RecordingEventLog();
        using var factory = new AppFactory().With("PathBase", "my-app")
            .WithServices(s =>
            {
                s.AddSingleton<IEventLog>(events);
                s.AddSingleton<IFeedbackSink>(new FailingSink());
            });
        using var client = factory.CreateClient();
        var token = await AntiForgeryTokenAsync(client);

        using var request = Post(token, new { message = "hello" });
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("down for maintenance", body.GetProperty("error").GetString());
        var audit = Assert.Single(events.Written, e => e.Action == "feedback");
        Assert.Equal("Failed", audit.Status);
    }

    [Fact]
    public void The_route_comes_from_the_referer_and_never_carries_the_origin()
    {
        Assert.Equal("/my-app/page?x=1", FeedbackEndpoints.RouteOf("https://apps.example.org/my-app/page?x=1", "/my-app"));
        Assert.Equal("/my-app", FeedbackEndpoints.RouteOf(null, "/my-app"));
        Assert.Equal("/", FeedbackEndpoints.RouteOf("not a url", default));
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    private static HttpRequestMessage Post(string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("RequestVerificationToken", token);
        return request;
    }

    /// <summary>GETs the home page (which sets the anti-forgery cookie) and reads the matching form token.</summary>
    private static async Task<string> AntiForgeryTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync(new Uri(PathBase + "/", UriKind.Relative));
        var match = TokenInput().Match(html);
        Assert.True(match.Success, "The page did not render an anti-forgery token. Is the feedback partial on it?");
        return match.Groups["token"].Value;
    }

    /// <summary>A byte array that starts with the PNG signature and is exactly <paramref name="length"/> long. Not a decodable image; the endpoint checks the signature and the size, nothing more.</summary>
    private static byte[] PngBytes(int length)
    {
        var bytes = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        return bytes;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex TokenInput();

    private sealed class RecordingEventLog : IEventLog
    {
        private readonly List<AppEvent> _written = [];
        public IReadOnlyList<AppEvent> Written => _written;

        public Task WriteAsync(AppEvent appEvent, CancellationToken ct = default)
        {
            lock (_written) _written.Add(appEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>Captures formatted log lines so the fallback sink's output can be asserted.</summary>
    private sealed class CapturingLoggerProvider(List<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(lines);
        public void Dispose() { }

        private sealed class CapturingLogger(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (lines) lines.Add(formatter(state, exception));
            }
        }
    }

    private sealed class FailingSink : IFeedbackSink
    {
        public Task<FeedbackSinkResult> SendAsync(FeedbackTicket ticket, CancellationToken ct = default) =>
            Task.FromResult(FeedbackSinkResult.Failed("test", "down for maintenance"));
    }

    private sealed class FakeEnvironment(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "App.Web";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
