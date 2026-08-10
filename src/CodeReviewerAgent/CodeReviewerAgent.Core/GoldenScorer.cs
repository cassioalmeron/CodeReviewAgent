namespace CodeReviewerAgent.Core
{
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

        /// <summary>A planted problem counts as caught on file + keyword.</summary>
        public static bool Matches(Finding finding, ExpectFinding expect) =>
            FileMatches(finding.File, expect.File) && KeywordMatches(finding, expect.Keywords);

        /// <summary>
        /// A model falls for a trap only when it flags the bait itself. A legitimate remark about
        /// something else in the same diff is ignored — counting any finding as failure would turn
        /// the trap into a test of silence, rewarding the model that says little over the one that
        /// discriminates.
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
}
