namespace MathFirst.Core.Tests;

using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MathFirst.Application;
using MathFirst.Application.Telemetry;
using Xunit;

public sealed class AndroidFileProviderContractTests
{
    private static readonly XNamespace AndroidNs = "http://schemas.android.com/apk/res/android";

    [Fact]
    public void FileProviderResource_01_HasCanonicalEssentialsResourceFileName()
    {
        var resourcePath = GetRepositoryPath(
            "src", "MathFirst.App", "Platforms", "Android", "Resources", "xml", "microsoft_maui_essentials_fileprovider_file_paths.xml");

        Assert.True(File.Exists(resourcePath), "Canonical FileProvider XML resource must exist at the exact specified path.");
        Assert.Equal("microsoft_maui_essentials_fileprovider_file_paths.xml", Path.GetFileName(resourcePath));
    }

    [Fact]
    public void FileProviderResource_02_ContainsExactTelemetryShareCachePathElement()
    {
        var xml = LoadFileProviderXml();
        Assert.Equal("paths", xml.Root?.Name.LocalName);

        var cachePaths = xml.Root!.Elements("cache-path").ToList();
        Assert.Single(cachePaths);

        var telemetryShareElement = cachePaths.Single();
        Assert.Equal("telemetry_share", telemetryShareElement.Attribute("name")?.Value);
        Assert.Equal("telemetry-share", telemetryShareElement.Attribute("path")?.Value);
    }

    [Fact]
    public void FileProviderResource_03_TelemetrySharePathHasNoTrailingSlash()
    {
        var xml = LoadFileProviderXml();
        var pathAttribute = xml.Root!.Elements("cache-path").Single().Attribute("path")?.Value;

        Assert.NotNull(pathAttribute);
        Assert.False(pathAttribute.EndsWith('/'), "telemetry-share path must not have a trailing forward slash.");
        Assert.False(pathAttribute.EndsWith('\\'), "telemetry-share path must not have a trailing backslash.");
        Assert.Equal("telemetry-share", pathAttribute);
    }

    [Fact]
    public void FileProviderResource_04_DoesNotExposeRootCacheDirectory()
    {
        var xml = LoadFileProviderXml();
        var cachePaths = xml.Root!.Elements("cache-path").ToList();

        Assert.All(cachePaths, element =>
        {
            var path = element.Attribute("path")?.Value;
            Assert.False(string.IsNullOrWhiteSpace(path), "Cache path must not be empty or whitespace.");
            Assert.NotEqual(".", path);
            Assert.NotEqual("./", path);
            Assert.NotEqual("/", path);
        });
    }

    [Fact]
    public void FileProviderResource_05_DoesNotContainFilesPathElement()
    {
        var xml = LoadFileProviderXml();
        Assert.Empty(xml.Descendants("files-path"));
    }

    [Fact]
    public void FileProviderResource_06_DoesNotContainExternalPathElement()
    {
        var xml = LoadFileProviderXml();
        Assert.Empty(xml.Descendants("external-path"));
        Assert.Empty(xml.Descendants("external-files-path"));
        Assert.Empty(xml.Descendants("external-cache-path"));
        Assert.Empty(xml.Descendants("external-media-path"));
        Assert.Empty(xml.Descendants("root-path"));
    }

    [Fact]
    public void FileProviderResource_07_DoesNotContainBroaderCachePathElements()
    {
        var xml = LoadFileProviderXml();
        var allChildElements = xml.Root!.Elements().ToList();

        Assert.Single(allChildElements);
        Assert.Equal("cache-path", allChildElements[0].Name.LocalName);
        Assert.Equal("telemetry-share", allChildElements[0].Attribute("path")?.Value);
    }

    [Fact]
    public void FileProviderResource_08_DirectoryDoesNotContainGenericFilePathsResource()
    {
        var xmlDir = GetRepositoryPath("src", "MathFirst.App", "Platforms", "Android", "Resources", "xml");
        var xmlV28Dir = GetRepositoryPath("src", "MathFirst.App", "Platforms", "Android", "Resources", "xml-v28");

        var forbiddenNames = new[] { "file_paths.xml", "telemetry_file_paths.xml", "provider_paths.xml", "paths.xml" };

        if (Directory.Exists(xmlDir))
        {
            var files = Directory.GetFiles(xmlDir).Select(Path.GetFileName);
            foreach (var forbidden in forbiddenNames)
            {
                Assert.DoesNotContain(forbidden, files, StringComparer.OrdinalIgnoreCase);
            }
        }

        if (Directory.Exists(xmlV28Dir))
        {
            var files = Directory.GetFiles(xmlV28Dir).Select(Path.GetFileName);
            foreach (var forbidden in forbiddenNames)
            {
                Assert.DoesNotContain(forbidden, files, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void AndroidPlatform_09_DoesNotDefineCustomOrSecondaryFileProvider()
    {
        var androidDir = GetRepositoryPath("src", "MathFirst.App", "Platforms", "Android");
        var csharpFiles = Directory.GetFiles(androidDir, "*.cs", SearchOption.AllDirectories);

        foreach (var csFile in csharpFiles)
        {
            var content = File.ReadAllText(csFile);
            Assert.False(
                Regex.IsMatch(content, @"class\s+\w+\s*:\s*FileProvider\b"),
                $"File {Path.GetFileName(csFile)} must not define a custom FileProvider subclass.");
            Assert.False(
                Regex.IsMatch(content, @"class\s+\w+\s*:\s*androidx\.core\.content\.FileProvider\b"),
                $"File {Path.GetFileName(csFile)} must not define an androidx FileProvider subclass.");
        }
    }

    [Fact]
    public void AndroidManifest_10_ContainsNoFileProviderRegistration()
    {
        var manifestPath = GetRepositoryPath("src", "MathFirst.App", "Platforms", "Android", "AndroidManifest.xml");
        var manifest = XDocument.Load(manifestPath);

        var providers = manifest.Descendants("provider").ToList();
        Assert.Empty(providers);
    }

    [Fact]
    public void AndroidManifest_11_DeclaresNoNetworkPermissions()
    {
        var manifestPath = GetRepositoryPath("src", "MathFirst.App", "Platforms", "Android", "AndroidManifest.xml");
        var manifest = XDocument.Load(manifestPath);

        var permissions = manifest.Descendants("uses-permission")
            .Select(element => element.Attribute(AndroidNs + "name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        Assert.DoesNotContain("android.permission.INTERNET", permissions);
        Assert.DoesNotContain("android.permission.ACCESS_NETWORK_STATE", permissions);
        Assert.DoesNotContain("android.permission.CHANGE_NETWORK_STATE", permissions);
        Assert.Equal(["android.permission.VIBRATE"], permissions);
    }

    [Fact]
    public void AppBuildInfo_12_SourceDeclarationImplementsIAppBuildInfo()
    {
        var sourcePath = GetRepositoryPath("src", "MathFirst.App", "Services", "AppBuildInfo.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("using MathFirst.Application.Telemetry;", source, StringComparison.Ordinal);
        Assert.True(
            Regex.IsMatch(source, @"public\s+sealed\s+class\s+AppBuildInfo\s*:\s*(\w+,\s*)*IAppBuildInfo"),
            "AppBuildInfo must explicitly implement IAppBuildInfo in its class declaration.");
    }

    [Fact]
    public void AppBuildInfo_13_ExposesDisplayVersionBackedByMetadata()
    {
        var sourcePath = GetRepositoryPath("src", "MathFirst.App", "Services", "AppBuildInfo.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.True(
            Regex.IsMatch(source, @"public\s+string\s+DisplayVersion\s*\{\s*get;\s*\}"),
            "AppBuildInfo must declare 'public string DisplayVersion { get; }'.");
        Assert.Contains("DisplayVersion = parsedMetadata.DisplayVersion;", source, StringComparison.Ordinal);

        var defaultParsed = AppBuildInfoMetadataParser.Parse(new Dictionary<string, string?>
        {
            ["MathFirst.ApplicationTitle"] = "MathFirst",
            ["MathFirst.ApplicationId"] = "com.tachiguro.mathfirst",
            ["MathFirst.ApplicationDisplayVersion"] = "1.0",
            ["MathFirst.ApplicationVersion"] = "1",
            ["MathFirst.SourceCommit"] = "local",
            ["MathFirst.BuildClassification"] = "Local"
        });

        Assert.False(string.IsNullOrWhiteSpace(defaultParsed.DisplayVersion));
        Assert.Equal("1.0", defaultParsed.DisplayVersion);
    }

    [Fact]
    public void AppBuildInfo_14_ExposesBuildClassificationBackedByMetadata()
    {
        var sourcePath = GetRepositoryPath("src", "MathFirst.App", "Services", "AppBuildInfo.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.True(
            Regex.IsMatch(source, @"public\s+string\s+BuildClassification\s*\{\s*get;\s*\}"),
            "AppBuildInfo must declare 'public string BuildClassification { get; }'.");
        Assert.Contains("BuildClassification = parsedMetadata.BuildClassification;", source, StringComparison.Ordinal);

        var parsed = AppBuildInfoMetadataParser.Parse(new Dictionary<string, string?>
        {
            ["MathFirst.ApplicationTitle"] = "MathFirst",
            ["MathFirst.ApplicationId"] = "com.tachiguro.mathfirst",
            ["MathFirst.ApplicationDisplayVersion"] = "1.0",
            ["MathFirst.ApplicationVersion"] = "1",
            ["MathFirst.SourceCommit"] = "local",
            ["MathFirst.BuildClassification"] = "Tester"
        });

        Assert.False(string.IsNullOrWhiteSpace(parsed.BuildClassification));
        Assert.Equal("Tester", parsed.BuildClassification);
    }

    [Fact]
    public void MauiInstallationIdStore_SourceImplementsIInstallationIdStoreAndUsesMauiPreferences()
    {
        var sourcePath = GetRepositoryPath("src", "MathFirst.App", "Services", "MauiInstallationIdStore.cs");
        Assert.True(File.Exists(sourcePath), "MauiInstallationIdStore.cs source file must exist.");

        var source = File.ReadAllText(sourcePath);
        Assert.True(
            Regex.IsMatch(source, @"public\s+sealed\s+class\s+MauiInstallationIdStore\s*:\s*(\w+,\s*)*IInstallationIdStore"),
            "MauiInstallationIdStore must implement IInstallationIdStore.");
        Assert.Contains("Preferences.Default", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences.Default.Clear()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences.Clear()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MauiTelemetryShareService_SourceImplementsITelemetryShareServiceAndUsesCanonicalCacheSubdirectory()
    {
        var sourcePath = GetRepositoryPath("src", "MathFirst.App", "Services", "MauiTelemetryShareService.cs");
        Assert.True(File.Exists(sourcePath), "MauiTelemetryShareService.cs source file must exist.");

        var source = File.ReadAllText(sourcePath);
        Assert.True(
            Regex.IsMatch(source, @"public\s+sealed\s+class\s+MauiTelemetryShareService\s*:\s*(\w+,\s*)*ITelemetryShareService"),
            "MauiTelemetryShareService must implement ITelemetryShareService.");
        Assert.Contains("FileSystem.CacheDirectory", source, StringComparison.Ordinal);
        Assert.Contains("\"telemetry-share\"", source, StringComparison.Ordinal);
        Assert.Contains("Share.Default", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
    }

    private static XDocument LoadFileProviderXml()
    {
        var resourcePath = GetRepositoryPath(
            "src", "MathFirst.App", "Platforms", "Android", "Resources", "xml", "microsoft_maui_essentials_fileprovider_file_paths.xml");

        Assert.True(File.Exists(resourcePath), $"FileProvider resource does not exist at {resourcePath}");
        return XDocument.Load(resourcePath);
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
