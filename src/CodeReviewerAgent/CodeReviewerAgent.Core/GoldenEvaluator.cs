using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core
{
    /// <summary>A golden case: a diff with a known, planted problem and its expectations.</summary>
    public record GoldenCase(
        string Name,
        string Diff,
        string File,
        Category Category,
        List<string> Keywords);

    /// <summary>The outcome of running one golden case N times through the agent.</summary>
    public record GoldenCaseResult(
        string Name,
        Category Category,
        int Detections,
        int Runs,
        string? MissDetail);

    /// <summary>
    /// Runs the golden set: each case is a diff with a planted problem. The agent reviews
    /// it and the result is checked (file + category + keyword) to see whether the problem
    /// was caught. This is the deterministic fitness function — the assertion lives in code,
    /// even though the agent's output does not.
    /// </summary>
    public static class GoldenEvaluator
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static IReadOnlyList<GoldenCaseResult> Run(
            ILlmClient client, IProjectRepository projects, IReviewRepository reviewsRepo, IAssessmentRepository assessments)
        {
            var runs = int.TryParse(Environment.GetEnvironmentVariable("GOLDEN_RUNS"), out var n) && n > 0 ? n : 3;

            // The golden set is its own project, so its reviews are kept apart from real repositories.
            var project = projects.GetOrAdd("golden", "Golden Set");

            var directory = Path.Combine(AppContext.BaseDirectory, "evaluations", "golden");
            var cases = JsonSerializer.Deserialize<List<GoldenCase>>(
                File.ReadAllText(Path.Combine(directory, "cases.json")), JsonOptions) ?? [];

            var results = new List<GoldenCaseResult>();
            var reviews = new List<ReviewResult>();
            // Per-review verdict (caught/missed), so each round in the report can be labelled.
            var verdicts = new Dictionary<ReviewResult, string>(ReferenceEqualityComparer.Instance);
            foreach (var golden in cases)
            {
                var diff = File.ReadAllText(Path.Combine(directory, golden.Diff));

                // The golden diff is stable: store it once (reused by content hash across runs),
                // then attach each run's assessment to it.
                var reviewId = reviewsRepo.GetOrAdd(new Review
                {
                    ProjectId = project.Id,
                    Content = diff,
                    Source = golden.Name,
                    CreatedAt = DateTime.UtcNow,
                });

                // Run each case multiple times: the LLM output is non-deterministic, so a
                // single run is a noisy sample. The detection rate (e.g. 2/3) is the signal.
                var detections = 0;
                string? lastMiss = null;
                for (var i = 0; i < runs; i++)
                {
                    var review = new CodeReviewer(client, diff).Review();
                    reviews.Add(review);
                    assessments.Save(Assessment.FromReview(reviewId, review));
                    var findings = review.Findings ?? [];

                    if (findings.Any(f => Matches(f, golden)))
                    {
                        detections++;
                        verdicts[review] = "✅ caught";
                    }
                    else
                    {
                        lastMiss = Describe(golden, findings);
                        verdicts[review] = "❌ missed";
                    }
                }

                results.Add(new GoldenCaseResult(golden.Name, golden.Category, detections, runs, lastMiss));
            }

            // One report covering every run: each round is labelled with its golden verdict,
            // and the detection-rate summary is appended as a footer.
            var reportPath = ReportGenerator.Save(
                [.. reviews], BuildFooter(results), r => verdicts.GetValueOrDefault(r));
            System.Console.WriteLine($"Report saved to {reportPath}");

            PersistReviews(reviews);

            return results;
        }

        // Persist the raw reviews so the judge can score them in a separate run, without
        // re-invoking the (paid) executor. The judge loads this file.
        private static void PersistReviews(List<ReviewResult> reviews)
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
        // PASS = caught every run, FAIL = never, FLAKY = caught some but not all.
        public static string FormatLine(GoldenCaseResult r)
        {
            var status = r.Detections == r.Runs ? "PASS" : r.Detections == 0 ? "FAIL" : "FLAKY";
            var detail = r.Detections == r.Runs ? "" : $" — {r.MissDetail}";
            return $"[{status}] {r.Name} ({r.Category}) {r.Detections}/{r.Runs}{detail}";
        }

        private static string BuildFooter(IReadOnlyList<GoldenCaseResult> results)
        {
            var footer = new StringBuilder();
            footer.AppendLine("# Golden set");
            footer.AppendLine();
            footer.AppendLine("```text");
            foreach (var r in results)
                footer.AppendLine(FormatLine(r));
            footer.AppendLine("```");
            footer.AppendLine();

            var detections = results.Sum(r => r.Detections);
            var total = results.Sum(r => r.Runs);
            var runs = results.Count > 0 ? results[0].Runs : 0;
            footer.AppendLine($"Golden set: {detections}/{total} detections ({results.Count} cases × {runs} runs)");
            return footer.ToString();
        }

        // The golden set measures DETECTION: a planted problem counts as "caught" when
        // some finding points at the right file and mentions an expected keyword. Whether
        // the category/severity is appropriate is a quality concern, left to the judge.
        private static bool Matches(Finding finding, GoldenCase golden) =>
            FileMatches(finding.File, golden.File)
            && KeywordMatches(finding, golden.Keywords);

        private static bool FileMatches(string? findingFile, string expected)
        {
            if (findingFile is null)
                return false;
            var path = findingFile.Replace('\\', '/').Trim();
            return path == expected
                || path.EndsWith('/' + expected)
                || expected.EndsWith('/' + path);
        }

        private static bool KeywordMatches(Finding finding, List<string> keywords)
        {
            var text = $"{finding.Problem} {finding.Suggestion}".ToLowerInvariant();
            return keywords.Any(k => text.Contains(k.ToLowerInvariant()));
        }

        private static string Describe(GoldenCase golden, List<Finding> findings)
        {
            var got = findings.Count == 0
                ? "no findings"
                : string.Join(", ", findings.Select(f => $"{f.Category} in {f.File}"));
            return $"expected a finding in {golden.File} mentioning a keyword; got: {got}";
        }
    }
}
