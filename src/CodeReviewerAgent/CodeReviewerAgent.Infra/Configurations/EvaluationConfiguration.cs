using CodeReviewerAgent.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeReviewerAgent.Infra.Configurations;

internal sealed class EvaluationConfiguration : IEntityTypeConfiguration<Evaluation>
{
    public void Configure(EntityTypeBuilder<Evaluation> builder)
    {
        builder.ToTable("Evaluation");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Cost).HasPrecision(CostPrecision.Precision, CostPrecision.Scale);
        builder.HasOne<Assessment>()
            .WithMany()
            .HasForeignKey(e => e.AssessmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
