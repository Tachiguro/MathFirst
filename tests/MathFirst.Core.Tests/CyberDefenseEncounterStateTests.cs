namespace MathFirst.Core.Tests;

using MathFirst.Application.Practice;
using Xunit;

public sealed class CyberDefenseEncounterStateTests
{
    [Fact]
    public void InitialState_HasExpectedPrototypeDefaults()
    {
        var state = new CyberDefenseEncounterState();

        Assert.Equal(0, state.EnemyIndex);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, state.EnemyHitPoints);
        Assert.Equal(5, CyberDefenseEncounterState.PrototypeEnemyHitPoints);
        Assert.Equal(CyberDefenseEncounterState.PrototypeShieldSegments, state.ShieldSegments);
        Assert.Equal(3, CyberDefenseEncounterState.PrototypeShieldSegments);
        Assert.Equal(3, CyberDefenseEncounterState.PrototypeEnemyCount);
        Assert.Equal(0, state.Revision);
    }

    [Fact]
    public void RecordCorrectAnswer_ReducesEnemyHitPoints_AndIncrementsRevision()
    {
        var state = new CyberDefenseEncounterState();
        var initialRevision = state.Revision;

        state.RecordCorrectAnswer();

        Assert.Equal(4, state.EnemyHitPoints);
        Assert.Equal(0, state.EnemyIndex);
        Assert.True(state.Revision > initialRevision);
    }

    [Fact]
    public void DefeatingEnemy_AdvancesEnemyIndexModuloThree_AndResetsHitPoints()
    {
        var state = new CyberDefenseEncounterState();

        // Deal 5 damage to defeat Enemy 0
        for (var i = 0; i < CyberDefenseEncounterState.PrototypeEnemyHitPoints; i++)
        {
            state.RecordCorrectAnswer();
        }

        Assert.Equal(1, state.EnemyIndex);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, state.EnemyHitPoints);

        // Deal 5 damage to defeat Enemy 1
        for (var i = 0; i < CyberDefenseEncounterState.PrototypeEnemyHitPoints; i++)
        {
            state.RecordCorrectAnswer();
        }

        Assert.Equal(2, state.EnemyIndex);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, state.EnemyHitPoints);

        // Deal 5 damage to defeat Enemy 2 -> wraps back to Enemy 0
        for (var i = 0; i < CyberDefenseEncounterState.PrototypeEnemyHitPoints; i++)
        {
            state.RecordCorrectAnswer();
        }

        Assert.Equal(0, state.EnemyIndex);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, state.EnemyHitPoints);
    }

    [Fact]
    public void RecordIncorrectAnswer_ReducesShieldSegments_AndIncrementsRevision()
    {
        var state = new CyberDefenseEncounterState();
        var initialRevision = state.Revision;

        state.RecordIncorrectAnswer();

        Assert.Equal(2, state.ShieldSegments);
        Assert.True(state.Revision > initialRevision);
    }

    [Fact]
    public void LosingFinalShield_ResetsEncounterHpAndShields()
    {
        var state = new CyberDefenseEncounterState();
        // Deal some damage first
        state.RecordCorrectAnswer();
        state.RecordCorrectAnswer();
        Assert.Equal(3, state.EnemyHitPoints);

        // Take 3 hits to lose all shields
        state.RecordIncorrectAnswer();
        Assert.Equal(2, state.ShieldSegments);
        state.RecordIncorrectAnswer();
        Assert.Equal(1, state.ShieldSegments);

        // 3rd hit breaches defense and resets encounter state
        state.RecordIncorrectAnswer();
        Assert.Equal(CyberDefenseEncounterState.PrototypeShieldSegments, state.ShieldSegments);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, state.EnemyHitPoints);
    }

    [Fact]
    public void Reset_RestoresInitialValues_AndIncrementsRevision()
    {
        var state = new CyberDefenseEncounterState();
        state.RecordCorrectAnswer();
        state.RecordIncorrectAnswer();
        var revBeforeReset = state.Revision;

        state.Reset();

        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, state.EnemyHitPoints);
        Assert.Equal(CyberDefenseEncounterState.PrototypeShieldSegments, state.ShieldSegments);
        Assert.True(state.Revision > revBeforeReset);
    }
}
