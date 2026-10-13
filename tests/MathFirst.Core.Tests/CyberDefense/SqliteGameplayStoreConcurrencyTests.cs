namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SqliteGameplayStoreConcurrencyTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MathFirst_Tests_Concurrency_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private SqliteGameplayStore CreateStore(string? dir = null)
    {
        dir ??= CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");
        return new SqliteGameplayStore(dbPath);
    }

    private static async Task SetProgressionSpAsync(string dbPath, int earnedSp, int spentSp, int availableSp, int attackRank)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE gameplay_progression
            SET earned_sp = @earned,
                spent_sp = @spent,
                available_sp = @available,
                attack_rank = @rank
            WHERE id = 1;
        ";
        cmd.Parameters.AddWithValue("@earned", earnedSp);
        cmd.Parameters.AddWithValue("@spent", spentSp);
        cmd.Parameters.AddWithValue("@available", availableSp);
        cmd.Parameters.AddWithValue("@rank", attackRank);
        await cmd.ExecuteNonQueryAsync();
    }

    private static CyberDefensePendingIntentRecord CreateIntent(string subId, bool isCorrect = true, int latencyMs = 500, long resetEpoch = 0)
    {
        return new CyberDefensePendingIntentRecord(
            subId,
            "add:1+1",
            isCorrect,
            isEligible: true,
            responseLatencyMs: latencyMs,
            resetEpoch: resetEpoch,
            createdAt: DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Ignored in test cleanup
            }
        }
    }

    // =========================================================================
    // T01: Concurrent purchases with the same expected rank cannot double spend
    // =========================================================================
    [Fact]
    public async Task T01_ConcurrentPurchases_WithSameExpectedRank_CannotDoubleSpend()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using (var setup = new SqliteGameplayStore(dbPath))
        {
            await setup.InitializeAsync();
        }

        // Grant 1 earned SP, 0 spent, 1 available, rank 0
        await SetProgressionSpAsync(dbPath, earnedSp: 1, spentSp: 0, availableSp: 1, attackRank: 0);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var barrier = new Barrier(2);

        var task1 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        });

        var task2 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        });

        int successCount = 0;
        int conflictCount = 0;

        try
        {
            await task1;
            successCount++;
        }
        catch (InvalidOperationException)
        {
            conflictCount++;
        }

        try
        {
            await task2;
            successCount++;
        }
        catch (InvalidOperationException)
        {
            conflictCount++;
        }

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        var finalProg = await store.GetProgressionAsync();
        Assert.Equal(1, finalProg.AttackRank);
        Assert.Equal(1, finalProg.SpentSkillPoints);
        Assert.Equal(0, finalProg.AvailableSkillPoints);
        Assert.Equal(1, finalProg.EarnedSkillPoints);
        Assert.Equal(finalProg.EarnedSkillPoints, checked(finalProg.SpentSkillPoints + finalProg.AvailableSkillPoints));
    }

    // =========================================================================
    // T02: Concurrent purchase and Applied XP settlement on same store instance
    // =========================================================================
    [Fact]
    public async Task T02_ConcurrentPurchase_AndAppliedXpSettlement_DoNotCollideOnSharedConnection()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using (var setup = new SqliteGameplayStore(dbPath))
        {
            await setup.InitializeAsync();
        }

        // Grant 5 SP
        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-t02-1");

        using var barrier = new Barrier(2);

        var purchaseTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        });

        var attemptTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await store.ApplyAttemptTransactionAsync(intent);
        });

        await Task.WhenAll(purchaseTask, attemptTask);

        var finalProg = await store.GetProgressionAsync();
        Assert.Equal(1, finalProg.AttackRank);
        Assert.Equal(1, finalProg.SpentSkillPoints);
        Assert.Equal(finalProg.EarnedSkillPoints, checked(finalProg.SpentSkillPoints + finalProg.AvailableSkillPoints));

        var receipt = await store.GetReceiptAsync("sub-t02-1");
        Assert.NotNull(receipt);
        Assert.Equal(CyberDefenseReceiptKind.Applied, receipt.ReceiptKind);
    }

    // =========================================================================
    // T03: Concurrent purchase and Full Local Reset cannot produce mixed-epoch progression
    // =========================================================================
    [Fact]
    public async Task T03_ConcurrentPurchase_AndFullLocalReset_CannotProduceMixedEpochOrRestoreResetData()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using (var setup = new SqliteGameplayStore(dbPath))
        {
            await setup.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var barrier = new Barrier(2);

        var purchaseTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            try
            {
                await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
            }
            catch (InvalidOperationException)
            {
                // Expected if reset occurred before or fenced purchase
            }
        });

        var resetTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            var intent = await store.BeginOrGetResetIntentAsync();
            await store.ResetGameplayStateAsync(intent.TargetEpoch);
            await store.ClearResetIntentAsync(intent.TargetEpoch);
        });

        await Task.WhenAll(purchaseTask, resetTask);

        var finalProg = await store.GetProgressionAsync();
        var resetIntent = await store.GetResetIntentAsync();

        Assert.False(resetIntent.IsPending);
        Assert.Equal(1L, resetIntent.CurrentEpoch);
        Assert.Equal(1L, finalProg.ResetEpoch);
        Assert.Equal(0, finalProg.AttackRank);
        Assert.Equal(0, finalProg.SpentSkillPoints);
        Assert.Equal(0, finalProg.EarnedSkillPoints);
        Assert.Equal(0, finalProg.AvailableSkillPoints);
    }

    // =========================================================================
    // T04: Concurrent snapshot read and purchase return internally consistent snapshots
    // =========================================================================
    [Fact]
    public async Task T04_ConcurrentSnapshotRead_AndPurchase_ReturnConsistentSnapshotsWithoutCollision()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using (var setup = new SqliteGameplayStore(dbPath))
        {
            await setup.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var snapshots = new List<CyberDefenseAttackUpgradeSnapshot>();

        var readTask = Task.Run(async () =>
        {
            for (int i = 0; i < 30; i++)
            {
                var snap = await store.GetAttackUpgradeSnapshotAsync(cts.Token);
                lock (snapshots)
                {
                    snapshots.Add(snap);
                }
                await Task.Yield();
            }
        });

        var purchaseTask = Task.Run(async () =>
        {
            await Task.Yield();
            return await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0, cts.Token);
        });

        await Task.WhenAll(readTask, purchaseTask);

        Assert.NotEmpty(snapshots);
        foreach (var snap in snapshots)
        {
            Assert.True(snap.CurrentAttackRank == 0 || snap.CurrentAttackRank == 1);
            Assert.Equal(snap.CurrentAttackRank + 1, snap.CurrentBaseDamage);
            Assert.Equal(snap.CurrentAttackRank + 1, snap.NextAttackRank);
        }
    }

    // =========================================================================
    // T05: Concurrent GetProgressionAsync and Applied XP settlement do not access connection unsafely
    // =========================================================================
    [Fact]
    public async Task T05_ConcurrentGetProgression_AndAppliedXpSettlement_DoNotAccessConnectionUnsafely()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-t05-1");

        using var barrier = new Barrier(2);

        var readTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 15; i++)
            {
                var prog = await store.GetProgressionAsync();
                Assert.NotNull(prog);
            }
        });

        var attemptTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await store.ApplyAttemptTransactionAsync(intent);
        });

        await Task.WhenAll(readTask, attemptTask);

        var receipt = await store.GetReceiptAsync("sub-t05-1");
        Assert.NotNull(receipt);
    }

    // =========================================================================
    // T06: Concurrent pending intent staging and purchase do not cause nested connection transaction errors
    // =========================================================================
    [Fact]
    public async Task T06_ConcurrentPendingIntentStaging_AndPurchase_DoNotCauseNestedTransactionErrors()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using (var setup = new SqliteGameplayStore(dbPath))
        {
            await setup.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-t06-1");

        using var barrier = new Barrier(2);

        var stageTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await store.StagePendingIntentAsync(intent);
        });

        var purchaseTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        });

        await Task.WhenAll(stageTask, purchaseTask);

        var staged = await store.GetPendingIntentAsync("sub-t06-1");
        Assert.NotNull(staged);
        var prog = await store.GetProgressionAsync();
        Assert.Equal(1, prog.AttackRank);
    }

    // =========================================================================
    // T07: Concurrent first-use initialization results in one consistent initialized store state
    // =========================================================================
    [Fact]
    public async Task T07_ConcurrentFirstUseInitialization_ResultsInConsistentInitializedStoreState()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using var store = new SqliteGameplayStore(dbPath);

        const int workerCount = 10;
        using var barrier = new Barrier(workerCount);

        var tasks = new Task[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                await store.InitializeAsync();
            });
        }

        await Task.WhenAll(tasks);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(1, runState.Sector);
        var prog = await store.GetProgressionAsync();
        Assert.Equal(1L, prog.StoreRevision);
        Assert.Equal(0, prog.AttackRank);
    }

    // =========================================================================
    // T08: Cancellation while waiting for gate does not release another operation's ownership
    // =========================================================================
    [Fact]
    public async Task T08_CancellationWhileWaitingForGate_DoesNotReleaseAnotherOperationsOwnership()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        // Waiting on pre-cancelled token must throw OperationCanceledException immediately
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.GetProgressionAsync(cts.Token));

        // Subsequent operation on the store must succeed without gate corruption
        var prog = await store.GetProgressionAsync();
        Assert.NotNull(prog);
    }

    // =========================================================================
    // T09: An exception within a gated transaction releases the gate and permits subsequent operations
    // =========================================================================
    [Fact]
    public async Task T09_ExceptionWithinGatedTransaction_ReleasesGateAndPermitsSubsequentOperations()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Stale expected rank 99 throws InvalidOperationException within transaction
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PurchaseAttackUpgradeAsync(expectedRank: 99, expectedResetEpoch: 0));

        // Gate must have been released in finally block; next call must succeed immediately
        var prog = await store.GetProgressionAsync();
        Assert.Equal(0, prog.AttackRank);
    }

    // =========================================================================
    // T10: Existing public method composition does not deadlock through nested semaphore acquisition
    // =========================================================================
    [Fact]
    public async Task T10_ExistingPublicMethodComposition_DoesNotDeadlock()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using var store = new SqliteGameplayStore(dbPath);
        // Direct call without prior InitializeAsync: EnsureInitializedAsync + GetRunStateAsync
        var runState = await store.GetRunStateAsync();
        Assert.Equal(1, runState.Sector);

        // Snapshot calls GetProgressionAsync
        var snapshot = await store.GetAttackUpgradeSnapshotAsync();
        Assert.Equal(0, snapshot.CurrentAttackRank);
        Assert.Equal(1, snapshot.CurrentBaseDamage);
    }

    // =========================================================================
    // T11: CloseAsync coordinated with an active operation does not dispose connection during operation
    // =========================================================================
    [Fact]
    public async Task T11_CloseAsync_CoordinatedWithActiveOperation_DoesNotDisposeConnectionDuringOperation()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var opTask = Task.Run(async () =>
        {
            var prog = await store.GetProgressionAsync();
            return prog;
        });

        var closeTask = Task.Run(async () =>
        {
            await store.CloseAsync();
        });

        await Task.WhenAll(opTask, closeTask);

        var resultProg = await opTask;
        Assert.NotNull(resultProg);
    }

    // =========================================================================
    // T12: Dispose during or after active operations preserves documented lifecycle safety contract
    // =========================================================================
    [Fact]
    public async Task T12_Dispose_PreservesLifecycleSafetyContract()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var prog = await store.GetProgressionAsync();
        Assert.NotNull(prog);

        store.Dispose();
        // Multiple Dispose calls must be idempotent
        store.Dispose();
    }

    // =========================================================================
    // T13: Disposed store rejects new operations appropriately
    // =========================================================================
    [Fact]
    public async Task T13_DisposedStore_RejectsNewOperationsAppropriately()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        store.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetProgressionAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetRunStateAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.PurchaseAttackUpgradeAsync(0, 0));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.CloseAsync());
    }

    // =========================================================================
    // T14: Multiple store instances preserve SQLite atomicity; one conflicting purchase cannot double spend
    // =========================================================================
    [Fact]
    public async Task T14_MultipleStoreInstances_PreserveSqliteAtomicity_CannotDoubleSpend()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using (var setup = new SqliteGameplayStore(dbPath))
        {
            await setup.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 1, spentSp: 0, availableSp: 1, attackRank: 0);

        using var storeA = new SqliteGameplayStore(dbPath);
        using var storeB = new SqliteGameplayStore(dbPath);
        await storeA.InitializeAsync();
        await storeB.InitializeAsync();

        using var barrier = new Barrier(2);

        var taskA = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await storeA.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        });

        var taskB = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await storeB.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        });

        int successCount = 0;
        int conflictCount = 0;

        try
        {
            await taskA;
            successCount++;
        }
        catch (Exception)
        {
            conflictCount++;
        }

        try
        {
            await taskB;
            successCount++;
        }
        catch (Exception)
        {
            conflictCount++;
        }

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        var finalProg = await storeA.GetProgressionAsync();
        Assert.Equal(1, finalProg.AttackRank);
        Assert.Equal(1, finalProg.SpentSkillPoints);
        Assert.Equal(0, finalProg.AvailableSkillPoints);
    }

    // =========================================================================
    // T15: Store revision stays monotonic across serialized purchases and Applied attempts
    // =========================================================================
    [Fact]
    public async Task T15_StoreRevision_StaysMonotonic_AcrossPurchasesAndAttempts()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using (var setup = new SqliteGameplayStore(dbPath))
        {
            await setup.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 10, spentSp: 0, availableSp: 10, attackRank: 0);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        long rev0 = (await store.GetProgressionAsync()).StoreRevision;

        var p1 = await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        long rev1 = p1.StoreRevision;
        Assert.True(rev1 > rev0);

        var att1 = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-t15-1"));
        var p2 = await store.GetProgressionAsync();
        long rev2 = p2.StoreRevision;
        Assert.True(rev2 > rev1);

        var p3 = await store.PurchaseAttackUpgradeAsync(expectedRank: 1, expectedResetEpoch: 0);
        long rev3 = p3.StoreRevision;
        Assert.True(rev3 > rev2);
    }

    // =========================================================================
    // T16: Full Local Reset epoch fencing remains effective under concurrent operations
    // =========================================================================
    [Fact]
    public async Task T16_FullLocalResetEpochFencing_RemainsEffectiveUnderConcurrentOperations()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(intent.TargetEpoch);
        await store.ClearResetIntentAsync(intent.TargetEpoch);

        // Attempting an old epoch 0 purchase after reset must be rejected
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0));

        // Staging a pending intent with old epoch must be rejected
        var staleIntent = CreateIntent("sub-stale-epoch", resetEpoch: 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.StagePendingIntentAsync(staleIntent));
    }

    // =========================================================================
    // T18: A purchase beyond representable Attack damage fails without changing rank, SP, or revision
    // =========================================================================
    [Fact]
    public async Task T18_PurchaseBeyondRepresentableAttackDamage_FailsWithoutChangingRankSpOrRevision()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using (var setup = new SqliteGameplayStore(dbPath))
        {
            await setup.InitializeAsync();
        }

        int boundaryRank = int.MaxValue - CyberDefenseAttackUpgradePolicy.BaseAttackDamage; // int.MaxValue - 1

        await SetProgressionSpAsync(dbPath, earnedSp: 1000, spentSp: 500, availableSp: 500, attackRank: boundaryRank);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var initialProg = await store.GetProgressionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PurchaseAttackUpgradeAsync(expectedRank: boundaryRank, expectedResetEpoch: 0));

        var afterProg = await store.GetProgressionAsync();
        Assert.Equal(initialProg.AttackRank, afterProg.AttackRank);
        Assert.Equal(initialProg.SpentSkillPoints, afterProg.SpentSkillPoints);
        Assert.Equal(initialProg.AvailableSkillPoints, afterProg.AvailableSkillPoints);
        Assert.Equal(initialProg.StoreRevision, afterProg.StoreRevision);
    }

    // =========================================================================
    // T20: Existing exactly-once XP receipts remain identical under duplicate replay
    // =========================================================================
    [Fact]
    public async Task T20_ExistingExactlyOnceXpReceipts_RemainIdenticalUnderDuplicateReplay()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-t20-dup");
        var receipt1 = await store.ApplyAttemptTransactionAsync(intent);
        var receipt2 = await store.ApplyAttemptTransactionAsync(intent);

        Assert.Equal(receipt1.SubmissionId, receipt2.SubmissionId);
        Assert.Equal(receipt1.AwardedXp, receipt2.AwardedXp);
        Assert.Equal(receipt1.PlayerLevelAfter, receipt2.PlayerLevelAfter);
        Assert.Equal(receipt1.CumulativeXpAfter, receipt2.CumulativeXpAfter);

        var prog = await store.GetProgressionAsync();
        Assert.Equal(receipt1.CumulativeXpAfter, prog.CumulativeXp);
    }

    // =========================================================================
    // TEST A: Dispose invoked while an active operation owns the connection gate cannot destroy that connection
    // =========================================================================
    [Fact]
    public async Task TestA_DisposeWhileOperationHoldsConnectionGate_DoesNotDestroyConnection()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var gateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOpFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var opTask = store.ExecuteUnderGateForTestAsync(async conn =>
        {
            gateEntered.SetResult();
            await allowOpFinish.Task;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM gameplay_progression;";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(1, count);
        });

        await gateEntered.Task;

        store.Dispose();

        // Under safety contract, connection must NOT be destroyed while operation is active
        Assert.NotNull(store.ConnectionForTest);

        allowOpFinish.SetResult();
        await opTask;

        // After operation terminates, deferred cleanup must have released connection
        Assert.Null(store.ConnectionForTest);
    }

    // =========================================================================
    // TEST B: An active transaction can complete safely after Dispose is requested
    // =========================================================================
    [Fact]
    public async Task TestB_ActiveTransactionCanCompleteSafely_AfterDisposeRequested()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var txStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var opTask = store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            txStarted.SetResult();
            await disposeRequested.Task;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE gameplay_progression SET cumulative_xp = cumulative_xp + 77 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();
        });

        await txStarted.Task;
        store.Dispose();
        disposeRequested.SetResult();

        await opTask;

        using var verifyConn = new SqliteConnection($"Data Source={dbPath}");
        await verifyConn.OpenAsync();
        using var verifyCmd = verifyConn.CreateCommand();
        verifyCmd.CommandText = "SELECT cumulative_xp FROM gameplay_progression WHERE id = 1;";
        var xp = Convert.ToInt64(await verifyCmd.ExecuteScalarAsync());
        Assert.Equal(77L, xp);
    }

    // =========================================================================
    // TEST C: A slow operation exceeding the old five-second timeout does not trigger forced connection destruction
    // =========================================================================
    [Fact]
    public async Task TestC_SlowOperationExceedingFiveSecondTimeout_DoesNotTriggerForcedConnectionDestruction()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var gateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var opTask = store.ExecuteUnderGateForTestAsync(async conn =>
        {
            gateEntered.SetResult();
            await allowFinish.Task;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT player_level FROM gameplay_progression WHERE id = 1;";
            var level = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(1, level);
        });

        await gateEntered.Task;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        store.Dispose();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1000, $"Dispose blocked for {sw.ElapsedMilliseconds}ms");
        Assert.NotNull(store.ConnectionForTest);

        allowFinish.SetResult();
        await opTask;
    }

    // =========================================================================
    // TEST D: Dispose does not impose the previous five-second synchronous blocking delay
    // =========================================================================
    [Fact]
    public async Task TestD_Dispose_DoesNotImposeFiveSecondSynchronousBlockingDelay()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var gateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var bgTask = store.ExecuteUnderGateForTestAsync(async _ =>
        {
            gateEntered.SetResult();
            await finishGate.Task;
        });

        await gateEntered.Task;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        store.Dispose();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1000, $"Dispose blocked synchronously for {sw.ElapsedMilliseconds}ms");

        finishGate.SetResult();
        await bgTask;
    }

    // =========================================================================
    // TEST E: A queued operation observes terminal disposal and fails with documented exception without executing SQL
    // =========================================================================
    [Fact]
    public async Task TestE_QueuedOperationObservesTerminalDisposal_AndFailsWithoutExecutingSql()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using (var setup = new SqliteGameplayStore(dbPath))
        {
            await setup.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var ownerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOwnerFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var ownerTask = store.ExecuteUnderGateForTestAsync(async _ =>
        {
            ownerEntered.SetResult();
            await allowOwnerFinish.Task;
        });

        await ownerEntered.Task;

        var queuedTask = Task.Run(() => store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0));

        while (store.ActiveOperationsForTest < 2)
        {
            await Task.Yield();
        }

        store.Dispose();

        allowOwnerFinish.SetResult();
        await ownerTask;

        await Assert.ThrowsAsync<ObjectDisposedException>(() => queuedTask);

        using var verifyConn = new SqliteConnection($"Data Source={dbPath}");
        await verifyConn.OpenAsync();
        using var verifyCmd = verifyConn.CreateCommand();
        verifyCmd.CommandText = "SELECT attack_rank FROM gameplay_progression WHERE id = 1;";
        var rank = Convert.ToInt32(await verifyCmd.ExecuteScalarAsync());
        Assert.Equal(0, rank);
    }

    // =========================================================================
    // TEST F: Cancellation of waiting operation does not release another operation's ownership
    // =========================================================================
    [Fact]
    public async Task TestF_CancellationOfWaitingOperation_DoesNotReleaseAnotherOperationsOwnership()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var ownerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOwnerFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var ownerTask = store.ExecuteUnderGateForTestAsync(async conn =>
        {
            ownerEntered.SetResult();
            await allowOwnerFinish.Task;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT cumulative_xp FROM gameplay_progression WHERE id = 1;";
            await cmd.ExecuteScalarAsync();
        });

        await ownerEntered.Task;

        using var cts = new CancellationTokenSource();
        var waiterTask = store.GetProgressionAsync(cts.Token);

        while (store.ActiveOperationsForTest < 2)
        {
            await Task.Yield();
        }

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiterTask);
        Assert.False(ownerTask.IsCompleted);

        allowOwnerFinish.SetResult();
        await ownerTask;

        var prog = await store.GetProgressionAsync();
        Assert.NotNull(prog);
    }

    // =========================================================================
    // TEST G: CloseAsync and Dispose invoked concurrently do not corrupt connection state or deadlock
    // =========================================================================
    [Fact]
    public async Task TestG_CloseAsyncAndDispose_InvokedConcurrently_DoNotCorruptOrDeadlock()
    {
        for (int i = 0; i < 10; i++)
        {
            var dir = CreateTempDirectory();
            var dbPath = Path.Combine(dir, $"gameplay_concurrency_test_g_{i}.db");

            var store = new SqliteGameplayStore(dbPath);
            await store.InitializeAsync();

            using var barrier = new Barrier(2);

            var closeTask = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                try
                {
                    await store.CloseAsync();
                }
                catch (ObjectDisposedException)
                {
                }
            });

            var disposeTask = Task.Run(() =>
            {
                barrier.SignalAndWait();
                store.Dispose();
            });

            await Task.WhenAll(closeTask, disposeTask);

            Assert.True(store.IsDisposedForTest);
            Assert.Null(store.ConnectionForTest);
            Assert.Equal(0, store.ActiveOperationsForTest);
        }
    }

    // =========================================================================
    // TEST H: Concurrent Dispose calls are idempotent
    // =========================================================================
    [Fact]
    public async Task TestH_ConcurrentDisposeCalls_AreIdempotent()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        const int callerCount = 10;
        using var barrier = new Barrier(callerCount);

        var tasks = new Task[callerCount];
        for (int i = 0; i < callerCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                store.Dispose();
            });
        }

        await Task.WhenAll(tasks);

        Assert.True(store.IsDisposedForTest);
        Assert.Null(store.ConnectionForTest);
        Assert.Equal(0, store.ActiveOperationsForTest);
    }

    // =========================================================================
    // TEST I: A new method invocation cannot register itself after terminal disposal
    // =========================================================================
    [Fact]
    public async Task TestI_NewMethodInvocation_CannotRegisterAfterTerminalDisposal()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        store.Dispose();

        Assert.True(store.IsDisposedForTest);
        Assert.Equal(0, store.ActiveOperationsForTest);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetProgressionAsync());
        Assert.Equal(0, store.ActiveOperationsForTest);
    }

    // =========================================================================
    // TEST J: An already registered waiter is accounted for correctly during disposal
    // =========================================================================
    [Fact]
    public async Task TestJ_AlreadyRegisteredWaiter_IsAccountedForCorrectlyDuringDisposal()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var ownerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOwnerFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var ownerTask = store.ExecuteUnderGateForTestAsync(async _ =>
        {
            ownerEntered.SetResult();
            await allowOwnerFinish.Task;
        });

        await ownerEntered.Task;

        var waiterTask = Task.Run(() => store.GetProgressionAsync());

        while (store.ActiveOperationsForTest < 2)
        {
            await Task.Yield();
        }

        store.Dispose();

        Assert.Equal(2, store.ActiveOperationsForTest);
        Assert.NotNull(store.ConnectionForTest);

        allowOwnerFinish.SetResult();
        await ownerTask;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiterTask);

        Assert.Equal(0, store.ActiveOperationsForTest);
        Assert.Null(store.ConnectionForTest);
    }

    // =========================================================================
    // TEST K: A transaction exception followed by Dispose does not leave connection or semaphore ownership corrupted
    // =========================================================================
    [Fact]
    public async Task TestK_TransactionExceptionFollowedByDispose_DoesNotLeaveGateCorrupted()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PurchaseAttackUpgradeAsync(expectedRank: 999, expectedResetEpoch: 0));

        Assert.Equal(0, store.ActiveOperationsForTest);

        store.Dispose();

        Assert.True(store.IsDisposedForTest);
        Assert.Null(store.ConnectionForTest);
    }

    // =========================================================================
    // TEST L: Post-commit PRAGMA restoration cannot encounter a connection nulled by concurrent Dispose
    // =========================================================================
    [Fact]
    public async Task TestL_PostCommitPragmaRestoration_CannotEncounterNulledConnection()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var committedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowPragmaTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var opTask = store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE gameplay_progression SET cumulative_xp = cumulative_xp + 10 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();

            committedTcs.SetResult();
            await allowPragmaTcs.Task;

            using var restoreCmd = conn.CreateCommand();
            restoreCmd.CommandText = "PRAGMA synchronous = NORMAL;";
            await restoreCmd.ExecuteNonQueryAsync();
        });

        await committedTcs.Task;

        store.Dispose();

        Assert.NotNull(store.ConnectionForTest);

        allowPragmaTcs.SetResult();
        await opTask;

        Assert.Null(store.ConnectionForTest);
    }

    // =========================================================================
    // TEST M: Normal CloseAsync still allows the documented reopening behavior
    // =========================================================================
    [Fact]
    public async Task TestM_NormalCloseAsync_AllowsDocumentedReopeningBehavior()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.CloseAsync();
        Assert.Null(store.ConnectionForTest);

        var prog = await store.GetProgressionAsync();
        Assert.NotNull(prog);
        Assert.NotNull(store.ConnectionForTest);
    }

    // =========================================================================
    // TEST N: After terminal Dispose, initialization and all public connection operations fail closed
    // =========================================================================
    [Fact]
    public async Task TestN_AfterTerminalDispose_AllPublicOperationsFailClosed()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_concurrency_test.db");

        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();
        store.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.InitializeAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.CloseAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetRunStateAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetResetEpochAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetStoreRevisionAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetProgressionAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetAttackUpgradeSnapshotAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.PurchaseAttackUpgradeAsync(0, 0));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetReceiptAsync("sub-test"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.RecordReceiptDirectAsync(new CyberDefenseReceiptRecord(
            "sub-test",
            CyberDefenseReceiptKind.CalmModeSuppressed,
            "1+1",
            true,
            true,
            100,
            0,
            DateTimeOffset.UtcNow,
            null)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.StagePendingIntentAsync(CreateIntent("sub-test")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetPendingIntentAsync("sub-test"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetPendingIntentsAsync(0));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ClearPendingIntentAsync("sub-test"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ApplyAttemptTransactionAsync(CreateIntent("sub-test")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetResetIntentAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.BeginOrGetResetIntentAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ResetGameplayStateAsync(1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ClearResetIntentAsync(1));
    }
}
