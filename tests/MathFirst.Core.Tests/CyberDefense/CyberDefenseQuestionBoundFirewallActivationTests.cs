namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Xunit;

public sealed class CyberDefenseQuestionBoundFirewallActivationTests : IDisposable
{
    private readonly string _testDirectory;

    public CyberDefenseQuestionBoundFirewallActivationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_FirewallActivation_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string GetTempDbPath(string name = "gameplay_activation_test")
    {
        return Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");
    }

    private static async Task SetProgressionAsync(
        SqliteGameplayStore store,
        int playerLevel,
        int earnedSp,
        int spentSp,
        int availableSp,
        int attackRank = 0,
        int firewallRank = 0,
        int isAssistanceActive = 0,
        long cumulativeXp = 0,
        long storeRevision = 1,
        long resetEpoch = 0)
    {
        long resolvedXp = cumulativeXp > 0
            ? cumulativeXp
            : CyberDefenseLevelPolicy.GetCumulativeXpThreshold(playerLevel);

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_progression
                SET player_level = @lvl,
                    earned_sp = @earned,
                    spent_sp = @spent,
                    available_sp = @available,
                    attack_rank = @atk,
                    firewall_rank = @fw,
                    is_beginner_assistance_active = @assist,
                    cumulative_xp = @xp,
                    store_revision = @rev,
                    reset_epoch = @epoch,
                    updated_at = @now
                WHERE id = 1;
            ";
            cmd.Parameters.AddWithValue("@lvl", playerLevel);
            cmd.Parameters.AddWithValue("@earned", earnedSp);
            cmd.Parameters.AddWithValue("@spent", spentSp);
            cmd.Parameters.AddWithValue("@available", availableSp);
            cmd.Parameters.AddWithValue("@atk", attackRank);
            cmd.Parameters.AddWithValue("@fw", firewallRank);
            cmd.Parameters.AddWithValue("@assist", isAssistanceActive);
            cmd.Parameters.AddWithValue("@xp", resolvedXp);
            cmd.Parameters.AddWithValue("@rev", storeRevision);
            cmd.Parameters.AddWithValue("@epoch", resetEpoch);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        });
    }

    private static async Task SetRunShieldStateAsync(SqliteGameplayStore store, int capacity, int active, int recharge)
    {
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_run_state
                SET shield_capacity = @cap,
                    active_shields = @active,
                    shield_recharge_units = @recharge
                WHERE id = 1;
            ";
            cmd.Parameters.AddWithValue("@cap", capacity);
            cmd.Parameters.AddWithValue("@active", active);
            cmd.Parameters.AddWithValue("@recharge", recharge);
            await cmd.ExecuteNonQueryAsync();
        });
    }

    private static CyberDefensePendingIntentRecord CreateIntent(
        string submissionId,
        bool isCorrect = false,
        bool isEligible = true,
        int effectiveDamage = 1,
        long resetEpoch = 0,
        string factId = "add:1+1")
    {
        return new CyberDefensePendingIntentRecord(
            submissionId: submissionId,
            factId: factId,
            isCorrect: isCorrect,
            isEligible: isEligible,
            responseLatencyMs: 500,
            resetEpoch: resetEpoch,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: effectiveDamage);
    }

    private static SubmissionChangeSet CreateTestChangeSet(
        string submissionId,
        string factId,
        bool isCorrect,
        long responseLatencyMs,
        long practicePosition = 1,
        long expectedRevision = 1)
    {
        var op = ArithmeticOperation.Addition;
        var stripped = factId.StartsWith("add:", StringComparison.Ordinal) ? factId.Substring(4) : factId;
        var parts = stripped.Split('+');
        int left = int.Parse(parts[0]);
        int right = int.Parse(parts[1]);
        int answer = left + right;

        var attempt = new AttemptRecord(
            submissionId: submissionId,
            factId: factId,
            operation: op,
            leftOperand: left,
            rightOperand: right,
            submittedAnswer: isCorrect ? answer : answer + 1,
            correctAnswer: answer,
            isCorrect: isCorrect,
            isFluent: isCorrect,
            responseLatencyMs: responseLatencyMs,
            timestamp: DateTimeOffset.UtcNow,
            outcome: isCorrect ? AttemptOutcome.Correct : AttemptOutcome.Incorrect,
            practicePosition: practicePosition);

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = practicePosition;
        progression.CurriculumStage = CurriculumStage.Stage1_Addition;

        var itemState = new ItemLearningState
        {
            FactId = factId,
            Operation = op,
            LeftOperand = left,
            RightOperand = right,
            TotalAttempts = 1,
            CorrectAttempts = isCorrect ? 1 : 0,
            IncorrectAttempts = isCorrect ? 0 : 1,
            ConsecutiveCorrectStreak = isCorrect ? 1 : 0,
            LastLatencyMs = responseLatencyMs,
            RollingLatencyMs = responseLatencyMs,
            FluentStreak = isCorrect ? 1 : 0,
            IsProvisionallyMastered = false,
            NeedsRemediation = !isCorrect,
            RemediationDueOrder = 0,
            LastPracticedOrder = (int)practicePosition,
            LastPracticedAt = DateTimeOffset.UtcNow
        };

        var opProgressions = progression.OperationProgressions;

        return new SubmissionChangeSet(
            submissionId: submissionId,
            ExpectedRevision: expectedRevision,
            attempt: attempt,
            updatedItemState: itemState,
            updatedProgression: progression,
            updatedFsrsState: null,
            operationProgressions: opProgressions);
    }

    // =========================================================================
    // Category A: Purchase Before New Question (DEC-02 In-Flight Protection)
    // =========================================================================

    [Fact]
    public async Task T01_PurchaseDuringQuestionQ10_DoesNotProtectQ10_IncorrectAnswerDealsDamage()
    {
        var dbPath = GetTempDbPath("mid_q10_purchase");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        // Level 5, 5 SP available, 0 Firewall rank
        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);

        // Q10 is presented
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 10);

        // Mid-question purchase: Player purchases Firewall Rank 1 while Q10 is active
        var prog = await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(1, prog.FirewallRank);

        // Run state in DB must remain unchanged during Q10
        var midRunState = await store.GetRunStateAsync();
        Assert.Equal(0, midRunState.ShieldCapacity);
        Assert.Equal(0, midRunState.ActiveShields);

        // Re-rendering or presentation callbacks for Q10 must not activate
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 10);
        var runStateAfterReRender = await store.GetRunStateAsync();
        Assert.Equal(0, runStateAfterReRender.ShieldCapacity);

        // Q10 is submitted incorrectly -> damage must NOT be absorbed
        var q10Intent = CreateIntent("sub-q10", isCorrect: false);
        var receipt = await store.ApplyAttemptTransactionAsync(q10Intent);

        Assert.False(receipt.TransitionResult!.IsShieldAbsorbed);
        Assert.Equal(4, receipt.TransitionResult.AppliedPlayerDamage);
        Assert.Equal(96, receipt.TransitionResult.NextState.PlayerCurrentHp);
    }

    [Fact]
    public async Task T02_AfterQ10Settled_AdvanceToQ11_ActivatesFirewallRank1()
    {
        var dbPath = GetTempDbPath("advance_to_q11");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);

        // Q10 active, purchase Rank 1
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 10);
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        // Q10 settled
        var q10Intent = CreateIntent("sub-q10", isCorrect: true);
        await store.ApplyAttemptTransactionAsync(q10Intent);

        // Now advancing to genuinely new question Q11
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 11);

        // Database run state must have activated Rank 1
        var q11RunState = await store.GetRunStateAsync();
        Assert.Equal(1, q11RunState.ShieldCapacity);
        Assert.Equal(1, q11RunState.ActiveShields);
        Assert.Equal(0, q11RunState.ShieldRechargeUnits);

        // Coordinator HUD ViewModel must also reflect the active shield
        Assert.Equal(100, coordinator.CurrentViewModel.PlayerCurrentHp);

        // Submitting an incorrect answer on Q11 must now be absorbed by the newly active shield
        var q11Intent = CreateIntent("sub-q11", isCorrect: false);
        var q11Receipt = await store.ApplyAttemptTransactionAsync(q11Intent);

        Assert.True(q11Receipt.TransitionResult!.IsShieldAbsorbed);
        Assert.Equal(0, q11Receipt.TransitionResult.AppliedPlayerDamage);
        Assert.Equal(100, q11Receipt.TransitionResult.NextState.PlayerCurrentHp);
        Assert.Equal(0, q11Receipt.TransitionResult.NextState.ActiveShields);
    }

    // =========================================================================
    // Category B: Multiple Capacity Upgrades & Recharge Retention
    // =========================================================================

    [Fact]
    public async Task T03_MultipleUpgrades_Rank1To2_GrantsOneActiveShield_PreservesDepletedSlotsAndRecharge()
    {
        var dbPath = GetTempDbPath("rank1_to_2_recharge");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        // Level 20, Rank 1 purchased, but current shield is depleted (0 active) with 18 recharge units
        await SetProgressionAsync(store, playerLevel: 20, earnedSp: 20, spentSp: 3, availableSp: 17, firewallRank: 1);
        await SetRunShieldStateAsync(store, capacity: 1, active: 0, recharge: 18);

        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 10);

        // Purchase Rank 2 (cost 8 SP)
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 1, expectedResetEpoch: 0);

        // Before Q11: State remains capacity 1, active 0, recharge 18
        var preAdvance = await store.GetRunStateAsync();
        Assert.Equal(1, preAdvance.ShieldCapacity);
        Assert.Equal(0, preAdvance.ActiveShields);
        Assert.Equal(18, preAdvance.ShieldRechargeUnits);

        // Advance to Q11
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 11);

        // After Q11: Capacity becomes 2, exactly 1 newly purchased active shield added (0 + 1 = 1),
        // depleted older slot remains depleted, and 18 recharge units preserved!
        var postAdvance = await store.GetRunStateAsync();
        Assert.Equal(2, postAdvance.ShieldCapacity);
        Assert.Equal(1, postAdvance.ActiveShields);
        Assert.Equal(18, postAdvance.ShieldRechargeUnits);
    }

    [Fact]
    public async Task T04_MultipleUpgrades_Rank2To3_WhenFullyCharged_ZeroesRechargeUnits()
    {
        var dbPath = GetTempDbPath("rank2_to_3_full");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        // Level 50, Rank 2, 2 active shields (fully charged)
        await SetProgressionAsync(store, playerLevel: 50, earnedSp: 50, spentSp: 11, availableSp: 39, firewallRank: 2);
        await SetRunShieldStateAsync(store, capacity: 2, active: 2, recharge: 0);

        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 10);
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 2, expectedResetEpoch: 0);

        // Advance to Q11
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 11);

        var postAdvance = await store.GetRunStateAsync();
        Assert.Equal(3, postAdvance.ShieldCapacity);
        Assert.Equal(3, postAdvance.ActiveShields);
        Assert.Equal(0, postAdvance.ShieldRechargeUnits);
    }

    // =========================================================================
    // Category C: Idempotency & Concurrency
    // =========================================================================

    [Fact]
    public async Task T05_Idempotency_RepeatedPresentationForSameQuestion_DoesNotDuplicateActivation()
    {
        var dbPath = GetTempDbPath("idempotent_duplicate");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 10);
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        // First presentation of Q11
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 11);
        var first = await store.GetRunStateAsync();
        Assert.Equal(1, first.ShieldCapacity);
        Assert.Equal(1, first.ActiveShields);

        // Repeated presentations of Q11
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 11);
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 11);

        var repeated = await store.GetRunStateAsync();
        Assert.Equal(1, repeated.ShieldCapacity);
        Assert.Equal(1, repeated.ActiveShields);
    }

    [Fact]
    public async Task T06_Idempotency_ConcurrentActivationRequests_SafelySynchronized()
    {
        var dbPath = GetTempDbPath("concurrent_activation");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 10);
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        // 10 concurrent activation requests for Q11
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 11))
            .ToArray();

        await Task.WhenAll(tasks);

        var finalRunState = await store.GetRunStateAsync();
        Assert.Equal(1, finalRunState.ShieldCapacity);
        Assert.Equal(1, finalRunState.ActiveShields);
    }

    [Fact]
    public async Task T07_Idempotency_DelayedCallbackFromOlderQuestion_Ignored()
    {
        var dbPath = GetTempDbPath("delayed_older_callback");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 10);
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        // Delayed callback with questionKey 9 or 10 must not activate
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 9);
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 10);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(0, runState.ShieldCapacity);
        Assert.Equal(0, runState.ActiveShields);
    }

    // =========================================================================
    // Category D: Durable Recovery & Cold Restart
    // =========================================================================

    [Fact]
    public async Task T08_DurableRecovery_ColdRestartBeforeActivation_PreCrashIntentSettlesWithOldCapacity()
    {
        var dbPath = GetTempDbPath("crash_recovery_intent");

        // 1. Session before crash
        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
            await SetProgressionAsync(store1, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);

            // Intent for Q10 is staged
            var intent = CreateIntent("sub-pre-crash", isCorrect: false);
            await store1.StagePendingIntentAsync(intent);

            // Purchase Rank 1 occurs while Q10 was active
            await store1.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

            // Crash happens before intent was consumed!
        }

        // 2. Post-crash recovery session
        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();

            // Intended recovery consumes the pre-crash intent
            var pendingIntents = await store2.GetPendingIntentsAsync(resetEpoch: 0);
            Assert.Single(pendingIntents);

            // Consuming Q10 intent MUST use old capacity (0), taking damage
            var receipt = await store2.ApplyAttemptTransactionAsync(pendingIntents[0]);
            Assert.False(receipt.TransitionResult!.IsShieldAbsorbed);
            Assert.Equal(96, receipt.TransitionResult.NextState.PlayerCurrentHp);

            // Now, post-restart newly presented question Q1 can activate the purchased capacity!
            var coordinator2 = new CyberDefenseCombatCoordinator(store2, null);
            await coordinator2.InitializeAsync();
            await coordinator2.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 1);

            var postRestartRunState = await store2.GetRunStateAsync();
            Assert.Equal(1, postRestartRunState.ShieldCapacity);
            Assert.Equal(1, postRestartRunState.ActiveShields);
            Assert.Equal(96, postRestartRunState.PlayerCurrentHp);
        }
    }

    [Fact]
    public async Task T09_DurableRecovery_ColdRestartAfterActivation_NoDuplication()
    {
        var dbPath = GetTempDbPath("restart_after_activation");

        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
            await SetProgressionAsync(store1, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);
            var coordinator1 = new CyberDefenseCombatCoordinator(store1, null);
            await coordinator1.InitializeAsync();

            await coordinator1.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 10);
            await store1.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

            // Activated on Q11
            await coordinator1.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 11);
            var state = await store1.GetRunStateAsync();
            Assert.Equal(1, state.ShieldCapacity);
            Assert.Equal(1, state.ActiveShields);
        }

        // Restart store and coordinator
        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();
            var coordinator2 = new CyberDefenseCombatCoordinator(store2, null);
            await coordinator2.InitializeAsync();

            // New question post restart
            await coordinator2.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 1);

            var state2 = await store2.GetRunStateAsync();
            Assert.Equal(1, state2.ShieldCapacity);
            Assert.Equal(1, state2.ActiveShields);
        }
    }

    // =========================================================================
    // Category E: Gameplay Invariants
    // =========================================================================

    [Fact]
    public async Task T10_GameplayInvariants_ZeroRank_ActivationIsDeterministicNoOp()
    {
        var dbPath = GetTempDbPath("zero_rank_noop");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        // Fresh game, Rank 0
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 1);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(0, runState.ShieldCapacity);
        Assert.Equal(0, runState.ActiveShields);
        Assert.Equal(0, runState.ShieldRechargeUnits);
        Assert.Equal(100, runState.PlayerCurrentHp);
    }

    [Fact]
    public async Task T11_GameplayInvariants_AttackRankAndHpPreservedDuringFirewallActivation()
    {
        var dbPath = GetTempDbPath("preserve_invariants");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        // Level 5, AttackRank 2, FirewallRank 1 purchased
        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 5, availableSp: 0, attackRank: 2, firewallRank: 1);

        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 1);

        var runState = await store.GetRunStateAsync();
        var prog = await store.GetProgressionAsync();

        Assert.Equal(1, runState.ShieldCapacity);
        Assert.Equal(2, prog.AttackRank);
        Assert.Equal(100, runState.PlayerCurrentHp);
        Assert.Equal(5, checked(prog.SpentSkillPoints + prog.AvailableSkillPoints));
    }

    // =========================================================================
    // Category F: Failure Handling & Epoch Fencing
    // =========================================================================

    [Fact]
    public async Task T12_FailureHandling_PendingReset_StoreRejectsActivation()
    {
        var dbPath = GetTempDbPath("reject_pending_reset");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 1);
        await store.BeginOrGetResetIntentAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 0));
    }

    [Fact]
    public async Task T13_FailureHandling_EpochMismatch_StoreRejectsActivation()
    {
        var dbPath = GetTempDbPath("reject_epoch_mismatch");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 1, resetEpoch: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 1));
    }

    [Fact]
    public async Task T14_FailureHandling_UnsettledPendingIntent_ReturnsDeferredPendingIntent()
    {
        var dbPath = GetTempDbPath("defer_unsettled_intent");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 1);

        // Intent is pending in store
        var intent = CreateIntent("unsettled-intent-1");
        await store.StagePendingIntentAsync(intent);

        // Store must defer activation while unsettled intent exists
        var result = await store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 0);
        Assert.Equal(CyberDefenseFirewallActivationStatus.DeferredPendingIntent, result.Status);
    }

    [Fact]
    public async Task T15_FailureHandling_CoordinatorIsolatesStoreFailure()
    {
        var dbPath = GetTempDbPath("failure_isolation");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        // Induce reset intent to cause store activation to throw
        await store.BeginOrGetResetIntentAsync();

        // Coordinator must safely catch and isolate the store failure without throwing
        var ex = await Record.ExceptionAsync(() =>
            coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 5));

        Assert.Null(ex);
    }

    // =========================================================================
    // Category G: Remediation Regression Suites (BLK-01, BLK-02, MAJOR-01)
    // =========================================================================

    [Fact]
    public async Task T16_BLK01_DelayedPresentationActivation_DoesNotReadFirewallRankPurchasedMidQuestion()
    {
        var dbPath = GetTempDbPath("blk01_delayed_activation");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);

        // Purchase Firewall Rank 1 mid-question
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        // Act: Execute activation bound to Q10's presentation-time entitlement (target capacity = 0)
        var result = await store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 0, expectedResetEpoch: 0);

        // Assert: Must NOT activate Rank 1 capacity for Q10! Target was 0.
        // Status must be AlreadyCurrent / NoOp, NOT Applied!
        Assert.Equal(CyberDefenseFirewallActivationStatus.AlreadyCurrent, result.Status);
        var runState = await store.GetRunStateAsync();
        Assert.Equal(0, runState.ShieldCapacity);
        Assert.Equal(0, runState.ActiveShields);
    }

    [Fact]
    public async Task T17_BLK01_Coordinator_PurchaseDuringQ10_DoesNotRetroactivelyUpgradeQ10Entitlement()
    {
        var dbPath = GetTempDbPath("blk01_coordinator_q10");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);

        // Q10 is presented when player has Rank 0
        await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 10);

        // Player purchases Rank 1 mid-question
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        // Run state must remain unshielded during Q10
        var stateDuringQ10 = await store.GetRunStateAsync();
        Assert.Equal(0, stateDuringQ10.ShieldCapacity);
        Assert.Equal(0, stateDuringQ10.ActiveShields);

        // Submission await for Q10 must NOT activate the shield
        await coordinator.AwaitQuestionBoundFirewallActivationAsync(questionKey: 10);
        var stateAfterAwait = await store.GetRunStateAsync();
        Assert.Equal(0, stateAfterAwait.ShieldCapacity);
        Assert.Equal(0, stateAfterAwait.ActiveShields);
    }

    [Fact]
    public async Task T18_BLK02_SubmissionPath_DoesNotInitiateActivationForNewlyPurchasedShield()
    {
        var dbPath = GetTempDbPath("blk02_submission");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, null);
        await coordinator.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);

        // Player purchases Rank 1 during Q10 without presentation activation having run
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        // At submission time, Home.razor calls AwaitQuestionBoundFirewallActivationAsync
        // which must NOT initiate a new activation for Q10!
        await coordinator.AwaitQuestionBoundFirewallActivationAsync(questionKey: 10);

        // Store must remain at capacity 0
        var state = await store.GetRunStateAsync();
        Assert.Equal(0, state.ShieldCapacity);
        Assert.Equal(0, state.ActiveShields);
    }

    [Fact]
    public async Task T19_MAJOR01_UnsettledPendingIntent_ReturnsDeferredPendingIntent_AndRetriesSafelyAfterSettlement()
    {
        var dbPath = GetTempDbPath("major01_deferral_retry");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        var learnerDbPath = Path.Combine(_testDirectory, "learner_" + Guid.NewGuid().ToString("N") + ".db");
        using var learnerStore = new SqliteLearnerStore(learnerDbPath);
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Level 5, Rank 1 purchased
        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 3, availableSp: 2, firewallRank: 1);

        // Stage an unsettled intent for Q10
        var q10Intent = CreateIntent("sub-q10", isCorrect: true);
        await store.StagePendingIntentAsync(q10Intent);

        // Q11 is presented while Q10 is unsettled:
        // Direct store activation must return DeferredPendingIntent (NOT throw, NOT return Applied)
        var storeResult = await store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 0);
        Assert.Equal(CyberDefenseFirewallActivationStatus.DeferredPendingIntent, storeResult.Status);

        // Database run state must still be at capacity 0 (not activated yet)
        var unactivatedState = await store.GetRunStateAsync();
        Assert.Equal(0, unactivatedState.ShieldCapacity);

        // Coordinator activation for Q11 also reports DeferredPendingIntent
        var coordResult = await coordinator.ActivateQuestionBoundFirewallCapacityAsync(questionKey: 11);
        Assert.NotNull(coordResult);
        Assert.Equal(CyberDefenseFirewallActivationStatus.DeferredPendingIntent, coordResult.Status);

        // Now Q10 settles via consumption
        var changeSet = CreateTestChangeSet("sub-q10", "add:1+1", true, 500);
        await learnerStore.CommitSubmissionAsync(changeSet);
        var consumptionResult = await coordinator.ConsumeCommittedAttemptAsync("sub-q10");
        Assert.NotNull(consumptionResult);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, consumptionResult.Status);

        // After settlement, Q11 activation retries safely and activates 1 shield!
        var settledState = await store.GetRunStateAsync();
        Assert.Equal(1, settledState.ShieldCapacity);
        Assert.Equal(1, settledState.ActiveShields);
    }
}
