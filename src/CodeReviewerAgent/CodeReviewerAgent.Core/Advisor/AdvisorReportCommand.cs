using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Core.Routing;

namespace CodeReviewerAgent.Core.Advisor;

public sealed record AdvisorReportOptions(
    string CheapModel,
    string StrongModel,
    IReadOnlyList<string> Stores,
    string Skills,
    string PromptVersion,
    string? OutputFile);

/// <summary>
/// The advisor comparison over rounds already on disk: no model call, so no credential. Whatever it
/// can detect that would make the numbers wrong is explained and returned as an exit code before the
/// composer runs.
/// </summary>
public static class AdvisorReportCommand
{
    public const string Usage =
        "advisor-report --cheap <model> --strong <model> --store <path> [--store <path>]... [--skills <setting>] [--prompt <version>] [--out <file>]";

    public static bool TryParse(
        IReadOnlyList<string> args, string defaultPromptVersion, out AdvisorReportOptions? options, out string? error)
    {
        options = null;
        var parsed = AdvisorArguments.Read(
            args, ["--cheap", "--strong", "--store", "--skills", "--prompt", "--out"], ["--store"], [], out error);
        if (parsed is null)
            return false;

        if (parsed.One("--cheap") is not { } cheap || parsed.One("--strong") is not { } strong || parsed.Many("--store").Count == 0)
        {
            error = "--cheap, --strong and at least one --store are required.";
            return false;
        }

        options = new AdvisorReportOptions(
            cheap, strong, parsed.Many("--store"), parsed.One("--skills") ?? AdvisorRunCommand.DefaultSkills,
            parsed.One("--prompt") ?? defaultPromptVersion, parsed.One("--out"));
        return true;
    }

    /// <summary>Returns the process exit code: 0 when the report was written, 1 otherwise.</summary>
    public static int Run(AdvisorReportOptions options, TextWriter output, TextWriter error)
    {
        var absent = options.Stores.Where(path => !File.Exists(path)).ToList();
        foreach (var path in absent)
            error.WriteLine($"Store file not found: {path}");
        if (absent.Count > 0)
            return 1;

        var cases = RoutingReportCommand.LoadCases(error);
        if (cases is null)
            return 1;

        var byStore = options.Stores.Select(path => (Path: path, Records: FileGoldenRoundStore.Load(path))).ToList();
        var cheap = Pass(byStore, options.CheapModel, ReviewSettings.SinglePattern, options);
        var strong = Pass(byStore, options.StrongModel, ReviewSettings.SinglePattern, options);
        var advisor = Pass(byStore, options.CheapModel, ReviewSettings.AdvisorPattern, options);

        var problems = new List<string>();
        var rounds = new List<AdvisorCaseRounds>();
        foreach (var (golden, _) in cases)
        {
            var cheapRounds = RoundsOf(cheap, golden.Name);
            var strongRounds = RoundsOf(strong, golden.Name);
            var advisorRounds = RoundsOf(advisor, golden.Name);

            Report(problems, $"cheap pass of '{options.CheapModel}'", golden.Name, cheapRounds.Select(r => r.Key).ToList(), null);
            Report(problems, $"strong pass of '{options.StrongModel}'", golden.Name, strongRounds.Select(r => r.Key).ToList(), cheapRounds.Select(r => r.Key).ToList());
            Report(problems, $"advisor pass of '{options.CheapModel}'", golden.Name, advisorRounds.Select(r => r.Key).ToList(), cheapRounds.Select(r => r.Key).ToList());

            rounds.Add(new AdvisorCaseRounds(
                golden,
                [.. cheapRounds.Select(r => r.Value)],
                [.. strongRounds.Select(r => r.Value)],
                [.. advisorRounds.Select(r => r.Value)]));
        }

        if (problems.Count > 0)
        {
            foreach (var problem in problems)
                error.WriteLine(problem);
            return 1;
        }

        AdvisorComparison comparison;
        try
        {
            comparison = AdvisorComparison.Compose(rounds, options.PromptVersion);
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }

        AdvisorPassInfo[] passes =
        [
            Info("Cheap", options.CheapModel, ReviewSettings.SinglePattern, cheap),
            Info("Strong", options.StrongModel, ReviewSettings.SinglePattern, strong),
            Info("Advisor", options.CheapModel, ReviewSettings.AdvisorPattern, advisor),
        ];
        var text = AdvisorComparisonReport.Build(comparison, passes, options.Skills, options.PromptVersion);
        output.WriteLine(text);
        output.WriteLine($"Advisor report saved to {Write(text, options.OutputFile)}");
        return 0;
    }

    // What one pass found, with the store file each record came from.
    private static List<(string Store, GoldenRoundRecord Record)> Pass(
        List<(string Path, List<GoldenRoundRecord> Records)> byStore, string model, string pattern,
        AdvisorReportOptions options) =>
        [.. byStore.SelectMany(store =>
            RoutingReportCommand.Select(store.Records, model, options.Skills, options.PromptVersion)
                .Where(r => string.Equals(r.Pattern, pattern, StringComparison.OrdinalIgnoreCase))
                .Select(r => (store.Path, r)))];

    // The same run index recorded twice is one round re-recorded; the later line wins, as in the store.
    private static List<KeyValuePair<int, ReviewResult>> RoundsOf(
        List<(string Store, GoldenRoundRecord Record)> pass, string caseName) =>
        [.. pass
            .Where(p => p.Record.Case == caseName)
            .GroupBy(p => p.Record.RunIndex)
            .OrderBy(g => g.Key)
            .Select(g => KeyValuePair.Create(g.Key, g.Last().Record.Review))];

    // A pass with no round on a case, or without a round the reference pass has, names what is absent.
    private static void Report(List<string> problems, string pass, string caseName, List<int> present, List<int>? expected)
    {
        if (present.Count == 0)
        {
            problems.Add($"No stored rounds in the {pass} for case '{caseName}'.");
            return;
        }

        var absent = expected?.Except(present).ToList() ?? [];
        if (absent.Count > 0)
            problems.Add($"The {pass} lacks round(s) {string.Join(", ", absent)} of case '{caseName}'.");
    }

    private static AdvisorPassInfo Info(
        string label, string model, string pattern, List<(string Store, GoldenRoundRecord Record)> pass) =>
        new(label, model, pattern, [.. pass.Select(p => p.Store).Distinct()]);

    private static string Write(string text, string? outputFile)
    {
        var path = outputFile ?? Path.Combine(OutputPaths.Reports, $"advisor-comparison-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.md");
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, text);
        return path;
    }
}
