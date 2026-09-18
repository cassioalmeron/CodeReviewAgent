using CodeReviewerAgent.Core;
using Microsoft.EntityFrameworkCore;

namespace CodeReviewerAgent.Infra;

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

    /// <param name="applyMigrations">
    /// False for a caller that builds a store per request: the schema is applied once, at startup, and
    /// migrating again on every request would cost a round trip for nothing.
    /// </param>
    public static RepositoryContext Create(bool applyMigrations = true)
    {
        var storage = (Environment.GetEnvironmentVariable("STORAGE") ?? "files").ToLowerInvariant();

        if (storage == "files")
            return new RepositoryContext(
                new FileProjectRepository(),
                new FileReviewRepository(),
                new FileAssessmentRepository(),
                new FileEvaluationRepository(),
                new FileGoldenRunRepository());

        if (!Providers.TryGetValue(storage, out var provider))
            throw new InvalidOperationException(
                $"Unknown STORAGE '{storage}'. Supported values: 'files', 'sqlite', 'postgres'.");

        var connectionString = provider.ResolveConnectionString(
            Environment.GetEnvironmentVariable("DB_CONNECTION"));

        var context = new CodeReviewDbContext(options => provider.Configure(options, connectionString));
        // Migrate, not EnsureCreated: EnsureCreated does nothing on a database that already exists,
        // so a table added later would never reach it.
        if (applyMigrations)
            context.Database.Migrate();

        return new RepositoryContext(
            new EfProjectRepository(context),
            new EfReviewRepository(context),
            new EfAssessmentRepository(context),
            new EfEvaluationRepository(context),
            new EfGoldenRunRepository(context),
            context);
    }
}
