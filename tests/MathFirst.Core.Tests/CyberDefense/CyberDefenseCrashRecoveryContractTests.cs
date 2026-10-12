namespace MathFirst.Core.Tests.CyberDefense;

using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Xunit;

public sealed class CyberDefenseCrashRecoveryContractTests : IDisposable
{
    private readonly string _testDirectory;

    public CyberDefenseCrashRecoveryContractTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_Crash_" + Guid.NewGuid().ToString("N"));
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

    private string GetTempDbPath(string name = "crash_test")
    {
        return Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");
    }

    private static SubmissionChangeSet CreateTestChangeSet(
        string submissionId,
        string factId = "add:1+1",
        bool isCorrect = true,
        long responseLatencyMs = 200,
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

        return new SubmissionChangeSet(
            submissionId: submissionId,
            ExpectedRevision: expectedRevision,
            attempt: attempt,
            updatedItemState: itemState,
            updatedProgression: progression,
            updatedFsrsState: null,
            operationProgressions: progression.OperationProgressions);
    }

    private static CyberDefensePendingIntentRecord CreateIntent(
        string submissionId,
        string factId = "add:1+1",
        bool isCorrect = true,
        bool isEligible = true,
        int effectiveDamage = 1,
        long resetEpoch = 0,
        long latencyMs = 200)
    {
        return new CyberDefensePendingIntentRecord(
            submissionId: submissionId,
            factId: factId,
            isCorrect: isCorrect,
            isEligible: isEligible,
            responseLatencyMs: latencyMs,
            resetEpoch: resetEpoch,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: effectiveDamage);
    }

    private static CommittedLearnerAttemptEvidence CreateEvidence(
        string submissionId,
        string factId = "add:1+1",
        bool isCorrect = true,
        long latencyMs = 200,
        long? practicePosition = 1)
    {
        return new CommittedLearnerAttemptEvidence(
            submissionId: submissionId,
            factId: factId,
            isCorrect: isCorrect,
            responseLatencyMs: latencyMs,
            practicePosition: practicePosition,
            timestamp: DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task T19_CrashRecovery_UsesStoredDamageValue_NotCurrentAttackRank()
    {
        var gameplayDb = GetTempDbPath("t19_gp");
        var learnerDb = GetTempDbPath("t19_ln");

        using (var gpStore = new SqliteGameplayStore(gameplayDb))
        using (var lnStore = new SqliteLearnerStore(learnerDb))
        {
            await gpStore.InitializeAsync();
            await lnStore.InitializeAsync();

            // Give player SP and upgrade to rank 1 (damage 2)
            await gpStore.ExecuteUnderGateForTestAsync(async conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 10, available_sp = 10 WHERE id = 1;";
                await cmd.ExecuteNonQueryAsync();
            });
            await gpStore.PurchaseAttackUpgradeAsync(0, 0);

            // Stage an intent with bound damage 2
            var intent = CreateIntent("sub-crash-19", effectiveDamage: 2);
            await gpStore.StagePendingIntentAsync(intent);

            // Simulate learner store committing the attempt
            var r = await lnStore.CommitSubmissionAsync(
                CreateTestChangeSet("sub-crash-19", "add:1+1", true, 200, 1, 1));
            Assert.True(r.IsSuccess, r.Message);

            // Now, player levels up and upgrades attack rank to 3 (damage 4) BEFORE recovery runs!
            await gpStore.PurchaseAttackUpgradeAsync(1, 0);
            await gpStore.PurchaseAttackUpgradeAsync(2, 0);
        }

        // Crash restart: open fresh instances
        using (var gpStore = new SqliteGameplayStore(gameplayDb))
        using (var lnStore = new SqliteLearnerStore(learnerDb))
        {
            await gpStore.InitializeAsync();
            await lnStore.InitializeAsync();

            var consumer = new CyberDefenseSubmissionConsumer(gpStore, lnStore);
            var recoveryResult = await consumer.RecoverPendingIntentsAsync();

            Assert.Single(recoveryResult.Receipts);
            var receipt = recoveryResult.Receipts[0];

            // Crucial: recovery must use the stored intent damage (2), NOT the latest attack rank damage (4)!
            Assert.Equal(2, receipt.TransitionResult?.RequestedAttackDamage);
        }
    }

    [Fact]
    public async Task T21_FullLocalReset_RejectsOldEpochDamage()
    {
        var gameplayDb = GetTempDbPath("t21_gp");
        using var gpStore = new SqliteGameplayStore(gameplayDb);
        await gpStore.InitializeAsync();

        // Stage intent in epoch 0
        var intentEpoch0 = CreateIntent("sub-21", resetEpoch: 0, effectiveDamage: 1);
        await gpStore.StagePendingIntentAsync(intentEpoch0);

        // Perform full local reset to epoch 1
        var resetIntent = await gpStore.BeginOrGetResetIntentAsync();
        Assert.Equal(1, resetIntent.TargetEpoch);
        await gpStore.ResetGameplayStateAsync(resetIntent.TargetEpoch);
        await gpStore.ClearResetIntentAsync(resetIntent.TargetEpoch);

        // Verify epoch is 1
        var newEpoch = await gpStore.GetResetEpochAsync();
        Assert.Equal(1, newEpoch);

        // Applying old intent with epoch 0 must be rejected
        await Assert.ThrowsAsync<InvalidOperationException>(() => gpStore.ApplyAttemptTransactionAsync(intentEpoch0));

        // Staging new intent with old epoch 0 must also be rejected
        await Assert.ThrowsAsync<InvalidOperationException>(() => gpStore.StagePendingIntentAsync(intentEpoch0));
    }

    [Fact]
    public async Task T22_PendingFullLocalReset_BlocksNewIntents()
    {
        var gameplayDb = GetTempDbPath("t22_gp");
        using var gpStore = new SqliteGameplayStore(gameplayDb);
        await gpStore.InitializeAsync();

        // Begin reset intent (reset is now pending)
        var resetIntent = await gpStore.BeginOrGetResetIntentAsync();
        Assert.True(resetIntent.IsPending);

        // Staging a new intent must fail while reset is pending
        var intent = CreateIntent("sub-22", effectiveDamage: 1, resetEpoch: resetIntent.CurrentEpoch);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gpStore.StagePendingIntentAsync(intent));

        // Applying attempt transaction must also fail while reset is pending
        await Assert.ThrowsAsync<InvalidOperationException>(() => gpStore.ApplyAttemptTransactionAsync(intent));
    }

    [Fact]
    public async Task T23_HistoricalReceiptReplay_RemainsIdentical()
    {
        var gameplayDb = GetTempDbPath("t23_gp");
        using var gpStore = new SqliteGameplayStore(gameplayDb);
        await gpStore.InitializeAsync();

        var intent = CreateIntent("sub-23", effectiveDamage: 1);
        await gpStore.StagePendingIntentAsync(intent);

        var firstReceipt = await gpStore.ApplyAttemptTransactionAsync(intent);

        // Second call (replay) returns identical receipt
        var secondReceipt = await gpStore.ApplyAttemptTransactionAsync(intent);

        Assert.Equal(firstReceipt.SubmissionId, secondReceipt.SubmissionId);
        Assert.Equal(firstReceipt.AwardedXp, secondReceipt.AwardedXp);
        Assert.Equal(firstReceipt.TransitionResult?.RequestedAttackDamage, secondReceipt.TransitionResult?.RequestedAttackDamage);
        Assert.Equal(firstReceipt.ProcessedAt, secondReceipt.ProcessedAt);
    }

    [Fact]
    public async Task T25_LearnerStoreCorrectnessEvidence_IsRequiredBeforeGameplaySettlement()
    {
        var gameplayDb = GetTempDbPath("t25_gp");
        var learnerDb = GetTempDbPath("t25_ln");

        using var gpStore = new SqliteGameplayStore(gameplayDb);
        using var lnStore = new SqliteLearnerStore(learnerDb);
        await gpStore.InitializeAsync();
        await lnStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gpStore, lnStore);

        // Stage intent
        var intent = CreateIntent("sub-25", effectiveDamage: 1);
        await consumer.StageIntentAsync(intent);

        // Attempting to consume without committing learner evidence must yield UncommittedLearnerAttempt
        var consumptionResult = await consumer.ConsumeAttemptAsync("sub-25");
        Assert.Equal(CyberDefenseConsumptionStatus.UncommittedLearnerAttempt, consumptionResult.Status);

        // Now commit learner evidence
        await lnStore.CommitSubmissionAsync(
            CreateTestChangeSet("sub-25", "add:1+1", true, 200, 1, 1));

        // Now consume succeeds
        var successResult = await consumer.ConsumeAttemptAsync("sub-25");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, successResult.Status);
        Assert.NotNull(successResult.Receipt);
    }
}
