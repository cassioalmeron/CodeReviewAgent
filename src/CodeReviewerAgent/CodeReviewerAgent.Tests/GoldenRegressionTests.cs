using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class GoldenRegressionTests
{
    private static readonly GoldenConfiguration Ci = new("gpt-4o-mini", "globs", "v3", 5, Temperature: 0);

    private static GoldenCaseResult Case(string name, int cleanRounds, string promptVersion = "v3") =>
        new(name, GoldenKind.Detection, null, promptVersion, Successes: 5, Runs: 5, MissDetail: null, CleanRounds: cleanRounds);

    private static GoldenBaseline Baseline(params (string Name, int Clean)[] cases) =>
        GoldenBaseline.From(Ci, cases.Select(c => Case(c.Name, c.Clean)));

    [Fact]
    public void Compare_TheSameRun_PassesEveryCase()
    {
        var report = GoldenRegression.Compare(Baseline(("sql-injection", 5), ("off-by-one", 4)), Ci,
            [Case("sql-injection", 5), Case("off-by-one", 4)]);

        Assert.False(report.Failed);
        Assert.All(report.Cases, c => Assert.False(c.Regressed));
    }

    [Theory]
    [InlineData(5, 4, false)] // one round down: within the tolerance
    [InlineData(5, 3, false)] // two down: still what identical runs did in phase 0
    [InlineData(5, 2, true)]  // three down: a regression
    [InlineData(2, 5, false)] // better is never a regression
    [InlineData(1, 0, false)]
    public void Compare_FailsOnlyBeyondTheTolerance(int before, int now, bool regressed)
    {
        var report = GoldenRegression.Compare(Baseline(("sql-injection", before)), Ci, [Case("sql-injection", now)]);

        Assert.Equal(regressed, report.Failed);
        Assert.Equal(regressed, Assert.Single(report.Cases).Regressed);
    }

    [Fact]
    public void Compare_ACaseMissingFromTheBaseline_IsReportedAndDoesNotFail()
    {
        var report = GoldenRegression.Compare(Baseline(("sql-injection", 5)), Ci,
            [Case("sql-injection", 5), Case("brand-new-case", 0)]);

        Assert.False(report.Failed);
        Assert.True(report.Cases.Single(c => c.Name == "brand-new-case").IsNew);
    }

    [Fact]
    public void Compare_ABaselineFromAnotherConfiguration_IsRefusedNotCompared()
    {
        var otherModel = Ci with { Model = "claude-haiku-4-5" };

        var report = GoldenRegression.Compare(Baseline(("sql-injection", 5)), otherModel, [Case("sql-injection", 5)]);

        Assert.True(report.Failed);
        Assert.Contains("claude-haiku-4-5", report.Mismatch);
        Assert.Empty(report.Cases);
    }

    [Fact]
    public void Compare_ABaselineAtAnotherTemperature_IsRefused()
    {
        var atDefault = Ci with { Temperature = null };

        var report = GoldenRegression.Compare(Baseline(("sql-injection", 5)), atDefault, [Case("sql-injection", 5)]);

        Assert.True(report.Failed);
        Assert.Contains("temperature 0", report.Mismatch);
        Assert.Contains("default temperature", report.Mismatch);
    }

    [Fact]
    public void Compare_OnlyLooksAtTheConfiguredPromptVersion()
    {
        // A comparison run carries both sides; the other version's collapse is not this baseline's business.
        var report = GoldenRegression.Compare(Baseline(("sql-injection", 5)), Ci,
            [Case("sql-injection", 5), Case("sql-injection", 0, promptVersion: "v5")]);

        Assert.False(report.Failed);
        Assert.Single(report.Cases);
    }

    [Fact]
    public void Markdown_NamesTheRegressedCase()
    {
        var report = GoldenRegression.Compare(Baseline(("sql-injection", 5)), Ci, [Case("sql-injection", 2)]);

        var markdown = report.ToMarkdown();
        Assert.Contains("| sql-injection | 5 | 2 | **regressed** |", markdown);
        Assert.Contains("**Regression.**", markdown);
    }

    [Fact]
    public void Baseline_RoundTripsThroughItsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cra-baseline-{Guid.NewGuid():N}.json");
        try
        {
            var written = Baseline(("sql-injection", 5), ("extension-block", 0));
            written.Save(path);

            var read = GoldenBaseline.Load(path);

            Assert.Equal(Ci, read.Configuration);
            Assert.Equal(new GoldenBaselineCase(5, 5), read.Cases["sql-injection"]);
            Assert.Equal(new GoldenBaselineCase(5, 0), read.Cases["extension-block"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
