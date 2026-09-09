using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeReviewerAgent.Core.Judge;

namespace CodeReviewerAgent.Core.Golden;

/// <summary>
/// Publishes a finished <see cref="GoldenScore"/>: the report, the console summary lines, and the
/// raw reviews for the judge. Kept apart from <see cref="GoldenEvaluator"/> so running the set
/// stays free of I/O — everything here is formatting and persistence of an already-finished run.
/// </summary>
public static class GoldenEvaluatorReport
{
    /// <summary>
    /// Publishes a finished run: one report covering every round, each labelled with its golden
    /// verdict and the rate summary appended as a footer, plus the raw reviews for the judge.
    /// </summary>
    public static string SaveReport(GoldenScore run)
    {
        var reportPath = ReportGenerator.Save(
            [.. run.Reviews], BuildFooter(run.Results, run.Condition),
            r => run.Verdicts.GetValueOrDefault(r));
        System.Console.WriteLine($"Report saved to {reportPath}");

        PersistReviews(run.Reviews);
        return reportPath;
    }

    // Persist the raw reviews so the judge can score them in a separate run, without
    // re-invoking the (paid) executor. The judge loads this file.
    //
    // This still runs once, after the whole set has finished, and that is now fine: it is no
    // longer the only thing standing between a crash and the money. GoldenRoundStore appends each
    // round as it comes back, so a run that dies loses nothing it paid for and resumes from what
    // is on disk. This file is the judge's input, rebuilt from a finished run — not the durable
    // record of one.
    private static void PersistReviews(IReadOnlyList<ReviewResult> reviews)
    {
        var directory = OutputPaths.Reviews;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "eval-results.json");
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        File.WriteAllText(path, JsonSerializer.Serialize(reviews, options));
        System.Console.WriteLine($"Eval results saved to {path}");
    }

    // A single result line, shared by the console output and the report footer:
    // PASS = succeeded every run, FAIL = never, FLAKY = some but not all. The prompt version is
    // shown only when a run actually compares two of them — a single-side run reads exactly as
    // it did before this label existed.
    public static string FormatLine(GoldenCaseResult r, bool showPromptVersion = false)
    {
        var status = r.Successes == r.Runs ? "PASS" : r.Successes == 0 ? "FAIL" : "FLAKY";
        var kind = r.Kind == GoldenKind.Trap ? "trap" : "detection";
        var version = showPromptVersion ? $", {r.PromptVersion}" : "";
        var since = r.Since is null ? "" : $", {r.Since}";
        var detail = r.Successes == r.Runs ? "" : $" — {r.MissDetail}";
        return $"[{status}] {r.Name} ({kind}{version}{since}) {r.Successes}/{r.Runs}{detail}";
    }

    /// <summary>
    /// The two rates, side by side and never summed — the console summary. When the run
    /// compared two prompt versions, each gets its own line so the rates are never blended
    /// across sides either.
    /// </summary>
    public static string FormatTotals(IReadOnlyList<GoldenCaseResult> results)
    {
        var sides = PromptVersions(results);
        return string.Join(Environment.NewLine, sides.Select(side =>
        {
            var inSide = results.Where(r => r.PromptVersion == side).ToList();
            var summary = $"Detection {Rate(inSide, GoldenKind.Detection)}";
            var line = inSide.Any(r => r.Kind == GoldenKind.Trap)
                ? $"{summary} · Trap resistance {Rate(inSide, GoldenKind.Trap)}"
                : summary;
            return sides.Count > 1 ? $"{side}: {line}" : line;
        }));
    }

    public static string BuildFooter(IReadOnlyList<GoldenCaseResult> results, GoldenCondition condition)
    {
        var footer = new StringBuilder();
        footer.AppendLine("# Golden set v2");
        footer.AppendLine();
        AppendCondition(footer, condition);

        var sides = PromptVersions(results);
        var showPromptVersion = sides.Count > 1;

        footer.AppendLine("```text");
        foreach (var r in results)
            footer.AppendLine(FormatLine(r, showPromptVersion));
        footer.AppendLine("```");
        footer.AppendLine();

        var hasTraps = results.Any(r => r.Kind == GoldenKind.Trap);
        foreach (var side in sides)
        {
            var inSide = results.Where(r => r.PromptVersion == side).ToList();
            var label = showPromptVersion ? $" ({side})" : "";
            footer.AppendLine($"- **Detection**{label} {Rate(inSide, GoldenKind.Detection)} {Scope(inSide, GoldenKind.Detection)}");
            if (hasTraps)
                footer.AppendLine($"- **Trap resistance**{label} {Rate(inSide, GoldenKind.Trap)} {Scope(inSide, GoldenKind.Trap)}");
        }

        foreach (var side in sides)
        {
            var inSide = results.Where(r => r.PromptVersion == side).ToList();
            var label = showPromptVersion ? $" ({side})" : "";
            AppendPrecision(footer, inSide, label);
            AppendCalibration(footer, inSide, label);
        }

        foreach (var side in sides)
            AppendVersionLadder(footer, [.. results.Where(r => r.PromptVersion == side)], hasTraps,
                showPromptVersion ? side : null);

        AppendUnforeseen(footer, results, showPromptVersion);
        return footer.ToString();
    }

    // Precision counts findings, not runs: a run that emitted four findings which should not exist
    // has to weigh four times one that emitted a single one, and a mean of per-run rates would
    // flatten exactly that. The two kinds of case are reported apart for the same reason detection
    // and trap resistance are — their denominators mean different things.
    private static void AppendPrecision(StringBuilder footer, IReadOnlyList<GoldenCaseResult> results, string label)
    {
        foreach (var kind in new[] { GoldenKind.Detection, GoldenKind.Trap })
        {
            var inKind = results.Where(r => r.Kind == kind).ToList();
            var counted = inKind.Sum(r => r.PrecisionCounted);
            if (counted == 0)
                continue;

            var correct = inKind.Sum(r => r.PrecisionCorrect);
            var name = kind == GoldenKind.Detection ? "Precision" : "Precision (traps)";
            footer.AppendLine(
                $"- **{name}**{label} {correct}/{counted} ({(double)correct / counted:P1})" +
                $" · {counted - correct} finding(s) that should not have been said");
        }
    }

    // Signed distance on the severity scale: Info 0, Warning 1, Critical 2. The mean alone is not
    // enough — one run at +1 and another at −1 average to zero, which reads as perfect calibration
    // when neither run was right — so the counts are printed beside it.
    private static void AppendCalibration(StringBuilder footer, IReadOnlyList<GoldenCaseResult> results, string label)
    {
        var distances = results
            .Where(r => r.CalibrationDistances is not null)
            .SelectMany(r => r.CalibrationDistances!)
            .ToList();
        if (distances.Count == 0)
            return;

        var exact = distances.Count(d => d == 0);
        var inflated = distances.Count(d => d > 0);
        var understated = distances.Count(d => d < 0);
        footer.AppendLine(
            $"- **Calibration**{label} mean {distances.Average():+0.00;-0.00;0.00}" +
            $" · exact {exact}/{distances.Count} · inflated {inflated} · understated {understated}");
    }

    // Findings that were neither planted, nor listed, nor repeats. On a detection case they cost
    // nothing — what is incomplete may well be the list, not the model — so they are printed for a
    // human to read. A remark that keeps coming back across runs is the candidate to promote into
    // the case's AlsoAcceptable; one that appears once is noise until it repeats.
    private static void AppendUnforeseen(
        StringBuilder footer, IReadOnlyList<GoldenCaseResult> results, bool showPromptVersion)
    {
        var rows = results
            .Where(r => r.Unforeseen is { Count: > 0 })
            .SelectMany(r => r.Unforeseen!.Select(f => (Result: r, Finding: f)))
            .GroupBy(x => (
                x.Result.Name,
                x.Result.PromptVersion,
                x.Finding.Severity,
                Problem: Truncate(x.Finding.Problem, 90)))
            .OrderByDescending(g => g.Count())
            .ToList();
        if (rows.Count == 0)
            return;

        footer.AppendLine();
        footer.AppendLine("## Unforeseen findings");
        footer.AppendLine();
        footer.AppendLine(
            "Neither planted, listed, nor repeats. They carry no penalty on a detection case." +
            " What repeats across runs is a candidate for that case's `alsoAcceptable`.");
        footer.AppendLine();
        footer.AppendLine($"| Case |{(showPromptVersion ? " Prompt |" : "")} Runs | Severity | Finding |");
        footer.AppendLine($"|---|{(showPromptVersion ? "---|" : "")}---:|---|---|");
        foreach (var g in rows)
            footer.AppendLine(
                $"| {g.Key.Name} |{(showPromptVersion ? $" {g.Key.PromptVersion} |" : "")}" +
                $" {g.Count()} | {g.Key.Severity} | {g.Key.Problem} |");
    }

    // A finding's problem text is prose written by a model: it can be long and it can contain the
    // pipe that would break the table it is being rendered into.
    private static string Truncate(string? text, int max)
    {
        var single = string
            .Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Replace("|", "\\|");
        return single.Length <= max ? single : single[..max] + "…";
    }

    // The distinct prompt versions present in a run, in first-seen order — one entry for
    // today's single-version set, two when `eval` was run as a pairwise comparison.
    private static List<string> PromptVersions(IReadOnlyList<GoldenCaseResult> results) =>
        [.. results.Select(r => r.PromptVersion).Distinct()];

    // Which harness configuration produced these numbers. Without it two runs of opposite
    // conditions render identically, which is how a ruler gets swapped mid-measurement.
    private static void AppendCondition(StringBuilder footer, GoldenCondition condition)
    {
        footer.AppendLine($"Condition: **{condition.Label}** (SKILLS={condition.Setting})");
        footer.AppendLine();

        if (condition.IsBaseline || condition.Rounds == 0)
            return;

        var active = condition.SkillRounds.Count == 0
            ? "none"
            : string.Join(", ", condition.SkillRounds
                .OrderByDescending(s => s.Value)
                .Select(s => $"{s.Key} ({s.Value}/{condition.Rounds})"));
        footer.AppendLine($"Skills active: {active}");
        footer.AppendLine();

        if (condition.RoundsWithoutSkill > 0)
        {
            footer.AppendLine(
                $"> **{condition.RoundsWithoutSkill}/{condition.Rounds} rounds ran with no skill loaded.** "
                + "Those rounds are baseline in disguise: averaging them into the harness condition "
                + "dilutes the delta between the two.");
            footer.AppendLine();
        }
    }

    // Scaled by the C# version each case requires, so the report says which constructs a model
    // handles badly rather than only that it failed. Deliberately not called a knowledge cutoff:
    // measurement rejected that reading. gpt-4.1 (cutoff jun/2024) fails C# 12 from 2023 and
    // deepseek (cutoff apr/2026) fails C# 11 from 2022, both well inside their training window.
    private static void AppendVersionLadder(
        StringBuilder footer, IReadOnlyList<GoldenCaseResult> results, bool hasTraps, string? sideLabel)
    {
        // Ordered as a ladder, oldest first — "C# 8" before "C# 14", which a string sort gets
        // backwards. A scrambled ladder defeats the point of the table.
        var groups = results
            .GroupBy(r => r.Since ?? "agnostic")
            .OrderBy(g => VersionRank(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count < 2)
            return;

        footer.AppendLine();
        footer.AppendLine(sideLabel is null ? "## By required C# version" : $"## By required C# version ({sideLabel})");
        footer.AppendLine();
        footer.AppendLine($"| Since | Cases | Detection |{(hasTraps ? " Trap resistance |" : "")}");
        footer.AppendLine($"|---|---|---|{(hasTraps ? "---|" : "")}");
        foreach (var group in groups)
        {
            var inGroup = group.ToList();
            var traps = hasTraps ? $" {Rate(inGroup, GoldenKind.Trap)} |" : "";
            footer.AppendLine($"| {group.Key} | {inGroup.Count} | {Rate(inGroup, GoldenKind.Detection)} |{traps}");
        }
    }

    // Version-agnostic cases sort first; the rest by the number in "C# <n>". Anything that
    // parses as neither lands at the end rather than silently jumping the queue.
    private static int VersionRank(string since)
    {
        if (since == "agnostic")
            return -1;
        var digits = new string([.. since.Where(char.IsDigit)]);
        return int.TryParse(digits, out var version) ? version : int.MaxValue;
    }

    private static string Rate(IReadOnlyList<GoldenCaseResult> results, GoldenKind kind)
    {
        var ofKind = results.Where(r => r.Kind == kind).ToList();
        return ofKind.Count == 0 ? "—" : $"{ofKind.Sum(r => r.Successes)}/{ofKind.Sum(r => r.Runs)}";
    }

    private static string Scope(IReadOnlyList<GoldenCaseResult> results, GoldenKind kind)
    {
        var ofKind = results.Where(r => r.Kind == kind).ToList();
        if (ofKind.Count == 0)
            return "";
        var runs = ofKind[0].Runs;
        return $"({ofKind.Count} case{(ofKind.Count == 1 ? "" : "s")} × {runs} run{(runs == 1 ? "" : "s")})";
    }
}
