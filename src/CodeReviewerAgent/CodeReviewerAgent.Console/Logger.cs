using System.Text.Json;

namespace CodeReviewerAgent.Console
{
    /// <summary>
    /// Appends log entries as JSON (one JSON object per line) to a file under the logs directory.
    /// </summary>
    internal static class Logger
    {
        private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

        public static void Log(object entry)
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"llm-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            var json = JsonSerializer.Serialize(entry);
            File.AppendAllText(path, json + Environment.NewLine);
        }
    }
}
