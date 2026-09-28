namespace CodeReviewerAgent.Core.Diff;

/// <summary>
/// Selects the <see cref="IDiffSource"/> from the command-line arguments:
/// <list type="bullet">
///   <item><c>pr &lt;number&gt;</c> — a real pull request</item>
///   <item><c>staged</c> — the staged changes</item>
///   <item><c>files &lt;path&gt; [path...]</c> — specific files</item>
///   <item><i>(no args)</i> — the local repository (staged if any, else HEAD)</item>
/// </list>
/// </summary>
/// <param name="repositoryDirectory">
/// The repository the commands run in, resolved by the entry point from <c>REPO_DIR</c>; blank
/// means the current directory. It used to be read inside <c>ProcessRunner</c> (US-018).
/// </param>
public static class DiffSourceFactory
{
    public static IDiffSource Create(string[] args, string repositoryDirectory = "") => args switch
    {
        ["pr", var prArg, ..] when int.TryParse(prArg, out var prNumber)
            => new PullRequestDiffSource(prNumber, repositoryDirectory),
        ["staged", ..] => new StagedDiffSource(repositoryDirectory),
        ["files", .. var paths] => new FilesDiffSource(paths, repositoryDirectory),
        _ => new LocalDiffSource(repositoryDirectory),
    };
}
