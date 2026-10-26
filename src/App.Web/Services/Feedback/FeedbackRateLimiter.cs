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

            // Bounded memory: a script hammering a refused identity must not grow the queue for the whole
            // window. Keeping the newest max+1 timestamps preserves the lockout (the count in the window
            // stays at or above the limit while attempts keep arriving) and drops only entries that would
            // have expired first anyway.
            while (queue.Count > maxPerWindow + 1)
                queue.Dequeue();

            return allowed;
        }
    }

    /// <summary>How many timestamps are held for an identity. For tests of the bound; not a rate.</summary>
    internal int TrackedAttempts(string identity)
    {
        if (!_submissions.TryGetValue(identity, out var queue))
            return 0;
        lock (queue)
            return queue.Count;
    }
}
