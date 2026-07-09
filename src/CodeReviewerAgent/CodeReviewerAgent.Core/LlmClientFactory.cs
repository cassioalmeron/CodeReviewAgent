namespace CodeReviewerAgent.Core
{
    public static class LlmClientFactory
    {
        public static ILlmClient Create()
        {
            var engine = Environment.GetEnvironmentVariable("LLM_ENGINE")
                ?? throw new InvalidOperationException("LLM_ENGINE is not configured. Add it to the .env file.");

            return engine.ToLowerInvariant() switch
            {
                "ollama" => CreateOllama(),
                "claude" => CreateClaude(),
                "claude-code" => new ClaudeCodeClient(),
                "claude-cli" => new ClaudeCliClient(),
                _ => throw new InvalidOperationException(
                    $"Unknown LLM_ENGINE '{engine}'. Supported values: 'ollama', 'claude', 'claude-code', 'claude-cli'."),
            };
        }

        // Builds a Claude (Anthropic HTTP) client. The judge uses this with a stronger model.
        public static ILlmClient CreateClaude(string? model = null)
        {
            var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not configured. Add it to the .env file.");
            var resolvedModel = model
                ?? Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")
                ?? throw new InvalidOperationException("ANTHROPIC_MODEL is not configured. Add it to the .env file.");

            var http = new HttpClient
            {
                BaseAddress = new Uri("https://api.anthropic.com"),
                Timeout = TimeSpan.FromSeconds(120),
            };
            http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            return new AnthropicClient(Resilient(http), resolvedModel);
        }

        private static ILlmClient CreateOllama()
        {
            var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL")
                ?? throw new InvalidOperationException("OLLAMA_MODEL is not configured. Add it to the .env file.");
            var host = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";

            var http = new HttpClient { BaseAddress = new Uri(host), Timeout = TimeSpan.FromSeconds(120) };
            return new OllamaClient(Resilient(http), model);
        }

        // Composes the resilient HTTP transport shared by every HTTP-based client.
        private static IHttpTransport Resilient(HttpClient http) =>
            new ResilientHttpTransport(new HttpTransport(http));
    }
}
