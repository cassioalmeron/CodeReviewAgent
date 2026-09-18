using CodeReviewerAgent.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class MigrationTests
{
    // No server is needed: comparing the model with the migrations' snapshot never opens a connection.
    private const string UnusedConnection = "Host=localhost;Database=unused";

    [Fact]
    public void PostgresModel_HasNoPendingChanges()
    {
        var provider = new PostgresProviderStrategy();
        using var context = new CodeReviewDbContext(options => provider.Configure(options, UnusedConnection));

        // SQLite ignores this check (see SqliteProviderStrategy.Configure), so this is where a model
        // change without a migration gets caught.
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Schema_HasExactlyOneMigration()
    {
        var provider = new PostgresProviderStrategy();
        using var context = new CodeReviewDbContext(options => provider.Configure(options, UnusedConnection));

        var migration = Assert.Single(context.Database.GetMigrations());
        Assert.EndsWith("_InitialSchema", migration);
    }
}
