using System.Net;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    public class HttpResilienceTests
    {
        private static HttpResponseMessage Ok(string body = "ok") =>
            new(HttpStatusCode.OK) { Content = new StringContent(body) };

        private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

        // Drives HttpResilience.Post with a no-op backoff so tests never actually sleep.
        private static (HttpResponseMessage Response, string Body) Post(
            StubHttpMessageHandler handler, int maxAttempts = 3)
        {
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local") };
            return HttpResilience.Post(
                http, "/x", "{}",
                maxAttempts: maxAttempts, backoff: _ => { });
        }

        [Fact]
        public void Returns_on_first_success()
        {
            var handler = new StubHttpMessageHandler(() => Ok("done"));

            var (response, body) = Post(handler);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("done", body);
            Assert.Equal(1, handler.SendCount);
        }

        [Fact]
        public void Retries_transient_status_then_succeeds()
        {
            var handler = new StubHttpMessageHandler(
                () => Status(HttpStatusCode.ServiceUnavailable),
                () => Ok());

            var (response, _) = Post(handler);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, handler.SendCount);
        }

        [Fact]
        public void Retries_on_429()
        {
            var handler = new StubHttpMessageHandler(
                () => Status(HttpStatusCode.TooManyRequests),
                () => Ok());

            var (response, _) = Post(handler);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, handler.SendCount);
        }

        [Fact]
        public void Does_not_retry_non_transient_status()
        {
            var handler = new StubHttpMessageHandler(
                () => Status(HttpStatusCode.BadRequest),
                () => Ok()); // must never be reached

            var (response, _) = Post(handler);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(1, handler.SendCount);
        }

        [Fact]
        public void Returns_last_response_after_exhausting_retries()
        {
            var handler = new StubHttpMessageHandler(
                () => Status(HttpStatusCode.ServiceUnavailable),
                () => Status(HttpStatusCode.ServiceUnavailable),
                () => Status(HttpStatusCode.ServiceUnavailable));

            var (response, _) = Post(handler, maxAttempts: 3);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(3, handler.SendCount);
        }

        [Fact]
        public void Retries_on_transient_exception_then_succeeds()
        {
            var handler = new StubHttpMessageHandler(
                () => throw new HttpRequestException("network down"),
                () => Ok());

            var (response, _) = Post(handler);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, handler.SendCount);
        }

        [Fact]
        public void Rethrows_when_all_attempts_time_out()
        {
            var handler = new StubHttpMessageHandler(
                () => throw new TaskCanceledException("timeout"),
                () => throw new TaskCanceledException("timeout"),
                () => throw new TaskCanceledException("timeout"));

            Assert.Throws<TaskCanceledException>(() => Post(handler, maxAttempts: 3));
            Assert.Equal(3, handler.SendCount);
        }
    }
}
