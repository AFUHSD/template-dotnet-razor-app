using App.Web.Services.Feedback;

namespace App.Web.Tests;

public sealed class FeedbackRateLimiterTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    [Fact]
    public void The_limit_admits_exactly_the_configured_count_per_window()
    {
        var clock = new FakeTime();
        var limiter = new FeedbackRateLimiter(clock);

        for (var i = 0; i < 5; i++)
            Assert.True(limiter.TryAcquire("person@example.org", 5, Window), $"attempt {i + 1} should be allowed");

        Assert.False(limiter.TryAcquire("person@example.org", 5, Window));
    }

    [Fact]
    public void The_window_slides_and_a_refused_attempt_still_counts()
    {
        var clock = new FakeTime();
        var limiter = new FeedbackRateLimiter(clock);
        for (var i = 0; i < 5; i++)
            limiter.TryAcquire("person@example.org", 5, Window);
        Assert.False(limiter.TryAcquire("person@example.org", 5, Window)); // sixth, refused, at t=0

        clock.Advance(Window - TimeSpan.FromSeconds(1));
        Assert.False(limiter.TryAcquire("person@example.org", 5, Window)); // the first five have not expired

        clock.Advance(TimeSpan.FromSeconds(2));
        // The six from t=0 (five allowed, one refused) have aged out; the refused seventh (t=9:59) remains
        // and counts, so four more are allowed, not five.
        Assert.True(limiter.TryAcquire("person@example.org", 5, Window));
        Assert.True(limiter.TryAcquire("person@example.org", 5, Window));
        Assert.True(limiter.TryAcquire("person@example.org", 5, Window));
        Assert.True(limiter.TryAcquire("person@example.org", 5, Window));
        Assert.False(limiter.TryAcquire("person@example.org", 5, Window));
    }

    [Fact]
    public void Identities_are_independent_and_compared_case_insensitively()
    {
        var limiter = new FeedbackRateLimiter(new FakeTime());
        for (var i = 0; i < 5; i++)
            limiter.TryAcquire("one@example.org", 5, Window);

        Assert.False(limiter.TryAcquire("ONE@example.org", 5, Window));
        Assert.True(limiter.TryAcquire("two@example.org", 5, Window));
    }

    [Fact]
    public void A_hammering_client_does_not_grow_memory_and_stays_locked_out()
    {
        // Codex review on #5: every refused attempt used to enqueue a timestamp for the whole window.
        var clock = new FakeTime();
        var limiter = new FeedbackRateLimiter(clock);

        for (var i = 0; i < 500; i++)
        {
            limiter.TryAcquire("script@example.org", 5, Window);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.True(limiter.TrackedAttempts("script@example.org") <= 6);
        Assert.False(limiter.TryAcquire("script@example.org", 5, Window)); // still locked out while attempts keep coming

        clock.Advance(Window);
        Assert.True(limiter.TryAcquire("script@example.org", 5, Window)); // and free again once it stops
    }

    [Fact]
    public void A_zero_limit_or_a_zero_window_refuses_everything()
    {
        var limiter = new FeedbackRateLimiter(new FakeTime());
        Assert.False(limiter.TryAcquire("person@example.org", 0, Window));
        // A zero window would otherwise expire every earlier attempt and admit everything (Codex review on #5).
        Assert.False(limiter.TryAcquire("person@example.org", 5, TimeSpan.Zero));
        Assert.False(limiter.TryAcquire("person@example.org", 5, TimeSpan.FromMinutes(-1)));
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
