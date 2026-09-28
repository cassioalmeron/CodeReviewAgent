namespace CodeReviewerAgent.Core;

/// <summary>
/// The lines a finished review shows on a terminal, in the order it always showed them:
/// the activated skills, the summary, then the findings one per line.
/// <para>
/// <see cref="CodeReviewer"/> used to write these itself (US-018). The wording stays here, next
/// to the result it describes and reachable from a test, and the writing belongs to whoever owns
/// the terminal. It is the same split <see cref="Golden.GoldenEvaluatorReport.FormatLine"/>
/// already used for the golden set.
/// </para>
/// </summary>
public static class ReviewConsoleReport
{
    public static IReadOnlyList<string> Lines(ReviewResult review)
    {
        var lines = new List<string>();

        if (review.Skills is { } skills && !string.IsNullOrWhiteSpace(skills))
            lines.Add($"Skills: {string.Join(", ", skills.Split(','))}");

        if (!string.IsNullOrWhiteSpace(review.Summary))
            lines.Add(review.Summary);

        var findings = review.Findings ?? [];
        lines.Add("");
        lines.Add($"Findings: {findings.Count}");
        foreach (var f in findings)
            lines.Add($"  [{f.Severity}] {f.File}:{f.Line} ({f.Category}) — {f.Problem} -> {f.Suggestion}");

        return lines;
    }
}
