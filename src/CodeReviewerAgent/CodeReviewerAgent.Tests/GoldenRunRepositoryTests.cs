using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Infra;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class GoldenRunRepositoryTests
{
    private static readonly DateTime StartedAt = new(2026, 9, 11, 21, 49, 52, DateTimeKind.Utc);

    public static TheoryData<string> Stores => ["ef", "files"];

    [Theory]
    [MemberData(nameof(Stores))]
    public void Find_ReturnsTheSavedRunWithItsChildren_OnlyForTheSameIdentity(string store)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cra-runs-{Guid.NewGuid():N}");
        CodeReviewDbContext? context = null;
        try
        {
            IGoldenRunRepository runs;
            if (store == "ef")
            {
                context = new CodeReviewDbContext(
                    o => new SqliteProviderStrategy().Configure(o, $"Data Source={path}.db"));
                context.Database.Migrate();
                runs = new EfGoldenRunRepository(context);
            }
            else
                runs = new FileGoldenRunRepository(path);

            var id = runs.Save(NewRun());

            var found = runs.Find("deepseek/deepseek-v4-flash-0731", "off", "v3", StartedAt);
            Assert.NotNull(found);
            Assert.Equal(id, found!.Id);
            Assert.Equal(2, found.Cases!.Count);
            Assert.Equal(5, found.Gates!.Count);
            Assert.All(found.Gates, g => Assert.Equal(id, g.RunId));
            Assert.Equal(0.09660000m, found.Cost);

            // Any one of the four identifying fields apart is another run.
            Assert.Null(runs.Find("deepseek/deepseek-v4-flash-0731", "globs", "v3", StartedAt));
            Assert.Null(runs.Find("deepseek/deepseek-v4-flash-0731", "off", "v2", StartedAt));
            Assert.Null(runs.Find("deepseek/deepseek-v4-flash-0731", "off", "v3", StartedAt.AddSeconds(1)));
            Assert.Null(runs.Find("qwen/qwen3.8-flash", "off", "v3", StartedAt));

            Assert.Equal(id, runs.Get(id)!.Id);
            Assert.Single(runs.List());
        }
        finally
        {
            context?.Dispose();
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                File.Delete($"{path}.db");
            }
            catch { /* pooled connection may hold the file; leak the temp file */ }
        }
    }

    private static GoldenRun NewRun() => new()
    {
        Model = "deepseek/deepseek-v4-flash-0731",
        Engine = "openrouter",
        Skills = "off",
        PromptVersion = "v3",
        StartedAt = StartedAt,
        DurationMs = 2_957_000,
        Cost = 0.09660000m,
        Approved = true,
        Cases =
        [
            new GoldenCaseScore { CaseName = "sql-injection", Kind = GoldenKind.Detection, Runs = 5, CleanRounds = 5, Approved = true },
            new GoldenCaseScore { CaseName = "safe-interpolation", Kind = GoldenKind.Trap, Runs = 5, CleanRounds = 4, Approved = true },
        ],
        Gates =
        [
            new GoldenGate { Name = "Detection", Part = 41, Whole = 60, Floor = 60, Direction = GateDirection.Min, Passed = true },
            new GoldenGate { Name = "Trap resistance", Part = 12, Whole = 15, Floor = 50, Direction = GateDirection.Min, Passed = true },
            new GoldenGate { Name = "Precision", Part = 45, Whole = 50, Floor = 85, Direction = GateDirection.Min, Passed = true },
            new GoldenGate { Name = "Exact calibration", Part = 36, Whole = 41, Floor = 75, Direction = GateDirection.Min, Passed = true },
            new GoldenGate { Name = "Trap noise", Part = 9, Whole = 15, Floor = 1, Direction = GateDirection.Max, Passed = true },
        ],
    };
}
