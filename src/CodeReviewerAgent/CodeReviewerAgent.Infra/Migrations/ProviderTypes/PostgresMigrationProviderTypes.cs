using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace CodeReviewerAgent.Infra.Migrations.ProviderTypes;

public sealed class PostgresMigrationProviderTypes : IMigrationProviderTypes
{
    public string Bool => "boolean";
    public string DateTime => "timestamp with time zone";

    public OperationBuilder<AddColumnOperation> AsAutoIncrementPrimaryKey(OperationBuilder<AddColumnOperation> column) =>
        column.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
}
