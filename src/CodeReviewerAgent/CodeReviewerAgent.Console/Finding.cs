namespace CodeReviewerAgent.Console
{
    internal enum Severity
    {
        Info,
        Warning,
        Critical,
    }

    internal enum Category
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
    internal record Finding(
        string? File,
        int? Line,
        Severity? Severity,
        Category? Category,
        string? Problem,
        string? Suggestion);

    /// <summary>
    /// The structured result of a review: an overall summary plus the findings.
    /// </summary>
    internal record ReviewResult(
        string? Summary,
        List<Finding>? Findings);
}
