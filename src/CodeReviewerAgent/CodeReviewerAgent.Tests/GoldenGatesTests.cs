using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The approval criterion of ADR-015: a clean round, a case approved with 4 clean rounds in 5, and
/// five gates for the model, all of which have to pass.
/// </summary>
public class GoldenGatesTests
{
    private static readonly ExpectFinding Injection = new(
        "src/Data/UserRepository.cs", Category.Security, ["injection", "sql", "parameter"], Severity.Critical);

    private static readonly GoldenCase SqlInjection = new("sql-injection", "d.diff", "g.md", Injection);

    private static Finding Found(string problem, Severity severity = Severity.Critical) =>
        new("src/Data/UserRepository.cs", "snippet", severity, Category.Security, problem, "fix it");

    // --- A clean round ---

    [Fact]
    public void FoundOnceAtTheRightSeverity_IsClean()
    {
        var score = GoldenScorer.Score([Found("SQL injection through concatenation")], SqlInjection);

        Assert.True(score.IsClean);
    }

    [Fact]
    public void TheSameProblemSaidTwice_IsNotClean()
    {
        var score = GoldenScorer.Score(
            [Found("SQL injection through concatenation"),
             Found("The query execution follows from the SQL injection on the preceding line")],
            SqlInjection);

        Assert.True(score.Succeeded);
        Assert.False(score.IsClean);
    }

    [Fact]
    public void FoundButAtTheWrongSeverity_IsNotClean()
    {
        var score = GoldenScorer.Score([Found("SQL injection", Severity.Info)], SqlInjection);

        Assert.True(score.Succeeded);
        Assert.False(score.IsClean);
    }

    [Fact]
    public void AMissedRound_IsNotClean()
    {
        var score = GoldenScorer.Score([Found("Consider renaming this method", Severity.Info)], SqlInjection);

        Assert.False(score.IsClean);
    }

    /// <summary>
    /// An unforeseen remark on a detection case carries no penalty anywhere else, because the
    /// case's list may be what is incomplete. It would be inconsistent for it to spoil the round.
    /// </summary>
    [Fact]
    public void AnUnforeseenRemarkOnADetectionCase_DoesNotSpoilTheRound()
    {
        var score = GoldenScorer.Score(
            [Found("SQL injection through concatenation"),
             Found("Reverting to the in-memory LINQ query would be simpler", Severity.Warning)],
            SqlInjection);

        Assert.True(score.IsClean);
    }

    // --- The control: collection-spread, 2026-08-16 ---

    private static readonly GoldenCase CollectionSpread = new(
        "collection-spread", "d.diff", "g.md",
        new ExpectNoFinding("src/Notifications/RecipientResolver.cs", "return [.. configured, _options.FallbackAddress];"));

    /// <summary>
    /// The five rounds exactly as the reference report has them: three silent, and two that each
    /// said one thing about correct code. The old ruler scored it 5/5. ADR-015 records it as 3
    /// clean rounds in 5, and the floors were fixed on that reading; if this ever returns 4 or 5,
    /// the code has drifted from the rescoring the floors came from, and the floors no longer hold.
    /// </summary>
    [Fact]
    public void CollectionSpread_HasThreeCleanRoundsInFive_AndIsNotApproved()
    {
        var silent = new ReviewResult("ok", []);
        var nullCheck = new ReviewResult("ok",
        [
            new("src/Notifications/RecipientResolver.cs", "return [_options.FallbackAddress];",
                Severity.Warning, Category.Bug,
                "_options.FallbackAddress is accessed without null checking. If FallbackAddress is null, it will be included in the array, potentially causing null reference exceptions downstream.",
                "Check if _options.FallbackAddress is null before including it in the array."),
        ]);
        var deduplication = new ReviewResult("ok",
        [
            new("src/Notifications/RecipientResolver.cs",
                "public string[] Merge(string[] primary, string[] secondary) => [.. primary, .. secondary];",
                Severity.Warning, Category.Bug,
                "The Merge method concatenates two email address arrays without deduplication, which could result in duplicate recipients.",
                "Consider deduplicating the combined array using LINQ's Distinct() method."),
        ]);

        var (result, _) = GoldenEvaluator.ScoreOneSide(
            CollectionSpread, "v3", [silent, silent, nullCheck, silent, deduplication]);

        Assert.Equal(5, result.Successes);
        Assert.Equal(3, result.CleanRounds);
        Assert.False(GoldenGates.CaseApproved(result));
    }

    // --- A case ---

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(4, 5, true)]
    [InlineData(3, 5, false)]
    [InlineData(8, 10, true)]
    [InlineData(7, 10, false)]
    [InlineData(3, 3, true)]
    [InlineData(2, 3, false)]
    public void ACaseIsApprovedWithFourCleanRoundsInFive(int clean, int runs, bool approved)
    {
        var result = new GoldenCaseResult("case", GoldenKind.Detection, null, "v3", runs, runs, null, CleanRounds: clean);

        Assert.Equal(approved, GoldenGates.CaseApproved(result));
    }

    // --- The model ---

    /// <summary>
    /// A run at the reference's rates: detection 40/60, trap resistance 8/15, precision 88,2%,
    /// exact calibration 31 of 40, 13 findings over 15 trap rounds. The individual counts are
    /// chosen to hit those rates; the rates are what the floors were fixed on.
    /// </summary>
    private static GoldenCaseResult[] Run(
        int detected = 40, int precisionCorrect = 45, int precisionCounted = 51,
        int exact = 31, int resisted = 8, int trapFindings = 13) =>
    [
        new("detection", GoldenKind.Detection, null, "v3", detected, 60, null,
            precisionCorrect, precisionCounted,
            [.. Enumerable.Repeat(0, exact), .. Enumerable.Repeat(1, detected - exact)]),
        new("trap", GoldenKind.Trap, null, "v3", resisted, 15, null, 0, trapFindings),
    ];

    private static IEnumerable<string> Failed(ModelVerdict verdict) =>
        verdict.Gates.Where(g => !g.Passed).Select(g => g.Name);

    [Fact]
    public void TheReferencePassesAllFiveGates()
    {
        var verdict = GoldenGates.Judge(Run());

        Assert.Equal(5, verdict.Gates.Count);
        Assert.Empty(Failed(verdict));
        Assert.True(verdict.Approved);
    }

    [Fact]
    public void ExactlyOnTheFloor_Passes()
    {
        var verdict = GoldenGates.Judge(Run(detected: 36, exact: 36));

        Assert.True(verdict.Approved);
    }

    [Fact]
    public void OneGateBelowTheFloor_FailsTheModel()
    {
        var verdict = GoldenGates.Judge(Run(detected: 35, exact: 35));

        Assert.Equal(["Detection"], Failed(verdict));
        Assert.False(verdict.Approved);
    }

    /// <summary>The ADR's own example: good detection does not buy back bad precision.</summary>
    [Fact]
    public void HighDetectionDoesNotCompensateLowPrecision()
    {
        var verdict = GoldenGates.Judge(Run(detected: 45, exact: 45, precisionCorrect: 15, precisionCounted: 50));

        Assert.Equal(["Precision"], Failed(verdict));
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public void TrapNoise_IsAtMostOneFindingPerTrapRound(int trapFindings, bool passes)
    {
        var verdict = GoldenGates.Judge(Run(trapFindings: trapFindings));

        Assert.Equal(passes, !Failed(verdict).Contains("Trap noise"));
    }

    [Fact]
    public void AGateWithNothingToMeasure_Fails()
    {
        var verdict = GoldenGates.Judge(Run(detected: 0, exact: 0, precisionCorrect: 0, precisionCounted: 0));

        Assert.Contains("Exact calibration", Failed(verdict));
    }
}
