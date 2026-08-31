using CodeReviewerAgent.Core;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    /// <summary>
    /// The pairwise judge report (plan 015, T5/T6): majority vote over categorical verdicts, and a
    /// verdict table instead of the absolute path's average table.
    /// </summary>
    public class PairwiseJudgeReportTests
    {
        // --- Majority vote (T5): 3-0, 2-1 and 1-1-1 each produce the expected aggregate ---

        [Fact]
        public void MajorityVote_ThreeZero_PicksTheUnanimousWinner()
        {
            var tally = PairwiseJudgeReport.MajorityVote(["v5", "v5", "v5"]);

            Assert.Equal("v5", tally.Winner);
            Assert.False(tally.IsSplit);
        }

        [Fact]
        public void MajorityVote_TwoOne_PicksTheMajority()
        {
            var tally = PairwiseJudgeReport.MajorityVote(["v5", "v5", "v3"]);

            Assert.Equal("v5", tally.Winner);
            Assert.False(tally.IsSplit);
        }

        [Fact]
        public void MajorityVote_OneOneOne_IsReportedAsATieAndFlagged()
        {
            var tally = PairwiseJudgeReport.MajorityVote(["v3", "v5", "tie"]);

            Assert.Equal("tie", tally.Winner);
            Assert.True(tally.IsSplit); // a genuine three-way split, not the judge calling it even
        }

        [Fact]
        public void MajorityVote_TieWinsOutright_IsNotFlagged()
        {
            var tally = PairwiseJudgeReport.MajorityVote(["tie", "tie", "v3"]);

            Assert.Equal("tie", tally.Winner);
            Assert.False(tally.IsSplit); // the judge repeatedly called it even — a real, unflagged tie
        }

        // --- Report shape (T6): a verdict table, not an average table ---

        private static PairJudgeOutcome Outcome(Verdict verdict, string slotA = "v3", string slotB = "v5") =>
            new(new PairJudgment("because", verdict, verdict, verdict, verdict, verdict, verdict),
                new JudgePairing(slotA, slotB), 0.001m, 100, 300, 120);

        private const string Diff =
            "diff --git a/App.cs b/App.cs\n--- a/App.cs\n+++ b/App.cs\n@@ -1 +1,2 @@\n line\n+added\n";

        [Fact]
        public void Generate_RendersAVerdictTable_NotAnAverageTable()
        {
            var pairs = new[] { new JudgedPair(Diff, [Outcome(Verdict.A), Outcome(Verdict.A), Outcome(Verdict.B)]) };

            var report = PairwiseJudgeReport.Generate(pairs, "claude-sonnet-4-6", "v2", judgeRuns: 3);

            Assert.Contains("| Criterion | Verdict | Votes |", report);
            Assert.DoesNotContain("| Criterion | Avg |", report);
            Assert.DoesNotContain("## Averages", report);
            // Slot A/B resolve to prompt versions in the tally, e.g. "v3 2 · v5 1".
            Assert.Contains("v3 2", report);
            Assert.Contains("v5 1", report);
        }

        [Fact]
        public void Generate_IncludesTheHeaderAndThePairingRule()
        {
            var pairs = new[] { new JudgedPair(Diff, [Outcome(Verdict.A)]) };

            var report = PairwiseJudgeReport.Generate(pairs, "claude-sonnet-4-6", "v2", judgeRuns: 1);

            Assert.Contains("judge claude-sonnet-4-6, rubric v2", report);
            Assert.Contains("Pairing is positional", report);
        }

        [Fact]
        public void Generate_PrintsReasoningPerPair()
        {
            var pairs = new[] { new JudgedPair(Diff, [Outcome(Verdict.A)]) };

            var report = PairwiseJudgeReport.Generate(pairs, "claude-sonnet-4-6", "v2", judgeRuns: 1);

            Assert.Contains("### Pair 1", report);
            Assert.Contains("because", report); // the reasoning text
        }

        [Fact]
        public void Generate_IncludesAnOverallTallyAcrossAllPairs()
        {
            var pairs = new[]
            {
                new JudgedPair(Diff, [Outcome(Verdict.A)]),
                new JudgedPair("diff --git a/B.cs b/B.cs", [Outcome(Verdict.B)]),
            };

            var report = PairwiseJudgeReport.Generate(pairs, "claude-sonnet-4-6", "v2", judgeRuns: 1);

            Assert.Contains("# Overall", report);
            Assert.Contains("Across 2 pairs, 2 executions.", report);
        }

        // --- Partial runs: the run stopped early, and the report has to say so ---

        [Fact]
        public void Generate_WithoutPartial_NeverMentionsAPartialRun()
        {
            var pairs = new[] { new JudgedPair(Diff, [Outcome(Verdict.A)]) };

            var report = PairwiseJudgeReport.Generate(pairs, "claude-sonnet-4-6", "v2", judgeRuns: 1);

            Assert.DoesNotContain("Partial run", report);
        }

        [Fact]
        public void Generate_WithPartial_StatesHowMuchMadeItToDisk()
        {
            var pairs = new[] { new JudgedPair(Diff, [Outcome(Verdict.A)]) };
            var partial = new PartialRun(PairsRecorded: 24, PairsPlanned: 30, ExecutionsRecorded: 72, ExecutionsPlanned: 90);

            var report = PairwiseJudgeReport.Generate(pairs, "claude-sonnet-4-6", "v2", judgeRuns: 3, partial);

            Assert.Contains("Partial run", report);
            Assert.Contains("24/30 pairs", report);
            Assert.Contains("72/90", report);
        }

        // --- Rebuilding from durable records (the fix): the report has to come out the same whether
        // it is fed the in-memory pairs of a clean run or records reloaded from disk ---

        [Fact]
        public void ToJudgedPairs_GroupsByDiffAndPairIndex_OrderedByRunIndex()
        {
            var records = new[]
            {
                new JudgeExecutionRecord(Diff, "App.cs", PairIndex: 0, RunIndex: 1, "claude-sonnet-4-6", "v2", Outcome(Verdict.A)),
                new JudgeExecutionRecord(Diff, "App.cs", PairIndex: 0, RunIndex: 0, "claude-sonnet-4-6", "v2", Outcome(Verdict.B)),
                new JudgeExecutionRecord(Diff, "App.cs", PairIndex: 1, RunIndex: 0, "claude-sonnet-4-6", "v2", Outcome(Verdict.A)),
            };

            var pairs = PairwiseJudgeReport.ToJudgedPairs(records);

            Assert.Equal(2, pairs.Count); // one JudgedPair per (diff, pairIndex)
            Assert.Equal(2, pairs[0].Executions.Count); // pairIndex 0 has both its runs
            // Run 0 must come before run 1 regardless of the order the file happened to store them in.
            Assert.Equal(Verdict.B, pairs[0].Executions[0].Judgment.Correctness);
            Assert.Equal(Verdict.A, pairs[0].Executions[1].Judgment.Correctness);
        }

        [Fact]
        public void ToJudgedPairs_OmitsPairsWithNoRecordedExecutionAtAll()
        {
            // Only pairIndex 0 was ever attempted; pairIndex 1 must not appear as an empty pair.
            var records = new[] { new JudgeExecutionRecord(Diff, "App.cs", PairIndex: 0, RunIndex: 0, "claude-sonnet-4-6", "v2", Outcome(Verdict.A)) };

            var pairs = PairwiseJudgeReport.ToJudgedPairs(records);

            Assert.Single(pairs);
        }

        /// <summary>
        /// The scenario from the bug report, minus the API: append three executions to a real
        /// <c>.jsonl</c> file (one of them out of run order, as a resumed run would produce), reload
        /// it cold, and confirm the report renders exactly as it would from an in-memory run.
        /// </summary>
        [Fact]
        public void Report_RegeneratedFromAJsonlFixture_MatchesTheInMemoryReport()
        {
            var path = Path.Combine(Path.GetTempPath(), $"cra-judge-results-{Guid.NewGuid():N}.jsonl");
            try
            {
                JudgeResultsStore.Append(path, new JudgeExecutionRecord(Diff, "App.cs", 0, 0, "claude-sonnet-4-6", "v2", Outcome(Verdict.A)));
                JudgeResultsStore.Append(path, new JudgeExecutionRecord(Diff, "App.cs", 0, 2, "claude-sonnet-4-6", "v2", Outcome(Verdict.A)));
                JudgeResultsStore.Append(path, new JudgeExecutionRecord(Diff, "App.cs", 0, 1, "claude-sonnet-4-6", "v2", Outcome(Verdict.B)));

                var fromFile = PairwiseJudgeReport.Generate(
                    PairwiseJudgeReport.ToJudgedPairs(JudgeResultsStore.Load(path)), "claude-sonnet-4-6", "v2", judgeRuns: 3);
                var fromMemory = PairwiseJudgeReport.Generate(
                    [new JudgedPair(Diff, [Outcome(Verdict.A), Outcome(Verdict.B), Outcome(Verdict.A)])],
                    "claude-sonnet-4-6", "v2", judgeRuns: 3);

                // Ignore the "_Generated <timestamp>_" line: everything else (tables, reasoning,
                // totals) must be byte-identical regardless of which source produced it.
                Assert.Equal(WithoutTimestamp(fromMemory), WithoutTimestamp(fromFile));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string WithoutTimestamp(string report) =>
            string.Join('\n', report.Split('\n').Where(l => !l.StartsWith("_Generated")));
    }
}
