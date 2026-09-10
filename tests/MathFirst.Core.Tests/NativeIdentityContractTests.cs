namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

public sealed class NativeIdentityContractTests
{
    [Fact]
    public void Project_DeclaresCanonicalIdentityAndReleaseMetadata()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));

        Assert.Equal("MathFirst", GetProperty(project, "ApplicationTitle"));
        Assert.Equal("com.tachiguro.mathfirst", GetProperty(project, "ApplicationId"));
        Assert.Equal("1.0", GetProperty(project, "ApplicationDisplayVersion"));
        Assert.Equal("1", GetProperty(project, "ApplicationVersion"));
        Assert.Equal("None", GetProperty(project, "WindowsPackageType"));
        Assert.Equal("24.0", project.Descendants("SupportedOSPlatformVersion").Single(element => element.Attribute("Condition")!.Value.Contains("android", StringComparison.Ordinal)).Value);

        Assert.Equal("Tachiguro", GetProperty(project, "Company"));
        Assert.Equal("$(ApplicationTitle)", GetProperty(project, "Product"));
        Assert.Equal("$(ApplicationTitle)", GetProperty(project, "AssemblyTitle"));
        Assert.NotEqual("MathFirst", GetProperty(project, "Product"));
        Assert.NotEqual("MathFirst", GetProperty(project, "AssemblyTitle"));
        Assert.False(string.IsNullOrWhiteSpace(GetProperty(project, "Description")));
    }

    [Fact]
    public void Project_ProjectsCanonicalApplicationPropertiesIntoAssemblyMetadata()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));
        var metadata = project.Descendants("AssemblyMetadata")
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Value")!.Value,
                StringComparer.Ordinal);

        Assert.Equal("$(ApplicationTitle)", metadata["MathFirst.ApplicationTitle"]);
        Assert.Equal("$(ApplicationDisplayVersion)", metadata["MathFirst.ApplicationDisplayVersion"]);
        Assert.Equal("$(ApplicationVersion)", metadata["MathFirst.ApplicationVersion"]);
    }

    [Fact]
    public void AppBuildInfo_UsesGeneratedAssemblyMetadataAndDelegatesValidationToTheSharedParser()
    {
        var source = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Services", "AppBuildInfo.cs"));

        Assert.Contains("AssemblyMetadataAttribute", source, StringComparison.Ordinal);
        Assert.Contains("typeof(AppBuildInfo).Assembly", source, StringComparison.Ordinal);
        Assert.Contains("AppBuildInfoMetadataParser.Parse(metadata)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= \"MathFirst\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= \"1.0\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= \"1\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeWindowAndSettings_UseTheSharedBuildInfoService()
    {
        var app = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "App.xaml.cs"));
        var composition = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "MauiProgram.cs"));
        var settings = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor"));
        var styles = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));

        Assert.Contains("AppBuildInfo buildInfo", app, StringComparison.Ordinal);
        Assert.Contains("Title = _buildInfo.ApplicationTitle", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Title = \"MathFirst\"", app, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<AppBuildInfo>()", composition, StringComparison.Ordinal);
        Assert.Contains("@inject AppBuildInfo BuildInfo", settings, StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"Settings_VersionBuild\", BuildInfo.DisplayVersion, BuildInfo.BuildNumber]", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Version 1.0 (Build 1)", settings, StringComparison.Ordinal);
        Assert.Contains(".settings-version", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionFooterLocalization_HasMatchingPlaceholderArity()
    {
        var source = File.ReadAllText(GetRepositoryPath("src", "MathFirst.Application", "LocalizationService.cs"));
        var expected = new Dictionary<string, string>
        {
            ["EnglishStrings"] = "Version {0} (Build {1})",
            ["GermanStrings"] = "Version {0} (Build {1})",
            ["RussianStrings"] = "Версия {0} (сборка {1})"
        };

        foreach (var (dictionary, value) in expected)
        {
            var dictionaryStart = source.IndexOf($"{dictionary} =", StringComparison.Ordinal);
            Assert.True(dictionaryStart >= 0, $"Could not locate {dictionary}.");
            var dictionaryBody = source[dictionaryStart..];
            var match = Regex.Match(dictionaryBody, "\\[\\\"Settings_VersionBuild\\\"\\]\\s*=\\s*\\\"(?<value>[^\\\"]+)\\\"");

            Assert.True(match.Success, $"{dictionary} does not define Settings_VersionBuild.");
            Assert.Equal(value, match.Groups["value"].Value);
            Assert.Single(Regex.Matches(match.Groups["value"].Value, "\\{0\\}"));
            Assert.Single(Regex.Matches(match.Groups["value"].Value, "\\{1\\}"));
        }
    }

    private static string GetProperty(XDocument project, string name) =>
        project.Descendants(name).Single().Value;

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
