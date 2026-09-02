using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodeReviewerAgent.Core;

namespace CodeReviewerAgent.Infra;

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
        var path = PathOf(directory, prefix, id);
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

    /// <summary>Rewrites the existing file of entity <paramref name="id"/> in place (for renames/updates).</summary>
    public static void Overwrite<T>(string directory, string prefix, int id, T entity)
    {
        var path = PathOf(directory, prefix, id)
            ?? throw new InvalidOperationException($"No {prefix} file with id {id}.");
        File.WriteAllText(path, JsonSerializer.Serialize(entity, JsonOptions));
    }

    private static string? PathOf(string directory, string prefix, int id)
    {
        if (!Directory.Exists(directory))
            return null;
        return Directory.EnumerateFiles(directory, $"{prefix}-{id}-*.json").FirstOrDefault();
    }
}

public class FileProjectRepository : IProjectRepository
{
    private static readonly string Directory =
        Path.Combine(AppContext.BaseDirectory, "projects");

    public Project GetOrAdd(string folder, string name)
    {
        // Folder is the natural key; match case-insensitively (Windows paths are case-insensitive,
        // so "C:\..." and "c:\..." are the same repo and must not create two projects).
        var existing = List().FirstOrDefault(p => string.Equals(p.Folder, folder, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        var id = FileStore.NextId(Directory, "project");
        var project = new Project { Id = id, Name = name, Folder = folder, CreatedAt = DateTime.UtcNow };
        FileStore.Save(Directory, "project", id, project);
        return project;
    }

    public Project? Get(int id) => FileStore.Get<Project>(Directory, "project", id);

    public IReadOnlyList<Project> List() => FileStore.List<Project>(Directory, "project");

    public void Rename(int id, string name)
    {
        var project = Get(id) ?? throw new InvalidOperationException($"No project with id {id}.");
        FileStore.Overwrite(Directory, "project", id, project with { Name = name });
    }
}

public class FileReviewRepository : IReviewRepository
{
    private static readonly string Directory =
        Path.Combine(AppContext.BaseDirectory, "reviews");

    public int Save(Review review)
    {
        var id = FileStore.NextId(Directory, "review");
        FileStore.Save(Directory, "review", id, review with { Id = id, ContentHash = Hashing.Sha256(review.Content) });
        return id;
    }

    public int GetOrAdd(Review review)
    {
        var hash = Hashing.Sha256(review.Content);
        var existing = List().FirstOrDefault(r => r.ProjectId == review.ProjectId && r.ContentHash == hash);
        return existing?.Id ?? Save(review);
    }

    public Review? Get(int id) => FileStore.Get<Review>(Directory, "review", id);

    public IReadOnlyList<Review> List() => FileStore.List<Review>(Directory, "review");
}

public class FileAssessmentRepository : IAssessmentRepository
{
    private static readonly string Directory =
        Path.Combine(AppContext.BaseDirectory, "assessments");

    public int Save(Assessment assessment)
    {
        var id = FileStore.NextId(Directory, "assessment");
        // Findings stay nested in the assessment JSON (no child files in the file store).
        FileStore.Save(Directory, "assessment", id, assessment with { Id = id });
        return id;
    }

    public Assessment? Get(int id) => FileStore.Get<Assessment>(Directory, "assessment", id);

    public IReadOnlyList<Assessment> List() => FileStore.List<Assessment>(Directory, "assessment");
}

public class FileEvaluationRepository : IEvaluationRepository
{
    private static readonly string Directory =
        Path.Combine(AppContext.BaseDirectory, "evaluations");

    public int Save(Evaluation evaluation)
    {
        var id = FileStore.NextId(Directory, "evaluation");
        FileStore.Save(Directory, "evaluation", id, evaluation with { Id = id });
        return id;
    }

    public Evaluation? Get(int id) => FileStore.Get<Evaluation>(Directory, "evaluation", id);

    public IReadOnlyList<Evaluation> List() => FileStore.List<Evaluation>(Directory, "evaluation");
}
