using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CodeReviewerAgent.Infra;

/// <summary>
/// Configures the <see cref="CodeReviewDbContext"/> for one relational provider. Selecting a
/// database is a registry lookup by <see cref="Name"/>, not a growing if/else chain —
/// adding a provider is a new strategy plus one registration line.
/// </summary>
public interface IDbProviderStrategy
{
    string Name { get; }
    /// <summary>Resolves the connection string from the configured <c>DB_CONNECTION</c> (may be blank).</summary>
    string ResolveConnectionString(string? configured);
    void Configure(DbContextOptionsBuilder options, string connectionString);
}

public sealed class SqliteProviderStrategy : IDbProviderStrategy
{
    public string Name => "sqlite";

    // DB_CONNECTION when set. When blank, the database lives under the user's local application data
    // (%LOCALAPPDATA%/CodeReviewerAgent/review.db): a stable, per-user location that survives
    // rebuilds and is shared by the Console and the Api, and the one every setup opened before
    // DB_CONNECTION applied to SQLite. The folder is created on first run.
    public string ResolveConnectionString(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodeReviewerAgent");
        Directory.CreateDirectory(directory);
        return $"Data Source={Path.Combine(directory, "review.db")}";
    }

    // The migrations are generated against Postgres, so their snapshot carries Postgres column types.
    // Compared with the SQLite model, that reads as a pending model change on every migrate, which
    // is a false positive. The real check runs against the Postgres model, in
    // MigrationTests.PostgresModel_HasNoPendingChanges.
    public void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlite(connectionString)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
}

public sealed class PostgresProviderStrategy : IDbProviderStrategy
{
    public string Name => "postgres";

    public string ResolveConnectionString(string? configured) =>
        !string.IsNullOrWhiteSpace(configured)
            ? configured
            : throw new InvalidOperationException("DB_CONNECTION is not configured. Add it to the .env file.");

    public void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString);
}
