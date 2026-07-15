namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Persists and retrieves captured <see cref="Diff"/>s by id. The Core knows only this
    /// contract; concrete implementations (file / EF Core) live in <c>Infra</c>.
    /// </summary>
    public interface IDiffRepository
    {
        int Save(Diff diff);
        /// <summary>Reuses an existing diff with the same content (by hash), else inserts a new one.</summary>
        int GetOrAdd(Diff diff);
        Diff? Get(int id);
        IReadOnlyList<Diff> List();
    }

    /// <summary>
    /// Persists and retrieves <see cref="Analysis"/> records by id.
    /// </summary>
    public interface IAnalysisRepository
    {
        int Save(Analysis analysis);
        Analysis? Get(int id);
        IReadOnlyList<Analysis> List();
    }

    /// <summary>
    /// Persists and retrieves <see cref="JudgeEvaluation"/> records by id.
    /// </summary>
    public interface IJudgeEvaluationRepository
    {
        int Save(JudgeEvaluation evaluation);
        JudgeEvaluation? Get(int id);
        IReadOnlyList<JudgeEvaluation> List();
    }
}
