using System.Text;
using CodeReviewerAgent.Core.Golden;

namespace CodeReviewerAgent.Core.Advisor;

/// <summary>One recorded pass the report read: what it is and which store files held it.</summary>
public sealed record AdvisorPassInfo(string Label, string Model, string Pattern, IReadOnlyList<string> Stores);

public static class AdvisorComparisonReport
{
    public static string Build(
        AdvisorComparison comparison, IReadOnlyList<AdvisorPassInfo> passes, string skills, string promptVersion)
    {
        var report = new StringBuilder();
        report.AppendLine("# Advisor strategy");
        report.AppendLine();
        report.AppendLine($"Alarm **{AdvisorTrigger.Version}**: the cheap review is escalated when it has no Bug and no Security finding.");
        report.AppendLine();
        AppendPasses(report, passes, skills, promptVersion);
        AppendColumns(report, comparison);
        AppendEscalation(report, comparison);
        AppendCases(report, comparison);
        return report.ToString();
    }

    private static void AppendPasses(
        StringBuilder report, IReadOnlyList<AdvisorPassInfo> passes, string skills, string promptVersion)
    {
        report.AppendLine("## Passes");
        report.AppendLine();
        report.AppendLine("| Pass | Model | Pattern | Skills | Prompt | Stores |");
        report.AppendLine("|---|---|---|---|---|---|");
        foreach (var pass in passes)
            report.AppendLine($"| {pass.Label} | `{pass.Model}` | {pass.Pattern} | {skills} | {promptVersion} | {string.Join("<br>", pass.Stores.Select(s => $"`{s}`"))} |");
        report.AppendLine();
    }

    private static void AppendColumns(StringBuilder report, AdvisorComparison comparison)
    {
        AdvisorColumn[] columns = [comparison.Cheap, comparison.Strong, comparison.Replace, comparison.Merge];

        report.AppendLine("## Gates and cost");
        report.AppendLine();
        report.AppendLine($"| | {string.Join(" | ", columns.Select(c => c.Label))} |");
        report.AppendLine($"|---|{string.Join("|", columns.Select(_ => "---"))}|");
        for (var gate = 0; gate < comparison.Cheap.Verdict.Gates.Count; gate++)
        {
            var first = comparison.Cheap.Verdict.Gates[gate];
            var cells = columns.Select(c =>
            {
                var g = c.Verdict.Gates[gate];
                return $"{g.ValueText} {(g.Passed ? "pass" : "**fail**")}";
            });
            report.AppendLine($"| {first.Name} (floor {first.FloorText}) | {string.Join(" | ", cells)} |");
        }

        report.AppendLine($"| Approved | {string.Join(" | ", columns.Select(c => c.Verdict.Approved ? "yes" : "no"))} |");
        report.AppendLine($"| Cost | {string.Join(" | ", columns.Select(c => AdvisorArguments.Money(c.Cost)))} |");
        report.AppendLine($"| Latency | {string.Join(" | ", columns.Select(c => Minutes(c.LatencyMs)))} |");
        report.AppendLine();
        report.AppendLine($"The advisor calls alone cost {AdvisorArguments.Money(comparison.AdvisorCallsCost)}, which is inside the two advisor columns.");
        report.AppendLine("Replace is the second review's findings only; merge adds the first review's findings the second did not repeat. Both cost the same calls.");
        report.AppendLine();
    }

    private static void AppendEscalation(StringBuilder report, AdvisorComparison comparison)
    {
        report.AppendLine("## Escalated rounds");
        report.AppendLine();
        report.AppendLine($"{comparison.Escalated} of {comparison.Rounds} rounds escalated; {comparison.ZeroHints} of them got zero hints from the advisor; {comparison.AdvisorFailed} advisor calls failed (empty or unreadable reply), which is not a zero-hint answer.");
        report.AppendLine();
        report.AppendLine("| | Replace | Merge |");
        report.AppendLine("|---|---:|---:|");
        report.AppendLine($"| Detection: miss became catch | {comparison.ReplaceTransitions.Detection.Gained} | {comparison.MergeTransitions.Detection.Gained} |");
        report.AppendLine($"| Detection: catch became miss | {comparison.ReplaceTransitions.Detection.Lost} | {comparison.MergeTransitions.Detection.Lost} |");
        report.AppendLine($"| Trap: fell for the bait, now resists | {comparison.ReplaceTransitions.Trap.Gained} | {comparison.MergeTransitions.Trap.Gained} |");
        report.AppendLine($"| Trap: resisted, now falls for the bait | {comparison.ReplaceTransitions.Trap.Lost} | {comparison.MergeTransitions.Trap.Lost} |");
        report.AppendLine();
    }

    private static void AppendCases(StringBuilder report, AdvisorComparison comparison)
    {
        report.AppendLine("## Per case");
        report.AppendLine();
        report.AppendLine("| Case | Kind | Escalated | Cheap | Strong | Replace | Merge |");
        report.AppendLine("|---|---|---:|---|---|---|---|");
        foreach (var c in comparison.Cases)
            report.AppendLine($"| {c.Name} | {c.Kind} | {c.Escalated} | {Cell(c.Cheap)} | {Cell(c.Strong)} | {Cell(c.Replace)} | {Cell(c.Merge)} |");
    }

    private static string Cell(AdvisorCaseColumn column) =>
        $"{column.Result.Successes}/{column.Result.Runs} ({column.Result.CleanRounds} clean) · {AdvisorArguments.Money(column.Cost)}";

    private static string Minutes(long milliseconds) =>
        $"{TimeSpan.FromMilliseconds(milliseconds).TotalMinutes:0.0} min";
}
