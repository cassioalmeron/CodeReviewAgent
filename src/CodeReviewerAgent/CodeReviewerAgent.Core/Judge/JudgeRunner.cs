using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// One pair of reviews (same diff, two prompt versions, run <c>i</c> of each side) judged
    /// <c>judgeRuns</c> times, each execution re-randomising which review sits in which prompt slot.
    /// </summary>
    public record JudgedPair(string? Diff, IReadOnlyList<PairJudgeOutcome> Executions);

    /// <summary>
    /// Second evaluation stage: loads the reviews persisted by <c>eval</c> and judges them
    /// pairwise — the two prompt versions of the same diff, positionally paired (plan 013),
    /// compared head-to-head with a randomised, re-drawn slot assignment and N executions per pair
    /// (plan 015, T3-T5). Runs independently so the executor does not have to be re-invoked.
    /// <para>
    /// Every execution is appended to <see cref="JudgeResultsStore"/> the moment it comes back, not
    /// batched until the run finishes. That makes a fatal error mid-run (quota, network, anything)
    /// lose at most the one call in flight instead of everything judged so far: a second run reads
    /// the file back, skips whatever is already recorded, and picks up where the first one stopped.
    /// The report is always built from what is actually durable on disk, never from what this
    /// process happened to be holding in memory when it died.
    /// </para>
    /// <para>
    /// This is not the only judge path: <see cref="Judge.Evaluate(string, ReviewResult)"/> plus
    /// <see cref="JudgeReportGenerator"/> score one real, stored review at a time on an absolute
    /// 1-to-5 scale (rubric v1) — there is nothing to pair there, and that path is untouched.
    /// </para>
    /// </summary>
    public static class JudgeRunner
    {
        private static readonly JsonSerializerOptions LoadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        // The judge client is built by the entry point (Console/Api) via the Infra factory and
        // injected here — Core does not depend on the concrete LLM clients or their factory.
        /// <param name="judgeRuns">
        /// How many times each pair is judged. Each execution re-draws the slot assignment, so the
        /// N also samples position bias instead of merely repeating it (Design Decision 3, plan 015).
        /// </param>
        public static void Run(ILlmClient judgeClient, int judgeRuns)
        {
            var resultsPath = Path.Combine(AppContext.BaseDirectory, "reviews", "eval-results.json");
            if (!File.Exists(resultsPath))
            {
                System.Console.WriteLine("No eval-results.json found. Run `eval` first.");
                return;
            }

            var reviews = JsonSerializer.Deserialize<List<ReviewResult>>(
                File.ReadAllText(resultsPath), LoadOptions) ?? [];
            if (reviews.Count == 0)
            {
                System.Console.WriteLine("eval-results.json has no reviews.");
                return;
            }

            var judgeModel = Environment.GetEnvironmentVariable("JUDGE_MODEL") ?? "claude-sonnet-4-6";
            // v2 is the pairwise rubric (reasoning first, A/B/tie verdicts). The stored-assessment
            // path is untouched by this plan and keeps resolving its own default independently ("v1").
            var rubricVersion = Environment.GetEnvironmentVariable("RUBRIC_VERSION") ?? "v2";
            var judge = new Judge(judgeClient, rubricVersion);

            var plan = PlanPairs(reviews);
            var resultsFile = JudgeResultsStore.DefaultPath;
            var completed = JudgeResultsStore.CompletedKeys(
                JudgeResultsStore.Load(resultsFile), judgeModel, rubricVersion);
            var pending = Pending(plan, judgeRuns, completed);
            if (completed.Count > 0)
                System.Console.WriteLine(
                    $"Resuming: {completed.Count} execution(s) already recorded, {pending.Count} remaining.");

            System.Console.WriteLine("=== Pairwise judge ===");
            try
            {
                foreach (var (diff, label, pairIndex, runIndex, reviewA, reviewB) in pending)
                {
                    var (slotA, slotB) = AssignSlots(reviewA, reviewB, swap: Random.Shared.Next(2) == 1);
                    var outcome = judge.Evaluate(diff, slotA, slotB);
                    JudgeResultsStore.Append(
                        resultsFile,
                        new JudgeExecutionRecord(
                            diff, label, pairIndex, runIndex, judgeModel, rubricVersion, outcome));
                    System.Console.WriteLine($"{label}: pair {pairIndex + 1}, run {runIndex + 1}/{judgeRuns} — judged");
                }
            }
            finally
            {
                // Whatever made it to disk gets a report, crash or not — the point of persisting as
                // we go is that a fatal error never loses judgments that were already paid for.
                SaveReport(resultsFile, judgeModel, rubricVersion, judgeRuns, plan.Count);
            }
        }

        // Every (diff, pair, run) this run still needs to judge: the full plan, minus whatever a
        // previous run already recorded. Pure — no I/O, no LLM call — so resuming is testable
        // against a fixture of already-recorded keys without touching the API.
        internal static List<(string Diff, string Label, int PairIndex, int RunIndex, ReviewResult A, ReviewResult B)> Pending(
            IReadOnlyList<(string Diff, string Label, int PairIndex, ReviewResult A, ReviewResult B)> plan,
            int judgeRuns,
            IReadOnlySet<(string Diff, int PairIndex, int RunIndex)> completed)
        {
            var pending = new List<(string, string, int, int, ReviewResult, ReviewResult)>();
            foreach (var (diff, label, pairIndex, reviewA, reviewB) in plan)
                for (var runIndex = 0; runIndex < judgeRuns; runIndex++)
                    if (!completed.Contains((diff, pairIndex, runIndex)))
                        pending.Add((diff, label, pairIndex, runIndex, reviewA, reviewB));
            return pending;
        }

        // Reloads whatever is durably on disk — not the in-memory results of this run, which may be
        // incomplete if this is running inside a `finally` after a fatal error — and reports on that.
        private static void SaveReport(
            string resultsFile, string judgeModel, string rubricVersion, int judgeRuns, int pairsPlanned)
        {
            var allRecords = JudgeResultsStore.Load(resultsFile);
            var records = JudgeResultsStore.ForConfiguration(allRecords, judgeModel, rubricVersion);

            // Foreign records are announced rather than dropped quietly: the file legitimately holds
            // other configurations' history, but a report that silently discarded them would look
            // identical to one where this run judged fewer pairs than it meant to.
            var foreign = allRecords.Count - records.Count;
            if (foreign > 0)
                System.Console.WriteLine(
                    $"Ignoring {foreign} execution(s) judged under a different model or rubric.");

            if (records.Count == 0)
            {
                System.Console.WriteLine("No judge results recorded; nothing to report.");
                return;
            }

            var pairsRecorded = records.Select(r => (r.Diff, r.PairIndex)).Distinct().Count();
            var partial = pairsRecorded < pairsPlanned
                ? new PartialRun(pairsRecorded, pairsPlanned, records.Count, pairsPlanned * judgeRuns)
                : null;

            var judgedPairs = PairwiseJudgeReport.ToJudgedPairs(records);
            var reportPath = PairwiseJudgeReport.Save(judgedPairs, judgeModel, rubricVersion, judgeRuns, partial);

            System.Console.WriteLine(
                $"Judge: {judgedPairs.Count} pairs, {records.Count} executions — " +
                $"{Utils.Money(records.Sum(r => r.Outcome.Cost))}, {records.Sum(r => r.Outcome.LatencyMs)} ms");
            System.Console.WriteLine($"Judge report saved to {reportPath}");
        }

        // Every (diff, pairIndex) this run intends to judge, in the order eval-results.json presents
        // its diffs — the plan the loop walks, independent of what a previous run already recorded.
        internal static List<(string Diff, string Label, int PairIndex, ReviewResult A, ReviewResult B)> PlanPairs(
            IReadOnlyList<ReviewResult> reviews)
        {
            var plan = new List<(string, string, int, ReviewResult, ReviewResult)>();
            foreach (var group in reviews.GroupBy(r => r.Diff ?? ""))
            {
                var pairs = Pair([.. group]);
                for (var i = 0; i < pairs.Count; i++)
                    plan.Add((group.Key, FirstFile(group.Key), i, pairs[i].A, pairs[i].B));
            }
            return plan;
        }

        // Splits one diff's reviews by prompt version and zips them positionally: run i of one side
        // with run i of the other (plan 013 — the runs are independent samples of the same
        // configuration, so the pairing is arbitrary, and positional is the arbitrary choice that
        // reproduces). A diff that does not carry exactly two prompt versions cannot be judged
        // pairwise, and staying silent about it would look like a clean run.
        internal static List<(ReviewResult A, ReviewResult B)> Pair(IReadOnlyList<ReviewResult> reviews)
        {
            var sides = reviews.GroupBy(r => r.PromptVersion ?? "").ToList();
            if (sides.Count != 2)
                throw new InvalidOperationException(
                    $"Pairwise judge requires exactly two prompt versions per diff; found {sides.Count} "
                    + $"({string.Join(", ", sides.Select(s => s.Key))}). Run `eval` with PROMPT_VERSION_COMPARISON set.");

            var sideA = sides[0].ToList();
            var sideB = sides[1].ToList();
            if (sideA.Count != sideB.Count)
                throw new InvalidOperationException(
                    $"Uneven runs between prompt versions '{sides[0].Key}' ({sideA.Count}) and "
                    + $"'{sides[1].Key}' ({sideB.Count}); cannot zip positionally.");

            return [.. sideA.Zip(sideB, (a, b) => (a, b))];
        }

        // The pairing (which two reviews are compared) never changes; only which slot — A or B —
        // each one lands in for a given judge call is randomised.
        internal static (ReviewResult SlotA, ReviewResult SlotB) AssignSlots(
            ReviewResult a, ReviewResult b, bool swap) =>
            swap ? (b, a) : (a, b);

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
