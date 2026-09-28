using System.Reflection;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Infra;
using NetArchTest.Rules;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The architecture fitness function (US-018). The physical boundary between Core and Infra was
/// drawn by ADR-009; these are what stop it from being quietly redrawn. They run inside the
/// ordinary test suite, so the CI already enforces them on every push and pull request, and a
/// regression fails a build instead of waiting to be caught in review.
/// <para>
/// Each rule names what it forbids rather than describing a layer in the abstract, because a rule
/// nobody can read is a rule that gets deleted the first time it goes red for a good reason.
/// </para>
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly CoreAssembly = typeof(CodeReviewer).Assembly;
    private static readonly Assembly InfraAssembly = typeof(EnvFile).Assembly;

    /// <summary>
    /// Core does not know infrastructure. The project reference only goes one way, so the compiler
    /// already refuses this; the rule is here because the cheapest way to "solve" a future problem
    /// is to add that reference, and then nothing else would object.
    /// </summary>
    [Fact]
    public void Core_DoesNotDependOnInfrastructure() =>
        AssertNoDependency(CoreAssembly, "CodeReviewerAgent.Infra", "CodeReviewerAgent.Api");

    /// <summary>
    /// The provider does not leak into the domain: no ORM, no database driver, no vendor SDK and
    /// no HTTP client anywhere in Core. Persistence reaches it as <c>IRepository</c> and a model
    /// as <c>ILlmClient</c>, which is what lets the whole review flow be tested with a fake and no
    /// database at all.
    /// </summary>
    [Fact]
    public void Core_DoesNotDependOnAnyProvider() =>
        AssertNoDependency(
            CoreAssembly,
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Microsoft.Data.Sqlite",
            "ClaudeAgentSdk",
            "System.Net.Http",
            "DotNetEnv");

    /// <summary>
    /// Core writes nothing to a terminal. It formats and it returns; whoever owns a terminal
    /// decides what reaches it. Progress that only makes sense mid-flow travels as
    /// <c>IProgress&lt;string&gt;</c>, which a caller without a terminal simply leaves null.
    /// </summary>
    [Fact]
    public void Core_DoesNotWriteToTheConsole() =>
        AssertNoDependency(CoreAssembly, "System.Console");

    /// <summary>
    /// The one rule pointing the other way, and the reason the boundary is worth having: Infra
    /// builds on Core, so a type that belongs to the domain must not end up defined over here
    /// where only the database and the vendor SDKs can see it.
    /// </summary>
    [Fact]
    public void Infrastructure_DependsOnCore() =>
        Assert.True(
            Types.InAssembly(InfraAssembly).That().HaveDependencyOn("CodeReviewerAgent.Core")
                .GetTypes().Any(),
            "Infra is expected to build on Core; nothing here referenced it.");

    /// <summary>
    /// Core does not read the process environment. Every setting arrives as an argument
    /// (<see cref="ReviewSettings"/>, <see cref="Core.Golden.GoldenSettings"/>, and the plain
    /// parameters beside them), which is what makes a run's configuration visible at its call
    /// site and lets two callers in one process run under different ones.
    /// <para>
    /// This one reads the sources rather than the compiled assembly, and that is the point. The
    /// IL rule cannot tell my code apart from the compiler's: a method that returns
    /// <c>IEnumerable</c> through <c>yield</c> gets a generated state machine that compares
    /// <c>Environment.CurrentManagedThreadId</c>, so banning the type in IL failed on
    /// <c>RoundBuffer</c>, which reads nothing. Excluding generated types would have opened a real
    /// hole, since a <c>yield</c> method's own body lives inside one of them. Naming the call
    /// instead is exact.
    /// </para>
    /// </summary>
    [Fact]
    public void Core_DoesNotReadTheProcessEnvironment()
    {
        var offenders = CoreSourceFiles()
            .Where(ReadsAnEnvironmentVariable)
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Core must take its settings as arguments, but these read a variable directly: " +
            string.Join(", ", offenders));
    }

    // Comment lines are skipped: the records that replaced those reads document which variable
    // each field used to be, and a rule that went red over its own explanation would be deleted.
    private static bool ReadsAnEnvironmentVariable(string file) =>
        File.ReadLines(file)
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith("//") && !line.StartsWith('*'))
            .Any(line => line.Contains("GetEnvironmentVariable"));

    private static IEnumerable<string> CoreSourceFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeReviewerAgent.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var core = Path.Combine(directory.FullName, "CodeReviewerAgent.Core");
        Assert.True(Directory.Exists(core), $"Core sources were not found at {core}.");

        return Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{assembly.GetName().Name} must not depend on [{string.Join(", ", forbidden)}], but these do: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}
