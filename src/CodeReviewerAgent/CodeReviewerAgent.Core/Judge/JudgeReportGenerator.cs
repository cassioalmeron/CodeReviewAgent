using System.Text;
using CodeReviewerAgent.Core.Diff;

namespace CodeReviewerAgent.Core.Judge;

/// <summary>A diff's judge outcomes, with a readable label (typically its first file) and the reviewed diff.</summary>
public record JudgeReportGroup(string Label, string? Diff, IReadOnlyList<JudgeOutcome> Outcomes);

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
            report.AppendLine($"## {group.Label}");
            report.AppendLine();
            AppendReviewedDiff(report, group.Diff);
            AppendScores(report, judgments);
            report.AppendLine(
                $"_Cost: {Utils.Money(group.Outcomes.Sum(o => o.Cost))} · Latency: {group.Outcomes.Sum(o => o.LatencyMs)} ms_");
            report.AppendLine();
            AppendRationales(report, judgments);
        }

        var allOutcomes = groups.SelectMany(g => g.Outcomes).ToList();
        var allJudgments = allOutcomes.Select(o => o.Judgment).ToList();
        report.AppendLine("---");
        report.AppendLine("# Overall");
        report.AppendLine();
        AppendScores(report, allJudgments);
        AppendTotals(report, allOutcomes);

        return report.ToString();
    }

    /// <summary>
    /// Generates the report and writes it to the reports directory, returning the file path.
    /// </summary>
    public static string Save(
        IReadOnlyList<JudgeReportGroup> groups, string judgeModel, string rubricVersion)
    {
        var directory = OutputPaths.Reports;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"judge-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.md");
        File.WriteAllText(path, Generate(groups, judgeModel, rubricVersion));
        return path;
    }

    private static void AppendScores(StringBuilder report, IReadOnlyList<Judgment> judgments)
    {
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

    // --- Store-backed report: the persisted evaluations of one assessment, with the reviewed diff ---

    /// <summary>
    /// Renders the judge report for one assessment's stored <see cref="Evaluation"/>s (one or
    /// many), including the reviewed diff. Averages/totals are appended when there is more than one.
    /// </summary>
    public static string GenerateForAssessment(
        int assessmentId, string? diff, IReadOnlyList<Evaluation> evaluations)
    {
        var report = new StringBuilder();
        report.AppendLine("# Judge Report");
        report.AppendLine();
        report.AppendLine(
            $"_Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC — assessment {assessmentId}, {evaluations.Count} evaluation(s)_");
        report.AppendLine();

        AppendReviewedDiff(report, diff);
        foreach (var evaluation in evaluations)
            AppendEvaluation(report, evaluation);
        if (evaluations.Count > 1)
            AppendAverages(report, evaluations);

        return report.ToString();
    }

    /// <summary>
    /// Generates the analysis judge report and writes it to the reports directory,
    /// returning the file path.
    /// </summary>
    public static string SaveForAssessment(
        int assessmentId, string? diff, IReadOnlyList<Evaluation> evaluations)
    {
        var directory = OutputPaths.Reports;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"judge-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.md");
        File.WriteAllText(path, GenerateForAssessment(assessmentId, diff, evaluations));
        return path;
    }

    private static void AppendReviewedDiff(StringBuilder report, string? diff)
    {
        var fileDiffs = DiffSplitter.ByFile(diff);
        if (fileDiffs.Count == 0)
            return;

        report.AppendLine("## Reviewed diff");
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

    private static void AppendEvaluation(StringBuilder report, Evaluation e)
    {
        report.AppendLine($"## Evaluation {e.Id}");
        report.AppendLine();
        report.AppendLine(
            $"_Judge {e.JudgeModel ?? "—"}, rubric {e.RubricVersion ?? "—"} · {Utils.Money(e.Cost)} · {e.LatencyMs} ms_");
        report.AppendLine();
        report.AppendLine("| Criterion | Score |");
        report.AppendLine("|-----------|-------|");
        report.AppendLine($"| Correctness | {e.Correctness} |");
        report.AppendLine($"| Actionability | {e.Actionability} |");
        report.AppendLine($"| Calibration | {e.Calibration} |");
        report.AppendLine($"| Signal-to-noise | {e.SignalToNoise} |");
        report.AppendLine($"| **Overall** | **{e.Overall}** |");
        report.AppendLine();
        if (!string.IsNullOrWhiteSpace(e.Rationale))
        {
            report.AppendLine($"**Rationale:** {e.Rationale}");
            report.AppendLine();
        }
    }

    private static void AppendAverages(StringBuilder report, IReadOnlyList<Evaluation> evaluations)
    {
        report.AppendLine("---");
        report.AppendLine("## Averages");
        report.AppendLine();
        report.AppendLine("| Criterion | Avg |");
        report.AppendLine("|-----------|-----|");
        report.AppendLine($"| Correctness | {Utils.FormatScore(evaluations.Average(e => e.Correctness))} |");
        report.AppendLine($"| Actionability | {Utils.FormatScore(evaluations.Average(e => e.Actionability))} |");
        report.AppendLine($"| Calibration | {Utils.FormatScore(evaluations.Average(e => e.Calibration))} |");
        report.AppendLine($"| Signal-to-noise | {Utils.FormatScore(evaluations.Average(e => e.SignalToNoise))} |");
        report.AppendLine($"| **Overall** | **{Utils.FormatScore(evaluations.Average(e => e.Overall))}** |");
        report.AppendLine();
        report.AppendLine(
            $"_Total: {Utils.Money(evaluations.Sum(e => e.Cost))} · {evaluations.Sum(e => e.LatencyMs)} ms · " +
            $"{evaluations.Sum(e => e.InputTokens + e.OutputTokens)} tokens_");
        report.AppendLine();
    }
}
