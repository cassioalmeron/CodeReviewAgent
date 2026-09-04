using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Llm;

namespace CodeReviewerAgent.Infra;

public static class LlmClientFactory
{
    public static ILlmClient Create() => CreateWithModel().Client;

    /// <summary>
    /// The client, plus the model it will actually call. A caller that keys durable state on the
    /// model has to know it <em>before</em> spending anything: the golden set skips a round a
    /// previous run already paid for, and a round bought from another model answers a different
    /// question. Reusing one reports the old model's numbers under the new model's name, and it
    /// does so silently.
    /// <para>
    /// The value comes back from the same switch that builds the client, so the two cannot
    /// drift. Null means the model could not be established — the CLI-backed engines fall back
    /// to whatever the CLI itself picks — and null must match no configuration, never any.
    /// </para>
    /// </summary>
    public static (ILlmClient Client, string? Model) CreateWithModel()
    {
        var engine = Environment.GetEnvironmentVariable("LLM_ENGINE")
            ?? throw new InvalidOperationException("LLM_ENGINE is not configured. Add it to the .env file.");

        return engine.ToLowerInvariant() switch
        {
            "ollama" => CreateOllama(),
            "claude" => CreateClaudeWithModel(null),
            "openai" => CreateOpenAi(),
            "openrouter" => CreateOpenRouter(),
            // These read CLAUDE_CODE_MODEL themselves and treat blank as "the CLI decides".
            // Reading the same variable here reports what was asked for; blank stays null, which
            // is the honest answer, because the choice is not visible from this side.
            "claude-code" => (new ClaudeCodeClient(), CliModel()),
            "claude-cli" => (new ClaudeCliClient(), CliModel()),
            _ => throw new InvalidOperationException(
                $"Unknown LLM_ENGINE '{engine}'. Supported values: 'ollama', 'claude', 'openai', 'openrouter', 'claude-code', 'claude-cli'."),
        };
    }

    private static string? CliModel()
    {
        var model = Environment.GetEnvironmentVariable("CLAUDE_CODE_MODEL");
        return string.IsNullOrWhiteSpace(model) ? null : model;
    }

    // Builds a Claude (Anthropic HTTP) client. The judge uses this with a stronger model.
    public static ILlmClient CreateClaude(string? model = null) => CreateClaudeWithModel(model).Client;

    private static (ILlmClient Client, string? Model) CreateClaudeWithModel(string? model)
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

        return (new AnthropicClient(Resilient(http), resolvedModel), resolvedModel);
    }

    private static (ILlmClient Client, string? Model) CreateOllama()
    {
        var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL")
            ?? throw new InvalidOperationException("OLLAMA_MODEL is not configured. Add it to the .env file.");
        var host = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";

        var http = new HttpClient { BaseAddress = new Uri(host), Timeout = TimeSpan.FromSeconds(120) };
        return (new OllamaClient(Resilient(http), model), model);
    }

    private static (ILlmClient Client, string? Model) CreateOpenAi()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured. Add it to the .env file.");
        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL")
            ?? throw new InvalidOperationException("OPENAI_MODEL is not configured. Add it to the .env file.");

        var http = new HttpClient
        {
            BaseAddress = new Uri("https://api.openai.com"),
            Timeout = TimeSpan.FromSeconds(120),
        };
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        return (new OpenAiClient(Resilient(http), model), model);
    }

    private static (ILlmClient Client, string? Model) CreateOpenRouter()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            ?? throw new InvalidOperationException("OPENROUTER_API_KEY is not configured. Add it to the .env file.");
        var model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL")
            ?? throw new InvalidOperationException("OPENROUTER_MODEL is not configured. Add it to the .env file.");

        var http = new HttpClient
        {
            BaseAddress = new Uri("https://openrouter.ai"),
            Timeout = TimeSpan.FromSeconds(120),
        };
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        return (new OpenRouterClient(Resilient(http), model), model);
    }

    // Composes the resilient HTTP transport shared by every HTTP-based client.
    private static IHttpTransport Resilient(HttpClient http) =>
        new ResilientHttpTransport(new HttpTransport(http));
}
