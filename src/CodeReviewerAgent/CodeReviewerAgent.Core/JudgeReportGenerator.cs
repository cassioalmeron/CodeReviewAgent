using System.Text;

namespace CodeReviewerAgent.Core
{
    /// <summary>A diff's judge outcomes, with a readable label (typically its first file).</summary>
    public record JudgeReportGroup(string Label, IReadOnlyList<JudgeOutcome> Outcomes);

    /// <summary>
    /// Renders judge outcomes as a Markdown report: per-diff average scores and rationales,
    /// then overall averages and totals. Mirrors <see cref="ReportGenerator"/> so report
    /// formatting lives outside the <see cref="JudgeRunner"/> orchestration.
    /// </summary>
    public static class JudgeReportGenerator
    {
        /// <summary>Builds the judge report and returns it as a string.</summary>
        public static string Generate(
            IReadOnlyList<JudgeReportGroup> groups, string judgeModel, string rubricVersion)
        {
            var report = new StringBuilder();
            report.AppendLine("# Judge Report");
            report.AppendLine();
            report.AppendLine(
                $"_Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC — judge {judgeModel}, rubric {rubricVersion}_");
            report.AppendLine();

            foreach (var group in groups)
            {
                var judgments = group.Outcomes.Select(o => o.Judgment).ToList();
                AppendScores(report, $"## {group.Label}", judgments);
                report.AppendLine(
                    $"_Cost: {Utils.Money(group.Outcomes.Sum(o => o.Cost))} · Latency: {group.Outcomes.Sum(o => o.LatencyMs)} ms_");
                report.AppendLine();
                AppendRationales(report, judgments);
            }

            var allOutcomes = groups.SelectMany(g => g.Outcomes).ToList();
            var allJudgments = allOutcomes.Select(o => o.Judgment).ToList();
            report.AppendLine("---");
            AppendScores(report, "# Overall", allJudgments);
            AppendTotals(report, allOutcomes);

            return report.ToString();
        }

        /// <summary>
        /// Generates the report and writes it to the reports directory, returning the file path.
        /// </summary>
        public static string Save(
            IReadOnlyList<JudgeReportGroup> groups, string judgeModel, string rubricVersion)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "reports");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"judge-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.md");
            File.WriteAllText(path, Generate(groups, judgeModel, rubricVersion));
            return path;
        }

        private static void AppendScores(StringBuilder report, string heading, IReadOnlyList<Judgment> judgments)
        {
            report.AppendLine(heading);
            report.AppendLine();
            report.AppendLine("| Criterion | Avg |");
            report.AppendLine("|-----------|-----|");
            report.AppendLine($"| Correctness | {Utils.FormatScore(Avg(judgments, j => j.Correctness))} |");
            report.AppendLine($"| Actionability | {Utils.FormatScore(Avg(judgments, j => j.Actionability))} |");
            report.AppendLine($"| Calibration | {Utils.FormatScore(Avg(judgments, j => j.Calibration))} |");
            report.AppendLine($"| Signal-to-noise | {Utils.FormatScore(Avg(judgments, j => j.SignalToNoise))} |");
            report.AppendLine($"| **Overall** | **{Utils.FormatScore(Avg(judgments, j => j.Overall))}** |");
            report.AppendLine();
        }

        private static void AppendRationales(StringBuilder report, IReadOnlyList<Judgment> judgments)
        {
            for (var i = 0; i < judgments.Count; i++)
                if (!string.IsNullOrWhiteSpace(judgments[i].Rationale))
                    report.AppendLine($"- Round {i + 1}: {judgments[i].Rationale}");
            report.AppendLine();
        }

        private static void AppendTotals(StringBuilder report, IReadOnlyList<JudgeOutcome> outcomes)
        {
            report.AppendLine();
            report.AppendLine("## Totals");
            report.AppendLine();
            report.AppendLine("| Setting | Value |");
            report.AppendLine("|---------|-------|");
            report.AppendLine($"| Total cost | {Utils.Money(outcomes.Sum(o => o.Cost))} |");
            report.AppendLine($"| Total latency | {outcomes.Sum(o => o.LatencyMs)} ms |");
            report.AppendLine($"| Total input tokens | {outcomes.Sum(o => o.InputTokens)} |");
            report.AppendLine($"| Total output tokens | {outcomes.Sum(o => o.OutputTokens)} |");
            report.AppendLine();
        }

        private static double Avg(IReadOnlyList<Judgment> judgments, Func<Judgment, int> selector) =>
            judgments.Count == 0 ? 0 : judgments.Average(selector);
    }
}
