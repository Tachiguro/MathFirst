namespace MathFirst.Core.Tests;

using System.Xml.Linq;
using Xunit;

/// <summary>
/// Verifies the SQLite adapter architecture mandated by MF-AND-001:
/// - MathFirst.Application has NO Microsoft.Data.Sqlite dependency
/// - SqliteLearnerStore lives in MathFirst.Infrastructure.Sqlite (not Application)
/// - Persistence contracts/interfaces remain in MathFirst.Application
/// Existing schema, migration, and persistence conformance suites continue to
/// provide the behavioral coverage for the extracted adapter.
/// </summary>
public sealed class SqliteAdapterArchitectureTests
{
    // ============================================================
    // Project layout / dependency assertions
    // ============================================================

    [Fact]
    public void Architecture_ApplicationProjectHasNoSqliteDependency()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.Application", "MathFirst.Application.csproj"));

        var packageRefs = project.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(
            packageRefs,
            r => r.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            packageRefs,
            r => r.Contains("Microsoft.Data", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Architecture_InfrastructureSqliteProjectOwnsSqlitePackage()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.Infrastructure.Sqlite", "MathFirst.Infrastructure.Sqlite.csproj"));

        var hasSqlite = project.Descendants("PackageReference")
            .Any(e => (e.Attribute("Include")?.Value ?? string.Empty)
                       .Equals("Microsoft.Data.Sqlite", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasSqlite, "MathFirst.Infrastructure.Sqlite must own the Microsoft.Data.Sqlite package reference.");
    }

    [Fact]
    public void Architecture_InfrastructureSqliteReferencesApplicationProject()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.Infrastructure.Sqlite", "MathFirst.Infrastructure.Sqlite.csproj"));

        var refs = project.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(
            refs,
            r => r.Contains("MathFirst.Application", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Architecture_ApplicationProjectHasNoSqliteLearnerStoreSource()
    {
        foreach (var file in EnumerateProjectSourceFiles("MathFirst.Application"))
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("class SqliteLearnerStore", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.Data.Sqlite", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Architecture_SqliteLearnerStoreExistsInInfrastructureProject()
    {
        var infrastructureDir = GetRepositoryPath("src", "MathFirst.Infrastructure.Sqlite");
        Assert.True(Directory.Exists(infrastructureDir),
            "MathFirst.Infrastructure.Sqlite directory must exist.");

        var csFiles = EnumerateProjectSourceFiles("MathFirst.Infrastructure.Sqlite");
        var hasSqliteLearnerStore = csFiles.Any(f =>
        {
            var content = File.ReadAllText(f);
            return content.Contains("class SqliteLearnerStore", StringComparison.Ordinal);
        });

        Assert.True(hasSqliteLearnerStore,
            "SqliteLearnerStore must be defined in MathFirst.Infrastructure.Sqlite.");
    }

    [Fact]
    public void Architecture_PersistenceInterfaceRemainsInApplication()
    {
        var csFiles = EnumerateProjectSourceFiles("MathFirst.Application");
        var hasILearnerStore = csFiles.Any(f =>
        {
            var content = File.ReadAllText(f);
            return content.Contains("interface ILearnerStore", StringComparison.Ordinal);
        });

        Assert.True(hasILearnerStore,
            "ILearnerStore interface must remain in MathFirst.Application.");
    }

    [Fact]
    public void Architecture_AppProjectReferencesBothApplicationAndInfrastructure()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));

        var refs = project.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(refs, r => r.Contains("MathFirst.Application", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(refs, r => r.Contains("MathFirst.Infrastructure.Sqlite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Architecture_AppProjectHasNoDirectSqlitePackageReference()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));

        var packageRefs = project.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(
            packageRefs,
            r => r.Equals("Microsoft.Data.Sqlite", StringComparison.OrdinalIgnoreCase));
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static string[] EnumerateProjectSourceFiles(string projectName)
    {
        var projectDir = GetRepositoryPath("src", projectName);
        return Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetRelativePath(projectDir, path);
                return !relative.StartsWith($"bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                       !relative.StartsWith($"obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
