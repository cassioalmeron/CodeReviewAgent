namespace CodeReviewerAgent.Core;

/// <summary>
/// Where a run writes what it produces: reports, raw reviews, raw judgments.
/// <para>
/// These all used to be anchored to <see cref="AppContext.BaseDirectory"/>, which is the build
/// output. That put months of paid evaluation history one <c>clean</c> away from gone, and it
/// also put the eval artefacts in the same <c>reviews</c> folder as the file-backed repository's
/// own store, so the two were indistinguishable on disk. <c>EVAL_OUTPUT_DIR</c> moves the eval
/// side out; the repository store stays where it is, which separates them.
/// </para>
/// <para>
/// The default is the old location on purpose: someone who clones the repository and runs it
/// with no configuration gets exactly the previous behaviour, and nothing silently writes
/// outside the working tree.
/// </para>
/// <para>
/// Resolved once per process. The environment is read on first access, not at load time, which
/// matters because the entry point loads the env file as its first statement — a value captured
/// before that would be the one from before the configuration existed. The consequence is that
/// changing <c>EVAL_OUTPUT_DIR</c> after something has already asked for a path has no effect
/// for the rest of the process.
/// </para>
/// </summary>
public static class OutputPaths
{
    private static readonly Lazy<string> RootPath = new(Resolve);
    private static readonly Lazy<string> ReportsPath = new(() => Path.Combine(Root, "reports"));
    private static readonly Lazy<string> ReviewsPath = new(() => Path.Combine(Root, "reviews"));

    public static string Root => RootPath.Value;

    /// <summary>Generated reports, one file per run.</summary>
    public static string Reports => ReportsPath.Value;

    /// <summary>Raw reviews and judgments, the durable input a report can be rebuilt from.</summary>
    public static string Reviews => ReviewsPath.Value;

    /// <summary>
    /// The resolution rule, kept separate and reachable from the tests. The properties above
    /// answer from a value frozen on first access, so a test that set the variable and then read
    /// them would be pinning whichever test ran first, not the rule. This is the rule.
    /// </summary>
    internal static string Resolve() =>
        Environment.GetEnvironmentVariable("EVAL_OUTPUT_DIR") is { } configured
        && !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim()
            : AppContext.BaseDirectory;
}
