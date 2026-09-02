using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Diff;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class DiffParserTests
{
    private static string Diff(params string[] lines) => string.Join("\n", lines);

    [Fact]
    public void Parse_EmptyDiff_ReturnsNoFiles()
    {
        Assert.Empty(DiffParser.Parse("").Files);
        Assert.Empty(DiffParser.Parse("   ").Files);
    }

    [Fact]
    public void Parse_ModifiedFile_ResolvesPathsHunkAndLineNumbers()
    {
        var diff = Diff(
            "diff --git a/src/App.cs b/src/App.cs",
            "index 1111111..2222222 100644",
            "--- a/src/App.cs",
            "+++ b/src/App.cs",
            "@@ -1,3 +1,4 @@ namespace App",
            " context",
            "-removed",
            "+added1",
            "+added2",
            " context2");

        var file = Assert.Single(DiffParser.Parse(diff).Files);
        Assert.Equal(DiffChangeType.Modified, file.ChangeType);
        Assert.Equal("src/App.cs", file.Path);

        var hunk = Assert.Single(file.Hunks);
        Assert.Equal(1, hunk.NewStart);
        Assert.Equal(4, hunk.NewCount);
        Assert.Equal("namespace App", hunk.HeaderContext);

        // Added lines land on the right absolute new-file line numbers.
        var added = hunk.Lines.Where(l => l.Kind == DiffLineKind.Added).ToList();
        Assert.Equal(["added1", "added2"], added.Select(l => l.Text));
        Assert.Equal([2, 3], added.Select(l => l.NewLineNumber));

        var removed = Assert.Single(hunk.Lines, l => l.Kind == DiffLineKind.Removed);
        Assert.Equal(2, removed.OldLineNumber);
        Assert.Null(removed.NewLineNumber);
    }

    [Fact]
    public void Parse_AddedFile_HasNullOldPathAndAddedChangeType()
    {
        var diff = Diff(
            "diff --git a/New.cs b/New.cs",
            "new file mode 100644",
            "index 0000000..3333333",
            "--- /dev/null",
            "+++ b/New.cs",
            "@@ -0,0 +1,2 @@",
            "+line one",
            "+line two");

        var file = Assert.Single(DiffParser.Parse(diff).Files);
        Assert.Equal(DiffChangeType.Added, file.ChangeType);
        Assert.Null(file.OldPath);
        Assert.Equal("New.cs", file.NewPath);
        Assert.Equal([1, 2], Assert.Single(file.Hunks).Lines.Select(l => l.NewLineNumber));
    }

    [Fact]
    public void Parse_DeletedFile_HasNullNewPathAndDeletedChangeType()
    {
        var diff = Diff(
            "diff --git a/Old.cs b/Old.cs",
            "deleted file mode 100644",
            "index 4444444..0000000",
            "--- a/Old.cs",
            "+++ /dev/null",
            "@@ -1,2 +0,0 @@",
            "-line one",
            "-line two");

        var file = Assert.Single(DiffParser.Parse(diff).Files);
        Assert.Equal(DiffChangeType.Deleted, file.ChangeType);
        Assert.Equal("Old.cs", file.OldPath);
        Assert.Null(file.NewPath);
    }

    [Fact]
    public void Parse_RenamedFile_CapturesBothPaths()
    {
        var diff = Diff(
            "diff --git a/Old.cs b/New.cs",
            "similarity index 100%",
            "rename from Old.cs",
            "rename to New.cs");

        var file = Assert.Single(DiffParser.Parse(diff).Files);
        Assert.Equal(DiffChangeType.Renamed, file.ChangeType);
        Assert.Equal("Old.cs", file.OldPath);
        Assert.Equal("New.cs", file.NewPath);
        Assert.Empty(file.Hunks);
    }

    [Fact]
    public void Parse_MultipleFilesAndHunks_AreAllCaptured()
    {
        var diff = Diff(
            "diff --git a/A.cs b/A.cs",
            "--- a/A.cs",
            "+++ b/A.cs",
            "@@ -1,1 +1,2 @@",
            " a",
            "+a2",
            "@@ -10,1 +11,2 @@",
            " b",
            "+b2",
            "diff --git a/B.cs b/B.cs",
            "--- a/B.cs",
            "+++ b/B.cs",
            "@@ -5,0 +6,1 @@",
            "+c");

        var files = DiffParser.Parse(diff).Files;
        Assert.Equal(2, files.Count);
        Assert.Equal(2, files[0].Hunks.Count);
        Assert.Equal(12, files[0].Hunks[1].Lines.Single(l => l.Kind == DiffLineKind.Added).NewLineNumber);
        Assert.Equal("B.cs", files[1].Path);
    }

    [Fact]
    public void Parse_HunkHeaderWithoutCounts_DefaultsToOne()
    {
        var diff = Diff(
            "diff --git a/A.cs b/A.cs",
            "--- a/A.cs",
            "+++ b/A.cs",
            "@@ -1 +1 @@",
            "-old",
            "+new");

        var hunk = Assert.Single(Assert.Single(DiffParser.Parse(diff).Files).Hunks);
        Assert.Equal(1, hunk.OldCount);
        Assert.Equal(1, hunk.NewCount);
    }

    [Fact]
    public void Parse_RemovedLineStartingWithDashes_IsNotMistakenForHeader()
    {
        var diff = Diff(
            "diff --git a/A.cs b/A.cs",
            "--- a/A.cs",
            "+++ b/A.cs",
            "@@ -1,1 +1,1 @@",
            "--- old comment marker",
            "+++ new comment marker");

        var hunk = Assert.Single(Assert.Single(DiffParser.Parse(diff).Files).Hunks);
        Assert.Equal(DiffLineKind.Removed, hunk.Lines[0].Kind);
        Assert.Equal("-- old comment marker", hunk.Lines[0].Text);
        Assert.Equal(DiffLineKind.Added, hunk.Lines[1].Kind);
        Assert.Equal("++ new comment marker", hunk.Lines[1].Text);
    }
}
