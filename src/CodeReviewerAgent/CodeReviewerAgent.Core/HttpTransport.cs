using System.Net;
using System.Text;

namespace CodeReviewerAgent.Core
{
    /// <summary>Sends a JSON POST and returns the response body, throwing on failure.</summary>
    internal interface IHttpTransport
    {
        string Post(string path, string json);
    }

    /// <summary>A transient (retryable) HTTP failure: 429, 5xx, network error, or timeout.</summary>
    internal sealed class TransientHttpException(string message, Exception? inner = null)
        : Exception(message, inner);

    /// <summary>
    /// A single JSON POST over an <see cref="HttpClient"/>. Returns the body on success;
    /// throws <see cref="TransientHttpException"/> for retryable failures (429 / 5xx / network /
    /// timeout) and <see cref="HttpRequestException"/> for permanent ones (other 4xx).
    /// </summary>
    internal sealed class HttpTransport(HttpClient http) : IHttpTransport
    {
        public string Post(string path, string json)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = http.PostAsync(path, content).GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex)
            {
                throw new TransientHttpException("Network error.", ex);
            }
            catch (TaskCanceledException ex)
            {
                throw new TransientHttpException("Request timed out.", ex);
            }

            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
                return body;

            var status = (int)response.StatusCode;
            if (status == 429 || status >= 500)
                throw new TransientHttpException($"HTTP {status}: {body}");
            throw new HttpRequestException($"HTTP request failed ({status}): {body}");
        }
    }

    /// <summary>
    /// Decorator that adds retry + exponential backoff over an <see cref="IHttpTransport"/>,
    /// retrying only <see cref="TransientHttpException"/>; everything else propagates (fail fast).
    /// maxAttempts and backoff are seams for tests; production uses the defaults.
    /// </summary>
    internal sealed class ResilientHttpTransport(
        IHttpTransport inner, int maxAttempts = 3, Action<TimeSpan>? backoff = null) : IHttpTransport
    {
        private readonly Action<TimeSpan> _backoff = backoff ?? Thread.Sleep;

        public string Post(string path, string json)
        {
            var attempt = 1;
            while (true)
            {
                try
                {
                    return inner.Post(path, json);
                }
                catch (TransientHttpException) when (attempt < maxAttempts)
                {
                    // retryable — fall through to backoff and retry
                }

                _backoff(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1))); // 1s, 2s, ...
                attempt++;
            }
        }
    }
}
