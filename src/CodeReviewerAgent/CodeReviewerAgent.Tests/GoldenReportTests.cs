using CodeReviewerAgent.Core;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    /// <summary>
    /// The golden footer. Two rules carry the whole redesign: detection and trap resistance are
    /// never added together, and the report says which harness condition produced the numbers.
    /// </summary>
    public class GoldenReportTests
    {
        private static ReviewResult Review(string? skills) =>
            new("summary", [], "fake", "fake-model", "v3", 0m, 0, 0, 0, "diff", skills);

        private static GoldenCaseResult Detection(string name, int successes, int runs = 3, string? since = null) =>
            new(name, GoldenKind.Detection, since, successes, runs, null);

        private static GoldenCaseResult Trap(string name, int successes, int runs = 3, string? since = null) =>
            new(name, GoldenKind.Trap, since, successes, runs, null);

        [Fact]
        public void ReportsDetectionAndTrapResistanceSeparately()
        {
            var results = new[] { Detection("bug", 3), Detection("other", 2), Trap("clean", 1) };

            var footer = GoldenEvaluator.BuildFooter(results, GoldenCondition.From([], "off"));

            Assert.Contains("Detection", footer);
            Assert.Contains("5/6", footer);          // detections only
            Assert.Contains("Trap resistance", footer);
            Assert.Contains("1/3", footer);          // traps only
            Assert.DoesNotContain("6/9", footer);    // the sum must never appear
        }

        [Fact]
        public void OmitsTrapResistanceWhenThereAreNoTraps()
        {
            var footer = GoldenEvaluator.BuildFooter([Detection("bug", 3)], GoldenCondition.From([], "off"));

            Assert.Contains("Detection", footer);
            Assert.DoesNotContain("Trap resistance", footer);
        }

        [Fact]
        public void BreaksTheRatesDownByCSharpVersion()
        {
            var results = new[]
            {
                Detection("old", 3),
                Detection("modern", 1, since: "C# 12"),
                Trap("extension", 0, since: "C# 14"),
            };

            var footer = GoldenEvaluator.BuildFooter(results, GoldenCondition.From([], "off"));

            Assert.Contains("C# 12", footer);
            Assert.Contains("C# 14", footer);
            Assert.Contains("agnostic", footer);
        }

        /// <summary>
        /// The breakdown is a ladder: it exists to show <em>which</em> constructs a model handles
        /// badly, so it has to read oldest-first. A plain string sort puts "C# 8" after "C# 14".
        /// </summary>
        [Fact]
        public void OrdersTheVersionsChronologicallyNotAlphabetically()
        {
            var results = new[]
            {
                Detection("modern", 1, since: "C# 14"),
                Detection("older", 1, since: "C# 8"),
                Detection("mid", 1, since: "C# 11"),
                Detection("plain", 1),
            };

            var footer = GoldenEvaluator.BuildFooter(results, GoldenCondition.From([], "off"));

            var order = new[] { "agnostic", "C# 8", "C# 11", "C# 14" }.Select(v => footer.IndexOf($"| {v} |")).ToList();
            Assert.All(order, i => Assert.True(i > 0, "every version group should appear in the table"));
            Assert.Equal(order.OrderBy(i => i), order);
        }

        // --- Condition (decisions 11 and 12) ---

        [Fact]
        public void LabelsTheBaselineCondition()
        {
            var footer = GoldenEvaluator.BuildFooter([Detection("bug", 3)], GoldenCondition.From([], "off"));

            Assert.Contains("baseline", footer);
            Assert.Contains("SKILLS=off", footer);
        }

        [Fact]
        public void LabelsTheHarnessConditionWithTheSkillsThatWereActive()
        {
            var reviews = new[] { Review("csharp"), Review("csharp"), Review("csharp,react") };

            var footer = GoldenEvaluator.BuildFooter([Detection("bug", 3)], GoldenCondition.From(reviews, "all"));

            Assert.Contains("harness", footer);
            Assert.Contains("csharp (3/3)", footer);
            Assert.Contains("react (1/3)", footer);
        }

        /// <summary>
        /// Decision 12: a run whose skill selection came back unreadable selected nothing, so it is
        /// baseline wearing the harness label. Averaging it in dilutes the very delta the two
        /// conditions exist to measure, so the footer has to say it happened.
        /// </summary>
        [Fact]
        public void FlagsHarnessRoundsThatEndedUpWithNoSkill()
        {
            var reviews = new[] { Review("csharp"), Review(null), Review("") };

            var footer = GoldenEvaluator.BuildFooter([Detection("bug", 3)], GoldenCondition.From(reviews, "all"));

            Assert.Contains("2/3", footer);
            Assert.Contains("no skill", footer);
        }

        [Fact]
        public void DoesNotFlagMissingSkillsInTheBaselineCondition()
        {
            var reviews = new[] { Review(null), Review(null) };

            var footer = GoldenEvaluator.BuildFooter([Detection("bug", 3)], GoldenCondition.From(reviews, "off"));

            Assert.DoesNotContain("no skill", footer);
        }
    }
}
