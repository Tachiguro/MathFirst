namespace MathFirst.Core.Tests;

using System.Xml.Linq;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Non-brittle contract tests for Android input / keypad behavior mandated by MF-AND-001.
///
/// Verified by static source/project file inspection — no pixel-perfect or runtime tests.
/// Covers:
///   - Custom keypad remains the primary Android input UI
///   - Android requests native soft-keyboard suppression (inputmode="none")
///   - Windows does NOT inherit Android-only suppression
///   - Both platforms use the same NumericAnswerInputPolicy
///   - No Android-specific numeric semantic fork
/// </summary>
public sealed class AndroidInputContractTests
{
    // ============================================================
    // Custom keypad — primary Android input UI
    // ============================================================

    [Fact]
    public void AndroidInput_CustomKeypadIsPresentAndEnabledInHome()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // Custom numeric keypad markup must be present
        Assert.Contains("class=\"numeric-keypad\"", home, StringComparison.Ordinal);

        // Keypad must use NumericKeypadLayoutPolicy for key enumeration
        Assert.Contains("NumericKeypadLayoutPolicy.GetKeys", home, StringComparison.Ordinal);

        // Backspace key must be accessible (not glyph-only)
        Assert.Contains("Keypad_Backspace", home, StringComparison.Ordinal);

        // Keypad key handler must be wired
        Assert.Contains("HandleKeypadKey", home, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidInput_KeypadSupportsPhoneAndNumpadLayouts()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));
        var settings = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor"));
        var onboarding = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Onboarding", "OnboardingHost.razor"));

        // Both layout options must appear in settings and onboarding UI
        Assert.Contains("NumericKeypadLayout.Phone", settings, StringComparison.Ordinal);
        Assert.Contains("NumericKeypadLayout.Numpad", settings, StringComparison.Ordinal);
        Assert.Contains("NumericKeypadLayout.Phone", onboarding, StringComparison.Ordinal);
        Assert.Contains("NumericKeypadLayout.Numpad", onboarding, StringComparison.Ordinal);

        // Home uses the stored keypad preference
        Assert.Contains("_keypadLayout", home, StringComparison.Ordinal);
        Assert.Contains("GetNumericKeypadLayout", home, StringComparison.Ordinal);
    }

    // ============================================================
    // Native soft-keyboard suppression on Android
    // ============================================================

    [Fact]
    public void AndroidInput_AnswerFieldUsesAndroidSuppressingInputMode()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // The answer input uses dynamic AnswerInputMode, not a hardcoded value
        Assert.Contains("inputmode=\"@AnswerInputMode\"", home, StringComparison.Ordinal);
        Assert.Contains("virtualkeyboardpolicy=\"@AnswerVirtualKeyboardPolicy\"", home, StringComparison.Ordinal);

        var (androidBranch, _) = GetAndroidConditionalBranches(home);
        Assert.Matches(
            "private\\s+static\\s+string\\s+AnswerInputMode\\s*=>\\s*\"none\";",
            androidBranch);
        Assert.Matches(
            "private\\s+static\\s+string\\s+AnswerVirtualKeyboardPolicy\\s*=>\\s*\"manual\";",
            androidBranch);
    }

    [Fact]
    public void AndroidInput_WindowsBranchRetainsDecimalInputMode()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        var (_, nonAndroidBranch) = GetAndroidConditionalBranches(home);
        Assert.Matches(
            "private\\s+static\\s+string\\s+AnswerInputMode\\s*=>\\s*\"decimal\";",
            nonAndroidBranch);
        Assert.Matches(
            "private\\s+static\\s+string\\s+AnswerVirtualKeyboardPolicy\\s*=>\\s*\"auto\";",
            nonAndroidBranch);
    }

    [Fact]
    public void AndroidInput_SuppressionIsGatedToAndroidOnly()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        var (androidBranch, nonAndroidBranch) = GetAndroidConditionalBranches(home);

        Assert.Contains("AnswerInputMode => \"none\"", androidBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("AnswerInputMode => \"decimal\"", androidBranch, StringComparison.Ordinal);
        Assert.Contains("AnswerInputMode => \"decimal\"", nonAndroidBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("AnswerInputMode => \"none\"", nonAndroidBranch, StringComparison.Ordinal);
    }

    // ============================================================
    // NumericAnswerInputPolicy — shared across platforms
    // ============================================================

    [Fact]
    public void AndroidInput_BothPlatformsUseSharedNumericAnswerInputPolicy()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // The shared policy must be referenced (not a forked Android version)
        Assert.Contains("NumericAnswerInputPolicy", home, StringComparison.Ordinal);
        Assert.Contains("NumericAnswerInputPolicy.TryParseSubmission", home, StringComparison.Ordinal);
        Assert.Contains("NumericAnswerInputPolicy.Append", home, StringComparison.Ordinal);
        Assert.Contains("NumericAnswerInputPolicy.Backspace", home, StringComparison.Ordinal);

        // No Android-specific parsing fork must exist in Home.razor
        Assert.DoesNotContain("AndroidNumericPolicy", home, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AndroidInputPolicy", home, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AndroidInput_NumericAnswerInputPolicyLivesInApplication_NotPlatformLayer()
    {
        // NumericAnswerInputPolicy must be in the Application or Domain layer
        var applicationDir = GetRepositoryPath("src", "MathFirst.Application");
        var domainDir = GetRepositoryPath("src", "MathFirst.Domain");

        var appFiles = Directory.GetFiles(applicationDir, "*.cs", SearchOption.AllDirectories);
        var domainFiles = Directory.GetFiles(domainDir, "*.cs", SearchOption.AllDirectories);

        var allFiles = appFiles.Concat(domainFiles).ToList();
        var hasPolicy = allFiles.Any(f =>
        {
            var content = File.ReadAllText(f);
            return content.Contains("class NumericAnswerInputPolicy", StringComparison.Ordinal);
        });

        Assert.True(hasPolicy,
            "NumericAnswerInputPolicy must live in Application or Domain, not in a platform-specific project.");
    }

    // ============================================================
    // Android target framework activation
    // ============================================================

    [Fact]
    public void AndroidInput_AppProjectTargetsAndroid()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));
        var targets = project.Descendants("TargetFrameworks").Single().Value;

        Assert.Contains("net10.0-android", targets, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("net10.0-windows10.0.19041.0", targets, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AndroidInput_AndroidMinimumApiIs24()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));

        var androidMinVersion = project
            .Descendants("SupportedOSPlatformVersion")
            .FirstOrDefault(e => e.Attribute("Condition")?.Value?.Contains("android") == true)
            ?.Value;

        Assert.NotNull(androidMinVersion);
        // Must be 24.0 (API 24 = Android 7.0)
        Assert.Equal("24.0", androidMinVersion, ignoreCase: true, ignoreWhiteSpaceDifferences: true);
    }

    [Fact]
    public void AndroidInput_ApplicationIdIsCanonical()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));
        var appId = project.Descendants("ApplicationId").Single().Value;

        Assert.Equal("com.tachiguro.mathfirst", appId);
    }

    // ============================================================
    // MainActivity configuration changes (orientation safety)
    // ============================================================

    [Fact]
    public void AndroidInput_MainActivityHandlesOrientationConfigChanges()
    {
        var mainActivity = File.ReadAllText(
            GetRepositoryPath("src", "MathFirst.App", "Platforms", "Android", "MainActivity.cs"));

        // ConfigurationChanges must include Orientation to avoid Activity recreation on rotation
        Assert.Contains("ConfigChanges.Orientation", mainActivity, StringComparison.Ordinal);
        Assert.Contains("ConfigChanges.ScreenSize", mainActivity, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidInput_MainPageRespectsSystemBarAndCutoutSafeArea()
    {
        var page = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MainPage.xaml"));

        Assert.Equal("Container", page.Root?.Attribute("SafeAreaEdges")?.Value);
    }

    [Fact]
    public void AndroidInput_AnswerFieldRemainsExternalKeyboardCapable()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));
        var input = Regex.Match(
            home,
            "<input(?<attributes>.*?)aria-label=\"@Localizer\\[\"Training_AnswerInputAriaLabel\"\\]\"\\s*/>",
            RegexOptions.Singleline);

        Assert.True(input.Success, "Answer input markup was not found.");
        Assert.Contains("@oninput=\"HandleAnswerInput\"", input.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("readonly", input.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@onkeydown=\"HandleGlobalKeyDown\"", home, StringComparison.Ordinal);
    }

    // ============================================================
    // Android manifest audit
    // ============================================================

    [Fact]
    public void AndroidInput_ManifestContainsNoProhibitedPermissions()
    {
        var manifestPath = GetRepositoryPath("src", "MathFirst.App", "Platforms", "Android", "AndroidManifest.xml");
        var manifest = File.ReadAllText(manifestPath);

        // These permissions must not appear in MathFirst V1
        string[] prohibitedPermissions =
        [
            "CAMERA",
            "RECORD_AUDIO",
            "READ_CONTACTS",
            "ACCESS_FINE_LOCATION",
            "ACCESS_COARSE_LOCATION",
            "READ_EXTERNAL_STORAGE",
            "WRITE_EXTERNAL_STORAGE",
            "POST_NOTIFICATIONS",
        ];

        foreach (var permission in prohibitedPermissions)
        {
            Assert.DoesNotContain(permission, manifest, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static (string Android, string NonAndroid) GetAndroidConditionalBranches(string source)
    {
        var match = Regex.Match(
            source,
            "#if\\s+ANDROID(?<android>.*?)#else(?<nonAndroid>.*?)#endif",
            RegexOptions.Singleline);

        Assert.True(match.Success, "Android conditional input policy block was not found.");
        return (match.Groups["android"].Value, match.Groups["nonAndroid"].Value);
    }

    private static string GetRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
