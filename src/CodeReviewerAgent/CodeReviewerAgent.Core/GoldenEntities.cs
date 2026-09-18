using CodeReviewerAgent.Core.Golden;

namespace CodeReviewerAgent.Core;

/// <summary>
/// One golden run: a single model, under one configuration, over the whole set. It is what the
/// <see cref="Assessment"/>s of that run hang from (<see cref="Assessment.RunId"/>), and without it
/// a stored assessment cannot say which execution produced it.
/// <para>
/// The regression rule of US-014 compares a case against the same case in an earlier run, so the
/// run has to be an entity: "the previous run of this model" is not derivable from assessments
/// that only know their own timestamp.
/// </para>
/// </summary>
public record GoldenRun
{
    public int Id { get; init; }
    public string Model { get; init; } = "";
    public string? Engine { get; init; }
    /// <summary>The skills condition of the run ("off", "globs", ...), not the selected skills of a round.</summary>
    public string? Skills { get; init; }
    public string? PromptVersion { get; init; }
    public DateTime StartedAt { get; init; }
    /// <summary>
    /// Wall-clock time of the whole run. Not derivable from the assessments: paid runs review several
    /// rounds at once, so the sum of their latencies is several times the real duration.
    /// </summary>
    public long DurationMs { get; init; }
    // The totals below are sums over the run's assessments. They are stored because a finished run
    // does not change, so they cannot go stale, and the list of runs should not aggregate every review.
    public decimal Cost { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    /// <summary>Nearest-rank percentiles of the assessments' latencies.</summary>
    public long LatencyP50Ms { get; init; }
    public long LatencyP95Ms { get; init; }
    public long LatencyP99Ms { get; init; }
    /// <summary>The generated markdown report of this run, so a row on screen can link to it.</summary>
    public string? ReportFile { get; init; }
    /// <summary>
    /// The verdict, which is the conjunction of every gate. Stored rather than derived because the
    /// list of runs shows one verdict per row and would otherwise join five gate rows for a boolean.
    /// </summary>
    public bool Approved { get; init; }
    public List<GoldenCaseScore>? Cases { get; init; }
    public List<GoldenGate>? Gates { get; init; }
}

/// <summary>
/// How one case went in one run. The columns mirror <see cref="GoldenCaseResult"/>, the in-memory
/// record the scorer produces, so the report and the database say the same thing with the same
/// words; the class is named differently only because both live in the same assembly.
/// </summary>
public record GoldenCaseScore
{
    public int Id { get; init; }
    public int RunId { get; init; }
    public string CaseName { get; init; } = "";
    public GoldenKind Kind { get; init; }
    public int Runs { get; init; }
    public int Successes { get; init; }
    public int CleanRounds { get; init; }
    public int PrecisionCorrect { get; init; }
    public int PrecisionCounted { get; init; }
    /// <summary>Rounds that got the severity exactly right (calibration distance zero).</summary>
    public int ExactCalibrations { get; init; }
    public int DiscardedFindings { get; init; }
    /// <summary>Four clean rounds in five, by <see cref="GoldenGates.CaseApproved"/>.</summary>
    public bool Approved { get; init; }
}

/// <summary>Which way a gate's floor points: everything is a minimum except trap noise.</summary>
public enum GateDirection { Min, Max }

/// <summary>
/// One of the five gates of ADR-015, as measured in one run.
/// <para>
/// The numbers are stored raw, not as the formatted text the report prints ("41/60 (68,3%)").
/// A chart cannot be drawn over that text, and two runs cannot be compared through it.
/// </para>
/// </summary>
public record GoldenGate
{
    public int Id { get; init; }
    public int RunId { get; init; }
    /// <summary>Detection, Trap resistance, Precision, Exact calibration, Trap noise.</summary>
    public string Name { get; init; } = "";
    public int Part { get; init; }
    /// <summary>Zero when the gate had nothing to measure, which is a failure and not a pass.</summary>
    public int Whole { get; init; }
    public int Floor { get; init; }
    public GateDirection Direction { get; init; }
    public bool Passed { get; init; }
}
