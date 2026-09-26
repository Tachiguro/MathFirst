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

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
