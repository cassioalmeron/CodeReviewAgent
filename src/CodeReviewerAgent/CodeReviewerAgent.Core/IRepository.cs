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
