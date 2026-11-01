namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
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
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

public sealed class Issue112UiRepairsRegressionTests : IDisposable
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
    public void TutorialCss_SpotlightThreeLayerStacking_DimmerUnderTarget_TargetVisiblyHighlighted_CardOnTop()
    {
        var css = ReadFile("src", "MathFirst.App", "Components", "Training", "CyberDefenseTutorial.razor.css");
        var appCss = ReadFile("src", "MathFirst.App", "wwwroot", "app.css");

        // 1. Dark scrim/backdrop is at z-index: 90 (below spotlight target 96, above normal gameplay at z-index: 0)
        Assert.Matches(@"\.tutorial-backdrop\s*\{[\s\S]*?z-index:\s*90\b", css);
        Assert.Matches(@"\.tutorial-backdrop\s*\{[\s\S]*?background:\s*rgba\(\s*0\s*,\s*0\s*,\s*0\s*,\s*0\.75\)", css);
        Assert.Matches(@"\.tutorial-backdrop\s*\{[\s\S]*?backdrop-filter:\s*blur\(3px\)", css);

        // 2. Active spotlight target is at z-index: 96 in app.css (above backdrop 90)
        Assert.Contains("z-index: 96", appCss, StringComparison.Ordinal);

        // 3. Tutorial overlay container is at z-index: 100 with transparent background so it does not obscure or darken the target
        Assert.Matches(@"\.tutorial-overlay\s*\{[\s\S]*?z-index:\s*100\b", css);
        Assert.Matches(@"\.tutorial-overlay\s*\{[\s\S]*?background:\s*transparent\b", css);
        Assert.Matches(@"\.tutorial-overlay\s*\{[\s\S]*?pointer-events:\s*none\b", css);

        // 4. Tutorial card dialog is at z-index: 101 with pointer-events: auto (above spotlight target 96)
        Assert.Matches(@"\.tutorial-card\s*\{[\s\S]*?z-index:\s*101\b", css);
        Assert.Matches(@"\.tutorial-card\s*\{[\s\S]*?pointer-events:\s*auto\b", css);
    }

    [Fact]
    public void TutorialCss_CardLayout_ProvidesContentSafeInternalScrollLayout()
    {
        var css = ReadFile("src", "MathFirst.App", "Components", "Training", "CyberDefenseTutorial.razor.css");

        // Card container prevents uncontained overflow and respects viewport bounds
        Assert.Matches(@"\.tutorial-card\s*\{[\s\S]*?overflow:\s*hidden\b", css);
        Assert.Matches(@"\.tutorial-card\s*\{[\s\S]*?max-height:\s*min\(85vh,\s*85dvh\)", css);

        // Header and actions are pinned (flex-shrink: 0) so controls remain visible and accessible
        Assert.Matches(@"\.tutorial-card-header\s*\{[\s\S]*?flex-shrink:\s*0\b", css);
        Assert.Matches(@"\.tutorial-card-actions\s*\{[\s\S]*?flex-shrink:\s*0\b", css);

        // Card body has flex-grow/shrink and overflow-y: auto for content-safe scrolling
        Assert.Matches(@"\.tutorial-card-body\s*\{[\s\S]*?overflow-y:\s*auto\b", css);
        Assert.Matches(@"\.tutorial-card-body\s*\{[\s\S]*?min-height:\s*0\b", css);
        Assert.Matches(@"\.tutorial-card-body\s*\{[\s\S]*?flex:\s*1\s+1\s+auto\b", css);

        // Expression keypad step uses responsive max-height rather than a fixed rigid overflow trap
        Assert.Matches(@"\.tutorial-overlay\[data-target=""expression-keypad""\]\s*\.tutorial-card\s*\{[\s\S]*?max-height:\s*min\(280px,\s*46vh\)", css);

        // Compact landscape/short-screen overrides preserve clearance
        Assert.Contains("@media (max-height: 480px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 480px)", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", 0)]
    [InlineData("en", 1)]
    [InlineData("en", 2)]
    [InlineData("en", 3)]
    [InlineData("de", 2)]
    [InlineData("ru", 2)]
    public async Task TutorialComponent_RenderedMarkup_RendersBackdropAndAccessibleModalDialogAcrossAllSteps(string lang, int step)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(lang);

        var html = await RenderTutorialComponentAsync(step, localizer);

        // Both backdrop and dialog overlay must be rendered as siblings
        Assert.Contains("class=\"tutorial-backdrop\"", html);
        Assert.Contains("aria-hidden=\"true\"", html);

        Assert.Contains("class=\"tutorial-overlay\"", html);
        Assert.Contains("role=\"dialog\"", html);
        Assert.Contains("aria-modal=\"true\"", html);
        Assert.Contains("class=\"tutorial-card\"", html);

        // Header, body, and actions must be present
        Assert.Contains("class=\"tutorial-card-header\"", html);
        Assert.Contains("class=\"tutorial-card-body\"", html);
        Assert.Contains("class=\"tutorial-card-actions\"", html);
        Assert.Contains("tutorial-btn-skip", html);

        if (step > 0)
        {
            Assert.Contains("tutorial-btn-back", html);
        }

        if (step == 3)
        {
            Assert.Contains("tutorial-btn-finish", html);
        }
        else
        {
            Assert.Contains("tutorial-btn-next", html);
        }
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
    public void HomeRazor_Header_HasNoDuplicateUpgradeButton_SinceIssue139()
    {
        var home = ReadFile("src", "MathFirst.App", "Components", "Pages", "Home.razor");

        // Header includes dedicated upgrade button with SP badge
        Assert.DoesNotContain("upgrade-nav-btn", home, StringComparison.Ordinal);
        Assert.DoesNotContain("header-sp-badge", home, StringComparison.Ordinal);
    }

    [Fact]
    public void AppCss_NarrowViewports_NeverHideUpgradeLabel()
    {
        // Static guard only; rendered geometry was measured separately in a Chromium viewport harness (see PR #113).
        var css = ReadFile("src", "MathFirst.App", "wwwroot", "app.css");

        Assert.DoesNotMatch(@"\.upgrade-nav-text\s*\{[^}]*display:\s*none", css);
        Assert.DoesNotMatch(@"\.upgrade-nav-btn\s+\.upgrade-nav-text\s*\{[^}]*display:\s*none", css);
        Assert.Matches(@"@media \(max-width: 480px\)\s*\{\s*\.upgrade-nav-btn\s*\{[^}]*font-size:", css);
    }

    [Fact]
    public void HomeRazor_Header_HasNoModeToggle_SinceIssue122()
    {
        var home = ReadFile("src", "MathFirst.App", "Components", "Pages", "Home.razor");

        // The Cyber Defense / Calm mode lives in Settings; the gameplay header carries no ambiguous toggle.
        Assert.DoesNotContain("ModeToggle_", home, StringComparison.Ordinal);
        Assert.DoesNotContain("mode-toggle", home, StringComparison.Ordinal);
    }

    // =========================================================================
    // E. UPGRADE AND SHIELD PRESENTATION
    // =========================================================================

    [Fact]
    public void UpgradeTerminal_ExplainsSkillPointsAndFirewallEligibility()
    {
        var razor = ReadFile("src", "MathFirst.App", "Components", "Training", "UpgradeTerminal.razor");

        Assert.Contains("UpgradeTerminal_SkillPointsExplainer", razor, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_FirewallLockedNotice\", EffectiveNextFirewallRequiredLevel", razor, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public void FirewallLockedNotice_Template_HasNoHardcodedLevel_AndFormatsPolicyUnlockLevel(string language)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);

        var template = localizer["UpgradeTerminal_FirewallLockedNotice"];
        Assert.Contains("{0}", template, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d", template.Replace("{0}", string.Empty, StringComparison.Ordinal));

        var formatted = localizer["UpgradeTerminal_FirewallLockedNotice", CyberDefenseFirewallPolicy.UnlockLevel];
        Assert.Contains(CyberDefenseFirewallPolicy.UnlockLevel.ToString(CultureInfo.InvariantCulture), formatted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 4, true)]
    [InlineData(0, 5, false)]
    [InlineData(1, 10, false)]
    [InlineData(2, 60, false)]
    [InlineData(3, 60, false)]
    public void FirewallShieldLockedProjection_OnlyBeforeFirstPolicyUnlock(int firewallRank, int playerLevel, bool expectedLocked)
    {
        var status = CreateStatus(playerLevel, firewallRank);

        Assert.Equal(expectedLocked, status.IsFirewallShieldLocked);
        if (expectedLocked)
        {
            Assert.Equal(CyberDefenseFirewallPolicy.UnlockLevel, status.NextFirewallRequiredLevel);
        }
    }

    [Theory]
    [InlineData("en", 1)]
    [InlineData("en", 4)]
    [InlineData("en", 5)]
    [InlineData("en", 6)]
    [InlineData("de", 4)]
    [InlineData("de", 5)]
    [InlineData("ru", 4)]
    [InlineData("ru", 5)]
    public async Task UpgradeTerminal_RenderedFirewallRequirement_MatchesPolicyEligibility(string language, int playerLevel)
    {
        var directory = CreateTempDirectory();
        using var store = new SqliteGameplayStore(Path.Combine(directory, "synthetic-firewall-terminal.db"));
        await store.InitializeAsync();
        await SetSyntheticPlayerLevelAsync(store, playerLevel);
        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);

        var html = await RenderUpgradeTerminalAsync(vm, localizer);
        var firewallCard = Regex.Match(html, @"<article[^>]*firewall-upgrade-card[\s\S]*?</article>").Value;

        bool policyUnlocked = CyberDefenseFirewallPolicy.IsRankUnlockedAtLevel(1, playerLevel);
        bool policyPurchasable = CyberDefenseFirewallPolicy.CanPurchaseNextRank(0, playerLevel, playerLevel - 1);
        Assert.Equal(policyUnlocked, vm.IsFirewallLevelUnlocked);
        Assert.Equal(policyPurchasable, vm.CanPurchaseFirewall);
        Assert.Equal(!policyUnlocked, firewallCard.Contains("upgrade-firewall-locked-notice", StringComparison.Ordinal));
        Assert.Equal(!policyUnlocked, firewallCard.Contains("locked-card", StringComparison.Ordinal));
        Assert.Equal(policyUnlocked, firewallCard.Contains("firewall-purchase-btn", StringComparison.Ordinal));

        if (!policyUnlocked)
        {
            Assert.Contains(localizer["UpgradeTerminal_FirewallLockedNotice", CyberDefenseFirewallPolicy.UnlockLevel], firewallCard, StringComparison.Ordinal);
            Assert.Contains(localizer["UpgradeTerminal_RequiredLevel", CyberDefenseFirewallPolicy.UnlockLevel], firewallCard, StringComparison.Ordinal);
        }
        else
        {
            // Blazor omits boolean attributes when false, so a purchasable button has no disabled attribute.
            var button = Regex.Match(firewallCard, @"<button[^>]*firewall-purchase-btn[^>]*>").Value;
            Assert.Equal(!policyPurchasable, Regex.IsMatch(button, @"\sdisabled(\s|>|="")"));
        }
    }

    [Fact]
    public async Task UpgradeTerminal_MaxRankCards_AreNotPresentedAsLocked()
    {
        var directory = CreateTempDirectory();
        using var store = new SqliteGameplayStore(Path.Combine(directory, "synthetic-max-rank-terminal.db"));
        await store.InitializeAsync();
        await SetSyntheticPlayerLevelAsync(store, 80);
        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();
        for (int rank = 0; rank < CyberDefenseFirewallPolicy.MaxRank; rank++)
        {
            Assert.True((await vm.PurchaseFirewallUpgradeAsync()).IsSuccess);
        }
        Assert.True(vm.IsFirewallMaxRank);
        var localizer = new LocalizationService();

        var html = await RenderUpgradeTerminalAsync(vm, localizer);
        var firewallCard = Regex.Match(html, @"<article[^>]*firewall-upgrade-card[\s\S]*?</article>").Value;

        Assert.DoesNotContain("locked-card", firewallCard, StringComparison.Ordinal);
        Assert.DoesNotContain("upgrade-firewall-locked-notice", firewallCard, StringComparison.Ordinal);
        Assert.Contains("cost-max", firewallCard, StringComparison.Ordinal);
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
        // An unlocked operation without a stored stage is "unavailable", never presented as locked (Issue #122).
        Assert.Contains("Training_Unavailable", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings_OperationLocked", home, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(1, CyberDefenseOpponentScalePolicy.BossClass)]
    [InlineData(2, CyberDefenseOpponentScalePolicy.BossClass)]
    [InlineData(3, CyberDefenseOpponentScalePolicy.SectorBossClass)]
    [InlineData(10, CyberDefenseOpponentScalePolicy.SectorBossClass)]
    public async Task HudViewModel_FromRunState_SectorBossScale_ReachesRenderedHud(int sector, string expectedClass)
    {
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var runState = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp));

        var vm = CyberDefenseHudViewModel.FromRunState(runState);

        Assert.True(vm.IsSectorBoss);
        Assert.Equal(expectedClass, vm.OpponentScaleClass);
        // Presentation-only: combat projection stays on the authoritative scaling policy.
        Assert.Equal(bossMaxHp, vm.OpponentMaxHp);
        Assert.Equal(bossMaxHp, vm.OpponentCurrentHp);
        Assert.Equal(CyberDefenseScalingPolicy.GetNormalOpponentCount(sector), vm.NormalOpponentCount);

        var wrapperClasses = await RenderHudArtworkWrapperClassesAsync(vm);
        Assert.Contains(expectedClass, wrapperClasses);
        Assert.Contains("is-boss", wrapperClasses);
        var otherBossClass = expectedClass == CyberDefenseOpponentScalePolicy.BossClass
            ? CyberDefenseOpponentScalePolicy.SectorBossClass
            : CyberDefenseOpponentScalePolicy.BossClass;
        Assert.DoesNotContain(otherBossClass, wrapperClasses);
    }

    [Fact]
    public void HudViewModel_FromRunState_OrdinaryOpponentScaleAndHp_AreUnchangedAcrossSectors()
    {
        for (int sector = 1; sector <= 12; sector++)
        {
            int normalCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(sector);
            for (int index = 0; index < normalCount; index++)
            {
                int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, index);
                var runState = CyberDefenseRunState.CreateActive(sector, index, 100, new OpponentState(OpponentKind.Normal, maxHp, maxHp));

                var vm = CyberDefenseHudViewModel.FromRunState(runState);

                Assert.False(vm.IsBoss);
                Assert.Equal(CyberDefenseOpponentScalePolicy.GetScaleClass(index, normalCount, isSectorBoss: false, isBoss: false), vm.OpponentScaleClass);
                Assert.Equal(maxHp, vm.OpponentMaxHp);
            }
        }
    }

    [Fact]
    public async Task HudViewModel_FromRunState_OrdinaryOpponents_RenderProgressiveScaleBelowBossTier()
    {
        var expected = new[]
        {
            CyberDefenseOpponentScalePolicy.SmallClass,
            CyberDefenseOpponentScalePolicy.SmallClass,
            CyberDefenseOpponentScalePolicy.MediumSmallClass,
            CyberDefenseOpponentScalePolicy.MediumSmallClass,
            CyberDefenseOpponentScalePolicy.MediumClass
        };
        Assert.Equal(expected.Length, CyberDefenseScalingPolicy.GetNormalOpponentCount(1));

        for (int index = 0; index < expected.Length; index++)
        {
            int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, index);
            var vm = CyberDefenseHudViewModel.FromRunState(
                CyberDefenseRunState.CreateActive(1, index, 100, new OpponentState(OpponentKind.Normal, maxHp, maxHp)));

            var wrapperClasses = await RenderHudArtworkWrapperClassesAsync(vm);
            Assert.Contains(expected[index], wrapperClasses);
            Assert.DoesNotContain("is-boss", wrapperClasses);
            Assert.True(
                CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScalePolicy.GetScaleTier(index, expected.Length, false, false)) <
                CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.Boss));
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private readonly List<string> _tempDirectories = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var directory in _tempDirectories)
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue112Ui_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }

    private static UpgradeTerminalStatus CreateStatus(int playerLevel, int firewallRank)
    {
        var progress = CyberDefenseLevelPolicy.CalculateProgress(CyberDefenseLevelPolicy.GetCumulativeXpThreshold(playerLevel));
        return new UpgradeTerminalStatus(
            playerLevel: playerLevel,
            cumulativeXp: CyberDefenseLevelPolicy.GetCumulativeXpThreshold(playerLevel),
            xpIntoCurrentLevel: progress.XpIntoCurrentLevel,
            xpRequiredForNextLevel: progress.XpRequiredForNextLevel,
            remainingXpToNextLevel: progress.RemainingXpToNextLevel,
            progressRatio: progress.ProgressRatio,
            levelProgress: progress,
            availableSkillPoints: 0,
            earnedSkillPoints: playerLevel - 1,
            spentSkillPoints: playerLevel - 1,
            currentAttackRank: 0,
            nextAttackRank: 1,
            currentBaseDamage: 1,
            nextBaseDamage: 2,
            nextRankCost: 1,
            hasSufficientSkillPoints: false,
            resetEpoch: 0,
            storeRevision: 1,
            currentFirewallRank: firewallRank);
    }

    private static async Task SetSyntheticPlayerLevelAsync(SqliteGameplayStore store, int playerLevel)
    {
        using var connection = new SqliteConnection($"Data Source={store.StoragePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE gameplay_progression SET cumulative_xp = @xp, player_level = @level, earned_sp = @sp, spent_sp = 0, available_sp = @sp WHERE id = 1;";
        command.Parameters.AddWithValue("@xp", CyberDefenseLevelPolicy.GetCumulativeXpThreshold(playerLevel));
        command.Parameters.AddWithValue("@level", playerLevel);
        command.Parameters.AddWithValue("@sp", playerLevel - 1);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<string> RenderUpgradeTerminalAsync(UpgradeTerminalViewModel vm, ILocalizationService localizer)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton(localizer).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<global::MathFirst.Core.Tests.UpgradeTerminal>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["ViewModel"] = vm,
                ["Localizer"] = localizer
            }));
            return WebUtility.HtmlDecode(root.ToHtmlString());
        });
    }

    private static async Task<string[]> RenderHudArtworkWrapperClassesAsync(CyberDefenseHudViewModel vm)
    {
        var localizer = new LocalizationService();
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ILocalizationService>(localizer).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<global::MathFirst.Core.Tests.CyberDefenseHud>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["ViewModel"] = vm
            }));
            return WebUtility.HtmlDecode(root.ToHtmlString());
        });
        var match = Regex.Match(html, @"class=""scene-artwork-wrapper ([^""]*)""");
        Assert.True(match.Success, "Rendered HUD must contain the opponent artwork wrapper.");
        return match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
    private sealed class TutorialTestHost : ComponentBase
    {
        [Parameter] public ILocalizationService Localizer { get; set; } = null!;
        [Parameter] public Action<global::MathFirst.Core.Tests.CyberDefenseTutorial> OnReady { get; set; } = null!;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<global::MathFirst.Core.Tests.CyberDefenseTutorial>(0);
            builder.AddAttribute(1, "Localizer", Localizer);
            builder.AddComponentReferenceCapture(2, inst => OnReady((global::MathFirst.Core.Tests.CyberDefenseTutorial)inst));
            builder.CloseComponent();
        }
    }

    private static async Task<string> RenderTutorialComponentAsync(int step, ILocalizationService localizer)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton(localizer).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            global::MathFirst.Core.Tests.CyberDefenseTutorial? tutorial = null;
            var root = await renderer.RenderComponentAsync<TutorialTestHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Localizer"] = localizer,
                ["OnReady"] = (Action<global::MathFirst.Core.Tests.CyberDefenseTutorial>)(t => tutorial = t)
            }));

            Assert.NotNull(tutorial);
            for (var s = 0; s < step; s++)
            {
                await tutorial.NextAsync();
            }

            return WebUtility.HtmlDecode(root.ToHtmlString());
        });
    }
}
