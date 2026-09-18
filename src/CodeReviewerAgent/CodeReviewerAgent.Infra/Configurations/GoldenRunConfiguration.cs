using CodeReviewerAgent.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeReviewerAgent.Infra.Configurations;

internal sealed class GoldenRunConfiguration : IEntityTypeConfiguration<GoldenRun>
{
    public void Configure(EntityTypeBuilder<GoldenRun> builder)
    {
        builder.ToTable("GoldenRun");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Model).IsRequired();
        builder.Property(r => r.Cost).HasPrecision(CostPrecision.Precision, CostPrecision.Scale);
        builder.HasMany(r => r.Cases)
            .WithOne()
            .HasForeignKey(c => c.RunId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Gates)
            .WithOne()
            .HasForeignKey(g => g.RunId)
            .OnDelete(DeleteBehavior.Cascade);
        // What identifies a run, and what a repeated import looks it up by.
        builder.HasIndex(r => new { r.Model, r.Skills, r.PromptVersion, r.StartedAt });
    }
}
