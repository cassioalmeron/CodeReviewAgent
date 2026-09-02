using CodeReviewerAgent.Core.Diff;

namespace CodeReviewerAgent.Core;

/// <summary>
/// Grounds LLM findings against the parsed diff: a finding is kept only when its
/// cited <see cref="Finding.CodeSnippet"/> matches an <b>added</b> line of the file
/// it points to. The matching line's number becomes the finding's <see cref="Finding.Line"/>,
/// so the line is derived from the diff rather than trusted from the model.
/// </summary>
public static class FindingValidator
{
    public static List<Finding> Validate(IReadOnlyList<Finding> findings, ParsedDiff diff)
    {
        var validated = new List<Finding>();
        foreach (var finding in findings)
        {
            var line = ResolveAddedLine(finding, diff);
            if (line is not null)
                validated.Add(finding with { Line = line });
        }
        return validated;
    }

    private static int? ResolveAddedLine(Finding finding, ParsedDiff diff)
    {
        if (finding.File is null || string.IsNullOrWhiteSpace(finding.CodeSnippet))
            return null;

        var snippet = Normalize(finding.CodeSnippet);
        if (snippet.Length == 0)
            return null;

        var file = diff.Files.FirstOrDefault(f => PathMatches(f.Path, finding.File));
        if (file is null)
            return null;

        foreach (var hunk in file.Hunks)
            foreach (var diffLine in hunk.Lines)
            {
                if (diffLine.Kind != DiffLineKind.Added)
                    continue;

                var text = Normalize(diffLine.Text);
                if (text.Length > 0 && (text == snippet || text.Contains(snippet)))
                    return diffLine.NewLineNumber;
            }

        return null;
    }

    private static bool PathMatches(string? filePath, string findingFile)
    {
        if (filePath is null)
            return false;

        var target = findingFile.Replace('\\', '/').Trim();
        return filePath == target
            || filePath.EndsWith('/' + target)
            || target.EndsWith('/' + filePath);
    }

    // Collapses runs of whitespace so cosmetic indentation differences don't block a match.
    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
