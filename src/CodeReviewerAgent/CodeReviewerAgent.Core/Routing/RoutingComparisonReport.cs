using System.Globalization;
using System.Text;
using CodeReviewerAgent.Core.Golden;

namespace CodeReviewerAgent.Core.Routing;

public static class RoutingComparisonReport
{
    /// <summary>Writes the report to the reports directory and returns its path.</summary>
    public static string Save(RoutingComparison comparison)
    {
        var directory = OutputPaths.Reports;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"routing-comparison-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.md");
        File.WriteAllText(path, Build(comparison));
        return path;
    }

    public static string Build(RoutingComparison comparison)
    {
        var report = new StringBuilder();
        report.AppendLine("# Cost-based model routing");
        report.AppendLine();
        report.Append($"Rule **{RoutingRule.Version}**: cheap route when a diff changes at most {RoutingRule.MaxChangedLines} lines");
        report.AppendLine($" and touches at most {RoutingRule.MaxTouchedFiles} files, strong route otherwise.");
        report.AppendLine();
        report.AppendLine($"- Cheap model: `{comparison.CheapModel}`");
        report.AppendLine($"- Strong model: `{comparison.StrongModel}`");
        report.Append($"- Cases: {comparison.Cases.Count} ({comparison.CheapRouteCases} cheap route,");
        report.AppendLine($" {comparison.StrongRouteCases} strong route)");
        report.AppendLine();

        AppendColumns(report, comparison);
        AppendCases(report, comparison);
        AppendQuality(report, comparison);
        return report.ToString();
    }

    private static void AppendColumns(StringBuilder report, RoutingComparison comparison)
    {
        RoutingColumn[] columns = [comparison.Cheap, comparison.Routed, comparison.Strong];

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

        report.AppendLine(
            $"| Approved | {string.Join(" | ", columns.Select(c => c.Verdict.Approved ? "yes" : "no"))} |");
        report.AppendLine($"| Cost | {string.Join(" | ", columns.Select(c => Money(c.Cost)))} |");
        report.AppendLine();
        report.AppendLine(
            "The routed column is composed from the two recorded passes: per case, the result of the pass its route selects.");
        report.AppendLine();
    }

    private static void AppendCases(StringBuilder report, RoutingComparison comparison)
    {
        report.AppendLine("## Per case");
        report.AppendLine();
        report.AppendLine("| Case | Kind | Route | Lines | Files | Cheap | Strong |");
        report.AppendLine("|---|---|---|---:|---:|---|---|");
        foreach (var c in comparison.Cases)
        {
            report.Append($"| {c.Name} | {c.Kind} | {c.Route} | {c.Size.ChangedLines} | {c.Size.TouchedFiles}");
            report.Append($" | {c.Cheap.Successes}/{c.Cheap.Runs} ({c.Cheap.CleanRounds} clean) · {Money(c.CheapCost)}");
            report.AppendLine($" | {c.Strong.Successes}/{c.Strong.Runs} ({c.Strong.CleanRounds} clean) · {Money(c.StrongCost)} |");
        }
        report.AppendLine();
    }

    private static void AppendQuality(StringBuilder report, RoutingComparison comparison)
    {
        report.AppendLine("## Cheap-route cases: did the strong pass find more?");
        report.AppendLine();
        report.AppendLine(
            "Routing can only lose on these cases. The strong-route cases are not listed: the rule already chose the strong model there.");
        report.AppendLine();

        if (comparison.CheapRouteQuality.Count == 0)
        {
            report.AppendLine("No case took the cheap route.");
            return;
        }

        report.AppendLine("| Case | Kind | Cheap | Strong | Strong found more |");
        report.AppendLine("|---|---|---|---|---|");
        foreach (var q in comparison.CheapRouteQuality)
        {
            report.Append($"| {q.Name} | {q.Kind} | {q.CheapSuccesses}/{q.Runs} | {q.StrongSuccesses}/{q.Runs}");
            report.AppendLine($" | {(q.Kind == GoldenKind.Trap ? "n/a (trap)" : q.StrongFoundMore ? "yes" : "no")} |");
        }

        var more = comparison.CheapRouteQuality.Count(q => q.StrongFoundMore);
        report.AppendLine();
        report.AppendLine($"The strong pass found more on {more} of {comparison.CheapRouteQuality.Count} cheap-route cases.");
    }

    private static string Money(decimal cost) => $"US$ {cost.ToString("0.0000", CultureInfo.InvariantCulture)}";
}
