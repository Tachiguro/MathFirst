namespace MathFirst.Core.Tests;

using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// MF-CYBER-001 Slice 4/4: Lower Math Area Layout & Visual Contract Regression Tests.
/// Validates source-level CSS and Razor structure protecting the rigid lower mathematical training area,
/// ensuring that Calm Mode cleanly omits combat controls without shrinking, compressing, or shifting
/// keypad geometry, answer inputs, arithmetic expressions, or operation progress indicators.
/// </summary>
public sealed class LowerMathAreaLayoutContractTests
{
    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    [Fact]
    public void CalmMode_OmitsCyberDefenseHudAndCombatVisuals_FromHomeMarkup()
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        Assert.True(File.Exists(homePath), "Home.razor must exist.");
        var homeContent = File.ReadAllText(homePath);

        // Combat HUD component must be inside an IsCyberDefenseActive condition
        Assert.Matches(@"@if\s*\([^)]*IsCyberDefenseActive[^)]*\)[\s\S]*?<CyberDefenseHud", homeContent);

        // Combo badges, solve corners, and solve-to-attack title are conditioned on IsCyberDefenseActive
        Assert.Matches(@"@if\s*\([^)]*IsCyberDefenseActive[^)]*\)[\s\S]*?cyber-combo-slot", homeContent);
        Assert.Matches(@"@if\s*\([^)]*IsCyberDefenseActive[^)]*\)[\s\S]*?solve-corner", homeContent);
        Assert.Matches(@"@if\s*\([^)]*IsCyberDefenseActive[^)]*\)[\s\S]*?CyberDefense_SolveToAttack", homeContent);

        // Calm Mode provides a neutral localized solve heading
        Assert.Contains("Practice_SolveHeading", homeContent, StringComparison.Ordinal);
        Assert.Contains("calm-panel-tag", homeContent, StringComparison.Ordinal);
    }

    [Fact]
    public void CalmMode_DoesNotReserveBlankTopRegionSpace_WhenDisabled()
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        Assert.True(File.Exists(homePath), "Home.razor must exist.");
        var homeContent = File.ReadAllText(homePath);

        // .training-top-region is ONLY rendered when Cyber Defense is active and encounter is not null
        Assert.Matches(@"@if\s*\([^)]*IsCyberDefenseActive\s*&&\s*CyberDefenseState\.ActiveEncounter\s*is\s*not\s*null[^)]*\)[\s\S]*?class=""training-top-region""", homeContent);

        // In the @else branch (Calm Mode or null encounter), only invisible PracticeCountdownTimer is placed
        Assert.DoesNotMatch(@"@else[\s\S]*?class=""training-top-region""", homeContent);
    }

    [Fact]
    public void PracticeLayout_AndActiveGameplay_PreserveViewportStabilityRules_InCalmMode()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        Assert.True(File.Exists(cssPath), "app.css must exist.");
        var css = File.ReadAllText(cssPath);

        // 1. Calm mode grid row expansion
        Assert.Contains(".training-host.active-gameplay.calm-mode .training-card", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: minmax(0, 1fr);", css, StringComparison.Ordinal);

        // 2. Practice layout flex stability
        Assert.Contains(".training-host.active-gameplay.calm-mode .practice-layout", css, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 0;", css, StringComparison.Ordinal);

        // 3. Active gameplay host rules
        Assert.Contains(".training-host.active-gameplay", css, StringComparison.Ordinal);
        Assert.Contains(".training-host.active-gameplay .mathfirst-container", css, StringComparison.Ordinal);
    }

    [Fact]
    public void NumericKeypad_LayoutAndKeypadButtonTouchTarget_DefinitionsArePreserved()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        Assert.True(File.Exists(cssPath), "app.css must exist.");
        var css = File.ReadAllText(cssPath);

        // 1. 3-column grid definition
        Assert.Contains(".numeric-keypad {", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr));", css, StringComparison.Ordinal);

        // 2. Touch target minimum height (at least 2.75rem / 44px)
        Assert.Contains(".numeric-keypad-button {", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 2.75rem;", css, StringComparison.Ordinal);

        // 3. Keyboard focus ring support
        Assert.Contains(".numeric-keypad-button:focus-visible", css, StringComparison.Ordinal);

        // 4. Backspace styling
        Assert.Contains(".numeric-keypad-backspace", css, StringComparison.Ordinal);
    }

    [Fact]
    public void AnswerInput_AndExpressionRow_GeometryContract_IsPreservedAcrossBothModes()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        Assert.True(File.Exists(cssPath), "app.css must exist.");
        var css = File.ReadAllText(cssPath);

        // 1. Expression row styling
        Assert.Contains(".expression-row", css, StringComparison.Ordinal);
        Assert.Contains(".expression-problem", css, StringComparison.Ordinal);

        // 2. Answer input geometry and controlled behavior
        Assert.Contains(".answer-input", css, StringComparison.Ordinal);
        Assert.Contains(".answer-input:focus-visible", css, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationProgressHud_AndUnlockArea_AreVisibleAndConsistent_InBothModes()
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        Assert.True(File.Exists(homePath), "Home.razor must exist.");
        var homeContent = File.ReadAllText(homePath);

        // Operation unlock area and HUD are outside the IsCyberDefenseActive condition
        var match = Regex.Match(homeContent, @"<div class=""operation-unlock-area""[\s\S]*?<div class=""operation-progress-hud""");
        Assert.True(match.Success, "operation-unlock-area must be present in the practice layout.");

        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        Assert.True(File.Exists(cssPath), "app.css must exist.");
        var css = File.ReadAllText(cssPath);

        Assert.Contains(".operation-unlock-area", css, StringComparison.Ordinal);
        Assert.Contains(".operation-progress-hud", css, StringComparison.Ordinal);
        Assert.Contains(".operation-progress-entry", css, StringComparison.Ordinal);
        Assert.Contains(".operation-progress-symbol", css, StringComparison.Ordinal);
        Assert.Contains(".operation-progress-stage", css, StringComparison.Ordinal);
    }

    [Fact]
    public void SolveToAttackPanel_CalmModeModifier_ThemeParity_LightAndDark()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        Assert.True(File.Exists(cssPath), "app.css must exist.");
        var css = File.ReadAllText(cssPath);

        // Light theme Calm Mode styling
        Assert.Contains(".solve-to-attack-panel.calm-mode {", css, StringComparison.Ordinal);
        Assert.Contains("background: var(--color-surface-elevated);", css, StringComparison.Ordinal);

        // Dark theme Calm Mode styling
        Assert.Contains(".app-theme-root[data-theme=\"dark\"] .solve-to-attack-panel.calm-mode {", css, StringComparison.Ordinal);
        Assert.Contains("background: var(--color-surface);", css, StringComparison.Ordinal);
    }

    [Fact]
    public void ReducedMotion_Contract_SuppressesOrNeutralizesAnimations()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        Assert.True(File.Exists(cssPath), "app.css must exist.");
        var css = File.ReadAllText(cssPath);

        // Reduced motion media query must exist
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_ModeSelection_AccessibleSemantics()
    {
        var settingsPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor");
        Assert.True(File.Exists(settingsPath), "Settings.razor must exist.");
        var settingsContent = File.ReadAllText(settingsPath);

        // Settings contains accessible Cyber Defense vs Calm Mode configuration
        Assert.Contains("CyberDefensePreferences", settingsContent, StringComparison.Ordinal);
        Assert.Contains("Settings_CyberDefenseModeTitle", settingsContent, StringComparison.Ordinal);
        Assert.Contains("Settings_CyberDefenseModeHelp", settingsContent, StringComparison.Ordinal);
        Assert.Contains("CyberDefense_Mode_CyberDefense", settingsContent, StringComparison.Ordinal);
        Assert.Contains("CyberDefense_Mode_Calm", settingsContent, StringComparison.Ordinal);
        Assert.Contains("choice-grid choice-grid-two", settingsContent, StringComparison.Ordinal);
        Assert.Contains("choice-button", settingsContent, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=", settingsContent, StringComparison.Ordinal);
    }
}
