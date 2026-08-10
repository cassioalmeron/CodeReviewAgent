using CodeReviewerAgent.Core;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    /// <summary>
    /// The trigger eval over the bundled cases, driven by stub selectors — no LLM involved, so the
    /// pass rule and the plumbing are exercised without spending a call.
    /// </summary>
    public class SkillTriggerEvaluatorTests
    {
        private static readonly string[] Catalog = ["csharp", "react"];

        /// <summary>Always answers the same names, whatever the case.</summary>
        private sealed class FixedSelector(params string[] names) : ISkillSelector
        {
            public SkillSelection Select(IReadOnlyList<SkillRef> catalog, IReadOnlyList<string> files) =>
                new(names);
        }

        /// <summary>Answers the skills the case expects — a perfect selector.</summary>
        private sealed class OracleSelector : ISkillSelector
        {
            public List<IReadOnlyList<string>> SeenFiles { get; } = [];

            public SkillSelection Select(IReadOnlyList<SkillRef> catalog, IReadOnlyList<string> files)
            {
                SeenFiles.Add(files);
                var names = new List<string>();
                if (files.Any(f => f.EndsWith(".cs")))
                    names.Add("csharp");
                if (files.Any(f => f.EndsWith(".tsx") || f.EndsWith(".ts")))
                    names.Add("react");
                return new SkillSelection(names);
            }
        }

        [Fact]
        public void Run_ScoresEveryBundledCase()
        {
            var results = SkillTriggerEvaluator.Run(new FixedSelector());

            Assert.Equal(10, results.Count);
            Assert.Contains(results, r => r.Name == "csharp-braces" && r.Expected.SequenceEqual(["csharp"]));
            Assert.Contains(results, r => r.Name == "fullstack" && r.Expected.Count == 2);
            // Both sets are represented, so the train/validation split stays meaningful.
            Assert.Contains(results, r => r.Set == "train");
            Assert.Contains(results, r => r.Set == "validation");
        }

        [Fact]
        public void Run_FeedsTheSelectorTheFilesOfEachDiff()
        {
            var selector = new OracleSelector();

            SkillTriggerEvaluator.Run(selector);

            Assert.Contains(selector.SeenFiles, files => files.SequenceEqual(["src/Api/UserController.cs"]));
            // The full-stack case must arrive as one diff with both files, not split in two.
            Assert.Contains(selector.SeenFiles, files => files.Count == 2
                && files.Any(f => f.EndsWith(".cs")) && files.Any(f => f.EndsWith(".tsx")));
        }

        [Fact]
        public void Run_WithASelectorThatAlwaysSaysCsharp_FailsTheNegativeCases()
        {
            var results = SkillTriggerEvaluator.Run(new FixedSelector("csharp"));

            Assert.True(Result(results, "csharp-braces").Passed(Catalog));
            // A .cs comment-only diff and a SQL migration expect nothing: over-triggering is a fail.
            Assert.False(Result(results, "csharp-comment-only").Passed(Catalog));
            Assert.False(Result(results, "sql-migration").Passed(Catalog));
            // React cases expect react, which never triggers here.
            Assert.False(Result(results, "react-component").Passed(Catalog));
        }

        [Fact]
        public void Run_WithASelectorThatSaysNothing_PassesOnlyTheNegatives()
        {
            var results = SkillTriggerEvaluator.Run(new FixedSelector());

            Assert.True(Result(results, "sql-migration").Passed(Catalog));
            Assert.True(Result(results, "python-script").Passed(Catalog));
            Assert.False(Result(results, "csharp-braces").Passed(Catalog));
        }

        [Fact]
        public void Run_CountsTriggersPerRun()
        {
            var previous = Environment.GetEnvironmentVariable("SKILL_EVAL_RUNS");
            Environment.SetEnvironmentVariable("SKILL_EVAL_RUNS", "4");
            try
            {
                var result = Result(SkillTriggerEvaluator.Run(new FixedSelector("csharp")), "csharp-braces");

                Assert.Equal(4, result.Runs);
                Assert.Equal(4, result.Triggers["csharp"]);
                Assert.Equal(0, result.Triggers["react"]);
                Assert.Equal(1.0, result.Rate("csharp"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("SKILL_EVAL_RUNS", previous);
            }
        }

        [Fact]
        public void Passed_UsesTheThresholdNotAllOrNothing()
        {
            // 2 of 3 runs is above 0.5, so an expected skill still counts as triggered.
            var flaky = new SkillTriggerResult(
                "flaky", "train", ["csharp"],
                new Dictionary<string, int> { ["csharp"] = 2, ["react"] = 0 }, 3);
            var rare = flaky with { Triggers = new Dictionary<string, int> { ["csharp"] = 1, ["react"] = 0 } };

            Assert.True(flaky.Passed(Catalog));
            Assert.False(rare.Passed(Catalog));
        }

        /// <summary>Answers like a model whose response could not be read: nothing selected.</summary>
        private sealed class UnreadableSelector : ISkillSelector
        {
            public SkillSelection Select(IReadOnlyList<SkillRef> catalog, IReadOnlyList<string> files) =>
                SkillSelection.None with { Unreadable = true };
        }

        [Fact]
        public void Run_CountsTheRunsWhoseAnswerCouldNotBeRead()
        {
            var read = SkillTriggerEvaluator.Run(new FixedSelector("csharp"));
            var unreadable = SkillTriggerEvaluator.Run(new UnreadableSelector());

            Assert.All(read, r => Assert.Equal(0, r.Unreadable));
            Assert.All(unreadable, r => Assert.Equal(r.Runs, r.Unreadable));
        }

        [Fact]
        public void Report_FlagsAReportBuiltOnUnreadableAnswers()
        {
            var report = SkillTriggerEvaluator.Generate(
                SkillTriggerEvaluator.Run(new UnreadableSelector()), Catalog);

            // Negative cases "pass" when nothing is selected, so a run that never answered would
            // otherwise look like a good score.
            Assert.Contains("unreadable", report, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Answers with a fixed usage, so the accounting can be asserted exactly.</summary>
        private sealed class MeteredSelector : ISkillSelector
        {
            public SkillSelection Select(IReadOnlyList<SkillRef> catalog, IReadOnlyList<string> files) =>
                new(["csharp"], InputTokens: 300, OutputTokens: 20, Cost: 0.0004m);
        }

        [Fact]
        public void Run_SumsTheUsageOfEveryRunOfACase()
        {
            var previous = Environment.GetEnvironmentVariable("SKILL_EVAL_RUNS");
            Environment.SetEnvironmentVariable("SKILL_EVAL_RUNS", "2");
            try
            {
                var result = Result(SkillTriggerEvaluator.Run(new MeteredSelector()), "csharp-braces");

                Assert.Equal(600, result.InputTokens);
                Assert.Equal(40, result.OutputTokens);
                Assert.Equal(0.0008m, result.Cost);
                // Measured around the call, so it exists even for strategies that report no usage.
                Assert.True(result.LatencyMs >= 0);
            }
            finally
            {
                Environment.SetEnvironmentVariable("SKILL_EVAL_RUNS", previous);
            }
        }

        [Fact]
        public void Report_ReportsTokensCostAndLatency()
        {
            var report = SkillTriggerEvaluator.Generate(
                SkillTriggerEvaluator.Run(new MeteredSelector()), Catalog);

            Assert.Contains("## Cost", report);
            Assert.Contains("Tokens", report);
            Assert.Contains("Latency", report);
            Assert.Contains("**Total**", report);
        }

        [Fact]
        public void Report_WritesAReportWithTheRatesPerSet()
        {
            var report = SkillTriggerEvaluator.Generate(
                SkillTriggerEvaluator.Run(new FixedSelector("csharp")), Catalog);
            Assert.Contains("# Skill trigger eval", report);
            Assert.Contains("csharp-braces", report);
            Assert.Contains("**train**", report);
            Assert.Contains("**validation**", report);
        }

        private static SkillTriggerResult Result(IReadOnlyList<SkillTriggerResult> results, string name) =>
            results.Single(r => r.Name == name);
    }
}
