using System.Diagnostics;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Runs an external command and returns its standard output.
    /// </summary>
    internal static class ProcessRunner
    {
        public static string Run(string fileName, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start {fileName}.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"`{fileName} {arguments}` failed: {error}");

            return output;
        }
    }
}
