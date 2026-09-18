using CodeReviewerAgent.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeReviewerAgent.Infra.Configurations;

internal sealed class AssessmentConfiguration : IEntityTypeConfiguration<Assessment>
{
    public void Configure(EntityTypeBuilder<Assessment> builder)
    {
        builder.ToTable("Assessment");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Cost).HasPrecision(CostPrecision.Precision, CostPrecision.Scale);
        builder.HasOne<Review>()
            .WithMany()
            .HasForeignKey(a => a.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);
        // Findings are a child table (was a JSON column): queryable on their own,
        // cascade-deleted with their assessment.
        builder.HasMany(a => a.Findings)
            .WithOne()
            .HasForeignKey(f => f.AssessmentId)
            .OnDelete(DeleteBehavior.Cascade);
        // Optional, because an ordinary review belongs to no run. Cascading, because a run is deleted
        // when it should not have counted, and its assessments would otherwise stay in every chart.
        builder.HasOne<GoldenRun>()
            .WithMany()
            .HasForeignKey(a => a.RunId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
