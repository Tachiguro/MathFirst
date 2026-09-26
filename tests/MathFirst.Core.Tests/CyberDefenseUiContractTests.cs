namespace MathFirst.Core.Tests;

using System.Text.RegularExpressions;
using MathFirst.Application;
using Xunit;

public sealed class CyberDefenseUiContractTests
{
    [Fact]
    public void CyberDefenseHud_ComponentAcceptsEncounterStateAndReferencesSvgAssets()
    {
        var componentPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor");
        Assert.True(File.Exists(componentPath), "CyberDefenseHud.razor must exist.");

        var content = File.ReadAllText(componentPath);

        // Parameter acceptance
        Assert.Matches(@"\[Parameter[^\]]*\]\s*public\s+CyberDefenseEncounterState\s+State\s*\{\s*get;\s*set;\s*\}", content);

        // Exactly the three prototype SVG assets
        Assert.Contains("images/cyber-defense/glitch-drone.svg", content, StringComparison.Ordinal);
        Assert.Contains("images/cyber-defense/virus-core.svg", content, StringComparison.Ordinal);
        Assert.Contains("images/cyber-defense/crystal-malware.svg", content, StringComparison.Ordinal);

        // Accessible labels / progress semantics for HP and Shield
        Assert.Contains("role=\"progressbar\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-valuenow", content, StringComparison.Ordinal);
        Assert.Contains("aria-valuemax", content, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseHud_PreservesLearningIsolation_AndContainsNoGameEconomyScopeCreep()
    {
        var componentPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor");
        Assert.True(File.Exists(componentPath), "CyberDefenseHud.razor must exist.");

        var content = File.ReadAllText(componentPath);

        // Strictly forbidden in HUD
        string[] prohibitedTokens =
        [
            "FSRS",
            "Curriculum",
            "Sqlite",
            "ILearnerStore",
            "SubmissionChangeSet",
            "DailyStreak",
            "Achievement",
            "Notification",
            "Shop",
            "Currency",
            "Leaderboard",
            "TaskSelection"
        ];

        foreach (var token in prohibitedTokens)
        {
            Assert.DoesNotContain(token, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CyberDefenseHud_CssUsesThemeTokens_AndDoesNotHardcodeDarkOnlyPalette()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor.css");
        Assert.True(File.Exists(cssPath), "CyberDefenseHud.razor.css must exist.");

        var css = File.ReadAllText(cssPath);

        Assert.Contains("var(--color-", css, StringComparison.Ordinal);
        Assert.DoesNotContain("color-scheme: dark", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseHud_SvgOpponentAssetsExistAndAreWellFormedVectors()
    {
        string[] svgs =
        [
            "glitch-drone.svg",
            "virus-core.svg",
            "crystal-malware.svg"
        ];

        foreach (var svgName in svgs)
        {
            var path = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "images", "cyber-defense", svgName);
            Assert.True(File.Exists(path), $"SVG asset {svgName} must exist at {path}");

            var svgContent = File.ReadAllText(path);
            Assert.Contains("<svg", svgContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("</svg>", svgContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("viewBox=", svgContent, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CyberDefenseHud_LocalizationKeysExistAcrossAllSupportedLanguages()
    {
        var service = new LocalizationService();

        string[] requiredKeys =
        [
            "CyberDefense_Opponent_GlitchDrone",
            "CyberDefense_Opponent_VirusCore",
            "CyberDefense_Opponent_CrystalMalware",
            "CyberDefense_ShieldLabel",
            "CyberDefense_EnemyHpLabel"
        ];

        string[] languages = ["en", "de", "ru"];

        foreach (var lang in languages)
        {
            service.ApplyLanguagePreference(lang);
            foreach (var key in requiredKeys)
            {
                var localized = service[key];
                Assert.NotEqual(key, localized);
                Assert.False(string.IsNullOrWhiteSpace(localized), $"Key {key} for lang {lang} was empty.");
            }
        }
    }

    [Fact]
    public void HomeTrainingShell_IntegratesCyberDefenseHud_WithTransientEncounterState()
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        Assert.True(File.Exists(homePath));
        var home = File.ReadAllText(homePath);

        // Transient state instance
        Assert.Contains("CyberDefenseEncounterState", home, StringComparison.Ordinal);
        Assert.Contains("<CyberDefenseHud", home, StringComparison.Ordinal);

        // Wired reactions on accepted answer outcomes
        Assert.Contains("RecordCorrectAnswer()", home, StringComparison.Ordinal);
        Assert.Contains("RecordIncorrectAnswer()", home, StringComparison.Ordinal);

        // Settings access preserved
        Assert.Contains("href=\"settings\"", home, StringComparison.Ordinal);
        Assert.Contains("Settings_Title", home, StringComparison.Ordinal);

        // No bottom navigation bar
        Assert.DoesNotContain("<nav class=\"bottom-nav\"", home, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bottom-navigation", home, StringComparison.OrdinalIgnoreCase);

        // Core learning invariants in Home
        Assert.Contains("NumericAnswerInputPolicy", home, StringComparison.Ordinal);
        Assert.Contains("AnswerAutoSubmissionPolicy", home, StringComparison.Ordinal);
    }

    [Fact]
    public void TrainingLayout_SupportsLargeExpressions_WithoutHorizontalScrollOrKeypadOverlap()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        Assert.True(File.Exists(cssPath));
        var css = File.ReadAllText(cssPath);

        // Stable portrait row without wrapping or overflow
        Assert.Contains(".expression-row", css, StringComparison.Ordinal);
        Assert.Contains(".expression-problem", css, StringComparison.Ordinal);
        Assert.Contains(".operand", css, StringComparison.Ordinal);
        Assert.Contains(".answer-input", css, StringComparison.Ordinal);

        // Bounded clamp typography for arithmetic and min-width constraint
        Assert.Contains("clamp(", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 0", css, StringComparison.Ordinal);

        // Numeric keypad min-height / touch target contract
        Assert.Contains(".numeric-keypad-button", css, StringComparison.Ordinal);
        Assert.Contains("min-height:", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseNormalTraining_DoesNotShowVisibleTimer_WhileInternalTimingRemainsActive()
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        var timerPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Shared", "PracticeCountdownTimer.razor");
        var trainingSessionPath = GetRepositoryPath("src", "MathFirst.Application", "TrainingSession.cs");

        Assert.True(File.Exists(homePath));
        Assert.True(File.Exists(timerPath));
        Assert.True(File.Exists(trainingSessionPath));

        var home = File.ReadAllText(homePath);
        var timer = File.ReadAllText(timerPath);
        var session = File.ReadAllText(trainingSessionPath);

        // Normal encounters pass IsVisible="false" to timer component
        Assert.Contains("IsVisible=\"false\"", home, StringComparison.Ordinal);

        // Timer component supports IsVisible and uses hidden styling when false
        Assert.Contains("public bool IsVisible { get; set; } = true;", timer, StringComparison.Ordinal);
        Assert.Contains("timer-hidden", timer, StringComparison.Ordinal);
        Assert.Contains("display: none;", timer, StringComparison.Ordinal);

        // Timer loop and timeout notification remain active
        Assert.Contains("PeriodicTimer", timer, StringComparison.Ordinal);
        Assert.Contains("OnTimeout.InvokeAsync()", timer, StringComparison.Ordinal);

        // TrainingSession internal latency measurement and pacing remain completely intact
        Assert.Contains("GetCurrentItemElapsed()", session, StringComparison.Ordinal);
        Assert.Contains("LastResponseLatencyMs", session, StringComparison.Ordinal);
        Assert.Contains("AdaptivePacePolicy", session, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseEnemyScene_ComponentExistsAndIsNotOldTinyIcon()
    {
        var hudPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor");
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor.css");

        Assert.True(File.Exists(hudPath));
        Assert.True(File.Exists(cssPath));

        var hud = File.ReadAllText(hudPath);
        var css = File.ReadAllText(cssPath);

        // Scene composition elements
        Assert.Contains("enemy-battle-scene", hud, StringComparison.Ordinal);
        Assert.Contains("scene-top-bar", hud, StringComparison.Ordinal);
        Assert.Contains("scene-stage", hud, StringComparison.Ordinal);
        Assert.Contains("scene-intel", hud, StringComparison.Ordinal);
        Assert.Contains("scene-enemy-name", hud, StringComparison.Ordinal);
        Assert.Contains("scene-artwork-wrapper", hud, StringComparison.Ordinal);
        Assert.Contains("scene-opponent-image", hud, StringComparison.Ordinal);

        // Atmospheric environment layers in CSS
        Assert.Contains(".scene-skyline", css, StringComparison.Ordinal);
        Assert.Contains(".scene-grid-floor", css, StringComparison.Ordinal);
        Assert.Contains(".scene-particles", css, StringComparison.Ordinal);
        Assert.Contains(".scene-artwork-wrapper", css, StringComparison.Ordinal);

        // Old tiny avatar wrapper is replaced
        Assert.DoesNotContain("opponent-avatar-wrapper", hud, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseShieldRegion_ExistsWithExplicitBadgesRadarAndBossTimerSlot()
    {
        var hudPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor");
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor.css");

        Assert.True(File.Exists(hudPath));
        Assert.True(File.Exists(cssPath));

        var hud = File.ReadAllText(hudPath);
        var css = File.ReadAllText(cssPath);

        // Explicit shield badges and count
        Assert.Contains("player-shield-panel", hud, StringComparison.Ordinal);
        Assert.Contains("shield-badges-row", hud, StringComparison.Ordinal);
        Assert.Contains("shield-badge", hud, StringComparison.Ordinal);
        Assert.Contains("shield-icon", hud, StringComparison.Ordinal);

        // Tactical radar & telemetry
        Assert.Contains("tactical-radar-widget", hud, StringComparison.Ordinal);
        Assert.Contains("tactical-telemetry-list", hud, StringComparison.Ordinal);

        // Reserved boss timer hook
        Assert.Contains("boss-timer-slot", hud, StringComparison.Ordinal);

        // CSS contains badge and radar styles
        Assert.Contains(".player-shield-panel", css, StringComparison.Ordinal);
        Assert.Contains(".shield-intact", css, StringComparison.Ordinal);
        Assert.Contains(".shield-broken", css, StringComparison.Ordinal);
        Assert.Contains(".tactical-radar-widget", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseSolveToAttackAndOperationRow_ExistInTrainingShell()
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");

        Assert.True(File.Exists(homePath));
        Assert.True(File.Exists(cssPath));

        var home = File.ReadAllText(homePath);
        var css = File.ReadAllText(cssPath);

        // Solve to Attack cyber frame
        Assert.Contains("solve-to-attack-panel", home, StringComparison.Ordinal);
        Assert.Contains("CyberDefense_SolveToAttack", home, StringComparison.Ordinal);
        Assert.Contains(".solve-to-attack-panel", css, StringComparison.Ordinal);
        Assert.Contains(".solve-panel-header", css, StringComparison.Ordinal);

        // Operation state row below keypad
        Assert.Contains("operation-unlock-area", home, StringComparison.Ordinal);
        Assert.Contains("operation-unlock-goal", home, StringComparison.Ordinal);
        Assert.Contains("operation-progress-hud", home, StringComparison.Ordinal);
        Assert.Contains(".operation-unlock-area", css, StringComparison.Ordinal);
        Assert.Contains(".operation-unlock-goal", css, StringComparison.Ordinal);

        // Brand shield emblem and subtitle in header
        Assert.Contains("brand-shield-emblem", home, StringComparison.Ordinal);
        Assert.Contains("CyberDefense_Header_Subtitle", home, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseTheme_SupportsBothLightAndDarkTokenSets()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        Assert.True(File.Exists(cssPath));

        var css = File.ReadAllText(cssPath);

        // Light theme baseline
        Assert.Contains(".app-theme-root[data-theme=\"light\"]", css, StringComparison.Ordinal);

        // Dark theme baseline with luminous cyber accents
        Assert.Contains(":root[data-theme=\"dark\"]", css, StringComparison.Ordinal);
        Assert.Contains(".app-theme-root[data-theme=\"dark\"]", css, StringComparison.Ordinal);

        // Both themes configure primary, background, surface, danger
        Assert.Contains("--color-primary: #1be3a9;", css, StringComparison.Ordinal);
        Assert.Contains("--color-background: #081310;", css, StringComparison.Ordinal);
        Assert.Contains("--color-danger: #ff4766;", css, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
