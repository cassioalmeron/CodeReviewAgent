using Microsoft.EntityFrameworkCore;

namespace CodeReviewerAgent.Core.Infra
{
    /// <summary>The repositories backing a run — same store, shared context when relational.</summary>
    public record Repositories(
        IDiffRepository Diffs,
        IAnalysisRepository Analyses,
        IJudgeEvaluationRepository JudgeEvaluations);

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
                    new FileDiffRepository(),
                    new FileAnalysisRepository(),
                    new FileJudgeEvaluationRepository());

            if (!Providers.TryGetValue(storage, out var provider))
                throw new InvalidOperationException(
                    $"Unknown STORAGE '{storage}'. Supported values: 'files', 'sqlite', 'postgres'.");

            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION")
                ?? throw new InvalidOperationException("DB_CONNECTION is not configured. Add it to the .env file.");

            var context = new ReviewDbContext(options => provider.Configure(options, connectionString));
            context.Database.EnsureCreated();

            return new Repositories(
                new EfDiffRepository(context),
                new EfAnalysisRepository(context),
                new EfJudgeEvaluationRepository(context));
        }
    }
}
