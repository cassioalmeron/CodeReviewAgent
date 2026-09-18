using Microsoft.EntityFrameworkCore.Design;

namespace CodeReviewerAgent.Infra;

/// <summary>
/// Lets <c>dotnet ef</c> build the context, which it cannot do alone because the context takes a
/// configuration action instead of options. Used only at design time, to generate migrations.
/// <para>
/// It builds the Postgres model, because Postgres is the database the migrations are generated
/// against. Generating a migration needs no connection, so a placeholder stands in when
/// <c>DB_CONNECTION</c> is not set; <c>dotnet ef database update</c> does need the real one.
/// </para>
/// </summary>
public sealed class CodeReviewDesignTimeFactory : IDesignTimeDbContextFactory<CodeReviewDbContext>
{
    private const string PlaceholderConnection = "Host=localhost;Database=codereview_design";

    public CodeReviewDbContext CreateDbContext(string[] args)
    {
        var configured = Environment.GetEnvironmentVariable("DB_CONNECTION");
        var connectionString = string.IsNullOrWhiteSpace(configured) ? PlaceholderConnection : configured;
        var provider = new PostgresProviderStrategy();

        return new CodeReviewDbContext(options => provider.Configure(options, connectionString));
    }
}
