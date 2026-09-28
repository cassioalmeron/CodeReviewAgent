using CodeReviewerAgent.Core;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// What a finished review says on a terminal. <c>CodeReviewer</c> used to write these lines while
/// it worked, so nothing could assert them without capturing stdout (US-018). They are a returned
/// value now, and this is what pins the wording.
/// </summary>
public class ReviewConsoleReportTests
{
    private static ReviewResult Result(string? summary, List<Finding> findings, string? skills = null) =>
        new(summary, findings, Engine: "claude", Model: "claude-opus-4-8", PromptVersion: "v2",
            Skills: skills);

    private static readonly Finding Missing = new(
        "src/Data/UserRepository.cs", "var sql = \"SELECT \" + name;",
        Severity.Critical, Category.Security,
        "SQL injection through string concatenation.", "Use a parameterized query.", Line: 42);

    [Fact]
    public void Lines_WithNoFindings_StillReportsTheCount()
    {
        var lines = ReviewConsoleReport.Lines(Result("All good.", []));

        Assert.Equal(["All good.", "", "Findings: 0"], lines);
    }

    [Fact]
    public void Lines_RenderOneFindingPerLine_WithSeverityFileAndLine()
    {
        var lines = ReviewConsoleReport.Lines(Result("One issue found.", [Missing]));

        Assert.Equal("Findings: 1", lines[^2]);
        Assert.Equal(
            "  [Critical] src/Data/UserRepository.cs:42 (Security) — "
            + "SQL injection through string concatenation. -> Use a parameterized query.",
            lines[^1]);
    }

    /// <summary>
    /// The activated skills come first, as they did when the reviewer announced them before the
    /// review call, and they are stored comma-joined but read with a space.
    /// </summary>
    [Fact]
    public void Lines_LeadWithTheActivatedSkills()
    {
        var lines = ReviewConsoleReport.Lines(Result("Summary.", [], skills: "csharp,csharp-modern"));

        Assert.Equal("Skills: csharp, csharp-modern", lines[0]);
        Assert.Equal("Summary.", lines[1]);
    }

    /// <summary>A blank summary is not a line: an empty one would print as a stray blank.</summary>
    [Fact]
    public void Lines_SkipAnEmptySummary()
    {
        var lines = ReviewConsoleReport.Lines(Result("   ", []));

        Assert.Equal(["", "Findings: 0"], lines);
    }
}
