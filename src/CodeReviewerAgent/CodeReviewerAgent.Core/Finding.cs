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
        Convention,
    }

    /// <summary>
    /// A single code-review finding. The LLM cites the affected line verbatim in
    /// <see cref="CodeSnippet"/>; <see cref="Line"/> is not supplied by the model but
    /// derived by matching the snippet against the parsed diff.
    /// <para>
    /// <see cref="Id"/> and <see cref="AssessmentId"/> are persistence-only (a child row in the
    /// relational stores). They are <see cref="JsonIgnoreCondition.WhenWritingDefault"/> so the
    /// schema sent to the LLM and the plugin's <c>--json</c> output stay unchanged on new reviews.
    /// </para>
    /// </summary>
    public record Finding(
        string? File,
        [property: JsonPropertyName("code_snippet")] string? CodeSnippet,
        Severity? Severity,
        Category? Category,
        string? Problem,
        string? Suggestion,
        int? Line = null)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Id { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int AssessmentId { get; init; }
    }

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
