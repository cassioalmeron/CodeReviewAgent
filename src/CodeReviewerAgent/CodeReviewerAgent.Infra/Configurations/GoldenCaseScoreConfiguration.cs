using CodeReviewerAgent.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeReviewerAgent.Infra.Configurations;

internal sealed class GoldenCaseScoreConfiguration : IEntityTypeConfiguration<GoldenCaseScore>
{
    public void Configure(EntityTypeBuilder<GoldenCaseScore> builder)
    {
        // The table keeps the name the scorer uses for the same thing; the class cannot, because the
        // in-memory GoldenCaseResult record already has it.
        builder.ToTable("GoldenCaseResult");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CaseName).IsRequired();
        builder.HasIndex(c => new { c.RunId, c.CaseName }).IsUnique();
    }
}
