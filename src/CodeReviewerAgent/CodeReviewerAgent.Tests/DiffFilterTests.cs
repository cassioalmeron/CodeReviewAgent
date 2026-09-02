using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Diff;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class DiffFilterTests
{
    private const string CsFile = """
        diff --git a/App.cs b/App.cs
        --- a/App.cs
        +++ b/App.cs
        @@ -1,1 +1,2 @@
         existing
        +var x = 1;
        """;

    private const string MdFile = """
        diff --git a/Readme.md b/Readme.md
        --- a/Readme.md
        +++ b/Readme.md
        @@ -1,1 +1,2 @@
         existing
        +New docs line.
        """;

    [Fact]
    public void ExcludeMarkdown_KeepsCodeFiles()
    {
        var result = DiffFilter.ExcludeMarkdown(CsFile);

        Assert.Contains("App.cs", result);
        Assert.Contains("+var x = 1;", result);
    }

    [Fact]
    public void ExcludeMarkdown_DropsMarkdownFileSections()
    {
        var diff = CsFile + "\n" + MdFile;

        var result = DiffFilter.ExcludeMarkdown(diff);

        Assert.Contains("App.cs", result);
        Assert.DoesNotContain("Readme.md", result);
        Assert.DoesNotContain("New docs line.", result);
    }

    [Fact]
    public void ExcludeMarkdown_WhenOnlyMarkdown_ReturnsEmpty()
    {
        var result = DiffFilter.ExcludeMarkdown(MdFile);

        Assert.True(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void ExcludeMarkdown_WhenBlank_ReturnsInput()
    {
        Assert.Equal("", DiffFilter.ExcludeMarkdown(""));
    }
}
