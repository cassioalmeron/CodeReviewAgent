using System.Text.RegularExpressions;

namespace CodeReviewerAgent.Core.Diff;

/// <summary>
/// Parses a unified diff (as produced by <c>git diff</c> / <c>gh pr diff</c>) into
/// a <see cref="ParsedDiff"/>, resolving the absolute line number of every line.
/// </summary>
public static partial class DiffParser
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@(.*)$")]
    private static partial Regex HunkHeaderRegex();

    public static ParsedDiff Parse(string diff)
    {
        var files = new List<DiffFile>();
        if (string.IsNullOrWhiteSpace(diff))
            return new ParsedDiff(files);

        var rawLines = diff.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        string? oldPath = null, newPath = null;
        var changeType = DiffChangeType.Modified;
        List<Hunk>? hunks = null;

        int oldStart = 0, oldCount = 0, newStart = 0, newCount = 0;
        var headerContext = string.Empty;
        List<DiffLine>? hunkLines = null;
        int oldLine = 0, newLine = 0;

        void EndHunk()
        {
            if (hunkLines is null)
                return;
            hunks!.Add(new Hunk(oldStart, oldCount, newStart, newCount, headerContext, hunkLines));
            hunkLines = null;
        }

        void EndFile()
        {
            EndHunk();
            if (hunks is null)
                return;
            files.Add(new DiffFile(oldPath, newPath, changeType, hunks));
            hunks = null;
            oldPath = newPath = null;
            changeType = DiffChangeType.Modified;
        }

        foreach (var line in rawLines)
        {
            // Empty entries are only the trailing newline / separators: real diff
            // body lines always carry a prefix char (' ', '+', '-', '\').
            if (line.Length == 0)
                continue;

            if (line.StartsWith("diff --git"))
            {
                EndFile();
                hunks = [];
                continue;
            }

            // Lines before the first file header (shouldn't happen for git output).
            if (hunks is null)
                continue;

            var match = HunkHeaderRegex().Match(line);
            if (match.Success)
            {
                EndHunk();
                oldStart = int.Parse(match.Groups[1].Value);
                oldCount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
                newStart = int.Parse(match.Groups[3].Value);
                newCount = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1;
                headerContext = match.Groups[5].Value.Trim();
                hunkLines = [];
                oldLine = oldStart;
                newLine = newStart;
                continue;
            }

            // Inside a hunk: classify the body line and assign its line numbers.
            if (hunkLines is not null)
            {
                if (line[0] == '\\') // "\ No newline at end of file" — not a real line.
                    continue;

                switch (line[0])
                {
                    case '+':
                        hunkLines.Add(new DiffLine(DiffLineKind.Added, null, newLine, line[1..]));
                        newLine++;
                        break;
                    case '-':
                        hunkLines.Add(new DiffLine(DiffLineKind.Removed, oldLine, null, line[1..]));
                        oldLine++;
                        break;
                    default: // ' ' context line
                        hunkLines.Add(new DiffLine(DiffLineKind.Context, oldLine, newLine, line[1..]));
                        oldLine++;
                        newLine++;
                        break;
                }
                continue;
            }

            // File header region (before the first hunk).
            if (line.StartsWith("new file mode"))
                changeType = DiffChangeType.Added;
            else if (line.StartsWith("deleted file mode"))
                changeType = DiffChangeType.Deleted;
            else if (line.StartsWith("rename from "))
            {
                changeType = DiffChangeType.Renamed;
                oldPath = StripPrefix(line["rename from ".Length..]);
            }
            else if (line.StartsWith("rename to "))
            {
                changeType = DiffChangeType.Renamed;
                newPath = StripPrefix(line["rename to ".Length..]);
            }
            else if (line.StartsWith("--- "))
                oldPath = ParseHeaderPath(line[4..]);
            else if (line.StartsWith("+++ "))
                newPath = ParseHeaderPath(line[4..]);
        }

        EndFile();
        return new ParsedDiff(files);
    }

    // Parses a path from a `---`/`+++` header line: drops trailing metadata,
    // maps /dev/null to null, and strips the a//b/ prefix.
    private static string? ParseHeaderPath(string raw)
    {
        var path = raw.Trim();
        var tab = path.IndexOf('\t');
        if (tab >= 0)
            path = path[..tab];
        return path == "/dev/null" ? null : StripPrefix(path);
    }

    private static string StripPrefix(string path) =>
        path.StartsWith("a/") || path.StartsWith("b/") ? path[2..] : path;
}
