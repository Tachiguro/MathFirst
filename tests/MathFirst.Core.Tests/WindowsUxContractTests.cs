namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

public sealed class WindowsUxContractTests
{
    [Fact]
    public void ApplicationIdentity_UsesCanonicalRuntimeMetadata()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));
        var applicationId = project.Descendants("ApplicationId").Single().Value;

        Assert.Equal("com.tachiguro.mathfirst", applicationId);
        Assert.NotEqual("com.companyname.mathfirst.app", applicationId);

        var manifest = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "Platforms", "Windows", "Package.appxmanifest"));
        var ns = manifest.Root!.Name.Namespace;
        var publisherDisplayName = manifest.Descendants(ns + "PublisherDisplayName").Single().Value;
        var signingPublisher = manifest.Descendants(ns + "Identity").Single().Attribute("Publisher")!.Value;

        Assert.Equal("Tachiguro", publisherDisplayName);
        Assert.NotEqual("User Name", publisherDisplayName);
        Assert.Equal("CN=User Name", signingPublisher);
    }

    [Fact]
    public void Settings_UsesLanguageSelectAndSimpleLocalizedBackAction()
    {
        var settings = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor"));

        Assert.Contains("<select id=\"ui-language-select\"", settings, StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"Common_Back\"]", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Zurück zur Startseite", settings, StringComparison.Ordinal);
        Assert.Contains("InitializeAsync(startTiming: false)", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Onboarding_ContainsWelcomeAppearanceThreeStepTutorialAndGetStarted()
    {
        var onboarding = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Onboarding", "OnboardingHost.razor"));

        Assert.Contains("Onboarding_WelcomeTitle", onboarding, StringComparison.Ordinal);
        Assert.Contains("Settings_Appearance", onboarding, StringComparison.Ordinal);
        Assert.Contains("Onboarding_KeypadTitle", onboarding, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(onboarding, "class=\"keypad-choice-card ").Count);
        Assert.Contains("Session.PauseItemTiming()", onboarding, StringComparison.Ordinal);
        Assert.Contains("PreferenceStore.SetNumericKeypadLayout(_selectedKeypadLayout)", onboarding, StringComparison.Ordinal);
        Assert.Contains("Onboarding_TutorialTitle", onboarding, StringComparison.Ordinal);
        Assert.Contains("Common_Start", onboarding, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(onboarding, "class=\"workflow-step-badge\"").Count);
        Assert.Matches(">1</span>", onboarding);
        Assert.Matches(">2</span>", onboarding);
        Assert.Matches(">3</span>", onboarding);
    }

    [Fact]
    public void Practice_UsesControlledDecimalInputAndOnScreenKeypad()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));
        var browserInterop = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "mathfirst-ui.js"));

        // The answer field uses a platform-conditional inputmode via @AnswerInputMode:
        //   Android  => inputmode="none"  (suppress native soft keyboard; custom keypad is primary)
        //   Windows  => inputmode="decimal" (signal numeric decimal entry)
        Assert.Contains("inputmode=\"@AnswerInputMode\"", home, StringComparison.Ordinal);
        Assert.Matches("private\\s+static\\s+string\\s+AnswerInputMode\\s*=>\\s*\"decimal\";", home);
        Assert.Contains("NumericAnswerInputPolicy.MaximumLength", home, StringComparison.Ordinal);
        Assert.Contains("@oninput=\"HandleAnswerInput\"", home, StringComparison.Ordinal);
        Assert.Contains("MathFirstUi.attachNumericInputGuard", home, StringComparison.Ordinal);
        Assert.Contains("beforeinput", browserInterop, StringComparison.Ordinal);
        Assert.Contains("selectionStart", browserInterop, StringComparison.Ordinal);
        Assert.Contains("selectionEnd", browserInterop, StringComparison.Ordinal);
        Assert.Contains("preventDefault()", browserInterop, StringComparison.Ordinal);
        Assert.Contains("isComposing", browserInterop, StringComparison.Ordinal);
        Assert.Contains("paste", browserInterop, StringComparison.Ordinal);
        Assert.Contains("class=\"numeric-keypad\"", home, StringComparison.Ordinal);
        Assert.Contains("Keypad_Backspace", home, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"number\"", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Practice_WidensAnswerInputResponsivelyWithoutShrinkingArithmeticTypography()
    {
        var styles = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));

        Assert.Contains("inline-size: min(10ch, 100%);", styles, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: wrap;", styles, StringComparison.Ordinal);
        Assert.Contains("font-size: clamp(2.5rem", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_InlineConfirmationsUsePostRenderVisibilityAndFocusHandling()
    {
        var settings = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor"));
        var browserInterop = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "mathfirst-ui.js"));

        Assert.Contains("id=\"reset-learning-confirmation\"", settings, StringComparison.Ordinal);
        Assert.Contains("id=\"restore-defaults-confirmation\"", settings, StringComparison.Ordinal);
        Assert.Contains("id=\"full-local-reset-confirmation\"", settings, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(settings, "tabindex=\"-1\"").Count);
        Assert.Contains("OnAfterRenderAsync", settings, StringComparison.Ordinal);
        Assert.Contains("_pendingConfirmationVisibility", settings, StringComparison.Ordinal);
        Assert.Contains("MathFirstUi.focusAndScrollIntoView", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", settings, StringComparison.Ordinal);
        Assert.Contains("scrollIntoView", browserInterop, StringComparison.Ordinal);
        Assert.Contains("block: \"nearest\"", browserInterop, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", browserInterop, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_ContainsExactlyTwoKeypadLayoutChoices()
    {
        var settings = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor"));

        Assert.Contains("id=\"settings-keypad-title\"", settings, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(settings, "class=\"keypad-choice-card ").Count);
        Assert.Contains("NumericKeypadLayout.Phone", settings, StringComparison.Ordinal);
        Assert.Contains("NumericKeypadLayout.Numpad", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void MauiPreferences_PersistsKeypadOutsideLearnerDatabaseWithPhoneDefault()
    {
        var preferences = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Services", "MauiPreferenceStore.cs"));
        var learnerStore = File.ReadAllText(GetRepositoryPath("src", "MathFirst.Infrastructure.Sqlite", "SqliteLearnerStore.cs"));

        Assert.Contains("mathfirst.numeric_keypad_layout", preferences, StringComparison.Ordinal);
        Assert.Contains("Preferences.Default.Get(NumericKeypadLayoutKey, (int)NumericKeypadLayout.Phone)", preferences, StringComparison.Ordinal);
        Assert.Contains("Preferences.Default.Set(NumericKeypadLayoutKey", preferences, StringComparison.Ordinal);
        Assert.DoesNotContain("numeric_keypad_layout", learnerStore, StringComparison.Ordinal);
    }

    [Fact]
    public void Routes_ReevaluatesOnboardingRequirementAfterSettingsNavigation()
    {
        var routes = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Routes.razor"));

        Assert.Contains("Navigation.LocationChanged += OnLocationChanged", routes, StringComparison.Ordinal);
        Assert.Contains("Navigation.LocationChanged -= OnLocationChanged", routes, StringComparison.Ordinal);
        Assert.Contains("PreferenceStore.GetOnboardingCompleted()", routes, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
