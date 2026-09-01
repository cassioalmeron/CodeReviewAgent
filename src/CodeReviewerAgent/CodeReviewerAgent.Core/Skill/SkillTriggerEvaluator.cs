using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// A labelled trigger case: a diff and the skills that should be selected for it.
    /// <see cref="Set"/> splits the cases into <c>train</c> (used to tune the descriptions) and
    /// <c>validation</c> (held back, so a tuned description is checked for generalization).
    /// </summary>
    public record SkillTriggerCase(
        string Name, string Diff, List<string> ExpectedSkills, string Set, string? Why = null);

    /// <summary>The outcome of running one trigger case N times: how often each skill was picked.</summary>
    public record SkillTriggerResult(
        string Name,
        string Set,
        IReadOnlyList<string> Expected,
        IReadOnlyDictionary<string, int> Triggers,
        int Runs,
        int Unreadable = 0,
        int InputTokens = 0,
        int OutputTokens = 0,
        decimal Cost = 0m,
        long LatencyMs = 0)
    {
        public double Rate(string skill) => Runs == 0 ? 0 : (double)Triggers.GetValueOrDefault(skill) / Runs;

        /// <summary>
        /// A case passes when every expected skill triggers above the threshold and every other
        /// skill of the catalog stays below it — the pass rule from the skill-evaluation method.
        /// </summary>
        public bool Passed(IReadOnlyList<string> catalog) =>
            catalog.All(skill => Expected.Contains(skill, StringComparer.OrdinalIgnoreCase)
                ? Rate(skill) > SkillTriggerEvaluator.Threshold
                : Rate(skill) <= SkillTriggerEvaluator.Threshold);
    }

    /// <summary>
    /// Measures whether the right skills get selected, and nothing else — the trigger eval of the
    /// Agent Skills method, adapted to diffs instead of chat prompts.
    /// <para>
    /// It runs <b>only the selection step</b>, never a review: a run costs a few hundred tokens
    /// instead of a full analysis, so the descriptions can be tuned in a tight loop. Because it
    /// goes through <see cref="SkillSelectorFactory"/>, running it with <c>SKILLS=globs</c> scores
    /// the mechanical strategy on the same cases, for free.
    /// </para>
    /// </summary>
    public static class SkillTriggerEvaluator
    {
        /// <summary>A skill counts as triggered for a case when it is picked in more than half the runs.</summary>
        public const double Threshold = 0.5;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public static IReadOnlyList<SkillTriggerResult> Run(ISkillSelector selector)
        {
            var runs = int.TryParse(Environment.GetEnvironmentVariable("SKILL_EVAL_RUNS"), out var n) && n > 0 ? n : 3;
            var directory = Path.Combine(AppContext.BaseDirectory, "assets", "evals", "triggers");
            var cases = JsonSerializer.Deserialize<List<SkillTriggerCase>>(
                File.ReadAllText(Path.Combine(directory, "cases.json")), JsonOptions) ?? [];

            var (catalog, _) = SkillCatalog.Discover();
            var results = new List<SkillTriggerResult>();

            foreach (var triggerCase in cases)
            {
                // Same input the review pipeline would give the selector: markdown filtered out,
                // then the file paths of the diff.
                var diff = DiffFilter.ExcludeMarkdown(
                    File.ReadAllText(Path.Combine(directory, triggerCase.Diff)));
                var files = DiffSplitter.ByFile(diff).Select(f => f.Path).Distinct().ToList();

                var triggers = catalog.ToDictionary(s => s.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
                var unreadable = 0;
                int inputTokens = 0, outputTokens = 0;
                var cost = 0m;

                // Timed here rather than inside the strategy, so the mechanical ones are measured
                // on the same clock as the model-driven one.
                var stopwatch = Stopwatch.StartNew();
                for (var i = 0; i < runs; i++)
                {
                    var selection = selector.Select(catalog, files);
                    if (selection.Unreadable)
                        unreadable++;
                    foreach (var name in selection.Names)
                        triggers[name]++;
                    inputTokens += selection.InputTokens;
                    outputTokens += selection.OutputTokens;
                    cost += selection.Cost;
                }
                stopwatch.Stop();

                results.Add(new SkillTriggerResult(
                    triggerCase.Name, triggerCase.Set, triggerCase.ExpectedSkills, triggers, runs, unreadable,
                    inputTokens, outputTokens, cost, stopwatch.ElapsedMilliseconds));
            }

            return results;
        }

        /// <summary>One result line: the verdict, the expectation and the observed rates.</summary>
        public static string FormatLine(SkillTriggerResult r, IReadOnlyList<string> catalog)
        {
            var expected = r.Expected.Count == 0 ? "none" : string.Join("+", r.Expected);
            var rates = string.Join(" ", catalog.Select(s => $"{s}={r.Triggers.GetValueOrDefault(s)}/{r.Runs}"));
            var unreadable = r.Unreadable > 0 ? $"  ! {r.Unreadable}/{r.Runs} unreadable" : "";
            return $"[{(r.Passed(catalog) ? "PASS" : "FAIL")}] {r.Name} ({r.Set}) expected {expected} — {rates}{unreadable}";
        }

        /// <summary>The one-line cost summary printed to the console.</summary>
        public static string FormatTotals(IReadOnlyList<SkillTriggerResult> results)
        {
            var runs = results.Sum(r => r.Runs);
            var latency = runs == 0 ? 0 : results.Sum(r => r.LatencyMs) / runs;
            return $"{runs} runs · {results.Sum(r => r.InputTokens)} in / {results.Sum(r => r.OutputTokens)} out "
                + $"tokens · ${results.Sum(r => r.Cost):F6} · {latency} ms/run";
        }

        /// <summary>Writes the markdown report and returns its path.</summary>
        public static string Save(IReadOnlyList<SkillTriggerResult> results, IReadOnlyList<string> catalog)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "reports");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"skill-triggers-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.md");
            File.WriteAllText(path, Generate(results, catalog));
            return path;
        }

        /// <summary>Renders the markdown report. Pure — <see cref="Save"/> is this plus a write.</summary>
        public static string Generate(IReadOnlyList<SkillTriggerResult> results, IReadOnlyList<string> catalog)
        {
            var report = new StringBuilder();
            report.AppendLine("# Skill trigger eval");
            report.AppendLine();
            report.AppendLine($"Runs per case: {(results.Count > 0 ? results[0].Runs : 0)} · threshold: {Threshold}");
            report.AppendLine();
            report.AppendLine($"| Case | Set | Expected | {string.Join(" | ", catalog)} | Unreadable | Verdict |");
            report.AppendLine($"|---|---|---|{string.Concat(catalog.Select(_ => "---|"))}---|---|");
            foreach (var r in results)
            {
                var expected = r.Expected.Count == 0 ? "—" : string.Join(", ", r.Expected);
                var rates = string.Join(" | ", catalog.Select(s => $"{r.Rate(s):P0}"));
                var unreadable = r.Unreadable == 0 ? "—" : $"{r.Unreadable}/{r.Runs}";
                report.AppendLine($"| {r.Name} | {r.Set} | {expected} | {rates} | {unreadable} | {(r.Passed(catalog) ? "PASS" : "FAIL")} |");
            }
            report.AppendLine();

            var unreadableRuns = results.Sum(r => r.Unreadable);
            if (unreadableRuns > 0)
            {
                report.AppendLine(
                    $"> **{unreadableRuns}/{results.Sum(r => r.Runs)} runs returned an unreadable answer** and "
                    + "therefore selected nothing. A case expecting no skills passes either way, so that many "
                    + "PASS verdicts below reflect a missing answer rather than a correct decision.");
                report.AppendLine();
            }

            foreach (var set in results.Select(r => r.Set).Distinct())
            {
                var inSet = results.Where(r => r.Set == set).ToList();
                var passed = inSet.Count(r => r.Passed(catalog));
                report.AppendLine($"- **{set}**: {passed}/{inSet.Count} passed ({(double)passed / inSet.Count:P0})");
            }

            AppendCost(report, results);
            return report.ToString();
        }

        // What the measurement itself cost. The selection call is meant to be cheap, so this is
        // also the evidence for that claim — and the basis for estimating a full comparison run.
        private static void AppendCost(StringBuilder report, IReadOnlyList<SkillTriggerResult> results)
        {
            report.AppendLine();
            report.AppendLine("## Cost");
            report.AppendLine();
            report.AppendLine("| Case | Runs | Tokens (in / out) | Cost (USD) | Latency (avg/run) |");
            report.AppendLine("|---|---|---|---|---|");
            foreach (var r in results)
                report.AppendLine(
                    $"| {r.Name} | {r.Runs} | {r.InputTokens} / {r.OutputTokens} | {r.Cost:F6} | "
                    + $"{(r.Runs == 0 ? 0 : r.LatencyMs / r.Runs)} ms |");

            var totalRuns = results.Sum(r => r.Runs);
            report.AppendLine(
                $"| **Total** | **{totalRuns}** | **{results.Sum(r => r.InputTokens)} / "
                + $"{results.Sum(r => r.OutputTokens)}** | **{results.Sum(r => r.Cost):F6}** | "
                + $"**{(totalRuns == 0 ? 0 : results.Sum(r => r.LatencyMs) / totalRuns)} ms** |");
        }
    }
}
