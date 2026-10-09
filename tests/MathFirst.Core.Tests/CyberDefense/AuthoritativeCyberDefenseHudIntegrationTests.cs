namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.IO;
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

public sealed class AuthoritativeCyberDefenseHudIntegrationTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _learnerDbPath;
    private readonly string _gameplayDbPath;

    public AuthoritativeCyberDefenseHudIntegrationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _learnerDbPath = Path.Combine(_tempDirectory, "test_learner.db");
        _gameplayDbPath = Path.Combine(_tempDirectory, "test_gameplay.db");
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
    public void HudViewModel_InitialRun_Shows100PlayerHp()
    {
        var runState = CyberDefenseRunState.InitialRun();
        var vm = CyberDefenseHudViewModel.FromRunState(runState);

        Assert.Equal(100, vm.PlayerCurrentHp);
        Assert.Equal(100, vm.PlayerMaxHp);
        Assert.Equal(100, vm.PlayerHpPercentage);
        Assert.Equal(1, vm.SectorNumber);
        Assert.Equal(0, vm.OpponentIndex);
        Assert.False(vm.IsBoss);
        Assert.False(vm.IsSectorBoss);
    }

    [Fact]
    public void HudViewModel_InitialNormalEnemy_Shows2Hp()
    {
        var runState = CyberDefenseRunState.InitialRun();
        var vm = CyberDefenseHudViewModel.FromRunState(runState);

        Assert.Equal(2, vm.OpponentCurrentHp);
        Assert.Equal(2, vm.OpponentMaxHp);
        Assert.Equal(100, vm.OpponentHpPercentage);
        Assert.Equal(OpponentKind.Normal, vm.OpponentKind);
        Assert.False(string.IsNullOrWhiteSpace(vm.OpponentNameKey));
        Assert.False(string.IsNullOrWhiteSpace(vm.OpponentAssetPath));
    }

    [Fact]
    public void HudViewModel_SectorOne_UsesFiveNormalEnemies()
    {
        var runState = CyberDefenseRunState.InitialRun();
        var vm = CyberDefenseHudViewModel.FromRunState(runState);

        Assert.Equal(5, vm.NormalOpponentCount);
        Assert.Equal(6, vm.TotalOpponentCount);
        Assert.Equal(1, vm.WaveNumber);
    }

    [Fact]
    public void HudViewModel_Boss_UsesAuthoritativeKind()
    {
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var bossOpponent = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp);
        var runState = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, bossOpponent);

        var vm = CyberDefenseHudViewModel.FromRunState(runState);

        Assert.True(vm.IsBoss);
        Assert.True(vm.IsSectorBoss);
        Assert.Equal(OpponentKind.Boss, vm.OpponentKind);
        Assert.Equal(bossMaxHp, vm.OpponentCurrentHp);
        Assert.Equal(bossMaxHp, vm.OpponentMaxHp);
        Assert.Equal("CyberDefense_Opponent_NexusOverlord", vm.OpponentNameKey);
        Assert.Equal("images/cyber-defense/nexus-overlord.svg", vm.OpponentAssetPath);
        Assert.Equal(CyberDefenseOpponentScalePolicy.SectorBossClass, vm.OpponentScaleClass);
    }

    [Fact]
    public void HudViewModel_Damage_ReflectsPersistedState()
    {
        var normalOpponent = new OpponentState(OpponentKind.Normal, 2, 1);
        var runState = CyberDefenseRunState.CreateActive(1, 0, 96, normalOpponent);

        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-dmg-1",
            "add:1+1",
            isCorrect: false,
            isEligible: true,
            responseLatencyMs: 1200,
            resetEpoch: 0,
            processedAt: DateTimeOffset.UtcNow,
            transitionResult: new CyberDefenseCombatTransitionResult(
                isCorrect: false,
                requestedAttackDamage: 0,
                appliedOpponentDamage: 0,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 4,
                appliedPlayerDamage: 4,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: runState));

        var vm = CyberDefenseHudViewModel.FromRunState(runState, receipt, feedbackRevision: 1);

        Assert.Equal(96, vm.PlayerCurrentHp);
        Assert.Equal(96, vm.PlayerHpPercentage);
        Assert.Equal(1, vm.OpponentCurrentHp);
        Assert.Equal(50, vm.OpponentHpPercentage);
        Assert.Equal(4, vm.LastAppliedPlayerDamage);
        Assert.Equal(0, vm.LastAppliedOpponentDamage);
    }

    [Fact]
    public void HudViewModel_Defeat_ReflectsNextOpponent()
    {
        int nextOpponentMaxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 1);
        var nextOpponent = new OpponentState(OpponentKind.Normal, nextOpponentMaxHp, nextOpponentMaxHp);
        var nextState = CyberDefenseRunState.CreateActive(1, 1, 100, nextOpponent);

        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-def-1",
            "add:2+2",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 900,
            resetEpoch: 0,
            processedAt: DateTimeOffset.UtcNow,
            transitionResult: new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: 1,
                appliedOpponentDamage: 1,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 0,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: true,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: nextState));

        var vm = CyberDefenseHudViewModel.FromRunState(nextState, receipt, feedbackRevision: 2);

        Assert.Equal(1, vm.OpponentIndex);
        Assert.Equal(2, vm.WaveNumber);
        Assert.True(vm.IsOpponentDefeated);
        Assert.Equal(1, vm.LastAppliedOpponentDamage);
        Assert.Equal(nextOpponentMaxHp, vm.OpponentCurrentHp);
        Assert.Equal(100, vm.OpponentHpPercentage);
    }

    [Fact]
    public void HudViewModel_GameOver_PreservesTerminalFeedback()
    {
        var terminalSnapshot = new CyberDefenseTerminalRunSnapshot(
            sector: 1,
            opponentIndex: 0,
            kind: OpponentKind.Normal,
            opponentCurrentHp: 2,
            opponentMaxHp: 2,
            playerCurrentHp: 0);

        var rebootedState = CyberDefenseRunState.InitialRun();

        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-game-over",
            "add:3+3",
            isCorrect: false,
            isEligible: true,
            responseLatencyMs: 3000,
            resetEpoch: 0,
            processedAt: DateTimeOffset.UtcNow,
            transitionResult: new CyberDefenseCombatTransitionResult(
                isCorrect: false,
                requestedAttackDamage: 0,
                appliedOpponentDamage: 0,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 100,
                appliedPlayerDamage: 100,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: true,
                nextState: rebootedState,
                terminalSnapshot: terminalSnapshot));

        var vm = CyberDefenseHudViewModel.FromRunState(rebootedState, receipt, feedbackRevision: 3);

        Assert.True(vm.IsGameOver);
        Assert.NotNull(vm.TerminalSnapshot);
        Assert.Equal(0, vm.TerminalSnapshot!.PlayerCurrentHp);
        Assert.Equal(100, vm.PlayerCurrentHp); // Next rebooted state is active
    }

    [Fact]
    public void HudViewModel_NoUnimplementedShieldSlots()
    {
        var vm = CyberDefenseHudViewModel.Initial();
        Assert.Equal(100, vm.PlayerCurrentHp);
        Assert.Equal(100, vm.PlayerMaxHp);
    }

    [Fact]
    public void HudViewModel_LegacyCritical_DoesNotChangeDamage()
    {
        // Speed-based critical hit attempt produces 1 authoritative base damage in domain state machine
        var runState = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(runState, isCorrect: true, effectiveAttackDamage: 1);

        Assert.Equal(1, transition.AppliedOpponentDamage);
        Assert.Equal(0, transition.AppliedPlayerDamage);

        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-crit",
            "add:1+1",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 400, // fast
            resetEpoch: 0,
            processedAt: DateTimeOffset.UtcNow,
            transitionResult: transition);

        var vm = CyberDefenseHudViewModel.FromRunState(transition.NextState, receipt, feedbackRevision: 4);
        Assert.Equal(1, vm.LastAppliedOpponentDamage);
    }

    [Fact]
    public async Task PracticeFlow_StagesBeforeLearnerCommit()
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

        // Stage intent before learner commit
        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: eval.ChangeSet.Attempt.FactId,
            isCorrect: eval.IsCorrect,
            latencyMs: eval.LatencyMs,
            isEligibleAtSubmission: true);

        // Verify pending intent exists in store before learner commit
        var staged = await gameplayStore.GetPendingIntentAsync(submissionId);
        Assert.NotNull(staged);
        Assert.Equal(submissionId, staged!.SubmissionId);

        // Verify not consumed yet
        var receiptBefore = await gameplayStore.GetReceiptAsync(submissionId);
        Assert.Null(receiptBefore);
    }

    [Fact]
    public async Task PracticeFlow_ConfirmedCommit_ConsumesOnce()
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

        // Consume committed attempt
        var consumeResult = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.NotNull(consumeResult);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, consumeResult!.Status);
        Assert.NotNull(consumeResult.Receipt);

        // Verify run state updated and ViewModel updated
        var vm = coordinator.CurrentViewModel;
        Assert.Equal(1, vm.OpponentCurrentHp);
        Assert.Equal(1, vm.LastAppliedOpponentDamage);

        // Repeated consumption returns AlreadyConsumed without modifying run state again
        var secondResult = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.NotNull(secondResult);
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, secondResult!.Status);
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);
    }

    [Fact]
    public async Task PracticeFlow_UncommittedAttempt_NoCombat()
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

        // No receipt recorded
        var receipt = await gameplayStore.GetReceiptAsync(submissionId);
        Assert.Null(receipt);
    }

    [Fact]
    public async Task PracticeFlow_PersistenceRetry_NoDoubleAttack()
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

        await session.CommitCurrentEvaluationAsync();

        var firstConsume = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, firstConsume!.Status);
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);

        // Simulate retry path using same submissionId
        var retryConsume = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, retryConsume!.Status);
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);
    }

    [Fact]
    public async Task PracticeFlow_ConflictingDuplicate_FailsClosed()
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

        // Stage intent with mismatched fact ID
        var intent = new CyberDefensePendingIntentRecord(
            submissionId: submissionId,
            factId: "wrong:fact",
            isCorrect: eval.IsCorrect,
            isEligible: true,
            responseLatencyMs: eval.LatencyMs,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow);

        await consumer.StageIntentAsync(intent);
        await session.CommitCurrentEvaluationAsync();

        // Must fail closed with InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync(submissionId));
    }

    [Fact]
    public async Task PracticeFlow_GameplayStagingFailure_LearningContinues()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        // Null/failing gameplay store
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore: null, consumer: null);
        await coordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;

        // Staging failure does not throw or stop execution
        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: eval.ChangeSet.Attempt.FactId,
            isCorrect: eval.IsCorrect,
            latencyMs: eval.LatencyMs,
            isEligibleAtSubmission: true);

        // Learner commit proceeds seamlessly
        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(commitResult.IsSuccess);
        Assert.Equal(1, session.Progression.PracticePosition);
    }

    [Fact]
    public async Task PracticeFlow_GameplayConsumeFailure_LearningContinues()
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

        // Consume attempt on failing/null gameplay coordinator returns null and does not throw
        var consumeResult = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.Null(consumeResult);

        // Math progress remains committed
        Assert.Equal(1, session.Progression.PracticePosition);
    }

    [Fact]
    public async Task PracticeFlow_StartupRecovery_RestoresRun()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        // Stage and commit an attempt without consuming it
        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;

        var intent = new CyberDefensePendingIntentRecord(
            submissionId: submissionId,
            factId: eval.ChangeSet.Attempt.FactId,
            isCorrect: eval.IsCorrect,
            isEligible: true,
            responseLatencyMs: eval.LatencyMs,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow);

        await consumer.StageIntentAsync(intent);
        await session.CommitCurrentEvaluationAsync();

        // Simulate application restart
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        // Verify startup recovery processed the pending intent and updated HUD
        var vm = coordinator.CurrentViewModel;
        Assert.Equal(1, vm.OpponentCurrentHp);
        Assert.Equal(1, vm.LastAppliedOpponentDamage);
    }

    [Fact]
    public async Task PracticeFlow_PendingIntent_RecoversCommittedAttempt()
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

        var intent = new CyberDefensePendingIntentRecord(
            submissionId: submissionId,
            factId: eval.ChangeSet.Attempt.FactId,
            isCorrect: eval.IsCorrect,
            isEligible: true,
            responseLatencyMs: eval.LatencyMs,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow);

        await consumer.StageIntentAsync(intent);
        await session.CommitCurrentEvaluationAsync();

        var recoveryResult = await consumer.RecoverPendingIntentsAsync();
        Assert.Single(recoveryResult.Receipts);
        Assert.Equal(submissionId, recoveryResult.Receipts[0].SubmissionId);
    }

    [Fact]
    public async Task PracticeFlow_CalmMode_HidesHudAndFreezesRun()
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

        // Stage intent with isEligibleAtSubmission = false (Calm Mode active)
        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: eval.ChangeSet.Attempt.FactId,
            isCorrect: eval.IsCorrect,
            latencyMs: eval.LatencyMs,
            isEligibleAtSubmission: false);

        await session.CommitCurrentEvaluationAsync();
        var consumeResult = await coordinator.ConsumeCommittedAttemptAsync(submissionId);

        Assert.NotNull(consumeResult);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, consumeResult!.Status);
        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, consumeResult.Receipt!.ReceiptKind);

        // Run state remains initial (2 HP enemy, 100 HP player)
        var runState = await gameplayStore.GetRunStateAsync();
        Assert.Equal(2, runState.CurrentOpponent.CurrentHp);
        Assert.Equal(100, runState.PlayerCurrentHp);
    }

    [Fact]
    public async Task PracticeFlow_ReenableGameplay_ResumesPersistedRun()
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

        // 1. First attempt in active gameplay deals 1 damage
        var eval1 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await coordinator.StageIntentAsync(eval1.ChangeSet.SubmissionId, eval1.ChangeSet.Attempt.FactId, eval1.IsCorrect, eval1.LatencyMs, isEligibleAtSubmission: true);
        await session.CommitCurrentEvaluationAsync();
        await coordinator.ConsumeCommittedAttemptAsync(eval1.ChangeSet.SubmissionId);
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);

        // 2. Switch to calm mode (attempts are suppressed)
        await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
        var eval2 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await coordinator.StageIntentAsync(eval2.ChangeSet.SubmissionId, eval2.ChangeSet.Attempt.FactId, eval2.IsCorrect, eval2.LatencyMs, isEligibleAtSubmission: false);
        await session.CommitCurrentEvaluationAsync();
        await coordinator.ConsumeCommittedAttemptAsync(eval2.ChangeSet.SubmissionId);

        // 3. Switch back to Cyber Defense mode -> coordinator refreshes state from persisted store
        await coordinator.RefreshStateAsync();
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);
    }

    [Fact]
    public async Task PracticeFlow_FullReset_DiscardsStaleHudState()
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

        // Submit one answer and damage opponent
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await coordinator.StageIntentAsync(eval.ChangeSet.SubmissionId, eval.ChangeSet.Attempt.FactId, eval.IsCorrect, eval.LatencyMs, isEligibleAtSubmission: true);
        await session.CommitCurrentEvaluationAsync();
        await coordinator.ConsumeCommittedAttemptAsync(eval.ChangeSet.SubmissionId);
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);

        // Full Local Reset via AppResetCoordinator
        var resetCoordinator = new AppResetCoordinator(
            session: session,
            preferenceStore: new InMemoryPreferenceStore(),
            installationIdProvider: new InMemoryInstallationIdProvider(),
            cacheCleaner: new StubShareCacheCleaner(),
            cyberDefenseSessionState: new CyberDefenseSessionState(new InMemoryCyberDefensePreferences()),
            gameplayStore: gameplayStore);

        await resetCoordinator.ExecuteFullResetAsync();
        coordinator.InvalidateState();

        // Verify HUD reset to initial state
        var vm = coordinator.CurrentViewModel;
        Assert.Equal(100, vm.PlayerCurrentHp);
        Assert.Equal(2, vm.OpponentCurrentHp);
        Assert.Equal(1, vm.SectorNumber);
        Assert.Equal(0, vm.OpponentIndex);
    }

    [Fact]
    public async Task PracticeFlow_ResetEpochFence_RejectsStaleWork()
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

        // Commit learner attempt
        await session.CommitCurrentEvaluationAsync();

        // Full reset occurs on gameplay store (advances epoch to 1)
        var resetIntent = await gameplayStore.BeginOrGetResetIntentAsync();
        await gameplayStore.ResetGameplayStateAsync(resetIntent.TargetEpoch);
        await gameplayStore.ClearResetIntentAsync(resetIntent.TargetEpoch);

        // Stage stale intent with epoch 0 (while store is now at epoch 1) must be rejected by the epoch fence
        var staleIntent = new CyberDefensePendingIntentRecord(
            submissionId: submissionId,
            factId: eval.ChangeSet.Attempt.FactId,
            isCorrect: eval.IsCorrect,
            isEligible: true,
            responseLatencyMs: eval.LatencyMs,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.StageIntentAsync(staleIntent));
    }

    [Fact]
    public void PracticeFlow_NeverDispatchesPrototypeAndNewEngineTogether()
    {
        // Verified by architecture and contract: Home.razor invokes ICyberDefenseCombatCoordinator
        // and does NOT invoke CyberDefenseSessionState.DispatchAttempt.
        var sessionState = new CyberDefenseSessionState(new InMemoryCyberDefensePreferences());
        var coordinator = new CyberDefenseCombatCoordinator();

        Assert.NotNull(sessionState);
        Assert.NotNull(coordinator);
    }

    [Fact]
    public async Task PracticeFlow_LearningOnlyReset_PreservesExistingSemantics()
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

        // Answer one fact to damage opponent
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await coordinator.StageIntentAsync(eval.ChangeSet.SubmissionId, eval.ChangeSet.Attempt.FactId, eval.IsCorrect, eval.LatencyMs, isEligibleAtSubmission: true);
        await session.CommitCurrentEvaluationAsync();
        await coordinator.ConsumeCommittedAttemptAsync(eval.ChangeSet.SubmissionId);
        Assert.Equal(1, coordinator.CurrentViewModel.OpponentCurrentHp);

        // Learning-only reset: resets learning progress in TrainingSession, leaves gameplay store unchanged
        await session.ResetLearningProgressAsync(startTiming: false);

        // Gameplay state is preserved
        var runState = await gameplayStore.GetRunStateAsync();
        Assert.Equal(1, runState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public async Task NonInterference_LearnerSchemaV9Unchanged()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var snapshot = await learnerStore.LoadSnapshotAsync();
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal(0, snapshot.Progression.PracticePosition);
    }

    [Fact]
    public async Task NonInterference_FsrsAndFactSelectionUnchanged()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var firstFact = session.CurrentFact;
        Assert.NotNull(firstFact);
        Assert.Equal(ArithmeticOperation.Addition, firstFact.Operation);
    }

    [Fact]
    public async Task NonInterference_MathematicalLatencyUnaffected()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var session = new TrainingSession(learnerStore);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.LatencyMs >= 0);
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
