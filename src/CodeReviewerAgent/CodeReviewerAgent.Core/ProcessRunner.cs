using System.Diagnostics;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Runs an external command and returns its standard output. Commands run in the
    /// repository directory given by the <c>REPO_DIR</c> environment variable, so diffs
    /// and pull requests can be analyzed from another repository; when it is blank the
    /// current working directory is used.
    /// </summary>
    internal static class ProcessRunner
    {
        private static string RepoDirectory =>
            Environment.GetEnvironmentVariable("REPO_DIR") ?? "";

        public static string Run(string fileName, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = RepoDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            return Run(fileName, startInfo, arguments);
        }

        /// <summary>
        /// Runs an external command with each argument passed separately, so values
        /// containing spaces (e.g. user-supplied file paths) are escaped correctly.
        /// </summary>
        public static string Run(string fileName, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = RepoDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            return Run(fileName, startInfo, string.Join(' ', arguments));
        }

        /// <summary>
        /// Runs an external command with each argument passed separately, feeding
        /// <paramref name="input"/> to its standard input. Used to pass large payloads
        /// (e.g. a diff) that would overflow the command-line length limit.
        /// <paramref name="workingDirectory"/> overrides the repository directory — pass a
        /// neutral path when the command must not pick up the repository's local config.
        /// <paramref name="removeEnvironmentVariables"/> are dropped from the child's
        /// environment (e.g. an API key that must not override a subscription login).
        /// </summary>
        public static string RunWithInput(string fileName, string input, string workingDirectory,
            IReadOnlyCollection<string> removeEnvironmentVariables, string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            foreach (var name in removeEnvironmentVariables)
                startInfo.Environment.Remove(name);

            return Run(fileName, startInfo, string.Join(' ', arguments), input);
        }

        private const int TimeoutMs = 120_000;

        private static string Run(string fileName, ProcessStartInfo startInfo, string argumentsForError, string? input = null)
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start {fileName}.");

            // Drain both streams asynchronously so a full pipe buffer can't deadlock the wait.
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (input is not null)
            {
                process.StandardInput.Write(input);
                process.StandardInput.Close();
            }

            if (!process.WaitForExit(TimeoutMs))
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException(
                    $"`{fileName} {argumentsForError}` timed out after {TimeoutMs / 1000}s.");
            }

            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"`{fileName} {argumentsForError}` failed: {error}");

            return output;
        }
    }
}
