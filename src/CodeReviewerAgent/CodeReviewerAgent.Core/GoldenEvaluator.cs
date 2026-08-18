using System.Text;
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
    /// </summary>
    public static class GoldenEvaluator
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static string CasesDirectory => Path.Combine(AppContext.BaseDirectory, "evals", "golden");

        public static IReadOnlyList<GoldenCase> LoadCases() =>
            JsonSerializer.Deserialize<List<GoldenCase>>(
                File.ReadAllText(Path.Combine(CasesDirectory, "cases.json")), JsonOptions) ?? [];

        /// <param name="filter">
        /// Comma-separated case names to run, for tuning a prompt or a skill against one stubborn
        /// case without paying for a full pass. Null or blank runs the whole set.
        /// </param>
        public static GoldenRun Run(
            ILlmClient client, IProjectRepository projects, IReviewRepository reviewsRepo,
            IAssessmentRepository assessments, string? filter = null)
        {
            var runs = int.TryParse(Environment.GetEnvironmentVariable("GOLDEN_RUNS"), out var n) && n > 0 ? n : 3;

            // The golden set is its own project, so its reviews are kept apart from real repositories.
            var project = projects.GetOrAdd("golden", "Golden Set");

            var cases = Select(LoadCases(), filter);
            var diffs = cases.Select(c => File.ReadAllText(Path.Combine(CasesDirectory, c.Diff))).ToList();

            // Phase 1 — the paid part, run concurrently. Every round is independent: one diff in,
            // one review out, no shared state. Position in the array is case index × runs + round,
            // so the ordering survives the concurrency.
            var rounds = new ReviewResult[cases.Count * runs];
            Parallel.For(0, rounds.Length, ParallelOptions(), slot =>
                rounds[slot] = new CodeReviewer(client, diffs[slot / runs]).Review());

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
                // then attach each run's assessment to it.
                var reviewId = reviewsRepo.GetOrAdd(new Review
                {
                    ProjectId = project.Id,
                    Content = diffs[caseIndex],
                    Source = golden.Name,
                    CreatedAt = DateTime.UtcNow,
                });

                // Each case ran multiple times: the LLM output is non-deterministic, so a single
                // run is a noisy sample. The success rate (e.g. 2/3) is the signal.
                var successes = 0;
                string? lastMiss = null;
                for (var i = 0; i < runs; i++)
                {
                    var review = rounds[(caseIndex * runs) + i];
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
                    golden.Since, successes, runs, lastMiss));
            }

            var condition = GoldenCondition.From(reviews, Environment.GetEnvironmentVariable("SKILLS"));
            return new GoldenRun(results, reviews, condition, verdicts);
        }

        /// <summary>
        /// Publishes a finished run: one report covering every round, each labelled with its golden
        /// verdict and the rate summary appended as a footer, plus the raw reviews for the judge.
        /// Kept apart from <see cref="Run"/> so running the set is free of I/O.
        /// </summary>
        public static string SaveReport(GoldenRun run)
        {
            var reportPath = ReportGenerator.Save(
                [.. run.Reviews], BuildFooter(run.Results, run.Condition),
                r => run.Verdicts.GetValueOrDefault(r));
            System.Console.WriteLine($"Report saved to {reportPath}");

            PersistReviews(run.Reviews);
            return reportPath;
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

        // Persist the raw reviews so the judge can score them in a separate run, without
        // re-invoking the (paid) executor. The judge loads this file.
        private static void PersistReviews(IReadOnlyList<ReviewResult> reviews)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "reviews");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "eval-results.json");
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            };
            File.WriteAllText(path, JsonSerializer.Serialize(reviews, options));
            System.Console.WriteLine($"Eval results saved to {path}");
        }

        // A single result line, shared by the console output and the report footer:
        // PASS = succeeded every run, FAIL = never, FLAKY = some but not all.
        public static string FormatLine(GoldenCaseResult r)
        {
            var status = r.Successes == r.Runs ? "PASS" : r.Successes == 0 ? "FAIL" : "FLAKY";
            var kind = r.Kind == GoldenKind.Trap ? "trap" : "detection";
            var since = r.Since is null ? "" : $", {r.Since}";
            var detail = r.Successes == r.Runs ? "" : $" — {r.MissDetail}";
            return $"[{status}] {r.Name} ({kind}{since}) {r.Successes}/{r.Runs}{detail}";
        }

        /// <summary>The two rates, side by side and never summed — the console summary.</summary>
        public static string FormatTotals(IReadOnlyList<GoldenCaseResult> results)
        {
            var summary = $"Detection {Rate(results, GoldenKind.Detection)}";
            return results.Any(r => r.Kind == GoldenKind.Trap)
                ? $"{summary} · Trap resistance {Rate(results, GoldenKind.Trap)}"
                : summary;
        }

        public static string BuildFooter(IReadOnlyList<GoldenCaseResult> results, GoldenCondition condition)
        {
            var footer = new StringBuilder();
            footer.AppendLine("# Golden set v2");
            footer.AppendLine();
            AppendCondition(footer, condition);

            footer.AppendLine("```text");
            foreach (var r in results)
                footer.AppendLine(FormatLine(r));
            footer.AppendLine("```");
            footer.AppendLine();

            var hasTraps = results.Any(r => r.Kind == GoldenKind.Trap);
            footer.AppendLine($"- **Detection** {Rate(results, GoldenKind.Detection)} {Scope(results, GoldenKind.Detection)}");
            if (hasTraps)
                footer.AppendLine($"- **Trap resistance** {Rate(results, GoldenKind.Trap)} {Scope(results, GoldenKind.Trap)}");

            AppendVersionLadder(footer, results, hasTraps);
            return footer.ToString();
        }

        // Which harness configuration produced these numbers. Without it two runs of opposite
        // conditions render identically, which is how a ruler gets swapped mid-measurement.
        private static void AppendCondition(StringBuilder footer, GoldenCondition condition)
        {
            footer.AppendLine($"Condition: **{condition.Label}** (SKILLS={condition.Setting})");
            footer.AppendLine();

            if (condition.IsBaseline || condition.Rounds == 0)
                return;

            var active = condition.SkillRounds.Count == 0
                ? "none"
                : string.Join(", ", condition.SkillRounds
                    .OrderByDescending(s => s.Value)
                    .Select(s => $"{s.Key} ({s.Value}/{condition.Rounds})"));
            footer.AppendLine($"Skills active: {active}");
            footer.AppendLine();

            if (condition.RoundsWithoutSkill > 0)
            {
                footer.AppendLine(
                    $"> **{condition.RoundsWithoutSkill}/{condition.Rounds} rounds ran with no skill loaded.** "
                    + "Those rounds are baseline in disguise: averaging them into the harness condition "
                    + "dilutes the delta between the two.");
                footer.AppendLine();
            }
        }

        // Scaled by the C# version each case requires, so the report says which constructs a model
        // handles badly rather than only that it failed. Deliberately not called a knowledge cutoff:
        // measurement rejected that reading. gpt-4.1 (cutoff jun/2024) fails C# 12 from 2023 and
        // deepseek (cutoff apr/2026) fails C# 11 from 2022, both well inside their training window.
        private static void AppendVersionLadder(
            StringBuilder footer, IReadOnlyList<GoldenCaseResult> results, bool hasTraps)
        {
            // Ordered as a ladder, oldest first — "C# 8" before "C# 14", which a string sort gets
            // backwards. A scrambled ladder defeats the point of the table.
            var groups = results
                .GroupBy(r => r.Since ?? "agnostic")
                .OrderBy(g => VersionRank(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (groups.Count < 2)
                return;

            footer.AppendLine();
            footer.AppendLine("## By required C# version");
            footer.AppendLine();
            footer.AppendLine($"| Since | Cases | Detection |{(hasTraps ? " Trap resistance |" : "")}");
            footer.AppendLine($"|---|---|---|{(hasTraps ? "---|" : "")}");
            foreach (var group in groups)
            {
                var inGroup = group.ToList();
                var traps = hasTraps ? $" {Rate(inGroup, GoldenKind.Trap)} |" : "";
                footer.AppendLine($"| {group.Key} | {inGroup.Count} | {Rate(inGroup, GoldenKind.Detection)} |{traps}");
            }
        }

        // Version-agnostic cases sort first; the rest by the number in "C# <n>". Anything that
        // parses as neither lands at the end rather than silently jumping the queue.
        private static int VersionRank(string since)
        {
            if (since == "agnostic")
                return -1;
            var digits = new string([.. since.Where(char.IsDigit)]);
            return int.TryParse(digits, out var version) ? version : int.MaxValue;
        }

        private static string Rate(IReadOnlyList<GoldenCaseResult> results, GoldenKind kind)
        {
            var ofKind = results.Where(r => r.Kind == kind).ToList();
            return ofKind.Count == 0 ? "—" : $"{ofKind.Sum(r => r.Successes)}/{ofKind.Sum(r => r.Runs)}";
        }

        private static string Scope(IReadOnlyList<GoldenCaseResult> results, GoldenKind kind)
        {
            var ofKind = results.Where(r => r.Kind == kind).ToList();
            if (ofKind.Count == 0)
                return "";
            var runs = ofKind[0].Runs;
            return $"({ofKind.Count} case{(ofKind.Count == 1 ? "" : "s")} × {runs} run{(runs == 1 ? "" : "s")})";
        }
    }
}
