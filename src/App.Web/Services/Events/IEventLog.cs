namespace App.Web.Services.Events;

/// <summary>
/// One auditable thing that happened: a sign-in decision, a record changed, an export run.
/// <paramref name="Detail"/> is serialized as JSON by the sink; keep it free of secrets and of student data.
/// </summary>
public sealed record AppEvent(string Action, string? Status = null, string? Identity = null, object? Detail = null);

/// <summary>
/// A central, queryable audit trail, separate from diagnostic logging (which goes through ILogger).
///
/// The template ships <see cref="NoOpEventLog"/>. To send events to your organization's central store, add
/// an implementation (for example one that executes an insert stored procedure with Integrated Security),
/// register it in Program.cs when its connection string is configured, and keep the no-op as the fallback so
/// the app always boots. Reuse the organization's existing store; do not build a parallel logging database.
///
/// Implementations may throw. Callers on paths where logging must not fail the user's action use
/// <see cref="EventLogExtensions.SafeWriteAsync"/>.
/// </summary>
public interface IEventLog
{
    Task WriteAsync(AppEvent appEvent, CancellationToken ct = default);
}

/// <summary>Default sink: discards events. Registered when no central event store is configured.</summary>
public sealed class NoOpEventLog : IEventLog
{
    public Task WriteAsync(AppEvent appEvent, CancellationToken ct = default) => Task.CompletedTask;
}

public static partial class EventLogExtensions
{
    /// <summary>
    /// Writes the event, and on failure logs a warning instead of throwing: an audit-store outage must not
    /// fail an action that already succeeded. The failure is still visible in the diagnostic log.
    /// </summary>
    public static async Task SafeWriteAsync(this IEventLog log, AppEvent appEvent, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await log.WriteAsync(appEvent, ct);
        }
#pragma warning disable CA1031 // Deliberate: this is the one place an event-store failure is contained, and it is logged.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogWriteFailed(logger, ex, appEvent.Action);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event log write failed for action {Action}")]
    private static partial void LogWriteFailed(ILogger logger, Exception ex, string action);
}
