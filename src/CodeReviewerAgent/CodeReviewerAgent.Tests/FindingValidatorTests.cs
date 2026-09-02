using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Diff;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class FindingValidatorTests
{
    // A diff that adds `var x = compute();` at new-file line 2.
    private static ParsedDiff SampleDiff() => DiffParser.Parse(string.Join("\n",
        "diff --git a/src/App.cs b/src/App.cs",
        "--- a/src/App.cs",
        "+++ b/src/App.cs",
        "@@ -1,2 +1,3 @@",
        " context before",
        "+var x = compute();",
        " context after"));

    private static Finding Finding(string? file, string? snippet) =>
        new(file, snippet, Severity.Warning, Category.Bug, "problem", "suggestion");

    [Fact]
    public void Validate_SnippetMatchingAddedLine_KeepsFindingWithDerivedLine()
    {
        var findings = new[] { Finding("src/App.cs", "var x = compute();") };

        var result = FindingValidator.Validate(findings, SampleDiff());

        var kept = Assert.Single(result);
        Assert.Equal(2, kept.Line);
    }

    [Fact]
    public void Validate_IgnoresLeadingAndTrailingWhitespaceDifferences()
    {
        var findings = new[] { Finding("src/App.cs", "   var x = compute();   ") };

        var kept = Assert.Single(FindingValidator.Validate(findings, SampleDiff()));
        Assert.Equal(2, kept.Line);
    }

    [Fact]
    public void Validate_SnippetNotInDiff_DropsFinding()
    {
        var findings = new[] { Finding("src/App.cs", "var y = neverAdded();") };

        Assert.Empty(FindingValidator.Validate(findings, SampleDiff()));
    }

    [Fact]
    public void Validate_SnippetOnContextLine_DropsFinding()
    {
        // "context before" exists in the diff but is not an added line.
        var findings = new[] { Finding("src/App.cs", "context before") };

        Assert.Empty(FindingValidator.Validate(findings, SampleDiff()));
    }

    [Fact]
    public void Validate_WrongFile_DropsFinding()
    {
        var findings = new[] { Finding("src/Other.cs", "var x = compute();") };

        Assert.Empty(FindingValidator.Validate(findings, SampleDiff()));
    }

    [Fact]
    public void Validate_MissingSnippet_DropsFinding()
    {
        var findings = new[] { Finding("src/App.cs", null), Finding("src/App.cs", "   ") };

        Assert.Empty(FindingValidator.Validate(findings, SampleDiff()));
    }
}
