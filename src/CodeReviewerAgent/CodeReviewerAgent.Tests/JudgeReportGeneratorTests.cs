using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Judge;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class JudgeReportGeneratorTests
{
    private static JudgeOutcome Outcome(int overall, decimal cost = 0.001m, long latencyMs = 100) =>
        new(new Judgment(overall, overall, overall, overall, overall, $"rationale {overall}"),
            cost, latencyMs, InputTokens: 300, OutputTokens: 120);

    [Fact]
    public void Generate_RendersHeaderWithJudgeAndRubric()
    {
        var groups = new List<JudgeReportGroup>
        {
            new("App.cs", null, [Outcome(4)]),
        };

        var report = JudgeReportGenerator.Generate(groups, "claude-sonnet-4-6", "v1");

        Assert.Contains("# Judge Report", report);
        Assert.Contains("judge claude-sonnet-4-6, rubric v1", report);
    }

    private static Evaluation Evaluation(int id, int overall) => new()
    {
        Id = id,
        AssessmentId = 7,
        RubricVersion = "v1",
        JudgeModel = "claude-sonnet-4-6",
        Correctness = overall,
        Actionability = overall,
        Calibration = overall,
        SignalToNoise = overall,
        Overall = overall,
        Rationale = $"rationale {id}",
        Cost = 0.001m,
        LatencyMs = 100,
        InputTokens = 300,
        OutputTokens = 120,
    };

    [Fact]
    public void GenerateForAssessment_IncludesDiffAndEachEvaluation()
    {
        var diff = "diff --git a/App.cs b/App.cs\n--- a/App.cs\n+++ b/App.cs\n@@ -1 +1,2 @@\n line\n+added\n";

        var report = JudgeReportGenerator.GenerateForAssessment(7, diff, [Evaluation(1, 4), Evaluation(2, 2)]);

        Assert.Contains("## Reviewed diff", report);
        Assert.Contains("App.cs", report);
        Assert.Contains("+added", report);
        Assert.Contains("## Evaluation 1", report);
        Assert.Contains("## Evaluation 2", report);
        Assert.Contains("**Rationale:** rationale 1", report);
        // Two evaluations → averages section with the mean overall (4 and 2 → 3.0).
        Assert.Contains("## Averages", report);
        Assert.Contains("**3.0**", report);
    }

    [Fact]
    public void GenerateForAssessment_SingleEvaluation_OmitsAverages()
    {
        var report = JudgeReportGenerator.GenerateForAssessment(7, "diff --git a/A.cs b/A.cs", [Evaluation(1, 5)]);

        Assert.Contains("## Evaluation 1", report);
        Assert.DoesNotContain("## Averages", report);
    }

    [Fact]
    public void Generate_RendersPerGroupScoresRationalesAndOverall()
    {
        var groups = new List<JudgeReportGroup>
        {
            new("App.cs", "diff --git a/App.cs b/App.cs\n+++ b/App.cs\n@@ -1 +1,2 @@\n line\n+added\n",
                [Outcome(4), Outcome(2)]),
        };

        var report = JudgeReportGenerator.Generate(groups, "claude-sonnet-4-6", "v1");

        // Per-group section with the label and averaged score (4 and 2 → 3.0).
        Assert.Contains("## App.cs", report);
        // The reviewed diff is now included in the consolidated report.
        Assert.Contains("## Reviewed diff", report);
        Assert.Contains("+added", report);
        Assert.Contains("| **Overall** | **3.0** |", report);
        // Rationales numbered per round.
        Assert.Contains("- Round 1: rationale 4", report);
        Assert.Contains("- Round 2: rationale 2", report);
        // Overall section across all groups.
        Assert.Contains("# Overall", report);
    }

    [Fact]
    public void Generate_RendersTotalsSummingCostLatencyAndTokens()
    {
        var groups = new List<JudgeReportGroup>
        {
            new("App.cs", null, [Outcome(4, cost: 0.01m, latencyMs: 1000)]),
            new("Other.cs", null, [Outcome(3, cost: 0.002m, latencyMs: 250)]),
        };

        var report = JudgeReportGenerator.Generate(groups, "claude-sonnet-4-6", "v1");

        Assert.Contains("## Totals", report);
        Assert.Contains("| Total cost | $0.012 USD |", report);
        Assert.Contains("| Total latency | 1250 ms |", report);
        Assert.Contains("| Total input tokens | 600 |", report);
        Assert.Contains("| Total output tokens | 240 |", report);
    }

    private static JudgeOutcome Scored(int correctness, int actionability, int calibration, int signalToNoise) =>
        new(new Judgment(correctness, actionability, calibration, signalToNoise, 4, null),
            0.001m, 100, InputTokens: 300, OutputTokens: 120);

    [Fact]
    public void JudgeReportGeneratorTests_The_header_names_the_pattern_it_was_produced_under()
    {
        var groups = new List<JudgeReportGroup> { new("App.cs", null, [Outcome(4)]) };

        var report = JudgeReportGenerator.Generate(groups, "claude-sonnet-4-6", "v1", "sectioned");

        Assert.Contains("judge claude-sonnet-4-6, rubric v1, pattern sectioned", report);
    }

    [Fact]
    public void JudgeReportGeneratorTests_The_overall_block_prints_saturation_and_collapse_per_judgment()
    {
        // Four judgments of 16 criterion scores: 9 are 5s. Two judgments have four identical criteria.
        var groups = new List<JudgeReportGroup>
        {
            new("A.cs", null, [Scored(5, 5, 5, 5), Scored(5, 5, 5, 5), Scored(5, 4, 3, 2)]),
            new("B.cs", null, [Scored(4, 4, 4, 4)]),
        };

        var report = JudgeReportGenerator.Generate(groups, "claude-sonnet-4-6", "v1", "single");

        Assert.Contains("| Saturation (criterion scores equal to 5) | 56% (9 of 16) |", report);
        Assert.Contains("| Collapse (reviews with four identical criteria) | 75% (3 of 4) |", report);
    }

    [Fact]
    public void JudgeReportGeneratorTests_A_stored_evaluation_names_its_pattern()
    {
        var evaluation = Evaluation(1, 4) with { Pattern = JudgePattern.Sectioned };

        var report = JudgeReportGenerator.GenerateForAssessment(7, null, [evaluation]);

        Assert.Contains("rubric v1, pattern sectioned", report);
    }
}
