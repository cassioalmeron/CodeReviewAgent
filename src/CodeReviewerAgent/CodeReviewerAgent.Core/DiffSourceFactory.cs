namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Selects the <see cref="IDiffSource"/> from the command-line arguments:
    /// <list type="bullet">
    ///   <item><c>pr &lt;number&gt;</c> — a real pull request</item>
    ///   <item><c>staged</c> — the staged changes</item>
    ///   <item><c>files &lt;path&gt; [path...]</c> — specific files</item>
    ///   <item><i>(no args)</i> — the local repository (staged if any, else HEAD)</item>
    /// </list>
    /// </summary>
    public static class DiffSourceFactory
    {
        public static IDiffSource Create(string[] args) => args switch
        {
            ["pr", var prArg, ..] when int.TryParse(prArg, out var prNumber)
                => new PullRequestDiffSource(prNumber),
            ["staged", ..] => new StagedDiffSource(),
            ["files", .. var paths] => new FilesDiffSource(paths),
            _ => new LocalDiffSource(),
        };
    }
}
