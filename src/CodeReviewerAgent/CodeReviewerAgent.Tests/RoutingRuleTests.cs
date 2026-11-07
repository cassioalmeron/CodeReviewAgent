using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Core.Routing;
using System.Text;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class RoutingRuleTests
{
    private static string FileDiff(string path, int addedLines)
    {
        var sb = new StringBuilder();
        sb.Append($"diff --git a/{path} b/{path}\n");
        sb.Append($"--- a/{path}\n");
        sb.Append($"+++ b/{path}\n");
        sb.Append($"@@ -1,1 +1,{addedLines + 1} @@\n");
        sb.Append(" context\n");
        for (var i = 0; i < addedLines; i++)
            sb.Append($"+line {i}\n");
        return sb.ToString();
    }

    private static string FilesDiff(int files, int linesPerFile) =>
        string.Concat(Enumerable.Range(0, files).Select(i => FileDiff($"src/File{i}.cs", linesPerFile)));

    [Fact]
    public void RoutingRuleTests_A_diff_of_fifty_changed_lines_routes_cheap()
    {
        var diff = FileDiff("src/A.cs", 50);

        Assert.Equal(new DiffSize(50, 1), RoutingRule.Measure(diff));
        Assert.Equal(ModelRoute.Cheap, RoutingRule.Route(diff));
    }

    [Fact]
    public void RoutingRuleTests_A_diff_of_fifty_one_lines_routes_strong()
    {
        var diff = FileDiff("src/A.cs", 51);

        Assert.Equal(new DiffSize(51, 1), RoutingRule.Measure(diff));
        Assert.Equal(ModelRoute.Strong, RoutingRule.Route(diff));
    }

    [Fact]
    public void RoutingRuleTests_A_diff_touching_three_files_routes_cheap()
    {
        var diff = FilesDiff(3, 1);

        Assert.Equal(new DiffSize(3, 3), RoutingRule.Measure(diff));
        Assert.Equal(ModelRoute.Cheap, RoutingRule.Route(diff));
    }

    [Fact]
    public void RoutingRuleTests_A_diff_touching_four_files_routes_strong()
    {
        var diff = FilesDiff(4, 1);

        Assert.Equal(new DiffSize(4, 4), RoutingRule.Measure(diff));
        Assert.Equal(ModelRoute.Strong, RoutingRule.Route(diff));
    }

    [Fact]
    public void RoutingRuleTests_Ten_lines_spread_over_eight_files_routes_strong()
    {
        var diff = $"{FilesDiff(8, 1)}-removed a\n-removed b\n";

        Assert.Equal(new DiffSize(10, 8), RoutingRule.Measure(diff));
        Assert.Equal(ModelRoute.Strong, RoutingRule.Route(diff));
    }

    [Fact]
    public void RoutingRuleTests_One_file_with_eight_hundred_new_lines_routes_strong()
    {
        var diff = FileDiff("src/Big.cs", 800);

        Assert.Equal(new DiffSize(800, 1), RoutingRule.Measure(diff));
        Assert.Equal(ModelRoute.Strong, RoutingRule.Route(diff));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void RoutingRuleTests_An_empty_diff_measures_zero_and_routes_cheap(string? diff)
    {
        Assert.Equal(new DiffSize(0, 0), RoutingRule.Measure(diff));
        Assert.Equal(ModelRoute.Cheap, RoutingRule.Route(diff));
    }

    [Fact]
    public void RoutingRuleTests_A_file_deletion_counts_its_removed_lines_but_not_its_headers()
    {
        const string diff = """
            diff --git a/src/Old.cs b/src/Old.cs
            deleted file mode 100644
            index 1234567..0000000
            --- a/src/Old.cs
            +++ /dev/null
            @@ -1,3 +0,0 @@
            -one
            -two
            -three
            """;

        Assert.Equal(new DiffSize(3, 1), RoutingRule.Measure(diff));
        Assert.Equal(ModelRoute.Cheap, RoutingRule.Route(diff));
    }

    [Fact]
    public void RoutingRuleTests_Code_lines_starting_with_plus_plus_plus_or_minus_minus_minus_without_a_space_are_counted_and_real_headers_are_not()
    {
        const string diff = """
            diff --git a/src/Counter.cs b/src/Counter.cs
            --- a/src/Counter.cs
            +++ b/src/Counter.cs
            @@ -1,2 +1,2 @@
            ---i;
            +++i;
            """;

        Assert.Equal(new DiffSize(2, 1), RoutingRule.Measure(diff));
    }

    /// <summary>
    /// The version names these thresholds. Changing a threshold without bumping the version fails
    /// here, so a rule version cannot drift in silence from the values it stands for.
    /// </summary>
    [Fact]
    public void RoutingRuleTests_Version_v1_means_fifty_lines_and_three_files()
    {
        Assert.Equal("v1", RoutingRule.Version);
        Assert.Equal(50, RoutingRule.MaxChangedLines);
        Assert.Equal(3, RoutingRule.MaxTouchedFiles);
    }

    [Fact]
    public void RoutingRuleTests_A_diff_with_crlf_endings_measures_the_same_as_with_lf()
    {
        var lf = FilesDiff(2, 5);
        var crlf = lf.Replace("\n", "\r\n");
        var lone = lf.Replace("\n", "\r");

        var expected = RoutingRule.Measure(lf);

        Assert.Equal(new DiffSize(10, 2), expected);
        Assert.Equal(expected, RoutingRule.Measure(crlf));
        Assert.Equal(expected, RoutingRule.Measure(lone));
    }

    [Fact]
    public void RoutingRuleTests_All_golden_cases_route_cheap()
    {
        var cases = GoldenEvaluator.LoadCases();

        Assert.Equal(15, cases.Count);
        Assert.All(cases, c =>
        {
            var diff = GoldenEvaluator.LoadDiff(Path.Combine(GoldenEvaluator.CasesDirectory, c.Diff));
            Assert.True(
                RoutingRule.Route(diff) == ModelRoute.Cheap,
                $"Golden case '{c.Name}' routes strong with {RoutingRule.Measure(diff)}.");
        });
    }

    [Fact]
    public void RoutingRuleTests_Every_case_of_the_routing_eval_set_routes_strong()
    {
        var cases = GoldenEvaluator.LoadCases("routing");

        Assert.NotEmpty(cases);
        Assert.All(cases, c =>
        {
            var diff = GoldenEvaluator.LoadDiff(Path.Combine(GoldenEvaluator.CasesDirectoryFor("routing"), c.Diff));
            Assert.True(
                RoutingRule.Route(diff) == ModelRoute.Strong,
                $"Routing case '{c.Name}' routes cheap with {RoutingRule.Measure(diff)}.");
        });
    }
}
