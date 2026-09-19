using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The golden set reviews the same text on every machine. Git on Windows checks the fixtures out with
/// <c>\r\n</c> and the Linux CI runner with <c>\n</c>; before this, the model saw two different diffs
/// and the CI run failed against a baseline measured on Windows with nothing changed.
/// </summary>
public class GoldenDiffLineEndingTests
{
    [Fact]
    public void LoadDiff_TurnsWindowsLineEndingsIntoUnix()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cra-diff-{Guid.NewGuid():N}.diff");
        try
        {
            File.WriteAllText(path, "diff --git a/A.cs b/A.cs\r\n+var x = 1;\r\n");

            Assert.Equal("diff --git a/A.cs b/A.cs\n+var x = 1;\n", GoldenEvaluator.LoadDiff(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The splitter rebuilds each file's block, and that block is what reaches the model. It used
    /// AppendLine, which writes \r\n on Windows: this is the test that would have caught it.
    /// </summary>
    [Fact]
    public void DiffSplitter_RebuildsWithUnixLineEndings_OnAnyOperatingSystem()
    {
        var blocks = CodeReviewerAgent.Core.Diff.DiffSplitter.ByFile(
            "diff --git a/A.cs b/A.cs\r\n--- a/A.cs\r\n+++ b/A.cs\r\n+var x = 1;\r\n");

        var block = Assert.Single(blocks);
        Assert.DoesNotContain('\r', block.Text);
        Assert.Contains("+var x = 1;\n", block.Text);
    }

    [Fact]
    public void EveryBundledCase_LoadsWithoutACarriageReturn()
    {
        var cases = GoldenEvaluator.LoadCases();

        Assert.NotEmpty(cases);
        Assert.All(cases, c =>
            Assert.DoesNotContain('\r', GoldenEvaluator.LoadDiff(Path.Combine(GoldenEvaluator.CasesDirectory, c.Diff))));
    }
}
