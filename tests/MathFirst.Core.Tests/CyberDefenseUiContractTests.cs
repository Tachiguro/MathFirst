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
            "CyberDefense_EnemyHpLabel",
            "CyberDefense_OperationsTitle"
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
        Assert.Contains("CyberDefense_OperationsTitle", home, StringComparison.Ordinal);
        Assert.DoesNotContain("CyberDefense_NextGoal", home, StringComparison.Ordinal);

        // Brand shield emblem and subtitle in header
        Assert.Contains("brand-shield-emblem", home, StringComparison.Ordinal);
        Assert.Contains("CyberDefense_Header_Subtitle", home, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseOperationPanel_DoesNotExposeFabricatedProgressOrUnbackedUnlockPredictions()
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        Assert.True(File.Exists(homePath));
        var home = File.ReadAllText(homePath);

        // Disallow fake next-goal claims and fixed progress bars
        Assert.DoesNotContain("CyberDefense_NextGoal", home, StringComparison.Ordinal);
        Assert.DoesNotContain("almost unlocked", home, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fast freigeschaltet", home, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("почти открыто", home, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("goal-progress-bar", home, StringComparison.Ordinal);
        Assert.DoesNotContain("goal-segment", home, StringComparison.Ordinal);

        // Ensure truthful operations title and real OperationProgress binding
        Assert.Contains("CyberDefense_OperationsTitle", home, StringComparison.Ordinal);
        Assert.Contains("OperationProgress", home, StringComparison.Ordinal);
        Assert.Contains("progress.PresentationStage", home, StringComparison.Ordinal);
        Assert.Contains("op-current", home, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizationService_DoesNotContainFabricatedCyberDefenseNextGoalStrings()
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var english = (Dictionary<string, string>)typeof(LocalizationService).GetField("EnglishStrings", flags)!.GetValue(null)!;
        var german = (Dictionary<string, string>)typeof(LocalizationService).GetField("GermanStrings", flags)!.GetValue(null)!;
        var russian = (Dictionary<string, string>)typeof(LocalizationService).GetField("RussianStrings", flags)!.GetValue(null)!;

        Assert.False(english.ContainsKey("CyberDefense_NextGoal"), "CyberDefense_NextGoal must not exist in English.");
        Assert.False(german.ContainsKey("CyberDefense_NextGoal"), "CyberDefense_NextGoal must not exist in German.");
        Assert.False(russian.ContainsKey("CyberDefense_NextGoal"), "CyberDefense_NextGoal must not exist in Russian.");

        Assert.Equal("OPERATIONS", english["CyberDefense_OperationsTitle"]);
        Assert.Equal("RECHENARTEN", german["CyberDefense_OperationsTitle"]);
        Assert.Equal("ОПЕРАЦИИ", russian["CyberDefense_OperationsTitle"]);
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

    [Fact]
    public void CyberDefenseCombo_UsesCyberDefenseSpecificLocalizationAcrossLanguages()
    {
        var service = new LocalizationService();

        service.ApplyLanguagePreference("de");
        Assert.Equal("KOMBO ×3", service.GetString("CyberDefense_Combo", 3));
        Assert.Equal("KOMBO ×100", service.GetString("CyberDefense_Combo", 100));

        service.ApplyLanguagePreference("en");
        Assert.Equal("COMBO ×3", service.GetString("CyberDefense_Combo", 3));
        Assert.Equal("COMBO ×100", service.GetString("CyberDefense_Combo", 100));

        service.ApplyLanguagePreference("ru");
        Assert.Equal("КОМБО ×3", service.GetString("CyberDefense_Combo", 3));
        Assert.Equal("КОМБО ×100", service.GetString("CyberDefense_Combo", 100));
    }

    [Fact]
    public void CyberDefenseCombo_LayoutIsStructurallyReserved_WithoutVerticalShift()
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");

        var home = File.ReadAllText(homePath);
        var css = File.ReadAllText(cssPath);

        // Reserved combo slot is always in the DOM
        Assert.Contains("cyber-combo-slot", home, StringComparison.Ordinal);
        Assert.Contains("cyber-combo-badge", home, StringComparison.Ordinal);
        Assert.Contains("combo-active", home, StringComparison.Ordinal);
        Assert.Contains("combo-inactive", home, StringComparison.Ordinal);

        // Fixed heights ensuring identical geometry
        Assert.Contains(".solve-panel-header", css, StringComparison.Ordinal);
        Assert.Contains(".cyber-combo-slot", css, StringComparison.Ordinal);
        Assert.Contains(".cyber-combo-badge.combo-inactive", css, StringComparison.Ordinal);
        Assert.Contains("visibility: hidden;", css, StringComparison.Ordinal);
        Assert.Contains(".practice-middle-region", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseExpressionSizingPolicy_ReturnsDeterministicClasses_AcrossTargetComplexity()
    {
        // 0 + 0 = ? -> short
        Assert.Equal(
            MathFirst.Application.Practice.CyberDefenseExpressionSizingPolicy.ShortClass,
            MathFirst.Application.Practice.CyberDefenseExpressionSizingPolicy.GetSizeClass("0", "0", 1));

        // 7 + 8 = ? -> short
        Assert.Equal(
            MathFirst.Application.Practice.CyberDefenseExpressionSizingPolicy.ShortClass,
            MathFirst.Application.Practice.CyberDefenseExpressionSizingPolicy.GetSizeClass("7", "8", 2));

        // 12 × 12 = ? -> medium
        Assert.Equal(
            MathFirst.Application.Practice.CyberDefenseExpressionSizingPolicy.MediumClass,
            MathFirst.Application.Practice.CyberDefenseExpressionSizingPolicy.GetSizeClass("12", "12", 3));

        // 99 × 99 = ? -> long
        Assert.Equal(
            MathFirst.Application.Practice.CyberDefenseExpressionSizingPolicy.LongClass,
            MathFirst.Application.Practice.CyberDefenseExpressionSizingPolicy.GetSizeClass("99", "99", 4));

        // 999 + 999 = ? (and answer 1998) -> xlong
        Assert.Equal(
            MathFirst.Application.Practice.CyberDefenseExpressionSizingPolicy.XLongClass,
            MathFirst.Application.Practice.CyberDefenseExpressionSizingPolicy.GetSizeClass("999", "999", 4));
    }

    [Fact]
    public void CyberDefenseExpressionTypography_ShortClassIsLargest_AndLongClassesScaleDown()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        var css = File.ReadAllText(cssPath);

        Assert.Contains(".expression-row.expression-short", css, StringComparison.Ordinal);
        Assert.Contains(".expression-row.expression-medium", css, StringComparison.Ordinal);
        Assert.Contains(".expression-row.expression-long", css, StringComparison.Ordinal);
        Assert.Contains(".expression-row.expression-xlong", css, StringComparison.Ordinal);

        // Short class uses 4.25rem base, medium uses 3.0rem, long uses 2.4rem, xlong uses 1.85rem
        Assert.Contains("4.25rem", css, StringComparison.Ordinal);
        Assert.Contains("3.0rem", css, StringComparison.Ordinal);
        Assert.Contains("2.4rem", css, StringComparison.Ordinal);
        Assert.Contains("1.85rem", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseAnswerInput_UsesCultureSafeLengthClasses_WithoutUnusedBlankSpace()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        var css = File.ReadAllText(cssPath);
        var home = File.ReadAllText(homePath);

        // Home.razor applies GetAnswerLengthClass dynamically to the answer input
        Assert.Contains("class=\"answer-input @GetAnswerLengthClass()\"", home, StringComparison.Ordinal);
        Assert.Contains("GetAnswerLengthClass", home, StringComparison.Ordinal);

        // Deterministic integer-based width classes defined in CSS
        for (var i = 1; i <= 6; i++)
        {
            Assert.Contains($".answer-input.answer-len-{i}", css, StringComparison.Ordinal);
            Assert.Contains($"{i}.15ch", css, StringComparison.Ordinal);
        }

        // Answer input centers glyphs and uses tabular numbers to guarantee optical balance
        Assert.Contains("font-variant-numeric: tabular-nums;", css, StringComparison.Ordinal);
        Assert.Contains("text-align: center;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseEquationLayout_IsCenteredAsOneUnifiedGroup()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        var css = File.ReadAllText(cssPath);

        // Expression row and problem are flex containers centering children
        Assert.Contains(".expression-row", css, StringComparison.Ordinal);
        Assert.Contains("display: flex;", css, StringComparison.Ordinal);
        Assert.Contains("align-items: center;", css, StringComparison.Ordinal);
        Assert.Contains("justify-content: center;", css, StringComparison.Ordinal);

        // Mobile portrait has maximized font size for young children
        Assert.Contains("clamp(4.25rem, 16vw + 0.8vh, 4.85rem)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefenseBattleFeedback_OnlyTriggersAfterEvaluation_AndPreservesLearningEngineAuthority()
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        var hudPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor");

        var home = File.ReadAllText(homePath);
        var hud = File.ReadAllText(hudPath);

        // Feedback triggers only after Session.SubmitAnswer
        var evalIdx = home.IndexOf("Session.SubmitAnswer", StringComparison.Ordinal);
        var correctRecordIdx = home.IndexOf("_cyberDefenseState.RecordCorrectAnswer()", StringComparison.Ordinal);
        var incorrectRecordIdx = home.IndexOf("_cyberDefenseState.RecordIncorrectAnswer()", StringComparison.Ordinal);

        Assert.True(evalIdx > 0, "SubmitAnswer must be called.");
        Assert.True(correctRecordIdx > evalIdx, "RecordCorrectAnswer must occur AFTER evaluation.");
        Assert.True(incorrectRecordIdx > evalIdx, "RecordIncorrectAnswer must occur AFTER evaluation.");

        // Feedback overlays in HUD
        Assert.Contains("hit-feedback-overlay", hud, StringComparison.Ordinal);
        Assert.Contains("blocked-feedback-overlay", hud, StringComparison.Ordinal);
        Assert.Contains("CyberDefenseFeedbackKind", hud, StringComparison.Ordinal);
    }

    [Fact]
    public void CyberDefense_PreservesDomainPersistenceIsolation()
    {
        var statePath = GetRepositoryPath("src", "MathFirst.Application", "Practice", "CyberDefenseEncounterState.cs");
        var stateCode = File.ReadAllText(statePath);

        // No database, EF, SQLite, or persistence imports
        Assert.DoesNotContain("Microsoft.Data.Sqlite", stateCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EntityFramework", stateCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ILearnerStore", stateCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Save", stateCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Commit", stateCode, StringComparison.OrdinalIgnoreCase);
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
