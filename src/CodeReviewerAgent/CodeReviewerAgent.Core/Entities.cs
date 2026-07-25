namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// The root entity: an analyzed repository. <see cref="Folder"/> is the absolute,
    /// normalized path (natural resolution key); <see cref="Name"/> starts as the folder's
    /// last segment and can later be renamed. One project has many <see cref="Review"/>s.
    /// </summary>
    public record Project
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string Folder { get; init; } = "";
        public DateTime CreatedAt { get; init; }
    }

    /// <summary>
    /// A captured diff belonging to a <see cref="Project"/> (via <see cref="ProjectId"/>),
    /// addressable by <see cref="Id"/>. One review can have many <see cref="Assessment"/>
    /// records (re-run with different prompts/models).
    /// </summary>
    public record Review
    {
        public int Id { get; init; }
        public int ProjectId { get; init; }
        public string Content { get; init; } = "";
        // SHA-256 of Content, set by the repository on Save. Indexed together with ProjectId
        // so a diff is content-addressed reused within its own project (GetOrAdd).
        public string ContentHash { get; init; } = "";
        public string? Source { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    /// <summary>
    /// One LLM analysis of a <see cref="Review"/> (referenced by <see cref="ReviewId"/>): the
    /// summary and findings plus the run metadata (engine, model, cost, latency, tokens).
    /// Findings are a child table in the relational stores and stay nested in the file store.
    /// </summary>
    public record Assessment
    {
        public int Id { get; init; }
        public int ReviewId { get; init; }
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

        /// <summary>Builds a persistable assessment of <paramref name="reviewId"/> from a review result.</summary>
        public static Assessment FromReview(int reviewId, ReviewResult r) => new()
        {
            ReviewId = reviewId,
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
    /// The LLM-as-judge's quality scores for one <see cref="Assessment"/> (referenced by
    /// <see cref="AssessmentId"/>): the four rubric criteria (1-5) plus overall and a rationale,
    /// with the judge run's metadata. One assessment can be judged many times (different rubric/model).
    /// </summary>
    public record Evaluation
    {
        public int Id { get; init; }
        public int AssessmentId { get; init; }
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
