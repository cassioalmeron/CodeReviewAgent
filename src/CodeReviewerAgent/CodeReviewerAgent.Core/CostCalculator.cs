namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Estimates the API cost (USD) of a request. Local engines (Ollama) are free;
    /// Claude is priced per model based on input/output tokens.
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

        public static decimal Estimate(string? engine, string? model, int inputTokens, int outputTokens)
        {
            // Local models (Ollama) have no API cost.
            if (!string.Equals(engine, "claude", StringComparison.OrdinalIgnoreCase) || model is null)
                return 0m;

            var match = ClaudePricing.FirstOrDefault(
                p => model.Contains(p.Key, StringComparison.OrdinalIgnoreCase));
            if (match.Key is null)
                return 0m;

            return inputTokens / 1_000_000m * match.Value.Input
                 + outputTokens / 1_000_000m * match.Value.Output;
        }
    }
}
