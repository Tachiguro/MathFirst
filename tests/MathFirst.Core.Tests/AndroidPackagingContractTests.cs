namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

public sealed class AndroidPackagingContractTests
{
    private static readonly XNamespace AndroidNs = "http://schemas.android.com/apk/res/android";

    [Fact]
    public void Manifest_AdheresToOfflineFirstPolicyAndPermissions()
    {
        var manifestPath = GetRepositoryPath("src", "MathFirst.App", "Platforms", "Android", "AndroidManifest.xml");
        var manifest = XDocument.Load(manifestPath);

        var permissions = manifest.Descendants("uses-permission")
            .Select(element => element.Attribute(AndroidNs + "name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        // Enforce offline-first architecture (ADR-0002): No network permissions declared in release manifest
        Assert.DoesNotContain("android.permission.INTERNET", permissions);
        Assert.DoesNotContain("android.permission.ACCESS_NETWORK_STATE", permissions);
        Assert.Empty(permissions);

        var application = manifest.Descendants("application").Single();
        Assert.Equal("true", application.Attribute(AndroidNs + "allowBackup")?.Value);
        Assert.Equal("true", application.Attribute(AndroidNs + "supportsRtl")?.Value);
        Assert.Equal("@mipmap/appicon", application.Attribute(AndroidNs + "icon")?.Value);
        Assert.Equal("@mipmap/appicon_round", application.Attribute(AndroidNs + "roundIcon")?.Value);
    }

    [Fact]
    public void Project_ConfiguresAndroidPackagingAndIdentityProperties()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));

        Assert.Equal("com.tachiguro.mathfirst", GetProperty(project, "ApplicationId"));
        Assert.Equal("1.0", GetProperty(project, "ApplicationDisplayVersion"));
        Assert.Equal("1", GetProperty(project, "ApplicationVersion"));

        var androidSupportedOs = project.Descendants("SupportedOSPlatformVersion")
            .Single(element => element.Attribute("Condition")!.Value.Contains("android", StringComparison.Ordinal))
            .Value;
        Assert.Equal("24.0", androidSupportedOs);
    }

    [Fact]
    public void GitIgnore_ContainsStrictRulesForKeystoresSecretsAndArtifacts()
    {
        var gitignoreContent = File.ReadAllText(GetRepositoryPath(".gitignore"));
        var lines = gitignoreContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Contains("*.keystore", lines);
        Assert.Contains("*.jks", lines);
        Assert.Contains("*.p12", lines);
        Assert.Contains("*.pfx", lines);
        Assert.Contains("*.publishsettings", lines);
        Assert.Contains("signing.properties", lines);
        Assert.Contains(".env", lines);
        Assert.Contains("artifacts/", lines);
        Assert.Contains("*.aab", lines);
        Assert.Contains("*.apk", lines);
    }

    private static string GetProperty(XDocument project, string name) =>
        project.Descendants(name).Single().Value;

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine(new[] { GetRepositoryRoot() }.Concat(segments).ToArray());

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
