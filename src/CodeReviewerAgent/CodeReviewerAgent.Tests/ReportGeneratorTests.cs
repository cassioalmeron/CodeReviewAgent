using CodeReviewerAgent.Core;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    public class ReportGeneratorTests
    {
        private static ReviewResult Result(
            string? summary, List<Finding> findings, string? diff = null,
            string model = "claude-opus-4-8", decimal cost = 0.001234m, long latencyMs = 1234) =>
            new(summary, findings,
                Engine: "claude", Model: model, PromptVersion: "v2",
                Cost: cost, LatencyMs: latencyMs, InputTokens: 500, OutputTokens: 200, Diff: diff);

        [Fact]
        public void Generate_WithNoFilesOrFindings_RendersEmptyState()
        {
            var result = Result("All good.", []);

            var report = ReportGenerator.Generate(result);

            Assert.Contains("# Code Review Report", report);
            Assert.Contains("## Summary", report);
            Assert.Contains("All good.", report);
            Assert.Contains("No files reviewed.", report);
        }

        [Fact]
        public void Generate_RendersRunDetails()
        {
            var result = Result("Summary.", []);

            var report = ReportGenerator.Generate(result);

            Assert.Contains("## Run details", report);
            Assert.Contains("| LLM Engine | claude |", report);
            Assert.Contains("| Model | claude-opus-4-8 |", report);
            Assert.Contains("| Prompt version | v2 |", report);
            Assert.Contains("| Cost | $0.001234 USD |", report);
            Assert.Contains("| Latency | 1234 ms |", report);
            Assert.Contains("| Input tokens | 500 |", report);
            Assert.Contains("| Output tokens | 200 |", report);
            Assert.Contains("| Total tokens | 700 |", report);
        }

        [Fact]
        public void Generate_GroupsFindingsAndDiffPerFile()
        {
            var diff = string.Join("\n",
                "diff --git a/App.cs b/App.cs",
                "--- a/App.cs",
                "+++ b/App.cs",
                "@@ -1,1 +1,2 @@",
                " existing",
                "+var x = service.Process();",
                "diff --git a/Other.cs b/Other.cs",
                "--- a/Other.cs",
                "+++ b/Other.cs",
                "@@ -1,1 +1,2 @@",
                " existing",
                "+var y = 1;");

            var result = Result("Two files.",
            [
                new Finding(
                    File: "App.cs",
                    CodeSnippet: "var x = service.Process();",
                    Severity: Severity.Warning,
                    Category: Category.Bug,
                    Problem: "Possible null dereference.",
                    Suggestion: "Add a null check.",
                    Line: 2),
            ], diff);

            var report = ReportGenerator.Generate(result);

            Assert.Contains("| Files reviewed | 2 |", report);
            Assert.Contains("| Findings | 1 |", report);

            // App.cs: one finding plus its diff slice.
            Assert.Contains("## `App.cs`", report);
            Assert.Contains("### Findings (1)", report);
            Assert.Contains("#### 1. [Warning] Bug — line 2", report);
            Assert.Contains("**Problem:** Possible null dereference.", report);
            Assert.Contains("var x = service.Process();", report);

            // Other.cs: no findings but still its diff slice.
            Assert.Contains("## `Other.cs`", report);
            Assert.Contains("No findings.", report);
            Assert.Contains("+var y = 1;", report);
        }

        [Fact]
        public void Generate_PutsTheDiffInsideTheFileSectionBeforeTheFindings()
        {
            var diff = string.Join("\n",
                "diff --git a/App.cs b/App.cs",
                "--- a/App.cs",
                "+++ b/App.cs",
                "@@ -1,1 +1,2 @@",
                " existing",
                "+var x = 1;");
            var result = Result("Summary.", [], diff);

            var report = ReportGenerator.Generate(result);

            // No separate top-level diff section; the diff lives inside the file's scope.
            Assert.DoesNotContain("## Analyzed diff", report);
            var fileSection = report.IndexOf("## `App.cs`");
            var diffSection = report.IndexOf("### Diff");
            var findings = report.IndexOf("### Findings");
            Assert.True(fileSection >= 0 && diffSection > fileSection && findings > diffSection);
            Assert.Contains("```diff", report);
        }

        [Fact]
        public void Generate_WithNullSummary_OmitsSummarySection()
        {
            var result = Result(null, []);

            var report = ReportGenerator.Generate(result);

            Assert.DoesNotContain("## Summary", report);
        }

        [Fact]
        public void Generate_WithSingleReview_OmitsReviewLabelAndTotals()
        {
            var result = Result("Summary.", []);

            var report = ReportGenerator.Generate(result);

            Assert.DoesNotContain("# Review 1", report);
            Assert.DoesNotContain("# Totals", report);
        }

        [Fact]
        public void Generate_WithFooter_AppendsItAtTheEnd()
        {
            var result = Result("Summary.", []);
            const string footer = "# Golden set\n\nGolden set: 2/5";

            var report = ReportGenerator.Generate([result], footer);

            Assert.Contains("# Golden set", report);
            Assert.Contains("Golden set: 2/5", report);
            Assert.True(report.IndexOf("# Golden set") > report.IndexOf("## Run details"));
        }

        [Fact]
        public void Generate_WithMultipleRoundsOverSameDiff_GroupsThemAndSumsTotals()
        {
            var diff = string.Join("\n",
                "diff --git a/App.cs b/App.cs",
                "--- a/App.cs",
                "+++ b/App.cs",
                "@@ -1,1 +1,2 @@",
                " existing",
                "+var x = 1;");
            var a = Result("Run A.", [], diff, model: "qwen2.5-coder:7b", cost: 0.01m, latencyMs: 1000);
            var b = Result("Run B.", [], diff, model: "qwen2.5-coder:7b", cost: 0.002m, latencyMs: 250);

            var report = ReportGenerator.Generate([a, b], null);

            // Same diff, so a single shared diff section and one round per attempt.
            Assert.Contains("## Analyzed diff", report);
            // Round headers carry no model suffix (the model is in the shared Configuration).
            Assert.Contains($"# Round 1{Environment.NewLine}", report);
            Assert.Contains($"# Round 2{Environment.NewLine}", report);
            Assert.Contains("Run A.", report);
            Assert.Contains("Run B.", report);
            // The shared diff and configuration are shown once; run details are per round.
            Assert.Equal(1, CountOccurrences(report, "+var x = 1;"));
            Assert.Equal(1, CountOccurrences(report, "## Configuration"));
            Assert.Equal(1, CountOccurrences(report, "| Model | qwen2.5-coder:7b |"));
            Assert.Equal(2, CountOccurrences(report, "## Run details"));
            Assert.DoesNotContain("### Diff", report);
            // Totals sum the individual values.
            Assert.Contains("# Totals", report);
            Assert.Contains("| Total cost | $0.012 USD |", report);
            Assert.Contains("| Total latency | 1250 ms |", report);
            Assert.Contains("| Total input tokens | 1000 |", report);
            Assert.Contains("| Total output tokens | 400 |", report);
        }

        [Fact]
        public void Generate_WithDifferentDiffs_RendersOneGroupPerDiff()
        {
            var diffA = string.Join("\n",
                "diff --git a/App.cs b/App.cs",
                "--- a/App.cs",
                "+++ b/App.cs",
                "@@ -1,1 +1,2 @@",
                " existing",
                "+var a = 1;");
            var diffB = string.Join("\n",
                "diff --git a/Other.cs b/Other.cs",
                "--- a/Other.cs",
                "+++ b/Other.cs",
                "@@ -1,1 +1,2 @@",
                " existing",
                "+var b = 2;");
            var a = Result("Diff A.", [], diffA);
            var b = Result("Diff B.", [], diffB);

            var report = ReportGenerator.Generate([a, b], null);

            Assert.Contains("# Diff 1 — App.cs", report);
            Assert.Contains("# Diff 2 — Other.cs", report);
            Assert.DoesNotContain("# Round", report);

            // A horizontal rule separates one diff group from the next.
            var separator = report.IndexOf($"{Environment.NewLine}---{Environment.NewLine}");
            Assert.True(report.IndexOf("# Diff 1") < separator && separator < report.IndexOf("# Diff 2"));
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            for (var i = text.IndexOf(value); i >= 0; i = text.IndexOf(value, i + value.Length))
                count++;
            return count;
        }
    }
}
