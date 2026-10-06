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
    public void OnboardingComponent_IsRemovedFromApplication()
    {
        var onboardingPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Onboarding", "OnboardingHost.razor");
        Assert.False(File.Exists(onboardingPath), "OnboardingHost.razor must be deleted from MathFirst.App.");
    }

    [Fact]
    public void OnboardingLocalization_IsRemovedFromLocalizationService()
    {
        var localizationFile = File.ReadAllText(GetRepositoryPath("src", "MathFirst.Application", "LocalizationService.cs"));
        Assert.DoesNotContain("[\"Onboarding_", localizationFile, StringComparison.Ordinal);
    }

    [Fact]
    public void OnboardingCss_IsRemovedFromAppCss()
    {
        var cssFile = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));
        Assert.DoesNotContain(".onboarding-", cssFile, StringComparison.Ordinal);
    }

    [Fact]
    public void PreferenceStoreContract_ContainsNoOnboardingState()
    {
        var interfaceSource = File.ReadAllText(GetRepositoryPath("src", "MathFirst.Application", "IPreferenceStore.cs"));
        var implementationSource = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Services", "MauiPreferenceStore.cs"));

        Assert.DoesNotContain("GetOnboardingCompleted", interfaceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetOnboardingCompleted", interfaceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOnboardingCompleted", implementationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetOnboardingCompleted", implementationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OnboardingKey", implementationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("mathfirst.onboarding_completed", implementationSource, StringComparison.Ordinal);
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
    public void Settings_PresentsNumpadBeforePhoneInKeypadChoiceGrid()
    {
        var settings = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor"));

        var gridMatch = Regex.Match(settings, "<div class=\"keypad-choice-grid\">(?<grid>.*?)</div>", RegexOptions.Singleline);
        Assert.True(gridMatch.Success, "Keypad choice grid markup was not found in Settings.razor.");

        var grid = gridMatch.Groups["grid"].Value;
        var numpadIndex = grid.IndexOf("NumericKeypadLayout.Numpad", StringComparison.Ordinal);
        var phoneIndex = grid.IndexOf("NumericKeypadLayout.Phone", StringComparison.Ordinal);

        Assert.True(numpadIndex >= 0, "Numpad layout choice must exist in Settings keypad choice grid.");
        Assert.True(phoneIndex >= 0, "Phone layout choice must exist in Settings keypad choice grid.");
        Assert.True(numpadIndex < phoneIndex, "Settings must present Numpad layout before Phone layout.");
        Assert.Matches("private\\s+NumericKeypadLayout\\s+_selectedKeypadLayout\\s*=\\s*NumericKeypadLayout\\.Numpad;", settings);
    }

    [Fact]
    public void MauiPreferences_PersistsKeypadOutsideLearnerDatabaseWithNumpadDefault()
    {
        var preferences = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Services", "MauiPreferenceStore.cs"));
        var learnerStore = File.ReadAllText(GetRepositoryPath("src", "MathFirst.Infrastructure.Sqlite", "SqliteLearnerStore.cs"));

        Assert.Contains("mathfirst.numeric_keypad_layout", preferences, StringComparison.Ordinal);
        Assert.Contains("Preferences.Default.Get(NumericKeypadLayoutKey, (int)NumericKeypadLayout.Numpad)", preferences, StringComparison.Ordinal);
        Assert.Contains("Preferences.Default.Set(NumericKeypadLayoutKey", preferences, StringComparison.Ordinal);
        Assert.DoesNotContain("numeric_keypad_layout", learnerStore, StringComparison.Ordinal);
    }

    [Fact]
    public void MauiPreferences_OperationPreferencesDefaultToAdditionOnly()
    {
        var preferences = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Services", "MauiPreferenceStore.cs"));

        Assert.Contains("Preferences.Default.Get(GetOperationKey(operation), operation == ArithmeticOperation.Addition)", preferences, StringComparison.Ordinal);
    }

    [Fact]
    public void Routes_AlwaysRendersRouterWithoutOnboardingStartupGate()
    {
        var routes = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Routes.razor"));

        Assert.Contains("<Router", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOnboardingCompleted", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("OnboardingHost", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleOnboardingCompleted", routes, StringComparison.Ordinal);
        Assert.Contains("class=\"app-theme-root\"", routes, StringComparison.Ordinal);
        Assert.Contains("ThemeService.ThemeChanged", routes, StringComparison.Ordinal);
    }

    [Fact]
    public void Keypad_DefinesActivePressFeedbackAndFocusVisibleContracts()
    {
        var styles = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));

        // Centralized focus-visible rule must include .numeric-keypad-button:focus-visible
        Assert.Matches(@"\.numeric-keypad-button:focus-visible\s*,\s*select:focus-visible", styles);
        Assert.Contains("outline: 3px solid var(--color-focus-ring);", styles, StringComparison.Ordinal);
        Assert.Contains("outline-offset: 2px;", styles, StringComparison.Ordinal);

        // Active state must provide primary background, primary border, contrast text, 1px translation, and instant transition
        var activeMatch = Regex.Match(styles, @"\.numeric-keypad-button:not\(:disabled\):active\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(activeMatch.Success, ".numeric-keypad-button:not(:disabled):active rule must exist in app.css");

        var rules = activeMatch.Groups["rules"].Value;
        Assert.Contains("border-color: var(--color-primary);", rules, StringComparison.Ordinal);
        Assert.Contains("background: var(--color-primary);", rules, StringComparison.Ordinal);
        Assert.Contains("color: var(--color-primary-contrast);", rules, StringComparison.Ordinal);
        Assert.Contains("transform: translateY(1px);", rules, StringComparison.Ordinal);
        Assert.Contains("transition: none;", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void Keypad_GuardsHoverWithPointerFineMediaQuery_ToPreventStickyTouchHover()
    {
        var styles = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));

        // Hover style on numeric keypad button must be wrapped in @media (hover: hover) and (pointer: fine)
        var hoverMatch = Regex.Match(
            styles,
            @"@media\s*\(\s*hover:\s*hover\s*\)\s*and\s*\(\s*pointer:\s*fine\s*\)\s*\{[^{}]*\.numeric-keypad-button:not\(:disabled\):hover\s*\{(?<rules>[^}]+)\}[^{}]*\}",
            RegexOptions.Singleline);
        Assert.True(hoverMatch.Success, ".numeric-keypad-button:not(:disabled):hover must be scoped to @media (hover: hover) and (pointer: fine)");

        var rules = hoverMatch.Groups["rules"].Value;
        Assert.Contains("border-color: var(--color-primary);", rules, StringComparison.Ordinal);
        Assert.Contains("background: var(--color-primary-soft);", rules, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
