namespace CodeReviewerAgent.Core.Golden;

/// <summary>
/// One of the five gates: what the run measured, the floor, and whether it cleared it. The numbers
/// are kept raw, because the database stores them and a chart compares them; the report's wording is
/// formatted from them.
/// </summary>
/// <param name="Part">What counted: bugs caught, traps resisted, findings, or findings on traps.</param>
/// <param name="Whole">What it is counted over. Zero means there was nothing to measure, which fails.</param>
/// <param name="Floor">A percentage for <see cref="GateDirection.Min"/>, a count per round for <see cref="GateDirection.Max"/>.</param>
public record Gate(string Name, int Part, int Whole, int Floor, GateDirection Direction, bool Passed)
{
    public string ValueText => Whole == 0
        ? "no data"
        : Direction == GateDirection.Min
            ? $"{Part}/{Whole} ({(double)Part / Whole:P1})"
            : $"{Part} in {Whole} rounds ({(double)Part / Whole:0.00} per round)";

    public string FloorText => Direction == GateDirection.Min ? $"≥ {Floor}%" : $"≤ {Floor} per round";
}

/// <summary>
/// A model's verdict over one golden run. Approved only when every gate passes: a model with 75%
/// detection and 30% precision averages to a mediocre-looking 52,5% while being a noise machine
/// that trips over bugs by volume, so no gate is ever averaged with another.
/// </summary>
public record ModelVerdict(IReadOnlyList<Gate> Gates)
{
    public bool Approved => Gates.All(g => g.Passed);
}

/// <summary>
/// The approval criterion of ADR-015, which answers two different questions: is a case approved
/// for a model, and is the model approved.
/// <para>
/// The floors came from the Haiku run of 2026-08-16, rescored with the composite metric. That
/// makes the reference pass by construction, so it enters a comparison as a declared baseline,
/// not as an approved model. Moving a floor after the model comparison has run is choosing the
/// cut that gives the answer already wanted, which is the one change this class must not see.
/// </para>
/// <para>
/// Every comparison is integer arithmetic: 80% of 5 rounds has to be exactly 4, not whatever
/// floating point makes of it, and a floor is only meaningful if its edge is.
/// </para>
/// </summary>
public static class GoldenGates
{
    // A case is approved with 4 clean rounds in 5. Written as a share so it stays defined when a
    // run uses another number of rounds. Not 5 in 5, because then the instrument's own variation
    // decides the verdict; not 3 in 5, because with five rounds that is close to a coin toss.
    public const int CleanRoundsRequired = 4;
    public const int CleanRoundsOutOf = 5;

    public const int MinDetectionPercent = 60;
    public const int MinTrapResistancePercent = 50;
    public const int MinPrecisionPercent = 85;
    public const int MinExactCalibrationPercent = 75;

    // A count, not a rate. The trap precision rate this replaces could only ever be 0/N: on a
    // trap nothing is planted and the acceptable lists are empty, so no finding can be one that
    // needed saying. What that rate was really counting was the findings, so that is the gate.
    public const int MaxTrapFindingsPerRound = 1;

    public static bool CaseApproved(GoldenCaseResult result) =>
        result.Runs > 0 && result.CleanRounds * CleanRoundsOutOf >= result.Runs * CleanRoundsRequired;

    public static ModelVerdict Judge(IReadOnlyList<GoldenCaseResult> results)
    {
        var detection = results.Where(r => r.Kind == GoldenKind.Detection).ToList();
        var traps = results.Where(r => r.Kind == GoldenKind.Trap).ToList();
        var distances = detection.SelectMany(r => r.CalibrationDistances ?? []).ToList();

        return new ModelVerdict(
        [
            AtLeast("Detection",
                detection.Sum(r => r.Successes), detection.Sum(r => r.Runs), MinDetectionPercent),
            AtLeast("Trap resistance",
                traps.Sum(r => r.Successes), traps.Sum(r => r.Runs), MinTrapResistancePercent),
            AtLeast("Precision",
                detection.Sum(r => r.PrecisionCorrect), detection.Sum(r => r.PrecisionCounted), MinPrecisionPercent),
            AtLeast("Exact calibration",
                distances.Count(d => d == 0), distances.Count, MinExactCalibrationPercent),
            AtMost("Trap noise",
                traps.Sum(r => r.FindingsAgainst), traps.Sum(r => r.Runs), MaxTrapFindingsPerRound),
        ]);
    }

    // A gate with nothing to measure fails rather than passes: a model that detected nothing has
    // no severity to have been right about, and that is not a perfect calibration.
    private static Gate AtLeast(string name, int part, int whole, int floorPercent) =>
        new(name, part, whole, floorPercent, GateDirection.Min, whole > 0 && part * 100 >= whole * floorPercent);

    private static Gate AtMost(string name, int findings, int rounds, int perRound) =>
        new(name, findings, rounds, perRound, GateDirection.Max, rounds > 0 && findings <= rounds * perRound);
}
