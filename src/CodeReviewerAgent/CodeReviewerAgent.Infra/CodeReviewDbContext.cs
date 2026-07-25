using Microsoft.EntityFrameworkCore;
using CodeReviewerAgent.Core;

namespace CodeReviewerAgent.Infra
{
    /// <summary>
    /// The single EF Core context for both relational providers (SQLite / Postgres). The
    /// provider is injected as a configuration action (see <see cref="IDbProviderStrategy"/>),
    /// so there is one context and one model regardless of the backing database. All mapping
    /// lives here via Fluent API — the entities carry no persistence annotations.
    /// </summary>
    public class CodeReviewDbContext : DbContext
    {
        private readonly Action<DbContextOptionsBuilder> _configure;

        public CodeReviewDbContext(Action<DbContextOptionsBuilder> configure) => _configure = configure;

        public DbSet<Project> Projects => Set<Project>();
        public DbSet<Review> Reviews => Set<Review>();
        public DbSet<Assessment> Assessments => Set<Assessment>();
        public DbSet<Evaluation> Evaluations => Set<Evaluation>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => _configure(options);

        protected override void OnModelCreating(ModelBuilder model)
        {
            var project = model.Entity<Project>();
            project.ToTable("Project");
            project.HasKey(p => p.Id);
            project.Property(p => p.Name).IsRequired();
            project.Property(p => p.Folder).IsRequired();
            project.HasIndex(p => p.Folder).IsUnique();

            var review = model.Entity<Review>();
            review.ToTable("Review");
            review.HasKey(r => r.Id);
            review.Property(r => r.Content).IsRequired();
            review.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId);
            // Content-addressed reuse is scoped to the project (GetOrAdd within ProjectId).
            review.HasIndex(r => new { r.ProjectId, r.ContentHash });

            var assessment = model.Entity<Assessment>();
            assessment.ToTable("Assessment");
            assessment.HasKey(a => a.Id);
            assessment.HasOne<Review>().WithMany().HasForeignKey(a => a.ReviewId);
            // Findings are a child table now (was a JSON column): queryable on their own,
            // cascade-deleted with their assessment.
            assessment.HasMany(a => a.Findings)
                .WithOne()
                .HasForeignKey(f => f.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);

            var finding = model.Entity<Finding>();
            finding.ToTable("Finding");
            finding.HasKey(f => f.Id);

            var evaluation = model.Entity<Evaluation>();
            evaluation.ToTable("Evaluation");
            evaluation.HasKey(e => e.Id);
            evaluation.HasOne<Assessment>().WithMany().HasForeignKey(e => e.AssessmentId);
        }
    }
}
