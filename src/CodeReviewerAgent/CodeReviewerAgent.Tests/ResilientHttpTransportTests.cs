using CodeReviewerAgent.Infra;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class ResilientHttpTransportTests
{
    // Drives ResilientHttpTransport with a no-op backoff so tests never actually sleep.
    private static string Post(FakeHttpTransport inner, int maxAttempts = 3) =>
        new ResilientHttpTransport(inner, maxAttempts, backoff: _ => { }).Post("/x", "{}");

    [Fact]
    public void Returns_on_first_success()
    {
        var inner = new FakeHttpTransport(() => "ok");

        Assert.Equal("ok", Post(inner));
        Assert.Equal(1, inner.PostCount);
    }

    [Fact]
    public void Retries_transient_then_succeeds()
    {
        var inner = new FakeHttpTransport(
            () => throw new TransientHttpException("t"),
            () => "ok");

        Assert.Equal("ok", Post(inner));
        Assert.Equal(2, inner.PostCount);
    }

    [Fact]
    public void Propagates_after_exhausting_retries()
    {
        var inner = new FakeHttpTransport(
            () => throw new TransientHttpException("t"),
            () => throw new TransientHttpException("t"),
            () => throw new TransientHttpException("t"));

        Assert.Throws<TransientHttpException>(() => Post(inner, maxAttempts: 3));
        Assert.Equal(3, inner.PostCount);
    }

    [Fact]
    public void Does_not_retry_permanent()
    {
        var inner = new FakeHttpTransport(
            () => throw new HttpRequestException("permanent"),
            () => "ok"); // must never be reached

        Assert.Throws<HttpRequestException>(() => Post(inner));
        Assert.Equal(1, inner.PostCount);
    }

    /// <summary>
    /// A 429 says when to come back. Guessing an exponential curve instead means either
    /// hammering a closed window or waiting far longer than the server asked.
    /// </summary>
    [Fact]
    public void Waits_for_the_delay_the_server_asked_for()
    {
        var waits = new List<TimeSpan>();
        var inner = new FakeHttpTransport(
            () => throw new TransientHttpException("429", retryAfter: TimeSpan.FromSeconds(7)),
            () => "ok");

        new ResilientHttpTransport(inner, maxAttempts: 3, backoff: waits.Add).Post("/x", "{}");

        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(waits));
    }

    [Fact]
    public void Falls_back_to_exponential_backoff_when_the_server_says_nothing()
    {
        var waits = new List<TimeSpan>();
        var inner = new FakeHttpTransport(
            () => throw new TransientHttpException("t"),
            () => throw new TransientHttpException("t"),
            () => "ok");

        new ResilientHttpTransport(inner, maxAttempts: 3, backoff: waits.Add).Post("/x", "{}");

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], waits);
    }

    /// <summary>
    /// A token-per-minute window can reopen a minute out. Honouring that is the point of
    /// reading the header, so it must not be clipped back to the exponential curve.
    /// </summary>
    [Fact]
    public void Honours_a_long_delay_without_capping_it_to_the_curve()
    {
        var waits = new List<TimeSpan>();
        var inner = new FakeHttpTransport(
            () => throw new TransientHttpException("429", retryAfter: TimeSpan.FromSeconds(45)),
            () => "ok");

        new ResilientHttpTransport(inner, maxAttempts: 3, backoff: waits.Add).Post("/x", "{}");

        Assert.Equal(TimeSpan.FromSeconds(45), Assert.Single(waits));
    }
}
