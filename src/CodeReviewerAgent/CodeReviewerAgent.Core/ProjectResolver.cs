namespace CodeReviewerAgent.Core;

/// <summary>
/// Resolves the current <see cref="Project"/> from the repository being analyzed. The folder
/// is <c>REPO_DIR</c> (the same variable <see cref="ProcessRunner"/> uses) or the current
/// directory, normalized to an absolute path; the initial name is the folder's last segment.
/// No database I/O of its own — it delegates creation/reuse to the given repository.
/// </summary>
public static class ProjectResolver
{
    public static Project Resolve(IProjectRepository projects)
    {
        var repoDir = Environment.GetEnvironmentVariable("REPO_DIR");
        var folder = string.IsNullOrWhiteSpace(repoDir)
            ? Directory.GetCurrentDirectory()
            : repoDir;

        folder = Path.GetFullPath(folder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var name = new DirectoryInfo(folder).Name;
        return projects.GetOrAdd(folder, name);
    }
}
