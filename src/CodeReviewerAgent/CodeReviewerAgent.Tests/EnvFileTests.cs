using CodeReviewerAgent.Infra;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class EnvFileTests
{
    [Fact]
    public void Candidates_AreTheFilesOnTheWayUp_NearestFirst()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cra-env-{Guid.NewGuid():N}");
        var project = Path.Combine(root, "src", "Project");
        var output = Path.Combine(project, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(output);

        try
        {
            var shared = Path.Combine(root, ".env");
            var local = Path.Combine(project, ".env");
            File.WriteAllText(shared, "STORAGE=postgres");
            File.WriteAllText(local, "STORAGE=files");

            // The one closest to the executable comes first, and DotNetEnv's NoClobber makes the
            // first value win, so a file next to a project overrides the shared one.
            Assert.Equal([local, shared], EnvFile.Candidates(output));

            File.Delete(local);
            Assert.Equal([shared], EnvFile.Candidates(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Candidates_WithNoFileAnywhere_AreEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cra-env-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            Assert.Empty(EnvFile.Candidates(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
