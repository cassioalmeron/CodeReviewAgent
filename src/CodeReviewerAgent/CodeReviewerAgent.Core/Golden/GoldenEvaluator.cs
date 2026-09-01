using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core
{
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

        public static IReadOnlyList<GoldenCase> LoadCases() =>
            JsonSerializer.Deserialize<List<GoldenCase>>(
                File.ReadAllText(Path.Combine(CasesDirectory, "cases.json")), JsonOptions) ?? [];

        /// <param name="promptVersions">
        /// The prompt version(s) to run every case through. One version reproduces today's set;
        /// two turn the run into a pairwise comparison, over the same diffs, with each side scored
        /// separately.
        /// </param>
        /// <param name="filter">
        /// Comma-separated case names to run, for tuning a prompt or a skill against one stubborn
        /// case without paying for a full pass. Null or blank runs the whole set.
        /// </param>
        public static GoldenRun Run(
            ILlmClient client, IProjectRepository projects, IReviewRepository reviewsRepo,
            IAssessmentRepository assessments, IReadOnlyList<string> promptVersions, string? filter = null)
        {
            var runs = int.TryParse(Environment.GetEnvironmentVariable("GOLDEN_RUNS"), out var n) && n > 0 ? n : 3;
            var sides = promptVersions.Count;

            // The golden set is its own project, so its reviews are kept apart from real repositories.
            var project = projects.GetOrAdd("golden", "Golden Set");

            var cases = Select(LoadCases(), filter);
            var diffs = cases.Select(c => File.ReadAllText(Path.Combine(CasesDirectory, c.Diff))).ToList();

            if (sides > 1)
                System.Console.WriteLine(
                    $"Prompt versions: {string.Join(" vs ", promptVersions)} — this doubles the cost of this run.");

            // Phase 1 — the paid part, run concurrently. Every round is independent: one diff in,
            // one review out, no shared state. Position in the array is (case index × runs × sides) +
            // (side index × runs) + round, so the ordering survives the concurrency.
            var perCase = runs * sides;
            var rounds = new ReviewResult[cases.Count * perCase];
            Parallel.For(0, rounds.Length, ParallelOptions(), slot =>
            {
                var caseIndex = slot / perCase;
                var sideIndex = slot % perCase / runs;
                rounds[slot] = new CodeReviewer(client, diffs[caseIndex], promptVersions[sideIndex]).Review();
            });

            // Phase 2 — scoring and persistence, strictly sequential. Neither repository is
            // thread-safe: DbContext forbids concurrent use outright, and the file store derives
            // the next id from the highest one on disk, which races.
            var results = new List<GoldenCaseResult>();
            var reviews = new List<ReviewResult>();
            // Per-review verdict, so each round in the report can be labelled.
            var verdicts = new Dictionary<ReviewResult, string>(ReferenceEqualityComparer.Instance);

            for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
            {
                var golden = cases[caseIndex];
                var isTrap = golden.Expect is ExpectNoFinding;

                // The golden diff is stable: store it once (reused by content hash across runs),
                // then attach each run's assessment to it. Both sides review the same stored diff.
                var reviewId = reviewsRepo.GetOrAdd(new Review
                {
                    ProjectId = project.Id,
                    Content = diffs[caseIndex],
                    Source = golden.Name,
                    CreatedAt = DateTime.UtcNow,
                });

                // The two sides never mix: each is scored on its own against the golden
                // expectation, so blending their rates never happens even by accident.
                for (var sideIndex = 0; sideIndex < sides; sideIndex++)
                {
                    var promptVersion = promptVersions[sideIndex];

                    // Each case ran multiple times: the LLM output is non-deterministic, so a
                    // single run is a noisy sample. The success rate (e.g. 2/3) is the signal.
                    var successes = 0;
                    string? lastMiss = null;
                    for (var i = 0; i < runs; i++)
                    {
                        var review = rounds[(caseIndex * perCase) + (sideIndex * runs) + i];
                        reviews.Add(review);
                        assessments.Save(Assessment.FromReview(reviewId, review));
                        var findings = review.Findings ?? [];

                        if (GoldenScorer.Succeeded(findings, golden.Expect))
                        {
                            successes++;
                            verdicts[review] = isTrap ? "✅ resisted" : "✅ caught";
                        }
                        else
                        {
                            lastMiss = GoldenScorer.Describe(golden.Expect, findings);
                            verdicts[review] = isTrap ? "❌ fell for it" : "❌ missed";
                        }
                    }

                    results.Add(new GoldenCaseResult(
                        golden.Name, isTrap ? GoldenKind.Trap : GoldenKind.Detection,
                        golden.Since, promptVersion, successes, runs, lastMiss));
                }
            }

            var condition = GoldenCondition.From(reviews, Environment.GetEnvironmentVariable("SKILLS"));
            return new GoldenRun(results, reviews, condition, verdicts);
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
        private static IReadOnlyList<GoldenCase> Select(IReadOnlyList<GoldenCase> cases, string? filter)
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
}
