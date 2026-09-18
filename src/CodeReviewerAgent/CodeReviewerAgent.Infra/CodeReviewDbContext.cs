using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using CodeReviewerAgent.Core;

namespace CodeReviewerAgent.Infra;

/// <summary>
/// The single EF Core context for both relational providers (SQLite / Postgres). The
/// provider is injected as a configuration action (see <see cref="IDbProviderStrategy"/>),
/// so there is one context and one model regardless of the backing database. The mapping lives in
/// one <c>IEntityTypeConfiguration</c> per entity under <c>Configurations/</c>; the entities carry no
/// persistence annotations. The schema comes from the migrations, not from <c>EnsureCreated</c>.
/// </summary>
public class CodeReviewDbContext : DbContext
{
    // Postgres stores DateTime as timestamp with time zone and refuses a value whose Kind is not UTC.
    // A value read back comes out Unspecified, so without this it would break the next save.
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        value => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private readonly Action<DbContextOptionsBuilder> _configure;

    public CodeReviewDbContext(Action<DbContextOptionsBuilder> configure) => _configure = configure;

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<GoldenRun> GoldenRuns => Set<GoldenRun>();
    public DbSet<GoldenCaseScore> GoldenCaseScores => Set<GoldenCaseScore>();
    public DbSet<GoldenGate> GoldenGates => Set<GoldenGate>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => _configure(options);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ApplyConfigurationsFromAssembly(typeof(CodeReviewDbContext).Assembly);

        var dateTimes = model.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.ClrType == typeof(DateTime));

        foreach (var property in dateTimes)
            property.SetValueConverter(UtcConverter);
    }
}
