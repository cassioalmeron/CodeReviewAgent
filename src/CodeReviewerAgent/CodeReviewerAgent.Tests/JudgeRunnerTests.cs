using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Judge;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The pure pairing logic behind the pairwise judge (plan 015, T3/T4): splitting one diff's
/// reviews by prompt version, zipping them positionally into pairs, and mapping a slot drawing
/// back onto the two reviews being compared. No LLM call is involved — <see cref="Judge"/> is
/// exercised separately, against a real client.
/// </summary>
public class JudgeRunnerTests
{
    private static ReviewResult Review(string promptVersion, string label, string diff = "diff") =>
        new(label, [], "fake", "fake-model", promptVersion, 0m, 0, 0, 0, diff);

    [Fact]
    public void Pair_SplitsByVersionAndZipsRunIWithRunI()
    {
        var reviews = new[]
        {
            Review("v3", "v3-run1"),
            Review("v3", "v3-run2"),
            Review("v5", "v5-run1"),
            Review("v5", "v5-run2"),
        };

        var pairs = JudgeRunner.Pair(reviews);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(("v3-run1", "v5-run1"), (pairs[0].A.Summary, pairs[0].B.Summary));
        Assert.Equal(("v3-run2", "v5-run2"), (pairs[1].A.Summary, pairs[1].B.Summary));
    }

    [Fact]
    public void Pair_WithOnlyOnePromptVersion_Throws()
    {
        var reviews = new[] { Review("v3", "a"), Review("v3", "b") };

        var error = Assert.Throws<InvalidOperationException>(() => JudgeRunner.Pair(reviews));

        Assert.Contains("exactly two prompt versions", error.Message);
    }

    [Fact]
    public void Pair_WithThreePromptVersions_Throws()
    {
        var reviews = new[] { Review("v3", "a"), Review("v4", "b"), Review("v5", "c") };

        Assert.Throws<InvalidOperationException>(() => JudgeRunner.Pair(reviews));
    }

    [Fact]
    public void Pair_WithUnevenRunCounts_Throws()
    {
        var reviews = new[] { Review("v3", "a1"), Review("v3", "a2"), Review("v5", "b1") };

        var error = Assert.Throws<InvalidOperationException>(() => JudgeRunner.Pair(reviews));

        Assert.Contains("Uneven runs", error.Message);
    }

    [Fact]
    public void AssignSlots_MapsBothDrawingsCorrectly()
    {
        var a = Review("v3", "a");
        var b = Review("v5", "b");

        var noSwap = JudgeRunner.AssignSlots(a, b, swap: false);
        var swapped = JudgeRunner.AssignSlots(a, b, swap: true);

        Assert.Equal((a, b), (noSwap.SlotA, noSwap.SlotB));
        Assert.Equal((b, a), (swapped.SlotA, swapped.SlotB));
    }

    // --- PlanPairs: one entry per (diff, pair), independent of what is already recorded ---

    private const string DiffA =
        "diff --git a/App.cs b/App.cs\n--- a/App.cs\n+++ b/App.cs\n@@ -1 +1,2 @@\n line\n+added\n";
    private const string DiffB =
        "diff --git a/B.cs b/B.cs\n--- a/B.cs\n+++ b/B.cs\n@@ -1 +1,2 @@\n line\n+added\n";

    [Fact]
    public void PlanPairs_GroupsByDiffAndLabelsEachWithItsFirstFile()
    {
        var reviews = new[]
        {
            Review("v3", "a-1", DiffA), Review("v5", "b-1", DiffA),
            Review("v3", "a-2", DiffB), Review("v5", "b-2", DiffB),
        };

        var plan = JudgeRunner.PlanPairs(reviews);

        Assert.Equal(2, plan.Count);
        Assert.Contains(plan, p => p.Diff == DiffA && p.Label == "App.cs" && p.PairIndex == 0);
        Assert.Contains(plan, p => p.Diff == DiffB && p.Label == "B.cs" && p.PairIndex == 0);
    }

    [Fact]
    public void PlanPairs_WithMultipleRunsPerSide_AssignsSequentialPairIndexesWithinACase()
    {
        var reviews = new[]
        {
            Review("v3", "a-1", DiffA), Review("v3", "a-2", DiffA),
            Review("v5", "b-1", DiffA), Review("v5", "b-2", DiffA),
        };

        var plan = JudgeRunner.PlanPairs(reviews);

        Assert.Equal(2, plan.Count);
        Assert.Equal([0, 1], plan.Select(p => p.PairIndex).OrderBy(i => i));
    }

    // --- Pending: the plan minus whatever is already recorded — testable against a fixture of
    // completed keys, exactly what a resumed run reads back from judge-results.jsonl ---

    [Fact]
    public void Pending_WithNothingCompleted_ReturnsEveryRun()
    {
        var plan = JudgeRunner.PlanPairs([Review("v3", "a", DiffA), Review("v5", "b", DiffA)]);

        var pending = JudgeRunner.Pending(plan, judgeRuns: 3, completed: new HashSet<(string, int, int)>());

        Assert.Equal(3, pending.Count);
        Assert.Equal([0, 1, 2], pending.Select(p => p.RunIndex).OrderBy(i => i));
    }

    [Fact]
    public void Pending_SkipsExactlyTheCompletedRuns()
    {
        var plan = JudgeRunner.PlanPairs([Review("v3", "a", DiffA), Review("v5", "b", DiffA)]);
        var completed = new HashSet<(string Diff, int PairIndex, int RunIndex)> { (DiffA, 0, 1) };

        var pending = JudgeRunner.Pending(plan, judgeRuns: 3, completed);

        Assert.Equal([0, 2], pending.Select(p => p.RunIndex).OrderBy(i => i));
    }

    /// <summary>
    /// If every run of every pair is already recorded, resuming pays for nothing at all — which
    /// is exactly how a report can be regenerated for free once a run has fully completed.
    /// </summary>
    [Fact]
    public void Pending_WithEveryRunAlreadyCompleted_ReturnsNothing()
    {
        var plan = JudgeRunner.PlanPairs([Review("v3", "a", DiffA), Review("v5", "b", DiffA)]);
        var completed = new HashSet<(string Diff, int PairIndex, int RunIndex)>
        {
            (DiffA, 0, 0), (DiffA, 0, 1), (DiffA, 0, 2),
        };

        var pending = JudgeRunner.Pending(plan, judgeRuns: 3, completed);

        Assert.Empty(pending);
    }

    [Fact]
    public void Pending_OnlySkipsRunsOfTheMatchingDiffAndPairIndex()
    {
        var plan = JudgeRunner.PlanPairs([Review("v3", "a", DiffA), Review("v5", "b", DiffA)]);
        // A completed key for a different diff and a different pair index must not skip anything here.
        var completed = new HashSet<(string Diff, int PairIndex, int RunIndex)> { (DiffB, 0, 0), (DiffA, 1, 0) };

        var pending = JudgeRunner.Pending(plan, judgeRuns: 2, completed);

        Assert.Equal(2, pending.Count);
    }
}
