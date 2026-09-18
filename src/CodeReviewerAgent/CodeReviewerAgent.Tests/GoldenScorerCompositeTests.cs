using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The composite metric. Every case here is taken from the 2026-08-16 measurement rather than
/// invented, because the rules were decided by looking at what the model actually emitted, and a
/// test written against an imagined finding would not have caught what that report showed.
/// </summary>
public class GoldenScorerCompositeTests
{
    private static readonly ExpectFinding Injection = new(
        "src/Data/UserRepository.cs", Category.Security, ["injection", "sql", "parameter"], Severity.Critical);

    private static GoldenCase Detection(params AcceptableFinding[] acceptable) =>
        new("sql-injection", "d.diff", "g.md", Injection,
            AlsoAcceptable: acceptable.Length == 0 ? null : [.. acceptable]);

    private static Finding Found(string problem, Severity severity = Severity.Critical, string? file = null) =>
        new(file ?? "src/Data/UserRepository.cs", "snippet", severity, Category.Security, problem, "fix it");

    // Rounds 1, 4 and 5 of the case: one finding, the planted one.
    [Fact]
    public void OneCorrectFinding_IsAllThreeAxesClean()
    {
        var score = GoldenScorer.Score([Found("SQL injection through concatenation")], Detection());

        Assert.True(score.Succeeded);
        Assert.Equal(1, score.PrecisionCorrect);
        Assert.Equal(1, score.PrecisionCounted);
        Assert.Equal(0, score.CalibrationDistance);
    }

    /// <summary>
    /// Round 2: two Critical findings for the same concatenation, the second one saying only that
    /// the line below inherits the problem. Detection cannot tell it from round 1; precision can.
    /// </summary>
    [Fact]
    public void TheSameProblemSaidTwice_IsADuplicate()
    {
        var score = GoldenScorer.Score(
            [Found("SQL injection through concatenation"),
             Found("The query execution follows from the SQL injection on the preceding line")],
            Detection());

        Assert.True(score.Succeeded);
        Assert.Equal(1, score.Count(FindingVerdict.Planted));
        Assert.Equal(1, score.Count(FindingVerdict.Duplicate));
        Assert.Equal(1, score.PrecisionCorrect);
        Assert.Equal(2, score.PrecisionCounted);
    }

    /// <summary>
    /// Round 3's second finding, "go back to the in-memory LINQ". On a detection case an
    /// unforeseen remark leaves precision untouched on both sides — the case author's list may be
    /// what is incomplete — and is reported instead.
    /// </summary>
    [Fact]
    public void AnUnforeseenFinding_LeavesPrecisionAlone()
    {
        var score = GoldenScorer.Score(
            [Found("SQL injection through concatenation"),
             Found("Reverting to the in-memory LINQ query would be simpler", Severity.Warning)],
            Detection());

        Assert.Equal(1, score.Count(FindingVerdict.Unforeseen));
        Assert.Equal(1, score.PrecisionCorrect);
        Assert.Equal(1, score.PrecisionCounted);
    }

    /// <summary>Once the same remark is promoted into the list, it stops being unforeseen.</summary>
    [Fact]
    public void APromotedFinding_CountsAsCorrect()
    {
        var listed = new AcceptableFinding(
            "src/Data/UserRepository.cs", ["in-memory linq"], "because the measurement showed it");

        var score = GoldenScorer.Score(
            [Found("SQL injection through concatenation"),
             Found("Reverting to the in-memory LINQ query would be simpler", Severity.Warning)],
            Detection(listed));

        Assert.Equal(1, score.Count(FindingVerdict.Acceptable));
        Assert.Equal(0, score.Count(FindingVerdict.Unforeseen));
        Assert.Equal(2, score.PrecisionCorrect);
        Assert.Equal(2, score.PrecisionCounted);
    }

    [Theory]
    [InlineData(Severity.Critical, 0)]
    [InlineData(Severity.Warning, -1)]
    [InlineData(Severity.Info, -2)]
    public void Calibration_IsTheSignedDistanceFromTheExpectedSeverity(Severity emitted, int expected)
    {
        var score = GoldenScorer.Score([Found("SQL injection", emitted)], Detection());

        Assert.True(score.Succeeded, "the round still detects: only the severity changed");
        Assert.Equal(expected, score.CalibrationDistance);
    }

    /// <summary>
    /// A round that never found the planted problem has no severity to be right or wrong about.
    /// Detection already punishes it, and inventing a distance here would double-count the miss.
    /// </summary>
    [Fact]
    public void AMissedRound_HasNoCalibrationPoint()
    {
        var score = GoldenScorer.Score([Found("Consider renaming this method", Severity.Info)], Detection());

        Assert.False(score.Succeeded);
        Assert.Null(score.CalibrationDistance);
    }

    /// <summary>
    /// The extension-block case: correct code, and the model emitted four Critical findings in one
    /// round. Resistance already said it fell for the bait; precision is what makes the other three
    /// visible, because on a trap everything outside the list counts.
    /// </summary>
    [Fact]
    public void OnATrap_EverythingOutsideTheListCountsAgainstPrecision()
    {
        var trap = new GoldenCase(
            "extension-block", "d.diff", "g.md",
            new ExpectNoFinding("src/Domain/MoneyExtensions.cs", "extension(decimal value)"));

        var score = GoldenScorer.Score(
            [new("src/Domain/MoneyExtensions.cs", "extension(decimal value)", Severity.Critical,
                 Category.Bug, "This syntax is invalid", "Use a static class"),
             Found("Unrelated remark", Severity.Critical, "src/Domain/MoneyExtensions.cs")],
            trap);

        Assert.False(score.Succeeded);
        Assert.Equal(1, score.Count(FindingVerdict.Bait));
        Assert.Equal(1, score.Count(FindingVerdict.Unforeseen));
        Assert.Equal(0, score.PrecisionCorrect);
        Assert.Equal(2, score.PrecisionCounted);
    }

    /// <summary>
    /// The aggregation rule, which is where the two axes deliberately disagree: detection counts
    /// rounds, precision counts findings. Two rounds here, both of which caught the bug, but one
    /// said three things that should not have been said. Detection is a clean 2/2; precision is
    /// not, and averaging the two rounds' rates (100% and 25%) would have reported 62,5% instead
    /// of the 40% that the findings actually support.
    /// </summary>
    [Fact]
    public void AcrossRounds_DetectionCountsRoundsAndPrecisionCountsFindings()
    {
        var clean = new ReviewResult("ok", [Found("SQL injection through concatenation")]);
        var noisy = new ReviewResult("ok",
        [
            Found("SQL injection through concatenation"),
            Found("The query execution inherits the SQL injection above"),
            Found("The same SQL injection reaches the caller"),
            Found("And the sql string is still built by concatenation"),
        ]);

        var (result, labels) = GoldenEvaluator.ScoreOneSide(Detection(), "v3", [clean, noisy]);

        Assert.Equal(2, result.Successes);
        Assert.Equal(2, result.Runs);
        Assert.Equal(2, result.PrecisionCorrect);
        Assert.Equal(5, result.PrecisionCounted);
        Assert.All(labels, l => Assert.Contains("caught", l));
    }

    /// <summary>
    /// Calibration is kept as the list of distances, not as a mean, because the mean alone lies:
    /// these two rounds are one level too high and one too low, and their average is zero, which
    /// would read as perfect calibration when neither round was right.
    /// </summary>
    [Fact]
    public void CalibrationDistances_AreKeptPerRoundSoOppositeErrorsDoNotCancel()
    {
        var understated = new ReviewResult("ok", [Found("SQL injection", Severity.Warning)]);
        var exact = new ReviewResult("ok", [Found("SQL injection", Severity.Critical)]);

        var (result, _) = GoldenEvaluator.ScoreOneSide(Detection(), "v3", [understated, exact]);

        Assert.Equal([-1, 0], result.CalibrationDistances);
    }

    /// <summary>
    /// The collection-spread case resisted 5 of 5 and still said two things about correct code.
    /// A trap that emits nothing contributes to neither side of the rate, so a silent round is not
    /// scored as perfect precision — it is simply not a data point.
    /// </summary>
    [Fact]
    public void ATrapThatEmitsNothing_ContributesToNeitherSideOfTheRate()
    {
        var trap = new GoldenCase(
            "collection-spread", "d.diff", "g.md",
            new ExpectNoFinding("src/Notifications/RecipientResolver.cs", "[.. configured]"));

        var score = GoldenScorer.Score([], trap);

        Assert.True(score.Succeeded);
        Assert.Equal(0, score.PrecisionCorrect);
        Assert.Equal(0, score.PrecisionCounted);
    }

    /// <summary>
    /// Findings the grounding dropped are summed per case. They never reach a verdict, so this
    /// count is the only way to tell a model that cited the wrong line from one that saw nothing.
    /// </summary>
    [Fact]
    public void DiscardedFindings_AreSummedAcrossRounds()
    {
        var missed = new ReviewResult("ok", [], DiscardedFindings: 1);
        var caught = new ReviewResult("ok", [Found("SQL injection through concatenation")], DiscardedFindings: 2);

        var (result, _) = GoldenEvaluator.ScoreOneSide(Detection(), "v3", [missed, caught]);

        Assert.Equal(3, result.DiscardedFindings);
        Assert.Equal(1, result.Successes);
    }
}
