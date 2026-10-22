namespace MathFirst.Core.Tests.CyberDefense;

using MathFirst.Application.Persistence;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;

public sealed class CyberDefenseSectorRecapTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstSectorRecap_" + Guid.NewGuid().ToString("N"));
    private readonly SqliteGameplayStore _store;
    private readonly List<CyberDefenseReceiptRecord> _receipts = [];
    private IGameplayStore Store => _store;

    public CyberDefenseSectorRecapTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new SqliteGameplayStore(Path.Combine(_directory, "synthetic.db"));
    }

    private async Task<CyberDefenseReceiptRecord> Answer(bool correct = true, bool eligible = true, long latency = 1000)
    {
        await Store.InitializeAsync();
        var intent = new CyberDefensePendingIntentRecord(
            "answer-" + _receipts.Count, "1+1", correct, eligible, latency,
            await Store.GetResetEpochAsync(), DateTimeOffset.UnixEpoch, 1, 0, 0, practicePosition: _receipts.Count + 1);
        await Store.StagePendingIntentAsync(intent);
        var receipt = await Store.ApplyAttemptTransactionAsync(intent);
        _receipts.Add(receipt);
        return receipt;
    }

    private async Task<CyberDefenseReceiptRecord> CompleteSector()
    {
        for (var i = 0; i < 1000; i++)
        {
            var receipt = await Answer();
            if (receipt.TransitionResult!.IsSectorCompleted) return receipt;
        }
        throw new InvalidOperationException("Synthetic sector did not complete.");
    }

    [Fact]
    public async Task CompleteSector_IncludesEveryCombatAnswerAndAllFourMetrics_ExcludesCalmMode()
    {
        await Answer(false);
        await Answer(false);
        await Answer(eligible: false, latency: 900000);
        var boss = await CompleteSector();
        var recap = await Store.GetCompletedSectorRecapAsync(boss.SubmissionId, 0);
        Assert.NotNull(recap);
        Assert.Equal(1, recap.Sector);
        Assert.Equal(24, recap.AnswerCount);
        Assert.Equal(22, recap.CorrectAnswerCount);
        Assert.Equal(24000, recap.ActiveAnsweringTimeMs);
        Assert.Equal(178, recap.XpGained);
        Assert.Equal(1, recap.LevelUps);
        Assert.Equal(100m * 22 / 24, recap.AccuracyPercentage);
    }

    [Fact]
    public async Task ConsecutiveSectors_BoundedByPreviousCompletion_AndHistoricalQueryStaysStable()
    {
        var first = await CompleteSector();
        var second = await CompleteSector();
        var recap = await Store.GetCompletedSectorRecapAsync(second.SubmissionId, 0);
        Assert.NotNull(recap);
        Assert.Equal(2, recap.Sector);
        Assert.Equal(second.CumulativeXpAfter - first.CumulativeXpAfter, recap.XpGained);
        Assert.Equal(second.PlayerLevelAfter - first.PlayerLevelAfter, recap.LevelUps);
        var earlier = await Store.GetCompletedSectorRecapAsync(first.SubmissionId, 0);
        Assert.NotNull(earlier);
        Assert.Equal(176, earlier.XpGained);
    }

    [Fact]
    public async Task RepeatedSectorOne_AfterGameOver_ExcludesAllPreviousRuns()
    {
        await CompleteSector();
        CyberDefenseReceiptRecord loss;
        do { loss = await Answer(false); } while (!loss.TransitionResult!.IsGameOver);
        var previous = loss;
        var boss = await CompleteSector();
        var recap = await Store.GetCompletedSectorRecapAsync(boss.SubmissionId, 0);
        Assert.NotNull(recap);
        Assert.Equal(1, recap.Sector);
        Assert.Equal(22, recap.AnswerCount);
        Assert.Equal(176, recap.XpGained);
        Assert.Equal(boss.PlayerLevelAfter - previous.PlayerLevelAfter, recap.LevelUps);
        Assert.Null(await Store.GetCompletedSectorRecapAsync(loss.SubmissionId, 0));
    }

    [Fact]
    public async Task DuplicateCommitAndTimestampTies_DoNotDuplicateOrReorderMetrics()
    {
        var first = await Answer();
        var intent = new CyberDefensePendingIntentRecord(first.SubmissionId, first.FactId, true, true, 1000, 0, DateTimeOffset.UnixEpoch);
        var replay = await Store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(first, replay);
        var boss = await CompleteSector();
        await Mutate("UPDATE gameplay_receipt_ledger SET processed_at = '2026-10-21T00:00:00.0000000+00:00';");
        var recap = await Store.GetCompletedSectorRecapAsync(boss.SubmissionId, 0);
        Assert.NotNull(recap);
        Assert.Equal(22, recap.AnswerCount);
        Assert.Equal(176, recap.XpGained);
        Assert.Null(await Store.GetCompletedSectorRecapAsync(first.SubmissionId, 0));
    }

    [Theory]
    [InlineData("DELETE FROM gameplay_receipt_ledger WHERE submission_id = 'answer-0';")]
    [InlineData("UPDATE gameplay_receipt_ledger SET awarded_xp = NULL, player_level_after = NULL, cumulative_xp_after = NULL WHERE submission_id = 'answer-0';")]
    [InlineData("UPDATE gameplay_receipt_ledger SET player_level_after = 7 WHERE submission_id = 'answer-0';")]
    public async Task MissingOrLegacyOrInconsistentEvidence_FailsClosed(string corruption)
    {
        var boss = await CompleteSector();
        await Mutate(corruption);
        Assert.Null(await Store.GetCompletedSectorRecapAsync(boss.SubmissionId, 0));
    }

    [Fact]
    public async Task EpochAndPendingResetFence_RejectStaleEvidence_AndHonorCancellation()
    {
        var boss = await CompleteSector();
        Assert.Null(await Store.GetCompletedSectorRecapAsync(boss.SubmissionId, 1));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.GetCompletedSectorRecapAsync(boss.SubmissionId, 0, cancelled.Token));
        var reset = await Store.BeginOrGetResetIntentAsync();
        Assert.Null(await Store.GetCompletedSectorRecapAsync(boss.SubmissionId, 0));
        await Store.ResetGameplayStateAsync(reset.TargetEpoch);
        await Store.ClearResetIntentAsync(reset.TargetEpoch);
        Assert.Null(await Store.GetCompletedSectorRecapAsync(boss.SubmissionId, 0));
        Assert.Null(await Store.GetCompletedSectorRecapAsync(boss.SubmissionId, reset.TargetEpoch));
    }

    private async Task Mutate(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Store.StoragePath}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task QueuedConcurrentCommit_DoesNotChangeHistoricalRecap_OrDuplicateProgression()
    {
        var boss = await CompleteSector();
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var blocker = _store.ExecuteUnderGateForTestAsync(async _ => { entered.SetResult(); await release.Task; });
        await entered.Task;
        var query = Store.GetCompletedSectorRecapAsync(boss.SubmissionId, 0);
        var commit = Answer();
        release.SetResult();
        await blocker;
        await commit;
        var recap = await query;
        Assert.NotNull(recap);
        Assert.Equal(176, recap.XpGained);
        Assert.Equal(182, (await Store.GetProgressionAsync()).CumulativeXp);
        Assert.Equal(recap, await Store.GetCompletedSectorRecapAsync(boss.SubmissionId, 0));
    }

    public void Dispose()
    {
        _store.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
