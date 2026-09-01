using System.Text;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// How much of a planned pairwise judge run actually made it to disk before it stopped —
    /// present on the report only when it stopped early, so a complete run's report carries no
    /// trace of this at all.
    /// </summary>
    public record PartialRun(int PairsRecorded, int PairsPlanned, int ExecutionsRecorded, int ExecutionsPlanned);

    /// <summary>
    /// Renders a finished pairwise judge run as a Markdown report: a verdict table per case
    /// (majority vote per criterion, resolved into prompt-version terms) with the judge's reasoning
    /// per pair, and a top-level tally across the whole run — the actual answer of US-011.
    /// <para>
    /// Kept apart from <see cref="JudgeReportGenerator"/>, which renders the absolute 1-to-5 scoring
    /// path (rubric v1) and keeps rendering it for the stored-assessment route. The two judgment
    /// shapes share nothing but cost and latency; a report that branches on which one it received is
    /// exactly the coupling ADR-005 removed from <see cref="CodeReviewer"/> and, one commit ago, from
    /// <see cref="GoldenEvaluator"/> (see <see cref="GoldenEvaluatorReport"/>).
    /// </para>
    /// </summary>
    public static class PairwiseJudgeReport
    {
        // The six criteria of assets/rubrics/judge-v2.md, in report order.
        private static readonly (string Name, Func<PairJudgment, Verdict> Select)[] Criteria =
        [
            ("Correctness", j => j.Correctness),
            ("Actionability", j => j.Actionability),
            ("Calibration", j => j.Calibration),
            ("Signal-to-noise", j => j.SignalToNoise),
            ("Conciseness", j => j.Conciseness),
            ("Overall", j => j.Overall),
        ];

        public static string Generate(
            IReadOnlyList<JudgedPair> pairs, string judgeModel, string rubricVersion, int judgeRuns,
            PartialRun? partial = null)
        {
            var report = new StringBuilder();
            report.AppendLine("# Pairwise Judge Report");
            report.AppendLine();
            report.AppendLine(
                $"_Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC — judge {judgeModel}, rubric {rubricVersion}, "
                + $"{judgeRuns} execution(s) per pair_");
            report.AppendLine();
            if (partial is not null)
                AppendPartialNotice(report, partial);
            report.AppendLine(
                "> Pairing is positional: run *i* of one prompt version is judged against run *i* of the "
                + "other. The runs are independent samples of the same configuration, so the pairing is "
                + "arbitrary — positional is simply the arbitrary choice that reproduces. `tie *` marks a "
                + "genuine split with no majority among the votes, as opposed to the judge repeatedly "
                + "calling the pair even.");
            report.AppendLine();

            foreach (var group in pairs.GroupBy(p => p.Diff ?? ""))
                AppendCase(report, group.Key, [.. group]);

            var allExecutions = pairs.SelectMany(p => p.Executions).ToList();
            report.AppendLine("---");
            report.AppendLine("# Overall");
            report.AppendLine();
            report.AppendLine($"_Across {pairs.Count} pairs, {allExecutions.Count} executions._");
            report.AppendLine();
            AppendVerdictTable(report, allExecutions);
            AppendTotals(report, allExecutions);

            return report.ToString();
        }

        /// <summary>
        /// Generates the report and writes it to the reports directory, returning the file path.
        /// </summary>
        public static string Save(
            IReadOnlyList<JudgedPair> pairs, string judgeModel, string rubricVersion, int judgeRuns,
            PartialRun? partial = null)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "reports");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"pairwise-judge-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.md");
            File.WriteAllText(path, Generate(pairs, judgeModel, rubricVersion, judgeRuns, partial));
            return path;
        }

        // The run stopped before every planned pair got at least one execution — a quota error, a
        // crash, anything. Said up front so the numbers below are never mistaken for the full 30
        // pairs when they are not.
        private static void AppendPartialNotice(StringBuilder report, PartialRun partial)
        {
            report.AppendLine(
                $"> **Partial run** — {partial.PairsRecorded}/{partial.PairsPlanned} pairs have at least "
                + $"one recorded execution ({partial.ExecutionsRecorded}/{partial.ExecutionsPlanned} "
                + "executions total). The tables below reflect only what was durably saved before the "
                + "run stopped; re-run `judge` to pick up the remaining pairs — already-recorded "
                + "executions are skipped, not repeated.");
            report.AppendLine();
        }

        /// <summary>
        /// Reassembles the report's input purely from durable records — the file <see
        /// cref="JudgeRunner"/> appends to as it goes, not whatever this process happens to hold in
        /// memory. Grouped by (diff, pair) and ordered by run index, so the report reads the same
        /// whether the run finished cleanly or was resumed after a crash.
        /// </summary>
        public static List<JudgedPair> ToJudgedPairs(IReadOnlyList<JudgeExecutionRecord> records) =>
            [.. records
                .GroupBy(r => (r.Diff, r.PairIndex))
                .OrderBy(g => g.Key.PairIndex)
                .Select(g => new JudgedPair(g.Key.Diff, [.. g.OrderBy(r => r.RunIndex).Select(r => r.Outcome)]))];

        private static void AppendCase(StringBuilder report, string diff, IReadOnlyList<JudgedPair> casePairs)
        {
            report.AppendLine($"## {FirstFile(diff)}");
            report.AppendLine();
            AppendReviewedDiff(report, diff);

            var executions = casePairs.SelectMany(p => p.Executions).ToList();
            AppendVerdictTable(report, executions);
            report.AppendLine(
                $"_Cost: {Utils.Money(executions.Sum(o => o.Cost))} · Latency: {executions.Sum(o => o.LatencyMs)} ms_");
            report.AppendLine();

            for (var i = 0; i < casePairs.Count; i++)
                AppendReasoning(report, i + 1, casePairs[i]);
        }

        // The verdict table: majority vote per criterion over every raw execution given, each one
        // resolved into prompt-version terms via its own (independently randomised) pairing — never
        // averaged, because "A"/"B"/"tie" is categorical, not a point on a numeric scale.
        private static void AppendVerdictTable(StringBuilder report, IReadOnlyList<PairJudgeOutcome> executions)
        {
            report.AppendLine("| Criterion | Verdict | Votes |");
            report.AppendLine("|---|---|---|");
            foreach (var (name, select) in Criteria)
            {
                var votes = executions.Select(o => Translate(select(o.Judgment), o.Pairing)).ToList();
                var tally = MajorityVote(votes);
                var verdictCell = tally.IsSplit ? "tie *" : tally.Winner == "tie" ? "tie" : $"**{tally.Winner}**";
                var breakdown = string.Join(" · ", tally.Votes
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} {kv.Value}"));
                report.AppendLine($"| {name} | {verdictCell} | {breakdown} |");
            }
            report.AppendLine();
        }

        private static void AppendReasoning(StringBuilder report, int pairNumber, JudgedPair pair)
        {
            report.AppendLine($"### Pair {pairNumber}");
            report.AppendLine();
            for (var i = 0; i < pair.Executions.Count; i++)
            {
                var execution = pair.Executions[i];
                report.AppendLine(
                    $"- **Run {i + 1}** (A={execution.Pairing.SlotA}, B={execution.Pairing.SlotB}): "
                    + $"{execution.Judgment.Reasoning}");
            }
            report.AppendLine();
        }

        private static void AppendTotals(StringBuilder report, IReadOnlyList<PairJudgeOutcome> executions)
        {
            report.AppendLine();
            report.AppendLine("## Totals");
            report.AppendLine();
            report.AppendLine("| Setting | Value |");
            report.AppendLine("|---------|-------|");
            report.AppendLine($"| Total cost | {Utils.Money(executions.Sum(o => o.Cost))} |");
            report.AppendLine($"| Total latency | {executions.Sum(o => o.LatencyMs)} ms |");
            report.AppendLine($"| Total input tokens | {executions.Sum(o => o.InputTokens)} |");
            report.AppendLine($"| Total output tokens | {executions.Sum(o => o.OutputTokens)} |");
            report.AppendLine();
        }

        private static void AppendReviewedDiff(StringBuilder report, string? diff)
        {
            var fileDiffs = DiffSplitter.ByFile(diff);
            if (fileDiffs.Count == 0)
                return;

            report.AppendLine("### Reviewed diff");
            report.AppendLine();
            foreach (var (path, text) in fileDiffs)
            {
                report.AppendLine($"#### `{path}`");
                report.AppendLine();
                report.AppendLine("```diff");
                report.AppendLine(text.TrimEnd());
                report.AppendLine("```");
                report.AppendLine();
            }
        }

        // Slot verdict → prompt-version terms: "A"/"B" resolve through this execution's own
        // (independently randomised) pairing; "tie" needs no resolution.
        private static string Translate(Verdict verdict, JudgePairing pairing) => verdict switch
        {
            Verdict.A => pairing.SlotA,
            Verdict.B => pairing.SlotB,
            Verdict.Tie => "tie",
            _ => throw new ArgumentOutOfRangeException(nameof(verdict)),
        };

        // Majority vote over categorical votes — never an average, since there is no numeric
        // distance between "v3", "tie" and "v5". A split with no unique leader is reported as a tie
        // and flagged: it means the criterion did not discriminate, which is a real result, not
        // something to hide behind a coin flip.
        internal static Tally MajorityVote(IReadOnlyList<string> votes)
        {
            var counts = votes
                .GroupBy(v => v, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count());
            var max = counts.Values.Max();
            var leaders = counts.Where(kv => kv.Value == max).Select(kv => kv.Key).ToList();
            return new Tally(leaders.Count == 1 ? leaders[0] : "tie", leaders.Count > 1, counts);
        }

        internal sealed record Tally(string Winner, bool IsSplit, IReadOnlyDictionary<string, int> Votes);

        // The first added file in the diff, used as a readable case label.
        private static string FirstFile(string diff)
        {
            foreach (var line in diff.Replace("\r\n", "\n").Split('\n'))
                if (line.StartsWith("+++ "))
                {
                    var path = line[4..].Trim();
                    return path.StartsWith("b/") ? path[2..] : path;
                }
            return "(unknown)";
        }
    }
}
