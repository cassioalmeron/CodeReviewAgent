using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Core.Routing;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The routing-report command reads round store files and never calls a model. Every store here is
/// a temporary file written with handmade rounds, covering every case of the golden and routing sets.
/// </summary>
public class RoutingReportCommandTests : IDisposable
{
    private const string Prompt = "v3";
    private const int RoundsPerCase = 2;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"cra-routing-report-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static ReviewResult Review(decimal cost) =>
        new("summary", [], "fake", "fake-model", Prompt, cost, 0, 0, 0, "diff");

    private static IEnumerable<string> AllCaseNames() =>
        new[] { GoldenEvaluator.GoldenSet, "routing" }.SelectMany(set => GoldenEvaluator.LoadCases(set)).Select(c => c.Name);

    private string WriteStore(
        string fileName, string model, decimal costPerRound, string skills = "off", string prompt = Prompt,
        IEnumerable<string>? cases = null, IEnumerable<string>? except = null)
    {
        var path = Path.Combine(_directory, fileName);
        var store = new FileGoldenRoundStore(path, model, skills);
        foreach (var name in (cases ?? AllCaseNames()).Except(except ?? []))
            for (var run = 0; run < RoundsPerCase; run++)
                store.Record(name, prompt, run, Review(costPerRound));
        return path;
    }

    private (int ExitCode, string Output, string Error) Run(RoutingReportOptions options)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = RoutingReportCommand.Run(options, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private RoutingReportOptions Options(IEnumerable<string> stores, string? outputFile = null) =>
        new("cheap-model", "strong-model", [.. stores], "off", Prompt, outputFile ?? Path.Combine(_directory, "report.md"));

    [Fact]
    public void RoutingReportCommandTests_Arguments_parse_with_the_defaults_for_skills_and_prompt()
    {
        var ok = RoutingReportCommand.TryParse(
            ["--cheap", "a", "--strong", "b", "--store", "one.jsonl", "--store", "two.jsonl"],
            "v9", out var options, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(("a", "b", "off", "v9", null), (options!.CheapModel, options.StrongModel, options.Skills, options.PromptVersion, options.OutputFile));
        Assert.Equal(["one.jsonl", "two.jsonl"], options.Stores);
    }

    [Fact]
    public void RoutingReportCommandTests_Skills_prompt_and_out_override_their_defaults()
    {
        RoutingReportCommand.TryParse(
            ["--cheap", "a", "--strong", "b", "--store", "s", "--skills", "all", "--prompt", "v5", "--out", "r.md"],
            "v9", out var options, out _);

        Assert.Equal(("all", "v5", "r.md"), (options!.Skills, options.PromptVersion, options.OutputFile));
    }

    [Theory]
    [InlineData("--strong", "b", "--store", "s")]
    [InlineData("--cheap", "a", "--store", "s")]
    [InlineData("--cheap", "a", "--strong", "b")]
    [InlineData("--cheap", "a", "--strong", "b", "--store")]
    [InlineData("--cheap", "a", "--strong", "--store", "s")]
    [InlineData("--cheap", "a", "--strong", "b", "--store", "s", "--bogus", "x")]
    [InlineData("--cheap", "a", "--cheap", "c", "--strong", "b", "--store", "s")]
    public void RoutingReportCommandTests_A_missing_or_malformed_argument_is_rejected_with_a_reason(params string[] args)
    {
        var ok = RoutingReportCommand.TryParse(args, "v9", out var options, out var error);

        Assert.False(ok);
        Assert.Null(options);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void RoutingReportCommandTests_Records_are_kept_only_for_the_model_skills_and_prompt_version_asked_for()
    {
        GoldenRoundRecord Record(string model, string? skills, string prompt) =>
            new("case", prompt, 0, model, skills, Review(0m));
        GoldenRoundRecord[] records =
        [
            Record("m", "off", "v3"),
            Record("other", "off", "v3"),
            Record("m", "all", "v3"),
            Record("m", "off", "v5"),
            Record("m", "OFF", "v3"),
        ];

        var kept = RoutingReportCommand.Select(records, "m", "off", "v3");

        Assert.Equal([records[0], records[4]], kept);
    }

    [Fact]
    public void RoutingReportCommandTests_A_run_over_a_temporary_store_writes_a_report_whose_routed_cost_is_the_per_case_sum_of_the_selected_pass()
    {
        const decimal cheapRound = 0.01m;
        const decimal strongRound = 0.5m;
        var goldenNames = GoldenEvaluator.LoadCases(GoldenEvaluator.GoldenSet).Select(c => c.Name).ToList();
        var routingNames = GoldenEvaluator.LoadCases("routing").Select(c => c.Name).ToList();
        // The two models live in different files, and so do the two sets, as in the real measurement.
        var stores = new[]
        {
            WriteStore("cheap-golden.jsonl", "cheap-model", cheapRound, cases: goldenNames),
            WriteStore("cheap-routing.jsonl", "cheap-model", cheapRound, cases: routingNames),
            WriteStore("strong.jsonl", "strong-model", strongRound),
        };
        var reportPath = Path.Combine(_directory, "nested", "report.md");

        var result = Run(Options(stores, reportPath));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(reportPath, result.Output);
        var report = File.ReadAllText(reportPath);
        // golden cases route cheap and routing cases route strong
        var cheapEverywhere = (goldenNames.Count + routingNames.Count) * RoundsPerCase * cheapRound;
        var strongEverywhere = (goldenNames.Count + routingNames.Count) * RoundsPerCase * strongRound;
        var routed = goldenNames.Count * RoundsPerCase * cheapRound + routingNames.Count * RoundsPerCase * strongRound;
        Assert.Contains(
            FormattableString.Invariant(
                $"| Cost | US$ {cheapEverywhere:0.0000} | US$ {routed:0.0000} | US$ {strongEverywhere:0.0000} |"),
            report);
        Assert.Contains($"({goldenNames.Count} cheap route, {routingNames.Count} strong route)", report);
    }

    [Fact]
    public void RoutingReportCommandTests_A_case_missing_for_one_model_is_named_with_that_model_and_no_report_is_written()
    {
        var missingCase = GoldenEvaluator.LoadCases("routing").First().Name;
        var stores = new[]
        {
            WriteStore("cheap.jsonl", "cheap-model", 0.01m),
            WriteStore("strong.jsonl", "strong-model", 0.5m, except: [missingCase]),
        };
        var reportPath = Path.Combine(_directory, "report.md");

        var result = Run(Options(stores, reportPath));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("strong-model", result.Error);
        Assert.Contains(missingCase, result.Error);
        Assert.DoesNotContain("cheap-model", result.Error);
        Assert.False(File.Exists(reportPath));
    }

    [Fact]
    public void RoutingReportCommandTests_Rounds_recorded_under_another_prompt_version_do_not_count()
    {
        var stores = new[]
        {
            WriteStore("cheap.jsonl", "cheap-model", 0.01m, prompt: "v5"),
            WriteStore("strong.jsonl", "strong-model", 0.5m),
        };

        var result = Run(Options(stores));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cheap-model", result.Error);
        Assert.Contains($"prompt={Prompt}", result.Error);
    }

    [Fact]
    public void RoutingReportCommandTests_A_store_path_that_does_not_exist_is_named_and_fails()
    {
        var present = WriteStore("strong.jsonl", "strong-model", 0.5m);
        var absent = Path.Combine(_directory, "nowhere.jsonl");

        var result = Run(Options([present, absent]));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"Store file not found: {absent}", result.Error);
    }

    [Fact]
    public void RoutingReportCommandTests_Passes_with_a_different_number_of_rounds_fail_with_a_message_instead_of_an_exception()
    {
        var oneCase = GoldenEvaluator.LoadCases(GoldenEvaluator.GoldenSet).First().Name;
        var cheap = WriteStore("cheap.jsonl", "cheap-model", 0.01m);
        var strong = WriteStore("strong.jsonl", "strong-model", 0.5m);
        new FileGoldenRoundStore(strong, "strong-model", "off").Record(oneCase, Prompt, RoundsPerCase, Review(0.5m));

        var result = Run(Options([cheap, strong]));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(oneCase, result.Error);
        Assert.Contains("same number of rounds", result.Error);
    }
}
