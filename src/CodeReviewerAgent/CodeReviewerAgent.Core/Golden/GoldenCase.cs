using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core.Golden;

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
/// <param name="Severity">
/// How grave the planted problem actually is, which is what makes calibration measurable: a run
/// that catches a SQL injection and files it as <c>Info</c> detected exactly as well as one that
/// filed it <c>Critical</c>, and only this field can tell them apart.
/// <para>
/// Nullable so that a case written without it fails loudly at load instead of defaulting to
/// <c>Info</c> — an enum missing from JSON deserializes to zero, and a silent expected value is
/// a silent wrong measurement. <c>GoldenEvaluator.LoadCases</c> rejects it.
/// </para>
/// </param>
public sealed record ExpectFinding(
    string File, Category Category, List<string> Keywords, Severity? Severity = null) : GoldenExpectation;

/// <summary>
/// Something the diff makes legitimate to point out beyond the planted problem, so that finding
/// it is not counted as noise.
/// <para>
/// It exists because the measurement of 16/08 found one: on <c>null-dereference</c> the model
/// remarked, in all five rounds, that the diff also changes the returned value by applying
/// <c>ToUpperInvariant</c>. That remark is correct and is not in the case's ground truth. Without
/// this list, precision punishes the model for being right, and punishes hardest the model that
/// notices what the case author did not.
/// </para>
/// </summary>
/// <param name="Why">
/// Where the entry came from. Not decoration: six months on, nothing else distinguishes a
/// considered entry from one added to silence an inconvenient false positive.
/// </param>
public sealed record AcceptableFinding(string File, List<string> Keywords, string? Why = null);

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
/// <param name="AlsoAcceptable">
/// What else this diff makes legitimate to report. Absent means nothing else is: every other
/// finding is either a duplicate of one already counted, or unforeseen. Applies to trap cases
/// too — a correct diff can still carry something worth remarking on.
/// </param>
public record GoldenCase(
    string Name,
    string Diff,
    string GroundTruth,
    GoldenExpectation Expect,
    string? Since = null,
    List<AcceptableFinding>? AlsoAcceptable = null);

public enum GoldenKind { Detection, Trap }

/// <summary>
/// What one finding turned out to be. The three metrics are counts over these, so every rate the
/// report prints can be traced back to the findings that produced it.
/// </summary>
public enum FindingVerdict
{
    /// <summary>The problem the case plants. At most one per round; the rest are duplicates.</summary>
    Planted,

    /// <summary>Something the case's <c>AlsoAcceptable</c> list declares legitimate to report.</summary>
    Acceptable,

    /// <summary>A second finding for a problem already counted: fragmentation.</summary>
    Duplicate,

    /// <summary>
    /// Neither planted, nor listed, nor a repeat. On a detection case this is deliberately not a
    /// penalty — it may well be correct, and the list is what is incomplete — so it is reported
    /// for a human to read and promote. On a trap it counts against precision, because there the
    /// diff is correct by construction.
    /// </summary>
    Unforeseen,

    /// <summary>The trap's bait, flagged as if it were a bug. Trap cases only.</summary>
    Bait,
}

public record FindingOutcome(Finding Finding, FindingVerdict Verdict);

/// <summary>One round that came back, with the case and diff it belongs to.</summary>
public record CompletedRound(string Case, string Diff, ReviewResult Review);

/// <summary>
/// What a run produced, whether or not it finished. A run that dies part-way is not an
/// exceptional situation here — it is a quota exhausted on round 55 of 60, which this project has
/// lived through — so it is a value the caller receives rather than an exception that unwinds
/// past the rounds already bought.
/// </summary>
/// <param name="Score">
/// The scored run, or null when the run did not finish. Null on purpose: a detection rate over a
/// partial set is not a smaller truth, it is a wrong number, so it is never offered.
/// </param>
/// <param name="Completed">
/// Every round that came back, finished or not. This is what persistence is written from, and
/// what makes a failed run still worth something.
/// </param>
/// <param name="Failure">What went wrong, kept rather than swallowed, so the caller can report it.</param>
/// <param name="WholeSet">
/// False when a filter narrowed the run to some cases. Such a run is a tuning pass, not a verdict on
/// the model: its gates have nothing to stand on, so it is never recorded as a golden run.
/// </param>
/// <param name="StartedAt">When the run started, in UTC.</param>
/// <param name="DurationMs">
/// Wall-clock time of the reviews. Not the sum of their latencies, which counts parallel rounds once
/// each; with two prompt versions it is the time of both sides together.
/// </param>
public record GoldenRunResult(
    GoldenScore? Score,
    IReadOnlyList<CompletedRound> Completed,
    Exception? Failure,
    bool WholeSet = true,
    DateTime StartedAt = default,
    long DurationMs = 0)
{
    public bool Succeeded => Failure is null;
}

/// <summary>
/// One round on all three axes. <see cref="Succeeded"/> is the old boolean, kept because it is
/// still what detection and trap resistance are; the rest is what the old ruler could not see.
/// </summary>
/// <param name="CalibrationDistance">
/// Emitted severity minus expected, on the <see cref="FindingVerdict.Planted"/> finding. Zero is
/// exact, positive is inflated, negative is understated. Null when there is nothing to compare:
/// the round missed the planted problem, or the case is a trap and has no expected severity.
/// </param>
public record RoundScore(
    GoldenKind Kind,
    bool Succeeded,
    IReadOnlyList<FindingOutcome> Outcomes,
    int? CalibrationDistance)
{
    public int Count(FindingVerdict verdict) => Outcomes.Count(o => o.Verdict == verdict);

    /// <summary>Findings that needed saying.</summary>
    public int PrecisionCorrect => Count(FindingVerdict.Planted) + Count(FindingVerdict.Acceptable);

    /// <summary>
    /// Findings the precision rate is taken over, and the one place the two kinds of case differ.
    /// On a detection case the unforeseen are excluded from both sides, so an observation the
    /// case author never anticipated neither helps nor hurts. On a trap everything counts, because
    /// the diff is correct: there is no such thing as a finding that needed saying, beyond what
    /// the list allows.
    /// </summary>
    public int PrecisionCounted => Kind == GoldenKind.Detection
        ? PrecisionCorrect + Count(FindingVerdict.Duplicate)
        : Outcomes.Count;

    /// <summary>
    /// A round with nothing to answer for: it found the planted problem or resisted the trap, said
    /// nothing that counts against precision, and got the severity exactly right.
    /// <para>
    /// One rule for both kinds of case, because precision already carries the difference. On a
    /// detection case that means no duplicate, and an unforeseen remark does not spoil the round,
    /// since it is outside precision on both sides. On a trap every finding counts, so a clean
    /// round is a silent one. A trap has no expected severity, so there is nothing to calibrate.
    /// </para>
    /// </summary>
    public bool IsClean =>
        Succeeded && PrecisionCounted == PrecisionCorrect && CalibrationDistance is null or 0;
}

/// <summary>
/// The outcome of running one golden case N times. <see cref="Successes"/> means detections for
/// a <see cref="GoldenKind.Detection"/> case and runs that resisted for a
/// <see cref="GoldenKind.Trap"/> — two different things, which is exactly why they are never
/// summed into a single rate.
/// </summary>
/// <param name="PrecisionCorrect">
/// Findings that needed saying, summed over the runs. Precision aggregates over findings, not
/// over per-run rates: a run that emitted four wrong findings has to weigh more than one that
/// emitted a single wrong finding, and averaging rates would flatten exactly that.
/// </param>
/// <param name="CalibrationDistances">
/// One signed distance per run that had something to calibrate. Kept as the list, not as a mean,
/// because the mean alone lies: a run at +1 and another at −1 average to zero, which reads as
/// perfect calibration when neither run was right.
/// </param>
/// <param name="Unforeseen">
/// Findings that were neither planted, listed, nor repeats. On a detection case these carry no
/// penalty; they are here so a human can read them and decide what belongs in
/// <c>AlsoAcceptable</c>. This is how the list is meant to grow.
/// </param>
/// <param name="CleanRounds">
/// Rounds with nothing to answer for (<see cref="RoundScore.IsClean"/>). Counted here, while each
/// round is still at hand: the calibration list only holds rounds that found something, so it
/// cannot be lined up with the rounds afterwards to recover this.
/// </param>
/// <param name="DiscardedFindings">
/// Findings the grounding dropped, summed over the runs. They never reach any verdict here, so a
/// detection miss can be a model that saw nothing or one that cited the wrong line; this count is
/// what tells the two apart.
/// </param>
public record GoldenCaseResult(
    string Name,
    GoldenKind Kind,
    string? Since,
    string PromptVersion,
    int Successes,
    int Runs,
    string? MissDetail,
    int PrecisionCorrect = 0,
    int PrecisionCounted = 0,
    IReadOnlyList<int>? CalibrationDistances = null,
    IReadOnlyList<Finding>? Unforeseen = null,
    int CleanRounds = 0,
    int DiscardedFindings = 0)
{
    /// <summary>Findings that should not have been said, summed over the runs.</summary>
    public int FindingsAgainst => PrecisionCounted - PrecisionCorrect;
}

/// <summary>
/// Everything a finished golden run produced, with no side effect attached: the per-case
/// verdicts, the raw reviews, the harness condition, and the label for each round.
/// Publishing it is a separate step (<c>GoldenEvaluatorReport.SaveReport</c>), so running the set
/// touches nothing on disk.
/// </summary>
public record GoldenScore(
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
