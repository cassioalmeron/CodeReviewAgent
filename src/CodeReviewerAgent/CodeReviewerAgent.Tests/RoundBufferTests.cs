using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The slot arithmetic. It used to be written out in three separate methods, and it is the kind
/// of duplication that never fails loudly: a formula that drifts in one of them attributes the
/// review of case 3 to case 4 and reports a rate that looks entirely plausible. Now it is in one
/// place, so it can have a test.
/// </summary>
public class RoundBufferTests
{
    private static ReviewResult Review(string summary) => new(summary, []);

    /// <summary>Every slot maps to exactly one address, and back again.</summary>
    [Fact]
    public void LocateAndSlotOf_AreInverses()
    {
        var buffer = new RoundBuffer(cases: 4, runs: 3, sides: 2);

        Assert.Equal(24, buffer.Length);
        for (var slot = 0; slot < buffer.Length; slot++)
        {
            var (caseIndex, sideIndex, runIndex) = buffer.Locate(slot);
            Assert.Equal(slot, buffer.SlotOf(caseIndex, sideIndex, runIndex));
        }
    }

    /// <summary>
    /// The one that matters for correctness of the report: a case's two prompt versions must not
    /// bleed into each other, because the whole point of a pairwise run is scoring them apart.
    /// </summary>
    [Fact]
    public void Side_ReturnsOnlyTheRoundsOfThatCaseAndThatPromptVersion()
    {
        var buffer = new RoundBuffer(cases: 2, runs: 2, sides: 2);

        for (var slot = 0; slot < buffer.Length; slot++)
        {
            var (caseIndex, sideIndex, runIndex) = buffer.Locate(slot);
            buffer.Record(slot, Review($"c{caseIndex}s{sideIndex}r{runIndex}"));
        }

        Assert.Equal(["c0s0r0", "c0s0r1"], buffer.Side(0, 0).Select(r => r.Summary));
        Assert.Equal(["c0s1r0", "c0s1r1"], buffer.Side(0, 1).Select(r => r.Summary));
        Assert.Equal(["c1s0r0", "c1s0r1"], buffer.Side(1, 0).Select(r => r.Summary));
        Assert.Equal(["c1s1r0", "c1s1r1"], buffer.Side(1, 1).Select(r => r.Summary));
    }

    /// <summary>
    /// A run that died part-way leaves gaps. They are absent, never zero: the rounds that came
    /// back are returned, and the ones that never did are simply not there.
    /// </summary>
    [Fact]
    public void AHalfFilledBuffer_ReportsOnlyWhatCameBack()
    {
        var buffer = new RoundBuffer(cases: 3, runs: 2, sides: 1);
        buffer.Record(0, Review("first"));
        buffer.Record(1, Review("second"));
        buffer.Record(2, Review("third"));

        Assert.Equal(["first", "second", "third"], buffer.Completed().Select(r => r.Review.Summary));
        Assert.Equal([0, 0, 1], buffer.Completed().Select(r => r.Address.Case));

        // Case 1 got one of its two rounds; case 2 got none at all.
        Assert.Single(buffer.Side(1, 0));
        Assert.Empty(buffer.Side(2, 0));
    }
}
