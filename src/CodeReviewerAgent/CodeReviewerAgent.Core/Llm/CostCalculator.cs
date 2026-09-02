namespace CodeReviewerAgent.Core.Llm;

/// <summary>
/// Estimates the API cost (USD) of a request. Local (Ollama) and subscription
/// (claude-code / claude-cli) engines are free; metered engines (Claude, OpenAI)
/// are priced per model based on input/output tokens. OpenRouter reports its real
/// cost on the response, so the pipeline uses that instead of this estimate.
/// </summary>
public static class CostCalculator
{
    // USD per 1M tokens: (input, output). Update when pricing changes.
    private static readonly Dictionary<string, (decimal Input, decimal Output)> ClaudePricing =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-fable-5"] = (10m, 50m),
            ["claude-opus-4-8"] = (5m, 25m),
            ["claude-opus-4-7"] = (5m, 25m),
            ["claude-opus-4-6"] = (5m, 25m),
            ["claude-opus-4-5"] = (5m, 25m),
            ["claude-sonnet-4-6"] = (3m, 15m),
            ["claude-sonnet-4-5"] = (3m, 15m),
            ["claude-haiku-4-5"] = (1m, 5m),
        };

    private static readonly Dictionary<string, (decimal Input, decimal Output)> OpenAiPricing =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4o-mini"] = (0.15m, 0.60m),
            ["gpt-4o"] = (2.50m, 10m),
            ["gpt-4.1-nano"] = (0.10m, 0.40m),
            ["gpt-4.1-mini"] = (0.40m, 1.60m),
            ["gpt-4.1"] = (2m, 8m),
        };

    public static decimal Estimate(string? engine, string? model, int inputTokens, int outputTokens)
    {
        if (model is null)
            return 0m;

        var pricing = engine?.ToLowerInvariant() switch
        {
            "claude" => ClaudePricing,
            "openai" => OpenAiPricing,
            // Ollama (local) and subscription engines have no metered cost. OpenRouter
            // reports its real per-call cost on the response (usage accounting), which the
            // pipeline prefers over this estimate, so it isn't priced here either.
            _ => null,
        };
        if (pricing is null)
            return 0m;

        // Longest matching key wins, so "gpt-4o" doesn't shadow "gpt-4o-mini".
        var match = pricing
            .Where(p => model.Contains(p.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Key.Length)
            .FirstOrDefault();
        if (match.Key is null)
            return 0m;

        return inputTokens / 1_000_000m * match.Value.Input
             + outputTokens / 1_000_000m * match.Value.Output;
    }
}
