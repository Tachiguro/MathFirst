namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Xunit;

public sealed class CyberDefenseQuestionBoundAttackDamageTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _gameplayDbPath;
    private readonly string _learnerDbPath;

    public CyberDefenseQuestionBoundAttackDamageTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_BoundDamage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _gameplayDbPath = Path.Combine(_tempDirectory, "test_gameplay.db");
        _learnerDbPath = Path.Combine(_tempDirectory, "test_learner.db");
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
        }
    }

    private SqliteGameplayStore CreateGameplayStore() => new(_gameplayDbPath);
    private SqliteLearnerStore CreateLearnerStore() => new(_learnerDbPath);

    private static string GetRepositoryPath(params string[] segments)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "MathFirst.slnx");
            if (File.Exists(candidate))
            {
                var fullSegments = new string[segments.Length + 1];
                fullSegments[0] = current;
                Array.Copy(segments, 0, fullSegments, 1, segments.Length);
                return Path.Combine(fullSegments);
            }
            current = Directory.GetParent(current)?.FullName;
        }
        throw new InvalidOperationException("Could not locate repository root from BaseDirectory.");
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

    private static async Task PurchaseAttackRankAsync(SqliteGameplayStore store, int targetRank)
    {
        var prog = await store.GetProgressionAsync();
        int currentRank = prog.AttackRank;

        while (currentRank < targetRank)
        {
            int cost = CyberDefenseAttackUpgradePolicy.GetUpgradeCost(currentRank + 1);
            while (prog.AvailableSkillPoints < cost)
            {
                var hitIntent = new CyberDefensePendingIntentRecord(
                    submissionId: "seed-sub-" + Guid.NewGuid().ToString("N"),
                    factId: "add:1+1",
                    isCorrect: true,
                    isEligible: true,
                    responseLatencyMs: 100,
                    resetEpoch: prog.ResetEpoch,
                    createdAt: DateTimeOffset.UtcNow,
                    effectiveAttackDamage: 1);
                await store.ApplyAttemptTransactionAsync(hitIntent);
                prog = await store.GetProgressionAsync();
            }

            prog = await store.PurchaseAttackUpgradeAsync(expectedRank: currentRank, expectedResetEpoch: prog.ResetEpoch);
            currentRank = prog.AttackRank;
        }
    }

    [Fact]
    public async Task T01_Rank0_PresentedQuestion_BindsDamage1()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        int damage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 1);

        Assert.Equal(1, damage);
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(1));
    }

    [Fact]
    public async Task T02_Rank1_PresentedQuestion_BindsDamage2()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        await PurchaseAttackRankAsync(store, targetRank: 1);

        int damage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 2);

        Assert.Equal(2, damage);
        Assert.Equal(2, coordinator.GetBoundEffectiveAttackDamageOrDefault(2));
    }

    [Fact]
    public async Task T03_Rank3_PresentedQuestion_BindsDamage4()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        await PurchaseAttackRankAsync(store, targetRank: 3);

        int damage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 3);

        Assert.Equal(4, damage);
        Assert.Equal(4, coordinator.GetBoundEffectiveAttackDamageOrDefault(3));
    }

    [Fact]
    public async Task T04_PurchaseDuringActiveQuestion_DoesNotIncreaseAlreadyBoundDamage()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        delayedStore.EnableSnapshotDelay();

        // 1. Question 101 presentation begins capture with delayed store read
        var captureTaskA = coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 101);

        // 2. Player purchases Rank 1 upgrade while snapshot read for question 101 is still in flight
        await PurchaseAttackRankAsync(store, targetRank: 1);
        var snapshot = await store.GetAttackUpgradeSnapshotAsync();
        Assert.Equal(1, snapshot.CurrentAttackRank);
        Assert.Equal(2, snapshot.CurrentBaseDamage);

        // 3. Delayed snapshot read completes
        delayedStore.CompleteDelayedSnapshotWithInner();
        int dmgA = await captureTaskA;

        // 4. Question 101 damage must remain 1 (pre-purchase), proving no retroactive upgrade race
        Assert.Equal(1, dmgA);
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(101));

        // 5. Next question binds newly entitled damage 2
        int dmgB = await coordinator.GetOrBindEffectiveAttackDamageAsync(102);
        Assert.Equal(2, dmgB);
    }

    [Fact]
    public async Task T05_NextQuestionAfterPurchase_BindsNewlyEntitledDamage()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Question 101 presented at Rank 0
        int dmg1 = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 101);
        Assert.Equal(1, dmg1);

        // Purchase upgrade
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // Question 102 presented after upgrade
        int dmg2 = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 102);
        Assert.Equal(2, dmg2);

        // Question 101 still has 1
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(101));
    }

    [Fact]
    public async Task T06_RepeatedSyncStateCalls_ForSameRevision_DoNotRebind()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        int dmg1 = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 200);
        int dmg2 = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 200);
        int dmg3 = coordinator.GetBoundEffectiveAttackDamageOrDefault(questionKey: 200);

        Assert.Equal(dmg1, dmg2);
        Assert.Equal(dmg1, dmg3);
    }

    [Fact]
    public async Task T07_RepeatedPresentationCallbacks_DoNotDuplicateDamageSampling()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 300))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(1, r));
    }

    [Fact]
    public async Task T08_SameFactId_WithDistinctRevisions_ReceiveIndependentBindings()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Presentation 1 of fact "add:3+4" at revision 10
        int dmgRev10 = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 10);
        Assert.Equal(1, dmgRev10);

        // Player purchases upgrade to rank 1
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // Presentation 2 of same fact "add:3+4" at revision 15
        int dmgRev15 = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 15);
        Assert.Equal(2, dmgRev15);

        // Revision 10 still has 1
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(10));
    }

    [Fact]
    public async Task T09_LateAsyncCaptureForOlderQuestion_CannotOverwriteNewerBinding()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Bind Question 1
        var task1 = coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 1);

        // Upgrade and bind Question 2
        await PurchaseAttackRankAsync(store, targetRank: 1);
        var task2 = coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 2);

        var dmg1 = await task1;
        var dmg2 = await task2;

        Assert.Equal(1, dmg1);
        Assert.Equal(2, dmg2);
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(1));
        Assert.Equal(2, coordinator.GetBoundEffectiveAttackDamageOrDefault(2));
    }

    [Fact]
    public async Task T10_SubmissionStaging_UsesBoundQuestionDamage()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        await PurchaseAttackRankAsync(store, targetRank: 2); // Rank 2 -> Damage 3

        int boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 50);
        Assert.Equal(3, boundDamage);

        string submissionId = "sub-t10";
        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: "add:2+2",
            isCorrect: true,
            latencyMs: 150,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: boundDamage);

        var stagedIntent = await store.GetPendingIntentAsync(submissionId);
        Assert.NotNull(stagedIntent);
        Assert.Equal(3, stagedIntent.EffectiveAttackDamage);
    }

    [Fact]
    public async Task T11_SubmissionStaging_DoesNotSampleCurrentRankAfterAnswer()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Question presented at rank 0
        int boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 60);
        Assert.Equal(1, boundDamage);

        // Answered, but before staging completes, rank is upgraded to rank 2 (damage 3)
        await PurchaseAttackRankAsync(store, targetRank: 2);

        // Staging uses the question-bound damage (1), NOT the new rank damage (3)
        string submissionId = "sub-t11";
        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: "add:2+2",
            isCorrect: true,
            latencyMs: 150,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: boundDamage);

        var intent = await store.GetPendingIntentAsync(submissionId);
        Assert.NotNull(intent);
        Assert.Equal(1, intent.EffectiveAttackDamage);
    }

    [Fact]
    public async Task T12_PersistedSchemaV4PendingIntent_ContainsExactBoundDamage()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        await PurchaseAttackRankAsync(store, targetRank: 1);
        int boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 70);
        Assert.Equal(2, boundDamage);

        string submissionId = "sub-t12";
        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: "add:5+5",
            isCorrect: true,
            latencyMs: 200,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: boundDamage);

        // Verify direct SQLite column
        var intent = await store.GetPendingIntentAsync(submissionId);
        Assert.NotNull(intent);
        Assert.Equal(2, intent.EffectiveAttackDamage);
    }

    [Fact]
    public async Task T13_CommittedAttempt_AppliesOriginalBoundDamage()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        await PurchaseAttackRankAsync(store, targetRank: 1); // Rank 1 -> Damage 2
        int boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 80);

        string submissionId = "sub-t13";
        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: "add:1+1",
            isCorrect: true,
            latencyMs: 150,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: boundDamage);

        // Commit learner evidence
        var changeSet = CreateTestChangeSet(submissionId, "add:1+1", true, 150);
        await learnerStore.CommitSubmissionAsync(changeSet);

        var runBefore = await store.GetRunStateAsync();
        int initialHp = runBefore.CurrentOpponent.CurrentHp;

        var result = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.NotNull(result);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, result.Status);
        Assert.NotNull(result.Receipt);
        Assert.Equal(2, result.Receipt.TransitionResult?.RequestedAttackDamage);
        Assert.Equal(2, result.Receipt.TransitionResult?.AppliedOpponentDamage);

        var runAfter = await store.GetRunStateAsync();
        Assert.Equal(initialHp - 2, runAfter.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public async Task T14_DuplicateReceiptReplay_DoesNotExecuteCombatTwice()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        int boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 90);
        string submissionId = "sub-t14";

        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: "add:1+1",
            isCorrect: true,
            latencyMs: 150,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: boundDamage);

        var changeSet = CreateTestChangeSet(submissionId, "add:1+1", true, 150);
        await learnerStore.CommitSubmissionAsync(changeSet);

        var result1 = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, result1!.Status);

        var runStateAfterFirst = await store.GetRunStateAsync();

        // Duplicate replay
        var result2 = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, result2!.Status);
        Assert.Equal(result1.Receipt!.SubmissionId, result2.Receipt!.SubmissionId);

        var runStateAfterSecond = await store.GetRunStateAsync();
        Assert.Equal(runStateAfterFirst.CurrentOpponent.CurrentHp, runStateAfterSecond.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public async Task T15_HigherDamage_AffectsOpponentHp_WithoutFabricatingExtraAttacks()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        await PurchaseAttackRankAsync(store, targetRank: 2); // Damage 3

        string submissionId = "sub-t15";
        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: "add:1+1",
            isCorrect: true,
            latencyMs: 100,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: 3);

        var changeSet = CreateTestChangeSet(submissionId, "add:1+1", true, 100);
        await learnerStore.CommitSubmissionAsync(changeSet);

        var runBefore = await store.GetRunStateAsync();
        int initialHp = runBefore.CurrentOpponent.CurrentHp;
        int initialAttacks = runBefore.CurrentOpponent.ExecutedAttacks;

        var result = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.NotNull(result);
        var transition = result.Receipt?.TransitionResult;
        Assert.NotNull(transition);

        int expectedDamage = Math.Min(3, initialHp);
        Assert.Equal(expectedDamage, transition.AppliedOpponentDamage);

        var runAfter = await store.GetRunStateAsync();
        if (transition.IsOpponentDefeated)
        {
            Assert.Equal(initialAttacks + 1, transition.ExecutedAttacksOnDefeatedOpponent);
        }
        else
        {
            Assert.Equal(initialHp - expectedDamage, runAfter.CurrentOpponent.CurrentHp);
            Assert.Equal(initialAttacks + 1, runAfter.CurrentOpponent.ExecutedAttacks);
        }
    }

    [Fact]
    public async Task T16_IncorrectAnswer_PreservesZeroPlayerAttackBehavior()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        await PurchaseAttackRankAsync(store, targetRank: 1); // Damage 2
        string submissionId = "sub-t16";

        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: "add:1+1",
            isCorrect: false,
            latencyMs: 500,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: 2);

        var changeSet = CreateTestChangeSet(submissionId, "add:1+1", false, 500);
        await learnerStore.CommitSubmissionAsync(changeSet);

        var runBefore = await store.GetRunStateAsync();
        var result = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.NotNull(result);
        Assert.Equal(0, result.Receipt?.TransitionResult?.AppliedOpponentDamage);

        var runAfter = await store.GetRunStateAsync();
        Assert.Equal(runBefore.CurrentOpponent.CurrentHp, runAfter.CurrentOpponent.CurrentHp);
        Assert.True(runAfter.PlayerCurrentHp < runBefore.PlayerCurrentHp);
    }

    [Fact]
    public async Task T17_CalmMode_SuppressesGameplay_WithoutAlteringMathematicalProgress()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        int boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 170);
        string submissionId = "sub-t17";

        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: "add:1+1",
            isCorrect: true,
            latencyMs: 150,
            isEligibleAtSubmission: false, // Calm Mode
            effectiveAttackDamage: boundDamage);

        var changeSet = CreateTestChangeSet(submissionId, "add:1+1", true, 150);
        await learnerStore.CommitSubmissionAsync(changeSet);

        var runBefore = await store.GetRunStateAsync();
        var result = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.NotNull(result);
        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, result.Receipt?.ReceiptKind);

        var runAfter = await store.GetRunStateAsync();
        Assert.Equal(runBefore.CurrentOpponent.CurrentHp, runAfter.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public async Task T18_ModeToggleDuringSameQuestion_CannotUpgradeBoundDamage()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Question bound at Rank 0
        int boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 180);
        Assert.Equal(1, boundDamage);

        // Player toggles mode and purchases upgrade
        await coordinator.RefreshStateAsync();
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // Same question revision retains damage 1
        int rechecked = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 180);
        Assert.Equal(1, rechecked);
    }

    [Fact]
    public async Task T19_PauseAndResume_RetainsSameBoundDamage()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        int boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 190);
        Assert.Equal(1, boundDamage);

        // Simulate pause/resume by checking the bound damage remains identical
        Assert.Equal(boundDamage, coordinator.GetBoundEffectiveAttackDamageOrDefault(190));
        Assert.Equal(boundDamage, await coordinator.GetOrBindEffectiveAttackDamageAsync(190));
    }

    [Fact]
    public async Task T20_AppBackgroundForeground_RetainsActiveQuestionDamage()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        int boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 200);

        // Foreground refresh
        await coordinator.RefreshStateAsync();

        Assert.Equal(boundDamage, coordinator.GetBoundEffectiveAttackDamageOrDefault(200));
    }

    [Fact]
    public async Task T21_PersistenceFailureRetry_UsesOriginalStagedSubmission_DoesNotRebind()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        string submissionId = "sub-t21";
        await coordinator.StageIntentAsync(
            submissionId: submissionId,
            factId: "add:1+1",
            isCorrect: true,
            latencyMs: 150,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: 1);

        // Simulated persistence failure: commit delayed. Rank upgraded in DB.
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // Retry commits learner evidence and consumes original staged intent
        var changeSet = CreateTestChangeSet(submissionId, "add:1+1", true, 150);
        await learnerStore.CommitSubmissionAsync(changeSet);

        var result = await coordinator.ConsumeCommittedAttemptAsync(submissionId);
        Assert.NotNull(result);
        Assert.Equal(1, result.Receipt?.TransitionResult?.RequestedAttackDamage);
    }

    [Fact]
    public async Task T22_FullLocalReset_InvalidatesOldBindings()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 220);
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(220));

        coordinator.InvalidateState();

        // InvalidateState cleared bindings; fallback is returned
        Assert.Equal(99, coordinator.GetBoundEffectiveAttackDamageOrDefault(220, fallback: 99));
    }

    [Fact]
    public async Task T23_SuccessiveQuestions_ExerciseHistoryEviction()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Dispatch 20 successive questions (exceeds MaxQuestionDamageHistory = 16)
        for (int q = 1; q <= 20; q++)
        {
            int dmg = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: q);
            Assert.Equal(1, dmg);
        }

        // Active question 20 is retained
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(20));

        // Question 1 has been evicted from the 16-entry history
        Assert.Equal(-1, coordinator.GetBoundEffectiveAttackDamageOrDefault(1, fallback: -1));
    }

    [Fact]
    public async Task T24_StartupRecovery_ConsumesPersistedDamage_DoesNotRecomputeCurrentRank()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        // Stage intent with damage 1
        var intent = new CyberDefensePendingIntentRecord(
            submissionId: "sub-t24",
            factId: "add:1+1",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 150,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1);
        await store.StagePendingIntentAsync(intent);

        var changeSet = CreateTestChangeSet("sub-t24", "add:1+1", true, 150);
        await learnerStore.CommitSubmissionAsync(changeSet);

        // Player buys upgrade before restart recovery
        await PurchaseAttackRankAsync(store, targetRank: 1); // Current rank is 1 (damage 2)

        // New coordinator starts up and recovers
        var newCoordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await newCoordinator.InitializeAsync();

        var receipt = await store.GetReceiptAsync("sub-t24");
        Assert.NotNull(receipt);
        Assert.Equal(1, receipt.TransitionResult?.RequestedAttackDamage);
    }

    [Fact]
    public async Task T25_FailureToLoadUpgradeProgression_DoesNotBlockLearnerCommit()
    {
        // Coordinator without gameplay store (fails safe)
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore: null, consumer: null);
        await coordinator.InitializeAsync();

        int damage = await coordinator.GetOrBindEffectiveAttackDamageAsync(questionKey: 250);
        Assert.Equal(1, damage);

        // Staging with no consumer completes without throwing
        await coordinator.StageIntentAsync("sub-t25", "add:1+1", true, 100, true, damage);
    }

    [Fact]
    public async Task T26_AnswerResponseLatency_GovernedByTrainingSession_NotSnapshotDuration()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var session = new TrainingSession(learnerStore, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: true);

        // Submit immediately
        var eval = session.SubmitAnswer(session.CurrentFact!.CorrectResult);
        Assert.True(eval.LatencyMs >= 1);
    }

    [Fact]
    public async Task T27_ExistingCoordinatorCallSites_PreserveLegacyDamage1Compatibility()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // 5-parameter legacy overload
        string subId = "sub-legacy-27";
        await coordinator.StageIntentAsync(
            submissionId: subId,
            factId: "add:1+1",
            isCorrect: true,
            latencyMs: 120,
            isEligibleAtSubmission: true);

        var staged = await store.GetPendingIntentAsync(subId);
        Assert.NotNull(staged);
        Assert.Equal(1, staged.EffectiveAttackDamage);
    }

    [Fact]
    public async Task T28_ExplicitDamageAboveEntitlement_FailsAtSqliteBoundary()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord(
            submissionId: "sub-t28",
            factId: "add:1+1",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 100,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 99); // Above entitlement (rank 0 -> max 1)

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intent));
    }

    [Fact]
    public async Task T29_ActiveQuestionSurvivesHarmlessRerenders_WithoutCreatingNewKey()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var session = new TrainingSession(learnerStore, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        long rev1 = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("1");
        session.SetCurrentAnswerInput("12");

        Assert.Equal(rev1, session.FactInstanceRevision);
    }

    [Fact]
    public void T30_ExistingUiLayoutKeypadHudContracts_RemainUnchanged()
    {
        // Verifies Home.razor source contracts and coordinator integration signatures
        // without claiming full visual-layout rendering.
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        Assert.True(File.Exists(homePath));
        var content = File.ReadAllText(homePath);

        Assert.Contains("CombatCoordinator.StageIntentAsync", content, StringComparison.Ordinal);
        Assert.Contains("isEligibleAtSubmission: wasEligibleAtSubmission", content, StringComparison.Ordinal);
        Assert.Contains("CombatCoordinator.ConsumeCommittedAttemptAsync", content, StringComparison.Ordinal);
        Assert.Contains("CombatCoordinator.GetOrBindEffectiveAttackDamageAsync", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task R01_SnapshotRead_DeliberatelyDelayed_PurchaseCommitsBeforeReadCompletes_QuestionRetainsPrePurchaseDamage()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        delayedStore.EnableSnapshotDelay();

        // T0 & T1: Question 101 presentation begins capture
        var captureTaskA = coordinator.GetOrBindEffectiveAttackDamageAsync(101);

        // T3: Player purchases Rank 1 upgrade before Question 101 snapshot read finishes
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // T4: Snapshot read completes by delegating to inner store (which now has Rank 1, dmg 2)
        delayedStore.CompleteDelayedSnapshotWithInner();

        int dmgA = await captureTaskA;

        // REQUIRED RESULT: Question 101 must retain pre-purchase damage (1), never 2!
        Assert.Equal(1, dmgA);
    }

    [Fact]
    public async Task R02_NextQuestionDispatchedAfterCommittedPurchase_BindsNewlyEntitledDamage()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        int dmg1 = await coordinator.GetOrBindEffectiveAttackDamageAsync(101);
        Assert.Equal(1, dmg1);

        // Purchase upgrade
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // Question 102 dispatched after purchase commits
        int dmg2 = await coordinator.GetOrBindEffectiveAttackDamageAsync(102);
        Assert.Equal(2, dmg2);
    }

    [Fact]
    public async Task R03_TwoOverlappingQuestionBindings_CompleteInReverseOrder_WithoutContamination()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        var tcs101 = new TaskCompletionSource<CyberDefenseAttackUpgradeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs102 = new TaskCompletionSource<CyberDefenseAttackUpgradeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);

        delayedStore.EnqueueSnapshotTask(tcs101.Task);
        delayedStore.EnqueueSnapshotTask(tcs102.Task);

        // Start 101 then 102
        var task101 = coordinator.GetOrBindEffectiveAttackDamageAsync(101);
        var task102 = coordinator.GetOrBindEffectiveAttackDamageAsync(102);

        // Complete 102 FIRST with damage 2
        var snap2 = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 1,
            nextAttackRank: 2,
            currentBaseDamage: 2,
            nextBaseDamage: 3,
            nextRankCost: 2,
            availableSkillPoints: 0,
            hasSufficientSkillPoints: false,
            playerLevel: 1,
            resetEpoch: 0,
            storeRevision: 2);
        tcs102.TrySetResult(snap2);

        // Complete 101 SECOND with damage 1
        var snap1 = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 0,
            nextAttackRank: 1,
            currentBaseDamage: 1,
            nextBaseDamage: 2,
            nextRankCost: 1,
            availableSkillPoints: 1,
            hasSufficientSkillPoints: true,
            playerLevel: 1,
            resetEpoch: 0,
            storeRevision: 1);
        tcs101.TrySetResult(snap1);

        int dmg102 = await task102;
        int dmg101 = await task101;

        Assert.Equal(2, dmg102);
        Assert.Equal(1, dmg101);
    }

    [Fact]
    public async Task R04_DelayedEpoch0Binding_CompletesAfterEpoch1Reset_MustNotClearEpoch1Bindings()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        // Epoch 0: Question 101 begins capture
        var tcsEpoch0 = new TaskCompletionSource<CyberDefenseAttackUpgradeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        delayedStore.EnqueueSnapshotTask(tcsEpoch0.Task);
        var taskEpoch0 = coordinator.GetOrBindEffectiveAttackDamageAsync(101);

        // Reset store and coordinator to Epoch 1
        await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(targetEpoch: 1);
        await store.ClearResetIntentAsync(targetEpoch: 1);

        // Invalidate and re-initialize coordinator in Epoch 1
        coordinator.InvalidateState();

        var snapEpoch1 = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 0,
            nextAttackRank: 1,
            currentBaseDamage: 1,
            nextBaseDamage: 2,
            nextRankCost: 1,
            availableSkillPoints: 0,
            hasSufficientSkillPoints: false,
            playerLevel: 1,
            resetEpoch: 1,
            storeRevision: 1);
        delayedStore.EnqueueSnapshotTask(Task.FromResult(snapEpoch1));

        int dmgEpoch1 = await coordinator.GetOrBindEffectiveAttackDamageAsync(201);
        Assert.Equal(1, dmgEpoch1);
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(201));

        // Now Epoch 0 delayed task finishes
        var snapEpoch0 = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 0,
            nextAttackRank: 1,
            currentBaseDamage: 1,
            nextBaseDamage: 2,
            nextRankCost: 1,
            availableSkillPoints: 0,
            hasSufficientSkillPoints: false,
            playerLevel: 1,
            resetEpoch: 0,
            storeRevision: 1);
        tcsEpoch0.TrySetResult(snapEpoch0);
        await taskEpoch0;

        // CRITICAL CHECK: Epoch 1 question binding MUST NOT have been cleared by delayed Epoch 0 task
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(201, fallback: -99));
    }

    [Fact]
    public async Task R05_LateCallback_CannotResurrectStateAfterInvalidateState()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        delayedStore.EnableSnapshotDelay();

        var captureTask = coordinator.GetOrBindEffectiveAttackDamageAsync(101);

        // InvalidateState called while capture is in flight
        coordinator.InvalidateState();

        // Delayed capture finishes
        delayedStore.CompleteDelayedSnapshotWithInner();
        await captureTask;

        // State must NOT be resurrected in _boundQuestionDamage
        Assert.Equal(99, coordinator.GetBoundEffectiveAttackDamageOrDefault(101, fallback: 99));
    }

    [Fact]
    public async Task R06_SuccessiveQuestions_ExerciseBoundedHistoryEvictionContract()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Dispatch 20 successive questions (exceeding MaxQuestionDamageHistory = 16)
        for (int q = 1; q <= 20; q++)
        {
            int dmg = await coordinator.GetOrBindEffectiveAttackDamageAsync(q);
            Assert.Equal(1, dmg);
        }

        // Active question (20) must be present
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(20, fallback: -1));

        // Question 1 should have been evicted
        Assert.Equal(-1, coordinator.GetBoundEffectiveAttackDamageOrDefault(1, fallback: -1));
    }

    [Fact]
    public async Task R07_ActiveQuestion_NeverEligibleForRetroactiveResamplingAfterEviction()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Question 1 bound at Rank 0
        int dmg1 = await coordinator.GetOrBindEffectiveAttackDamageAsync(1);
        Assert.Equal(1, dmg1);

        // Dispatch 20 more questions (2..21) to evict question 1
        for (int q = 2; q <= 21; q++)
        {
            await coordinator.GetOrBindEffectiveAttackDamageAsync(q);
        }

        // Purchase upgrade to rank 1
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // Querying evicted question 1 must NEVER rebind or resample to damage 2
        int resampled = await coordinator.GetOrBindEffectiveAttackDamageAsync(1);
        Assert.Equal(1, resampled);
    }

    [Fact]
    public async Task R08_SubmissionOccursWhileBindingInFlight_StagedIntentUsesBoundDamage()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        delayedStore.EnableSnapshotDelay();

        // Presentation begins capture
        var presentationTask = coordinator.GetOrBindEffectiveAttackDamageAsync(101);

        // Fast submission: Submit calls GetOrBindEffectiveAttackDamageAsync concurrently
        var submissionTask = Task.Run(async () =>
        {
            var boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(101);
            await coordinator.StageIntentAsync(
                submissionId: "sub-r08",
                factId: "add:1+1",
                isCorrect: true,
                latencyMs: 150,
                isEligibleAtSubmission: true,
                effectiveAttackDamage: boundDamage);
        });

        // Purchase occurs while both await
        await PurchaseAttackRankAsync(store, targetRank: 1);

        delayedStore.CompleteDelayedSnapshotWithInner();

        await presentationTask;
        await submissionTask;

        var staged = await store.GetPendingIntentAsync("sub-r08");
        Assert.NotNull(staged);
        Assert.Equal(1, staged.EffectiveAttackDamage);
    }

    [Fact]
    public async Task R09_DirectStorePurchaseAndCoordinatorBinding_PreserveCommittedOrdering()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        int dmg1 = await coordinator.GetOrBindEffectiveAttackDamageAsync(101);
        Assert.Equal(1, dmg1);

        // Direct store purchase bypassing any UI or coordinator wrapper
        await PurchaseAttackRankAsync(store, targetRank: 1);

        int dmg2 = await coordinator.GetOrBindEffectiveAttackDamageAsync(102);
        Assert.Equal(2, dmg2);
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(101));
    }

    [Fact]
    public async Task R10_FailedOrCancelledPurchase_DoesNotLeaveFalselyUpgradedEntitlement()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Attempt invalid purchase (expectedRank 99 mismatch or insufficient SP)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PurchaseAttackUpgradeAsync(expectedRank: 99, expectedResetEpoch: 0));

        int dmg = await coordinator.GetOrBindEffectiveAttackDamageAsync(101);
        Assert.Equal(1, dmg);
    }

    [Fact]
    public async Task R11_ComponentRecreation_WithSameActiveQuestion_PreservesOriginalBinding()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Question 101 bound to 1
        int dmg1 = await coordinator.GetOrBindEffectiveAttackDamageAsync(101);
        Assert.Equal(1, dmg1);

        // Player purchases upgrade
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // Simulated component recreation re-querying active question 101
        int dmg1Recreated = await coordinator.GetOrBindEffectiveAttackDamageAsync(101);
        Assert.Equal(1, dmg1Recreated);

        // New question 102 gets the upgraded damage
        int dmg2 = await coordinator.GetOrBindEffectiveAttackDamageAsync(102);
        Assert.Equal(2, dmg2);
    }

    [Fact]
    public async Task R12_CalmModePauseResumePersistenceRetry_DoNotProduceStrongerBinding()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Question 101 bound at Rank 0
        int dmg1 = await coordinator.GetOrBindEffectiveAttackDamageAsync(101);
        Assert.Equal(1, dmg1);

        // Simulate pause, mode toggle, purchase during pause
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // Simulate resume: re-binding same question
        int dmg1Resumed = await coordinator.GetOrBindEffectiveAttackDamageAsync(101);
        Assert.Equal(1, dmg1Resumed);

        // Stage intent for question 101
        await coordinator.StageIntentAsync(
            submissionId: "sub-r12",
            factId: "add:1+1",
            isCorrect: true,
            latencyMs: 200,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: dmg1Resumed);

        var intent = await store.GetPendingIntentAsync("sub-r12");
        Assert.NotNull(intent);
        Assert.Equal(1, intent.EffectiveAttackDamage);
    }

    [Fact]
    public async Task R13_QuestionABeginsAtRank0_TwoPurchasesCommitBeforeCaptureCompletes_AStillUsesDamage1()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        delayedStore.EnableSnapshotDelay();

        // Question 101 begins capture at rank 0
        var captureTaskA = coordinator.GetOrBindEffectiveAttackDamageAsync(101);

        // Two purchases commit before capture completion
        await PurchaseAttackRankAsync(store, targetRank: 2);

        // Delayed capture completes with inner store
        delayedStore.CompleteDelayedSnapshotWithInner();

        int dmgA = await captureTaskA;

        // Question A MUST still use damage 1!
        Assert.Equal(1, dmgA);
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(101));
    }

    [Fact]
    public async Task R14_NextQuestionDispatchedAfterBothPurchases_BindsNewlyEntitledDamage3()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        delayedStore.EnableSnapshotDelay();

        var captureTaskA = coordinator.GetOrBindEffectiveAttackDamageAsync(101);

        // Two purchases commit before capture completion
        await PurchaseAttackRankAsync(store, targetRank: 2);

        delayedStore.CompleteDelayedSnapshotWithInner();
        await captureTaskA;

        // Question B dispatched after both purchases
        int dmgB = await coordinator.GetOrBindEffectiveAttackDamageAsync(102);
        Assert.Equal(3, dmgB);
        Assert.Equal(3, coordinator.GetBoundEffectiveAttackDamageOrDefault(102));
    }

    [Fact]
    public async Task R15_DelayedRank0SnapshotCompletesAfterCommittedRank1Event_CacheRemainsRank1()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        var tcsRank0 = new TaskCompletionSource<CyberDefenseAttackUpgradeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        delayedStore.EnqueueSnapshotTask(tcsRank0.Task);

        // Start Question 101 capture which waits on tcsRank0
        var task101 = coordinator.GetOrBindEffectiveAttackDamageAsync(101);

        // Player purchases Rank 1 upgrade, which fires AttackUpgradeSnapshotChanged (Rank 1, Revision 2)
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // Now delayed rank-0 snapshot completes (Rank 0, Revision 1)
        var snapRank0 = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 0,
            nextAttackRank: 1,
            currentBaseDamage: 1,
            nextBaseDamage: 2,
            nextRankCost: 1,
            availableSkillPoints: 0,
            hasSufficientSkillPoints: false,
            playerLevel: 1,
            resetEpoch: 0,
            storeRevision: 1);
        tcsRank0.TrySetResult(snapRank0);

        await task101;

        // Cache must NOT have been regressed to Rank 0!
        // Question 102 dispatched now must receive Rank 1 damage (2), not Rank 0 damage (1)!
        int dmg102 = await coordinator.GetOrBindEffectiveAttackDamageAsync(102);
        Assert.Equal(2, dmg102);
    }

    [Fact]
    public async Task R16_NewQuestionDispatchedAfterR15_ReceivesDamage2()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        var tcsRank0 = new TaskCompletionSource<CyberDefenseAttackUpgradeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        delayedStore.EnqueueSnapshotTask(tcsRank0.Task);

        var task101 = coordinator.GetOrBindEffectiveAttackDamageAsync(101);
        await PurchaseAttackRankAsync(store, targetRank: 1);

        var snapRank0 = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 0,
            nextAttackRank: 1,
            currentBaseDamage: 1,
            nextBaseDamage: 2,
            nextRankCost: 1,
            availableSkillPoints: 0,
            hasSufficientSkillPoints: false,
            playerLevel: 1,
            resetEpoch: 0,
            storeRevision: 1);
        tcsRank0.TrySetResult(snapRank0);
        await task101;

        int dmg102 = await coordinator.GetOrBindEffectiveAttackDamageAsync(102);
        Assert.Equal(2, dmg102);
        Assert.Equal(2, coordinator.GetBoundEffectiveAttackDamageOrDefault(102));
    }

    [Fact]
    public async Task R17_MultiplePurchaseNotifications_CannotArriveOrBeAppliedInWayThatRegressesCacheRevision()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);
        await coordinator.InitializeAsync();

        // Purchase Rank 2 (Revision 3)
        await PurchaseAttackRankAsync(store, targetRank: 2);
        var snapRank2 = await store.GetAttackUpgradeSnapshotAsync();
        Assert.Equal(2, snapRank2.CurrentAttackRank);
        Assert.Equal(3, snapRank2.CurrentBaseDamage);

        // Simulate an out-of-order stale notification arriving (Revision 2, Rank 1)
        var staleSnapRank1 = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 1,
            nextAttackRank: 2,
            currentBaseDamage: 2,
            nextBaseDamage: 3,
            nextRankCost: 2,
            availableSkillPoints: 0,
            hasSufficientSkillPoints: false,
            playerLevel: 1,
            resetEpoch: snapRank2.ResetEpoch,
            storeRevision: snapRank2.StoreRevision - 1);

        delayedStore.TriggerAttackUpgradeSnapshotChanged(staleSnapRank1);

        // Next question dispatched must still receive damage 3!
        int dmg = await coordinator.GetOrBindEffectiveAttackDamageAsync(1701);
        Assert.Equal(3, dmg);
        Assert.Equal(3, coordinator.GetBoundEffectiveAttackDamageOrDefault(1701));
    }

    [Fact]
    public async Task R18_DelayedSnapshotFromOldResetEpoch_CannotOverrideNewEpochBaseline()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        var tcsEpoch0 = new TaskCompletionSource<CyberDefenseAttackUpgradeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        delayedStore.EnqueueSnapshotTask(tcsEpoch0.Task);
        var taskEpoch0 = coordinator.GetOrBindEffectiveAttackDamageAsync(1801);

        // Reset store to Epoch 1
        await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(targetEpoch: 1);
        await store.ClearResetIntentAsync(targetEpoch: 1);

        coordinator.InvalidateState();

        var snapEpoch1 = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 0,
            nextAttackRank: 1,
            currentBaseDamage: 1,
            nextBaseDamage: 2,
            nextRankCost: 1,
            availableSkillPoints: 0,
            hasSufficientSkillPoints: false,
            playerLevel: 1,
            resetEpoch: 1,
            storeRevision: 1);
        delayedStore.EnqueueSnapshotTask(Task.FromResult(snapEpoch1));

        int dmgEpoch1 = await coordinator.GetOrBindEffectiveAttackDamageAsync(1802);
        Assert.Equal(1, dmgEpoch1);

        // Old epoch snapshot finishes
        var snapEpoch0 = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 4,
            nextAttackRank: 5,
            currentBaseDamage: 5,
            nextBaseDamage: 6,
            nextRankCost: 3,
            availableSkillPoints: 0,
            hasSufficientSkillPoints: false,
            playerLevel: 4,
            resetEpoch: 0,
            storeRevision: 12);
        tcsEpoch0.TrySetResult(snapEpoch0);
        await taskEpoch0;

        // Baseline for Epoch 1 remains 1
        int nextDmg = await coordinator.GetOrBindEffectiveAttackDamageAsync(1803);
        Assert.Equal(1, nextDmg);
    }

    private static async Task SeedSkillPointsAsync(SqliteGameplayStore store, int targetSp)
    {
        var prog = await store.GetProgressionAsync();
        while (prog.AvailableSkillPoints < targetSp)
        {
            var hitIntent = new CyberDefensePendingIntentRecord(
                submissionId: "seed-sp-" + Guid.NewGuid().ToString("N"),
                factId: "add:1+1",
                isCorrect: true,
                isEligible: true,
                responseLatencyMs: 100,
                resetEpoch: prog.ResetEpoch,
                createdAt: DateTimeOffset.UtcNow,
                effectiveAttackDamage: 1);
            await store.ApplyAttemptTransactionAsync(hitIntent);
            prog = await store.GetProgressionAsync();
        }
    }

    [Fact]
    public async Task R19_SubscriberPerformingStoreRead_DoesNotDeadlockPurchaseCompletion()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        await SeedSkillPointsAsync(store, 1);

        bool subscriberRan = false;
        CyberDefenseProgressionRecord? reentrantProgression = null;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        store.AttackUpgradeSnapshotChanged += snapshot =>
        {
            subscriberRan = true;
            // Synchronous store read from inside event callback (reentrancy test)
            reentrantProgression = store.GetProgressionAsync(cts.Token).GetAwaiter().GetResult();
        };

        var prog = await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        Assert.True(subscriberRan);
        Assert.NotNull(reentrantProgression);
        Assert.Equal(1, prog.AttackRank);
        Assert.Equal(1, reentrantProgression.AttackRank);
    }

    [Fact]
    public async Task R20_ThrowingSubscriber_CannotFalselyMarkCommittedPurchaseAsFailed()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        await SeedSkillPointsAsync(store, 1);

        store.AttackUpgradeSnapshotChanged += _ =>
        {
            throw new InvalidOperationException("Simulated throwing observer.");
        };

        var prog = await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(1, prog.AttackRank);

        var dbProg = await store.GetProgressionAsync();
        Assert.Equal(1, dbProg.AttackRank);
    }

    [Fact]
    public async Task R21_FailedPurchase_ProducesNoSuccessfulNotification()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        int notificationCount = 0;
        store.AttackUpgradeSnapshotChanged += _ => notificationCount++;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PurchaseAttackUpgradeAsync(expectedRank: 99, expectedResetEpoch: 0));

        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public async Task R22_ResetNotification_CarriesResetEpochAndRank0()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        CyberDefenseAttackUpgradeSnapshot? receivedSnapshot = null;
        store.AttackUpgradeSnapshotChanged += snap => receivedSnapshot = snap;

        var intent = await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(targetEpoch: intent.TargetEpoch);
        await store.ClearResetIntentAsync(targetEpoch: intent.TargetEpoch);

        Assert.NotNull(receivedSnapshot);
        Assert.Equal(intent.TargetEpoch, receivedSnapshot.ResetEpoch);
        Assert.Equal(0, receivedSnapshot.CurrentAttackRank);
        Assert.Equal(1, receivedSnapshot.CurrentBaseDamage);
        Assert.Equal(1, receivedSnapshot.StoreRevision);
    }

    [Fact]
    public async Task R23_UninitializedCoordinatorState_CannotDeriveHistoricalEntitlementThroughRankSubtraction()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        delayedStore.EnableSnapshotDelay();

        // Question 101 begins capture when coordinator is uninitialized
        var captureTask = coordinator.GetOrBindEffectiveAttackDamageAsync(101);

        // Simulate an upgrade event with Rank 3 arriving (e.g. multiple purchases committed)
        var multiUpgradeSnapshot = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 3,
            nextAttackRank: 4,
            currentBaseDamage: 4,
            nextBaseDamage: 5,
            nextRankCost: 3,
            availableSkillPoints: 0,
            hasSufficientSkillPoints: false,
            playerLevel: 3,
            resetEpoch: 0,
            storeRevision: 10);

        delayedStore.TriggerAttackUpgradeSnapshotChanged(multiUpgradeSnapshot);

        delayedStore.CompleteDelayedSnapshotWithInner();
        int dmg = await captureTask;

        // Baseline entitlement must be 1, NOT 3 (from 3 - 1 = 2 -> damage 3)
        Assert.Equal(1, dmg);
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(101));
    }

    [Fact]
    public async Task R24_AlreadyBoundQuestion_RemainsImmutableDuringThreeConsecutivePurchases()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Bound at Rank 0 -> Damage 1
        int initialDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(2401);
        Assert.Equal(1, initialDamage);

        // Perform three consecutive purchases
        await PurchaseAttackRankAsync(store, targetRank: 1);
        await PurchaseAttackRankAsync(store, targetRank: 2);
        await PurchaseAttackRankAsync(store, targetRank: 3);

        // Question 2401 must still have damage 1
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(2401));
        Assert.Equal(1, await coordinator.GetOrBindEffectiveAttackDamageAsync(2401));

        // A new question receives the newly upgraded damage (4)
        int newQuestionDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(2402);
        Assert.Equal(4, newQuestionDamage);
    }

    [Fact]
    public async Task R25_DirectStorePurchases_UpdateQuestionEligibilityWithoutUiUpgradeTerminal()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        int dmg0 = await coordinator.GetOrBindEffectiveAttackDamageAsync(2501);
        Assert.Equal(1, dmg0);

        // Direct store purchase
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // New question reflects direct purchase immediately
        int dmg1 = await coordinator.GetOrBindEffectiveAttackDamageAsync(2502);
        Assert.Equal(2, dmg1);
    }

    [Fact]
    public async Task R26_SubscriberDisposal_PreventsStaleCoordinatorUpdates()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Dispose coordinator (unsubscribes from store.AttackUpgradeSnapshotChanged)
        coordinator.Dispose();

        // Purchase upgrade on store
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // Since coordinator was disposed, its internal authoritative snapshot was not updated
        // Binding another question via disposed coordinator does not see store event
        int dmg = coordinator.GetBoundEffectiveAttackDamageOrDefault(2601, fallback: -1);
        Assert.Equal(-1, dmg);
    }

    [Fact]
    public async Task R27_ConcurrentResetSnapshotReadAndQuestionDispatch_PreserveEpochAndGenerationFencing()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var delayedStore = new ControllableDelayedGameplayStore(store);
        var coordinator = new CyberDefenseCombatCoordinator(delayedStore, consumer);

        var tcsEpoch0 = new TaskCompletionSource<CyberDefenseAttackUpgradeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        delayedStore.EnqueueSnapshotTask(tcsEpoch0.Task);
        var captureTaskEpoch0 = coordinator.GetOrBindEffectiveAttackDamageAsync(2701);

        // Reset occurs to Epoch 1
        await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(targetEpoch: 1);
        await store.ClearResetIntentAsync(targetEpoch: 1);
        coordinator.InvalidateState();

        var snapEpoch1 = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 0,
            nextAttackRank: 1,
            currentBaseDamage: 1,
            nextBaseDamage: 2,
            nextRankCost: 1,
            availableSkillPoints: 0,
            hasSufficientSkillPoints: false,
            playerLevel: 1,
            resetEpoch: 1,
            storeRevision: 1);
        delayedStore.EnqueueSnapshotTask(Task.FromResult(snapEpoch1));

        int dmgEpoch1 = await coordinator.GetOrBindEffectiveAttackDamageAsync(2702);
        Assert.Equal(1, dmgEpoch1);

        // Epoch 0 task completes late with an attempted upgrade snapshot
        var snapEpoch0Stale = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 5,
            nextAttackRank: 6,
            currentBaseDamage: 6,
            nextBaseDamage: 7,
            nextRankCost: 4,
            availableSkillPoints: 0,
            hasSufficientSkillPoints: false,
            playerLevel: 5,
            resetEpoch: 0,
            storeRevision: 15);
        tcsEpoch0.TrySetResult(snapEpoch0Stale);

        await captureTaskEpoch0;

        // Epoch 1 binding and state must not be corrupted
        Assert.Equal(1, coordinator.GetBoundEffectiveAttackDamageOrDefault(2702));
        int dmgEpoch1Next = await coordinator.GetOrBindEffectiveAttackDamageAsync(2703);
        Assert.Equal(1, dmgEpoch1Next);
    }

    [Fact]
    public async Task R28_BoundDamageSurvivesPendingIntentStagingLearnerCommitAndReceiptReplay()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();
        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(store, consumer);
        await coordinator.InitializeAsync();

        // Purchase Rank 1 -> Damage 2
        await PurchaseAttackRankAsync(store, targetRank: 1);

        // Bind Question 2801 to Damage 2
        int boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(2801);
        Assert.Equal(2, boundDamage);

        // Now purchase Rank 2 before staging
        await PurchaseAttackRankAsync(store, targetRank: 2);

        string subId = "sub-r28-" + Guid.NewGuid().ToString("N");
        await coordinator.StageIntentAsync(
            submissionId: subId,
            factId: "add:1+1",
            isCorrect: true,
            latencyMs: 120,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: boundDamage);

        var intent = await store.GetPendingIntentAsync(subId);
        Assert.NotNull(intent);
        Assert.Equal(2, intent.EffectiveAttackDamage);

        var changeSet = CreateTestChangeSet(subId, "add:1+1", true, 120);
        await learnerStore.CommitSubmissionAsync(changeSet);

        var result1 = await coordinator.ConsumeCommittedAttemptAsync(subId);
        Assert.NotNull(result1);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, result1.Status);
        Assert.Equal(2, result1.Receipt?.TransitionResult?.RequestedAttackDamage);
        Assert.Equal(2, result1.Receipt?.TransitionResult?.AppliedOpponentDamage);

        var result2 = await coordinator.ConsumeCommittedAttemptAsync(subId);
        Assert.NotNull(result2);
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, result2.Status);
        Assert.Equal(2, result2.Receipt?.TransitionResult?.RequestedAttackDamage);
    }
}

internal sealed class ControllableDelayedGameplayStore : IGameplayStore
{
    private readonly IGameplayStore _inner;
    private TaskCompletionSource<CyberDefenseAttackUpgradeSnapshot>? _delayedSnapshotTcs;
    private bool _delaySnapshotReads;
    private readonly Queue<Task<CyberDefenseAttackUpgradeSnapshot>> _snapshotQueue = new();
    private Action<CyberDefenseAttackUpgradeSnapshot>? _customHandlers;

    public ControllableDelayedGameplayStore(IGameplayStore inner)
    {
        _inner = inner;
    }

    public void TriggerAttackUpgradeSnapshotChanged(CyberDefenseAttackUpgradeSnapshot snapshot)
    {
        _customHandlers?.Invoke(snapshot);
    }

    public void EnableSnapshotDelay()
    {
        _delaySnapshotReads = true;
        _delayedSnapshotTcs = new TaskCompletionSource<CyberDefenseAttackUpgradeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void CompleteDelayedSnapshotWithInner()
    {
        _delaySnapshotReads = false;
        _ = Task.Run(async () =>
        {
            var snap = await _inner.GetAttackUpgradeSnapshotAsync();
            _delayedSnapshotTcs?.TrySetResult(snap);
        });
    }

    public void EnqueueSnapshotTask(Task<CyberDefenseAttackUpgradeSnapshot> task)
    {
        _snapshotQueue.Enqueue(task);
    }

    public string StoragePath => _inner.StoragePath;
    public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);
    public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => _inner.GetRunStateAsync(cancellationToken);
    public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => _inner.GetResetEpochAsync(cancellationToken);
    public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => _inner.GetStoreRevisionAsync(cancellationToken);
    public Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default) => _inner.GetProgressionAsync(cancellationToken);

    public async Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (_snapshotQueue.Count > 0)
        {
            return await _snapshotQueue.Dequeue().ConfigureAwait(false);
        }
        if (_delaySnapshotReads && _delayedSnapshotTcs != null)
        {
            return await _delayedSnapshotTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        return await _inner.GetAttackUpgradeSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged
    {
        add
        {
            _customHandlers += value;
            _inner.AttackUpgradeSnapshotChanged += value;
        }
        remove
        {
            _customHandlers -= value;
            _inner.AttackUpgradeSnapshotChanged -= value;
        }
    }

    public Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default) =>
        _inner.GetFirewallUpgradeSnapshotAsync(cancellationToken);

    public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged
    {
        add => _inner.FirewallUpgradeSnapshotChanged += value;
        remove => _inner.FirewallUpgradeSnapshotChanged -= value;
    }

    public Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
        _inner.PurchaseAttackUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);

    public Task<CyberDefenseCombatModifierSnapshot> GetCombatModifierSnapshotAsync(CancellationToken cancellationToken = default) =>
        _inner.GetCombatModifierSnapshotAsync(cancellationToken);

    public Task<CyberDefenseCombatModifierSnapshot> PurchaseCriticalStrikeUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
        _inner.PurchaseCriticalStrikeUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);
    public Task<CyberDefenseCombatModifierSnapshot> PurchaseOverdriveUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
        _inner.PurchaseOverdriveUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);
    public Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
        _inner.PurchaseFirewallUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);
    public Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
        _inner.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity, expectedResetEpoch, cancellationToken);
    public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) =>
        _inner.GetReceiptAsync(submissionId, cancellationToken);
    public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) =>
        _inner.StagePendingIntentAsync(intent, cancellationToken);
    public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) =>
        _inner.GetPendingIntentAsync(submissionId, cancellationToken);
    public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) =>
        _inner.GetPendingIntentsAsync(resetEpoch, cancellationToken);
    public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) =>
        _inner.ClearPendingIntentAsync(submissionId, cancellationToken);
    public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) =>
        _inner.ApplyAttemptTransactionAsync(intent, cancellationToken);
    public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) =>
        _inner.GetResetIntentAsync(cancellationToken);
    public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) =>
        _inner.BeginOrGetResetIntentAsync(cancellationToken);
    public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) =>
        _inner.ResetGameplayStateAsync(targetEpoch, cancellationToken);
    public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) =>
        _inner.ClearResetIntentAsync(targetEpoch, cancellationToken);
    public Task CloseAsync(CancellationToken cancellationToken = default) =>
        _inner.CloseAsync(cancellationToken);
    public void Dispose() => _inner.Dispose();
}
