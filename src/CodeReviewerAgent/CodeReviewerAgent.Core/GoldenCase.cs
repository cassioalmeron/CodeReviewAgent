using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// What a golden case expects of the agent. Two shapes, because the set measures two opposite
    /// things — catching a planted problem, and leaving correct code alone. Keeping them apart in
    /// the type system is what stops a clean case from carrying keywords it has no use for.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(ExpectFinding), "finding")]
    [JsonDerivedType(typeof(ExpectNoFinding), "noFinding")]
    public abstract record GoldenExpectation;

    /// <summary>
    /// The diff has a planted problem: some finding must point at <paramref name="File"/> and
    /// mention one of the <paramref name="Keywords"/>. The category is not part of the match —
    /// whether it was labelled well is a quality question, left to the judge.
    /// </summary>
    public sealed record ExpectFinding(string File, Category Category, List<string> Keywords) : GoldenExpectation;

    /// <summary>
    /// The diff is correct. <paramref name="Snippet"/> is the bait: a construct some models mistake
    /// for an error. It is anchored by text rather than by line
    /// number so that editing the diff can never turn the case into a silent pass — the worst
    /// failure a ruler can have, because it makes no noise.
    /// </summary>
    public sealed record ExpectNoFinding(string File, string Snippet) : GoldenExpectation;

    /// <summary>
    /// A golden case: a diff, its documented ground truth, and what the agent is expected to do
    /// with it. <see cref="Since"/> is the C# version the case requires (null when the case is
    /// version-agnostic), which is what lets the report say <em>which</em> constructs a model
    /// handles badly instead of only that it failed.
    /// </summary>
    public record GoldenCase(
        string Name,
        string Diff,
        string GroundTruth,
        GoldenExpectation Expect,
        string? Since = null);

    public enum GoldenKind { Detection, Trap }

    /// <summary>
    /// The outcome of running one golden case N times. <see cref="Successes"/> means detections for
    /// a <see cref="GoldenKind.Detection"/> case and runs that resisted for a
    /// <see cref="GoldenKind.Trap"/> — two different things, which is exactly why they are never
    /// summed into a single rate.
    /// </summary>
    public record GoldenCaseResult(
        string Name,
        GoldenKind Kind,
        string? Since,
        string PromptVersion,
        int Successes,
        int Runs,
        string? MissDetail);

    /// <summary>
    /// Everything a finished golden run produced, with no side effect attached: the per-case
    /// verdicts, the raw reviews, the harness condition, and the label for each round.
    /// Publishing it is a separate step (<c>GoldenEvaluatorReport.SaveReport</c>), so running the set
    /// touches nothing on disk.
    /// </summary>
    public record GoldenRun(
        IReadOnlyList<GoldenCaseResult> Results,
        IReadOnlyList<ReviewResult> Reviews,
        GoldenCondition Condition,
        IReadOnlyDictionary<ReviewResult, string> Verdicts);

    /// <summary>
    /// Which harness configuration produced a golden run, and what actually reached the model.
    /// <para>
    /// The second half is not bookkeeping: a run whose skill selection came back unreadable
    /// selected nothing and is therefore baseline wearing the harness label. Averaging it in
    /// dilutes the very delta the two conditions exist to measure, so the count is reported.
    /// </para>
    /// </summary>
    public record GoldenCondition(
        string Setting,
        int Rounds,
        int RoundsWithSkill,
        IReadOnlyDictionary<string, int> SkillRounds)
    {
        public bool IsBaseline => Setting.Equals("off", StringComparison.OrdinalIgnoreCase);

        public string Label => IsBaseline ? "baseline" : "harness";

        public int RoundsWithoutSkill => Rounds - RoundsWithSkill;

        /// <summary>Reads the condition back from the runs themselves, not from what was intended.</summary>
        public static GoldenCondition From(IReadOnlyList<ReviewResult> reviews, string? setting)
        {
            var skillRounds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var withSkill = 0;

            foreach (var review in reviews)
            {
                var active = (review.Skills ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (active.Length > 0)
                    withSkill++;
                foreach (var name in active)
                    skillRounds[name] = skillRounds.GetValueOrDefault(name) + 1;
            }

            var value = string.IsNullOrWhiteSpace(setting) ? "all" : setting.Trim();
            return new GoldenCondition(value, reviews.Count, withSkill, skillRounds);
        }
    }
}
