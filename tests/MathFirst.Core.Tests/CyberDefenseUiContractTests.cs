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

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
