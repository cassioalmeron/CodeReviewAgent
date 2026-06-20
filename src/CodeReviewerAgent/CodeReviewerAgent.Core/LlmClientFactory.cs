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
                "ollama" => new OllamaClient(),
                "claude" => new AnthropicClient(),
                _ => throw new InvalidOperationException(
                    $"Unknown LLM_ENGINE '{engine}'. Supported values: 'ollama', 'claude'."),
            };
        }
    }
}