using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Judge;
using CodeReviewerAgent.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class EvaluationPatternTests
{
    private static string ReadPatterns(CodeReviewDbContext context)
    {
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """SELECT "Pattern" FROM "Evaluation" ORDER BY "Id" """;
        using var reader = command.ExecuteReader();
        var patterns = new List<string>();
        while (reader.Read())
            patterns.Add(reader.GetString(0));
        return string.Join(",", patterns);
    }

    private static void WithMigratedDatabase(Action<CodeReviewDbContext> test)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-test-{Guid.NewGuid():N}.db");
        try
        {
            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.OpenConnection();
            context.Database.ExecuteSql($"PRAGMA foreign_keys = OFF");

            context.GetService<IMigrator>().Migrate("20261030220846_AddGoldenRunPattern");
            context.Database.ExecuteSql($"""
                INSERT INTO "Evaluation" ("AssessmentId", "Correctness", "Actionability", "Calibration",
                    "SignalToNoise", "Overall", "Cost", "LatencyMs", "InputTokens", "OutputTokens", "CreatedAt")
                VALUES (1, 4, 4, 4, 4, 4, 0, 0, 0, 0, '2026-01-01 00:00:00')
                """);
            context.Database.Migrate();

            test(context);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    [Fact]
    public void EvaluationPatternTests_Rows_that_predate_the_migration_read_single()
    {
        WithMigratedDatabase(context => Assert.Equal("single", ReadPatterns(context)));
    }

    [Fact]
    public void EvaluationPatternTests_A_sectioned_evaluation_is_stored_as_the_text_sectioned_and_read_back()
    {
        WithMigratedDatabase(context =>
        {
            var repository = new EfEvaluationRepository(context);

            repository.Save(new Evaluation { AssessmentId = 1, Pattern = JudgePattern.Sectioned, CreatedAt = DateTime.UtcNow });

            Assert.Equal("single,sectioned", ReadPatterns(context));
            Assert.Equal(JudgePattern.Sectioned, repository.List()[1].Pattern);
        });
    }
}
