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
/// Default sink when no intake URL is configured: the ticket (minus screenshot bytes) goes to the diagnostic
/// log through <see cref="ILogger"/> as one structured Information entry, so nothing is dropped while the help
/// desk is not yet wired up.
///
/// Not the audit-event store on purpose. <c>AppEvent.Detail</c> is contractually free of student data, and the
/// redactor cannot recognise a student's name typed into the message despite the warning. The diagnostic log
/// is where free text already lives; the audit trail gets the <c>feedback</c> event the endpoint writes, which
/// carries the redacted route and never the message.
/// </summary>
public sealed partial class LoggingFeedbackSink(ILogger<LoggingFeedbackSink> logger) : IFeedbackSink
{
    public const string Name = "log";

    public Task<FeedbackSinkResult> SendAsync(FeedbackTicket ticket, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        LogFeedback(logger, ticket.App, ticket.AppVersion, ticket.UserEmail, ticket.Route, ticket.UserAgent,
            ticket.ScreenshotPng?.Length ?? 0, ticket.Message);
        return Task.FromResult(FeedbackSinkResult.Ok(Name, "logged"));
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Feedback for {App} {AppVersion} from {UserEmail} on {Route} ({UserAgent}; screenshot {ScreenshotBytes} bytes): {Message}")]
    private static partial void LogFeedback(ILogger logger, string app, string appVersion, string userEmail, string route,
        string userAgent, int screenshotBytes, string message);
}
