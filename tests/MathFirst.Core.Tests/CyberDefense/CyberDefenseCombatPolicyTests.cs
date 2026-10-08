using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseCombatPolicyTests
{
    [Fact]
    public void FixedProductRules_PlayerMaxHpIsOneHundred()
    {
        Assert.Equal(100, CyberDefenseCombatPolicy.PlayerMaxHp);
    }

    [Fact]
    public void PreliminaryBaseDamage_ConstantsMatchSpecification()
    {
        Assert.Equal(4, CyberDefenseCombatPolicy.NormalEnemyBaseDamage);
        Assert.Equal(6, CyberDefenseCombatPolicy.BossEnemyBaseDamage);
    }

    [Fact]
    public void PreliminaryDefeatHealing_ConstantsMatchSpecification()
    {
        Assert.Equal(2, CyberDefenseCombatPolicy.NormalDefeatHealing);
        Assert.Equal(6, CyberDefenseCombatPolicy.BossDefeatHealing);
    }

    [Theory]
    [InlineData(1, OpponentKind.Normal, 4)]
    [InlineData(1, OpponentKind.Boss, 6)]
    [InlineData(49, OpponentKind.Normal, 4)]
    [InlineData(49, OpponentKind.Boss, 6)]
    [InlineData(50, OpponentKind.Normal, 5)]
    [InlineData(50, OpponentKind.Boss, 7)]
    [InlineData(51, OpponentKind.Normal, 5)]
    [InlineData(51, OpponentKind.Boss, 7)]
    [InlineData(99, OpponentKind.Normal, 5)]
    [InlineData(99, OpponentKind.Boss, 7)]
    [InlineData(100, OpponentKind.Normal, 6)]
    [InlineData(100, OpponentKind.Boss, 8)]
    [InlineData(101, OpponentKind.Normal, 6)]
    [InlineData(101, OpponentKind.Boss, 8)]
    [InlineData(150, OpponentKind.Normal, 7)]
    [InlineData(150, OpponentKind.Boss, 9)]
    [InlineData(int.MaxValue, OpponentKind.Normal, 42949676)]
    [InlineData(int.MaxValue, OpponentKind.Boss, 42949678)]
    public void GetEnemyDamage_CalculatesExactDamageScaling(int sector, OpponentKind kind, int expectedDamage)
    {
        int actualDamage = CyberDefenseCombatPolicy.GetEnemyDamage(sector, kind);
        Assert.Equal(expectedDamage, actualDamage);
    }

    [Theory]
    [InlineData(OpponentKind.Normal, 2)]
    [InlineData(OpponentKind.Boss, 6)]
    public void GetDefeatHealing_ReturnsPreliminaryHealingValues(OpponentKind kind, int expectedHealing)
    {
        int actualHealing = CyberDefenseCombatPolicy.GetDefeatHealing(kind);
        Assert.Equal(expectedHealing, actualHealing);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void GetEnemyDamage_ThrowsArgumentOutOfRangeException_ForInvalidSector(int invalidSector)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCombatPolicy.GetEnemyDamage(invalidSector, OpponentKind.Normal));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCombatPolicy.GetEnemyDamage(invalidSector, OpponentKind.Boss));
    }

    [Theory]
    [InlineData((OpponentKind)(-1))]
    [InlineData((OpponentKind)2)]
    [InlineData((OpponentKind)int.MaxValue)]
    public void GetEnemyDamage_ThrowsArgumentOutOfRangeException_ForInvalidOpponentKind(OpponentKind invalidKind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCombatPolicy.GetEnemyDamage(1, invalidKind));
    }

    [Theory]
    [InlineData((OpponentKind)(-1))]
    [InlineData((OpponentKind)2)]
    [InlineData((OpponentKind)int.MaxValue)]
    public void GetDefeatHealing_ThrowsArgumentOutOfRangeException_ForInvalidOpponentKind(OpponentKind invalidKind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCombatPolicy.GetDefeatHealing(invalidKind));
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(-1)]
    [InlineData(0)]
    public void ValidateAttackDamage_ThrowsArgumentOutOfRangeException_ForNonPositiveAttack(int invalidAttackDamage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCombatPolicy.ValidateAttackDamage(invalidAttackDamage));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void ValidateAttackDamage_AcceptsPositiveAttackValues(int validAttackDamage)
    {
        CyberDefenseCombatPolicy.ValidateAttackDamage(validAttackDamage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(49)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(50000)]
    [InlineData(int.MaxValue)]
    public void Invariants_DamageIsPositiveAndBossExceedsNormal(int sector)
    {
        int normalDamage = CyberDefenseCombatPolicy.GetEnemyDamage(sector, OpponentKind.Normal);
        int bossDamage = CyberDefenseCombatPolicy.GetEnemyDamage(sector, OpponentKind.Boss);

        Assert.True(normalDamage > 0);
        Assert.True(bossDamage > normalDamage);
    }

    [Fact]
    public void Invariants_DamageIsMonotonicallyNonDecreasingAcrossSectors()
    {
        int prevNormal = CyberDefenseCombatPolicy.GetEnemyDamage(1, OpponentKind.Normal);
        int prevBoss = CyberDefenseCombatPolicy.GetEnemyDamage(1, OpponentKind.Boss);

        for (int sector = 2; sector <= 250; sector++)
        {
            int currentNormal = CyberDefenseCombatPolicy.GetEnemyDamage(sector, OpponentKind.Normal);
            int currentBoss = CyberDefenseCombatPolicy.GetEnemyDamage(sector, OpponentKind.Boss);

            Assert.True(currentNormal >= prevNormal);
            Assert.True(currentBoss >= prevBoss);

            prevNormal = currentNormal;
            prevBoss = currentBoss;
        }
    }

    [Fact]
    public void Invariants_HealingValuesAreNonNegative()
    {
        Assert.True(CyberDefenseCombatPolicy.GetDefeatHealing(OpponentKind.Normal) >= 0);
        Assert.True(CyberDefenseCombatPolicy.GetDefeatHealing(OpponentKind.Boss) >= 0);
    }
}
