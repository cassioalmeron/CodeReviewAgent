using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace CodeReviewerAgent.Infra.Migrations.ProviderTypes;

public sealed class SqliteMigrationProviderTypes : IMigrationProviderTypes
{
    public string Bool => "INTEGER";
    public string DateTime => "TEXT";

    public OperationBuilder<AddColumnOperation> AsAutoIncrementPrimaryKey(OperationBuilder<AddColumnOperation> column) =>
        column.Annotation("Sqlite:Autoincrement", true);
}
