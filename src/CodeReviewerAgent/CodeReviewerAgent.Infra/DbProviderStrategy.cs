using Microsoft.EntityFrameworkCore;

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

    // The SQLite database always lives under the user's local application data
    // (%LOCALAPPDATA%/CodeReviewerAgent/review.db) — a stable, per-user location that survives
    // rebuilds and is shared by the Console and the Api. DB_CONNECTION does not apply to SQLite
    // (it configures Postgres only); the folder is created on first run.
    public string ResolveConnectionString(string? configured)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodeReviewerAgent");
        Directory.CreateDirectory(directory);
        return $"Data Source={Path.Combine(directory, "review.db")}";
    }

    public void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlite(connectionString);
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
