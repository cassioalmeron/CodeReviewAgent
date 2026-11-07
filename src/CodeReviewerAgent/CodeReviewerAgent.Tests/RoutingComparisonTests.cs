using System.Text;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Core.Routing;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The routed run is composed from two recorded passes, so every test here hands the composer
/// results written by hand and no model is called. The cheap diff is one file with one changed
/// line; the strong diff is one file with 60 changed lines, past the 50-line threshold.
/// </summary>
public class RoutingComparisonTests
{
    private const string CheapDiff = """
        diff --git a/src/A.cs b/src/A.cs
        --- a/src/A.cs
        +++ b/src/A.cs
        @@ -1,1 +1,1 @@
        -var a = 1;
        +var a = 2;
        """;

    private static readonly string StrongDiff = LargeDiff(60);

    private static string LargeDiff(int changedLines)
    {
        var diff = new StringBuilder();
        diff.AppendLine("diff --git a/src/B.cs b/src/B.cs");
        diff.AppendLine("--- a/src/B.cs");
        diff.AppendLine("+++ b/src/B.cs");
        diff.AppendLine($"@@ -0,0 +1,{changedLines} @@");
        for (var line = 0; line < changedLines; line++)
            diff.AppendLine($"+var b{line} = {line};");
        return diff.ToString();
    }

    private static GoldenCaseResult Detection(string name, int successes, int runs = 3) =>
        new(name, GoldenKind.Detection, null, "v3", successes, runs, null, successes, runs, [0], CleanRounds: successes);

    private static GoldenCaseResult Trap(string name, int successes, int runs = 3) =>
        new(name, GoldenKind.Trap, null, "v3", successes, runs, null, CleanRounds: successes);

    private static CompletedRound Round(string name, string diff, decimal cost) =>
        new(name, diff, new ReviewResult("summary", [], "fake", "fake-model", "v3", cost, 0, 0, 0, diff));

    private static RoutingPass Pass(
        string model, IReadOnlyList<GoldenCaseResult> results, params CompletedRound[] rounds) =>
        new(model, results, rounds);

    [Fact]
    public void RoutingComparisonTests_A_case_whose_routes_disagree_takes_the_result_of_the_pass_its_route_selects()
    {
        var cheap = Pass("cheap-model",
            [Detection("small-bug", 1), Detection("large-bug", 0)],
            Round("small-bug", CheapDiff, 0.01m), Round("large-bug", StrongDiff, 0.02m));
        var strong = Pass("strong-model",
            [Detection("small-bug", 3), Detection("large-bug", 3)],
            Round("small-bug", CheapDiff, 0.10m), Round("large-bug", StrongDiff, 0.20m));

        var comparison = RoutingComparison.Compose(cheap, strong);

        var small = comparison.Cases.Single(c => c.Name == "small-bug");
        var large = comparison.Cases.Single(c => c.Name == "large-bug");
        Assert.Equal(ModelRoute.Cheap, small.Route);
        Assert.Equal(ModelRoute.Strong, large.Route);
        Assert.Equal(1, small.Selected.Successes);
        Assert.Equal(3, large.Selected.Successes);
        Assert.Equal([1, 3], comparison.Routed.Results.Select(r => r.Successes));
        Assert.Equal(new DiffSize(60, 1), large.Size);
    }

    [Fact]
    public void RoutingComparisonTests_A_case_whose_routes_agree_has_the_same_result_in_all_three_columns()
    {
        var cheap = Pass("cheap-model", [Detection("steady", 2)], Round("steady", CheapDiff, 0.01m));
        var strong = Pass("strong-model", [Detection("steady", 2)], Round("steady", CheapDiff, 0.05m));

        var comparison = RoutingComparison.Compose(cheap, strong);

        Assert.Equal(2, comparison.Cheap.Results.Single().Successes);
        Assert.Equal(2, comparison.Routed.Results.Single().Successes);
        Assert.Equal(2, comparison.Strong.Results.Single().Successes);
        Assert.False(comparison.CheapRouteQuality.Single().StrongFoundMore);
    }

    [Fact]
    public void RoutingComparisonTests_A_cheap_route_case_where_the_strong_pass_found_more_is_flagged()
    {
        var cheap = Pass("cheap-model",
            [Detection("missed-by-cheap", 1), Detection("found-by-both", 3), Detection("large-bug", 0)],
            Round("missed-by-cheap", CheapDiff, 0m), Round("found-by-both", CheapDiff, 0m),
            Round("large-bug", StrongDiff, 0m));
        var strong = Pass("strong-model",
            [Detection("missed-by-cheap", 3), Detection("found-by-both", 3), Detection("large-bug", 3)],
            Round("missed-by-cheap", CheapDiff, 0m), Round("found-by-both", CheapDiff, 0m),
            Round("large-bug", StrongDiff, 0m));

        var comparison = RoutingComparison.Compose(cheap, strong);

        var flagged = comparison.CheapRouteQuality.Single(q => q.StrongFoundMore);
        Assert.Equal("missed-by-cheap", flagged.Name);
        Assert.Equal((1, 3), (flagged.CheapSuccesses, flagged.StrongSuccesses));
        // The strong-route case differs just as much, but the rule already chose the strong model there.
        Assert.Equal(["found-by-both", "missed-by-cheap"], comparison.CheapRouteQuality.Select(q => q.Name).Order());
    }

    [Fact]
    public void RoutingComparisonTests_On_a_trap_the_strong_pass_is_never_said_to_have_found_more()
    {
        var cheap = Pass("cheap-model", [Trap("quiet-trap", 0)], Round("quiet-trap", CheapDiff, 0m));
        var strong = Pass("strong-model", [Trap("quiet-trap", 3)], Round("quiet-trap", CheapDiff, 0m));

        var quality = RoutingComparison.Compose(cheap, strong).CheapRouteQuality.Single();

        Assert.False(quality.StrongFoundMore);
        Assert.Equal((0, 3), (quality.CheapSuccesses, quality.StrongSuccesses));
    }

    [Fact]
    public void RoutingComparisonTests_Cost_columns_add_up_the_rounds_of_the_pass_each_column_selects()
    {
        var cheap = Pass("cheap-model",
            [Detection("small", 2), Detection("large", 2)],
            Round("small", CheapDiff, 0.0010m), Round("small", CheapDiff, 0.0020m),
            Round("large", StrongDiff, 0.0100m), Round("large", StrongDiff, 0.0200m));
        var strong = Pass("strong-model",
            [Detection("small", 3), Detection("large", 3)],
            Round("small", CheapDiff, 0.0300m), Round("small", CheapDiff, 0.0400m),
            Round("large", StrongDiff, 0.5000m), Round("large", StrongDiff, 0.7000m));

        var comparison = RoutingComparison.Compose(cheap, strong);

        Assert.Equal(0.0010m + 0.0020m + 0.0100m + 0.0200m, comparison.Cheap.Cost);
        Assert.Equal(0.0300m + 0.0400m + 0.5000m + 0.7000m, comparison.Strong.Cost);
        // small takes its cheap rounds, large takes its strong rounds
        Assert.Equal(0.0010m + 0.0020m + 0.5000m + 0.7000m, comparison.Routed.Cost);
    }

    [Fact]
    public void RoutingComparisonTests_Cases_from_two_eval_sets_share_one_comparison()
    {
        var golden = Detection("golden-case", 3);
        var routing = Detection("large-case", 3);
        var cheap = Pass("cheap-model", [golden, routing],
            Round("golden-case", CheapDiff, 0m), Round("large-case", StrongDiff, 0m));
        var strong = Pass("strong-model", [golden, routing],
            Round("golden-case", CheapDiff, 0m), Round("large-case", StrongDiff, 0m));

        var comparison = RoutingComparison.Compose(cheap, strong);

        Assert.Equal((1, 1), (comparison.CheapRouteCases, comparison.StrongRouteCases));
    }

    [Fact]
    public void RoutingComparisonTests_Passes_over_different_cases_are_rejected_naming_the_case()
    {
        var cheap = Pass("cheap-model", [Detection("only-in-cheap", 3)], Round("only-in-cheap", CheapDiff, 0m));
        var strong = Pass("strong-model", [], []);

        var error = Assert.Throws<InvalidOperationException>(() => RoutingComparison.Compose(cheap, strong));

        Assert.Contains("only-in-cheap", error.Message);
    }

    [Fact]
    public void RoutingComparisonTests_Passes_with_a_different_number_of_rounds_are_rejected()
    {
        var cheap = Pass("cheap-model", [Detection("case", 3, runs: 3)], Round("case", CheapDiff, 0m));
        var strong = Pass("strong-model", [Detection("case", 5, runs: 5)], Round("case", CheapDiff, 0m));

        var error = Assert.Throws<InvalidOperationException>(() => RoutingComparison.Compose(cheap, strong));

        Assert.Contains("same number of rounds", error.Message);
    }

    [Fact]
    public void RoutingComparisonTests_The_report_states_the_rule_the_models_the_split_and_the_cost_of_each_column()
    {
        var cheap = Pass("cheap-model",
            [Detection("small", 1), Detection("large", 0)],
            Round("small", CheapDiff, 0.25m), Round("large", StrongDiff, 1.5m));
        var strong = Pass("strong-model",
            [Detection("small", 3), Detection("large", 3)],
            Round("small", CheapDiff, 2m), Round("large", StrongDiff, 4m));

        var report = RoutingComparisonReport.Build(RoutingComparison.Compose(cheap, strong));

        Assert.Contains($"Rule **{RoutingRule.Version}**", report);
        Assert.Contains($"at most {RoutingRule.MaxChangedLines} lines", report);
        Assert.Contains($"at most {RoutingRule.MaxTouchedFiles} files", report);
        Assert.Contains("- Cheap model: `cheap-model`", report);
        Assert.Contains("- Strong model: `strong-model`", report);
        Assert.Contains("(1 cheap route, 1 strong route)", report);
        Assert.Contains("| Cost | US$ 1.7500 | US$ 4.2500 | US$ 6.0000 |", report);
        Assert.Contains("| small | Detection | 1/3 | 3/3 | yes |", report);
    }

    [Fact]
    public void RoutingComparisonTests_A_saved_report_carries_a_routed_cost_equal_to_the_per_case_sum_of_the_selected_pass()
    {
        var cheap = Pass("cheap-model",
            [Detection("small", 3), Detection("large", 3)],
            Round("small", CheapDiff, 0.125m), Round("large", StrongDiff, 9m));
        var strong = Pass("strong-model",
            [Detection("small", 3), Detection("large", 3)],
            Round("small", CheapDiff, 3m), Round("large", StrongDiff, 0.375m));

        var path = RoutingComparisonReport.Save(RoutingComparison.Compose(cheap, strong));
        try
        {
            // small is cheap route (0.125 from the cheap pass), large is strong route (0.375 from the strong pass)
            Assert.Contains("| Cost | US$ 9.1250 | US$ 0.5000 | US$ 3.3750 |", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
