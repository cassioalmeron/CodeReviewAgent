using System.Text;

namespace CodeReviewerAgent.Core.Golden;

/// <summary>One case of a run set against its baseline.</summary>
/// <param name="BaselineCleanRounds">Null when the case is not in the baseline yet: reported, never failed.</param>
public sealed record CaseComparison(string Name, int? BaselineCleanRounds, int CleanRounds, bool Regressed)
{
    public bool IsNew => BaselineCleanRounds is null;
}

/// <summary>
/// The outcome of comparing a run with its baseline. It fails when the baseline belongs to another
/// configuration, or when any case regressed.
/// </summary>
public sealed record RegressionReport(string? Mismatch, IReadOnlyList<CaseComparison> Cases)
{
    public bool Failed => Mismatch is not null || Cases.Any(c => c.Regressed);

    public string ToMarkdown()
    {
        var text = new StringBuilder();
        text.AppendLine("## Golden set regression");
        text.AppendLine();

        if (Mismatch is not null)
        {
            text.AppendLine($"**Not compared.** {Mismatch}");
            return text.ToString();
        }

        text.AppendLine(Failed
            ? "**Regression.** At least one case lost more clean rounds than the tolerance allows."
            : "**No regression.**");
        text.AppendLine();
        text.AppendLine("| Case | Baseline | Now | Result |");
        text.AppendLine("|---|---|---|---|");
        foreach (var c in Cases)
        {
            var result = c.IsNew ? "new, not in the baseline" : c.Regressed ? "**regressed**" : "ok";
            text.AppendLine($"| {c.Name} | {c.BaselineCleanRounds?.ToString() ?? "—"} | {c.CleanRounds} | {result} |");
        }
        return text.ToString();
    }
}

/// <summary>
/// US-014's rule: a change cannot make an old case worse. A case regresses when its clean rounds fall
/// below the baseline by more than <see cref="Tolerance"/>.
/// <para>
/// Clean rounds, not detection, because they are ADR-015's measure of a case: found, quiet and exactly
/// calibrated. A case that still finds the bug but starts adding noise has also got worse.
/// </para>
/// </summary>
public static class GoldenRegression
{
    /// <summary>
    /// Measured, not guessed (plan 015, phase 0): three identical runs of the CI configuration
    /// (gpt-4o-mini, temperature 0, skills by globs, 5 rounds) left 12 of 15 cases identical and moved
    /// the others by at most 2 clean rounds. With 1, two of six identical pairs would fail by chance;
    /// with 2, none. A case therefore regresses when it loses 3 or more of its 5.
    /// </summary>
    public const int Tolerance = 2;

    public static RegressionReport Compare(
        GoldenBaseline baseline, GoldenConfiguration configuration, IEnumerable<GoldenCaseResult> results,
        int tolerance = Tolerance)
    {
        if (baseline.Configuration != configuration)
            return new RegressionReport(
                $"The baseline was measured with {baseline.Configuration}; this run used {configuration}. " +
                "Rewrite the baseline with GOLDEN_BASELINE_WRITE under the new configuration.",
                []);

        var cases = results
            .Where(r => r.PromptVersion == configuration.PromptVersion)
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => baseline.Cases.TryGetValue(r.Name, out var before)
                ? new CaseComparison(r.Name, before.CleanRounds, r.CleanRounds, before.CleanRounds - r.CleanRounds > tolerance)
                : new CaseComparison(r.Name, null, r.CleanRounds, false))
            .ToList();

        return new RegressionReport(null, cases);
    }
}
