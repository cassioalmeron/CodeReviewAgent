using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace CodeReviewerAgent.Infra.Migrations.ProviderTypes;

/// <summary>
/// Every provider-specific value a migration needs. The migrations run on Postgres in use and on
/// SQLite in the tests, and a migration asks this instead of comparing <c>ActiveProvider</c> itself.
/// </summary>
public interface IMigrationProviderTypes
{
    string Bool { get; }
    string DateTime { get; }
    OperationBuilder<AddColumnOperation> AsAutoIncrementPrimaryKey(OperationBuilder<AddColumnOperation> column);
}
