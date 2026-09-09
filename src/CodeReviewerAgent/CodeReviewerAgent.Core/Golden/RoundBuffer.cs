namespace CodeReviewerAgent.Core.Golden;

/// <summary>Where one round sits in a run: which case, which prompt version, which repetition.</summary>
internal readonly record struct RoundAddress(int Case, int Side, int Run);

/// <summary>
/// The rounds of one golden run, and the arithmetic that maps a flat slot to the (case, side,
/// repetition) it belongs to.
/// <para>
/// It exists because that arithmetic was repeated in three places, and it is the kind of
/// duplication that does not fail loudly: a formula that drifts in one of the three attributes
/// the review of case 3 to case 4 and reports a rate that looks entirely plausible. Here it is
/// written once.
/// </para>
/// <para>
/// Slots start empty and are filled as answers come back. A slot that stays empty is a round that
/// never returned — on a finished run there are none, and on one that died part-way they are the
/// calls that were never made. Nothing here treats an empty slot as a zero: it is absent, and
/// <see cref="Completed"/> is the only way the contents are read.
/// </para>
/// </summary>
internal sealed class RoundBuffer(int cases, int runs, int sides)
{
    // Written from the parallel phase, one slot per iteration and never the same slot twice, so
    // the writes need no coordination. Reads all happen after that phase.
    private readonly ReviewResult?[] _slots = new ReviewResult?[cases * runs * sides];

    public int Length => _slots.Length;

    private int PerCase => runs * sides;

    /// <summary>The address of a flat slot. The inverse of <see cref="SlotOf"/>.</summary>
    public RoundAddress Locate(int slot) =>
        // Case and side are both whole multiples of runs in the slot formula, so what is left
        // over after dividing by runs is the repetition within the (case, side) pair.
        new(slot / PerCase, slot % PerCase / runs, slot % runs);

    public int SlotOf(int caseIndex, int sideIndex, int runIndex) =>
        (caseIndex * PerCase) + (sideIndex * runs) + runIndex;

    public void Record(int slot, ReviewResult review) => _slots[slot] = review;

    /// <summary>The rounds that came back for one case under one prompt version, in order.</summary>
    public List<ReviewResult> Side(int caseIndex, int sideIndex) =>
        [.. Range(SlotOf(caseIndex, sideIndex, 0), runs)];

    /// <summary>Every round that came back, with the address it landed at.</summary>
    public IEnumerable<(RoundAddress Address, ReviewResult Review)> Completed()
    {
        for (var slot = 0; slot < _slots.Length; slot++)
            if (_slots[slot] is { } review)
                yield return (Locate(slot), review);
    }

    private IEnumerable<ReviewResult> Range(int start, int count)
    {
        for (var slot = start; slot < start + count; slot++)
            if (_slots[slot] is { } review)
                yield return review;
    }
}
