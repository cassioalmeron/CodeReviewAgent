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
                new("App.cs", [Outcome(4)]),
            };

            var report = JudgeReportGenerator.Generate(groups, "claude-sonnet-4-6", "v1");

            Assert.Contains("# Judge Report", report);
            Assert.Contains("judge claude-sonnet-4-6, rubric v1", report);
        }

        [Fact]
        public void Generate_RendersPerGroupScoresRationalesAndOverall()
        {
            var groups = new List<JudgeReportGroup>
            {
                new("App.cs", [Outcome(4), Outcome(2)]),
            };

            var report = JudgeReportGenerator.Generate(groups, "claude-sonnet-4-6", "v1");

            // Per-group section with the label and averaged score (4 and 2 → 3.0).
            Assert.Contains("## App.cs", report);
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
                new("App.cs", [Outcome(4, cost: 0.01m, latencyMs: 1000)]),
                new("Other.cs", [Outcome(3, cost: 0.002m, latencyMs: 250)]),
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
