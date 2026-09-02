using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Diff;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class DiffSourceFactoryTests
{
    [Fact]
    public void Create_WithNoArgs_ReturnsLocalDiffSource()
    {
        Assert.IsType<LocalDiffSource>(DiffSourceFactory.Create([]));
    }

    [Fact]
    public void Create_WithPrAndNumber_ReturnsPullRequestDiffSource()
    {
        Assert.IsType<PullRequestDiffSource>(DiffSourceFactory.Create(["pr", "42"]));
    }

    [Fact]
    public void Create_WithStaged_ReturnsStagedDiffSource()
    {
        Assert.IsType<StagedDiffSource>(DiffSourceFactory.Create(["staged"]));
    }

    [Fact]
    public void Create_WithFilesAndPaths_ReturnsFilesDiffSource()
    {
        Assert.IsType<FilesDiffSource>(DiffSourceFactory.Create(["files", "a.cs", "b.cs"]));
    }

    [Fact]
    public void Create_WithFilesButNoPaths_Throws()
    {
        Assert.Throws<ArgumentException>(() => DiffSourceFactory.Create(["files"]));
    }

    [Fact]
    public void Create_WithPrButNonNumeric_FallsBackToLocalDiffSource()
    {
        Assert.IsType<LocalDiffSource>(DiffSourceFactory.Create(["pr", "abc"]));
    }
}
