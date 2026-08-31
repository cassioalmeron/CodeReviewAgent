using System.Text.Json;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    public class CodeReviewerTests
    {
        private const string CsharpDiff = """
            diff --git a/App.cs b/App.cs
            --- a/App.cs
            +++ b/App.cs
            @@ -1,2 +1,3 @@
             existing line
            +var result = service.Process();
             trailing line
            """;

        // The system prompt of the review call, i.e. what the activated skills were folded into.
        private static string SystemPrompt(FakeLlmClient client) =>
            JsonSerializer.SerializeToElement(client.LastRequestBody).GetProperty("system").GetString()!;

        [Fact]
        public void Review_WithEmptyDiff_DoesNotCallClientAndReturnsNoFindings()
        {
            var client = new FakeLlmClient("{}");

            var result = new CodeReviewer(client, "   ", "v3", new FakeSkillSelector()).Review();

            Assert.Null(client.LastRequestBody);
            Assert.Empty(result.Findings!);
        }

        [Fact]
        public void Review_WithOnlyMarkdownDiff_DoesNotCallClientAndReturnsNoFindings()
        {
            var diff = string.Join("\n",
                "diff --git a/Readme.md b/Readme.md",
                "--- a/Readme.md",
                "+++ b/Readme.md",
                "@@ -1,1 +1,2 @@",
                " existing",
                "+New docs line.");
            var client = new FakeLlmClient("{}");

            var result = new CodeReviewer(client, diff, "v3", new FakeSkillSelector()).Review();

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

            var result = new CodeReviewer(client, diff, "v3", new FakeSkillSelector()).Review();

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

            var result = new CodeReviewer(client, diff, "v3", new FakeSkillSelector()).Review();

            Assert.Empty(result.Findings!);
        }

        [Fact]
        public void Review_WhenClientReturnsInvalidJson_ReturnsEmptyResult()
        {
            var client = new FakeLlmClient("not valid json");

            var result = new CodeReviewer(client, "diff --git a/App.cs b/App.cs", "v3", new FakeSkillSelector()).Review();

            Assert.Null(result.Summary);
            Assert.Empty(result.Findings!);
        }

        [Fact]
        public void Review_LoadsOnlyTheSkillsTheModelChose()
        {
            var client = new FakeLlmClient("{}");
            var selector = new FakeSkillSelector("csharp");

            new CodeReviewer(client, CsharpDiff, "v3", selector).Review();

            var system = SystemPrompt(client);
            Assert.Contains("# Project guidelines", system);
            Assert.Contains("<skill_content name=\"csharp\">", system);
            Assert.DoesNotContain("styled-components", system); // the react skill was not loaded
            Assert.Equal(["App.cs"], selector.LastFiles);
        }

        [Fact]
        public void Review_RecordsWhichSkillsWereInThePrompt()
        {
            var withSkill = new CodeReviewer(new FakeLlmClient("{}"), CsharpDiff, "v3", new FakeSkillSelector("csharp"));
            var withNone = new CodeReviewer(new FakeLlmClient("{}"), CsharpDiff, "v3", new FakeSkillSelector());

            Assert.Equal("csharp", withSkill.Review().Skills);
            Assert.Null(withNone.Review().Skills);
        }

        [Fact]
        public void Review_WhenNoSkillIsChosen_OmitsTheGuidelinesSection()
        {
            var client = new FakeLlmClient("{}");

            new CodeReviewer(client, CsharpDiff, "v3", new FakeSkillSelector()).Review();

            Assert.DoesNotContain("# Project guidelines", SystemPrompt(client));
        }

        [Fact]
        public void Review_LoadsWhateverTheStrategyChose_WithoutKnowingWhichOneItIs()
        {
            var client = new FakeLlmClient("{}");

            // A .cs diff, and the strategy answers "react": the pipeline obeys the decision
            // instead of second-guessing it with its own file matching.
            new CodeReviewer(client, CsharpDiff, "v3", new GlobsOverride("react")).Review();

            Assert.Contains("<skill_content name=\"react\">", SystemPrompt(client));
            Assert.DoesNotContain("<skill_content name=\"csharp\">", SystemPrompt(client));
        }

        private sealed class GlobsOverride(params string[] names) : ISkillSelector
        {
            public SkillSelection Select(IReadOnlyList<SkillRef> catalog, IReadOnlyList<string> files) =>
                new(names);
        }

        [Fact]
        public void Review_FoldsTheSelectionUsageIntoTheTotals()
        {
            var client = new FakeLlmClient("{}");

            var result = new CodeReviewer(client, CsharpDiff, "v3", new FakeSkillSelector("csharp")).Review();

            Assert.Equal(17, result.InputTokens);  // 10 from the review + 7 from the selection
            Assert.Equal(23, result.OutputTokens); // 20 + 3
            Assert.Equal(0.5m, result.Cost);
        }

        /// <summary>
        /// The prompt version is a constructor parameter, not process state: <c>Review()</c> must
        /// use exactly what it was given, even when the environment disagrees.
        /// </summary>
        [Fact]
        public void Review_UsesTheGivenPromptVersion_RegardlessOfTheEnvironment()
        {
            var previous = Environment.GetEnvironmentVariable("PROMPT_VERSION");
            try
            {
                Environment.SetEnvironmentVariable("PROMPT_VERSION", "v5"); // must be ignored
                var client = new FakeLlmClient("{}");

                var result = new CodeReviewer(client, CsharpDiff, "v1", new FakeSkillSelector()).Review();

                Assert.Equal("v1", result.PromptVersion);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PROMPT_VERSION", previous);
            }
        }
    }
}
