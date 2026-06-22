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
        public void Review_WithValidDiff_SendsDiffToClientAndParsesFindings()
        {
            const string response = """
                {
                    "summary": "One issue found.",
                    "findings": [
                        {
                            "file": "src/App.cs",
                            "line": 42,
                            "severity": "warning",
                            "category": "bug",
                            "problem": "Possible null dereference.",
                            "suggestion": "Add a null check."
                        }
                    ]
                }
                """;
            var client = new FakeLlmClient(response);

            var result = new CodeReviewer(client, "diff --git a/App.cs b/App.cs").Review();

            Assert.NotNull(client.LastRequestBody);
            Assert.Equal("One issue found.", result.Summary);
            var finding = Assert.Single(result.Findings!);
            Assert.Equal("src/App.cs", finding.File);
            Assert.Equal(42, finding.Line);
            Assert.Equal(Severity.Warning, finding.Severity);
            Assert.Equal(Category.Bug, finding.Category);
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
