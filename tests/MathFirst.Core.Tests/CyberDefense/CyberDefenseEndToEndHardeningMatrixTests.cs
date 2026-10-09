namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Xunit;

/// <summary>
/// End-to-end hardening and regression matrix covering all 20 required production coordination scenarios (A through T)
/// as specified in MF-CYBER-003 Slice 6/6.
/// Validates the full production pipeline across Home/TrainingSession/CombatCoordinator/SubmissionConsumer/Sqlite Stores/HUD.
/// </summary>
public sealed class CyberDefenseEndToEndHardeningMatrixTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _learnerDbPath;
    private readonly string _gameplayDbPath;

    public CyberDefenseEndToEndHardeningMatrixTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "MathFirstE2E_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _learnerDbPath = Path.Combine(_tempDirectory, "learner_e2e.db");
        _gameplayDbPath = Path.Combine(_tempDirectory, "gameplay_e2e.db");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort temp cleanup
        }
    }

    private SqliteLearnerStore CreateLearnerStore() => new(_learnerDbPath);
    private SqliteGameplayStore CreateGameplayStore() => new(_gameplayDbPath);

    [Fact]
    public async Task ScenarioA_CorrectAnswer_DealsOneDamageAndAdvancesLearner()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;

        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: eval.ChangeSet.Attempt.FactId,
            isCorrect: eval.IsCorrect,
            latencyMs: eval.LatencyMs,
            isEligibleAtSubmission: true);

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(commitResult.IsSuccess);

        var consumeResult = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.NotNull(consumeResult);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, consumeResult!.Status);

        var vm = coordinator.CurrentViewModel;
        Assert.Equal(1, vm.OpponentCurrentHp);
        Assert.Equal(2, vm.OpponentMaxHp);
        Assert.Equal(100, vm.PlayerCurrentHp);
        Assert.Equal(1, vm.LastAppliedOpponentDamage);
        Assert.Equal(0, vm.LastAppliedPlayerDamage);
        Assert.Equal(1, session.Progression.PracticePosition);
    }

    [Fact]
    public async Task ScenarioB_IncorrectAnswer_AppliesDomainCounterDamage()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        // Submit wrong answer
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
        Assert.False(eval.IsCorrect);
        var submissionId = eval.ChangeSet.SubmissionId;

        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: eval.ChangeSet.Attempt.FactId,
            isCorrect: eval.IsCorrect,
            latencyMs: eval.LatencyMs,
            isEligibleAtSubmission: true);

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(commitResult.IsSuccess);

        var consumeResult = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.NotNull(consumeResult);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, consumeResult!.Status);

        var vm = coordinator.CurrentViewModel;
        Assert.Equal(2, vm.OpponentCurrentHp); // Opponent takes no damage
        Assert.Equal(96, vm.PlayerCurrentHp);  // 100 - 4 counter-damage
        Assert.Equal(4, vm.LastAppliedPlayerDamage);
        Assert.Equal(0, vm.LastAppliedOpponentDamage);
    }

    [Fact]
    public async Task ScenarioC_OpponentDefeat_AdvancesToNextOpponentAndAppliesDefeatHealing()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        // 1. Take damage so player is at 96 HP
        var wrongEval = session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
        await coordinator.StageIntentAsync(wrongEval.ChangeSet.SubmissionId, wrongEval.ChangeSet.Attempt.FactId, wrongEval.IsCorrect, wrongEval.LatencyMs, isEligibleAtSubmission: true);
        await session.CommitCurrentEvaluationAsync();
        await coordinator.ConsumeCommittedAttemptAsync(wrongEval.ChangeSet.SubmissionId);
        await session.AcknowledgeFeedbackAsync(startTiming: false);
        Assert.Equal(96, coordinator.CurrentViewModel.PlayerCurrentHp);

        // 2. Hit 1: 1 damage to enemy (HP 2 -> 1)
        var hit1 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await coordinator.StageIntentAsync(hit1.ChangeSet.SubmissionId, hit1.ChangeSet.Attempt.FactId, hit1.IsCorrect, hit1.LatencyMs, isEligibleAtSubmission: true);
        await session.CommitCurrentEvaluationAsync();
        await coordinator.ConsumeCommittedAttemptAsync(hit1.ChangeSet.SubmissionId);
        await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);

        // 3. Hit 2: 1 damage defeating enemy 0. Normal defeat heals 2 HP (96 -> 98 HP). Next enemy is OpponentIndex 1.
        var hit2 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await coordinator.StageIntentAsync(hit2.ChangeSet.SubmissionId, hit2.ChangeSet.Attempt.FactId, hit2.IsCorrect, hit2.LatencyMs, isEligibleAtSubmission: true);
        await session.CommitCurrentEvaluationAsync();
        await coordinator.ConsumeCommittedAttemptAsync(hit2.ChangeSet.SubmissionId);

        var vm = coordinator.CurrentViewModel;
        Assert.Equal(1, vm.OpponentIndex);
        Assert.Equal(2, vm.WaveNumber);
        Assert.True(vm.IsOpponentDefeated);
        Assert.Equal(98, vm.PlayerCurrentHp);
        Assert.Equal(CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 1), vm.OpponentCurrentHp);
    }

    [Fact]
    public async Task ScenarioD_BossDefeat_AdvancesSectorAndAppliesBossHealing()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        // Defeat 5 normal opponents in sector 1 (5 normal enemies * 2 HP = 10 hits)
        for (int normalHit = 1; normalHit <= 10; normalHit++)
        {
            if (session.InteractionState == SessionInteractionState.SessionCheckIn)
            {
                await session.ContinuePracticeAsync(startTiming: false);
            }

            var hit = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await coordinator.StageIntentAsync(hit.ChangeSet.SubmissionId, hit.ChangeSet.Attempt.FactId, hit.IsCorrect, hit.LatencyMs, isEligibleAtSubmission: true);
            await session.CommitCurrentEvaluationAsync();
            await coordinator.ConsumeCommittedAttemptAsync(hit.ChangeSet.SubmissionId);
            await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
        }

        if (session.InteractionState == SessionInteractionState.SessionCheckIn)
        {
            await session.ContinuePracticeAsync(startTiming: false);
        }

        // We are now facing the Sector 1 Boss (OpponentIndex 5, 12 HP)
        Assert.Equal(5, coordinator.CurrentViewModel.OpponentIndex);
        Assert.True(coordinator.CurrentViewModel.IsBoss);
        Assert.Equal(12, coordinator.CurrentViewModel.OpponentCurrentHp);

        // Deal 12 hits to defeat the Sector 1 Boss
        for (int bossHit = 1; bossHit <= 12; bossHit++)
        {
            if (session.InteractionState == SessionInteractionState.SessionCheckIn)
            {
                await session.ContinuePracticeAsync(startTiming: false);
            }

            var hit = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await coordinator.StageIntentAsync(hit.ChangeSet.SubmissionId, hit.ChangeSet.Attempt.FactId, hit.IsCorrect, hit.LatencyMs, isEligibleAtSubmission: true);
            await session.CommitCurrentEvaluationAsync();
            await coordinator.ConsumeCommittedAttemptAsync(hit.ChangeSet.SubmissionId);
            if (bossHit < 12)
            {
                await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
            }
        }

        // Sector advances to 2, OpponentIndex resets to 0, Boss defeat healing applied (clamped to 100)
        var vm = coordinator.CurrentViewModel;
        Assert.Equal(2, vm.SectorNumber);
        Assert.Equal(0, vm.OpponentIndex);
        Assert.Equal(1, vm.WaveNumber);
        Assert.True(vm.IsOpponentDefeated);
        Assert.Equal(100, vm.PlayerCurrentHp);
        Assert.Equal(CyberDefenseScalingPolicy.GetOpponentMaxHp(2, 0), vm.OpponentCurrentHp);
    }

    [Fact]
    public async Task ScenarioE_GameOver_PreservesTerminalSnapshotAndRebootsInitialRun()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        // 25 incorrect answers * 4 counter damage = 100 damage (lethal)
        SubmissionEvaluation? lastWrongEval = null;
        for (int i = 1; i <= 25; i++)
        {
            if (session.InteractionState == SessionInteractionState.TeachingIntervention)
            {
                await session.AcknowledgeTeachingInterventionAsync(startTiming: false);
            }
            if (session.InteractionState == SessionInteractionState.SessionCheckIn)
            {
                await session.ContinuePracticeAsync(startTiming: false);
            }

            lastWrongEval = session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
            await coordinator.StageIntentAsync(lastWrongEval.ChangeSet.SubmissionId, lastWrongEval.ChangeSet.Attempt.FactId, lastWrongEval.IsCorrect, lastWrongEval.LatencyMs, isEligibleAtSubmission: true);
            await session.CommitCurrentEvaluationAsync();
            await coordinator.ConsumeCommittedAttemptAsync(lastWrongEval.ChangeSet.SubmissionId);
            if (i < 25)
            {
                await session.AcknowledgeFeedbackAsync(startTiming: false);
                if (session.InteractionState == SessionInteractionState.TeachingIntervention)
                {
                    await session.AcknowledgeTeachingInterventionAsync(startTiming: false);
                }
                if (session.InteractionState == SessionInteractionState.SessionCheckIn)
                {
                    await session.ContinuePracticeAsync(startTiming: false);
                }
            }
        }

        Assert.NotNull(lastWrongEval);
        var receipt = await gameplayStore.GetReceiptAsync(lastWrongEval!.ChangeSet.SubmissionId);
        Assert.NotNull(receipt);
        Assert.True(receipt!.TransitionResult!.IsGameOver);
        Assert.NotNull(receipt.TransitionResult.TerminalSnapshot);
        Assert.Equal(0, receipt.TransitionResult.TerminalSnapshot!.PlayerCurrentHp);

        // Active stored run is rebooted to initial run
        var persistedRun = await gameplayStore.GetRunStateAsync();
        Assert.Equal(100, persistedRun.PlayerCurrentHp);
        Assert.Equal(1, persistedRun.Sector);
        Assert.Equal(0, persistedRun.OpponentIndex);

        // HUD ViewModel shows game over with terminal feedback
        var vm = coordinator.CurrentViewModel;
        Assert.True(vm.IsGameOver);
        Assert.NotNull(vm.TerminalSnapshot);
        Assert.Equal(0, vm.TerminalSnapshot!.PlayerCurrentHp);
        Assert.Equal(100, vm.PlayerCurrentHp);
    }

    [Fact]
    public async Task ScenarioF_LearnerCommitRejected_NoCombatApplied()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;

        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: eval.ChangeSet.Attempt.FactId,
            isCorrect: eval.IsCorrect,
            latencyMs: eval.LatencyMs,
            isEligibleAtSubmission: true);

        // Do NOT commit learner attempt
        var consumeResult = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.NotNull(consumeResult);
        Assert.Equal(CyberDefenseConsumptionStatus.UncommittedLearnerAttempt, consumeResult!.Status);

        var receipt = await gameplayStore.GetReceiptAsync(submissionId);
        Assert.Null(receipt);
        Assert.Equal(2, coordinator.CurrentViewModel.OpponentCurrentHp);
    }

    [Fact]
    public async Task ScenarioG_GameplayStagingFailure_LearnerCommitProceedsSeamlessly()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        // Coordinator with null/failing gameplay store
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore: null, consumer: null);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;

        // Stage intent does not throw or prevent progression
        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: eval.ChangeSet.Attempt.FactId,
            isCorrect: eval.IsCorrect,
            latencyMs: eval.LatencyMs,
            isEligibleAtSubmission: true);

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(commitResult.IsSuccess);
        Assert.Equal(1, session.Progression.PracticePosition);
    }

    [Fact]
    public async Task ScenarioH_GameplayConsumptionFailure_LearnerProgressRemainsCommitted()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore: null, consumer: null);

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(commitResult.IsSuccess);

        var consumeResult = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.Null(consumeResult);
        Assert.Equal(1, session.Progression.PracticePosition);
    }

    [Fact]
    public async Task ScenarioI_DuplicateSubmissionId_ExactlyOneGameplayTransition()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;

        await coordinator.StageIntentAsync(submissionId, eval.ChangeSet.Attempt.FactId, eval.IsCorrect, eval.LatencyMs, isEligibleAtSubmission: true);
        await session.CommitCurrentEvaluationAsync();

        var firstConsume = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, firstConsume!.Status);
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);

        var secondConsume = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, secondConsume!.Status);
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);
    }

    [Fact]
    public async Task ScenarioJ_ConflictingDuplicatePayload_FailsClosed()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;

        // Stage intent with conflicting fact ID
        var intent = new CyberDefensePendingIntentRecord(
            submissionId: submissionId,
            factId: "conflicting:fact:payload",
            isCorrect: eval.IsCorrect,
            isEligible: true,
            responseLatencyMs: eval.LatencyMs,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow);

        await consumer.StageIntentAsync(intent);
        await session.CommitCurrentEvaluationAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync(submissionId));
    }

    [Fact]
    public async Task ScenarioK_AppRestart_RehydratesPersistedRunState()
    {
        // 1. Session 1: Deal damage and persist
        using (var learnerStore = CreateLearnerStore())
        using (var gameplayStore = CreateGameplayStore())
        {
            await learnerStore.InitializeAsync();
            await gameplayStore.InitializeAsync();

            var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
            var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
            await coordinator.InitializeAsync();

            var session = new TrainingSession(learnerStore);
            await session.InitializeAsync(startTiming: false);

            var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await coordinator.StageIntentAsync(eval.ChangeSet.SubmissionId, eval.ChangeSet.Attempt.FactId, eval.IsCorrect, eval.LatencyMs, isEligibleAtSubmission: true);
            await session.CommitCurrentEvaluationAsync();
            await coordinator.ConsumeCommittedAttemptAsync(eval.ChangeSet.SubmissionId);
            Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);
        }

        // 2. Session 2 (simulating app restart): Rehydrate from persistent store
        using (var learnerStore2 = CreateLearnerStore())
        using (var gameplayStore2 = CreateGameplayStore())
        {
            await learnerStore2.InitializeAsync();
            await gameplayStore2.InitializeAsync();

            var consumer2 = new CyberDefenseSubmissionConsumer(gameplayStore2, learnerStore2);
            var coordinator2 = new CyberDefenseCombatCoordinator(gameplayStore2, consumer2);
            await coordinator2.InitializeAsync();

            var vm = coordinator2.CurrentViewModel;
            Assert.Equal(1, vm.OpponentCurrentHp);
            Assert.Equal(100, vm.PlayerCurrentHp);
            Assert.Equal(1, vm.SectorNumber);
            Assert.Equal(0, vm.OpponentIndex);
        }
    }

    [Fact]
    public async Task ScenarioL_CrashAfterStagingBeforeLearnerCommit_NoUnconfirmedCombat()
    {
        // 1. Session 1: Stage intent, then crash before learner commit
        using (var learnerStore = CreateLearnerStore())
        using (var gameplayStore = CreateGameplayStore())
        {
            await learnerStore.InitializeAsync();
            await gameplayStore.InitializeAsync();

            var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
            var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
            await coordinator.InitializeAsync();

            var session = new TrainingSession(learnerStore);
            await session.InitializeAsync(startTiming: false);

            var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await coordinator.StageIntentAsync(eval.ChangeSet.SubmissionId, eval.ChangeSet.Attempt.FactId, eval.IsCorrect, eval.LatencyMs, isEligibleAtSubmission: true);
            // Crash before session.CommitCurrentEvaluationAsync()
        }

        // 2. Session 2 (restart): Recovery finds uncommitted intent and does not apply combat
        using (var learnerStore2 = CreateLearnerStore())
        using (var gameplayStore2 = CreateGameplayStore())
        {
            await learnerStore2.InitializeAsync();
            await gameplayStore2.InitializeAsync();

            var consumer2 = new CyberDefenseSubmissionConsumer(gameplayStore2, learnerStore2);
            var coordinator2 = new CyberDefenseCombatCoordinator(gameplayStore2, consumer2);
            await coordinator2.InitializeAsync();

            var vm = coordinator2.CurrentViewModel;
            Assert.Equal(2, vm.OpponentCurrentHp); // Still initial 2 HP, uncommitted attempt was not applied
        }
    }

    [Fact]
    public async Task ScenarioM_CrashAfterLearnerCommitBeforeConsumption_RecoveryProcessesIntent()
    {
        string submissionId;
        // 1. Session 1: Stage and commit, then crash before consume
        using (var learnerStore = CreateLearnerStore())
        using (var gameplayStore = CreateGameplayStore())
        {
            await learnerStore.InitializeAsync();
            await gameplayStore.InitializeAsync();

            var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
            var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
            await coordinator.InitializeAsync();

            var session = new TrainingSession(learnerStore);
            await session.InitializeAsync(startTiming: false);

            var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            submissionId = eval.ChangeSet.SubmissionId;

            await coordinator.StageIntentAsync(submissionId, eval.ChangeSet.Attempt.FactId, eval.IsCorrect, eval.LatencyMs, isEligibleAtSubmission: true);
            await session.CommitCurrentEvaluationAsync();
            // Crash before consume
        }

        // 2. Session 2 (restart): Startup recovery finds committed attempt and consumes it
        using (var learnerStore2 = CreateLearnerStore())
        using (var gameplayStore2 = CreateGameplayStore())
        {
            await learnerStore2.InitializeAsync();
            await gameplayStore2.InitializeAsync();

            var consumer2 = new CyberDefenseSubmissionConsumer(gameplayStore2, learnerStore2);
            var coordinator2 = new CyberDefenseCombatCoordinator(gameplayStore2, consumer2);
            await coordinator2.InitializeAsync();

            var vm = coordinator2.CurrentViewModel;
            Assert.Equal(1, vm.OpponentCurrentHp);
            Assert.Equal(1, vm.LastAppliedOpponentDamage);

            var receipt = await gameplayStore2.GetReceiptAsync(submissionId);
            Assert.NotNull(receipt);
        }
    }

    [Fact]
    public async Task ScenarioN_CrashAfterGameplayTransactionCommitBeforeAck_ReturnsCachedReceipt()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;

        var intent = new CyberDefensePendingIntentRecord(submissionId, eval.ChangeSet.Attempt.FactId, eval.IsCorrect, isEligible: true, eval.LatencyMs, resetEpoch: 0, createdAt: DateTimeOffset.UtcNow);
        await consumer.StageIntentAsync(intent);
        await session.CommitCurrentEvaluationAsync();

        // First consume succeeds
        var result1 = await consumer.ConsumeAttemptAsync(submissionId);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, result1.Status);

        // Retry consume returns AlreadyConsumed with cached receipt
        var result2 = await consumer.ConsumeAttemptAsync(submissionId);
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, result2.Status);
        Assert.Equal(result1.Receipt!.SubmissionId, result2.Receipt!.SubmissionId);
    }

    [Fact]
    public async Task ScenarioO_CalmModeSubmission_NoCombatMutation()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;

        // Stage intent with isEligibleAtSubmission = false
        await coordinator.StageIntentAsync(submissionId, eval.ChangeSet.Attempt.FactId, eval.IsCorrect, eval.LatencyMs, isEligibleAtSubmission: false);
        await session.CommitCurrentEvaluationAsync();

        var consumeResult = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.NotNull(consumeResult);
        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, consumeResult!.Receipt!.ReceiptKind);

        var runState = await gameplayStore.GetRunStateAsync();
        Assert.Equal(2, runState.CurrentOpponent.CurrentHp);
        Assert.Equal(100, runState.PlayerCurrentHp);
    }

    [Fact]
    public async Task ScenarioP_ReenableCyberDefenseAfterCalmMode_ResumesPersistedRun()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        // Active mode: deal 1 damage
        var eval1 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await coordinator.StageIntentAsync(eval1.ChangeSet.SubmissionId, eval1.ChangeSet.Attempt.FactId, eval1.IsCorrect, eval1.LatencyMs, isEligibleAtSubmission: true);
        await session.CommitCurrentEvaluationAsync();
        await coordinator.ConsumeCommittedAttemptAsync(eval1.ChangeSet.SubmissionId);
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);

        // Calm mode: 2 attempts
        for (int i = 0; i < 2; i++)
        {
            await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
            var calmEval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await coordinator.StageIntentAsync(calmEval.ChangeSet.SubmissionId, calmEval.ChangeSet.Attempt.FactId, calmEval.IsCorrect, calmEval.LatencyMs, isEligibleAtSubmission: false);
            await session.CommitCurrentEvaluationAsync();
            await coordinator.ConsumeCommittedAttemptAsync(calmEval.ChangeSet.SubmissionId);
        }

        // Toggle back to Cyber Defense mode
        await coordinator.RefreshStateAsync();
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);
    }

    [Fact]
    public async Task ScenarioQ_FullLocalReset_CleansLearnerAndGameplayStateWithNewEpoch()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await coordinator.StageIntentAsync(eval.ChangeSet.SubmissionId, eval.ChangeSet.Attempt.FactId, eval.IsCorrect, eval.LatencyMs, isEligibleAtSubmission: true);
        await session.CommitCurrentEvaluationAsync();
        await coordinator.ConsumeCommittedAttemptAsync(eval.ChangeSet.SubmissionId);

        var resetCoordinator = new AppResetCoordinator(
            session: session,
            preferenceStore: new InMemoryPreferenceStore(),
            installationIdProvider: new InMemoryInstallationIdProvider(),
            cacheCleaner: new StubShareCacheCleaner(),
            cyberDefenseSessionState: new CyberDefenseSessionState(new InMemoryCyberDefensePreferences()),
            gameplayStore: gameplayStore);

        await resetCoordinator.ExecuteFullResetAsync();
        coordinator.InvalidateState();

        var vm = coordinator.CurrentViewModel;
        Assert.Equal(100, vm.PlayerCurrentHp);
        Assert.Equal(2, vm.OpponentCurrentHp);
        Assert.Equal(1, vm.SectorNumber);
        Assert.Equal(0, vm.OpponentIndex);

        var epoch = await gameplayStore.GetResetEpochAsync();
        Assert.Equal(1, epoch);
    }

    [Fact]
    public async Task ScenarioR_LearningOnlyReset_PreservesGameplayRunState()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await coordinator.StageIntentAsync(eval.ChangeSet.SubmissionId, eval.ChangeSet.Attempt.FactId, eval.IsCorrect, eval.LatencyMs, isEligibleAtSubmission: true);
        await session.CommitCurrentEvaluationAsync();
        await coordinator.ConsumeCommittedAttemptAsync(eval.ChangeSet.SubmissionId);
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);

        await session.ResetLearningProgressAsync(startTiming: false);

        var runState = await gameplayStore.GetRunStateAsync();
        Assert.Equal(1, runState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public async Task ScenarioS_StalePreResetLearnerSubmission_RejectedByRevisionFence()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var staleChangeSet = eval.ChangeSet;
        Assert.Equal(1, staleChangeSet.ExpectedRevision);

        // Perform learning reset, advancing revision to 2
        await learnerStore.ResetLearningProgressAsync();

        var postResetSnapshot = await learnerStore.LoadSnapshotAsync();
        Assert.Equal(2, postResetSnapshot.Revision);

        var commitResult = await learnerStore.CommitSubmissionAsync(staleChangeSet);
        Assert.False(commitResult.IsSuccess);
        Assert.Equal(PersistenceStatus.RevisionConflict, commitResult.Status);
    }

    [Fact]
    public async Task ScenarioT_StalePreResetGameplayIntent_RejectedByEpochFence()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        // Reset gameplay store, advancing epoch to 1
        var resetIntent = await gameplayStore.BeginOrGetResetIntentAsync();
        await gameplayStore.ResetGameplayStateAsync(resetIntent.TargetEpoch);
        await gameplayStore.ClearResetIntentAsync(resetIntent.TargetEpoch);

        // Stale intent with epoch 0
        var staleIntent = new CyberDefensePendingIntentRecord(
            submissionId: "stale-intent-epoch-0",
            factId: "add:1+1",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 800,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.StageIntentAsync(staleIntent));
    }

    [Fact]
    public async Task Concurrency_CoordinatorThreadSafety_SimultaneousReadersAndWriters()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var exceptions = new List<Exception>();

        var readTasks = new List<Task>();
        for (int i = 0; i < 8; i++)
        {
            readTasks.Add(Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var vm = coordinator.CurrentViewModel;
                        Assert.NotNull(vm);
                        Assert.True(vm.PlayerCurrentHp >= 0);
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }
            }));
        }

        var writeTasks = new List<Task>();
        for (int i = 0; i < 4; i++)
        {
            writeTasks.Add(Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        coordinator.InvalidateState();
                        await coordinator.RefreshStateAsync();
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }
            }));
        }

        await Task.WhenAll(readTasks.Concat(writeTasks));
        Assert.Empty(exceptions);
    }

    private sealed class InMemoryPreferenceStore : IPreferenceStore
    {
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "system";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => true;
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => PracticeOperationPreferencePolicy.AllOperations;
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public void ResetPracticePreferences() { }
        public void ResetAllPreferences() { }
    }

    private sealed class InMemoryInstallationIdProvider : IInstallationIdProvider
    {
        private string? _id = "test-install-id";
        public string GetOrCreateInstallationId() => _id ??= Guid.NewGuid().ToString("N");
        public void ClearInstallationId() => _id = null;
    }

    private sealed class StubShareCacheCleaner : ITelemetryShareCacheCleaner
    {
        public void PurgeShareCache() { }
    }

    private sealed class InMemoryCyberDefensePreferences : ICyberDefenseModePreferences
    {
        public bool GetCyberDefenseEnabled() => true;
        public void SetCyberDefenseEnabled(bool enabled) { }
    }
}
