namespace CodeReviewerAgent.Core.Golden;

/// <summary>
/// The golden verdict, as a pure function of the findings and the expectation — no LLM, no
/// store, no files. This is the deterministic half of the evaluation harness, and keeping it
/// free of I/O is what makes it testable without spending a call.
/// </summary>
public static class GoldenScorer
{
    public static bool Succeeded(IReadOnlyList<Finding> findings, GoldenExpectation expect) => expect switch
    {
        ExpectFinding detection => findings.Any(f => Matches(f, detection)),
        ExpectNoFinding trap => !findings.Any(f => FallsFor(f, trap)),
        _ => throw new ArgumentOutOfRangeException(nameof(expect), $"Unknown expectation: {expect.GetType().Name}"),
    };

    /// <summary>
    /// One round, scored on all three axes instead of one. <see cref="Succeeded"/> answers only
    /// the first — was the planted problem found — and two rounds that answer it identically can
    /// still differ in what else they said and how loudly, which is what the other two measure.
    /// <para>
    /// Every finding gets a verdict, because a rate is only defensible if you can point at which
    /// finding produced it.
    /// </para>
    /// </summary>
    public static RoundScore Score(IReadOnlyList<Finding> findings, GoldenCase golden)
    {
        var acceptable = golden.AlsoAcceptable ?? [];
        var acceptableSeen = new bool[acceptable.Count];
        var outcomes = new List<FindingOutcome>(findings.Count);
        var plantedSeen = false;
        var baitSeen = false;
        Finding? planted = null;

        foreach (var finding in findings)
        {
            var verdict = Classify(finding, golden.Expect, acceptable, acceptableSeen, ref plantedSeen, ref baitSeen);
            if (verdict == FindingVerdict.Planted)
                planted = finding;
            outcomes.Add(new FindingOutcome(finding, verdict));
        }

        var kind = golden.Expect is ExpectNoFinding ? GoldenKind.Trap : GoldenKind.Detection;

        // Only a round that found the planted problem has a severity to calibrate. A round that
        // missed it is already punished by detection, and asking how gravely it labelled
        // something it never said would be inventing a data point.
        int? distance = golden.Expect is ExpectFinding { Severity: { } expected } && planted?.Severity is { } actual
            ? (int)actual - (int)expected
            : null;

        return new RoundScore(kind, Succeeded(findings, golden.Expect), outcomes, distance);
    }

    private static FindingVerdict Classify(
        Finding finding, GoldenExpectation expect,
        IReadOnlyList<AcceptableFinding> acceptable, bool[] acceptableSeen,
        ref bool plantedSeen, ref bool baitSeen)
    {
        // The planted problem, or the bait, is checked first: a finding that is the thing the case
        // is about must never be classified as an incidental remark that happens to match a list.
        switch (expect)
        {
            case ExpectFinding detection when Matches(finding, detection):
            {
                // The second finding that matches the same expectation is the same problem said
                // twice. That is the fragmentation the rubric exists to punish, not a new finding.
                var repeat = plantedSeen;
                plantedSeen = true;
                return repeat ? FindingVerdict.Duplicate : FindingVerdict.Planted;
            }
            case ExpectNoFinding trap when FallsFor(finding, trap):
            {
                var repeat = baitSeen;
                baitSeen = true;
                return repeat ? FindingVerdict.Duplicate : FindingVerdict.Bait;
            }
        }

        for (var i = 0; i < acceptable.Count; i++)
        {
            if (!MatchesAcceptable(finding, acceptable[i]))
                continue;
            var repeat = acceptableSeen[i];
            acceptableSeen[i] = true;
            return repeat ? FindingVerdict.Duplicate : FindingVerdict.Acceptable;
        }

        return FindingVerdict.Unforeseen;
    }

    /// <summary>Same file-plus-keyword test as the planted problem; the list is ground truth too.</summary>
    public static bool MatchesAcceptable(Finding finding, AcceptableFinding acceptable) =>
        FileMatches(finding.File, acceptable.File) && KeywordMatches(finding, acceptable.Keywords);

    /// <summary>A planted problem counts as caught on file + keyword.</summary>
    public static bool Matches(Finding finding, ExpectFinding expect) =>
        FileMatches(finding.File, expect.File) && KeywordMatches(finding, expect.Keywords);

    /// <summary>
    /// A model falls for a trap only when it flags the bait itself. A remark about something else
    /// in the same diff does not count here: resistance asks whether the model mistook correct
    /// code for a bug, and only the bait answers that.
    /// <para>
    /// This used to carry a second argument — that counting any finding as failure would turn the
    /// trap into a test of silence, rewarding the model that says little. That held while
    /// resistance was the only thing measured on a trap. It is no longer the whole story:
    /// <see cref="Score"/> does count the other findings, against precision, because the diff is
    /// correct by construction. Silence is not rewarded by that, because the same model has to
    /// answer for detection on the twelve cases that do plant something. The two axes hold each
    /// other in place; one axis alone could not.
    /// </para>
    /// </summary>
    public static bool FallsFor(Finding finding, ExpectNoFinding trap) =>
        FileMatches(finding.File, trap.File) && CitesBait(finding.CodeSnippet, trap.Snippet);

    public static string Describe(GoldenExpectation expect, IReadOnlyList<Finding> findings)
    {
        var got = findings.Count == 0
            ? "no findings"
            : string.Join(", ", findings.Select(f => $"{f.Category} in {f.File}"));

        return expect switch
        {
            ExpectFinding detection => $"expected a finding in {detection.File} mentioning a keyword; got: {got}",
            ExpectNoFinding trap => $"expected nothing flagged on \"{trap.Snippet}\" in {trap.File}; got: {got}",
            _ => got,
        };
    }

    private static bool FileMatches(string? findingFile, string expected)
    {
        if (findingFile is null)
            return false;
        var path = findingFile.Replace('\\', '/').Trim();
        return path == expected
            || path.EndsWith('/' + expected)
            || expected.EndsWith('/' + path);
    }

    private static bool KeywordMatches(Finding finding, List<string> keywords)
    {
        var text = $"{finding.Problem} {finding.Suggestion}".ToLowerInvariant();
        return keywords.Any(k => text.Contains(k.ToLowerInvariant()));
    }

    // The model may quote the whole line the bait sits on, or only a fragment of it, so the
    // containment is checked both ways. Whitespace is collapsed first: indentation is cosmetic.
    private static bool CitesBait(string? cited, string bait)
    {
        var quote = Normalize(cited ?? "");
        var target = Normalize(bait);
        if (quote.Length == 0 || target.Length == 0)
            return false;
        return quote.Contains(target) || target.Contains(quote);
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
