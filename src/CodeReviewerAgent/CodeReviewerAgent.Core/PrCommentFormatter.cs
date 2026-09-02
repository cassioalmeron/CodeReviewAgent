using System.Text;

namespace CodeReviewerAgent.Core;

/// <summary>
/// Formats a <see cref="ReviewResult"/> as a concise Markdown comment for a pull request
/// (summary + findings), tailored for the PR audience — no diff dump or dev metrics.
/// </summary>
public static class PrCommentFormatter
{
    // Hidden marker so the agent can later find and update its own comment.
    public const string Marker = "<!-- code-review-agent -->";

    public static string Format(ReviewResult review)
    {
        var findings = review.Findings ?? [];
        var sb = new StringBuilder();

        sb.AppendLine($"## Code Review — {findings.Count} finding{(findings.Count == 1 ? "" : "s")}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(review.Summary))
        {
            sb.AppendLine(review.Summary);
            sb.AppendLine();
        }

        if (findings.Count == 0)
        {
            sb.AppendLine("No issues found.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("| Severity | Category | Location | Problem |");
            sb.AppendLine("|----------|----------|----------|---------|");
            foreach (var f in findings)
                sb.AppendLine($"| {Circle(f.Severity)} {f.Severity} | {f.Category} | {Location(f)} | {Inline(f.Problem)} |");
            sb.AppendLine();

            sb.AppendLine("### Suggestions");
            sb.AppendLine();
            foreach (var f in findings)
                sb.AppendLine($"- **{Location(f)}** — {Inline(f.Suggestion)}");
            sb.AppendLine();
        }

        sb.AppendLine($"<sub>{review.Model} · prompt {review.PromptVersion}</sub>");
        sb.AppendLine();
        sb.Append(Marker);
        return sb.ToString();
    }

    private static string Circle(Severity? severity) => severity switch
    {
        Severity.Critical => "🔴",
        Severity.Warning => "🟡",
        Severity.Info => "🔵",
        _ => "",
    };

    private static string Location(Finding f) =>
        f.Line is { } line ? $"`{f.File}:{line}`" : $"`{f.File}`";

    // Collapse newlines and escape pipes so a value stays inside one Markdown table cell.
    private static string Inline(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? "—"
            : text.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|").Trim();
}
