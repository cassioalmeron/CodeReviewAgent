namespace CodeReviewerAgent.Core.Routing;

public enum ModelRoute { Cheap, Strong }

public sealed record DiffSize(int ChangedLines, int TouchedFiles);

/// <summary>
/// Decides which model reviews a diff, from its size alone: no model call, no judgement. The cheap
/// route needs both conditions. One number alone lets a hard diff through: 10 lines over 8 files
/// passes a lines-only rule, and one file with 800 new lines passes a files-only rule.
/// </summary>
public static class RoutingRule
{
    // A constant, not an environment variable: a version that lives in two places lets a
    // measurement report a configuration other than the one that ran.
    public const string Version = "v1";
    public const int MaxChangedLines = 50;
    public const int MaxTouchedFiles = 3;

    public static DiffSize Measure(string? diff)
    {
        if (string.IsNullOrWhiteSpace(diff))
            return new DiffSize(0, 0);

        // Same normalization as DiffSplitter, so a diff measures the same on Windows and Linux.
        var lines = diff.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var changedLines = 0;
        var touchedFiles = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git"))
                touchedFiles++;
            else if (IsChangedLine(line))
                changedLines++;
        }

        return new DiffSize(changedLines, touchedFiles);
    }

    public static ModelRoute Route(string? diff)
    {
        var size = Measure(diff);

        return size.ChangedLines <= MaxChangedLines && size.TouchedFiles <= MaxTouchedFiles
            ? ModelRoute.Cheap
            : ModelRoute.Strong;
    }

    private static bool IsChangedLine(string line) =>
        (line.StartsWith('+') && !line.StartsWith("+++ ")) ||
        (line.StartsWith('-') && !line.StartsWith("--- "));
}
