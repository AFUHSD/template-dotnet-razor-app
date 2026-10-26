using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using App.Web.Options;
using App.Web.Services.Events;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace App.Web.Services.Feedback;

/// <summary>
/// <c>POST /feedback</c>: the embedded feedback control's endpoint. Behind the fallback policy like every
/// other route (signed in, with the app role); anti-forgery checked by hand from the
/// <c>RequestVerificationToken</c> header, because the body is JSON rather than a form.
///
/// Order of checks is deliberate: feature flag, anti-forgery, body size, shape, rate limit. The rate
/// limiter runs last so a malformed request from a stuck script does not burn a real person's allowance
/// before anything is validated.
/// </summary>
public static partial class FeedbackEndpoints
{
    public const string Route = "/feedback";

    /// <summary>Body of the POST. <c>screenshotDataUrl</c> is <c>data:image/png;base64,...</c> or absent.</summary>
    public sealed record FeedbackRequest(string? Message, string? ScreenshotDataUrl);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static void MapFeedbackEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Route, HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IAntiforgery antiforgery,
        IOptions<FeedbackOptions> options,
        IHostEnvironment environment,
        FeedbackRateLimiter limiter,
        IFeedbackSink sink,
        IEventLog events,
        ILoggerFactory loggerFactory,
        TimeProvider time)
    {
        var settings = options.Value;
        var logger = loggerFactory.CreateLogger(typeof(FeedbackEndpoints));
        var ct = http.RequestAborted;

        if (!settings.IsEnabled(environment))
            return Results.NotFound();

        if (!await antiforgery.IsRequestValidAsync(http))
            return Results.BadRequest(new { error = "The page has expired. Reload it and try again." });

        // Size first, before anything is read. ContentLength can be absent (chunked), so the read is
        // bounded as well: one byte over the cap and the whole request is refused.
        if (http.Request.ContentLength is > FeedbackLimits.MaxBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        var body = await ReadBoundedAsync(http.Request.Body, FeedbackLimits.MaxBodyBytes, ct);
        if (body is null)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        FeedbackRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<FeedbackRequest>(body.Value.Span, Json);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "The request was not understood." });
        }

        var message = request?.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return Results.BadRequest(new { error = "Type what happened before sending." });
        if (message.Length > FeedbackLimits.MaxMessageChars)
            return Results.BadRequest(new { error = $"Keep it under {FeedbackLimits.MaxMessageChars:N0} characters." });

        ReadOnlyMemory<byte>? screenshot = null;
        if (!string.IsNullOrWhiteSpace(request?.ScreenshotDataUrl))
        {
            if (!settings.AllowScreenshots)
                return Results.BadRequest(new { error = "Screenshots are switched off for this app." });
            screenshot = DecodePngDataUrl(request.ScreenshotDataUrl);
            if (screenshot is null)
                return Results.BadRequest(new { error = "The screenshot must be a PNG of at most 1 MB." });
        }

        var identity = http.User.FindFirstValue(ClaimTypes.Email) ?? http.User.Identity?.Name ?? "";
        if (string.IsNullOrWhiteSpace(identity))
            return Results.BadRequest(new { error = "Could not tell who you are. Sign in again." });

        if (!limiter.TryAcquire(identity, settings.MaxPerWindow, TimeSpan.FromMinutes(settings.WindowMinutes)))
            return Results.Json(new { error = "You have sent several messages recently. Please wait a few minutes." },
                statusCode: StatusCodes.Status429TooManyRequests);

        // Where the person was. The referer is the page the dialog sat on; the endpoint's own path is not.
        var pageUrl = http.Request.Headers.Referer.ToString();
        var route = FeedbackRedactor.Redact(RouteOf(pageUrl, http.Request.PathBase));
        var userAgent = Truncate(http.Request.Headers.UserAgent.ToString(), FeedbackLimits.MaxUserAgentChars);

        var ticket = new FeedbackTicket(
            settings.AppName,
            AppVersion(),
            Truncate(route, FeedbackLimits.MaxRouteChars),
            identity,
            userAgent,
            FeedbackRedactor.Redact(message),
            screenshot,
            time.GetUtcNow());

        var result = await sink.SendAsync(ticket, ct);

        // The audit row every app writes, whichever sink is in use. Detail never contains the message or the
        // screenshot: AppEvent.Detail is contractually free of student data, the redactor cannot recognise a
        // name, and the message lives with the ticket (or in the diagnostic log for the fallback sink).
        await events.SafeWriteAsync(new AppEvent("feedback", result.Accepted ? "Sent" : "Failed", identity, new
        {
            app = ticket.App,
            appVersion = ticket.AppVersion,
            route = ticket.Route,
            sink = result.Sink,
            reference = result.Reference,
            error = result.Error,
            screenshot = screenshot is not null,
            messageLength = ticket.Message.Length,
        }), logger, ct);

        if (!result.Accepted)
        {
            LogNotFiled(logger, result.Sink, result.Error);
            return Results.Json(new { error = result.Error ?? "Your feedback could not be filed. Try again later." },
                statusCode: StatusCodes.Status502BadGateway);
        }

        LogFiled(logger, result.Sink, result.Reference);
        return Results.Ok(new { reference = result.Reference, sink = result.Sink });
    }

    /// <summary>Reads up to <paramref name="max"/> bytes; null when the stream has more than that.</summary>
    private static async Task<ReadOnlyMemory<byte>?> ReadBoundedAsync(Stream stream, long max, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > max)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Base64-decodes a <c>data:image/png;base64,</c> URL, refusing anything that is not a PNG by signature or
    /// is over the cap. The cap is checked on the encoded length first, so an oversize body is never decoded.
    /// </summary>
    internal static ReadOnlyMemory<byte>? DecodePngDataUrl(string dataUrl)
    {
        const string prefix = "data:image/png;base64,";
        if (!dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var encoded = dataUrl.AsSpan(prefix.Length);
        if (encoded.Length > (FeedbackLimits.MaxScreenshotBytes + 2) / 3 * 4)
            return null;

        var bytes = new byte[encoded.Length / 4 * 3 + 3];
        if (!Convert.TryFromBase64Chars(encoded, bytes, out var written))
            return null;
        if (written > FeedbackLimits.MaxScreenshotBytes || written < PngSignature.Length)
            return null;
        if (!bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            return null;

        return new ReadOnlyMemory<byte>(bytes, 0, written);
    }

    /// <summary>
    /// The page route from the referer: path and query only, never the origin. Falls back to the PathBase
    /// when the browser sent no referer (a strict referrer policy), so the ticket still names the app.
    /// </summary>
    internal static string RouteOf(string? referer, PathString pathBase)
    {
        if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var uri))
            return uri.PathAndQuery;
        return pathBase.HasValue ? pathBase.Value! : "/";
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string AppVersion() =>
        typeof(FeedbackEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(FeedbackEndpoints).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    [LoggerMessage(Level = LogLevel.Information, Message = "Feedback filed through {Sink} as {Reference}")]
    private static partial void LogFiled(ILogger logger, string sink, string? reference);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Feedback not filed through {Sink}: {Error}")]
    private static partial void LogNotFiled(ILogger logger, string sink, string? error);
}
