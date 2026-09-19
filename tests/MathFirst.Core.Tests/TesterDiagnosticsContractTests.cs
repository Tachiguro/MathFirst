namespace MathFirst.Core.Tests;

using MathFirst.Application;
using Xunit;

public sealed class TesterDiagnosticsContractTests
{
    [Fact]
    public void Format_ProducesExpectedTesterDiagnosticPayload()
    {
        var formatted = AppDiagnosticFormatter.Format(
            applicationTitle: "MathFirst",
            displayVersion: "1.0",
            buildNumber: 1,
            buildClassification: "Tester",
            shortSourceCommit: "12345678",
            platformDescription: "Android 16",
            applicationId: "com.tachiguro.mathfirst.tester");

        const string expected = "MathFirst 1.0 (1)\n" +
                               "Build: Tester\n" +
                               "Source: 12345678\n" +
                               "Platform: Android 16\n" +
                               "App ID: com.tachiguro.mathfirst.tester";

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void Format_ProducesExpectedLocalDiagnosticPayload()
    {
        var formatted = AppDiagnosticFormatter.Format(
            applicationTitle: "MathFirst",
            displayVersion: "1.0",
            buildNumber: 1,
            buildClassification: "Local",
            shortSourceCommit: "local",
            platformDescription: "Windows 10.0.26100.0",
            applicationId: "com.tachiguro.mathfirst");

        const string expected = "MathFirst 1.0 (1)\n" +
                               "Build: Local\n" +
                               "Source: local\n" +
                               "Platform: Windows 10.0.26100.0\n" +
                               "App ID: com.tachiguro.mathfirst";

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void Format_PreservesExactSemanticFieldsWithoutFabricatingTesterIdentity()
    {
        var metadata = new AppBuildMetadata(
            ApplicationTitle: "MathFirst",
            ApplicationId: "com.tachiguro.mathfirst",
            DisplayVersion: "1.0",
            BuildNumber: 42,
            SourceCommit: "abcdef0123456789",
            BuildClassification: "Local");

        var formatted = AppDiagnosticFormatter.Format(
            metadata.ApplicationTitle,
            metadata.DisplayVersion,
            metadata.BuildNumber,
            metadata.BuildClassification,
            AppBuildInfoMetadataParser.GetShortSourceCommit(metadata.SourceCommit),
            "Windows 11",
            metadata.ApplicationId);

        Assert.Contains("MathFirst 1.0 (42)", formatted, StringComparison.Ordinal);
        Assert.Contains("Build: Local", formatted, StringComparison.Ordinal);
        Assert.Contains("Source: abcdef01", formatted, StringComparison.Ordinal);
        Assert.Contains("Platform: Windows 11", formatted, StringComparison.Ordinal);
        Assert.Contains("App ID: com.tachiguro.mathfirst", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("Tester", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("com.tachiguro.mathfirst.tester", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_EnforcesStrictPrivacyBoundary()
    {
        var formatted = AppDiagnosticFormatter.Format(
            applicationTitle: "MathFirst",
            displayVersion: "1.0",
            buildNumber: 1,
            buildClassification: "Tester",
            shortSourceCommit: "12345678",
            platformDescription: "Android 16",
            applicationId: "com.tachiguro.mathfirst.tester");

        // Learner progression / score / attempts
        Assert.DoesNotContain("attempt", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("score", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("progress", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fsrs", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fact", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latency", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("streak", formatted, StringComparison.OrdinalIgnoreCase);

        // Filesystem / Database
        Assert.DoesNotContain(".db", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mathfirst_learner", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppDataDirectory", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/data/", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/storage/", formatted, StringComparison.OrdinalIgnoreCase);

        // Hardware / Device identifiers / Accounts
        Assert.DoesNotContain("serial", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("imei", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("android_id", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("keystore", formatted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Format_HandlesNullOrWhitespaceInputsSafely()
    {
        var formatted = AppDiagnosticFormatter.Format(
            applicationTitle: "",
            displayVersion: " ",
            buildNumber: 0,
            buildClassification: "",
            shortSourceCommit: "",
            platformDescription: "",
            applicationId: "");

        Assert.Contains("MathFirst 1.0 (1)", formatted, StringComparison.Ordinal);
        Assert.Contains("Build: Local", formatted, StringComparison.Ordinal);
        Assert.Contains("Source: local", formatted, StringComparison.Ordinal);
        Assert.Contains("Platform: Unknown", formatted, StringComparison.Ordinal);
        Assert.Contains("App ID: com.tachiguro.mathfirst", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void AppPlatformInfo_FormatsDescriptionCorrectly()
    {
        IAppPlatformInfo fullPlatform = new TestPlatformInfo("Android", "16");
        Assert.Equal("Android 16", fullPlatform.PlatformDescription);

        IAppPlatformInfo nameOnlyPlatform = new TestPlatformInfo("Windows", "");
        Assert.Equal("Windows", nameOnlyPlatform.PlatformDescription);

        IAppPlatformInfo versionOnlyPlatform = new TestPlatformInfo("", "16.0");
        Assert.Equal("16.0", versionOnlyPlatform.PlatformDescription);

        IAppPlatformInfo emptyPlatform = new TestPlatformInfo("", "");
        Assert.Equal("Unknown", emptyPlatform.PlatformDescription);
    }

    [Fact]
    public async Task ClipboardService_InvokesAsynchronously()
    {
        var testClipboard = new TestClipboardService();
        await testClipboard.SetTextAsync("test payload");

        Assert.Equal("test payload", testClipboard.LastCopiedText);
    }

    private sealed class TestPlatformInfo(string platformName, string platformVersion) : IAppPlatformInfo
    {
        public string PlatformName { get; } = platformName;
        public string PlatformVersion { get; } = platformVersion;
    }

    private sealed class TestClipboardService : IClipboardService
    {
        public string? LastCopiedText { get; private set; }

        public Task SetTextAsync(string text)
        {
            LastCopiedText = text;
            return Task.CompletedTask;
        }
    }
}
