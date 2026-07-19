using CodeReviewerAgent.Infra;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests
{
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
    }
}
