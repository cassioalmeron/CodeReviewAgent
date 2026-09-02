using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Diff;
using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// Guards the bundled golden fixtures. A ruler whose cases rot in silence stops being a ruler,
/// and the failure mode that matters most is a trap whose bait no longer exists in its diff:
/// nothing can fall for it, so the case passes for the wrong reason.
/// </summary>
public class GoldenFixtureTests
{
    private static readonly IReadOnlyList<GoldenCase> Cases = GoldenEvaluator.LoadCases();

    [Fact]
    public void EveryCaseDeserializesWithAnExpectation()
    {
        Assert.NotEmpty(Cases);
        Assert.All(Cases, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.NotNull(c.Expect);
        });
    }

    [Fact]
    public void EveryCaseHasItsDiff()
    {
        Assert.All(Cases, c =>
            Assert.True(File.Exists(Path.Combine(GoldenEvaluator.CasesDirectory, c.Diff)),
                $"case '{c.Name}' points at a missing diff: {c.Diff}"));
    }

    [Fact]
    public void EveryCaseHasDocumentedGroundTruth()
    {
        Assert.All(Cases, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.GroundTruth), $"case '{c.Name}' has no ground truth file");
            var path = Path.Combine(GoldenEvaluator.CasesDirectory, c.GroundTruth);
            Assert.True(File.Exists(path), $"case '{c.Name}' points at a missing ground truth: {c.GroundTruth}");
            Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(path)), $"case '{c.Name}' has an empty ground truth");
        });
    }

    [Fact]
    public void NoGroundTruthFileIsOrphaned()
    {
        var referenced = Cases.Select(c => c.GroundTruth).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var onDisk = Directory.EnumerateFiles(GoldenEvaluator.CasesDirectory, "*.md").Select(Path.GetFileName);

        Assert.All(onDisk, file =>
            Assert.True(referenced.Contains(file!), $"'{file}' documents no case — dead fixture"));
    }

    /// <summary>
    /// Decision 7: the bait is anchored by snippet, not by line number, precisely so this check
    /// is possible. Decision 6 adds the rest: <see cref="FindingValidator"/> drops findings that
    /// do not cite an <b>added</b> line, so a bait sitting on a context line could never be
    /// fallen for.
    /// </summary>
    [Fact]
    public void EveryTrapBaitSitsOnAnAddedLineOfItsDiff()
    {
        var traps = Cases.Where(c => c.Expect is ExpectNoFinding).ToList();

        Assert.All(traps, c =>
        {
            var trap = (ExpectNoFinding)c.Expect;
            var diff = DiffParser.Parse(File.ReadAllText(Path.Combine(GoldenEvaluator.CasesDirectory, c.Diff)));

            var added = diff.Files
                .Where(f => f.Path is not null && f.Path.EndsWith(trap.File.Split('/').Last(), StringComparison.OrdinalIgnoreCase))
                .SelectMany(f => f.Hunks)
                .SelectMany(h => h.Lines)
                .Where(l => l.Kind == DiffLineKind.Added)
                .Select(l => Normalize(l.Text))
                .ToList();

            var bait = Normalize(trap.Snippet);
            Assert.True(added.Any(line => line.Contains(bait)),
                $"case '{c.Name}': the bait \"{trap.Snippet}\" is on no added line of {trap.File} — nothing can fall for it");
        });
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
