using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Judge;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The judge's two-stage persistence (fixing the loss described in the bug report): each
/// execution is appended to a real file the moment it comes back, and a crash that truncates
/// the file mid-write must not take earlier, already-durable lines down with it.
/// </summary>
public class JudgeResultsStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"cra-judge-results-{Guid.NewGuid():N}.jsonl");

    private const string Model = "claude-sonnet-4-6";
    private const string Rubric = "v2";

    private static JudgeExecutionRecord Record(
        string diff, int pairIndex, int runIndex, string reasoning = "why",
        string? judgeModel = Model, string? rubricVersion = Rubric) =>
        new(diff, "App.cs", pairIndex, runIndex, judgeModel, rubricVersion,
            new PairJudgeOutcome(
                new PairJudgment(reasoning, Verdict.A, Verdict.A, Verdict.A, Verdict.A, Verdict.A, Verdict.A),
                new JudgePairing("v3", "v5"), 0.001m, 100, 300, 120));

    [Fact]
    public void Load_OfAMissingFile_ReturnsEmpty()
    {
        Assert.Empty(JudgeResultsStore.Load(TempPath()));
    }

    [Fact]
    public void AppendThenLoad_RoundTripsEveryRecord()
    {
        var path = TempPath();
        try
        {
            JudgeResultsStore.Append(path, Record("diff-a", 0, 0));
            JudgeResultsStore.Append(path, Record("diff-a", 0, 1));
            JudgeResultsStore.Append(path, Record("diff-b", 0, 0));

            var records = JudgeResultsStore.Load(path);

            Assert.Equal(3, records.Count);
            Assert.Equal(["diff-a", "diff-a", "diff-b"], records.Select(r => r.Diff));
            Assert.Equal([0, 1, 0], records.Select(r => r.RunIndex));
            // The nested judgment/pairing survive the round trip, not just the flat fields.
            Assert.Equal(Verdict.A, records[0].Outcome.Judgment.Correctness);
            Assert.Equal("v3", records[0].Outcome.Pairing.SlotA);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The exact failure mode a crash produces: a file whose last line is cut off mid-write.
    /// Every earlier, complete line must still load — that is the entire point of writing one
    /// line at a time instead of one JSON array.
    /// </summary>
    [Fact]
    public void Load_SkipsATruncatedTrailingLine_ButKeepsEverythingBeforeIt()
    {
        var path = TempPath();
        try
        {
            JudgeResultsStore.Append(path, Record("diff-a", 0, 0));
            JudgeResultsStore.Append(path, Record("diff-a", 0, 1));
            File.AppendAllText(path, "{\"diff\":\"diff-a\",\"pairIndex\":0,\"runIndex\":2,\"outc"); // cut off

            var records = JudgeResultsStore.Load(path);

            Assert.Equal(2, records.Count);
            Assert.Equal([0, 1], records.Select(r => r.RunIndex));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CompletedKeys_ExtractsTheDiffPairAndRunTriples()
    {
        var records = new[] { Record("diff-a", 0, 0), Record("diff-a", 1, 0), Record("diff-b", 0, 2) };

        var keys = JudgeResultsStore.CompletedKeys(records, Model, Rubric);

        Assert.Equal(3, keys.Count);
        Assert.Contains(("diff-a", 0, 0), keys);
        Assert.Contains(("diff-a", 1, 0), keys);
        Assert.Contains(("diff-b", 0, 2), keys);
    }

    /// <summary>
    /// The bug this key exists to prevent: a run that switched rubric found the previous
    /// rubric's judgments on disk, counted them as already paid for, and aggregated the two
    /// together without saying so. A judgment made under another configuration answers a
    /// different question, so it must not close out this run's work.
    /// </summary>
    [Fact]
    public void CompletedKeys_IgnoresRecordsJudgedUnderAnotherRubricOrModel()
    {
        var records = new[]
        {
            Record("diff-a", 0, 0),
            Record("diff-a", 1, 0, rubricVersion: "v1"),
            Record("diff-b", 0, 0, judgeModel: "claude-opus-4-1"),
        };

        var keys = JudgeResultsStore.CompletedKeys(records, Model, Rubric);

        Assert.Equal([("diff-a", 0, 0)], keys);
    }

    /// <summary>
    /// Lines written before the configuration fields existed carry neither, so they match no
    /// configuration and are re-judged rather than counted as this run's.
    /// </summary>
    [Fact]
    public void CompletedKeys_IgnoresRecordsThatDoNotRecordTheirConfiguration()
    {
        var records = new[] { Record("diff-a", 0, 0, judgeModel: null, rubricVersion: null) };

        Assert.Empty(JudgeResultsStore.CompletedKeys(records, Model, Rubric));
    }

    [Fact]
    public void ForConfiguration_KeepsOnlyTheMatchingRecordsAndLeavesTheRestOnDisk()
    {
        var records = new[]
        {
            Record("diff-a", 0, 0),
            Record("diff-a", 1, 0, rubricVersion: "v1"),
        };

        var mine = JudgeResultsStore.ForConfiguration(records, Model, Rubric);

        Assert.Single(mine);
        Assert.Equal("diff-a", mine[0].Diff);
        Assert.Equal(0, mine[0].PairIndex);
        // Filtering is a read: the foreign record is still in the caller's collection.
        Assert.Equal(2, records.Length);
    }
}
