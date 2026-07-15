namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// A captured diff, addressable by <see cref="Id"/>. One diff can have many
    /// <see cref="Analysis"/> records (re-run with different prompts/models).
    /// </summary>
    public record Diff
    {
        public int Id { get; init; }
        public string Content { get; init; } = "";
        // SHA-256 of Content, set by the repository on Save. Indexed so diffs can later be
        // looked up by content (content-addressable reuse for the golden set).
        public string ContentHash { get; init; } = "";
        public string? Source { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    /// <summary>
    /// One LLM analysis of a <see cref="Diff"/> (referenced by <see cref="DiffId"/>): the
    /// summary and findings plus the run metadata (engine, model, cost, latency, tokens).
    /// Replaces the old JSONL run log — the metadata now lives on the persisted entity.
    /// </summary>
    public record Analysis
    {
        public int Id { get; init; }
        public int DiffId { get; init; }
        public string? Summary { get; init; }
        public List<Finding>? Findings { get; init; }
        public string? Engine { get; init; }
        public string? Model { get; init; }
        public string? PromptVersion { get; init; }
        public decimal Cost { get; init; }
        public long LatencyMs { get; init; }
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
        public DateTime CreatedAt { get; init; }

        /// <summary>Builds a persistable analysis of <paramref name="diffId"/> from a review result.</summary>
        public static Analysis FromReview(int diffId, ReviewResult r) => new()
        {
            DiffId = diffId,
            Summary = r.Summary,
            Findings = r.Findings,
            Engine = r.Engine,
            Model = r.Model,
            PromptVersion = r.PromptVersion,
            Cost = r.Cost,
            LatencyMs = r.LatencyMs,
            InputTokens = r.InputTokens,
            OutputTokens = r.OutputTokens,
            CreatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// The LLM-as-judge's quality scores for one <see cref="Analysis"/> (referenced by
    /// <see cref="AnalysisId"/>): the four rubric criteria (1-5) plus overall and a rationale,
    /// with the judge run's metadata. One analysis can be judged many times (different rubric/model).
    /// </summary>
    public record JudgeEvaluation
    {
        public int Id { get; init; }
        public int AnalysisId { get; init; }
        public string? RubricVersion { get; init; }
        public string? JudgeModel { get; init; }
        public int Correctness { get; init; }
        public int Actionability { get; init; }
        public int Calibration { get; init; }
        public int SignalToNoise { get; init; }
        public int Overall { get; init; }
        public string? Rationale { get; init; }
        public decimal Cost { get; init; }
        public long LatencyMs { get; init; }
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
