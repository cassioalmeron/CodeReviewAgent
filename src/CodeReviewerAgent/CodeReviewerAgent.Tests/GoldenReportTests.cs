using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The golden footer. Two rules carry the whole redesign: detection and trap resistance are
/// never added together, and the report says which harness condition produced the numbers.
/// </summary>
public class GoldenReportTests
{
    private static ReviewResult Review(string? skills) =>
        new("summary", [], "fake", "fake-model", "v3", 0m, 0, 0, 0, "diff", skills);

    private static GoldenCaseResult Detection(
        string name, int successes, int runs = 3, string? since = null, string promptVersion = "v3") =>
        new(name, GoldenKind.Detection, since, promptVersion, successes, runs, null);

    private static GoldenCaseResult Trap(
        string name, int successes, int runs = 3, string? since = null, string promptVersion = "v3") =>
        new(name, GoldenKind.Trap, since, promptVersion, successes, runs, null);

    [Fact]
    public void ReportsDetectionAndTrapResistanceSeparately()
    {
        var results = new[] { Detection("bug", 3), Detection("other", 2), Trap("clean", 1) };

        var footer = GoldenEvaluatorReport.BuildFooter(results, GoldenCondition.From([], "off"));

        Assert.Contains("Detection", footer);
        Assert.Contains("5/6", footer);          // detections only
        Assert.Contains("Trap resistance", footer);
        Assert.Contains("1/3", footer);          // traps only
        Assert.DoesNotContain("6/9", footer);    // the sum must never appear
    }

    [Fact]
    public void OmitsTrapResistanceWhenThereAreNoTraps()
    {
        var footer = GoldenEvaluatorReport.BuildFooter([Detection("bug", 3)], GoldenCondition.From([], "off"));

        Assert.Contains("Detection", footer);
        Assert.DoesNotContain("Trap resistance", footer);
    }

    /// <summary>
    /// The pairwise case (T1 of US-011): two prompt versions over the same cases must never be
    /// blended into one rate, which is exactly the corruption the per-side split exists to
    /// prevent.
    /// </summary>
    [Fact]
    public void SplitsDetectionRatesByPromptVersionWhenComparingTwoSides()
    {
        var results = new[] { Detection("bug", 3, promptVersion: "v3"), Detection("bug", 1, promptVersion: "v5") };

        var footer = GoldenEvaluatorReport.BuildFooter(results, GoldenCondition.From([], "off"));

        Assert.Contains("**Detection** (v3) 3/3", footer);
        Assert.Contains("**Detection** (v5) 1/3", footer);
        Assert.DoesNotContain("4/6", footer); // the two sides must never be summed
    }

    [Fact]
    public void DoesNotLabelTheVersionWhenOnlyOneSideRan()
    {
        var footer = GoldenEvaluatorReport.BuildFooter([Detection("bug", 3)], GoldenCondition.From([], "off"));

        Assert.Contains("**Detection** 3/3", footer);
        Assert.DoesNotContain("v3", footer);
    }

    [Fact]
    public void BreaksTheRatesDownByCSharpVersion()
    {
        var results = new[]
        {
            Detection("old", 3),
            Detection("modern", 1, since: "C# 12"),
            Trap("extension", 0, since: "C# 14"),
        };

        var footer = GoldenEvaluatorReport.BuildFooter(results, GoldenCondition.From([], "off"));

        Assert.Contains("C# 12", footer);
        Assert.Contains("C# 14", footer);
        Assert.Contains("agnostic", footer);
    }

    /// <summary>
    /// The breakdown is a ladder: it exists to show <em>which</em> constructs a model handles
    /// badly, so it has to read oldest-first. A plain string sort puts "C# 8" after "C# 14".
    /// </summary>
    [Fact]
    public void OrdersTheVersionsChronologicallyNotAlphabetically()
    {
        var results = new[]
        {
            Detection("modern", 1, since: "C# 14"),
            Detection("older", 1, since: "C# 8"),
            Detection("mid", 1, since: "C# 11"),
            Detection("plain", 1),
        };

        var footer = GoldenEvaluatorReport.BuildFooter(results, GoldenCondition.From([], "off"));

        var order = new[] { "agnostic", "C# 8", "C# 11", "C# 14" }.Select(v => footer.IndexOf($"| {v} |")).ToList();
        Assert.All(order, i => Assert.True(i > 0, "every version group should appear in the table"));
        Assert.Equal(order.OrderBy(i => i), order);
    }

    // --- Condition (decisions 11 and 12) ---

    [Fact]
    public void LabelsTheBaselineCondition()
    {
        var footer = GoldenEvaluatorReport.BuildFooter([Detection("bug", 3)], GoldenCondition.From([], "off"));

        Assert.Contains("baseline", footer);
        Assert.Contains("SKILLS=off", footer);
    }

    [Fact]
    public void LabelsTheHarnessConditionWithTheSkillsThatWereActive()
    {
        var reviews = new[] { Review("csharp"), Review("csharp"), Review("csharp,react") };

        var footer = GoldenEvaluatorReport.BuildFooter([Detection("bug", 3)], GoldenCondition.From(reviews, "all"));

        Assert.Contains("harness", footer);
        Assert.Contains("csharp (3/3)", footer);
        Assert.Contains("react (1/3)", footer);
    }

    /// <summary>
    /// Decision 12: a run whose skill selection came back unreadable selected nothing, so it is
    /// baseline wearing the harness label. Averaging it in dilutes the very delta the two
    /// conditions exist to measure, so the footer has to say it happened.
    /// </summary>
    [Fact]
    public void FlagsHarnessRoundsThatEndedUpWithNoSkill()
    {
        var reviews = new[] { Review("csharp"), Review(null), Review("") };

        var footer = GoldenEvaluatorReport.BuildFooter([Detection("bug", 3)], GoldenCondition.From(reviews, "all"));

        Assert.Contains("2/3", footer);
        Assert.Contains("no skill", footer);
    }

    // --- ADR-015 ---

    /// <summary>
    /// The trap precision rate could only ever print 0/N, so it is gone, and what it was really
    /// counting is printed instead: findings per trap round.
    /// </summary>
    [Fact]
    public void ReportsTrapNoiseInsteadOfTrapPrecision()
    {
        var trap = new GoldenCaseResult("clean", GoldenKind.Trap, null, "v3", 8, 15, null, 0, 13);

        var footer = GoldenEvaluatorReport.BuildFooter([Detection("bug", 3), trap], GoldenCondition.From([], "off"));

        Assert.Contains("**Trap noise** 13 finding(s) in 15 trap rounds", footer);
        Assert.DoesNotContain("Precision (traps)", footer);
    }

    [Fact]
    public void PrintsEveryGateAndTheVerdict()
    {
        var results = new[]
        {
            new GoldenCaseResult("bug", GoldenKind.Detection, null, "v3", 5, 5, null, 5, 5, [0, 0, 0, 0, 0], CleanRounds: 5),
            new GoldenCaseResult("clean", GoldenKind.Trap, null, "v3", 5, 5, null, 0, 0, CleanRounds: 5),
        };

        var footer = GoldenEvaluatorReport.BuildFooter(results, GoldenCondition.From([], "off"));

        Assert.Contains("## Approval", footer);
        Assert.Contains("Cases approved (at least 4 clean rounds in 5): 2/2", footer);
        foreach (var gate in new[] { "Detection", "Trap resistance", "Precision", "Exact calibration", "Trap noise" })
            Assert.Contains($"| {gate} |", footer);
        Assert.Contains("**Model approved.**", footer);
    }

    /// <summary>Two of the five gates are measured on traps, so a run without them cannot be judged.</summary>
    [Fact]
    public void DoesNotJudgeARunWithoutTraps()
    {
        var footer = GoldenEvaluatorReport.BuildFooter([Detection("bug", 3)], GoldenCondition.From([], "off"));

        Assert.Contains("Not judged", footer);
        Assert.DoesNotContain("Model approved", footer);
        Assert.DoesNotContain("Model not approved", footer);
    }

    /// <summary>
    /// PASS keeps meaning "found it every time". The clean count beside it is what the case is
    /// approved on, and the two can disagree: that disagreement is the point of the new line.
    /// </summary>
    [Fact]
    public void ACaseCanPassAndStillNotBeApproved()
    {
        var result = new GoldenCaseResult("collection-spread", GoldenKind.Trap, "C# 12", "v3", 5, 5, null, CleanRounds: 3);

        var line = GoldenEvaluatorReport.FormatLine(result);

        Assert.StartsWith("[PASS]", line);
        Assert.Contains("clean 3/5, not approved", line);
    }

    [Fact]
    public void ShowsDiscardedFindingsOnTheCaseLineOnlyWhenThereAreAny()
    {
        var none = new GoldenCaseResult("case", GoldenKind.Detection, null, "v3", 0, 5, "missed");
        var some = none with { DiscardedFindings = 4 };

        Assert.DoesNotContain("discarded", GoldenEvaluatorReport.FormatLine(none));
        Assert.Contains("discarded 4", GoldenEvaluatorReport.FormatLine(some));
    }

    /// <summary>Zero is printed too: between models, zero is a result, and a missing line reads as "not measured".</summary>
    [Fact]
    public void ReportsDiscardedFindingsInTheFooterEvenAtZero()
    {
        var footer = GoldenEvaluatorReport.BuildFooter([Detection("bug", 3)], GoldenCondition.From([], "off"));

        Assert.Contains("**Discarded** 0 finding(s)", footer);
    }

    [Fact]
    public void DoesNotFlagMissingSkillsInTheBaselineCondition()
    {
        var reviews = new[] { Review(null), Review(null) };

        var footer = GoldenEvaluatorReport.BuildFooter([Detection("bug", 3)], GoldenCondition.From(reviews, "off"));

        Assert.DoesNotContain("no skill", footer);
    }
}
