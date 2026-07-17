using CodeReviewerAgent.Core;

namespace CodeReviewerAgent.Infra
{
    /// <summary>
    /// EF Core-backed repositories. Both share one <see cref="ReviewDbContext"/>; the actual
    /// database (SQLite / Postgres) is decided by the provider strategy that configured it,
    /// so this code is written once and runs on either backend.
    /// </summary>
    public class EfDiffRepository(ReviewDbContext context) : IDiffRepository
    {
        public int Save(Diff diff)
        {
            var entity = diff with { Id = 0, ContentHash = Hashing.Sha256(diff.Content) };
            context.Diffs.Add(entity);
            context.SaveChanges();
            return entity.Id;
        }

        public int GetOrAdd(Diff diff)
        {
            var hash = Hashing.Sha256(diff.Content);
            var existing = context.Diffs.FirstOrDefault(d => d.ContentHash == hash);
            return existing?.Id ?? Save(diff);
        }

        public Diff? Get(int id) => context.Diffs.Find(id);

        public IReadOnlyList<Diff> List() => [.. context.Diffs.OrderBy(d => d.Id)];
    }

    public class EfAnalysisRepository(ReviewDbContext context) : IAnalysisRepository
    {
        public int Save(Analysis analysis)
        {
            var entity = analysis with { Id = 0 };
            context.Analyses.Add(entity);
            context.SaveChanges();
            return entity.Id;
        }

        public Analysis? Get(int id) => context.Analyses.Find(id);

        public IReadOnlyList<Analysis> List() => [.. context.Analyses.OrderBy(a => a.Id)];
    }

    public class EfJudgeEvaluationRepository(ReviewDbContext context) : IJudgeEvaluationRepository
    {
        public int Save(JudgeEvaluation evaluation)
        {
            var entity = evaluation with { Id = 0 };
            context.JudgeEvaluations.Add(entity);
            context.SaveChanges();
            return entity.Id;
        }

        public JudgeEvaluation? Get(int id) => context.JudgeEvaluations.Find(id);

        public IReadOnlyList<JudgeEvaluation> List() => [.. context.JudgeEvaluations.OrderBy(e => e.Id)];
    }
}
