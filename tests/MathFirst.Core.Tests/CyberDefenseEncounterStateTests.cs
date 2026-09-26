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

    [Fact]
    public void RecordCorrectAnswer_SetsHitFeedback_AndIncrementsFeedbackRevision()
    {
        var state = new CyberDefenseEncounterState();
        var initialRevision = state.FeedbackRevision;

        state.RecordCorrectAnswer();

        Assert.Equal(CyberDefenseFeedbackKind.Hit, state.LastFeedback);
        Assert.True(state.FeedbackRevision > initialRevision);
    }

    [Fact]
    public void RecordIncorrectAnswer_SetsBlockedFeedback_AndCannotDecrementEnemyHitPoints()
    {
        var state = new CyberDefenseEncounterState();
        var initialHp = state.EnemyHitPoints;
        var initialRevision = state.FeedbackRevision;

        state.RecordIncorrectAnswer();

        Assert.Equal(CyberDefenseFeedbackKind.Blocked, state.LastFeedback);
        Assert.Equal(initialHp, state.EnemyHitPoints); // Enemy HP must NOT be decremented
        Assert.True(state.FeedbackRevision > initialRevision);
    }

    [Fact]
    public void MultipleAnswers_IncrementFeedbackRevisionEachTime_EnablingRetriggerableAnimations()
    {
        var state = new CyberDefenseEncounterState();

        state.RecordCorrectAnswer();
        var rev1 = state.FeedbackRevision;

        state.RecordCorrectAnswer();
        var rev2 = state.FeedbackRevision;

        state.RecordCorrectAnswer();
        var rev3 = state.FeedbackRevision;

        Assert.True(rev2 > rev1);
        Assert.True(rev3 > rev2);
        Assert.Equal(CyberDefenseFeedbackKind.Hit, state.LastFeedback);
    }
}
