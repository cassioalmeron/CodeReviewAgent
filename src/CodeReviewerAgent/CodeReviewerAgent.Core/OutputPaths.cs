namespace CodeReviewerAgent.Core;

/// <summary>
/// Where a run writes what it produces: reports, raw reviews, raw judgments.
/// <para>
/// These all used to be anchored to <see cref="AppContext.BaseDirectory"/>, which is the build
/// output. That put months of paid evaluation history one <c>clean</c> away from gone, and it
/// also put the eval artefacts in the same <c>reviews</c> folder as the file-backed repository's
/// own store, so the two were indistinguishable on disk. A configured root moves the eval side
/// out; the repository store stays where it is, which separates them.
/// </para>
/// <para>
/// The default is the old location on purpose: someone who clones the repository and runs it
/// with no configuration gets exactly the previous behaviour, and nothing silently writes
/// outside the working tree.
/// </para>
/// <para>
/// The root arrives through <see cref="Configure"/>, called once by the entry point after it has
/// loaded the configuration file. Core no longer reads <c>EVAL_OUTPUT_DIR</c> itself (US-018):
/// reading it here made the value depend on which line of the process ran first, and it froze
/// whatever the first test class to touch a path happened to have set. Configuring after
/// something has already asked for a path still has no effect on that path, so the call belongs
/// at startup and nowhere else.
/// </para>
/// </summary>
public static class OutputPaths
{
    private static string? _root;

    public static string Root => _root ?? AppContext.BaseDirectory;

    /// <summary>Generated reports, one file per run.</summary>
    public static string Reports => Path.Combine(Root, "reports");

    /// <summary>Raw reviews and judgments, the durable input a report can be rebuilt from.</summary>
    public static string Reviews => Path.Combine(Root, "reviews");

    /// <summary>
    /// Sets the root for every path above. Null or blank keeps the default, which is the build
    /// output directory.
    /// </summary>
    public static void Configure(string? root) => _root = Normalize(root);

    /// <summary>
    /// The rule that turns a configured value into a root, kept separate and reachable from the
    /// tests: the trimming and the blank-is-unset case are pinned here, without a test having to
    /// move the process-wide state the properties above answer from.
    /// </summary>
    internal static string Resolve(string? configured) =>
        Normalize(configured) ?? AppContext.BaseDirectory;

    private static string? Normalize(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
}
