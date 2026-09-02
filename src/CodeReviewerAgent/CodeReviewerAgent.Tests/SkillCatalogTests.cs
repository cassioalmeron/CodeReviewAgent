using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Skill;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// Discovery and activation over a throwaway skills root, so the edge cases (missing
/// description, mismatched name, bundled resources) don't need real skills in the repo.
/// </summary>
public class SkillCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"skill-catalog-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string WriteSkill(string folder, string content)
    {
        var directory = Path.Combine(_root, folder);
        Directory.CreateDirectory(directory);
        var location = Path.Combine(directory, "SKILL.md");
        File.WriteAllText(location, content);
        return location;
    }

    [Fact]
    public void Discover_ReadsNameDescriptionAndMetadata()
    {
        WriteSkill("csharp", """
            ---
            name: csharp
            description: C# conventions. Use when the diff changes .cs files.
            metadata:
              applies-to: "*.cs"
            ---
            # C# conventions

            - Use string interpolation.
            """);

        var (skills, diagnostics) = SkillCatalog.Discover(_root);

        var skill = Assert.Single(skills);
        Assert.Equal("csharp", skill.Name);
        Assert.Equal("C# conventions. Use when the diff changes .cs files.", skill.Description);
        Assert.Equal("*.cs", skill.Metadata["applies-to"]);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Discover_WhenDescriptionHasUnquotedColon_KeepsTheWholeValue()
    {
        WriteSkill("pdf", """
            ---
            name: pdf
            description: Use this skill when: the user asks about PDFs
            ---
            # PDF
            """);

        var (skills, _) = SkillCatalog.Discover(_root);

        Assert.Equal("Use this skill when: the user asks about PDFs", Assert.Single(skills).Description);
    }

    [Fact]
    public void Discover_ReadsBlockScalarDescription()
    {
        WriteSkill("folded", """
            ---
            name: folded
            description: >
              A description spread
              over two lines.
            ---
            # Folded
            """);

        var (skills, _) = SkillCatalog.Discover(_root);

        Assert.Equal("A description spread over two lines.", Assert.Single(skills).Description);
    }

    [Fact]
    public void Discover_WhenDescriptionIsMissing_SkipsWithAnError()
    {
        WriteSkill("nodesc", """
            ---
            name: nodesc
            ---
            # No description
            """);

        var (skills, diagnostics) = SkillCatalog.Discover(_root);

        Assert.Empty(skills);
        Assert.Equal(SkillDiagnosticLevel.Error, Assert.Single(diagnostics).Level);
    }

    [Fact]
    public void Discover_WhenFrontmatterIsMissing_SkipsWithAnError()
    {
        WriteSkill("plain", "# Just markdown, no frontmatter");

        var (skills, diagnostics) = SkillCatalog.Discover(_root);

        Assert.Empty(skills);
        Assert.Equal(SkillDiagnosticLevel.Error, Assert.Single(diagnostics).Level);
    }

    [Fact]
    public void Discover_WhenNameDiffersFromTheDirectory_WarnsButLoads()
    {
        WriteSkill("csharp", """
            ---
            name: c-sharp
            description: C# conventions.
            ---
            # C#
            """);

        var (skills, diagnostics) = SkillCatalog.Discover(_root);

        Assert.Equal("c-sharp", Assert.Single(skills).Name);
        Assert.Equal(SkillDiagnosticLevel.Warning, Assert.Single(diagnostics).Level);
    }

    [Fact]
    public void Discover_WhenNameIsMissing_FallsBackToTheDirectoryName()
    {
        WriteSkill("react", """
            ---
            description: React conventions.
            ---
            # React
            """);

        var (skills, diagnostics) = SkillCatalog.Discover(_root);

        Assert.Equal("react", Assert.Single(skills).Name);
        Assert.Equal(SkillDiagnosticLevel.Warning, Assert.Single(diagnostics).Level);
    }

    [Fact]
    public void Discover_WhenNameExceeds64Characters_WarnsButLoads()
    {
        var name = new string('a', 65);
        WriteSkill(name, $"""
            ---
            name: {name}
            description: A long-named skill.
            ---
            # Long
            """);

        var (skills, diagnostics) = SkillCatalog.Discover(_root);

        Assert.Single(skills);
        Assert.Contains(diagnostics, d => d.Level == SkillDiagnosticLevel.Warning && d.Message.Contains("65"));
    }

    [Fact]
    public void Discover_WhenTwoSkillsShareAName_KeepsTheFirstAndWarns()
    {
        WriteSkill("a-first", """
            ---
            name: shared
            description: First.
            ---
            # First
            """);
        WriteSkill("b-second", """
            ---
            name: shared
            description: Second.
            ---
            # Second
            """);

        var (skills, diagnostics) = SkillCatalog.Discover(_root);

        Assert.Equal("First.", Assert.Single(skills).Description);
        Assert.Contains(diagnostics, d => d.Message.Contains("shadowed"));
    }

    [Fact]
    public void Discover_IgnoresDirectoriesWithoutASkillFile()
    {
        Directory.CreateDirectory(Path.Combine(_root, "not-a-skill"));
        File.WriteAllText(Path.Combine(_root, "not-a-skill", "README.md"), "# Nope");

        var (skills, diagnostics) = SkillCatalog.Discover(_root);

        Assert.Empty(skills);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Discover_WhenTheRootDoesNotExist_ReturnsAnEmptyCatalog()
    {
        var (skills, diagnostics) = SkillCatalog.Discover(Path.Combine(_root, "missing"));

        Assert.Empty(skills);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MatchByGlobs_SelectsTheSkillsWhoseAppliesToMatches()
    {
        WriteSkill("csharp", """
            ---
            name: csharp
            description: C# conventions.
            metadata:
              applies-to: "*.cs"
            ---
            # C#
            """);
        WriteSkill("react", """
            ---
            name: react
            description: React conventions.
            metadata:
              applies-to: "*.tsx,*.ts"
            ---
            # React
            """);
        var (skills, _) = SkillCatalog.Discover(_root);

        Assert.Equal(["csharp"], SkillCatalog.MatchByGlobs(skills, ["src/App.cs"]).Select(s => s.Name));
        Assert.Equal(["react"], SkillCatalog.MatchByGlobs(skills, ["web/src/App.tsx"]).Select(s => s.Name));
        Assert.Empty(SkillCatalog.MatchByGlobs(skills, ["main.py", "config.yaml"]));
        Assert.Empty(SkillCatalog.MatchByGlobs(skills, []));
    }

    [Fact]
    public void MatchByGlobs_IgnoresSkillsWithoutAnAppliesTo()
    {
        WriteSkill("csharp", """
            ---
            name: csharp
            description: C# conventions, with no applies-to.
            ---
            # C#
            """);
        var (skills, _) = SkillCatalog.Discover(_root);

        Assert.Empty(SkillCatalog.MatchByGlobs(skills, ["src/App.cs"]));
    }

    [Fact]
    public void Activate_StripsTheFrontmatterAndListsBundledResources()
    {
        WriteSkill("csharp", """
            ---
            name: csharp
            description: C# conventions.
            ---
            # C# conventions

            - Use string interpolation.
            """);
        Directory.CreateDirectory(Path.Combine(_root, "csharp", "references"));
        File.WriteAllText(Path.Combine(_root, "csharp", "references", "naming.md"), "# Naming");

        var (skills, _) = SkillCatalog.Discover(_root);
        var activated = SkillCatalog.Activate(skills[0]);

        Assert.StartsWith("# C# conventions", activated.Body);
        Assert.DoesNotContain("description:", activated.Body);
        Assert.Equal(["references/naming.md"], activated.Resources);
        Assert.False(activated.Truncated);

        var rendered = activated.Render();
        Assert.Contains("<skill_content name=\"csharp\">", rendered);
        Assert.Contains("<file>references/naming.md</file>", rendered);
        // The resource is listed, never read.
        Assert.DoesNotContain("# Naming", rendered);
    }

    [Fact]
    public void Activate_WhenThereAreNoResources_OmitsTheResourceSection()
    {
        WriteSkill("react", """
            ---
            name: react
            description: React conventions.
            ---
            # React conventions
            """);

        var (skills, _) = SkillCatalog.Discover(_root);
        var rendered = SkillCatalog.Activate(skills[0]).Render();

        Assert.DoesNotContain("<skill_resources>", rendered);
        Assert.DoesNotContain("Skill directory:", rendered);
    }

    [Fact]
    public void Activate_CapsTheResourceListing()
    {
        WriteSkill("big", """
            ---
            name: big
            description: A skill with many files.
            ---
            # Big
            """);
        for (var i = 0; i < SkillCatalog.MaxResources + 5; i++)
            File.WriteAllText(Path.Combine(_root, "big", $"file-{i:D2}.txt"), "x");

        var (skills, _) = SkillCatalog.Discover(_root);
        var activated = SkillCatalog.Activate(skills[0]);

        Assert.Equal(SkillCatalog.MaxResources, activated.Resources.Count);
        Assert.True(activated.Truncated);
        Assert.Contains("capped at", activated.Render());
    }
}
