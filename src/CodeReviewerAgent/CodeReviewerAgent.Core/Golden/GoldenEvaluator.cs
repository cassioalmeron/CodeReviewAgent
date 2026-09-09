using System.Text.Json;
using System.Text.Json.Serialization;
using CodeReviewerAgent.Core.Llm;

namespace CodeReviewerAgent.Core.Golden;

/// <summary>
/// Runs the golden set: each case is a diff whose correct outcome is known. Most have a planted
/// problem the agent must catch; some are correct code carrying a bait, and there the agent must
/// stay quiet. The assertion lives in code (<see cref="GoldenScorer"/>), even though the agent's
/// output does not.
/// <para>
/// The two outcomes are never added together. Detection and trap resistance answer opposite
/// questions, and a single rate hides which side failed — which is the whole problem the set
/// was rebuilt to expose.
/// </para>
/// <para>
/// Publishing a finished run — the report, the console summary, the raw reviews for the judge —
/// is <see cref="GoldenEvaluatorReport"/>'s job, kept apart so running the set stays free of I/O.
/// </para>
/// </summary>
public static class GoldenEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string CasesDirectory => Path.Combine(AppContext.BaseDirectory, "assets", "evals", "golden");

    public static IReadOnlyList<GoldenCase> LoadCases()
    {
        var cases = JsonSerializer.Deserialize<List<GoldenCase>>(
            File.ReadAllText(Path.Combine(CasesDirectory, "cases.json")), JsonOptions) ?? [];

        // A detection case with no expected severity has nothing to calibrate against, and a
        // missing enum in JSON deserializes to Info — a wrong expected value that still produces
        // a plausible-looking number. Failing here beats being silently wrong in the report.
        var missing = cases
            .Where(c => c.Expect is ExpectFinding { Severity: null })
            .Select(c => c.Name)
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Golden case(s) with no expected severity: {string.Join(", ", missing)}. " +
                "Every detection case needs one, or calibration cannot be measured for it.");

        return cases;
    }

    /// <summary>
    /// The same evaluation, plus the repositories it is written to. The split is not cosmetic:
    /// the other overload is the proof that the evaluation does not depend on persistence, and
    /// this one is the only place that does.
    /// <para>
    /// It writes whatever came back, finished or not. A run that dies halfway — a quota exhausted
    /// on round 55 of 60 is the case this project has actually lived through — arrives here as a
    /// result carrying its completed rounds rather than as an exception, and that is what lets
    /// this method stay three lines: evaluate, save, return.
    /// </para>
    /// </summary>
    public static GoldenRunResult Run(
        ILlmClient client, RepositoryContext repositories, IReadOnlyList<string> promptVersions,
        string? filter = null, IGoldenRoundStore? store = null)
    {
        var result = Run(client, promptVersions, filter, store);
        Persist(result.Completed, repositories);
        return result;
    }

    /// <summary>
    /// Runs and scores the golden set, and touches no repository at all. This is the whole
    /// evaluation: the paid rounds and the three axes, and nothing about where any of it is kept.
    /// </summary>
    /// <param name="promptVersions">
    /// The prompt version(s) to run every case through. One version reproduces today's set;
    /// two turn the run into a pairwise comparison, over the same diffs, with each side scored
    /// separately.
    /// </param>
    /// <param name="filter">
    /// Comma-separated case names to run, for tuning a prompt or a skill against one stubborn
    /// case without paying for a full pass. Null or blank runs the whole set.
    /// </param>
    /// <param name="store">
    /// Where each paid round is made durable as it comes back, so a crash never throws away
    /// reviews already bought. Optional, and null by default: without it <c>Run</c> touches no
    /// file at all, which keeps running the set free of I/O and publishing the caller's job.
    /// </param>
    public static GoldenRunResult Run(
        ILlmClient client, IReadOnlyList<string> promptVersions, string? filter = null,
        IGoldenRoundStore? store = null)
    {
        var (cases, diffs, runs) = Prepare(filter, promptVersions);
        var rounds = new RoundBuffer(cases.Count, runs, promptVersions.Count);

        try
        {
            ReviewEveryRound(rounds, client, cases, diffs, promptVersions, store);

            return new GoldenRunResult(
                Score(rounds, cases, promptVersions),
                CompletedRounds(rounds, cases, diffs),
                null);
        }
        catch (Exception failure)
        {
            // Only the paid phase is caught. A bug in the scoring below is a bug and must still
            // crash: turning it into "the run failed" would hide it behind a plausible outcome.
            return new GoldenRunResult(null, CompletedRounds(rounds, cases, diffs), failure);
        }
    }

    // Everything both overloads need before the first call goes out. Reading the cases can throw
    // (an unknown filter name, a case with no expected severity), and it costs nothing, so it
    // happens before anything is paid for.
    private static (IReadOnlyList<GoldenCase> Cases, IReadOnlyList<string> Diffs, int Runs) Prepare(
        string? filter, IReadOnlyList<string> promptVersions)
    {
        var runs = int.TryParse(Environment.GetEnvironmentVariable("GOLDEN_RUNS"), out var n) && n > 0 ? n : 3;
        var cases = SelectCases(LoadCases(), filter);
        var diffs = cases.Select(c => File.ReadAllText(Path.Combine(CasesDirectory, c.Diff))).ToList();

        if (promptVersions.Count > 1)
            System.Console.WriteLine(
                $"Prompt versions: {string.Join(" vs ", promptVersions)} — this doubles the cost of this run.");

        return (cases, diffs, runs);
    }

    /// <summary>
    /// Phase 1, the paid one, run concurrently. Every round is independent — one diff in, one
    /// review out, no shared state — so the only thing the concurrency has to preserve is where
    /// each answer lands, and the buffer is what knows that.
    /// </summary>
    /// <param name="rounds">
    /// Filled in place, and owned by the caller, so that a run which throws part-way leaves the
    /// completed rounds where the caller can still reach them.
    /// </param>
    private static void ReviewEveryRound(
        RoundBuffer rounds,
        ILlmClient client, IReadOnlyList<GoldenCase> cases, IReadOnlyList<string> diffs,
        IReadOnlyList<string> promptVersions, IGoldenRoundStore? store)
    {
        Parallel.For(0, rounds.Length, ParallelOptions(), slot =>
        {
            var (caseIndex, sideIndex, runIndex) = rounds.Locate(slot);
            var promptVersion = promptVersions[sideIndex];

            // A round a previous run already paid for is reused rather than bought again. The
            // store only offers rounds produced under this run's configuration.
            var recorded = store?.Find(cases[caseIndex].Name, promptVersion, runIndex);
            if (recorded is not null)
            {
                rounds.Record(slot, recorded);
                return;
            }

            var review = new CodeReviewer(client, diffs[caseIndex], promptVersion).Review();
            // Durable before the next paid call goes out, not batched until the run finishes.
            store?.Record(cases[caseIndex].Name, promptVersion, runIndex, review);
            rounds.Record(slot, review);
        });
    }

    /// <summary>
    /// Phase 2: the three axes, per case and per prompt version. No repository, no file, no call —
    /// scoring a finished run is arithmetic over what came back.
    /// </summary>
    private static GoldenScore Score(
        RoundBuffer rounds,
        IReadOnlyList<GoldenCase> cases,
        IReadOnlyList<string> promptVersions)
    {
        var results = new List<GoldenCaseResult>();
        var reviews = new List<ReviewResult>();
        // Per-review verdict, so each round in the report can be labelled.
        var verdicts = new Dictionary<ReviewResult, string>(ReferenceEqualityComparer.Instance);

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            // The two sides never mix: each is scored on its own against the golden
            // expectation, so blending their rates never happens even by accident.
            for (var sideIndex = 0; sideIndex < promptVersions.Count; sideIndex++)
            {
                var sideRounds = rounds.Side(caseIndex, sideIndex);

                var (result, labels) = ScoreOneSide(cases[caseIndex], promptVersions[sideIndex], sideRounds);
                results.Add(result);

                for (var i = 0; i < sideRounds.Count; i++)
                {
                    reviews.Add(sideRounds[i]);
                    verdicts[sideRounds[i]] = labels[i];
                }
            }
        }

        var condition = GoldenCondition.From(reviews, Environment.GetEnvironmentVariable("SKILLS"));
        return new GoldenScore(results, reviews, condition, verdicts);
    }

    /// <summary>
    /// Writes what came back to the repositories, finished run or not. It takes the completed
    /// rounds rather than a run, because a run that died has no scored result and its rounds are
    /// exactly the part worth keeping.
    /// <para>
    /// Sequential on purpose. Neither repository is thread-safe: <c>DbContext</c> forbids
    /// concurrent use outright, and the file store derives the next id from the highest one on
    /// disk, which races. Nothing here is paid, so the serialization costs milliseconds.
    /// </para>
    /// </summary>
    private static void Persist(IReadOnlyList<CompletedRound> completed, RepositoryContext repositories)
    {
        if (completed.Count == 0)
            return;

        // The golden set is its own project, so its reviews are kept apart from real repositories.
        var project = repositories.Projects.GetOrAdd("golden", "Golden Set");

        foreach (var group in completed.GroupBy(r => (r.Case, r.Diff)))
        {
            // The golden diff is stable: store it once (reused by content hash across runs),
            // then attach each run's assessment to it. Both sides review the same stored diff.
            var reviewId = repositories.Reviews.GetOrAdd(new Review
            {
                ProjectId = project.Id,
                Content = group.Key.Diff,
                Source = group.Key.Case,
                CreatedAt = DateTime.UtcNow,
            });

            foreach (var round in group)
                repositories.Assessments.Save(Assessment.FromReview(reviewId, round.Review));
        }
    }

    // Every round that came back, paired with the case and diff it belongs to. On a finished run
    // that is all of them; on one that died part-way it is what was already paid for.
    private static List<CompletedRound> CompletedRounds(
        RoundBuffer rounds, IReadOnlyList<GoldenCase> cases, IReadOnlyList<string> diffs) =>
        [.. rounds.Completed().Select(r =>
            new CompletedRound(cases[r.Address.Case].Name, diffs[r.Address.Case], r.Review))];

    /// <summary>
    /// One case, one prompt version, every round of it, on all three axes. Pure: it reads the
    /// reviews and returns numbers, so the aggregation rules can be tested without a repository,
    /// a file, or a call.
    /// <para>
    /// Detection counts rounds and precision counts findings, on purpose. A round that emitted
    /// four findings which should not exist has to outweigh one that emitted a single one, and a
    /// mean of per-round rates would flatten exactly that.
    /// </para>
    /// </summary>
    internal static (GoldenCaseResult Result, IReadOnlyList<string> Verdicts) ScoreOneSide(
        GoldenCase golden, string promptVersion, IReadOnlyList<ReviewResult> sideRounds)
    {
        var isTrap = golden.Expect is ExpectNoFinding;
        var labels = new List<string>(sideRounds.Count);

        // The LLM output is non-deterministic, so a single run is a noisy sample. The success
        // rate (e.g. 2/3) is the signal.
        var successes = 0;
        string? lastMiss = null;
        var precisionCorrect = 0;
        var precisionCounted = 0;
        var distances = new List<int>();
        var unforeseen = new List<Finding>();

        foreach (var review in sideRounds)
        {
            var findings = review.Findings ?? [];
            var score = GoldenScorer.Score(findings, golden);

            precisionCorrect += score.PrecisionCorrect;
            precisionCounted += score.PrecisionCounted;
            if (score.CalibrationDistance is { } distance)
                distances.Add(distance);
            unforeseen.AddRange(score.Outcomes
                .Where(o => o.Verdict == FindingVerdict.Unforeseen)
                .Select(o => o.Finding));

            if (score.Succeeded)
            {
                successes++;
                labels.Add(isTrap ? "✅ resisted" : "✅ caught");
            }
            else
            {
                lastMiss = GoldenScorer.Describe(golden.Expect, findings);
                labels.Add(isTrap ? "❌ fell for it" : "❌ missed");
            }
        }

        var result = new GoldenCaseResult(
            golden.Name, isTrap ? GoldenKind.Trap : GoldenKind.Detection,
            golden.Since, promptVersion, successes, sideRounds.Count, lastMiss,
            precisionCorrect, precisionCounted, distances, unforeseen);

        return (result, labels);
    }

    // Capped on purpose: uncapped concurrency trades latency for 429s, and the transport's
    // retry/backoff then gives the time back with interest.
    private static ParallelOptions ParallelOptions() => new()
    {
        MaxDegreeOfParallelism =
            int.TryParse(Environment.GetEnvironmentVariable("GOLDEN_PARALLELISM"), out var p) && p > 0 ? p : 4,
    };

    // An unknown name is a typo, not an empty selection: scoring nothing and reporting "0/0"
    // would look like a clean pass.
    private static IReadOnlyList<GoldenCase> SelectCases(IReadOnlyList<GoldenCase> cases, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return cases;

        var wanted = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = wanted
            .Where(name => !cases.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                $"No golden case named {string.Join(", ", unknown)}. Known cases: {string.Join(", ", cases.Select(c => c.Name))}.");

        return [.. cases.Where(c => wanted.Contains(c.Name, StringComparer.OrdinalIgnoreCase))];
    }
}
