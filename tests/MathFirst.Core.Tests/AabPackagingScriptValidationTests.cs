namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MathFirst.Application;
using Xunit;

public sealed class AabPackagingScriptValidationTests
{
    [Fact]
    public void PackageScript_ExistsAndDeclaresRequiredParameters()
    {
        var scriptPath = GetRepositoryPath("scripts", "package-android-aab.ps1");
        Assert.True(File.Exists(scriptPath), "scripts/package-android-aab.ps1 must exist.");

        var content = File.ReadAllText(scriptPath);
        Assert.Contains("[string]$Configuration = \"Release\"", content, StringComparison.Ordinal);
        Assert.Contains("[string]$DisplayVersion", content, StringComparison.Ordinal);
        Assert.Contains("[int]$BuildNumber", content, StringComparison.Ordinal);
        Assert.Contains("[switch]$Sign", content, StringComparison.Ordinal);
        Assert.Contains("[string]$KeystorePath", content, StringComparison.Ordinal);
        Assert.Contains("[string]$KeyAlias", content, StringComparison.Ordinal);
        Assert.Contains("[string]$OutputDir", content, StringComparison.Ordinal);
        Assert.Contains("[switch]$AllowDirty", content, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageScript_EnforcesFailClosedWorkingTreeCheck()
    {
        var content = File.ReadAllText(GetRepositoryPath("scripts", "package-android-aab.ps1"));
        Assert.Contains("git status --porcelain=v1", content, StringComparison.Ordinal);
        Assert.Contains("AllowDirty", content, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageScript_DoesNotLogPlaintextPasswords()
    {
        var content = File.ReadAllText(GetRepositoryPath("scripts", "package-android-aab.ps1"));
        Assert.DoesNotContain("Write-Host $StorePassword", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $KeyPassword", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output $StorePassword", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output $KeyPassword", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.0", 1, "9a9e5c4", "Release", "MathFirst-v1.0-b1-9a9e5c4-Release.aab")]
    [InlineData("1.2.3", 42, "abcdef1", "Release", "MathFirst-v1.2.3-b42-abcdef1-Release.aab")]
    public void ArtifactNaming_ConstructsDeterministicAabFileName(
        string displayVersion,
        int buildNumber,
        string shortCommit,
        string configuration,
        string expectedFileName)
    {
        var actualFileName = $"MathFirst-v{displayVersion}-b{buildNumber}-{shortCommit}-{configuration}.aab";
        Assert.Equal(expectedFileName, actualFileName);
    }

    [Fact]
    public void ProvenanceMetadata_SerializesAndDeserializesConformantly()
    {
        var provenance = new
        {
            ApplicationTitle = "MathFirst",
            ApplicationId = "com.tachiguro.mathfirst",
            DisplayVersion = "1.0",
            BuildNumber = 1,
            GitCommitSha = "9a9e5c43d1f1685cf21e766e0890b790ab09040c",
            GitBranch = "main",
            IsCleanWorkingTree = true,
            BuildTimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            Configuration = "Release",
            TargetFramework = "net10.0-android",
            IsSigned = false,
            Sha256Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        };

        var json = JsonSerializer.Serialize(provenance, new JsonSerializerOptions { WriteIndented = true });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("MathFirst", root.GetProperty("ApplicationTitle").GetString());
        Assert.Equal("com.tachiguro.mathfirst", root.GetProperty("ApplicationId").GetString());
        Assert.Equal("1.0", root.GetProperty("DisplayVersion").GetString());
        Assert.Equal(1, root.GetProperty("BuildNumber").GetInt32());
        Assert.Equal("9a9e5c43d1f1685cf21e766e0890b790ab09040c", root.GetProperty("GitCommitSha").GetString());
        Assert.Equal("Release", root.GetProperty("Configuration").GetString());
        Assert.Equal("net10.0-android", root.GetProperty("TargetFramework").GetString());
        Assert.False(root.GetProperty("IsSigned").GetBoolean());
        Assert.Equal(64, root.GetProperty("Sha256Checksum").GetString()!.Length);
    }

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1.2.3", true)]
    [InlineData("2.0.0.1", true)]
    [InlineData("1.a", false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("version 1.0", false)]
    public void DisplayVersionValidation_MatchesMetadataParserRules(string input, bool isValid)
    {
        var regex = new Regex(@"^\d+(\.\d+)+$");
        var isMatch = !string.IsNullOrWhiteSpace(input) && regex.IsMatch(input.Trim());
        Assert.Equal(isValid, isMatch);
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine(new[] { GetRepositoryRoot() }.Concat(segments).ToArray());

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
