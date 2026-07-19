using System.Net;
using CodeReviewerAgent.Infra;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    public class HttpTransportTests
    {
        private static HttpResponseMessage Ok(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body) };

        private static HttpResponseMessage Status(HttpStatusCode code) =>
            new(code) { Content = new StringContent("err") };

        private static string Post(StubHttpMessageHandler handler)
        {
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local") };
            return new HttpTransport(http).Post("/x", "{}");
        }

        [Fact]
        public void Returns_body_on_success()
        {
            var body = Post(new StubHttpMessageHandler(() => Ok("done")));
            Assert.Equal("done", body);
        }

        [Fact]
        public void Throws_transient_on_429()
        {
            var handler = new StubHttpMessageHandler(() => Status(HttpStatusCode.TooManyRequests));
            Assert.Throws<TransientHttpException>(() => Post(handler));
        }

        [Fact]
        public void Throws_transient_on_500()
        {
            var handler = new StubHttpMessageHandler(() => Status(HttpStatusCode.InternalServerError));
            Assert.Throws<TransientHttpException>(() => Post(handler));
        }

        [Fact]
        public void Throws_permanent_on_400()
        {
            var handler = new StubHttpMessageHandler(() => Status(HttpStatusCode.BadRequest));
            Assert.Throws<HttpRequestException>(() => Post(handler));
        }

        [Fact]
        public void Wraps_network_error_as_transient()
        {
            var handler = new StubHttpMessageHandler(() => throw new HttpRequestException("boom"));
            Assert.Throws<TransientHttpException>(() => Post(handler));
        }
    }
}
