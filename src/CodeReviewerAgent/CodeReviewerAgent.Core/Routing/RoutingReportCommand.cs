using CodeReviewerAgent.Core.Golden;

namespace CodeReviewerAgent.Core.Routing;

public sealed record RoutingReportOptions(
    string CheapModel,
    string StrongModel,
    IReadOnlyList<string> Stores,
    string Skills,
    string PromptVersion,
    string? OutputFile);

/// <summary>
/// The routing comparison over rounds already on disk. It reads round store files and never calls a
/// model, so it needs no credential. Every condition it can detect is explained and returned as an
/// exit code before the composer runs, because this is what stands between a paid run and a wrong
/// number.
/// </summary>
public static class RoutingReportCommand
{
    public const string Usage =
        "routing-report --cheap <model> --strong <model> --store <path> [--store <path>]... [--skills <setting>] [--prompt <version>] [--out <file>]";

    public const string DefaultSkills = "off";

    // The two sets the measurement spans: the golden cases all take the cheap route and the
    // routing cases all take the strong one.
    private static readonly string[] Sets = [GoldenEvaluator.GoldenSet, "routing"];

    /// <summary>Parses the arguments that follow the command name. On failure, <paramref name="error"/> says why.</summary>
    public static bool TryParse(
        IReadOnlyList<string> args, string defaultPromptVersion, out RoutingReportOptions? options, out string? error)
    {
        options = null;
        error = null;

        string? cheap = null, strong = null, skills = null, prompt = null, output = null;
        var stores = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            if (name is not ("--cheap" or "--strong" or "--store" or "--skills" or "--prompt" or "--out"))
            {
                error = $"Unknown argument '{name}'.";
                return false;
            }

            if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--"))
            {
                error = $"{name} needs a value.";
                return false;
            }

            var value = args[++i].Trim();
            switch (name)
            {
                case "--store":
                    stores.Add(value);
                    continue;
                case "--cheap" when cheap is null: cheap = value; continue;
                case "--strong" when strong is null: strong = value; continue;
                case "--skills" when skills is null: skills = value; continue;
                case "--prompt" when prompt is null: prompt = value; continue;
                case "--out" when output is null: output = value; continue;
                default:
                    error = $"{name} was given more than once.";
                    return false;
            }
        }

        if (cheap is null || strong is null || stores.Count == 0)
        {
            error = "--cheap, --strong and at least one --store are required.";
            return false;
        }

        options = new RoutingReportOptions(
            cheap, strong, stores, skills ?? DefaultSkills, prompt ?? defaultPromptVersion, output);
        return true;
    }

    /// <summary>The records that were produced by this model, under this skills setting and this prompt version.</summary>
    internal static List<GoldenRoundRecord> Select(
        IEnumerable<GoldenRoundRecord> records, string model, string skills, string promptVersion) =>
        [.. records.Where(r =>
            r.Model == model
            && string.Equals(r.Skills ?? "", skills, StringComparison.OrdinalIgnoreCase)
            && r.PromptVersion == promptVersion)];

    /// <summary>Returns the process exit code: 0 when the report was written, 1 otherwise.</summary>
    public static int Run(RoutingReportOptions options, TextWriter output, TextWriter error)
    {
        var absent = options.Stores.Where(path => !File.Exists(path)).ToList();
        foreach (var path in absent)
            error.WriteLine($"Store file not found: {path}");
        if (absent.Count > 0)
            return 1;

        var cases = LoadCases(error);
        if (cases is null)
            return 1;

        var records = options.Stores.SelectMany(FileGoldenRoundStore.Load).ToList();
        var cheap = Gather(options.CheapModel, records, cases, options);
        var strong = Gather(options.StrongModel, records, cases, options);

        var problems = false;
        foreach (var (model, gathered) in new[] { (options.CheapModel, cheap), (options.StrongModel, strong) })
            if (gathered.Missing.Count > 0)
            {
                error.WriteLine($"No stored rounds for model '{model}' (skills={options.Skills}, prompt={options.PromptVersion}) on {gathered.Missing.Count} case(s): {string.Join(", ", gathered.Missing)}");
                problems = true;
            }

        if (problems)
            return 1;

        var differing = cheap.Pass.Results
            .Join(strong.Pass.Results, r => r.Name, r => r.Name, (c, s) => (c.Name, Cheap: c.Runs, Strong: s.Runs))
            .Where(x => x.Cheap != x.Strong)
            .ToList();
        foreach (var (name, cheapRuns, strongRuns) in differing)
            error.WriteLine($"Case '{name}' has {cheapRuns} round(s) for '{options.CheapModel}' and {strongRuns} for '{options.StrongModel}'; the passes must use the same number of rounds.");
        if (differing.Count > 0)
            return 1;

        var comparison = RoutingComparison.Compose(cheap.Pass, strong.Pass);
        var reportPath = WriteReport(comparison, options.OutputFile);
        output.WriteLine($"Routing report saved to {reportPath}");
        return 0;
    }

    private static string WriteReport(RoutingComparison comparison, string? outputFile)
    {
        if (outputFile is null)
            return RoutingComparisonReport.Save(comparison);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputFile));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(outputFile, RoutingComparisonReport.Build(comparison));
        return outputFile;
    }

    private static List<(GoldenCase Case, string Diff)>? LoadCases(TextWriter error)
    {
        var cases = new List<(GoldenCase, string)>();
        foreach (var set in Sets)
            foreach (var golden in GoldenEvaluator.LoadCases(set))
                cases.Add((golden, GoldenEvaluator.LoadDiff(Path.Combine(GoldenEvaluator.CasesDirectoryFor(set), golden.Diff))));

        var duplicates = cases.GroupBy(c => c.Item1.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count == 0)
            return cases;

        error.WriteLine($"Case names must be unique across the sets; repeated: {string.Join(", ", duplicates)}");
        return null;
    }

    private static (RoutingPass Pass, List<string> Missing) Gather(
        string model, List<GoldenRoundRecord> records, List<(GoldenCase Case, string Diff)> cases,
        RoutingReportOptions options)
    {
        var selected = Select(records, model, options.Skills, options.PromptVersion);
        var results = new List<GoldenCaseResult>();
        var rounds = new List<CompletedRound>();
        var missing = new List<string>();

        foreach (var (golden, diff) in cases)
        {
            // The same run index recorded twice is one round re-recorded; the later line wins, as in the store.
            var reviews = selected
                .Where(r => r.Case == golden.Name)
                .GroupBy(r => r.RunIndex)
                .OrderBy(g => g.Key)
                .Select(g => g.Last().Review)
                .ToList();
            if (reviews.Count == 0)
            {
                missing.Add(golden.Name);
                continue;
            }

            results.Add(GoldenEvaluator.ScoreOneSide(golden, options.PromptVersion, reviews).Result);
            rounds.AddRange(reviews.Select(review => new CompletedRound(golden.Name, diff, review)));
        }

        return (new RoutingPass(model, results, rounds), missing);
    }
}
