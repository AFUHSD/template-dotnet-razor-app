using System.Collections.Concurrent;

namespace App.Web.Services.Feedback;

/// <summary>
/// Sliding-window limit per identity, in memory. It protects the help desk from a stuck key or a script,
/// not from an attacker: it resets when the process recycles and is not shared across a web garden.
/// <see cref="TimeProvider"/> is injected so a test can move the clock.
/// </summary>
public sealed class FeedbackRateLimiter(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _submissions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records an attempt and reports whether it is within the limit. The attempt counts even when refused,
    /// so hammering the endpoint extends the wait rather than shortening it.
    /// </summary>
    public bool TryAcquire(string identity, int maxPerWindow, TimeSpan window)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        // A zero or negative window would expire every earlier attempt and admit everything. Options are
        // validated at startup; this is the belt to that suspender.
        if (maxPerWindow <= 0 || window <= TimeSpan.Zero)
            return false;

        var now = time.GetUtcNow();
        var queue = _submissions.GetOrAdd(identity, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() >= window)
                queue.Dequeue();

            var allowed = queue.Count < maxPerWindow;
            queue.Enqueue(now);
            return allowed;
        }
    }
}
