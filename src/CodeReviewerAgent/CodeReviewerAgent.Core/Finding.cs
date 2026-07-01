using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core
{
    public enum Severity
    {
        Info,
        Warning,
        Critical,
    }

    public enum Category
    {
        Bug,
        Security,
        Performance,
        Style,
        Maintainability,
    }

    /// <summary>
    /// A single code-review finding. The LLM cites the affected line verbatim in
    /// <see cref="CodeSnippet"/>; <see cref="Line"/> is not supplied by the model but
    /// derived by matching the snippet against the parsed diff.
    /// </summary>
    public record Finding(
        string? File,
        [property: JsonPropertyName("code_snippet")] string? CodeSnippet,
        Severity? Severity,
        Category? Category,
        string? Problem,
        string? Suggestion,
        int? Line = null);

    /// <summary>
    /// The structured result of a review: an overall summary and the findings, plus
    /// the run metadata (engine, model, prompt version, cost, latency, token usage).
    /// The metadata fields default to empty so the record can be deserialized straight
    /// from the LLM's <c>{ summary, findings }</c> output and enriched afterwards.
    /// </summary>
    public record ReviewResult(
        string? Summary,
        List<Finding>? Findings,
        string? Engine = null,
        string? Model = null,
        string? PromptVersion = null,
        decimal Cost = 0m,
        long LatencyMs = 0,
        int InputTokens = 0,
        int OutputTokens = 0,
        string? Diff = null);
}
