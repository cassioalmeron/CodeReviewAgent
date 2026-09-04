using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core.Golden;

/// <summary>
/// One paid round of the golden set, durable the moment it comes back: which case, which prompt
/// version, which repetition, under which configuration, and the review itself.
/// <para>
/// <see cref="Model"/> and <see cref="Skills"/> are what make a record reusable or not. A round
/// produced by another model answers a different question, and silently counting it as this
/// run's work is the failure mode that already cost a US$ 0.32 judge run measured against the
/// wrong rubric: wrong output that looks exactly like clean output. Null model means the
/// configuration could not be established, and matches nothing.
/// </para>
/// </summary>
public record GoldenRoundRecord(
    string Case,
    string PromptVersion,
    int RunIndex,
    string? Model,
    string? Skills,
    ReviewResult Review);

/// <summary>
/// Durable record of the golden set's paid rounds. <see cref="GoldenEvaluator.Run"/> takes this
/// as an optional dependency: with no store it writes nothing at all, which is the invariant
/// <c>Run_WritesNothingToDisk</c> pins down, and the composition root supplies the file-backed
/// one for real runs.
/// </summary>
public interface IGoldenRoundStore
{
    /// <summary>The review already paid for this exact round, or null if there is none.</summary>
    ReviewResult? Find(string caseName, string promptVersion, int runIndex);

    /// <summary>Makes one round durable. Called as each review comes back, not at the end.</summary>
    void Record(string caseName, string promptVersion, int runIndex, ReviewResult review);
}

/// <summary>
/// The file-backed store, in JSON Lines, mirroring <c>JudgeResultsStore</c> on the judge side.
/// <para>
/// One record per line, not a JSON array: appending is a single write with nothing to
/// reserialize, and a file a crash truncated mid-write is still readable line by line, so
/// everything before the broken line survives. This is what the golden set was missing when a
/// quota error on review 55 of 60 would have thrown away the 54 already bought.
/// </para>
/// <para>
/// Writes are serialized on a lock because the paid phase of the run is a <c>Parallel.For</c>:
/// concurrent appends to the same file interleave and corrupt lines.
/// </para>
/// </summary>
public sealed class FileGoldenRoundStore : IGoldenRoundStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly string? _model;
    private readonly string? _skills;
    private readonly Dictionary<(string, string, int), ReviewResult> _completed;
    private readonly Lock _writeLock = new();

    public static string DefaultPath => Path.Combine(OutputPaths.Reviews, "golden-rounds.jsonl");

    /// <param name="model">
    /// The model this run will use. Null disables resume entirely: a run that cannot say which
    /// model it is about to call has no business claiming a stored round was produced by it.
    /// </param>
    /// <param name="skills">The <c>SKILLS</c> setting, which is the run's other condition.</param>
    public FileGoldenRoundStore(string path, string? model, string? skills)
    {
        _path = path;
        _model = model;
        _skills = skills;
        _completed = model is null ? [] : LoadMatching(path, model, skills);
    }

    /// <summary>How many rounds a previous run already paid for and this one can skip.</summary>
    public int ResumableCount => _completed.Count;

    public ReviewResult? Find(string caseName, string promptVersion, int runIndex) =>
        _completed.GetValueOrDefault((caseName, promptVersion, runIndex));

    public void Record(string caseName, string promptVersion, int runIndex, ReviewResult review)
    {
        var record = new GoldenRoundRecord(caseName, promptVersion, runIndex, _model, _skills, review);
        var line = JsonSerializer.Serialize(record, Options);

        lock (_writeLock)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // \n, not Environment.NewLine: JSON Lines is line-oriented interchange, and the
            // separator should not depend on which OS produced the file.
            File.AppendAllText(_path, line + "\n");
        }
    }

    /// <summary>
    /// Every record in the file, skipping any line a crash left truncated — resuming must never
    /// fail because of the very run being resumed. An absent file reads as empty.
    /// </summary>
    public static List<GoldenRoundRecord> Load(string path)
    {
        if (!File.Exists(path))
            return [];

        var records = new List<GoldenRoundRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var record = JsonSerializer.Deserialize<GoldenRoundRecord>(line, Options);
                if (record is not null)
                    records.Add(record);
            }
            catch (JsonException)
            {
                // Everything before this line is still good; only the one a crash caught
                // mid-write is lost, and it never counted as recorded to begin with.
            }
        }
        return records;
    }

    /// <summary>
    /// The rounds in the file that this configuration may reuse, keyed by round. Later lines win,
    /// so a re-run of the same round under the same configuration reads as the newer answer.
    /// </summary>
    public static Dictionary<(string, string, int), ReviewResult> LoadMatching(
        string path, string model, string? skills)
    {
        var matching = new Dictionary<(string, string, int), ReviewResult>();
        foreach (var record in Load(path))
            if (record.Model == model && record.Skills == skills)
                matching[(record.Case, record.PromptVersion, record.RunIndex)] = record.Review;
        return matching;
    }
}
