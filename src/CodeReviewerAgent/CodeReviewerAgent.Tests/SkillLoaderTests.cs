using CodeReviewerAgent.Core;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    public class SkillLoaderTests
    {
        [Fact]
        public void BuildGuidelines_ForCsFile_IncludesCsharpSkillOnly()
        {
            var result = SkillLoader.BuildGuidelines(["src/App.cs"]);

            Assert.Contains("Project guidelines", result);
            Assert.Contains("category: convention", result);
            Assert.Contains("C# conventions", result);
            Assert.DoesNotContain("React", result);
        }

        [Fact]
        public void BuildGuidelines_ForTsxFile_IncludesReactSkill()
        {
            var result = SkillLoader.BuildGuidelines(["web/src/App.tsx"]);

            Assert.Contains("React", result);
            Assert.DoesNotContain("C# conventions", result);
        }

        [Fact]
        public void BuildGuidelines_WhenNoFileMatches_ReturnsEmpty()
        {
            var result = SkillLoader.BuildGuidelines(["main.py", "config.yaml"]);

            Assert.Equal("", result);
        }

        [Fact]
        public void BuildGuidelines_WhenNoFiles_ReturnsEmpty()
        {
            Assert.Equal("", SkillLoader.BuildGuidelines([]));
        }
    }
}
