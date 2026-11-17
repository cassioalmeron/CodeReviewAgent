using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Advisor;
using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests.Core.Advisor;

public class AdvisorReportTests : IDisposable
{
    private static readonly GoldenCase Detection = new(
        "detect", "d.diff", "truth", new ExpectFinding("App.cs", Category.Bug, ["null"]));

    private static readonly GoldenCase Trap = new(
        "trap", "t.diff", "truth", new ExpectNoFinding("Safe.cs", "var safe = Compute();"));

    private readonly string _directory = Directory.CreateTempSubdirectory("advisor-report-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void AdvisorReportTests_Merge_drops_a_final_finding_on_the_same_file_and_snippet_and_keeps_a_different_snippet()
    {
        var first = Review(Catch("App.cs", "line one"));
        var final = Review(Catch("App.cs", "line one"), Catch("App.cs", "line two"), Catch("Other.cs", "line one"));

        var merged = AdvisorComparison.MergeFindings(first, final);

        Assert.Equal(["line one", "line two", "line one"], merged.Select(f => f.CodeSnippet));
        Assert.Equal(["App.cs", "App.cs", "Other.cs"], merged.Select(f => f.File));
    }

    [Fact]
    public void AdvisorReportTests_Transitions_are_counted_apart_for_replace_and_merge_on_detection_and_trap_cases()
    {
        var detection = new AdvisorCaseRounds(
            Detection,
            Cheap: [Review(Other()), Review(Catch("App.cs", "x")), Review(Other())],
            Strong: [Review(), Review(), Review()],
            Advisor:
            [
                Escalated(Review(Catch("App.cs", "x")), hints: 1),
                Escalated(Review(Other()), hints: 0),
                Quiet(Review(Other())),
            ]);
        var trap = new AdvisorCaseRounds(
            Trap,
            Cheap: [Review(Catch("Safe.cs", "var safe = Compute();"))],
            Strong: [Review()],
            Advisor: [Escalated(Review(Other()), hints: 2)]);

        var comparison = AdvisorComparison.Compose([detection, trap], "v3");

        // Round 0 caught after a miss; round 1 lost its catch in replace but merge keeps the first review's.
        Assert.Equal(new Transitions(1, 1), comparison.ReplaceTransitions.Detection);
        Assert.Equal(new Transitions(1, 0), comparison.MergeTransitions.Detection);
        // The trap round stopped citing the bait in replace; merge still carries the first review's bait finding.
        Assert.Equal(new Transitions(1, 0), comparison.ReplaceTransitions.Trap);
        Assert.Equal(new Transitions(0, 0), comparison.MergeTransitions.Trap);
        Assert.Equal((3, 4), (comparison.Escalated, comparison.Rounds));
        Assert.Equal(1, comparison.ZeroHints);
    }

    [Fact]
    public void AdvisorReportTests_Costs_latencies_and_the_advisor_calls_are_summed_per_column()
    {
        var detection = new AdvisorCaseRounds(
            Detection,
            Cheap: [Review(cost: 0.01m, latency: 100), Review(cost: 0.02m, latency: 200)],
            Strong: [Review(cost: 0.10m, latency: 1000), Review(cost: 0.20m, latency: 2000)],
            Advisor:
            [
                Escalated(Review(cost: 0.31m, latency: 700), hints: 1, advisorCost: 0.25m),
                Quiet(Review(cost: 0.02m, latency: 200)),
            ]);

        var comparison = AdvisorComparison.Compose([detection], "v3");

        Assert.Equal(0.03m, comparison.Cheap.Cost);
        Assert.Equal(0.30m, comparison.Strong.Cost);
        Assert.Equal(0.33m, comparison.Replace.Cost);
        Assert.Equal(0.33m, comparison.Merge.Cost);
        Assert.Equal(0.25m, comparison.AdvisorCallsCost);
        Assert.Equal(900, comparison.Replace.LatencyMs);
        Assert.Equal(3000, comparison.Strong.LatencyMs);
        Assert.Equal((1, 2), (comparison.Escalated, comparison.Rounds));
    }

    [Fact]
    public void AdvisorReportTests_The_report_text_carries_the_alarm_version_the_cost_columns_and_the_zero_hint_count()
    {
        var detection = new AdvisorCaseRounds(
            Detection, [Review(Other())], [Review()], [Escalated(Review(Other(), cost: 0.5m), hints: 0, advisorCost: 0.4m)]);
        var comparison = AdvisorComparison.Compose([detection], "v3");

        var text = AdvisorComparisonReport.Build(
            comparison, [new AdvisorPassInfo("Cheap", "cheap", "single", ["a.jsonl"])], "off", "v3");

        Assert.Contains($"Alarm **{AdvisorTrigger.Version}**", text);
        Assert.Contains("1 of 1 rounds escalated; 1 of them got zero hints", text);
        Assert.Contains("The advisor calls alone cost US$ 0.4000", text);
        Assert.Contains("| Cost | US$ 0.0000 | US$ 0.0000 | US$ 0.5000 | US$ 0.5000 |", text);
        Assert.Contains("`a.jsonl`", text);
    }

    [Fact]
    public void AdvisorReportTests_Failed_advisor_calls_are_counted_apart_from_zero_hint_answers()
    {
        var detection = new AdvisorCaseRounds(
            Detection,
            Cheap: [Review(Other()), Review(Other()), Review(Other())],
            Strong: [Review(), Review(), Review()],
            Advisor:
            [
                Escalated(Review(Other()), hints: 0),
                Failed(Review(Other()), advisorCost: 0.4m),
                Escalated(Review(Other()), hints: 2),
            ]);
        var comparison = AdvisorComparison.Compose([detection], "v3");

        var text = AdvisorComparisonReport.Build(
            comparison, [new AdvisorPassInfo("Cheap", "cheap", "single", ["a.jsonl"])], "off", "v3");

        Assert.Equal((3, 1, 1), (comparison.Escalated, comparison.ZeroHints, comparison.AdvisorFailed));
        Assert.Equal(0.4m, comparison.AdvisorCallsCost);
        Assert.Contains("3 of 3 rounds escalated; 1 of them got zero hints from the advisor; 1 advisor calls failed", text);
    }

    [Fact]
    public void AdvisorReportTests_A_pass_with_a_different_number_of_rounds_names_the_case()
    {
        var detection = new AdvisorCaseRounds(Detection, [Review(), Review()], [Review(), Review()], [Quiet(Review())]);

        var exception = Assert.Throws<InvalidOperationException>(() => AdvisorComparison.Compose([detection], "v3"));

        Assert.Contains("'detect'", exception.Message);
    }

    [Fact]
    public void AdvisorReportTests_An_advisor_round_without_a_trace_is_refused_naming_the_case()
    {
        var detection = new AdvisorCaseRounds(Detection, [Review()], [Review()], [Review()]);

        var exception = Assert.Throws<InvalidOperationException>(() => AdvisorComparison.Compose([detection], "v3"));

        Assert.Contains("'detect'", exception.Message);
        Assert.Contains("no advisor trace", exception.Message);
    }

    [Fact]
    public void AdvisorReportTests_The_command_names_a_case_that_has_no_stored_round_in_a_pass()
    {
        var store = Path.Combine(_directory, "rounds.jsonl");
        new FileGoldenRoundStore(store, "cheap", "off").Record("not-a-case", "v3", 0, Review());
        var options = new AdvisorReportOptions("cheap", "strong", [store], "off", "v3", Path.Combine(_directory, "report.md"));
        var error = new StringWriter();

        var code = AdvisorReportCommand.Run(options, new StringWriter(), error);

        var firstCase = GoldenEvaluator.LoadCases()[0].Name;
        Assert.Equal(1, code);
        Assert.Contains($"No stored rounds in the cheap pass of 'cheap' for case '{firstCase}'", error.ToString());
        Assert.Contains("advisor pass", error.ToString());
        Assert.False(File.Exists(options.OutputFile));
    }

    private static Finding Catch(string file, string snippet) =>
        file == "Safe.cs"
            ? new Finding(file, snippet, Severity.Warning, Category.Bug, "Looks wrong.", "Fix.")
            : new Finding(file, snippet, Severity.Warning, Category.Bug, "The value may be null.", "Guard.");

    private static Finding Other() =>
        new("Elsewhere.cs", "something else", Severity.Info, Category.Convention, "Naming.", "Rename.");

    private static ReviewResult Review(params Finding[] findings) => new("s", [.. findings]);

    private static ReviewResult Review(decimal cost, long latency) => new("s", [], Cost: cost, LatencyMs: latency);

    private static ReviewResult Review(Finding finding, decimal cost) => new("s", [finding], Cost: cost);

    private static ReviewResult Escalated(ReviewResult review, int hints, decimal advisorCost = 0m) =>
        review with { Advisor = new AdvisorTrace(true, "strong", advisorCost, 1, 1, hints) };

    private static ReviewResult Failed(ReviewResult review, decimal advisorCost = 0m) =>
        review with { Advisor = new AdvisorTrace(true, "strong", advisorCost, 1, 1, 0, Failed: true) };

    private static ReviewResult Quiet(ReviewResult review) =>
        review with { Advisor = new AdvisorTrace(false, null, 0m, 0, 0, 0) };
}
