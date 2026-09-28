namespace CodeReviewerAgent.Core;

/// <summary>
/// Resolves the current <see cref="Project"/> from the repository being analyzed. The folder is
/// the one the caller hands in (the entry point resolves it from <c>REPO_DIR</c>, the same value
/// the diff sources run their commands in) or the current directory, normalized to an absolute
/// path; the initial name is the folder's last segment. No database I/O of its own — it delegates
/// creation/reuse to the given repository.
/// </summary>
public static class ProjectResolver
{
    /// <param name="repositoryDirectory">The repository being analyzed; blank means the current directory.</param>
    public static Project Resolve(IProjectRepository projects, string repositoryDirectory = "")
    {
        var folder = string.IsNullOrWhiteSpace(repositoryDirectory)
            ? Directory.GetCurrentDirectory()
            : repositoryDirectory;

        folder = Path.GetFullPath(folder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var name = new DirectoryInfo(folder).Name;
        return projects.GetOrAdd(folder, name);
    }
}
