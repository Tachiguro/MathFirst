namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

public sealed class AndroidPackagingContractTests
{
    private static readonly XNamespace AndroidNs = "http://schemas.android.com/apk/res/android";
    private static readonly string[] LearnerDatabaseFiles =
    [
        "mathfirst_learner.db",
        "mathfirst_learner.db-shm",
        "mathfirst_learner.db-wal"
    ];

    private static readonly string[] BackupDomains =
    [
        "database",
        "device_database",
        "device_file",
        "device_root",
        "device_sharedpref",
        "external",
        "file",
        "root",
        "sharedpref"
    ];

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
        Assert.Equal("@xml/backup_rules", application.Attribute(AndroidNs + "fullBackupContent")?.Value);
        Assert.Equal("@xml/data_extraction_rules", application.Attribute(AndroidNs + "dataExtractionRules")?.Value);
        Assert.Equal("true", application.Attribute(AndroidNs + "supportsRtl")?.Value);
        Assert.Equal("@mipmap/appicon", application.Attribute(AndroidNs + "icon")?.Value);
        Assert.Equal("@mipmap/appicon_round", application.Attribute(AndroidNs + "roundIcon")?.Value);
    }

    [Fact]
    public void Project_ConfiguresAndroidPackagingAndIdentityProperties()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));
        var targetFrameworks = GetProperty(project, "TargetFrameworks")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal("com.tachiguro.mathfirst", GetProperty(project, "ApplicationId"));
        Assert.Equal("1.0", GetProperty(project, "ApplicationDisplayVersion"));
        Assert.Equal("1", GetProperty(project, "ApplicationVersion"));
        Assert.Equal(
            ["net10.0-windows10.0.19041.0", "net10.0-android36.0"],
            targetFrameworks);
        Assert.DoesNotContain("net10.0-android", targetFrameworks);

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

    [Fact]
    public void Api24To27BackupRules_DenyAllSupportedAppDataDomains()
    {
        var rules = XDocument.Load(GetRepositoryPath(
            "src", "MathFirst.App", "Platforms", "Android", "Resources", "xml", "backup_rules.xml"));

        Assert.Equal("full-backup-content", rules.Root?.Name.LocalName);
        Assert.Empty(rules.Root!.Elements("include"));

        var exclusions = rules.Root.Elements("exclude").ToArray();
        Assert.Equal(
            BackupDomains,
            exclusions.Select(GetDomain).OrderBy(domain => domain, StringComparer.Ordinal));
        Assert.All(exclusions, exclusion => Assert.Equal(".", exclusion.Attribute("path")?.Value));
        Assert.All(rules.Root.Elements(), element => Assert.Equal("exclude", element.Name.LocalName));
    }

    [Fact]
    public void Api28To30BackupRules_AllowOnlyLearnerDatabaseFilesForDeviceTransfer()
    {
        var rules = XDocument.Load(GetRepositoryPath(
            "src", "MathFirst.App", "Platforms", "Android", "Resources", "xml-v28", "backup_rules.xml"));

        Assert.Equal("full-backup-content", rules.Root?.Name.LocalName);
        Assert.Empty(rules.Root!.Elements("exclude"));

        var inclusions = rules.Root.Elements("include").ToArray();
        Assert.Equal(
            LearnerDatabaseFiles,
            inclusions.Select(GetPath).OrderBy(path => path, StringComparer.Ordinal));
        Assert.All(inclusions, inclusion => Assert.Equal("file", GetDomain(inclusion)));
        Assert.All(inclusions, inclusion => Assert.Equal("deviceToDeviceTransfer", inclusion.Attribute("requireFlags")?.Value));
        Assert.All(rules.Root.Elements(), element => Assert.Equal("include", element.Name.LocalName));
    }

    [Fact]
    public void Api31AndLaterExtractionRules_DenyCloudBackupAndAllowOnlyLearnerDatabaseFilesForDeviceTransfer()
    {
        var rules = XDocument.Load(GetRepositoryPath(
            "src", "MathFirst.App", "Platforms", "Android", "Resources", "xml", "data_extraction_rules.xml"));

        Assert.Equal("data-extraction-rules", rules.Root?.Name.LocalName);
        var cloudBackup = rules.Root!.Elements("cloud-backup").Single();
        var deviceTransfer = rules.Root.Elements("device-transfer").Single();
        Assert.Equal(2, rules.Root.Elements().Count());

        Assert.Empty(cloudBackup.Elements("include"));
        var cloudExclusions = cloudBackup.Elements("exclude").ToArray();
        Assert.Equal(
            BackupDomains,
            cloudExclusions.Select(GetDomain).OrderBy(domain => domain, StringComparer.Ordinal));
        Assert.All(cloudExclusions, exclusion => Assert.Equal(".", exclusion.Attribute("path")?.Value));
        Assert.All(cloudBackup.Elements(), element => Assert.Equal("exclude", element.Name.LocalName));

        Assert.Empty(deviceTransfer.Elements("exclude"));
        var transferInclusions = deviceTransfer.Elements("include").ToArray();
        Assert.Equal(
            LearnerDatabaseFiles,
            transferInclusions.Select(GetPath).OrderBy(path => path, StringComparer.Ordinal));
        Assert.All(transferInclusions, inclusion => Assert.Equal("file", GetDomain(inclusion)));
        Assert.All(deviceTransfer.Elements(), element => Assert.Equal("include", element.Name.LocalName));
    }

    [Fact]
    public void Adr0006_IsAcceptedAfterSuccessfulConsolidatedReview()
    {
        var adr = File.ReadAllText(GetRepositoryPath(
            "docs", "decisions", "ADR-0006-android-packaging-signing-and-manifest-release-security.md"));
        var status = Regex.Match(adr, "(?ms)^## Status\\s*\\r?\\n(?<status>.*?)(?=^## )");

        Assert.True(status.Success, "ADR-0006 status section was not found.");
        Assert.Equal("Accepted", status.Groups["status"].Value.Trim());
    }

    [Fact]
    public void Adr0006_RecordsTheCorrectiveTargetBackupSigningAndProvenanceBoundaries()
    {
        var adr = File.ReadAllText(GetRepositoryPath(
            "docs", "decisions", "ADR-0006-android-packaging-signing-and-manifest-release-security.md"));

        Assert.Contains("`net10.0-android36.0`", adr, StringComparison.Ordinal);
        Assert.Contains("API 24–27", adr, StringComparison.Ordinal);
        Assert.Contains("API 28–30", adr, StringComparison.Ordinal);
        Assert.Contains("API 31+", adr, StringComparison.Ordinal);
        Assert.Contains("`deviceToDeviceTransfer`", adr, StringComparison.Ordinal);
        Assert.Contains("source-candidate debug signing", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("independent validation", adr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reproducibly generated", adr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provably tied", adr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("zero risk", adr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no negative consequences", adr, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProperty(XDocument project, string name) =>
        project.Descendants(name).Single().Value;

    private static string GetDomain(XElement element) =>
        element.Attribute("domain")?.Value ?? string.Empty;

    private static string GetPath(XElement element) =>
        element.Attribute("path")?.Value ?? string.Empty;

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine(new[] { GetRepositoryRoot() }.Concat(segments).ToArray());

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
