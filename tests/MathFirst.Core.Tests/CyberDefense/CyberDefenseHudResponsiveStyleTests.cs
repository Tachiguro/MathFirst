namespace MathFirst.Core.Tests.CyberDefense;

using System.Text.RegularExpressions;

// CSS/markup contracts only: these do not measure a browser viewport or touch targets.
public sealed class CyberDefenseHudResponsiveStyleTests
{
    private static string ReadSource(params string[] parts) => File.ReadAllText(Path.Combine(
        Path.GetDirectoryName(SourcePath())!, "..", "..", "..", "src", "MathFirst.App", Path.Combine(parts)));

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
    private static string HudCss => ReadSource("Components", "Training", "CyberDefenseHud.razor.css");

    private static string Rule(string css, string selector) => Regex.Match(css,
        Regex.Escape(selector) + @"\s*\{([^{}]*)\}").Groups[1].Value;

    private static string Media(string css, string query)
    {
        var start = css.IndexOf(query, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing responsive rule: {query}");
        var open = css.IndexOf('{', start);
        var depth = 1;
        for (var i = open + 1; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            if (css[i] == '}' && --depth == 0) return css[(open + 1)..i];
        }
        throw new InvalidOperationException($"Unclosed media block: {query}");
    }

    [Fact]
    public void OpponentIntel_DoesNotEllipsizeOrLineClampEssentialText()
    {
        var css = HudCss;
        var name = Rule(css, ".scene-enemy-name");
        var description = Rule(css, ".scene-enemy-desc");
        Assert.Contains("white-space: normal;", name);
        Assert.DoesNotContain("overflow: hidden", name);
        Assert.DoesNotContain("text-overflow: ellipsis", name);
        Assert.DoesNotContain("line-clamp", description);
        Assert.DoesNotContain("overflow: hidden", description);
    }

    [Fact]
    public void NarrowHud_ReservesSpaceForSectorWaveUpgradeAndRadar_WithoutGrowingOuterRows()
    {
        var css = HudCss;
        var narrow = Media(css, "@media (max-width: 600px)");
        Assert.Contains("display: none;", Rule(narrow, ".scene-category-text")); // Redundant region title only.
        Assert.Contains("position: absolute;", Rule(narrow, ".scene-artwork-wrapper"));
        Assert.Contains("width: 100%;", Rule(narrow, ".scene-intel"));
        Assert.Contains("display: none;", Rule(narrow, ".tactical-telemetry-list")); // Decorative, aria-hidden already.
        Assert.Contains("flex-shrink: 0;", Rule(css, ".tactical-radar-widget"));
        foreach (var essential in new[] { ".scene-sector-tag", ".scene-wave-tag", ".upgrade-indicator-badge",
                     ".upgrade-indicator-text", ".shield-badges-row", ".player-hp-value", ".tactical-radar-widget" })
        {
            foreach (Match match in Regex.Matches(css, Regex.Escape(essential) + @"\s*\{([^{}]*)\}"))
                Assert.DoesNotMatch(@"display:\s*none|visibility:\s*hidden|font-size:\s*0(?:;|\s)", match.Value);
        }
        Assert.Contains("height: clamp(110px, 18vh, 155px);", Rule(css, ".scene-stage"));
        Assert.DoesNotContain("flex-wrap: wrap", Rule(css, ".scene-top-bar"));
        Assert.DoesNotContain("flex-wrap: wrap", Rule(css, ".player-shield-panel"));
    }

    [Fact]
    public void NarrowIntel_ReclaimsInternalPadding_WithinExistingFixedStageHeight()
    {
        var narrow = Media(HudCss, "@media (max-width: 600px)");
        Assert.Contains("padding-block: 0.25rem;", Rule(narrow, ".scene-stage"));
        Assert.Contains("padding-block: 0;", Rule(narrow, ".scene-intel"));
        Assert.Contains("gap: 0.125rem;", Rule(narrow, ".scene-intel"));
        Assert.Contains("line-height: 1.1;", Rule(narrow, ".enemy-hp-value"));
        Assert.DoesNotMatch(@"(?:^|[;\s])(?:height|min-height):", Rule(narrow, ".scene-stage"));
    }

    [Fact]
    public void HpAndShieldColumns_CanUseAvailableInlineSpace_WithoutFixedMinimumOverflow()
    {
        var css = HudCss;
        Assert.Contains("flex: 1;", Rule(css, ".shield-status-column"));
        Assert.Contains("min-width: 0;", Rule(css, ".player-hp-cluster"));
        Assert.Contains("white-space: nowrap;", Rule(css, ".shield-section-title"));
        Assert.Contains("flex-shrink: 0;", Rule(css, ".player-hp-value"));
        Assert.Contains("min-width: 0;", Rule(css, ".scene-top-actions"));
    }

    [Fact]
    public void UpgradeFocus_UsesThemeRing_AndReducedMotionStopsRadarAndControlTransitions()
    {
        var css = HudCss;
        var focus = Rule(css, ".upgrade-indicator-badge:focus-visible");
        Assert.Contains("outline: 2px solid var(--color-primary);", focus);
        Assert.Contains("outline-offset: 2px;", focus);
        var reduced = Media(css, "@media (prefers-reduced-motion: reduce)");
        Assert.Contains(".radar-sweep", reduced);
        Assert.Contains(".radar-blip", reduced);
        Assert.Matches(@"\.upgrade-indicator-badge[^{]*\{[^}]*transition:\s*none", reduced);
        var theme = ReadSource("wwwroot", "app.css");
        Assert.Contains(".app-theme-root[data-theme=\"light\"]", theme);
        Assert.Contains(".app-theme-root[data-theme=\"dark\"]", theme);
    }

    [Fact]
    public void HudStyles_CannotReachProtectedMathControlsOrTheirLayoutAncestors()
    {
        var css = HudCss;
        Assert.DoesNotContain("::deep", css);
        Assert.DoesNotContain(":global", css);
        Assert.DoesNotMatch(@"\.(?:training-|practice-|expression-|answer-input|solve-to-attack-panel|numeric-keypad|keypad-grid|operation-progress-hud)", css);
        var hud = ReadSource("Components", "Training", "CyberDefenseHud.razor");
        Assert.DoesNotContain("<style", hud);
        Assert.DoesNotContain("<script", hud);
        var math = ReadSource("wwwroot", "app.css");
        Assert.Contains("grid-template-rows: auto minmax(0, 1fr);", Rule(math, ".training-host.active-gameplay .training-card"));
        Assert.Contains("grid-template-rows: repeat(4, minmax(2.75rem, 1fr));", Rule(math, ".numeric-keypad"));
        Assert.Contains("flex-shrink: 0;", Rule(math, ".numeric-keypad"));
        Assert.Contains("grid-template-rows: minmax(0, 1fr);", Rule(math, ".training-card.active-gameplay.calm-mode"));
        // Portrait button minimum alone is not the effective target: the grid also imposes a row minimum.
        var portrait = Media(math, "@media (orientation: portrait) and (max-width: 480px)");
        Assert.Contains("min-height: 2.5rem;", Rule(portrait, ".numeric-keypad-button"));
    }
}
