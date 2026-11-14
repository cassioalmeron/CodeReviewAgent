using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Core.Llm;
using CodeReviewerAgent.Core.Routing;

namespace CodeReviewerAgent.Core.Advisor;

public sealed record AdvisorRunOptions(
    string CheapModel,
    string? StrongModel,
    string Store,
    string? Out,
    string Skills,
    string PromptVersion,
    bool DryRun);

/// <summary>What a paid run needs and a dry run does not: the two clients and how the executor is configured.</summary>
public sealed record AdvisorRunClients(
    ILlmClient Executor, ILlmClient Advisor, string? Engine, string? AdvisorEngine, int Parallelism);

/// <summary>
/// The advisor pattern over rounds already recorded: the recorded cheap review is the first review,
/// the alarm decides, and only a round that fires is escalated. Every round, escalated or not, lands
/// in the output store as it finishes, so a crash loses nothing and a rerun buys nothing twice.
/// </summary>
public static class AdvisorRunCommand
{
    public const string Usage =
        "advisor-run --cheap <model> --store <path> --out <path> [--skills <setting>] [--prompt <version>] [--strong <model>] [--dry-run] (one --out file holds one advisor model: the store key carries the executor, not the advisor)";

    public const string DefaultSkills = "off";

    /// <summary>Parses the arguments that follow the command name. On failure, <paramref name="error"/> says why.</summary>
    public static bool TryParse(
        IReadOnlyList<string> args, string defaultPromptVersion, out AdvisorRunOptions? options, out string? error)
    {
        options = null;
        var parsed = AdvisorArguments.Read(
            args, ["--cheap", "--strong", "--store", "--out", "--skills", "--prompt"], [], ["--dry-run"], out error);
        if (parsed is null)
            return false;

        var dryRun = parsed.Switches.Contains("--dry-run");
        if (parsed.One("--cheap") is not { } cheap || parsed.One("--store") is not { } store)
        {
            error = "--cheap and --store are required.";
            return false;
        }

        if (!dryRun && parsed.One("--out") is null)
        {
            error = "--out is required unless --dry-run is given.";
            return false;
        }

        options = new AdvisorRunOptions(
            cheap, parsed.One("--strong"), store, parsed.One("--out"), parsed.One("--skills") ?? DefaultSkills,
            parsed.One("--prompt") ?? defaultPromptVersion, dryRun);
        return true;
    }

    /// <summary>
    /// Returns the process exit code: 0 when the run (or the dry run) finished, 1 otherwise.
    /// <paramref name="clients"/> is read only when this is not a dry run, so a dry run needs no key.
    /// </summary>
    public static int Run(AdvisorRunOptions options, AdvisorRunClients? clients, TextWriter output, TextWriter error)
    {
        if (!File.Exists(options.Store))
        {
            error.WriteLine($"Store file not found: {options.Store}");
            return 1;
        }

        var records = FileGoldenRoundStore.Load(options.Store);
        var rounds = LatestRounds(RoutingReportCommand.Select(records, options.CheapModel, options.Skills, options.PromptVersion)
            .Where(r => IsPattern(r, ReviewSettings.SinglePattern)));
        if (rounds.Count == 0)
        {
            error.WriteLine($"No stored rounds for model '{options.CheapModel}' (skills={options.Skills}, prompt={options.PromptVersion}, pattern=single) in {options.Store}");
            return 1;
        }

        var withoutDiff = rounds.Where(r => r.Review.Diff is null).Select(r => $"{r.Case}#{r.RunIndex}").ToList();
        if (withoutDiff.Count > 0)
        {
            error.WriteLine($"Recorded round(s) without a diff cannot be escalated: {string.Join(", ", withoutDiff)}");
            return 1;
        }

        var escalating = rounds.Where(r => AdvisorTrigger.ShouldEscalate(r.Review.Findings ?? [])).ToList();

        if (options.DryRun)
            return DryRun(options, records, rounds, escalating, output, error);

        if (clients is null || options.Out is null)
        {
            error.WriteLine("A paid run needs the clients and --out.");
            return 1;
        }

        return Execute(options, clients, rounds, output, error);
    }

    private static int DryRun(
        AdvisorRunOptions options, List<GoldenRoundRecord> records, List<GoldenRoundRecord> rounds,
        List<GoldenRoundRecord> escalating, TextWriter output, TextWriter error)
    {
        output.WriteLine($"Dry run: no call is made. Alarm {AdvisorTrigger.Version}, recorded rounds of '{options.CheapModel}' (skills={options.Skills}, prompt={options.PromptVersion}).");
        output.WriteLine($"Would escalate {escalating.Count} of {rounds.Count} rounds.");

        if (options.StrongModel is null)
            return 0;

        var strong = LatestRounds(RoutingReportCommand.Select(records, options.StrongModel, options.Skills, options.PromptVersion)
                .Where(r => IsPattern(r, ReviewSettings.SinglePattern)))
            .ToDictionary(r => (r.Case, r.RunIndex));
        var absent = escalating.Where(r => !strong.ContainsKey((r.Case, r.RunIndex))).Select(r => $"{r.Case}#{r.RunIndex}").ToList();
        if (absent.Count > 0)
        {
            error.WriteLine($"No recorded round for '{options.StrongModel}' on {absent.Count} escalating round(s): {string.Join(", ", absent)}");
            return 1;
        }

        var strongCost = escalating.Sum(r => strong[(r.Case, r.RunIndex)].Review.Cost);
        var cheapCost = escalating.Sum(r => r.Review.Cost);
        output.WriteLine($"Recorded strong rounds of those cases ('{options.StrongModel}'): {AdvisorArguments.Money(strongCost)}");
        output.WriteLine($"Recorded cheap rounds of those cases ('{options.CheapModel}'): {AdvisorArguments.Money(cheapCost)}");
        output.WriteLine($"Upper estimate: {AdvisorArguments.Money(strongCost + cheapCost)}");
        return 0;
    }

    private static int Execute(
        AdvisorRunOptions options, AdvisorRunClients clients, List<GoldenRoundRecord> rounds,
        TextWriter output, TextWriter error)
    {
        var temperatures = rounds.Select(r => r.Temperature).Distinct().ToList();
        if (temperatures.Count > 1)
        {
            error.WriteLine("The recorded rounds were sampled at more than one temperature; the output store keys on one.");
            return 1;
        }

        var store = new FileGoldenRoundStore(
            options.Out!, options.CheapModel, options.Skills, temperatures[0], ReviewSettings.AdvisorPattern);
        var settings = new ReviewSettings(
            options.PromptVersion, clients.Engine, Skills: options.Skills,
            Pattern: ReviewSettings.AdvisorPattern, AdvisorEngine: clients.AdvisorEngine);

        var resumed = 0;
        var copied = 0;
        var escalated = 0;
        var advisorFailed = 0;
        var advisorCost = 0m;
        var gate = new Lock();

        try
        {
            Parallel.For(0, rounds.Count, new ParallelOptions { MaxDegreeOfParallelism = clients.Parallelism }, index =>
            {
                var round = rounds[index];
                if (store.Find(round.Case, round.PromptVersion, round.RunIndex) is not null)
                {
                    Interlocked.Increment(ref resumed);
                    return;
                }

                var recorded = round.Review;
                var fires = AdvisorTrigger.ShouldEscalate(recorded.Findings ?? []);
                var final = fires
                    ? new CodeReviewer(clients.Executor, recorded.Diff!, settings, advisor: clients.Advisor).Escalate(recorded)
                    : recorded with { Advisor = new AdvisorTrace(false, null, 0m, 0, 0, 0) };

                // Durable before the next paid call goes out, not at the end of the run.
                store.Record(round.Case, round.PromptVersion, round.RunIndex, final);

                lock (gate)
                    if (fires)
                    {
                        escalated++;
                        if (final.Advisor!.Failed)
                            advisorFailed++;
                        advisorCost += final.Advisor!.Cost;
                    }
                    else
                        copied++;
            });
        }
        catch (Exception ex)
        {
            var cause = ex is AggregateException aggregate ? aggregate.InnerExceptions[0] : ex;
            error.WriteLine($"Advisor run stopped: {cause.Message}");
            error.WriteLine($"The rounds that finished are in {options.Out}. Run again to resume from them.");
            return 1;
        }

        output.WriteLine($"Advisor run finished: {escalated} escalated ({advisorFailed} advisor failed), {copied} copied unchanged, {resumed} already in {options.Out}.");
        output.WriteLine($"Advisor calls in this run: {AdvisorArguments.Money(advisorCost)}");
        return 0;
    }

    private static bool IsPattern(GoldenRoundRecord record, string pattern) =>
        string.Equals(record.Pattern, pattern, StringComparison.OrdinalIgnoreCase);

    // The same run index recorded twice is one round re-recorded; the later line wins, as in the store.
    private static List<GoldenRoundRecord> LatestRounds(IEnumerable<GoldenRoundRecord> records) =>
        [.. records
            .GroupBy(r => (r.Case, r.RunIndex))
            .Select(g => g.Last())
            .OrderBy(r => r.Case, StringComparer.Ordinal)
            .ThenBy(r => r.RunIndex)];
}
