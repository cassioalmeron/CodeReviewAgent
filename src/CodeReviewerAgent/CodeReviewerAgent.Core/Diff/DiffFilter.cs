using System.Text;

namespace CodeReviewerAgent.Core.Diff;

/// <summary>
/// Removes files the reviewer should ignore from a unified diff before it is reviewed,
/// so those files never reach the LLM or the finding validator. Markdown (<c>.md</c>)
/// files are prose, not code, so they are dropped.
/// </summary>
public static class DiffFilter
{
    public static string ExcludeMarkdown(string diff)
    {
        if (string.IsNullOrWhiteSpace(diff))
            return diff;

        var kept = new StringBuilder();
        foreach (var (path, text) in DiffSplitter.ByFile(diff))
            if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                kept.Append(text);
        return kept.ToString();
    }
}
