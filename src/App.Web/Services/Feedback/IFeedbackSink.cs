using App.Web.Services.Events;

namespace App.Web.Services.Feedback;

/// <summary>
/// What the endpoint files. Every string field has already been through <see cref="FeedbackRedactor"/> except
/// <see cref="UserEmail"/> (the requester's identity, which the help desk needs) and <see cref="UserAgent"/>.
/// </summary>
/// <param name="App">Name the help desk files the ticket against.</param>
/// <param name="AppVersion">Informational version of the running build.</param>
/// <param name="Route">PathBase + path + query string, redacted.</param>
/// <param name="UserEmail">The signed-in person, as the platform cookie identifies them.</param>
/// <param name="UserAgent">Browser string, truncated.</param>
/// <param name="Message">What the person typed, redacted, at most <see cref="FeedbackLimits.MaxMessageChars"/> characters.</param>
/// <param name="ScreenshotPng">Optional PNG bytes, at most <see cref="FeedbackLimits.MaxScreenshotBytes"/>.</param>
/// <param name="SubmittedUtc">When the endpoint accepted it.</param>
public sealed record FeedbackTicket(
    string App,
    string AppVersion,
    string Route,
    string UserEmail,
    string UserAgent,
    string Message,
    ReadOnlyMemory<byte>? ScreenshotPng,
    DateTimeOffset SubmittedUtc);

/// <summary>Outcome of filing. <paramref name="Reference"/> is what the person is shown (a ticket number, or "logged").</summary>
public sealed record FeedbackSinkResult(bool Accepted, string Sink, string? Reference = null, string? Error = null)
{
    public static FeedbackSinkResult Ok(string sink, string? reference) => new(true, sink, reference);
    public static FeedbackSinkResult Failed(string sink, string error) => new(false, sink, Error: error);
}

/// <summary>Where feedback goes. Program.cs picks the implementation from configuration.</summary>
public interface IFeedbackSink
{
    /// <summary>Files the ticket. Returns a failed result rather than throwing for anything the caller can report.</summary>
    Task<FeedbackSinkResult> SendAsync(FeedbackTicket ticket, CancellationToken ct = default);
}

/// <summary>Caps the endpoint enforces. Constants, not configuration: they protect the server, not a preference.</summary>
public static class FeedbackLimits
{
    public const int MaxMessageChars = 2000;
    public const int MaxScreenshotBytes = 1024 * 1024;
    public const int MaxUserAgentChars = 512;
    public const int MaxRouteChars = 2048;

    /// <summary>Message + base64 screenshot (4/3 of 1 MB) + JSON overhead.</summary>
    public const long MaxBodyBytes = 1_600_000;
}

/// <summary>
/// Default sink when no intake URL is configured: the whole ticket (minus screenshot bytes) goes to the
/// audit-event store as one <c>feedback-message</c> event. Nothing is dropped; a person reading the event log
/// sees exactly what would have been filed.
/// </summary>
public sealed class LoggingFeedbackSink(IEventLog events) : IFeedbackSink
{
    public const string Name = "event-log";

    public async Task<FeedbackSinkResult> SendAsync(FeedbackTicket ticket, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        await events.WriteAsync(new AppEvent("feedback-message", "Logged", ticket.UserEmail, new
        {
            app = ticket.App,
            appVersion = ticket.AppVersion,
            route = ticket.Route,
            userAgent = ticket.UserAgent,
            message = ticket.Message,
            screenshotBytes = ticket.ScreenshotPng?.Length ?? 0,
            submittedUtc = ticket.SubmittedUtc,
        }), ct);
        return FeedbackSinkResult.Ok(Name, "logged");
    }
}
