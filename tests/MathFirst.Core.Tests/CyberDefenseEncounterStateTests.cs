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
        Assert.Equal(10, CyberDefenseEncounterState.PrototypeEnemyCount);
        Assert.False(state.IsBoss);
        Assert.Equal(0, state.Revision);
    }

    [Fact]
    public void RecordCorrectAnswer_ReducesEnemyHitPoints_AndIncrementsRevision()
    {
        var state = new CyberDefenseEncounterState();
        var initialRevision = state.Revision;

        state.RecordCorrectAnswer();

        Assert.Equal(4, state.EnemyHitPoints);
        Assert.Equal(1, state.LastDamageDealt);
        Assert.Equal(0, state.EnemyIndex);
        Assert.True(state.Revision > initialRevision);
    }

    [Fact]
    public void RecordCriticalHit_DealsTwoDamage_AndSetsCriticalFeedback()
    {
        var state = new CyberDefenseEncounterState();
        var initialRevision = state.Revision;

        state.RecordCriticalHit();

        Assert.Equal(3, state.EnemyHitPoints);
        Assert.Equal(2, state.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.CriticalHit, state.LastFeedback);
        Assert.True(state.Revision > initialRevision);
    }

    [Fact]
    public void DefeatingEnemy_AdvancesEnemyIndex_AndResetsHitPoints()
    {
        var state = new CyberDefenseEncounterState();

        // Deal 5 damage to defeat Enemy 0 (Glitch Drone)
        for (var i = 0; i < 5; i++)
        {
            state.RecordCorrectAnswer();
        }

        Assert.Equal(1, state.EnemyIndex);
        Assert.Equal("data-leech", state.CurrentEnemy.Id);
        Assert.Equal(5, state.EnemyHitPoints);

        // Deal 5 damage to defeat Enemy 1 (Data Leech)
        for (var i = 0; i < 5; i++)
        {
            state.RecordCorrectAnswer();
        }

        Assert.Equal(2, state.EnemyIndex);
        Assert.Equal("firewall-breaker", state.CurrentEnemy.Id);
        Assert.Equal(5, state.EnemyHitPoints);

        // Deal 5 damage to defeat Enemy 2 (Firewall Breaker)
        for (var i = 0; i < 5; i++)
        {
            state.RecordCorrectAnswer();
        }

        // Enemy 3 is Boss: Nexus Overlord!
        Assert.Equal(3, state.EnemyIndex);
        Assert.Equal("nexus-overlord", state.CurrentEnemy.Id);
        Assert.True(state.IsBoss);
        Assert.Equal(8, state.EnemyHitPoints);
        Assert.Equal(8, state.EnemyMaxHitPoints);
    }

    [Fact]
    public void BossEncounter_HasElevatedMaxHitPoints_AndIsBossFlag()
    {
        var state = new CyberDefenseEncounterState();
        state.TriggerBossEncounter();

        Assert.True(state.IsBoss);
        Assert.Equal(8, state.EnemyHitPoints);
        Assert.Equal(8, state.EnemyMaxHitPoints);
        Assert.Equal("nexus-overlord", state.CurrentEnemy.Id);
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
        state.RecordCorrectAnswer();
        state.RecordCorrectAnswer();
        Assert.Equal(3, state.EnemyHitPoints);

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

        Assert.Equal(0, state.EnemyIndex);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, state.EnemyHitPoints);
        Assert.Equal(CyberDefenseEncounterState.PrototypeShieldSegments, state.ShieldSegments);
        Assert.False(state.IsBoss);
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
        Assert.Equal(initialHp, state.EnemyHitPoints);
        Assert.True(state.FeedbackRevision > initialRevision);
    }

    [Fact]
    public void MultipleAnswers_IncrementFeedbackRevisionEachTime_EnablingRetriggerableAnimations()
    {
        var state = new CyberDefenseEncounterState();

        state.RecordCorrectAnswer();
        var rev1 = state.FeedbackRevision;

        state.RecordCriticalHit();
        var rev2 = state.FeedbackRevision;

        state.RecordCorrectAnswer();
        var rev3 = state.FeedbackRevision;

        Assert.True(rev2 > rev1);
        Assert.True(rev3 > rev2);
        Assert.Equal(CyberDefenseFeedbackKind.Hit, state.LastFeedback);
    }

    [Fact]
    public void DefaultRoster_ContainsAllConfiguredEnemiesAndBoss()
    {
        var roster = CyberDefenseEncounterState.DefaultRoster;

        Assert.Equal(10, roster.Count);
        Assert.Contains(roster, e => e.Id == "glitch-drone" && !e.IsBoss);
        Assert.Contains(roster, e => e.Id == "data-leech" && !e.IsBoss);
        Assert.Contains(roster, e => e.Id == "firewall-breaker" && !e.IsBoss);
        Assert.Contains(roster, e => e.Id == "nexus-overlord" && e.IsBoss);
        Assert.Contains(roster, e => e.Id == "virus-core" && !e.IsBoss);
        Assert.Contains(roster, e => e.Id == "signal-phantom" && !e.IsBoss);
        Assert.Contains(roster, e => e.Id == "quantum-bug" && !e.IsBoss);
        Assert.Contains(roster, e => e.Id == "trojan-wasp" && !e.IsBoss);
        Assert.Contains(roster, e => e.Id == "crystal-malware" && !e.IsBoss);
        Assert.Contains(roster, e => e.Id == "nexus-overlord-prime" && e.IsBoss && e.IsSectorBoss);
    }

    [Fact]
    public void DefeatingSectorBoss_AdvancesToNextSector_WithoutEndingGameplay()
    {
        var state = new CyberDefenseEncounterState();
        Assert.Equal(1, state.SectorNumber);

        // Defeat first 9 enemies (indices 0 through 8)
        for (var i = 0; i < 9; i++)
        {
            var hp = state.CurrentEnemy.MaxHitPoints;
            for (var hit = 0; hit < hp; hit++)
            {
                state.RecordCorrectAnswer();
            }
        }

        // Enemy 9 is the Sector Boss
        Assert.Equal(9, state.EnemyIndex);
        Assert.Equal("nexus-overlord-prime", state.CurrentEnemy.Id);
        Assert.True(state.IsBoss);
        Assert.True(state.IsSectorBoss);
        Assert.Equal(10, state.EnemyHitPoints);
        Assert.Equal(1, state.SectorNumber);

        // Defeat Sector Boss with 10 hits
        for (var hit = 0; hit < 10; hit++)
        {
            state.RecordCorrectAnswer();
        }

        // Must seamlessly continue to Sector 2, Wave 1 (glitch-drone) with no game-over / campaign termination
        Assert.Equal(2, state.SectorNumber);
        Assert.Equal(0, state.EnemyIndex);
        Assert.Equal("glitch-drone", state.CurrentEnemy.Id);
        Assert.False(state.IsBoss);
        Assert.False(state.IsSectorBoss);
        Assert.Equal(5, state.EnemyHitPoints);
    }

    [Fact]
    public void SectorProgression_IsRepeatableIndefinitely()
    {
        var state = new CyberDefenseEncounterState();

        // Advance through 3 complete sectors (30 enemies total)
        for (var sector = 1; sector <= 3; sector++)
        {
            Assert.Equal(sector, state.SectorNumber);
            for (var enemy = 0; enemy < CyberDefenseEncounterState.PrototypeEnemyCount; enemy++)
            {
                Assert.Equal(enemy, state.EnemyIndex);
                var hp = state.CurrentEnemy.MaxHitPoints;
                for (var hit = 0; hit < hp; hit++)
                {
                    state.RecordCorrectAnswer();
                }
            }
        }

        // After completing 3 full sectors, we are now in Sector 4, Wave 1
        Assert.Equal(4, state.SectorNumber);
        Assert.Equal(0, state.EnemyIndex);
        Assert.Equal("glitch-drone", state.CurrentEnemy.Id);
    }

    [Fact]
    public void CorrectAnswerAfterCriticalWindow_StillCausesNormalHit_WithoutPenalty()
    {
        var state = new CyberDefenseEncounterState();
        var initialHp = state.EnemyHitPoints;

        // Normal hit simulates answering after critical window elapsed:
        // LatencyMs > CurrentFactEasyThresholdMs -> RecordCorrectAnswer()
        state.RecordCorrectAnswer();

        Assert.Equal(initialHp - 1, state.EnemyHitPoints);
        Assert.Equal(1, state.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.Hit, state.LastFeedback);
        Assert.Equal(CyberDefenseEncounterState.PrototypeShieldSegments, state.ShieldSegments);
    }

    [Fact]
    public void CriticalHit_CausesOnlyCombatDifference_AndDoesNotTouchPersistenceOrLearningState()
    {
        var state = new CyberDefenseEncounterState();

        // Normal hit: 1 damage
        state.RecordHit(isCritical: false);
        Assert.Equal(1, state.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.Hit, state.LastFeedback);
        Assert.Equal(4, state.EnemyHitPoints);

        // Critical hit: 2 damage
        state.RecordHit(isCritical: true);
        Assert.Equal(2, state.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.CriticalHit, state.LastFeedback);
        Assert.Equal(2, state.EnemyHitPoints);

        // Verify transient-only nature: no database / FSRS / SRS properties
        var properties = typeof(CyberDefenseEncounterState).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name.Contains("Fsrs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Mastery", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Progression", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Curriculum", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Fact", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reset_RestoresSectorNumberToOne()
    {
        var state = new CyberDefenseEncounterState();

        // Defeat all 10 enemies to reach Sector 2
        for (var i = 0; i < 10; i++)
        {
            var hp = state.CurrentEnemy.MaxHitPoints;
            for (var hit = 0; hit < hp; hit++)
            {
                state.RecordCorrectAnswer();
            }
        }
        Assert.Equal(2, state.SectorNumber);

        state.Reset();

        Assert.Equal(1, state.SectorNumber);
        Assert.Equal(0, state.EnemyIndex);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, state.EnemyHitPoints);
    }
}
