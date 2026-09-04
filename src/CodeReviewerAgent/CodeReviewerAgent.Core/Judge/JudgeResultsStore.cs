using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core.Judge;

/// <summary>
/// One judged execution, durable enough to rebuild the pairwise report without the API: which
/// case (identified by its diff, the same key <see cref="JudgeRunner"/> already groups on) and
/// pair it belongs to, which run within that pair, the configuration that produced it, and the
/// outcome (verdicts, reasoning, pairing, cost, latency, tokens).
/// <para>
/// The model and rubric are nullable because lines written before those fields existed have
/// neither. A record that cannot say which configuration produced it matches no configuration,
/// which is the safe reading: it is reported as foreign and re-judged, never silently counted
/// as the current run's work.
/// </para>
/// </summary>
public record JudgeExecutionRecord(
    string Diff,
    string Label,
    int PairIndex,
    int RunIndex,
    string? JudgeModel,
    string? RubricVersion,
    PairJudgeOutcome Outcome);

/// <summary>
/// The pairwise judge's own two-stage persistence — the same pattern <c>GoldenEvaluator</c>
/// already documents one level up (paid calls → raw JSON → report, so the report can be rebuilt
/// for free and the executor never has to be re-invoked), completed here so the judge itself
/// stops losing paid work to a crash mid-run.
/// <para>
/// JSON Lines (<c>reviews/judge-results.jsonl</c>), one record per line, not a JSON array.
/// Appending is one write at the end of the file with nothing already there to reserialize, and
/// a file a crash truncated mid-write is still readable line by line — everything before the
/// broken line survived. A JSON array would need the whole file rewritten on every single
/// execution and would be entirely invalid the moment the process died mid-write.
/// </para>
/// </summary>
public static class JudgeResultsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath =>
        Path.Combine(OutputPaths.Reviews, "judge-results.jsonl");

    /// <summary>
    /// Appends one record and returns as soon as the write completes — durable before the next
    /// (paid) judge call is made, not batched until the run finishes.
    /// </summary>
    public static void Append(string path, JudgeExecutionRecord record)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // \n, not Environment.NewLine: JSON Lines is a line-oriented interchange format, and the
        // separator should not depend on which OS produced the file.
        File.AppendAllText(path, JsonSerializer.Serialize(record, Options) + "\n");
    }

    /// <summary>
    /// Reads every record from the file, skipping a line a crash left truncated or otherwise
    /// unreadable — resuming must never fail because the very run being resumed died mid-write.
    /// An absent file reads as empty, the same as a run that has not judged anything yet.
    /// </summary>
    public static List<JudgeExecutionRecord> Load(string path)
    {
        if (!File.Exists(path))
            return [];

        var records = new List<JudgeExecutionRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var record = JsonSerializer.Deserialize<JudgeExecutionRecord>(line, Options);
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
    /// The records produced by one judge configuration. A judgment made with a different model or
    /// a different rubric answers a different question, so it is never interchangeable with this
    /// run's: it is filtered out here rather than deleted, so the file keeps the full history.
    /// </summary>
    public static List<JudgeExecutionRecord> ForConfiguration(
        IReadOnlyList<JudgeExecutionRecord> records, string judgeModel, string rubricVersion) =>
        [.. records.Where(r => r.JudgeModel == judgeModel && r.RubricVersion == rubricVersion)];

    /// <summary>
    /// The (diff, pair, run) triples already on disk <em>for this configuration</em>, so a resumed
    /// run skips exactly what a previous run already paid for instead of re-judging everything
    /// from scratch.
    /// <para>
    /// Configuration is part of the resume key on purpose. Keying on the triple alone made a run
    /// that changed rubric or model treat the previous rubric's judgments as already paid for,
    /// and the aggregate silently mixed the two — a wrong answer that looks exactly like a clean
    /// one, which is worse than paying to judge the pair again.
    /// </para>
    /// </summary>
    public static HashSet<(string Diff, int PairIndex, int RunIndex)> CompletedKeys(
        IReadOnlyList<JudgeExecutionRecord> records, string judgeModel, string rubricVersion) =>
        [.. ForConfiguration(records, judgeModel, rubricVersion)
            .Select(r => (r.Diff, r.PairIndex, r.RunIndex))];
}
