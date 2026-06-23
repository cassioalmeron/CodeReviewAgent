using CodeReviewerAgent.Core;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    public class ReportGeneratorTests
    {
        private static readonly ReportMetadata Metadata =
            new("claude", "claude-opus-4-8", "v2", 0.001234m, 1234, 500, 200, null);

        [Fact]
        public void Generate_WithNoFilesOrFindings_RendersEmptyState()
        {
            var result = new ReviewResult("All good.", []);

            var report = ReportGenerator.Generate(result, Metadata);

            Assert.Contains("# Code Review Report", report);
            Assert.Contains("## Summary", report);
            Assert.Contains("All good.", report);
            Assert.Contains("No files reviewed.", report);
        }

        [Fact]
        public void Generate_RendersRunDetails()
        {
            var result = new ReviewResult("Summary.", []);

            var report = ReportGenerator.Generate(result, Metadata);

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

            var result = new ReviewResult("Two files.",
            [
                new Finding(
                    File: "App.cs",
                    CodeSnippet: "var x = service.Process();",
                    Severity: Severity.Warning,
                    Category: Category.Bug,
                    Problem: "Possible null dereference.",
                    Suggestion: "Add a null check.",
                    Line: 2),
            ]);
            var metadata = Metadata with { Diff = diff };

            var report = ReportGenerator.Generate(result, metadata);

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
        public void Generate_PutsEachFilesDiffUnderItsOwnSection()
        {
            var diff = string.Join("\n",
                "diff --git a/App.cs b/App.cs",
                "--- a/App.cs",
                "+++ b/App.cs",
                "@@ -1,1 +1,2 @@",
                " existing",
                "+var x = 1;");
            var result = new ReviewResult("Summary.", []);
            var metadata = Metadata with { Diff = diff };

            var report = ReportGenerator.Generate(result, metadata);

            var fileHeader = report.IndexOf("## `App.cs`");
            var diffHeader = report.IndexOf("### Diff");
            Assert.True(fileHeader >= 0 && diffHeader > fileHeader);
            Assert.Contains("```diff", report);
        }

        [Fact]
        public void Generate_WithNullSummary_OmitsSummarySection()
        {
            var result = new ReviewResult(null, []);

            var report = ReportGenerator.Generate(result, Metadata);

            Assert.DoesNotContain("## Summary", report);
        }
    }
}
