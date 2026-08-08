using CodeReviewerAgent.Core;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    public class SkillSelectorTests
    {
        private static readonly IReadOnlyList<SkillRef> Catalog =
        [
            new SkillRef("csharp", "C# conventions. Use for .cs files.", "/skills/csharp/SKILL.md", "/skills/csharp")
            {
                Metadata = new Dictionary<string, string> { ["applies-to"] = "*.cs" },
            },
            new SkillRef("react", "React conventions. Use for .tsx files.", "/skills/react/SKILL.md", "/skills/react")
            {
                Metadata = new Dictionary<string, string> { ["applies-to"] = "*.tsx,*.ts" },
            },
        ];

        private static SkillSelection Select(string response, params string[] files) =>
            new LlmSkillSelector(new FakeLlmClient(response)).Select(Catalog, files);


        [Fact]
        public void Llm_ReturnsTheChosenSkills()
        {
            Assert.Equal(["csharp"], Select("""{ "skills": ["csharp"] }""", "src/App.cs").Names);
        }

        [Fact]
        public void Llm_OffersTheCatalogAndTheChangedFiles()
        {
            var client = new FakeLlmClient("""{ "skills": [] }""");

            new LlmSkillSelector(client).Select(Catalog, ["src/App.cs"]);

            var body = System.Text.Json.JsonSerializer.SerializeToElement(client.LastRequestBody);
            var system = body.GetProperty("system").GetString()!;
            Assert.Contains("<name>csharp</name>", system);
            Assert.Contains("C# conventions. Use for .cs files.", system);
            // Tier 1 only: the catalog carries no instructions.
            Assert.DoesNotContain("PascalCase", system);
            Assert.Contains("src/App.cs", body.GetProperty("messages")[0].GetProperty("content").GetString());
        }

        [Fact]
        public void Llm_ConstrainsTheSchemaToTheCatalogNames()
        {
            var client = new FakeLlmClient("""{ "skills": [] }""");

            new LlmSkillSelector(client).Select(Catalog, ["src/App.cs"]);

            var enumerated = System.Text.Json.JsonSerializer.SerializeToElement(client.LastRequestBody)
                .GetProperty("json_schema").GetProperty("properties").GetProperty("skills")
                .GetProperty("items").GetProperty("enum")
                .EnumerateArray().Select(e => e.GetString()!).ToArray();

            Assert.Equal(["csharp", "react"], enumerated);
        }

        [Fact]
        public void Llm_DropsNamesThatAreNotInTheCatalog()
        {
            Assert.Equal(["csharp"], Select("""{ "skills": ["csharp", "hallucinated"] }""", "src/App.cs").Names);
        }

        [Fact]
        public void Llm_WithAnEmptyChoice_DoesNotFallBack()
        {
            // An empty answer is a decision ("no skill applies"), not a failure — the .cs glob
            // would have matched, so falling back here would silently override the model.
            Assert.Empty(Select("""{ "skills": [] }""", "src/App.cs").Names);
        }

        [Fact]
        public void Llm_ReadsABareArrayAsTheSelection()
        {
            // Engines without native schema enforcement answer with the list alone. Reading it is
            // better than degrading to the globs, and `TryGetProperty` throws on a non-object root.
            Assert.Equal(["csharp"], Select("""["csharp"]""", "src/App.cs").Names);
        }

        [Fact]
        public void Llm_ReadsAnEmptyBareArrayAsAnEmptySelection()
        {
            Assert.Empty(Select("[]", "src/App.cs").Names);
        }

        [Theory]
        [InlineData("""[{ "name": "csharp" }]""")]
        [InlineData("""[{ "skill": "csharp" }]""")]
        [InlineData("""{ "skills": [{ "name": "csharp" }] }""")]
        [InlineData("""{ "skill": ["csharp"] }""")]  // singular key, array value
        public void Llm_ReadsNamesWrappedInObjects(string response)
        {
            Assert.Equal(["csharp"], Select(response, "src/App.cs").Names);
        }

        [Theory]
        [InlineData("")]                                    // empty body
        [InlineData("   ")]                                 // blank body
        [InlineData("null")]                                // JSON null
        [InlineData("{}")]                                  // object with no skills key
        [InlineData("""{ "skills": null }""")]              // the key, explicitly empty
        public void Llm_ReadsNothingAsAnEmptySelection(string response)
        {
            // "No skill applies" is the right answer for most diffs, and models express it in all
            // these shapes. Treating them as a decision — not as a failure — is what keeps the
            // negative cases of the trigger eval measurable.
            var selection = Select(response, "src/App.cs");

            Assert.Empty(selection.Names);
            Assert.False(selection.Unreadable);
        }

        [Theory]
        [InlineData("I think the C# skill applies.")]      // not JSON
        [InlineData("\"csharp\"")]                          // scalar root
        [InlineData("""{ "summary": "no idea" }""")]        // object with another shape entirely
        [InlineData("[1, 2]")]                              // array with no readable name
        [InlineData("""[{ "id": 7 }]""")]                   // objects without name/skill
        public void Llm_WhenTheAnswerCannotBeRead_SelectsNothingAndSaysSo(string response)
        {
            // No fallback: an unreadable answer loads no skills. It stays counted apart from a
            // deliberate empty answer, so schema adherence remains measurable.
            var selection = Select(response, "src/App.cs");

            Assert.Empty(selection.Names);
            Assert.True(selection.Unreadable);
        }

        [Fact]
        public void Llm_ReportsTheUsageEvenWhenTheAnswerCannotBeRead()
        {
            // An unreadable answer was still paid for. Reporting zero would understate the cost of
            // exactly the runs that go wrong most often.
            var selection = Select("I think the C# skill applies.", "src/App.cs");

            Assert.True(selection.Unreadable);
            Assert.Equal(10, selection.InputTokens);
            Assert.Equal(20, selection.OutputTokens);
        }

        [Fact]
        public void MechanicalStrategies_AreNeverUnreadable()
        {
            Assert.False(new GlobSkillSelector().Select(Catalog, ["src/App.cs"]).Unreadable);
            Assert.False(new ExplicitSkillSelector(["csharp"]).Select(Catalog, []).Unreadable);
            Assert.False(new NoSkillSelector().Select(Catalog, []).Unreadable);
        }

        [Fact]
        public void Llm_ReportsTheUsageOfTheSelectionCall()
        {
            var selection = Select("""{ "skills": ["react"] }""", "web/src/App.tsx");

            Assert.Equal(10, selection.InputTokens);
            Assert.Equal(20, selection.OutputTokens);
        }

        [Fact]
        public void Glob_SelectsByAppliesTo()
        {
            var globs = new GlobSkillSelector();

            Assert.Equal(["csharp"], globs.Select(Catalog, ["src/App.cs"]).Names);
            Assert.Equal(["react"], globs.Select(Catalog, ["web/src/App.tsx"]).Names);
            Assert.Empty(globs.Select(Catalog, ["main.py"]).Names);
            Assert.Equal(0, globs.Select(Catalog, ["src/App.cs"]).InputTokens);
        }

        [Fact]
        public void Explicit_LoadsTheNamedSkillsRegardlessOfTheFiles()
        {
            var selector = new ExplicitSkillSelector(["react", "nonexistent"]);

            // A .cs diff, and react is loaded anyway: the user asked for it.
            Assert.Equal(["react"], selector.Select(Catalog, ["src/App.cs"]).Names);
        }

        [Fact]
        public void None_SelectsNothing()
        {
            Assert.Empty(new NoSkillSelector().Select(Catalog, ["src/App.cs"]).Names);
        }

        [Theory]
        [InlineData(null, typeof(LlmSkillSelector))]
        [InlineData("", typeof(LlmSkillSelector))]
        [InlineData("all", typeof(LlmSkillSelector))]
        [InlineData("ALL", typeof(LlmSkillSelector))]
        [InlineData("globs", typeof(GlobSkillSelector))]
        [InlineData("off", typeof(NoSkillSelector))]
        [InlineData("csharp,react", typeof(ExplicitSkillSelector))]
        public void Factory_PicksTheStrategyFromTheSetting(string? setting, Type expected)
        {
            var selector = SkillSelectorFactory.Create(new FakeLlmClient("{}"), setting);

            Assert.IsType(expected, selector);
        }

        [Fact]
        public void Factory_WithGlobs_NeverCallsTheModel()
        {
            var client = new FakeLlmClient("""{ "skills": ["react"] }""");

            var selection = SkillSelectorFactory.Create(client, "globs").Select(Catalog, ["src/App.cs"]);

            Assert.Null(client.LastRequestBody);
            Assert.Equal(["csharp"], selection.Names);
        }
    }
}
