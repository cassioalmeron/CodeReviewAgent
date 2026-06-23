using System.Globalization;
using System.Text;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Run metadata shown in the report header: which engine/model produced the
    /// review, the prompt version used, the estimated cost, and the analyzed diff.
    /// </summary>
    public record ReportMetadata(
        string? Engine,
        string? Model,
        string? PromptVersion,
        decimal Cost,
        long LatencyMs,
        int InputTokens,
        int OutputTokens,
        string? Diff);

    /// <summary>
    /// Renders a <see cref="ReviewResult"/> as a human-readable Markdown report:
    /// run metadata, a summary, then one section per analyzed file with its findings
    /// and its slice of the diff.
    /// </summary>
    public static class ReportGenerator
    {
        /// <summary>
        /// Builds the Markdown report for a review and returns it as a string.
        /// </summary>
        public static string Generate(ReviewResult result, ReportMetadata metadata)
        {
            var findings = result.Findings ?? [];
            var fileDiffs = SplitDiffByFile(metadata.Diff);
            var findingsByFile = findings
                .GroupBy(f => f.File ?? "(unknown)")
                .ToDictionary(g => g.Key, g => g.ToList());

            // Files in diff order first, then any finding-only files not present in the diff.
            var fileOrder = fileDiffs.Select(d => d.Path)
                .Concat(findingsByFile.Keys)
                .Distinct()
                .ToList();

            var report = new StringBuilder();

            report.AppendLine("# Code Review Report");
            report.AppendLine();
            report.AppendLine($"_Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC_");
            report.AppendLine();

            AppendMetadata(report, metadata, fileOrder.Count, findings.Count);

            if (!string.IsNullOrWhiteSpace(result.Summary))
            {
                report.AppendLine("## Summary");
                report.AppendLine();
                report.AppendLine(result.Summary);
                report.AppendLine();
            }

            if (fileOrder.Count == 0)
            {
                report.AppendLine("No files reviewed.");
                return report.ToString();
            }

            foreach (var path in fileOrder)
            {
                var fileFindings = findingsByFile.GetValueOrDefault(path, []);
                var diffText = fileDiffs.FirstOrDefault(d => d.Path == path).Text;
                AppendFile(report, path, fileFindings, diffText);
            }

            return report.ToString();
        }

        /// <summary>
        /// Generates the report and writes it to the reports directory, returning the file path.
        /// </summary>
        public static string Save(ReviewResult result, ReportMetadata metadata)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "reports");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"report-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.md");
            File.WriteAllText(path, Generate(result, metadata));
            return path;
        }

        private static void AppendMetadata(
            StringBuilder report, ReportMetadata metadata, int fileCount, int findingsCount)
        {
            report.AppendLine("## Run details");
            report.AppendLine();
            report.AppendLine("| Setting | Value |");
            report.AppendLine("|---------|-------|");
            report.AppendLine($"| LLM Engine | {metadata.Engine ?? "—"} |");
            report.AppendLine($"| Model | {metadata.Model ?? "—"} |");
            report.AppendLine($"| Prompt version | {metadata.PromptVersion ?? "—"} |");
            report.AppendLine($"| Cost | {metadata.Cost.ToString("$0.######", CultureInfo.InvariantCulture)} USD |");
            report.AppendLine($"| Latency | {metadata.LatencyMs} ms |");
            report.AppendLine($"| Input tokens | {metadata.InputTokens} |");
            report.AppendLine($"| Output tokens | {metadata.OutputTokens} |");
            report.AppendLine($"| Total tokens | {metadata.InputTokens + metadata.OutputTokens} |");
            report.AppendLine($"| Files reviewed | {fileCount} |");
            report.AppendLine($"| Findings | {findingsCount} |");
            report.AppendLine();
        }

        private static void AppendFile(
            StringBuilder report, string path, List<Finding> findings, string? diffText)
        {
            report.AppendLine($"## `{path}`");
            report.AppendLine();

            report.AppendLine($"### Findings ({findings.Count})");
            report.AppendLine();
            if (findings.Count == 0)
            {
                report.AppendLine("No findings. 🎉");
                report.AppendLine();
            }
            else
            {
                AppendFindingsOverview(report, findings);
                AppendFindingsDetails(report, findings);
            }

            if (!string.IsNullOrWhiteSpace(diffText))
            {
                report.AppendLine("### Diff");
                report.AppendLine();
                report.AppendLine("```diff");
                report.AppendLine(diffText.TrimEnd());
                report.AppendLine("```");
                report.AppendLine();
            }
        }

        private static void AppendFindingsOverview(StringBuilder report, List<Finding> findings)
        {
            report.AppendLine("| # | Severity | Category | Line |");
            report.AppendLine("|---|----------|----------|------|");
            for (var i = 0; i < findings.Count; i++)
            {
                var f = findings[i];
                report.AppendLine($"| {i + 1} | {f.Severity} | {f.Category} | {f.Line?.ToString() ?? "—"} |");
            }
            report.AppendLine();
        }

        private static void AppendFindingsDetails(StringBuilder report, List<Finding> findings)
        {
            for (var i = 0; i < findings.Count; i++)
            {
                var f = findings[i];
                var line = f.Line is { } l ? $"line {l}" : "—";
                report.AppendLine($"#### {i + 1}. [{f.Severity}] {f.Category} — {line}");
                report.AppendLine();
                report.AppendLine($"**Problem:** {f.Problem}");
                report.AppendLine();
                report.AppendLine($"**Suggestion:** {f.Suggestion}");
                report.AppendLine();

                if (!string.IsNullOrWhiteSpace(f.CodeSnippet))
                {
                    report.AppendLine("```");
                    report.AppendLine(f.CodeSnippet);
                    report.AppendLine("```");
                    report.AppendLine();
                }
            }
        }

        // Splits a unified diff into per-file blocks, keyed by the file path, preserving order.
        private static List<(string Path, string Text)> SplitDiffByFile(string? diff)
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
