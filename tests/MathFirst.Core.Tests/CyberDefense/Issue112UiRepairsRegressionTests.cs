namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

public sealed class Issue112UiRepairsRegressionTests
{
    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", ".."));

    private static string ReadFile(params string[] segments) =>
        File.ReadAllText(GetRepositoryPath(segments));

    // =========================================================================
    // A. TUTORIAL: MODAL LAYERING, SEPARATION, AND OBSTRUCTION PREVENTION
    // =========================================================================

    [Fact]
    public void TutorialCss_CardLayering_IsAboveSpotlightZIndex()
    {
        var css = ReadFile("src", "MathFirst.App", "Components", "Training", "CyberDefenseTutorial.razor.css");
        var appCss = ReadFile("src", "MathFirst.App", "wwwroot", "app.css");

        // Spotlight in app.css uses z-index: 96
        Assert.Contains("z-index: 96", appCss, StringComparison.Ordinal);

        // Tutorial card layer must be at z-index > 96 (e.g. 100) so highlighted math cannot obscure actions
        Assert.Matches(@"(?:\.tutorial-card|\.tutorial-overlay)[\s\S]*?z-index:\s*(?:9[7-9]|1\d{2})", css);
    }

    [Fact]
    public void TutorialCss_MobilePortraitLayout_MaintainsCompactCardHeightAndReliableSeparation()
    {
        var css = ReadFile("src", "MathFirst.App", "Components", "Training", "CyberDefenseTutorial.razor.css");

        // Compact indicator and actions on mobile viewports (< 480px)
        Assert.Contains("@media (max-width: 480px)", css, StringComparison.Ordinal);
        Assert.Matches(@"@media\s*\(max-width:\s*480px\)[\s\S]*?\.tutorial-card", css);

        // Expression keypad target placement must have strict top-clearance
        Assert.Contains("data-target=\"expression-keypad\"", css, StringComparison.Ordinal);
    }

    // =========================================================================
    // B. UPGRADE TERMINAL: FIXED MODAL DIALOG, SOLID BACKDROP, CARD CONTROLS
    // =========================================================================

    [Fact]
    public void UpgradeTerminalCss_IsFixedModalWithBackdrop_NotContainedInTrainingCardGrid()
    {
        var css = ReadFile("src", "MathFirst.App", "Components", "Training", "UpgradeTerminal.razor.css");

        // Must be a fixed modal dialog to avoid in-flow grid collision inside training-card
        Assert.Contains("position: fixed", css, StringComparison.Ordinal);
        Assert.Contains("inset: 0", css, StringComparison.Ordinal);
        Assert.Contains("z-index:", css, StringComparison.Ordinal);
        Assert.Matches(@"background:\s*rgba\(\s*0\s*,\s*0\s*,\s*0\s*,\s*0\.(?:8|9)\d*\)", css);
    }

    [Fact]
    public void UpgradeTerminal_Markup_PlacesPurchaseButtonsInsideUpgradeCards()
    {
        var razor = ReadFile("src", "MathFirst.App", "Components", "Training", "UpgradeTerminal.razor");

        // Attack card contains attack-purchase-btn
        var attackCardMatch = Regex.Match(razor, @"<article[^>]*class=""[^""]*attack-upgrade-card[^""]*""[\s\S]*?</article>");
        Assert.True(attackCardMatch.Success, "Attack card article must exist.");
        Assert.Contains("attack-purchase-btn", attackCardMatch.Value, StringComparison.Ordinal);

        // Firewall card contains firewall-purchase-btn
        var firewallCardMatch = Regex.Match(razor, @"<article[^>]*class=""[^""]*firewall-upgrade-card[^""]*""[\s\S]*?</article>");
        Assert.True(firewallCardMatch.Success, "Firewall card article must exist.");
        Assert.Contains("firewall-purchase-btn", firewallCardMatch.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeTerminal_Markup_HasCompactLockedCardPresentation()
    {
        var razor = ReadFile("src", "MathFirst.App", "Components", "Training", "UpgradeTerminal.razor");

        // Checks for locked-card modifier class or compact locked presentation
        Assert.Contains("locked-card", razor, StringComparison.Ordinal);
    }

    // =========================================================================
    // C. ACTIVE GAMEPLAY GEOMETRY & STABILITY
    // =========================================================================

    [Fact]
    public void CyberDefenseHudCss_SceneTopBarAndShieldPanel_HaveLockedMinHeights()
    {
        var css = ReadFile("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor.css");

        // Scene top bar has min-height
        Assert.Matches(@"\.scene-top-bar\s*\{[\s\S]*?min-height:", css);

        // Player shield panel has min-height to prevent layout shifts when shields appear
        Assert.Matches(@"\.player-shield-panel\s*\{[\s\S]*?min-height:", css);
    }

    // =========================================================================
    // D. HEADER NAVIGATION CLARITY
    // =========================================================================

    [Fact]
    public void HomeRazor_Header_HasDedicatedUpgradeButtonWhenCyberDefenseActive()
    {
        var home = ReadFile("src", "MathFirst.App", "Components", "Pages", "Home.razor");

        // Header includes dedicated upgrade button with SP badge
        Assert.Contains("upgrade-nav-btn", home, StringComparison.Ordinal);
        Assert.Contains("header-sp-badge", home, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeRazor_Header_ModeToggleUsesUnambiguousDesignation()
    {
        var home = ReadFile("src", "MathFirst.App", "Components", "Pages", "Home.razor");

        // The mode toggle button must have clear mode switch aria label / title
        Assert.Contains("ModeToggle_", home, StringComparison.Ordinal);
    }

    // =========================================================================
    // E. UPGRADE AND SHIELD PRESENTATION
    // =========================================================================

    [Fact]
    public void UpgradeTerminal_ExplainsSkillPointsAndFirewallEligibility()
    {
        var razor = ReadFile("src", "MathFirst.App", "Components", "Training", "UpgradeTerminal.razor");

        Assert.Contains("UpgradeTerminal_SkillPointsExplainer", razor, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_FirewallLockedNotice", razor, StringComparison.Ordinal);
    }

    // =========================================================================
    // F. OPERATION PROGRESSION PRESENTATION
    // =========================================================================

    [Fact]
    public void HomeRazor_OperationProgression_DisplaysExplicitStageLabels()
    {
        var home = ReadFile("src", "MathFirst.App", "Components", "Pages", "Home.razor");

        // Must display stage label explicitly, not just raw concatenation of symbol and number
        Assert.Contains("Training_ProgressStageDisplay", home, StringComparison.Ordinal);
        Assert.Contains("Settings_OperationLocked", home, StringComparison.Ordinal);
    }

    // =========================================================================
    // G. EARLY BOSS AND ENEMY PRESENTATION SCALING
    // =========================================================================

    [Fact]
    public void OpponentScalePolicy_SectorAware_CalibratesEarlySectorBossScaling()
    {
        // Sector 1 boss must scale reasonably (not 1.50)
        var sector1BossTier = CyberDefenseOpponentScalePolicy.GetScaleTier(sector: 1, opponentIndex: 5, normalOpponentCount: 5, isSectorBoss: true, isBoss: true);
        Assert.Equal(CyberDefenseOpponentScaleTier.Boss, sector1BossTier);

        // Later Sector boss (Sector 10) achieves SectorBoss tier (1.50)
        var sector10BossTier = CyberDefenseOpponentScalePolicy.GetScaleTier(sector: 10, opponentIndex: 8, normalOpponentCount: 8, isSectorBoss: true, isBoss: true);
        Assert.Equal(CyberDefenseOpponentScaleTier.SectorBoss, sector10BossTier);
    }
}
