namespace MathFirst.Core.Tests;

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Contract hardening and accessibility verification tests for P5 Slice 3
/// (Visual &amp; Accessibility Contract Hardening).
/// Covers: Theme token contrast ratios, reduced-motion suppression, focus-visible preservation,
/// responsive overflow safeguards, gameplay layout isolation, and read-only cascade specificity.
/// </summary>
public sealed class SecondaryVisualAccessibilityContractTests
{
    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private static string ReadAppCss() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));

    private static string ReadSettingsRazor() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor"));

    private static string ReadHomeRazor() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

    // =========================================================================
    // 1. THEME TOKEN CONTRAST RATIOS (WCAG AA 4.5:1 / 3:1)
    // =========================================================================

    [Fact]
    public void P5_Slice3_ThemeTokenContrast_GuaranteesAccessibleContrastRatiosAcrossThemes()
    {
        var css = ReadAppCss();

        // Extract light theme variables
        var lightMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""light""\]\s*\{(?<vars>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(lightMatch.Success, "Light theme token block must exist in app.css");
        var lightVars = lightMatch.Groups["vars"].Value;

        var lightText = ExtractColorVariable(lightVars, "--color-text");
        var lightMuted = ExtractColorVariable(lightVars, "--color-muted");
        var lightPrimary = ExtractColorVariable(lightVars, "--color-primary");
        var lightDanger = ExtractColorVariable(lightVars, "--color-danger");
        var lightSurface = ExtractColorVariable(lightVars, "--color-surface");
        var lightPrimaryContrast = ExtractColorVariable(lightVars, "--color-primary-contrast");

        // Extract dark theme variables
        var darkMatch = Regex.Match(css, @"(?::root\[data-theme=""dark""\],|\.app-theme-root\[data-theme=""dark""\])\s*\{(?<vars>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(darkMatch.Success, "Dark theme token block must exist in app.css");
        var darkVars = darkMatch.Groups["vars"].Value;

        var darkText = ExtractColorVariable(darkVars, "--color-text");
        var darkMuted = ExtractColorVariable(darkVars, "--color-muted");
        var darkPrimary = ExtractColorVariable(darkVars, "--color-primary");
        var darkDanger = ExtractColorVariable(darkVars, "--color-danger");
        var darkBackground = ExtractColorVariable(darkVars, "--color-background");
        var darkPrimaryContrast = ExtractColorVariable(darkVars, "--color-primary-contrast");

        // 1. Light theme normal text (>= 4.5:1)
        var lightTextRatio = CalculateContrastRatio(lightText, lightSurface);
        Assert.True(lightTextRatio >= 4.5, $"Light text ({lightText}) on surface ({lightSurface}) contrast ratio must be >= 4.5 (was {lightTextRatio:F2})");

        // 2. Light theme muted text (>= 4.5:1)
        var lightMutedRatio = CalculateContrastRatio(lightMuted, lightSurface);
        Assert.True(lightMutedRatio >= 4.5, $"Light muted text ({lightMuted}) on surface ({lightSurface}) contrast ratio must be >= 4.5 (was {lightMutedRatio:F2})");

        // 3. Light theme primary button text on primary background (>= 4.5:1)
        var lightPrimaryBtnRatio = CalculateContrastRatio(lightPrimaryContrast, lightPrimary);
        Assert.True(lightPrimaryBtnRatio >= 4.5, $"Light primary contrast ({lightPrimaryContrast}) on primary ({lightPrimary}) ratio must be >= 4.5 (was {lightPrimaryBtnRatio:F2})");

        // 4. Light theme danger text on surface (>= 4.5:1)
        var lightDangerRatio = CalculateContrastRatio(lightDanger, lightSurface);
        Assert.True(lightDangerRatio >= 4.5, $"Light danger ({lightDanger}) on surface ({lightSurface}) ratio must be >= 4.5 (was {lightDangerRatio:F2})");

        // 5. Dark theme normal text on dark background (>= 4.5:1)
        var darkTextRatio = CalculateContrastRatio(darkText, darkBackground);
        Assert.True(darkTextRatio >= 4.5, $"Dark text ({darkText}) on background ({darkBackground}) contrast ratio must be >= 4.5 (was {darkTextRatio:F2})");

        // 6. Dark theme muted text on dark background (>= 4.5:1)
        var darkMutedRatio = CalculateContrastRatio(darkMuted, darkBackground);
        Assert.True(darkMutedRatio >= 4.5, $"Dark muted text ({darkMuted}) on background ({darkBackground}) contrast ratio must be >= 4.5 (was {darkMutedRatio:F2})");

        // 7. Dark theme primary button text on primary background (>= 4.5:1)
        var darkPrimaryBtnRatio = CalculateContrastRatio(darkPrimaryContrast, darkPrimary);
        Assert.True(darkPrimaryBtnRatio >= 4.5, $"Dark primary contrast ({darkPrimaryContrast}) on primary ({darkPrimary}) ratio must be >= 4.5 (was {darkPrimaryBtnRatio:F2})");

        // 8. Dark theme emerald accent (#3effa8) on dark background (>= 4.5:1)
        var darkAccentRatio = CalculateContrastRatio("#3effa8", darkBackground);
        Assert.True(darkAccentRatio >= 4.5, $"Dark accent #3effa8 on background ({darkBackground}) ratio must be >= 4.5 (was {darkAccentRatio:F2})");

        // 9. Dark theme large equation text (#ffffff) on dark background (>= 3.0:1)
        var darkEquationRatio = CalculateContrastRatio("#ffffff", darkBackground);
        Assert.True(darkEquationRatio >= 3.0, $"Dark equation #ffffff on background ({darkBackground}) ratio must be >= 3.0 (was {darkEquationRatio:F2})");
    }

    // =========================================================================
    // 2. REDUCED MOTION SUPPRESSION
    // =========================================================================

    [Fact]
    public void P5_Slice3_ReducedMotion_DisablesInteractiveTransformsAndTransitionsOnSecondarySurfaces()
    {
        var css = ReadAppCss();
        var block = ExtractMediaBlock(css, "@media (prefers-reduced-motion: reduce)");

        // Transition suppression must include secondary controls
        Assert.Contains(".keypad-choice-card", block, StringComparison.Ordinal);
        Assert.Contains(".button", block, StringComparison.Ordinal);
        Assert.Contains(".choice-button", block, StringComparison.Ordinal);
        Assert.Contains(".operation-status-badge", block, StringComparison.Ordinal);
        Assert.Contains("transition: none", block, StringComparison.Ordinal);

        // Hover transform suppression on keypad card and button
        Assert.Contains("transform: none", block, StringComparison.Ordinal);
        Assert.Contains(".keypad-choice-card:hover", block, StringComparison.Ordinal);
    }

    // =========================================================================
    // 3. FOCUS-VISIBLE PRESERVATION ON SECONDARY CONTROLS
    // =========================================================================

    [Fact]
    public void P5_Slice3_FocusVisible_PreservesKeyboardNavigationAndFocusRingOnSecondaryControls()
    {
        var css = ReadAppCss();

        var focusVisibleMatch = Regex.Match(
            css,
            @"(?<selectors>[^{]+:focus-visible[^{]*)\s*\{(?<rules>[^}]+)\}",
            RegexOptions.Singleline);
        Assert.True(focusVisibleMatch.Success, "Centralized :focus-visible rule must exist in app.css");

        var selectors = focusVisibleMatch.Groups["selectors"].Value;
        var rules = focusVisibleMatch.Groups["rules"].Value;

        // Interactive secondary elements must be included in focus-visible selector
        Assert.Contains(".button:focus-visible", selectors, StringComparison.Ordinal);
        Assert.Contains(".choice-button:focus-visible", selectors, StringComparison.Ordinal);
        Assert.Contains(".keypad-choice-card:focus-visible", selectors, StringComparison.Ordinal);
        Assert.Contains(".numeric-keypad-button:focus-visible", selectors, StringComparison.Ordinal);
        Assert.Contains("select:focus-visible", selectors, StringComparison.Ordinal);
        Assert.Contains("a:focus-visible", selectors, StringComparison.Ordinal);

        // Focus outline styling
        Assert.Contains("outline: 3px solid var(--color-focus-ring);", rules, StringComparison.Ordinal);
        Assert.Contains("outline-offset: 2px;", rules, StringComparison.Ordinal);
    }

    // =========================================================================
    // 4. RESPONSIVE AND OVERFLOW SAFEGUARDS
    // =========================================================================

    [Fact]
    public void P5_Slice3_ResponsiveAndOverflow_SafeguardsSecondaryPagesAndDialogs()
    {
        var css = ReadAppCss();

        // 1. Settings page container bounds and scroll containment
        var settingsPageMatch = Regex.Match(css, @"\.settings-page\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(settingsPageMatch.Success, ".settings-page rule must exist in app.css");
        var settingsRules = settingsPageMatch.Groups["rules"].Value;
        Assert.Contains("max-width: min(48rem, 100%);", settingsRules, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", settingsRules, StringComparison.Ordinal);

        // 2. Settings card fluid box sizing and padding
        var settingsCardMatch = Regex.Match(css, @"\.settings-card\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(settingsCardMatch.Success, ".settings-card rule must exist in app.css");
        var cardRules = settingsCardMatch.Groups["rules"].Value;
        Assert.Contains("display: grid;", cardRules, StringComparison.Ordinal);
        Assert.Contains("padding:", cardRules, StringComparison.Ordinal);

        // 3. Word wrapping on localized operation names and ready gate names (long German/Russian text)
        var opNameMatch = Regex.Match(css, @"\.operation-name\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(opNameMatch.Success, ".operation-name rule must exist in app.css");
        Assert.Contains("overflow-wrap: break-word;", opNameMatch.Groups["rules"].Value, StringComparison.Ordinal);
        Assert.Contains("min-width: 0;", opNameMatch.Groups["rules"].Value, StringComparison.Ordinal);

        var readyNameMatch = Regex.Match(css, @"\.ready-progress-name\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(readyNameMatch.Success, ".ready-progress-name rule must exist in app.css");
        Assert.Contains("overflow-wrap: break-word;", readyNameMatch.Groups["rules"].Value, StringComparison.Ordinal);
        Assert.Contains("min-width: 0;", readyNameMatch.Groups["rules"].Value, StringComparison.Ordinal);

        // 4. Action button width bounds on overlays
        var actionMatch = Regex.Match(css, @"\.practice-overlay-action\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(actionMatch.Success, ".practice-overlay-action rule must exist in app.css");
        Assert.Contains("width: min(100%, 20rem);", actionMatch.Groups["rules"].Value, StringComparison.Ordinal);

        var checkinActionMatch = Regex.Match(css, @"\.checkin-actions\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(checkinActionMatch.Success, ".checkin-actions rule must exist in app.css");
        Assert.Contains("width: min(100%, 20rem);", checkinActionMatch.Groups["rules"].Value, StringComparison.Ordinal);
    }

    // =========================================================================
    // 5. GAMEPLAY ISOLATION (MF-UX-008 POSITIONAL STABILITY)
    // =========================================================================

    [Fact]
    public void P5_Slice3_GameplayIsolation_GuaranteesSecondarySelectorsDoNotLeakIntoActiveTraining()
    {
        var css = ReadAppCss();

        // 1. Numeric keypad coordinates and grid geometry remain strictly defined
        Assert.Contains(".numeric-keypad", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: repeat(4, minmax(2.75rem, 1fr));", css, StringComparison.Ordinal);

        // 2. Expression row geometry remains centered and clamped
        Assert.Contains(".expression-row", css, StringComparison.Ordinal);
        Assert.Contains("justify-content: center;", css, StringComparison.Ordinal);

        // 3. Secondary card styles (.settings-card, .checkin-dialog, .feedback-dialog, .teaching-dialog)
        // are explicitly class-scoped and do NOT target active training layout containers (.practice-layout, .expression-problem, .solve-to-attack-panel)
        Assert.DoesNotMatch(@"\.(?:expression-row|numeric-keypad|solve-to-attack-panel|combat-hud)[^{]*\{[^}]*background:\s*linear-gradient\(180deg,\s*rgba\(14,\s*30,\s*24", css);
    }

    // =========================================================================
    // 6. READ-ONLY CASCADE SPECIFICITY
    // =========================================================================

    [Fact]
    public void P5_Slice3_ReadOnlyOperations_CascadeSpecificityPreventsInteractiveStates()
    {
        var css = ReadAppCss();

        // 1. Generic choice button hover must exclude .read-only
        Assert.Contains(".choice-button:not(:disabled):not(.read-only):hover", css, StringComparison.Ordinal);

        // 2. Generic choice button active must exclude .read-only
        Assert.Contains(".choice-button.active:not(.read-only)", css, StringComparison.Ordinal);

        // 3. Read-only hover states explicitly reset cursor and transform
        var readOnlyMatch = Regex.Match(css, @"\.choice-button\.read-only\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(readOnlyMatch.Success);
        Assert.Contains("cursor: default;", readOnlyMatch.Groups["rules"].Value, StringComparison.Ordinal);
        Assert.Contains("user-select: none;", readOnlyMatch.Groups["rules"].Value, StringComparison.Ordinal);

        var readOnlyHoverMatch = Regex.Match(css, @"\.choice-button\.read-only:hover\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(readOnlyHoverMatch.Success);
        Assert.Contains("transform: none;", readOnlyHoverMatch.Groups["rules"].Value, StringComparison.Ordinal);

        // 4. Disabled read-only state preserves subdued readable opacity
        var readOnlyDisabledMatch = Regex.Match(css, @"\.choice-button\.read-only\.disabled\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(readOnlyDisabledMatch.Success);
        Assert.Contains("opacity: 0.85;", readOnlyDisabledMatch.Groups["rules"].Value, StringComparison.Ordinal);
    }

    // =========================================================================
    // COLOR & CONTRAST HELPER METHODS
    // =========================================================================

    private static string ExtractColorVariable(string varBlock, string varName)
    {
        var match = Regex.Match(varBlock, $@"{Regex.Escape(varName)}:\s*(?<val>#[0-9a-fA-F]{{6}});");
        Assert.True(match.Success, $"CSS variable {varName} with #RRGGBB value must exist in block");
        return match.Groups["val"].Value;
    }

    private static double CalculateLuminance(string hexColor)
    {
        hexColor = hexColor.TrimStart('#');
        if (hexColor.Length != 6)
        {
            throw new ArgumentException($"Invalid hex color: #{hexColor}", nameof(hexColor));
        }

        var r = Convert.ToInt32(hexColor.Substring(0, 2), 16) / 255.0;
        var g = Convert.ToInt32(hexColor.Substring(2, 2), 16) / 255.0;
        var b = Convert.ToInt32(hexColor.Substring(4, 2), 16) / 255.0;

        var rLin = r <= 0.04045 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        var gLin = g <= 0.04045 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        var bLin = b <= 0.04045 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        return (0.2126 * rLin) + (0.7152 * gLin) + (0.0722 * bLin);
    }

    private static double CalculateContrastRatio(string hexFg, string hexBg)
    {
        var l1 = CalculateLuminance(hexFg);
        var l2 = CalculateLuminance(hexBg);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static string ExtractMediaBlock(string css, string mediaQuery)
    {
        var idx = css.IndexOf(mediaQuery, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"{mediaQuery} must exist in CSS");
        var openBrace = css.IndexOf('{', idx);
        var depth = 1;
        var current = openBrace + 1;
        while (current < css.Length && depth > 0)
        {
            if (css[current] == '{') depth++;
            else if (css[current] == '}') depth--;
            current++;
        }
        return css.Substring(openBrace + 1, current - openBrace - 2);
    }
}
