using CodeReviewerAgent.Core;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    public class CodeReviewerTests
    {
        [Fact]
        public void Review_WithEmptyDiff_DoesNotCallClientAndReturnsNoFindings()
        {
            var client = new FakeLlmClient("{}");

            var result = new CodeReviewer(client, "   ").Review();

            Assert.Null(client.LastRequestBody);
            Assert.Empty(result.Findings!);
        }

        [Fact]
        public void Review_WithValidDiff_DerivesLineFromSnippetAndKeepsGroundedFinding()
        {
            var diff = string.Join("\n",
                "diff --git a/App.cs b/App.cs",
                "--- a/App.cs",
                "+++ b/App.cs",
                "@@ -1,2 +1,3 @@",
                " existing line",
                "+var result = service.Process();",
                " trailing line");

            const string response = """
                {
                    "summary": "One issue found.",
                    "findings": [
                        {
                            "file": "App.cs",
                            "code_snippet": "var result = service.Process();",
                            "severity": "warning",
                            "category": "bug",
                            "problem": "Possible null dereference.",
                            "suggestion": "Add a null check."
                        }
                    ]
                }
                """;
            var client = new FakeLlmClient(response);

            var result = new CodeReviewer(client, diff).Review();

            Assert.NotNull(client.LastRequestBody);
            Assert.Equal("One issue found.", result.Summary);
            var finding = Assert.Single(result.Findings!);
            Assert.Equal("App.cs", finding.File);
            Assert.Equal(2, finding.Line); // derived from the snippet, not sent by the model
            Assert.Equal(Severity.Warning, finding.Severity);
            Assert.Equal(Category.Bug, finding.Category);
        }

        [Fact]
        public void Review_WhenSnippetIsNotAnAddedLine_DropsTheFinding()
        {
            var diff = string.Join("\n",
                "diff --git a/App.cs b/App.cs",
                "--- a/App.cs",
                "+++ b/App.cs",
                "@@ -1,1 +1,2 @@",
                " existing line",
                "+var added = 1;");

            // The model cites a line that does not exist among the added lines.
            const string response = """
                {
                    "summary": "Hallucinated finding.",
                    "findings": [
                        {
                            "file": "App.cs",
                            "code_snippet": "var hallucinated = neverAdded();",
                            "severity": "critical",
                            "category": "bug",
                            "problem": "Made-up problem.",
                            "suggestion": "Made-up fix."
                        }
                    ]
                }
                """;
            var client = new FakeLlmClient(response);

            var result = new CodeReviewer(client, diff).Review();

            Assert.Empty(result.Findings!);
        }

        [Fact]
        public void Review_WhenClientReturnsInvalidJson_ReturnsEmptyResult()
        {
            var client = new FakeLlmClient("not valid json");

            var result = new CodeReviewer(client, "diff --git a/App.cs b/App.cs").Review();

            Assert.Null(result.Summary);
            Assert.Empty(result.Findings!);
        }
    }
}
