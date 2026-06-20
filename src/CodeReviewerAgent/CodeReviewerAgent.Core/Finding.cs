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
    /// A single code-review finding returned by the LLM.
    /// </summary>
    public record Finding(
        string? File,
        int? Line,
        Severity? Severity,
        Category? Category,
        string? Problem,
        string? Suggestion);

    /// <summary>
    /// The structured result of a review: an overall summary plus the findings.
    /// </summary>
    public record ReviewResult(
        string? Summary,
        List<Finding>? Findings);
}
