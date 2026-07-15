using CodeReviewerAgent.Core;
using Xunit;

namespace CodeReviewerAgent.Tests
{
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

        private static JudgeEvaluation Evaluation(int id, int overall) => new()
        {
            Id = id,
            AnalysisId = 7,
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
        public void GenerateForAnalysis_IncludesDiffAndEachEvaluation()
        {
            var diff = "diff --git a/App.cs b/App.cs\n--- a/App.cs\n+++ b/App.cs\n@@ -1 +1,2 @@\n line\n+added\n";

            var report = JudgeReportGenerator.GenerateForAnalysis(7, diff, [Evaluation(1, 4), Evaluation(2, 2)]);

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
        public void GenerateForAnalysis_SingleEvaluation_OmitsAverages()
        {
            var report = JudgeReportGenerator.GenerateForAnalysis(7, "diff --git a/A.cs b/A.cs", [Evaluation(1, 5)]);

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
    }
}
