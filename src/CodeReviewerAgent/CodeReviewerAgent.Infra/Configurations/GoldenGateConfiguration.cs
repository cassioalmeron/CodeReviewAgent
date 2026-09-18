using CodeReviewerAgent.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeReviewerAgent.Infra.Configurations;

internal sealed class GoldenGateConfiguration : IEntityTypeConfiguration<GoldenGate>
{
    public void Configure(EntityTypeBuilder<GoldenGate> builder)
    {
        builder.ToTable("GoldenGate");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).IsRequired();
        builder.HasIndex(g => new { g.RunId, g.Name }).IsUnique();
    }
}
