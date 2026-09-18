using DotNetEnv;

namespace CodeReviewerAgent.Infra;

/// <summary>
/// Loads the run's configuration file. There is one at the root of the repository, shared by the
/// Console, the Api and the scripts, because the three have to agree on where the data is: an Api
/// pointing at another store than the Console shows an empty screen and looks like a bug.
/// <para>
/// The search walks up from the executable, so a file placed closer to a project still applies and
/// overrides the shared one. Values already in the environment always win: the variables passed on
/// the command line are what a run is configured with.
/// </para>
/// </summary>
public static class EnvFile
{
    private const string FileName = ".env";

    // From bin/Debug/net10.0 up to the repository root is five levels; the cap is what keeps a
    // program started somewhere unexpected from walking the whole disk.
    private const int MaxLevels = 8;

    public static void Load()
    {
        foreach (var file in Candidates(AppContext.BaseDirectory))
            Env.NoClobber().Load(file);
    }

    /// <summary>The files to load, nearest to the executable first, which is the one that wins.</summary>
    internal static IReadOnlyList<string> Candidates(string startDirectory)
    {
        var found = new List<string>();
        var directory = new DirectoryInfo(startDirectory);

        for (var level = 0; directory is not null && level < MaxLevels; level++, directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, FileName);
            if (File.Exists(path))
                found.Add(path);
        }

        return found;
    }
}
