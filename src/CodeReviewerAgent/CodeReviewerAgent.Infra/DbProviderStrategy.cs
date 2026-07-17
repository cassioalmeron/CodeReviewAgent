using Microsoft.EntityFrameworkCore;

namespace CodeReviewerAgent.Infra
{
    /// <summary>
    /// Configures the <see cref="ReviewDbContext"/> for one relational provider. Selecting a
    /// database is a registry lookup by <see cref="Name"/>, not a growing if/else chain —
    /// adding a provider is a new strategy plus one registration line.
    /// </summary>
    public interface IDbProviderStrategy
    {
        string Name { get; }
        void Configure(DbContextOptionsBuilder options, string connectionString);
    }

    public sealed class SqliteProviderStrategy : IDbProviderStrategy
    {
        public string Name => "sqlite";
        public void Configure(DbContextOptionsBuilder options, string connectionString) =>
            options.UseSqlite(connectionString);
    }

    public sealed class PostgresProviderStrategy : IDbProviderStrategy
    {
        public string Name => "postgres";
        public void Configure(DbContextOptionsBuilder options, string connectionString) =>
            options.UseNpgsql(connectionString);
    }
}
