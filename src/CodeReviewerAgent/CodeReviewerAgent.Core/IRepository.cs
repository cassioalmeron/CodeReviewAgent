namespace CodeReviewerAgent.Core;

/// <summary>
/// Persists and retrieves <see cref="Project"/>s. A project is resolved from its folder
/// (the analyzed repository); <see cref="GetOrAdd"/> creates it on first sight and reuses
/// it afterwards. The Core knows only this contract; implementations live in <c>Infra</c>.
/// </summary>
public interface IProjectRepository
{
    /// <summary>Reuses the project with this <paramref name="folder"/>, else creates one named <paramref name="name"/>.</summary>
    Project GetOrAdd(string folder, string name);
    Project? Get(int id);
    IReadOnlyList<Project> List();
    void Rename(int id, string name);
}

/// <summary>
/// Persists and retrieves captured <see cref="Review"/>s by id.
/// </summary>
public interface IReviewRepository
{
    int Save(Review review);
    /// <summary>Reuses an existing review of the same project with the same content (by hash), else inserts a new one.</summary>
    int GetOrAdd(Review review);
    Review? Get(int id);
    IReadOnlyList<Review> List();
}

/// <summary>
/// Persists and retrieves <see cref="Assessment"/> records by id (findings included).
/// </summary>
public interface IAssessmentRepository
{
    int Save(Assessment assessment);
    Assessment? Get(int id);
    IReadOnlyList<Assessment> List();
}

/// <summary>
/// Persists and retrieves <see cref="Evaluation"/> records by id.
/// </summary>
public interface IEvaluationRepository
{
    int Save(Evaluation evaluation);
    Evaluation? Get(int id);
    IReadOnlyList<Evaluation> List();
}

/// <summary>
/// Persists and retrieves <see cref="GoldenRun"/>s, always together with their case scores and gates.
/// </summary>
public interface IGoldenRunRepository
{
    /// <summary>
    /// The stored run with this identity, or null. Separate from <see cref="Save"/> rather than a
    /// GetOrAdd, because an import has to know whether the run was already there: if it was, its
    /// assessments are too, and writing them again would count the run twice.
    /// </summary>
    GoldenRun? Find(string model, string? skills, string? promptVersion, DateTime startedAt);
    /// <summary>Stores the run with its cases and gates, and returns the run's id.</summary>
    int Save(GoldenRun run);
    GoldenRun? Get(int id);
    IReadOnlyList<GoldenRun> List();
}

/// <summary>
/// The repositories backing a run, passed around as one unit — same store, and a shared
/// <c>DbContext</c> when relational. Lives in Core because it is only the contracts;
/// <c>RepositoryFactory</c> (Infra) is what fills it with implementations.
/// </summary>
/// <param name="Owned">
/// What the store holds open, disposed with the context: the <c>DbContext</c> of a relational store,
/// null for the file store. It is what lets a caller with a lifetime of its own — a web request —
/// close the connection when it ends instead of leaving one per request to the garbage collector.
/// </param>
public record RepositoryContext(
    IProjectRepository Projects,
    IReviewRepository Reviews,
    IAssessmentRepository Assessments,
    IEvaluationRepository Evaluations,
    IGoldenRunRepository GoldenRuns,
    IDisposable? Owned = null) : IDisposable
{
    public void Dispose() => Owned?.Dispose();
}
