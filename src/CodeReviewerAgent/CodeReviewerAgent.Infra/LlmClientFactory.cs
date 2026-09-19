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
        var temperature = ExecutorTemperature();

        return engine.ToLowerInvariant() switch
        {
            "ollama" => CreateOllama(temperature),
            "claude" => CreateClaudeWithModel(null, temperature),
            "openai" => CreateOpenAi(temperature),
            "openrouter" => CreateOpenRouter(temperature),
            // These read CLAUDE_CODE_MODEL themselves and treat blank as "the CLI decides".
            // Reading the same variable here reports what was asked for; blank stays null, which
            // is the honest answer, because the choice is not visible from this side.
            "claude-code" => (new ClaudeCodeClient(), CliModel()),
            "claude-cli" => (new ClaudeCliClient(), CliModel()),
            _ => throw new InvalidOperationException(
                $"Unknown LLM_ENGINE '{engine}'. Supported values: 'ollama', 'claude', 'openai', 'openrouter', 'claude-code', 'claude-cli'."),
        };
    }

    /// <summary>
    /// <c>LLM_TEMPERATURE</c>, for the executor only: the judge is built through
    /// <see cref="CreateClaude"/> and keeps its own settings. Unset means the provider's default,
    /// which is 1.0 on OpenAI and the setting that varies the answer most; the golden set in CI runs
    /// at 0 so a change is measured against the model's past and not against its dice.
    /// </summary>
    public static double? ExecutorTemperature()
    {
        var configured = Environment.GetEnvironmentVariable("LLM_TEMPERATURE");
        if (string.IsNullOrWhiteSpace(configured))
            return null;

        return double.TryParse(configured, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : throw new InvalidOperationException(
                $"LLM_TEMPERATURE '{configured}' is not a non-negative number (use a dot: 0.2).");
    }

    private static string? CliModel()
    {
        var model = Environment.GetEnvironmentVariable("CLAUDE_CODE_MODEL");
        return string.IsNullOrWhiteSpace(model) ? null : model;
    }

    // Builds a Claude (Anthropic HTTP) client. The judge uses this with a stronger model.
    public static ILlmClient CreateClaude(string? model = null) => CreateClaudeWithModel(model).Client;

    private static (ILlmClient Client, string? Model) CreateClaudeWithModel(string? model, double? temperature = null)
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

        return (new AnthropicClient(Resilient(http), resolvedModel, temperature), resolvedModel);
    }

    private static (ILlmClient Client, string? Model) CreateOllama(double? temperature)
    {
        var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL")
            ?? throw new InvalidOperationException("OLLAMA_MODEL is not configured. Add it to the .env file.");
        var host = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";

        // A local model answers at the speed of the machine it runs on, and the resilient transport
        // retries a timeout from scratch: a limit sized for a hosted API would throw away minutes
        // of local work on every attempt. Hosted engines keep their two minutes.
        var http = new HttpClient { BaseAddress = new Uri(host), Timeout = TimeoutFrom("OLLAMA_TIMEOUT_SECONDS", 600) };
        return (new OllamaClient(Resilient(http), model, temperature), model);
    }

    private static (ILlmClient Client, string? Model) CreateOpenAi(double? temperature)
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

        return (new OpenAiClient(Resilient(http), model, temperature), model);
    }

    private static (ILlmClient Client, string? Model) CreateOpenRouter(double? temperature)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            ?? throw new InvalidOperationException("OPENROUTER_API_KEY is not configured. Add it to the .env file.");
        var model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL")
            ?? throw new InvalidOperationException("OPENROUTER_MODEL is not configured. Add it to the .env file.");

        var http = new HttpClient
        {
            BaseAddress = new Uri("https://openrouter.ai"),
            // Several models behind OpenRouter reason before answering by default, and a long chain
            // outlasts two minutes. A timeout is retried from scratch while the aborted attempt is
            // still billed, so a tight limit here costs money, not just time.
            Timeout = TimeoutFrom("OPENROUTER_TIMEOUT_SECONDS", 600),
        };
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        return (new OpenRouterClient(Resilient(http), model, temperature), model);
    }

    // A per-request limit from the environment, falling back to the engine's own default.
    private static TimeSpan TimeoutFrom(string variable, int defaultSeconds) =>
        TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable(variable), out var seconds) && seconds > 0
                ? seconds
                : defaultSeconds);

    // Composes the resilient HTTP transport shared by every HTTP-based client.
    private static IHttpTransport Resilient(HttpClient http) =>
        new ResilientHttpTransport(new HttpTransport(http));
}
