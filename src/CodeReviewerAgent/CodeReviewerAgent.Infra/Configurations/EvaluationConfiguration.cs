using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Judge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodeReviewerAgent.Infra.Configurations;

internal sealed class EvaluationConfiguration : IEntityTypeConfiguration<Evaluation>
{
    // Stored as lowercase text ("single" / "sectioned"), the way GoldenRun.Pattern is, so the column
    // reads the same in SQL on both sides and the rows the migration backfills already match.
    private static readonly ValueConverter<JudgePattern, string> LowercaseName = new(
        pattern => pattern.ToString().ToLowerInvariant(),
        text => Enum.Parse<JudgePattern>(text, true));

    public void Configure(EntityTypeBuilder<Evaluation> builder)
    {
        builder.ToTable("Evaluation");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Pattern).IsRequired().HasConversion(LowercaseName).HasDefaultValue(JudgePattern.Single);
        builder.Property(e => e.Cost).HasPrecision(CostPrecision.Precision, CostPrecision.Scale);
        builder.HasOne<Assessment>()
            .WithMany()
            .HasForeignKey(e => e.AssessmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
