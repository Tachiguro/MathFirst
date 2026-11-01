namespace MathFirst.Core.Tests.CyberDefense;

using System.Text.RegularExpressions;
using MathFirst.Application.Gameplay;
using MathFirst.Domain.CyberDefense;
using HudRender = Issue134HudPresentationTests.HudRender;

/// <summary>
/// Issue #136: the player status hierarchy and shield clarity in the real <c>CyberDefenseHud</c> component. Level/XP
/// is the top row with an upward progress icon, the earned shield status (icon, active / capacity and, only while a
/// shield is down, the recharge meter with its units) the middle row, and player HP with a heart icon the bottom row.
/// Presentation only: authoritative values, accessible names and the shield recharge rule are unchanged.
/// </summary>
public sealed class Issue136PlayerStatusPresentationTests
{
    [Theory]
    [InlineData("en", "HP: 73 / 100", "Lv. 3")]
    [InlineData("de", "KP: 73 / 100", "Lv. 3")]
    [InlineData("ru", "ОЗ: 73 / 100", "Ур. 3")]
    public async Task PlayerStatus_StacksLevelAndXpOnTop_ShieldsInTheMiddle_AndHpWithAHeartAtTheBottom(string language, string hp, string level)
    {
        await using var hud = await HudRender.CreateAsync(language);
        var progress = CyberDefenseLevelPolicy.CalculateProgress(CyberDefenseLevelPolicy.GetCumulativeXpThreshold(3) + 4);
        var xp = $"4 / {progress.XpRequiredForNextLevel}";
        await hud.SetAsync(Model(73, capacity: 1, active: 0, units: 12), progress: progress);

        var html = await hud.HtmlAsync();
        var column = Issue134HudPresentationTests.Block(html, "shield-status-column");
        var xpRow = Issue134HudPresentationTests.Block(column, "player-xp-cluster");
        var shieldRow = Issue134HudPresentationTests.Block(column, "player-status-heading");
        var hpRow = Issue134HudPresentationTests.Block(column, "player-hp-cluster");
        Assert.True(column.IndexOf(xpRow, StringComparison.Ordinal) < column.IndexOf(shieldRow, StringComparison.Ordinal));
        Assert.True(column.IndexOf(shieldRow, StringComparison.Ordinal) < column.IndexOf(hpRow, StringComparison.Ordinal));

        // Top: an upward progress icon, the level, the XP meter and the real XP values (the meter keeps "x of y XP").
        Assert.Matches(@"^<div class=""player-xp-cluster"">\s*<svg class=""player-status-icon xp-progress-icon""[^>]*aria-hidden=""true""", xpRow);
        Assert.Contains($"<span class=\"player-level-label\">{level}</span>", xpRow);
        Assert.Contains($"<span class=\"player-xp-value\">{xp}</span>", xpRow);
        Assert.Matches($@"class=""meter-bar player-xp-bar""[^>]*role=""progressbar""[^>]*aria-valuenow=""4""[^>]*aria-valuemax=""{progress.XpRequiredForNextLevel}""", xpRow);

        // Bottom: a heart and the real current / maximum HP once; the localized "HP: current / max" stays the accessible
        // name of the panel and of the HP meter, whose values are unchanged.
        Assert.Matches(@"^<div class=""player-hp-cluster"">\s*<svg class=""player-status-icon player-hp-icon""[^>]*aria-hidden=""true""", hpRow);
        Assert.Matches($@"class=""meter-bar player-hp-bar""[^>]*aria-label=""{Regex.Escape(hp)}""[^>]*aria-valuenow=""73""[^>]*aria-valuemax=""100""", hpRow);
        Assert.Contains("<span class=\"player-hp-value\">73 / 100</span>", hpRow);
        Assert.Single(Regex.Matches(html, ">73 / 100<"));
        Assert.DoesNotContain($">{hp}<", html);

        // The upward icon identifies XP progress only; nothing suggests HP regeneration.
        Assert.Single(Regex.Matches(html, "xp-progress-icon"));
        Assert.DoesNotContain("xp-progress-icon", hpRow);
        Assert.DoesNotContain("xp-progress-icon", shieldRow);
        Assert.Matches($"<section class=\"player-shield-panel\"[^>]*aria-label=\"{Regex.Escape(hp)}\"", html);
    }

    [Theory]
    [InlineData("en", 1, 0, 12, "Shield: 0 / 1", "Shield recharge: 12 / 30")]
    [InlineData("de", 1, 0, 29, "Schild: 0 / 1", "Schildaufladung: 29 / 30")]
    [InlineData("ru", 3, 2, 0, "Щит: 2 / 3", "Перезарядка щита: 0 / 30")]
    public async Task EarnedShields_ShowOneIconAndActiveOverCapacity_AndASeparateRechargeMeterWhileDown(
        string language, int capacity, int active, int units, string shieldText, string rechargeText)
    {
        await using var hud = await HudRender.CreateAsync(language);
        await hud.SetAsync(Model(100, capacity, active, units));

        var row = Issue134HudPresentationTests.Block(await hud.HtmlAsync(), "player-status-heading");
        var status = Regex.Match(row, @"<div class=""shield-badges-row""[^>]*>[\s\S]*?</div>").Value;
        Assert.Contains("role=\"meter\"", status);
        Assert.Contains($"aria-valuenow=\"{active}\"", status);
        Assert.Contains($"aria-valuemax=\"{capacity}\"", status);
        Assert.Contains($"aria-valuetext=\"{shieldText}\"", status);
        // One shield icon whatever the capacity, and the active / capacity count beside it.
        Assert.Single(Regex.Matches(status, "class=\"shield-badge "));
        Assert.Contains(active > 0 ? "class=\"shield-badge shield-intact\"" : "class=\"shield-badge shield-broken\"", status);
        Assert.Contains($"<span class=\"shield-count-text\" aria-hidden=\"true\">{active} / {capacity}</span>", status);
        Assert.DoesNotContain("shield-recharge", status);

        // The recharge meter and its units are a separate element, after the count, with their own recharge icon.
        var meter = Regex.Match(row, "<div class=\"meter-bar shield-recharge-bar\"[^>]*>").Value;
        Assert.Contains("role=\"progressbar\"", meter);
        Assert.Contains($"aria-valuenow=\"{units}\"", meter);
        Assert.Contains($"aria-valuemax=\"{CyberDefenseFirewallPolicy.RechargeThresholdUnits}\"", meter);
        Assert.Contains($"aria-valuetext=\"{rechargeText}\"", meter);
        var value = Regex.Match(row, @"<span class=""shield-recharge-value"" aria-hidden=""true"">\s*<svg class=""shield-recharge-icon""[^>]*>[\s\S]*?</svg>\s*([^<]*)</span>");
        Assert.True(value.Success, "The recharge units must be shown beside the recharge meter.");
        Assert.Equal($"{units} / {CyberDefenseFirewallPolicy.RechargeThresholdUnits}", value.Groups[1].Value.Trim());
        Assert.True(row.IndexOf("shield-count-text", StringComparison.Ordinal) < row.IndexOf("shield-recharge-bar", StringComparison.Ordinal));
        Assert.True(row.IndexOf("shield-recharge-bar", StringComparison.Ordinal) < row.IndexOf("shield-recharge-value", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task FullShields_KeepTheirIconAndCount_WithoutRechargeProgress(int capacity)
    {
        await using var hud = await HudRender.CreateAsync();
        await hud.SetAsync(Model(100, capacity, capacity, 0));

        var row = Issue134HudPresentationTests.Block(await hud.HtmlAsync(), "player-status-heading");
        Assert.Single(Regex.Matches(row, "class=\"shield-badge shield-intact\""));
        Assert.Contains($">{capacity} / {capacity}</span>", row);
        Assert.DoesNotContain("shield-recharge", row);
    }

    [Fact]
    public async Task NoShieldCapacity_ShowsNoShieldIconCountOrRecharge_ButKeepsTheRowReserved()
    {
        await using var hud = await HudRender.CreateAsync();
        await hud.SetAsync(Model(100, 0, 0, 0));

        var html = await hud.HtmlAsync();
        var row = Issue134HudPresentationTests.Block(html, "player-status-heading");
        Assert.DoesNotContain("shield-badge", html);
        Assert.DoesNotContain("shield-count-text", html);
        Assert.DoesNotContain("shield-recharge", html);
        Assert.Matches(@"^<div class=""player-status-heading"">\s*</div>$", row);
    }

    [Theory]
    [InlineData("en", 999, 12345, 15000, "Lv. 999", "12345 / 15000")]
    [InlineData("ru", 4321, 9999, 10001, "Ур. 4321", "9999 / 10001")]
    public async Task LargeLevelsAndXp_AreShownWithTheirRealNumbers(string language, int level, int into, int required, string levelText, string xpText)
    {
        await using var hud = await HudRender.CreateAsync(language);
        const long threshold = 50_000_000;
        var progress = new PlayerLevelProgress(level, threshold + into, threshold, threshold + required);
        await hud.SetAsync(Model(100, 0, 0, 0), progress: progress);

        var xpRow = Issue134HudPresentationTests.Block(await hud.HtmlAsync(), "player-xp-cluster");
        Assert.Contains($"<span class=\"player-level-label\">{levelText}</span>", xpRow);
        Assert.Contains($"<span class=\"player-xp-value\">{xpText}</span>", xpRow);
        Assert.Contains($"aria-valuenow=\"{into}\"", xpRow);
        Assert.Contains($"aria-valuemax=\"{required}\"", xpRow);
    }

    private static CyberDefenseHudViewModel Model(int playerHp, int capacity, int active, int units) =>
        CyberDefenseHudViewModel.FromRunState(CyberDefenseRunState.CreateActive(1, 0, playerHp,
            new OpponentState(OpponentKind.Normal, 2, 2), capacity, active, units));
}
