using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodeReviewerAgent.Core;

namespace CodeReviewerAgent.Infra
{
    /// <summary>
    /// Shared file-backed storage: entities are JSON files named
    /// <c>&lt;prefix&gt;-&lt;id&gt;-&lt;timestamp&gt;.json</c>. The next id is the highest existing
    /// id + 1, read from the file names (no database, no index file).
    /// </summary>
    internal static class FileStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static int NextId(string directory, string prefix)
        {
            if (!Directory.Exists(directory))
                return 1;

            var pattern = new Regex($@"^{Regex.Escape(prefix)}-(\d+)-", RegexOptions.Compiled);
            var maxId = Directory.EnumerateFiles(directory, $"{prefix}-*.json")
                .Select(f => pattern.Match(Path.GetFileName(f)))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value))
                .DefaultIfEmpty(0)
                .Max();
            return maxId + 1;
        }

        public static void Save<T>(string directory, string prefix, int id, T entity)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{prefix}-{id}-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(entity, JsonOptions));
        }

        public static T? Get<T>(string directory, string prefix, int id) where T : class
        {
            if (!Directory.Exists(directory))
                return null;
            var path = Directory.EnumerateFiles(directory, $"{prefix}-{id}-*.json").FirstOrDefault();
            return path is null ? null : JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }

        public static IReadOnlyList<T> List<T>(string directory, string prefix)
        {
            if (!Directory.Exists(directory))
                return [];
            var pattern = new Regex($@"^{Regex.Escape(prefix)}-(\d+)-", RegexOptions.Compiled);
            return Directory.EnumerateFiles(directory, $"{prefix}-*.json")
                .Select(f => (File: f, Match: pattern.Match(Path.GetFileName(f))))
                .Where(x => x.Match.Success)
                .OrderBy(x => int.Parse(x.Match.Groups[1].Value))
                .Select(x => JsonSerializer.Deserialize<T>(File.ReadAllText(x.File), JsonOptions)!)
                .ToList();
        }
    }

    public class FileDiffRepository : IDiffRepository
    {
        private static readonly string Directory =
            Path.Combine(AppContext.BaseDirectory, "diffs");

        public int Save(Diff diff)
        {
            var id = FileStore.NextId(Directory, "diff");
            FileStore.Save(Directory, "diff", id, diff with { Id = id, ContentHash = Hashing.Sha256(diff.Content) });
            return id;
        }

        public int GetOrAdd(Diff diff)
        {
            var hash = Hashing.Sha256(diff.Content);
            var existing = List().FirstOrDefault(d => d.ContentHash == hash);
            return existing?.Id ?? Save(diff);
        }

        public Diff? Get(int id) => FileStore.Get<Diff>(Directory, "diff", id);

        public IReadOnlyList<Diff> List() => FileStore.List<Diff>(Directory, "diff");
    }

    public class FileAnalysisRepository : IAnalysisRepository
    {
        // A dedicated directory (not the legacy "reviews/" folder, whose timestamp-named
        // files would be misread as ids by the "review-<id>-" pattern).
        private static readonly string Directory =
            Path.Combine(AppContext.BaseDirectory, "analyses");

        public int Save(Analysis analysis)
        {
            var id = FileStore.NextId(Directory, "review");
            FileStore.Save(Directory, "review", id, analysis with { Id = id });
            return id;
        }

        public Analysis? Get(int id) => FileStore.Get<Analysis>(Directory, "review", id);

        public IReadOnlyList<Analysis> List() => FileStore.List<Analysis>(Directory, "review");
    }

    public class FileJudgeEvaluationRepository : IJudgeEvaluationRepository
    {
        private static readonly string Directory =
            Path.Combine(AppContext.BaseDirectory, "judgements");

        public int Save(JudgeEvaluation evaluation)
        {
            var id = FileStore.NextId(Directory, "judge");
            FileStore.Save(Directory, "judge", id, evaluation with { Id = id });
            return id;
        }

        public JudgeEvaluation? Get(int id) => FileStore.Get<JudgeEvaluation>(Directory, "judge", id);

        public IReadOnlyList<JudgeEvaluation> List() => FileStore.List<JudgeEvaluation>(Directory, "judge");
    }
}
