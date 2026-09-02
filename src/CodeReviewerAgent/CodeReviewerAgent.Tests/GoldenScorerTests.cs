using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The golden verdict, in isolation: no LLM, no repositories, no fixtures. This is where the
/// two opposite failures are kept apart — missing a planted bug, and inventing one where the
/// code is correct.
/// </summary>
public class GoldenScorerTests
{
    private static Finding Finding(string? file, string? snippet, string? problem = "problem", string? suggestion = "suggestion") =>
        new(file, snippet, Severity.Warning, Category.Bug, problem, suggestion, 1);

    // --- Detection: a planted problem must be found ---

    [Fact]
    public void Detection_SucceedsOnFileAndKeyword()
    {
        var expect = new ExpectFinding("src/Data/UserRepository.cs", Category.Security, ["injection"]);
        var findings = new[] { Finding("src/Data/UserRepository.cs", "x", "SQL injection risk") };

        Assert.True(GoldenScorer.Succeeded(findings, expect));
    }

    [Fact]
    public void Detection_FailsWhenTheFindingIsInAnotherFile()
    {
        var expect = new ExpectFinding("src/Data/UserRepository.cs", Category.Security, ["injection"]);
        var findings = new[] { Finding("src/Other.cs", "x", "SQL injection risk") };

        Assert.False(GoldenScorer.Succeeded(findings, expect));
    }

    [Fact]
    public void Detection_FailsWhenNoKeywordAppears()
    {
        var expect = new ExpectFinding("src/Data/UserRepository.cs", Category.Security, ["injection"]);
        var findings = new[] { Finding("src/Data/UserRepository.cs", "x", "naming could be clearer") };

        Assert.False(GoldenScorer.Succeeded(findings, expect));
    }

    [Fact]
    public void Detection_ReadsTheKeywordFromTheSuggestionToo()
    {
        var expect = new ExpectFinding("A.cs", Category.Bug, ["parameter"]);
        var findings = new[] { Finding("A.cs", "x", "unsafe query", "use a PARAMETER") };

        Assert.True(GoldenScorer.Succeeded(findings, expect));
    }

    // --- Trap: correct code must not be flagged ---

    [Fact]
    public void Trap_SucceedsWhenNothingIsReported()
    {
        var trap = new ExpectNoFinding("src/Money.cs", "public extension(decimal value)");

        Assert.True(GoldenScorer.Succeeded([], trap));
    }

    /// <summary>
    /// Decision 2: only a finding on the bait counts as falling for it. Otherwise the trap
    /// would be a test of silence, rewarding the model that says little over the one that
    /// discriminates.
    /// </summary>
    [Fact]
    public void Trap_IgnoresALegitimateFindingElsewhereInTheSameFile()
    {
        var trap = new ExpectNoFinding("src/Money.cs", "public extension(decimal value)");
        var findings = new[] { Finding("src/Money.cs", "var total = a + b;", "prefer interpolation") };

        Assert.True(GoldenScorer.Succeeded(findings, trap));
    }

    [Fact]
    public void Trap_FailsWhenTheFindingCitesTheBait()
    {
        var trap = new ExpectNoFinding("src/Money.cs", "public extension(decimal value)");
        var findings = new[] { Finding("src/Money.cs", "public extension(decimal value)", "invalid syntax") };

        Assert.False(GoldenScorer.Succeeded(findings, trap));
    }

    /// <summary>The model may quote the whole line, of which the bait is only a part.</summary>
    [Fact]
    public void Trap_FailsWhenTheCitedLineContainsTheBait()
    {
        var trap = new ExpectNoFinding("src/Money.cs", "public extension(decimal value)");
        var findings = new[] { Finding("src/Money.cs", "    public extension(decimal value) { }", "invalid syntax") };

        Assert.False(GoldenScorer.Succeeded(findings, trap));
    }

    [Fact]
    public void Trap_MatchesTheBaitRegardlessOfIndentation()
    {
        var trap = new ExpectNoFinding("src/Money.cs", "public   extension(decimal value)");
        var findings = new[] { Finding("src/Money.cs", "\tpublic extension(decimal  value)", "invalid syntax") };

        Assert.False(GoldenScorer.Succeeded(findings, trap));
    }

    [Fact]
    public void Trap_IgnoresTheSameBaitInAnotherFile()
    {
        var trap = new ExpectNoFinding("src/Money.cs", "public extension(decimal value)");
        var findings = new[] { Finding("src/Other.cs", "public extension(decimal value)", "invalid syntax") };

        Assert.True(GoldenScorer.Succeeded(findings, trap));
    }

    [Fact]
    public void Trap_IgnoresAFindingThatCitesNothing()
    {
        var trap = new ExpectNoFinding("src/Money.cs", "public extension(decimal value)");
        var findings = new[] { Finding("src/Money.cs", null, "something feels off") };

        Assert.True(GoldenScorer.Succeeded(findings, trap));
    }
}
