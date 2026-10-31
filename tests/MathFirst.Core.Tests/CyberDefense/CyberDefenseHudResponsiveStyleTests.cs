namespace MathFirst.Core.Tests.CyberDefense;

using System.Text.RegularExpressions;

// CSS/markup contracts only: these do not measure a browser viewport or touch targets.
public sealed class CyberDefenseHudResponsiveStyleTests
{
    [Fact]
    public void ReducedMotion_StopsTheInheritedInfiniteBossTextAnimation()
    {
        var reduced = Media(HudCss, "@media (prefers-reduced-motion: reduce)");
        Assert.Matches(@"\.wave-boss-active \.boss-wave-text[^{}]*\{[^{}]*animation:\s*none !important;", reduced);
    }

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

    // Issue #134: the name wraps (bounded to two lines, never a single-line ellipsis) and the description is bounded to
    // two whole lines. The rendered Chromium test proves shipped names are never truncated.
    [Fact]
    public void OpponentIntel_WrapsTheNameAndBoundsNameAndDescriptionToTwoWholeLines()
    {
        var css = HudCss;
        var name = Rule(css, ".scene-enemy-name");
        var description = Rule(css, ".scene-enemy-desc");
        Assert.Contains("white-space: normal;", name);
        Assert.DoesNotContain("white-space: nowrap", name);
        Assert.DoesNotContain("text-overflow: ellipsis", name);
        Assert.Contains("-webkit-line-clamp: 2;", name);
        Assert.Contains("-webkit-line-clamp: 2;", description);
        Assert.Contains("line-clamp: 2;", description);
    }

    [Fact]
    public void NarrowHud_ReservesSpaceForSectorWaveUpgradeAndRadar_WithoutGrowingOuterRows()
    {
        var css = HudCss;
        var narrow = Media(css, "@media (max-width: 600px)");
        Assert.Contains("display: none;", Rule(narrow, ".scene-category-text")); // Redundant region title only.
        // Issue #134: the artwork has its own in-flow area beside the intel at every width, never under the text.
        Assert.DoesNotContain(".scene-artwork-wrapper", narrow);
        Assert.DoesNotContain("position: absolute", Rule(css, ".scene-artwork-wrapper"));
        Assert.Contains("height: 100%;", Rule(css, ".scene-artwork-wrapper"));
        Assert.Contains("aspect-ratio: 1 / 1;", Rule(css, ".scene-artwork-wrapper"));
        Assert.Contains("display: none;", Rule(narrow, ".tactical-telemetry-list")); // Decorative, aria-hidden already.
        Assert.Contains("flex-shrink: 0;", Rule(css, ".tactical-radar-widget"));
        foreach (var essential in new[] { ".scene-sector-tag", ".scene-wave-tag", ".shield-badges-row", ".player-hp-value",
                     ".player-level-label", ".player-xp-value", ".tactical-radar-widget" })
        {
            foreach (Match match in Regex.Matches(css, Regex.Escape(essential) + @"\s*\{([^{}]*)\}"))
                Assert.DoesNotMatch(@"display:\s*none|visibility:\s*hidden|font-size:\s*0(?:;|\s)", match.Value);
        }
        Assert.Contains("height: clamp(110px, 18vh, 155px);", Rule(css, ".scene-stage"));
        Assert.DoesNotContain("flex-wrap: wrap", Rule(css, ".scene-top-bar"));
        Assert.DoesNotContain("flex-wrap: wrap", Rule(css, ".player-shield-panel"));
    }

    // Issue #134: the opponent section keeps its pre-#134 outer height (top bar + fixed stage + borders); a wrapping
    // name shrinks the stage inside it instead of moving the math workspace.
    [Fact]
    public void OpponentSection_KeepsItsOuterHeight_AndTheStageYieldsInsideIt()
    {
        var css = HudCss;
        Assert.Contains("height: calc(2.25rem + clamp(110px, 18vh, 155px) + 2px);", Rule(css, ".enemy-battle-scene"));
        Assert.Contains("flex-shrink: 0;", Rule(css, ".scene-top-bar"));
        Assert.Contains("min-height: 0;", Rule(css, ".scene-stage"));
        Assert.DoesNotMatch(@"(?:^|[;\s])(?:height|min-height):", Rule(Media(css, "@media (max-width: 600px)"), ".scene-stage"));
    }

    [Fact]
    public void HpAndShieldColumns_CanUseAvailableInlineSpace_WithoutFixedMinimumOverflow()
    {
        var css = HudCss;
        Assert.Contains("flex: 1;", Rule(css, ".shield-status-column"));
        Assert.Contains("min-width: 0;", Rule(css, ".player-hp-cluster"));
        Assert.Contains("flex-shrink: 0;", Rule(css, ".player-hp-value"));
        Assert.Contains("min-width: 0;", Rule(css, ".scene-top-actions"));
        // Issue #134: the shield row keeps the former one-line title height with or without shields, and the panel keeps
        // the radar-bound height whether or not the earned radar is shown.
        Assert.Contains("height: calc(0.725rem * 1.1);", Rule(css, ".player-status-heading"));
        Assert.Contains("min-height: calc(46px + 2 * clamp(0.5rem, 1.2vh, 0.75rem) + 2px);", Rule(css, ".player-shield-panel"));
        Assert.DoesNotContain("shield-section-title", css);
    }

    [Fact]
    public void ReducedMotion_StopsRadarShieldAndMeterTransitions_ForTheXpAndHpBars()
    {
        var css = HudCss;
        Assert.DoesNotContain("upgrade-indicator", css); // The sector HUD has no Upgrade control of its own (Issue #122).
        var reduced = Media(css, "@media (prefers-reduced-motion: reduce)");
        Assert.Contains(".radar-sweep", reduced);
        Assert.Contains(".radar-blip", reduced);
        // .meter-fill covers both the HP fill and the XP fill.
        Assert.Matches(@"\.shield-badge,\s*\.meter-fill[^{]*\{[^}]*transition:\s*none", reduced);
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
