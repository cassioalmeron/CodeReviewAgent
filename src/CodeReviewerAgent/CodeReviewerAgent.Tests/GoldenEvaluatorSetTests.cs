using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The golden set is one of several eval sets, each a folder under <c>assets/evals</c> with its own
/// <c>cases.json</c>. The named-set tests build a throwaway set beside the bundled ones and delete it,
/// so no placeholder case ever lands in a set that a real measurement reads.
/// </summary>
public class GoldenEvaluatorSetTests
{
    private const string OneCase = """
        [
          {
            "name": "only-case",
            "diff": "only-case.diff",
            "groundTruth": "only-case.md",
            "expect": {
              "$type": "finding",
              "file": "src/A.cs",
              "category": "Bug",
              "keywords": ["null"],
              "severity": "Warning"
            }
          }
        ]
        """;

    private const string NothingFound = """{ "summary": "Nothing.", "findings": [] }""";

    [Fact]
    public void GoldenEvaluatorSetTests_A_named_set_loads_its_own_cases()
    {
        using var set = TemporarySet.With(OneCase);

        var cases = GoldenEvaluator.LoadCases(set.Name);

        var loaded = Assert.Single(cases);
        Assert.Equal("only-case", loaded.Name);
    }

    [Fact]
    public void GoldenEvaluatorSetTests_A_set_with_no_case_loads_as_empty()
    {
        using var set = TemporarySet.With("[]");

        Assert.Empty(GoldenEvaluator.LoadCases(set.Name));
    }

    [Fact]
    public void GoldenEvaluatorSetTests_An_absent_set_fails_with_a_message_naming_the_path_it_looked_for()
    {
        var name = $"absent-{Guid.NewGuid():N}";

        var failure = Assert.Throws<InvalidOperationException>(() => GoldenEvaluator.LoadCases(name));

        Assert.Contains(name, failure.Message);
        Assert.Contains(Path.Combine(GoldenEvaluator.CasesDirectoryFor(name), "cases.json"), failure.Message);
    }

    [Fact]
    public void GoldenEvaluatorSetTests_Loading_no_set_still_returns_the_fifteen_golden_cases()
    {
        var cases = GoldenEvaluator.LoadCases();

        Assert.Equal(15, cases.Count);
        Assert.Equal(cases.Select(c => c.Name), GoldenEvaluator.LoadCases(GoldenEvaluator.GoldenSet).Select(c => c.Name));
    }

    [Fact]
    public void GoldenEvaluatorSetTests_The_bundled_routing_set_is_copied_to_the_output_and_parses()
    {
        var routingFile = Path.Combine(GoldenEvaluator.CasesDirectoryFor("routing"), "cases.json");

        Assert.True(File.Exists(routingFile), $"expected {routingFile}");
        Assert.Null(Record.Exception(() => GoldenEvaluator.LoadCases("routing")));
    }

    [Fact]
    public void GoldenEvaluatorSetTests_A_run_naming_a_set_reviews_only_that_sets_cases()
    {
        using var set = TemporarySet.With(OneCase);
        File.WriteAllText(Path.Combine(set.Directory, "only-case.diff"), "diff --git a/src/A.cs b/src/A.cs\n+++ b/src/A.cs\n+var x = 1;\n");

        var run = GoldenEvaluator.Run(
            new FakeLlmClient(NothingFound), ["v3"],
            settings: new GoldenSettings(Runs: 1, Skills: "off", Set: set.Name)).Scored();

        var result = Assert.Single(run.Results);
        Assert.Equal("only-case", result.Name);
    }

    [Fact]
    public void GoldenEvaluatorSetTests_A_run_naming_no_set_reviews_the_golden_cases()
    {
        var run = GoldenEvaluator.Run(
            new FakeLlmClient(NothingFound), ["v3"], settings: new GoldenSettings(Runs: 1, Skills: "off")).Scored();

        Assert.Equal(GoldenEvaluator.LoadCases().Select(c => c.Name), run.Results.Select(r => r.Name));
    }

    private sealed class TemporarySet : IDisposable
    {
        private TemporarySet(string name, string directory)
        {
            Name = name;
            Directory = directory;
        }

        public string Name { get; }

        public string Directory { get; }

        public static TemporarySet With(string casesJson)
        {
            var name = $"temp-set-{Guid.NewGuid():N}";
            var directory = GoldenEvaluator.CasesDirectoryFor(name);
            System.IO.Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "cases.json"), casesJson);
            return new TemporarySet(name, directory);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
