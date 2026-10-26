using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using App.Web.Options;
using Microsoft.Extensions.Options;

namespace App.Web.Services.Feedback;

/// <summary>
/// Files the ticket through the help desk's HTTP intake. Never a direct database insert: a ticket the help
/// desk did not create itself does not notify anyone, so it would sit unseen.
///
/// The handshake (ServiceHub <c>POST /api/intake/feedback</c>):
/// <code>
/// POST {Feedback:ServiceHubIntakeUrl}
/// Authorization: Bearer {Feedback:ServiceHubIntakeKey}
/// Content-Type: application/json
/// { "app", "appVersion", "route", "userEmail", "userAgent", "message", "screenshotDataUrl"?, "submittedUtc" }
///
/// 201 { "ticketId": 123, "url": "https://host/servicehub/portal/tickets/123" }
/// 400 { "error": "..." }   bad payload (the intake re-validates every cap)
/// 401                       key missing or wrong
/// 404                       intake not enabled on that server
/// 422 { "error": "..." }   the person has no help-desk account
/// </code>
/// The intake files the ticket as <c>userEmail</c> (the requester), so the help desk talks to the person who
/// reported it, and puts <c>app</c>, <c>route</c>, <c>appVersion</c> and <c>userAgent</c> in the description.
/// </summary>
public sealed partial class ServiceHubFeedbackSink(
    HttpClient http,
    IOptions<FeedbackOptions> options,
    ILogger<ServiceHubFeedbackSink> logger) : IFeedbackSink
{
    public const string Name = "servicehub";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<FeedbackSinkResult> SendAsync(FeedbackTicket ticket, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var settings = options.Value;
        if (settings.ServiceHubIntakeUrl is null)
            return FeedbackSinkResult.Failed(Name, "Feedback__ServiceHubIntakeUrl is not configured.");
        if (string.IsNullOrWhiteSpace(settings.ServiceHubIntakeKey))
            return FeedbackSinkResult.Failed(Name, "Feedback__ServiceHubIntakeKey is not configured.");

        var body = new IntakeRequest(
            ticket.App,
            ticket.AppVersion,
            ticket.Route,
            ticket.UserEmail,
            ticket.UserAgent,
            ticket.Message,
            ticket.ScreenshotPng is { } png ? "data:image/png;base64," + Convert.ToBase64String(png.Span) : null,
            ticket.SubmittedUtc);

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.ServiceHubIntakeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ServiceHubIntakeKey);
        request.Content = JsonContent.Create(body, options: Json);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                // Only the documented answer counts as filed: 201 with a ticket number. A proxy's 200 page or
                // a changed upstream shape must not be reported to the person as "sent".
                var created = response.StatusCode == System.Net.HttpStatusCode.Created
                    ? await response.Content.ReadFromJsonAsync<IntakeResponse>(Json, ct)
                    : null;
                if (created?.TicketId is long ticketId)
                    return FeedbackSinkResult.Ok(Name, ticketId.ToString(System.Globalization.CultureInfo.InvariantCulture));

                LogRejected(logger, (int)response.StatusCode);
                return FeedbackSinkResult.Failed(Name, "The help desk answered without a ticket number.");
            }

            // The intake's error text is written for the requester ("no help-desk account"); pass it on.
            // Anything else is reported by status only, so nothing about the help desk's internals is echoed.
            var error = (int)response.StatusCode is 400 or 422
                ? (await response.Content.ReadFromJsonAsync<IntakeResponse>(Json, ct))?.Error
                : null;
            LogRejected(logger, (int)response.StatusCode);
            return FeedbackSinkResult.Failed(Name, error ?? $"The help desk answered {(int)response.StatusCode}.");
        }
        catch (HttpRequestException ex)
        {
            LogUnreachable(logger, ex);
            return FeedbackSinkResult.Failed(Name, "The help desk could not be reached.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout, not the caller's token.
            LogUnreachable(logger, null);
            return FeedbackSinkResult.Failed(Name, "The help desk did not answer in time.");
        }
        catch (JsonException ex)
        {
            LogUnreachable(logger, ex);
            return FeedbackSinkResult.Failed(Name, "The help desk answered in an unexpected shape.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Feedback intake rejected the ticket with HTTP {StatusCode}")]
    private static partial void LogRejected(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Feedback intake unreachable")]
    private static partial void LogUnreachable(ILogger logger, Exception? ex);

    /// <summary>The documented wire shape. Property names serialize camelCase.</summary>
    internal sealed record IntakeRequest(
        string App,
        string AppVersion,
        string Route,
        string UserEmail,
        string UserAgent,
        string Message,
        string? ScreenshotDataUrl,
        DateTimeOffset SubmittedUtc);

    internal sealed record IntakeResponse(long? TicketId, Uri? Url, string? Error);
}
