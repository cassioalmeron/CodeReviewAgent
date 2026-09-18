using Microsoft.EntityFrameworkCore;
using CodeReviewerAgent.Core;

namespace CodeReviewerAgent.Infra;

/// <summary>
/// EF Core-backed repositories. They share one <see cref="CodeReviewDbContext"/>; the actual
/// database (SQLite / Postgres) is decided by the provider strategy that configured it,
/// so this code is written once and runs on either backend.
/// </summary>
public class EfProjectRepository(CodeReviewDbContext context) : IProjectRepository
{
    public Project GetOrAdd(string folder, string name)
    {
        // Folder is the natural key. Match case-insensitively: on Windows the same repo can be
        // resolved as "C:\..." or "c:\...", and those must not create two projects.
        var lowered = folder.ToLower();
        var existing = context.Projects.FirstOrDefault(p => p.Folder.ToLower() == lowered);
        if (existing is not null)
            return existing;

        var entity = new Project { Name = name, Folder = folder, CreatedAt = DateTime.UtcNow };
        context.Projects.Add(entity);
        context.SaveChanges();
        return entity;
    }

    public Project? Get(int id) => context.Projects.Find(id);

    public IReadOnlyList<Project> List() => [.. context.Projects.OrderBy(p => p.Id)];

    public void Rename(int id, string name)
    {
        var project = context.Projects.Find(id)
            ?? throw new InvalidOperationException($"No project with id {id}.");
        context.Entry(project).CurrentValues["Name"] = name;
        context.SaveChanges();
    }
}

public class EfReviewRepository(CodeReviewDbContext context) : IReviewRepository
{
    public int Save(Review review)
    {
        var entity = review with { Id = 0, ContentHash = Hashing.Sha256(review.Content) };
        context.Reviews.Add(entity);
        context.SaveChanges();
        return entity.Id;
    }

    public int GetOrAdd(Review review)
    {
        var hash = Hashing.Sha256(review.Content);
        var existing = context.Reviews
            .FirstOrDefault(r => r.ProjectId == review.ProjectId && r.ContentHash == hash);
        return existing?.Id ?? Save(review);
    }

    public Review? Get(int id) => context.Reviews.Find(id);

    public IReadOnlyList<Review> List() => [.. context.Reviews.OrderBy(r => r.Id)];
}

public class EfAssessmentRepository(CodeReviewDbContext context) : IAssessmentRepository
{
    public int Save(Assessment assessment)
    {
        var entity = assessment with { Id = 0 };
        context.Assessments.Add(entity);
        context.SaveChanges();
        return entity.Id;
    }

    public Assessment? Get(int id) =>
        context.Assessments.Include(a => a.Findings).FirstOrDefault(a => a.Id == id);

    public IReadOnlyList<Assessment> List() =>
        [.. context.Assessments.Include(a => a.Findings).OrderBy(a => a.Id)];
}

public class EfEvaluationRepository(CodeReviewDbContext context) : IEvaluationRepository
{
    public int Save(Evaluation evaluation)
    {
        var entity = evaluation with { Id = 0 };
        context.Evaluations.Add(entity);
        context.SaveChanges();
        return entity.Id;
    }

    public Evaluation? Get(int id) => context.Evaluations.Find(id);

    public IReadOnlyList<Evaluation> List() => [.. context.Evaluations.OrderBy(e => e.Id)];
}

public class EfGoldenRunRepository(CodeReviewDbContext context) : IGoldenRunRepository
{
    public GoldenRun? Find(string model, string? skills, string? promptVersion, DateTime startedAt) =>
        WithChildren().FirstOrDefault(r =>
            r.Model == model && r.Skills == skills && r.PromptVersion == promptVersion && r.StartedAt == startedAt);

    public int Save(GoldenRun run)
    {
        // Ids reset so a run read from elsewhere (an import file, another store) is inserted, not updated.
        var entity = run with
        {
            Id = 0,
            Cases = run.Cases?.Select(c => c with { Id = 0, RunId = 0 }).ToList(),
            Gates = run.Gates?.Select(g => g with { Id = 0, RunId = 0 }).ToList(),
        };
        context.GoldenRuns.Add(entity);
        context.SaveChanges();
        return entity.Id;
    }

    public GoldenRun? Get(int id) => WithChildren().FirstOrDefault(r => r.Id == id);

    public IReadOnlyList<GoldenRun> List() => [.. WithChildren().OrderBy(r => r.Id)];

    private IQueryable<GoldenRun> WithChildren() =>
        context.GoldenRuns.Include(r => r.Cases).Include(r => r.Gates).AsSplitQuery();
}
