using System.Net;
using System.Text;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// POSTs a JSON body with retry + exponential backoff on transient failures (429, 5xx,
    /// network errors, timeouts). Non-transient failures (e.g. 400) are returned immediately
    /// for the caller to handle. A fresh <see cref="StringContent"/> is built per attempt,
    /// since an HttpContent can only be sent once.
    /// </summary>
    internal static class HttpResilience
    {
        private const int DefaultMaxAttempts = 3;

        // maxAttempts and backoff are seams for tests (fast, deterministic); production
        // uses the defaults (3 attempts, real exponential sleep).
        public static (HttpResponseMessage Response, string Body) Post(
            HttpClient http, string path, string json,
            int maxAttempts = DefaultMaxAttempts, Action<TimeSpan>? backoff = null)
        {
            backoff ??= Thread.Sleep;

            var attempt = 1;
            while (true)
            {
                try
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = http.PostAsync(path, content).GetAwaiter().GetResult();
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (response.IsSuccessStatusCode || !IsTransient(response.StatusCode) || attempt >= maxAttempts)
                        return (response, body);

                    response.Dispose(); // transient status — will retry
                }
                catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
                {
                    // transient network error or timeout — fall through to backoff and retry
                }

                backoff(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1))); // 1s, 2s, ...
                attempt++;
            }
        }

        private static bool IsTransient(HttpStatusCode status) =>
            status == HttpStatusCode.TooManyRequests || (int)status >= 500;

        private static bool IsTransient(Exception ex) =>
            ex is HttpRequestException or TaskCanceledException;
    }
}
