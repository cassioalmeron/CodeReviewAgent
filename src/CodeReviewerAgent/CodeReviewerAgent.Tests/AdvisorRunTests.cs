using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Advisor;
using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Core.Llm;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class AdvisorRunTests : IDisposable
{
    private const string Diff = "diff --git a/App.cs b/App.cs\n--- a/App.cs\n+++ b/App.cs\n@@ -1,2 +1,4 @@\n existing line\n+var result = service.Process();\n+var total = items.Sum();\n trailing line";

    private const string SecondReview = """
        {"summary":"second","findings":[{"file":"App.cs","code_snippet":"var total = items.Sum();","severity":"warning","category":"bug","problem":"items may be null.","suggestion":"Guard it."}]}
        """;

    private const string Hints = """{"hints":[{"file":"App.cs","snippet":"var total = items.Sum();","check":"Can items be null?"}]}""";

    private readonly string _directory = Directory.CreateTempSubdirectory("advisor-run-").FullName;

    private string Recorded => Path.Combine(_directory, "recorded.jsonl");

    private string Out => Path.Combine(_directory, "advisor.jsonl");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void AdvisorRunTests_A_quiet_round_is_copied_with_its_cost_and_no_call_is_made()
    {
        Record(Recorded, "cheap", "bug-case", 0, Review(Category.Bug, cost: 0.01m));
        var (clients, cheap, advisor) = Clients();

        var code = Run(Options(), clients, out _, out _);

        Assert.Equal(0, code);
        Assert.Equal(0, cheap.Calls + advisor.Calls);
        var copied = Assert.Single(Load(Out));
        Assert.Equal(ReviewSettings.AdvisorPattern, copied.Pattern);
        Assert.Equal(0.01m, copied.Review.Cost);
        Assert.False(copied.Review.Advisor!.Escalated);
        Assert.Equal("bug-case", copied.Case);
    }

    [Fact]
    public void AdvisorRunTests_A_firing_round_goes_through_the_escalation_and_is_recorded_under_the_advisor_pattern()
    {
        Record(Recorded, "cheap", "convention-case", 2, Review(Category.Convention, cost: 0.01m));
        var (clients, cheap, advisor) = Clients(advisorCost: 0.5m);

        var code = Run(Options(), clients, out var output, out _);

        Assert.Equal(0, code);
        Assert.Equal(1, cheap.Calls);
        Assert.Equal(1, advisor.Calls);
        var final = Assert.Single(Load(Out));
        Assert.Equal(2, final.RunIndex);
        Assert.True(final.Review.Advisor!.Escalated);
        Assert.Equal(1, final.Review.Advisor.Hints);
        Assert.Equal(0.5m, final.Review.Advisor.Cost);
        Assert.Equal("items may be null.", Assert.Single(final.Review.Findings!).Problem);
        Assert.Equal(0.51m + 0.02m, final.Review.Cost);
        Assert.Contains("1 escalated (0 advisor failed), 0 copied unchanged, 0 already", output);
    }

    [Fact]
    public void AdvisorRunTests_A_failed_advisor_call_is_recorded_counted_in_the_finish_line_and_its_cost_is_kept()
    {
        Record(Recorded, "cheap", "convention-case", 0, Review(Category.Convention, cost: 0.01m));
        Record(Recorded, "cheap", "other-case", 0, Review(Category.Convention, cost: 0.01m));
        var cheap = new Counting(SecondReview, 0.02m);
        var advisor = new Counting("""{"hints":[{"file":"App""", 0.5m);
        var clients = new AdvisorRunClients(cheap, advisor, "openai", "claude", 1);

        var code = Run(Options(), clients, out var output, out _);

        Assert.Equal(0, code);
        Assert.Equal(0, cheap.Calls);
        Assert.All(Load(Out), r => Assert.True(r.Review.Advisor!.Failed));
        Assert.Equal("first", Load(Out)[0].Review.Summary);
        Assert.Contains("2 escalated (2 advisor failed), 0 copied unchanged", output);
        Assert.Contains("Advisor calls in this run: US$ 1.0000", output);
    }

    [Fact]
    public void AdvisorRunTests_A_trace_recorded_without_the_failed_field_reads_as_not_failed()
    {
        Record(Out, "cheap", "bug-case", 0, Review(Category.Bug) with { Advisor = new AdvisorTrace(true, "m", 0.5m, 1, 1, 1) });
        var line = File.ReadAllText(Out);
        Assert.Contains("\"failed\":false", line);
        File.WriteAllText(Out, line.Replace(",\"failed\":false", ""));

        var loaded = Load(Out);

        Assert.DoesNotContain("failed", File.ReadAllText(Out));
        Assert.False(Assert.Single(loaded).Review.Advisor!.Failed);
    }

    [Fact]
    public void AdvisorRunTests_Parallel_rounds_are_all_recorded_and_the_totals_add_up()
    {
        // 40 firing rounds and 40 quiet ones, run 8 at a time.
        for (var i = 0; i < 40; i++)
        {
            Record(Recorded, "cheap", $"fire-{i}", 0, Review(Category.Convention, cost: 0.01m));
            Record(Recorded, "cheap", $"quiet-{i}", 0, Review(Category.Bug, cost: 0.01m));
        }

        var cheap = new Counting(SecondReview, 0.02m);
        var advisor = new Counting(Hints, 0.5m);
        var clients = new AdvisorRunClients(cheap, advisor, "openai", "claude", 8);

        var code = Run(Options(), clients, out var output, out _);

        Assert.Equal(0, code);
        Assert.Equal((40, 40), (cheap.Calls, advisor.Calls));
        var recorded = Load(Out);
        Assert.Equal(80, recorded.Count);
        Assert.Equal(80, recorded.Select(r => r.Case).Distinct().Count());
        Assert.Equal(40, recorded.Count(r => r.Review.Advisor!.Escalated));
        Assert.Contains("40 escalated (0 advisor failed), 40 copied unchanged, 0 already", output);
        Assert.Contains("Advisor calls in this run: US$ 20.0000", output);
    }

    [Fact]
    public void AdvisorRunTests_A_rerun_skips_the_rounds_already_in_the_output_store()
    {
        Record(Recorded, "cheap", "bug-case", 0, Review(Category.Bug));
        Record(Recorded, "cheap", "convention-case", 0, Review(Category.Convention));
        var (first, _, _) = Clients();
        Run(Options(), first, out _, out _);
        var (second, cheap, advisor) = Clients();

        var code = Run(Options(), second, out var output, out _);

        Assert.Equal(0, code);
        Assert.Equal(0, cheap.Calls + advisor.Calls);
        Assert.Equal(2, Load(Out).Count);
        Assert.Contains("2 already in", output);
    }

    [Fact]
    public void AdvisorRunTests_The_dry_run_counts_the_rounds_that_fire_and_sums_the_recorded_costs_of_those_rounds()
    {
        Record(Recorded, "cheap", "a", 0, Review(Category.Convention, cost: 0.01m));
        Record(Recorded, "cheap", "a", 1, Review(Category.Bug, cost: 0.02m));
        Record(Recorded, "cheap", "b", 0, Review(Category.Performance, cost: 0.03m));
        Record(Recorded, "strong", "a", 0, Review(Category.Bug, cost: 0.10m));
        Record(Recorded, "strong", "a", 1, Review(Category.Bug, cost: 0.40m));
        Record(Recorded, "strong", "b", 0, Review(Category.Bug, cost: 0.20m));

        var code = Run(Options() with { DryRun = true, StrongModel = "strong", Out = null }, null, out var output, out _);

        Assert.Equal(0, code);
        Assert.Contains("Would escalate 2 of 3 rounds.", output);
        Assert.Contains("US$ 0.3000", output);
        Assert.Contains("US$ 0.0400", output);
        Assert.Contains("Upper estimate: US$ 0.3400", output);
        Assert.False(File.Exists(Out));
    }

    [Fact]
    public void AdvisorRunTests_The_dry_run_names_an_escalating_round_the_strong_model_has_no_record_for()
    {
        Record(Recorded, "cheap", "a", 0, Review(Category.Convention));
        Record(Recorded, "strong", "other", 0, Review(Category.Bug));

        var code = Run(Options() with { DryRun = true, StrongModel = "strong", Out = null }, null, out _, out var error);

        Assert.Equal(1, code);
        Assert.Contains("a#0", error);
    }

    [Fact]
    public void AdvisorRunTests_Arguments_need_out_unless_the_run_is_a_dry_run()
    {
        var paid = AdvisorRunCommand.TryParse(["--cheap", "m", "--store", "s"], "v3", out _, out var error);
        var dry = AdvisorRunCommand.TryParse(["--cheap", "m", "--store", "s", "--dry-run"], "v3", out var options, out _);

        Assert.False(paid);
        Assert.Contains("--out", error);
        Assert.True(dry);
        Assert.Equal(("off", "v3"), (options!.Skills, options.PromptVersion));
    }

    private AdvisorRunOptions Options() => new("cheap", null, Recorded, Out, "off", "v3", false);

    private static int Run(AdvisorRunOptions options, AdvisorRunClients? clients, out string output, out string error)
    {
        var writer = new StringWriter();
        var errors = new StringWriter();
        var code = AdvisorRunCommand.Run(options, clients, writer, errors);
        output = writer.ToString();
        error = errors.ToString();
        return code;
    }

    private static (AdvisorRunClients Clients, Counting Cheap, Counting Advisor) Clients(decimal advisorCost = 0.5m)
    {
        var cheap = new Counting(SecondReview, 0.02m);
        var advisor = new Counting(Hints, advisorCost);
        return (new AdvisorRunClients(cheap, advisor, "openai", "claude", 1), cheap, advisor);
    }

    private static void Record(string path, string model, string caseName, int runIndex, ReviewResult review) =>
        new FileGoldenRoundStore(path, model, "off", null, ReviewSettings.SinglePattern)
            .Record(caseName, "v3", runIndex, review);

    private static List<GoldenRoundRecord> Load(string path) => FileGoldenRoundStore.Load(path);

    private static ReviewResult Review(Category category, decimal cost = 0m) => new(
        "first",
        [new Finding("App.cs", "var result = service.Process();", Severity.Info, category, "Problem.", "Fix.", 2)],
        Engine: "openai", Model: "cheap-2024", PromptVersion: "v3", Cost: cost, Diff: Diff);

    private sealed class Counting(string reply, decimal cost) : ILlmClient
    {
        private int _calls;

        public int Calls => _calls;

        public MessageResponse Request(object requestBody)
        {
            Interlocked.Increment(ref _calls);
            return new MessageResponse
            {
                Model = "m",
                Content = [new ContentBlock { Type = "text", Text = reply }],
                Usage = new Usage { InputTokens = 1, OutputTokens = 1 },
                Cost = cost,
            };
        }
    }
}
