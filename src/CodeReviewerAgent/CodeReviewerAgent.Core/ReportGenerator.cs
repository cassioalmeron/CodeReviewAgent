using System.Text;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Renders one or more <see cref="ReviewResult"/>s as a human-readable Markdown
    /// report. Each review contributes its own run metadata, summary and per-file
    /// findings/diff sections; when more than one review is given, a totals section
    /// at the end sums their cost and latency.
    /// </summary>
    public static class ReportGenerator
    {
        /// <summary>
        /// Builds the Markdown report for the given reviews and returns it as a string.
        /// </summary>
        public static string Generate(params ReviewResult[] reviews) => Generate(reviews, null);

        /// <summary>
        /// Builds the Markdown report for the given reviews and returns it as a string,
        /// appending a caller-formatted Markdown <paramref name="footer"/> after the totals.
        /// </summary>
        public static string Generate(
            IReadOnlyList<ReviewResult> reviews, string? footer, Func<ReviewResult, string?>? noteFor = null)
        {
            var report = new StringBuilder();

            report.AppendLine("# Code Review Report");
            report.AppendLine();
            report.AppendLine($"_Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC_");
            report.AppendLine();

            // Group rounds that ran over the same diff so their results sit together:
            // the diff is shown once and each round reports its own findings/metadata.
            var groups = reviews.GroupBy(r => r.Diff ?? "").ToList();
            var labelGroups = groups.Count > 1;

            var diffNumber = 0;
            foreach (var group in groups)
                AppendGroup(report, ++diffNumber, labelGroups, group.ToList(), noteFor);

            if (reviews.Count > 1)
                AppendTotals(report, reviews);

            if (!string.IsNullOrWhiteSpace(footer))
            {
                report.AppendLine("---");
                report.AppendLine(footer);
            }

            return report.ToString();
        }

        /// <summary>
        /// Generates the report and writes it to the reports directory, returning the file path.
        /// </summary>
        public static string Save(params ReviewResult[] reviews) => Save(reviews, null);

        /// <summary>
        /// Generates the report (with a footer) and writes it to the reports directory,
        /// returning the file path.
        /// </summary>
        public static string Save(
            IReadOnlyList<ReviewResult> reviews, string? footer, Func<ReviewResult, string?>? noteFor = null)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "reports");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"report-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.md");
            File.WriteAllText(path, Generate(reviews, footer, noteFor));
            return path;
        }

        // Renders one diff group: an optional group header, and either a single review
        // (with its diff inline) or, when the same diff was processed several times, the
        // shared diff once followed by each round's findings.
        private static void AppendGroup(
            StringBuilder report, int diffNumber, bool labelGroups, List<ReviewResult> rounds,
            Func<ReviewResult, string?>? noteFor)
        {
            // Separate consecutive diff groups with a horizontal rule.
            if (diffNumber > 1)
            {
                report.AppendLine("---");
                report.AppendLine();
            }

            if (labelGroups)
            {
                var files = DiffFileList(rounds[0].Diff);
                var suffix = string.IsNullOrEmpty(files) ? "" : $" — {files}";
                report.AppendLine($"# Diff {diffNumber}{suffix}");
                report.AppendLine();
            }

            // The diff and the engine/model/prompt are shared by every round over this
            // diff, so they are shown once, up front.
            AppendSharedDiff(report, rounds[0].Diff);
            AppendConfiguration(report, rounds[0]);

            if (rounds.Count == 1)
            {
                AppendReview(report, rounds[0], noteFor);
                return;
            }

            for (var i = 0; i < rounds.Count; i++)
            {
                report.AppendLine($"# Round {i + 1}");
                report.AppendLine();
                AppendReview(report, rounds[i], noteFor);
            }
        }

        // The engine/model/prompt that produced this diff's rounds — shown once per group.
        private static void AppendConfiguration(StringBuilder report, ReviewResult result)
        {
            report.AppendLine("## Configuration");
            report.AppendLine();
            report.AppendLine("| Setting | Value |");
            report.AppendLine("|---------|-------|");
            report.AppendLine($"| LLM Engine | {result.Engine ?? "—"} |");
            report.AppendLine($"| Model | {result.Model ?? "—"} |");
            report.AppendLine($"| Prompt version | {result.PromptVersion ?? "—"} |");
            report.AppendLine();
        }

        private static void AppendSharedDiff(StringBuilder report, string? diff)
        {
            var fileDiffs = DiffSplitter.ByFile(diff);
            if (fileDiffs.Count == 0)
                return;

            report.AppendLine("## Analyzed diff");
            report.AppendLine();
            foreach (var (path, text) in fileDiffs)
            {
                report.AppendLine($"### `{path}`");
                report.AppendLine();
                report.AppendLine("```diff");
                report.AppendLine(text.TrimEnd());
                report.AppendLine("```");
                report.AppendLine();
            }
        }

        private static string DiffFileList(string? diff) =>
            string.Join(", ", DiffSplitter.ByFile(diff).Select(d => d.Path));

        private static void AppendReview(
            StringBuilder report, ReviewResult result, Func<ReviewResult, string?>? noteFor = null)
        {
            var note = noteFor?.Invoke(result);
            if (!string.IsNullOrWhiteSpace(note))
            {
                report.AppendLine($"**Golden:** {note}");
                report.AppendLine();
            }

            var findings = result.Findings ?? [];
            var fileDiffs = DiffSplitter.ByFile(result.Diff);
            var findingsByFile = findings
                .GroupBy(f => f.File ?? "(unknown)")
                .ToDictionary(g => g.Key, g => g.ToList());

            // Files in diff order first, then any finding-only files not present in the diff.
            var fileOrder = fileDiffs.Select(d => d.Path)
                .Concat(findingsByFile.Keys)
                .Distinct()
                .ToList();

            AppendMetadata(report, result, fileOrder.Count, findings.Count);

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
                report.AppendLine();
                return;
            }

            foreach (var path in fileOrder)
            {
                var fileFindings = findingsByFile.GetValueOrDefault(path, []);
                AppendFile(report, path, fileFindings);
            }
        }

        private static void AppendTotals(StringBuilder report, IReadOnlyList<ReviewResult> reviews)
        {
            var totalCost = reviews.Sum(r => r.Cost);
            var totalLatency = reviews.Sum(r => r.LatencyMs);
            var totalFindings = reviews.Sum(r => r.Findings?.Count ?? 0);
            var totalInputTokens = reviews.Sum(r => r.InputTokens);
            var totalOutputTokens = reviews.Sum(r => r.OutputTokens);

            report.AppendLine("---");
            report.AppendLine("# Totals");
            report.AppendLine();
            report.AppendLine("| Setting | Value |");
            report.AppendLine("|---------|-------|");
            report.AppendLine($"| Total cost | {Utils.Money(totalCost)} |");
            report.AppendLine($"| Total latency | {totalLatency} ms |");
            report.AppendLine($"| Total findings | {totalFindings} |");
            report.AppendLine($"| Total input tokens | {totalInputTokens} |");
            report.AppendLine($"| Total output tokens | {totalOutputTokens} |");
            report.AppendLine($"| Total tokens | {totalInputTokens + totalOutputTokens} |");
            report.AppendLine();
        }

        private static void AppendMetadata(
            StringBuilder report, ReviewResult result, int fileCount, int findingsCount)
        {
            report.AppendLine("## Run details");
            report.AppendLine();
            report.AppendLine("| Setting | Value |");
            report.AppendLine("|---------|-------|");
            report.AppendLine($"| Cost | {Utils.Money(result.Cost)} |");
            report.AppendLine($"| Latency | {result.LatencyMs} ms |");
            report.AppendLine($"| Input tokens | {result.InputTokens} |");
            report.AppendLine($"| Output tokens | {result.OutputTokens} |");
            report.AppendLine($"| Total tokens | {result.InputTokens + result.OutputTokens} |");
            report.AppendLine($"| Files reviewed | {fileCount} |");
            report.AppendLine($"| Findings | {findingsCount} |");
            report.AppendLine();
        }

        private static void AppendFile(StringBuilder report, string path, List<Finding> findings)
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

    }
}
