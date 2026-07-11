using System.Text;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Splits a unified diff into per-file raw blocks, keyed by the file path and preserving
    /// order. Shared by the report (which renders each file) and the filter (which drops
    /// ignored files).
    /// </summary>
    internal static class DiffSplitter
    {
        public static List<(string Path, string Text)> ByFile(string? diff)
        {
            var result = new List<(string Path, string Text)>();
            if (string.IsNullOrWhiteSpace(diff))
                return result;

            var lines = diff.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder? current = null;
            var path = "(unknown)";

            void Flush()
            {
                if (current is not null)
                    result.Add((path, current.ToString()));
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("diff --git"))
                {
                    Flush();
                    current = new StringBuilder();
                    path = "(unknown)";
                }

                if (current is null)
                    continue;

                current.AppendLine(line);

                // Prefer the new path; fall back to the old path for deletions.
                if (line.StartsWith("+++ "))
                    path = ParsePath(line[4..]) ?? path;
                else if (line.StartsWith("--- ") && path == "(unknown)")
                    path = ParsePath(line[4..]) ?? path;
            }

            Flush();
            return result;
        }

        // Parses a path from a `---`/`+++` header line: drops trailing metadata,
        // maps /dev/null to null, and strips the a//b/ prefix.
        private static string? ParsePath(string raw)
        {
            var path = raw.Trim();
            var tab = path.IndexOf('\t');
            if (tab >= 0)
                path = path[..tab];
            if (path == "/dev/null")
                return null;
            return path.StartsWith("a/") || path.StartsWith("b/") ? path[2..] : path;
        }
    }
}
