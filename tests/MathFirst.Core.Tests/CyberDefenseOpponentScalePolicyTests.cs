namespace MathFirst.Core.Tests;

using MathFirst.Application.Practice;
using Xunit;

public sealed class CyberDefenseOpponentScalePolicyTests
{
    [Fact]
    public void DefaultRoster_MapsToDeterministicScaleTiersAcrossProgression()
    {
        var roster = CyberDefenseEncounterState.DefaultRoster;
        Assert.Equal(10, roster.Count);

        // Wave 1: Glitch Drone (early regular) -> Small
        Assert.Equal(CyberDefenseOpponentScaleTier.Small, CyberDefenseOpponentScalePolicy.GetScaleTier(roster[0], 0));
        Assert.Equal(CyberDefenseOpponentScalePolicy.SmallClass, CyberDefenseOpponentScalePolicy.GetScaleClass(roster[0], 0));
        Assert.Equal(0.65, CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.Small));

        // Wave 2: Data Leech (early regular) -> Small
        Assert.Equal(CyberDefenseOpponentScaleTier.Small, CyberDefenseOpponentScalePolicy.GetScaleTier(roster[1], 1));
        Assert.Equal(CyberDefenseOpponentScalePolicy.SmallClass, CyberDefenseOpponentScalePolicy.GetScaleClass(roster[1], 1));

        // Wave 3: Firewall Breaker (early-mid regular) -> MediumSmall
        Assert.Equal(CyberDefenseOpponentScaleTier.MediumSmall, CyberDefenseOpponentScalePolicy.GetScaleTier(roster[2], 2));
        Assert.Equal(CyberDefenseOpponentScalePolicy.MediumSmallClass, CyberDefenseOpponentScalePolicy.GetScaleClass(roster[2], 2));
        Assert.Equal(0.78, CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.MediumSmall));

        // Wave 4: Nexus Overlord (mid-boss) -> Boss
        Assert.Equal(CyberDefenseOpponentScaleTier.Boss, CyberDefenseOpponentScalePolicy.GetScaleTier(roster[3], 3));
        Assert.Equal(CyberDefenseOpponentScalePolicy.BossClass, CyberDefenseOpponentScalePolicy.GetScaleClass(roster[3], 3));
        Assert.Equal(1.42, CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.Boss));

        // Wave 5: Virus Core (mid regular) -> Medium
        Assert.Equal(CyberDefenseOpponentScaleTier.Medium, CyberDefenseOpponentScalePolicy.GetScaleTier(roster[4], 4));
        Assert.Equal(CyberDefenseOpponentScalePolicy.MediumClass, CyberDefenseOpponentScalePolicy.GetScaleClass(roster[4], 4));
        Assert.Equal(0.90, CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.Medium));

        // Wave 6: Signal Phantom (mid regular) -> Medium
        Assert.Equal(CyberDefenseOpponentScaleTier.Medium, CyberDefenseOpponentScalePolicy.GetScaleTier(roster[5], 5));
        Assert.Equal(CyberDefenseOpponentScalePolicy.MediumClass, CyberDefenseOpponentScalePolicy.GetScaleClass(roster[5], 5));

        // Wave 7: Quantum Bug (late regular) -> Large
        Assert.Equal(CyberDefenseOpponentScaleTier.Large, CyberDefenseOpponentScalePolicy.GetScaleTier(roster[6], 6));
        Assert.Equal(CyberDefenseOpponentScalePolicy.LargeClass, CyberDefenseOpponentScalePolicy.GetScaleClass(roster[6], 6));
        Assert.Equal(1.05, CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.Large));

        // Wave 8: Trojan Wasp (late regular) -> Large
        Assert.Equal(CyberDefenseOpponentScaleTier.Large, CyberDefenseOpponentScalePolicy.GetScaleTier(roster[7], 7));
        Assert.Equal(CyberDefenseOpponentScalePolicy.LargeClass, CyberDefenseOpponentScalePolicy.GetScaleClass(roster[7], 7));

        // Wave 9: Crystal Malware (late regular) -> Large
        Assert.Equal(CyberDefenseOpponentScaleTier.Large, CyberDefenseOpponentScalePolicy.GetScaleTier(roster[8], 8));
        Assert.Equal(CyberDefenseOpponentScalePolicy.LargeClass, CyberDefenseOpponentScalePolicy.GetScaleClass(roster[8], 8));

        // Wave 10: Nexus Overlord Prime (sector boss) -> SectorBoss
        Assert.Equal(CyberDefenseOpponentScaleTier.SectorBoss, CyberDefenseOpponentScalePolicy.GetScaleTier(roster[9], 9));
        Assert.Equal(CyberDefenseOpponentScalePolicy.SectorBossClass, CyberDefenseOpponentScalePolicy.GetScaleClass(roster[9], 9));
        Assert.Equal(1.50, CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.SectorBoss));
    }

    [Fact]
    public void ScaleHierarchy_MaintainsStrictMonotonicOrdering()
    {
        var small = CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.Small);
        var medSmall = CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.MediumSmall);
        var med = CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.Medium);
        var large = CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.Large);
        var boss = CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.Boss);
        var sectorBoss = CyberDefenseOpponentScalePolicy.GetBaseScaleFactor(CyberDefenseOpponentScaleTier.SectorBoss);

        Assert.True(small < medSmall, "Small scale must be smaller than MediumSmall.");
        Assert.True(medSmall < med, "MediumSmall scale must be smaller than Medium.");
        Assert.True(med < large, "Medium scale must be smaller than Large.");
        Assert.True(large < boss, "Large regular enemy must be smaller than Boss.");
        Assert.True(boss < sectorBoss, "Standard Boss must be smaller than Sector Boss.");
        Assert.True(small < 0.70, "Early regular enemies must start noticeably smaller (below 0.70).");
    }

    [Fact]
    public void NonRosterEnemies_FollowConfiguredIsBossAndIsSectorBossFlags()
    {
        var customSectorBoss = new CyberDefenseEnemy("custom-prime", "Name", "Desc", "path.svg", 10, true, true);
        var customBoss = new CyberDefenseEnemy("custom-boss", "Name", "Desc", "path.svg", 8, true, false);
        var customEarlyEnemy = new CyberDefenseEnemy("custom-minion", "Name", "Desc", "path.svg", 5, false, false);

        Assert.Equal(CyberDefenseOpponentScaleTier.SectorBoss, CyberDefenseOpponentScalePolicy.GetScaleTier(customSectorBoss, 0));
        Assert.Equal(CyberDefenseOpponentScaleTier.Boss, CyberDefenseOpponentScalePolicy.GetScaleTier(customBoss, 0));
        Assert.Equal(CyberDefenseOpponentScaleTier.Small, CyberDefenseOpponentScalePolicy.GetScaleTier(customEarlyEnemy, 0));
        Assert.Equal(CyberDefenseOpponentScaleTier.Large, CyberDefenseOpponentScalePolicy.GetScaleTier(customEarlyEnemy, 7));
    }
}
