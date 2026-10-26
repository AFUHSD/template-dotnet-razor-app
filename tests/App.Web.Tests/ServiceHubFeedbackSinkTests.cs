using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Web.Options;
using App.Web.Services.Feedback;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace App.Web.Tests;

/// <summary>
/// The wire contract with the help desk's intake, pinned from this side: method, bearer key, field names,
/// and how each answer maps to what the person is told. A fake handler stands in for the help desk.
/// </summary>
public sealed class ServiceHubFeedbackSinkTests
{
    private static readonly Uri Intake = new("https://helpdesk.example.org/servicehub/api/intake/feedback");

    private static readonly FeedbackTicket Ticket = new(
        "My App", "1.2.3+abc", "/my-app/page?tab=2", "person@example.org", "TestBrowser/1.0",
        "The Save button does nothing.", new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 }, new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));

    private static ServiceHubFeedbackSink Sink(FakeHandler handler, string? key = "k-test-key") =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new FeedbackOptions { ServiceHubIntakeUrl = Intake, ServiceHubIntakeKey = key }),
            NullLogger<ServiceHubFeedbackSink>.Instance);

    [Fact]
    public async Task Posts_the_documented_shape_with_the_bearer_key_and_returns_the_ticket_number()
    {
        using var handler = new FakeHandler(HttpStatusCode.Created, """{"ticketId":4321,"url":"https://helpdesk.example.org/servicehub/portal/tickets/4321"}""");
        var result = await Sink(handler).SendAsync(Ticket);

        Assert.True(result.Accepted);
        Assert.Equal("4321", result.Reference);
        Assert.Equal(ServiceHubFeedbackSink.Name, result.Sink);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(Intake, handler.Request.RequestUri);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization?.Scheme);
        Assert.Equal("k-test-key", handler.Request.Headers.Authorization?.Parameter);

        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("My App", body.GetProperty("app").GetString());
        Assert.Equal("1.2.3+abc", body.GetProperty("appVersion").GetString());
        Assert.Equal("/my-app/page?tab=2", body.GetProperty("route").GetString());
        Assert.Equal("person@example.org", body.GetProperty("userEmail").GetString());
        Assert.Equal("TestBrowser/1.0", body.GetProperty("userAgent").GetString());
        Assert.Equal("The Save button does nothing.", body.GetProperty("message").GetString());
        Assert.StartsWith("data:image/png;base64,", body.GetProperty("screenshotDataUrl").GetString(), StringComparison.Ordinal);
        Assert.Equal("2026-01-01T08:00:00+00:00", body.GetProperty("submittedUtc").GetString());
    }

    [Fact]
    public async Task A_successful_status_without_the_documented_shape_is_not_reported_as_sent()
    {
        // Codex review on #5: a proxy's 200 page or a changed upstream must not become "Thank you, sent".
        using var okEmpty = new FakeHandler(HttpStatusCode.OK, "{}");
        var fromEmpty = await Sink(okEmpty).SendAsync(Ticket);
        Assert.False(fromEmpty.Accepted);
        Assert.Contains("without a ticket number", fromEmpty.Error, StringComparison.Ordinal);

        using var createdNoId = new FakeHandler(HttpStatusCode.Created, """{"url":"https://helpdesk.example.org/x"}""");
        Assert.False((await Sink(createdNoId).SendAsync(Ticket)).Accepted);

        using var okWithId = new FakeHandler(HttpStatusCode.OK, """{"ticketId":7}""");
        Assert.False((await Sink(okWithId).SendAsync(Ticket)).Accepted); // 201 is the contract, not any 2xx
    }

    [Fact]
    public async Task A_ticket_without_a_screenshot_omits_the_field()
    {
        using var handler = new FakeHandler(HttpStatusCode.Created, """{"ticketId":1}""");
        await Sink(handler).SendAsync(Ticket with { ScreenshotPng = null });

        Assert.DoesNotContain("screenshotDataUrl", handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_requester_facing_rejection_is_passed_on_and_other_statuses_are_not_echoed()
    {
        using var unprocessable = new FakeHandler(HttpStatusCode.UnprocessableEntity, """{"error":"You do not have a help desk account yet."}""");
        var rejected = await Sink(unprocessable).SendAsync(Ticket);
        Assert.False(rejected.Accepted);
        Assert.Equal("You do not have a help desk account yet.", rejected.Error);

        using var serverError = new FakeHandler(HttpStatusCode.InternalServerError, """{"error":"NullReferenceException at Intake.cs:42 on HOST01"}""");
        var failed = await Sink(serverError).SendAsync(Ticket);
        Assert.False(failed.Accepted);
        Assert.DoesNotContain("HOST01", failed.Error, StringComparison.Ordinal);
        Assert.Contains("500", failed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_help_desk_is_a_failed_result_not_an_exception()
    {
        using var handler = new FakeHandler(new HttpRequestException("connection refused"));
        var result = await Sink(handler).SendAsync(Ticket);

        Assert.False(result.Accepted);
        Assert.DoesNotContain("connection refused", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_key_fails_before_anything_is_sent()
    {
        using var handler = new FakeHandler(HttpStatusCode.Created, "{}");
        var result = await Sink(handler, key: null).SendAsync(Ticket);

        Assert.False(result.Accepted);
        Assert.Contains("Feedback__ServiceHubIntakeKey", result.Error, StringComparison.Ordinal);
        Assert.Null(handler.Request);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _response;
        private readonly Exception? _throw;

        public FakeHandler(HttpStatusCode status, string response)
        {
            _status = status;
            _response = response;
        }

        public FakeHandler(Exception toThrow)
        {
            _throw = toThrow;
            _response = "";
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (_throw is not null)
                throw _throw;
            return new HttpResponseMessage(_status) { Content = JsonContent.Create(JsonDocument.Parse(_response).RootElement) };
        }
    }
}
