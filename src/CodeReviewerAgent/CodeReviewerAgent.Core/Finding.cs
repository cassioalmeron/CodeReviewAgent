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
    /// The structured result of a review: an overall summary plus the findings.
    /// </summary>
    public record ReviewResult(
        string? Summary,
        List<Finding>? Findings);
}
