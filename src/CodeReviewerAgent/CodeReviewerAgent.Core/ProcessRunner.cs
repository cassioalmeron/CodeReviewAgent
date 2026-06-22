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
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            return Run(fileName, startInfo, string.Join(' ', arguments));
        }

        private static string Run(string fileName, ProcessStartInfo startInfo, string argumentsForError)
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start {fileName}.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"`{fileName} {argumentsForError}` failed: {error}");

            return output;
        }
    }
}
