using CodeReviewerAgent.Core;

namespace CodeReviewerAgent.Infra
{
    /// <summary>The repositories backing a run — same store, shared context when relational.</summary>
    public record Repositories(
        IProjectRepository Projects,
        IReviewRepository Reviews,
        IAssessmentRepository Assessments,
        IEvaluationRepository Evaluations);

    /// <summary>
    /// Builds the repositories from configuration. <c>STORAGE</c> selects file-vs-EF and, when
    /// EF, the relational provider; the connection string comes from <c>DB_CONNECTION</c>.
    /// Lives in Infra (not Core) because it wires concrete implementations.
    /// </summary>
    public static class RepositoryFactory
    {
        private static readonly IReadOnlyDictionary<string, IDbProviderStrategy> Providers =
            new IDbProviderStrategy[] { new SqliteProviderStrategy(), new PostgresProviderStrategy() }
                .ToDictionary(s => s.Name);

        public static Repositories Create()
        {
            var storage = (Environment.GetEnvironmentVariable("STORAGE") ?? "files").ToLowerInvariant();

            if (storage == "files")
                return new Repositories(
                    new FileProjectRepository(),
                    new FileReviewRepository(),
                    new FileAssessmentRepository(),
                    new FileEvaluationRepository());

            if (!Providers.TryGetValue(storage, out var provider))
                throw new InvalidOperationException(
                    $"Unknown STORAGE '{storage}'. Supported values: 'files', 'sqlite', 'postgres'.");

            var connectionString = provider.ResolveConnectionString(
                Environment.GetEnvironmentVariable("DB_CONNECTION"));

            var context = new CodeReviewDbContext(options => provider.Configure(options, connectionString));
            context.Database.EnsureCreated();

            return new Repositories(
                new EfProjectRepository(context),
                new EfReviewRepository(context),
                new EfAssessmentRepository(context),
                new EfEvaluationRepository(context));
        }
    }
}
