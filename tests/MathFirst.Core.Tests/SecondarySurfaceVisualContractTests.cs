namespace MathFirst.Core.Tests;

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Visual contract tests for Secondary Surfaces (Settings, Privacy, Operation Status, Confirmations, Keypad Previews)
/// under P5 Slice 1 (Settings Visual Consistency & Operation Status Polish).
/// </summary>
public sealed class SecondarySurfaceVisualContractTests
{
    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private static string ReadAppCss() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));

    private static string ReadSettingsRazor() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor"));

    private static string ReadPrivacyRazor() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Privacy.razor"));

    [Fact]
    public void P5_Slice1_OperationStatusBadges_DefineDedicatedReadOnlyAndBadgeStyles()
    {
        var css = ReadAppCss();

        // 1. .choice-button.read-only must have non-interactive cursor and space-between layout for name + badge
        var readOnlyMatch = Regex.Match(css, @"\.choice-button\.read-only\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(readOnlyMatch.Success, ".choice-button.read-only rule must exist in app.css");
        var readOnlyRules = readOnlyMatch.Groups["rules"].Value;
        Assert.Contains("cursor: default;", readOnlyRules, StringComparison.Ordinal);
        Assert.Contains("display: flex;", readOnlyRules, StringComparison.Ordinal);
        Assert.Contains("justify-content: space-between;", readOnlyRules, StringComparison.Ordinal);

        // 2. Read-only hover state must not trigger clickable transform or misleading active hover effects
        var readOnlyHoverMatch = Regex.Match(css, @"\.choice-button\.read-only:hover[^{]*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(readOnlyHoverMatch.Success, ".choice-button.read-only:hover rule must exist in app.css to prevent clickable hover animation");

        // 3. .operation-status-badge base rule
        var badgeMatch = Regex.Match(css, @"\.operation-status-badge\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(badgeMatch.Success, ".operation-status-badge rule must exist in app.css");
        var badgeRules = badgeMatch.Groups["rules"].Value;
        Assert.Contains("display: inline-flex;", badgeRules, StringComparison.Ordinal);
        Assert.Contains("font-weight:", badgeRules, StringComparison.Ordinal);

        // 4. .badge-unlocked status styling (cyber green emphasis)
        var unlockedMatch = Regex.Match(css, @"\.badge-unlocked\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(unlockedMatch.Success, ".badge-unlocked rule must exist in app.css");
        var unlockedRules = unlockedMatch.Groups["rules"].Value;
        Assert.Contains("color:", unlockedRules, StringComparison.Ordinal);
        Assert.Contains("background:", unlockedRules, StringComparison.Ordinal);

        // 5. .badge-locked status styling (subdued readable styling)
        var lockedMatch = Regex.Match(css, @"\.badge-locked\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(lockedMatch.Success, ".badge-locked rule must exist in app.css");
        var lockedRules = lockedMatch.Groups["rules"].Value;
        Assert.Contains("color:", lockedRules, StringComparison.Ordinal);
        Assert.Contains("background:", lockedRules, StringComparison.Ordinal);
    }

    [Fact]
    public void P5_Slice1_SettingsCards_DefineThemeAwareCyberDefenseFraming()
    {
        var css = ReadAppCss();

        // Base .settings-card rule
        Assert.Contains(".settings-card", css, StringComparison.Ordinal);

        // Dark theme specific override for .settings-card providing technical cyber surface
        var darkCardMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""dark""\]\s+\.settings-card\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(darkCardMatch.Success, "Dark theme override for .settings-card must exist in app.css");
        var darkRules = darkCardMatch.Groups["rules"].Value;
        Assert.Contains("border-color:", darkRules, StringComparison.Ordinal);
    }

    [Fact]
    public void P5_Slice1_KeypadSelectionVisuals_DefineThemeAwarePreviewAndCardAlignment()
    {
        var css = ReadAppCss();

        // Keypad choice card and preview key rules
        Assert.Contains(".keypad-choice-card", css, StringComparison.Ordinal);
        Assert.Contains(".keypad-preview-key", css, StringComparison.Ordinal);

        // Dark theme keypad preview key styling mirroring numeric keypad buttons
        var darkKeyMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""dark""\]\s+\.keypad-preview-key\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(darkKeyMatch.Success, "Dark theme override for .keypad-preview-key must exist in app.css");
    }

    [Fact]
    public void P5_Slice1_SettingsConfirmationPanels_DefineDistinctFraming()
    {
        var css = ReadAppCss();

        Assert.Contains(".settings-confirm-panel", css, StringComparison.Ordinal);
        Assert.Contains(".destructive-confirmation", css, StringComparison.Ordinal);
        Assert.Contains(".restore-defaults-confirmation", css, StringComparison.Ordinal);
    }

    [Fact]
    public void P5_Slice1_DiagnosticsAndPrivacy_RetainSharedAndRefinedTechnicalFraming()
    {
        var css = ReadAppCss();
        var privacy = ReadPrivacyRazor();

        Assert.Contains(".settings-diagnostics-action", css, StringComparison.Ordinal);
        Assert.Contains(".settings-version", css, StringComparison.Ordinal);
        Assert.Contains(".settings-copy-diagnostics-btn", css, StringComparison.Ordinal);
        Assert.Contains(".privacy-contact-url", css, StringComparison.Ordinal);

        // Privacy page inherits .settings-card and .settings-page framing
        Assert.Contains("class=\"settings-page privacy-page\"", privacy, StringComparison.Ordinal);
        Assert.Contains("class=\"settings-card\"", privacy, StringComparison.Ordinal);
    }

    [Fact]
    public void P5_Slice1_SettingsRazor_PreservesReadOnlySemanticsAndCurriculumAuthority()
    {
        var settings = ReadSettingsRazor();

        // Strict read-only presentation without click handlers
        Assert.Contains("choice-button read-only", settings, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"true\"", settings, StringComparison.Ordinal);
        Assert.Contains("operation-status-badge", settings, StringComparison.Ordinal);
        Assert.Contains("badge-unlocked", settings, StringComparison.Ordinal);
        Assert.Contains("badge-locked", settings, StringComparison.Ordinal);
        Assert.Contains("CurriculumUnlockPolicy.GetUnlockedOperations(Session.Progression.CurriculumStage)", settings, StringComparison.Ordinal);

        Assert.DoesNotContain("@onclick=\"() => ToggleOperation", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("PracticeTimeSetting", settings, StringComparison.Ordinal);
    }
}
