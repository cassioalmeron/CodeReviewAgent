using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Second evaluation stage: loads the reviews persisted by <c>eval</c> and scores their
    /// quality with the LLM-as-judge (a stronger model than the executor). Runs independently
    /// so the executor does not have to be re-invoked. Report formatting lives in
    /// <see cref="JudgeReportGenerator"/>.
    /// </summary>
    public static class JudgeRunner
    {
        private static readonly JsonSerializerOptions LoadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static void Run()
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
            var rubricVersion = Environment.GetEnvironmentVariable("RUBRIC_VERSION") ?? "v1";
            var judge = new Judge(LlmClientFactory.CreateClaude(judgeModel), rubricVersion);

            System.Console.WriteLine("=== Judge ===");
            // Group reviews by diff (= golden case); each diff was reviewed several times.
            var groups = new List<JudgeReportGroup>();
            foreach (var group in reviews.GroupBy(r => r.Diff ?? ""))
            {
                var label = FirstFile(group.Key);
                var outcomes = group.Select(r => judge.Evaluate(r.Diff ?? "", r)).ToList();
                groups.Add(new JudgeReportGroup(label, group.Key, outcomes));
                System.Console.WriteLine($"{label}: overall {Utils.FormatScore(Avg(outcomes, j => j.Overall))}");
            }

            var allOutcomes = groups.SelectMany(g => g.Outcomes).ToList();
            System.Console.WriteLine(
                $"Judge: overall {Utils.FormatScore(Avg(allOutcomes, j => j.Overall))} across {allOutcomes.Count} reviews — " +
                $"{Utils.Money(allOutcomes.Sum(o => o.Cost))}, {allOutcomes.Sum(o => o.LatencyMs)} ms");

            var reportPath = JudgeReportGenerator.Save(groups, judgeModel, rubricVersion);
            System.Console.WriteLine($"Judge report saved to {reportPath}");
        }

        private static double Avg(IReadOnlyList<JudgeOutcome> outcomes, Func<Judgment, int> selector) =>
            outcomes.Count == 0 ? 0 : outcomes.Average(o => selector(o.Judgment));

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
