using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CodeReviewerAgent.Core.Infra
{
    /// <summary>
    /// The single EF Core context for both relational providers (SQLite / Postgres). The
    /// provider is injected as a configuration action (see <see cref="IDbProviderStrategy"/>),
    /// so there is one context and one model regardless of the backing database. All mapping
    /// lives here via Fluent API — the entities carry no persistence annotations.
    /// </summary>
    public class ReviewDbContext : DbContext
    {
        private static readonly JsonSerializerOptions FindingsJson = new()
        {
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly Action<DbContextOptionsBuilder> _configure;

        public ReviewDbContext(Action<DbContextOptionsBuilder> configure) => _configure = configure;

        public DbSet<Diff> Diffs => Set<Diff>();
        public DbSet<Analysis> Analyses => Set<Analysis>();
        public DbSet<JudgeEvaluation> JudgeEvaluations => Set<JudgeEvaluation>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => _configure(options);

        protected override void OnModelCreating(ModelBuilder model)
        {
            var diff = model.Entity<Diff>();
            diff.ToTable("Diff");
            diff.HasKey(d => d.Id);
            diff.Property(d => d.Content).IsRequired();
            // Non-unique: Save is append-only; content-addressed reuse (GetOrAdd) comes later.
            diff.HasIndex(d => d.ContentHash);

            var analysis = model.Entity<Analysis>();
            analysis.ToTable("Analysis");
            analysis.HasKey(a => a.Id);
            analysis.HasOne<Diff>().WithMany().HasForeignKey(a => a.DiffId);

            // The findings list is stored as a JSON string column — one relational shape
            // that works across providers, avoiding a child table for a read-mostly blob.
            var comparer = new ValueComparer<List<Finding>?>(
                (a, b) => Serialize(a) == Serialize(b),
                v => Serialize(v).GetHashCode(),
                v => Deserialize(Serialize(v)));

            analysis.Property(a => a.Findings)
                .HasConversion(v => Serialize(v), v => Deserialize(v))
                .Metadata.SetValueComparer(comparer);

            var evaluation = model.Entity<JudgeEvaluation>();
            evaluation.ToTable("JudgeEvaluation");
            evaluation.HasKey(e => e.Id);
            evaluation.HasOne<Analysis>().WithMany().HasForeignKey(e => e.AnalysisId);
        }

        private static string Serialize(List<Finding>? findings) =>
            JsonSerializer.Serialize(findings ?? [], FindingsJson);

        private static List<Finding>? Deserialize(string json) =>
            JsonSerializer.Deserialize<List<Finding>>(json, FindingsJson) ?? [];
    }
}
