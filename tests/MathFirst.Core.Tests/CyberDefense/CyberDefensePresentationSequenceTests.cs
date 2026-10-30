namespace MathFirst.Core.Tests.CyberDefense;

using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using System.Threading.Channels;

public sealed class CyberDefensePresentationSequenceTests
{
    private static readonly TimeSpan Victory = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Reveal = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static CyberDefenseReceiptRecord Receipt(bool victory = true, long epoch = 0, string id = "completion")
    {
        var run = victory
            ? CyberDefenseRunState.CreateActive(1, 5, 100, new OpponentState(OpponentKind.Boss, 12, 1))
            : CyberDefenseRunState.CreateActive(1, 0, 1, new OpponentState(OpponentKind.Normal, 2, 2));
        return CyberDefenseReceiptRecord.CreateApplied(id, "1+1", victory, true, 1234, epoch, DateTimeOffset.UnixEpoch,
            CyberDefenseStateMachine.ApplyAttempt(run, victory, 1));
    }

    private static Task<bool> Current(CancellationToken _) => Task.FromResult(true);

    private static CyberDefenseSectorRecap Recap(string id = "completion", long epoch = 0)
        => new(id, epoch, 1, 24, 22, 24000, 178, 1);

    /// <summary>Drives a boss victory to the point where Continue is revealed.</summary>
    private static async Task<(Task<bool> Pending, long Id)> ReachContinueAsync(
        FakeClock clock, CyberDefensePresentationSequence sequence, bool boss = true,
        CyberDefenseSectorRecap? recap = null, Func<CancellationToken, Task<bool>>? isCurrent = null,
        long epoch = 0, string id = "completion", CancellationToken ct = default)
    {
        var pending = sequence.RunAsync(Receipt(boss, epoch, id), recap, isCurrent ?? Current, ct);
        if (boss) (await clock.NextAsync()).Elapse();
        (await clock.NextAsync()).Elapse();
        await ContinueRevealedAsync(sequence);
        return (pending, sequence.PresentationId);
    }

    private static async Task ContinueRevealedAsync(CyberDefensePresentationSequence sequence)
    {
        var revealed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check() { if (sequence.CanContinue) revealed.TrySetResult(); }
        sequence.Changed += Check;
        Check();
        try { await revealed.Task.WaitAsync(Wait); }
        finally { sequence.Changed -= Check; }
    }

    [Fact]
    public async Task BossVictory_ShowsVictoryThenRecap_AndRevealsContinueOnlyAfterThreeRecapSeconds()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var recap = Recap();
        var stages = new List<CyberDefensePresentationStage>();
        sequence.Changed += () => stages.Add(sequence.Stage);

        var pending = sequence.RunAsync(Receipt(), recap, Current);
        Assert.Equal(CyberDefensePresentationStage.Victory, sequence.Stage);
        Assert.True(sequence.IsBlocking);
        Assert.False(sequence.CanContinue);
        var victoryDelay = await clock.NextAsync();
        Assert.Equal(Victory, victoryDelay.Duration);

        victoryDelay.Elapse();
        var revealDelay = await clock.NextAsync();
        Assert.Equal(CyberDefensePresentationStage.Recap, sequence.Stage);
        Assert.Same(recap, sequence.Recap);
        Assert.Equal(Reveal, revealDelay.Duration);
        Assert.False(sequence.CanContinue);
        Assert.False(sequence.Acknowledge(sequence.PresentationId));
        Assert.False(pending.IsCompleted);

        revealDelay.Elapse();
        await ContinueRevealedAsync(sequence);
        Assert.Equal(CyberDefensePresentationStage.Recap, sequence.Stage);
        Assert.False(pending.IsCompleted);
        Assert.True(sequence.IsBlocking);

        Assert.True(sequence.Acknowledge(sequence.PresentationId));
        Assert.True(await pending.WaitAsync(Wait));
        Assert.False(sequence.IsBlocking);
        Assert.False(sequence.CanContinue);
        Assert.Null(sequence.Recap);
        Assert.Equal(CyberDefensePresentationStage.None, sequence.Stage);
        Assert.Equal(
            new[] { CyberDefensePresentationStage.Victory, CyberDefensePresentationStage.Recap, CyberDefensePresentationStage.None },
            stages.Distinct().ToArray());
    }

    [Fact]
    public async Task EarlyContinue_IsRejectedWithoutLatching_AndLaterRevealStillWaitsForTheUser()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var pending = sequence.RunAsync(Receipt(), Recap(), Current);
        var id = sequence.PresentationId;
        for (var i = 0; i < 5; i++) Assert.False(sequence.Acknowledge(id));
        (await clock.NextAsync()).Elapse();
        for (var i = 0; i < 5; i++) Assert.False(sequence.Acknowledge(id));
        (await clock.NextAsync()).Elapse();
        await ContinueRevealedAsync(sequence);
        await Task.Delay(50);
        Assert.False(pending.IsCompleted);
        Assert.True(sequence.IsBlocking);
        Assert.True(sequence.Acknowledge(id));
        Assert.True(await pending.WaitAsync(Wait));
    }

    [Fact]
    public async Task Recap_NeverAdvancesBecauseATimerExpired_EvenAfterThirtySimulatedSeconds()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var checks = 0;
        var (pending, id) = await ReachContinueAsync(clock, sequence, recap: Recap(), isCurrent: _ => { checks++; return Task.FromResult(true); });

        var simulated = TimeSpan.Zero;
        while (simulated < TimeSpan.FromSeconds(30))
        {
            var tick = await clock.NextAsync();
            Assert.Equal(Tick, tick.Duration);
            tick.Elapse();
            simulated += tick.Duration;
        }
        var next = await clock.NextAsync();
        Assert.False(pending.IsCompleted);
        Assert.True(sequence.IsBlocking);
        Assert.True(sequence.CanContinue);
        Assert.Equal(CyberDefensePresentationStage.Recap, sequence.Stage);
        Assert.NotNull(sequence.Recap);
        Assert.True(checks > 15, "Context validity must be re-observed while the recap waits.");
        Assert.Equal(Tick, next.Duration);

        Assert.True(sequence.Acknowledge(id));
        Assert.True(await pending.WaitAsync(Wait));
    }

    [Fact]
    public async Task Acknowledge_ReleasesTheBarrierExactlyOnce_UnderRapidRepeatedActivation()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var (pending, id) = await ReachContinueAsync(clock, sequence);
        var accepted = 0;
        Parallel.For(0, 64, _ => { if (sequence.Acknowledge(id)) Interlocked.Increment(ref accepted); });
        Assert.Equal(1, accepted);
        Assert.True(await pending.WaitAsync(Wait));
        Assert.False(sequence.Acknowledge(id));
        Assert.False(sequence.IsBlocking);
    }

    [Fact]
    public async Task GameOver_ShowsSystemRestartUntilTheUserContinues_WithoutChangingTheAuthoritativeRun()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var receipt = Receipt(false);
        var pending = sequence.RunAsync(receipt, null, Current);
        Assert.Equal(CyberDefensePresentationStage.SystemOverload, sequence.Stage);
        var reveal = await clock.NextAsync();
        Assert.Equal(Reveal, reveal.Duration);
        Assert.False(sequence.CanContinue);
        Assert.False(sequence.Acknowledge(sequence.PresentationId));
        reveal.Elapse();
        await ContinueRevealedAsync(sequence);
        for (var i = 0; i < 15; i++) (await clock.NextAsync()).Elapse();
        Assert.False(pending.IsCompleted);
        Assert.Equal(CyberDefensePresentationStage.SystemOverload, sequence.Stage);
        Assert.True(sequence.Acknowledge(sequence.PresentationId));
        Assert.True(await pending.WaitAsync(Wait));
        Assert.False(sequence.IsBlocking);
        Assert.Equal(1, receipt.TransitionResult!.NextState.Sector);
        Assert.Equal(100, receipt.TransitionResult.NextState.PlayerCurrentHp);
    }

    [Fact]
    public async Task MissingOrMismatchedRecap_StaysPresentableAndStillRequiresContinue()
    {
        foreach (var recap in new CyberDefenseSectorRecap?[] { null, Recap(id: "someone-else"), Recap(epoch: 9) })
        {
            var clock = new FakeClock();
            var sequence = new CyberDefensePresentationSequence(clock.Delay);
            var (pending, id) = await ReachContinueAsync(clock, sequence, recap: recap);
            Assert.Equal(CyberDefensePresentationStage.Recap, sequence.Stage);
            Assert.Null(sequence.Recap);
            Assert.True(sequence.CanContinue);
            Assert.False(pending.IsCompleted);
            Assert.True(sequence.Acknowledge(id));
            Assert.True(await pending.WaitAsync(Wait));
        }
    }

    [Fact]
    public async Task StalePresentationIdentity_CannotAcknowledgeCurrentOrLaterCeremony()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var (first, firstId) = await ReachContinueAsync(clock, sequence, id: "first");
        Assert.False(sequence.Acknowledge(firstId + 1));
        Assert.False(sequence.Acknowledge(0));
        Assert.False(sequence.Acknowledge(-1));
        Assert.True(sequence.Acknowledge(firstId));
        Assert.True(await first.WaitAsync(Wait));

        var (second, secondId) = await ReachContinueAsync(clock, sequence, id: "second");
        Assert.NotEqual(firstId, secondId);
        Assert.False(sequence.Acknowledge(firstId));
        await Task.Delay(50);
        Assert.False(second.IsCompleted);
        Assert.True(sequence.IsBlocking);
        Assert.True(sequence.Acknowledge(secondId));
        Assert.True(await second.WaitAsync(Wait));
    }

    [Fact]
    public async Task Cancel_WhileWaitingForContinue_ReleasesWithoutAcknowledgement_AndLeavesNoLatch()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var (pending, id) = await ReachContinueAsync(clock, sequence, recap: Recap());
        sequence.Cancel();
        Assert.False(await pending.WaitAsync(Wait));
        Assert.False(sequence.IsBlocking);
        Assert.False(sequence.CanContinue);
        Assert.Null(sequence.Recap);
        Assert.False(sequence.Acknowledge(id));

        var (next, nextId) = await ReachContinueAsync(clock, sequence, id: "after-background");
        Assert.False(sequence.Acknowledge(id));
        await Task.Delay(50);
        Assert.False(next.IsCompleted);
        Assert.True(sequence.Acknowledge(nextId));
        Assert.True(await next.WaitAsync(Wait));
    }

    [Fact]
    public async Task CancellationToken_WhileWaitingForContinue_ReleasesWithoutAcknowledgement()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        using var cts = new CancellationTokenSource();
        var (pending, id) = await ReachContinueAsync(clock, sequence, ct: cts.Token);
        cts.Cancel();
        Assert.False(await pending.WaitAsync(Wait));
        Assert.False(sequence.IsBlocking);
        Assert.False(sequence.Acknowledge(id));
    }

    [Fact]
    public async Task CancellationToken_BeforeContinueIsRevealed_ReleasesBarrier()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        using var cts = new CancellationTokenSource();
        var pending = sequence.RunAsync(Receipt(), null, Current, cts.Token);
        Assert.True(sequence.IsBlocking);
        cts.Cancel();
        Assert.False(await pending.WaitAsync(Wait));
        Assert.False(sequence.IsBlocking);
    }

    [Theory]
    [InlineData("before-reveal")]
    [InlineData("while-waiting")]
    public async Task InvalidatedContext_ReleasesWithoutAcknowledgement_AndRejectsLateContinue(string when)
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var current = true;
        Task<bool> IsCurrent(CancellationToken _) => Task.FromResult(current);
        Task<bool> pending;
        long id;
        if (when == "before-reveal")
        {
            pending = sequence.RunAsync(Receipt(), Recap(), IsCurrent);
            (await clock.NextAsync()).Elapse();
            var reveal = await clock.NextAsync();
            id = sequence.PresentationId;
            current = false;
            reveal.Elapse();
        }
        else
        {
            (pending, id) = await ReachContinueAsync(clock, sequence, recap: Recap(), isCurrent: IsCurrent);
            current = false;
            (await clock.NextAsync()).Elapse();
        }
        Assert.False(await pending.WaitAsync(Wait));
        Assert.False(sequence.IsBlocking);
        Assert.Null(sequence.Recap);
        Assert.False(sequence.Acknowledge(id));
    }

    [Fact]
    public async Task InvalidationAfterContinue_IsObservedBeforeTheCallerBarrierIsReleased()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var current = true;
        var (pending, id) = await ReachContinueAsync(clock, sequence, isCurrent: _ => Task.FromResult(current));
        current = false;
        Assert.True(sequence.Acknowledge(id));
        Assert.False(await pending.WaitAsync(Wait));
        Assert.False(sequence.IsBlocking);
    }

    [Fact]
    public async Task NewResetEpoch_SupersedesAnOutstandingCeremony_AndOldContinueCannotReleaseTheNewOne()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var (old, oldId) = await ReachContinueAsync(clock, sequence, epoch: 0, id: "epoch0");
        var (current, currentId) = await ReachContinueAsync(clock, sequence, epoch: 1, id: "epoch1");
        Assert.False(await old.WaitAsync(Wait));
        Assert.NotEqual(oldId, currentId);
        Assert.False(sequence.Acknowledge(oldId));
        Assert.False(current.IsCompleted);
        Assert.True(sequence.Acknowledge(currentId));
        Assert.True(await current.WaitAsync(Wait));

        Assert.False(await sequence.RunAsync(Receipt(epoch: 0, id: "stale"), null, Current));
        Assert.False(sequence.IsBlocking);
    }

    [Fact]
    public async Task DuplicateRecoveredReceipt_IsPresentedOnceAcrossReentry_ButNewEpochCanReuseIdentity()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var victories = 0;
        sequence.Changed += () => { if (sequence.Stage == CyberDefensePresentationStage.Victory) victories++; };

        var (first, firstId) = await ReachContinueAsync(clock, sequence);
        Assert.True(sequence.Acknowledge(firstId));
        Assert.True(await first.WaitAsync(Wait));
        Assert.False(await sequence.RunAsync(Receipt(), null, Current));
        Assert.False(sequence.IsBlocking);
        Assert.Equal(1, victories);

        var (second, secondId) = await ReachContinueAsync(clock, sequence, epoch: 1);
        Assert.True(sequence.Acknowledge(secondId));
        Assert.True(await second.WaitAsync(Wait));
        Assert.False(await sequence.RunAsync(Receipt(epoch: 0, id: "stale"), null, Current));
        Assert.Equal(2, victories);
    }

    [Fact]
    public async Task NormalDefeatAndSuppressedReceipt_DoNotStartBlockingPresentation()
    {
        var sequence = new CyberDefensePresentationSequence((_, _) => throw new InvalidOperationException("Unexpected timer"));
        var normal = CyberDefenseStateMachine.ApplyAttempt(CyberDefenseRunState.InitialRun(), true, 2);
        Assert.False(await sequence.RunAsync(CyberDefenseReceiptRecord.CreateApplied("normal", "1+1", true, true, 1000, 0,
            DateTimeOffset.UnixEpoch, normal), null, Current));
        Assert.False(await sequence.RunAsync(CyberDefenseReceiptRecord.CreateCalmModeSuppressed("calm", "1+1", true, false,
            1000, 0, DateTimeOffset.UnixEpoch), null, Current));
        Assert.False(sequence.IsBlocking);
        Assert.False(sequence.CanContinue);
    }

    [Fact]
    public async Task DelayFailureWhileWaiting_SurfacesToTheCallerAndReleasesTheBarrier()
    {
        var calls = 0;
        var sequence = new CyberDefensePresentationSequence((_, _) =>
            ++calls < 3 ? Task.CompletedTask : Task.FromException(new InvalidOperationException("clock failed")));
        var pending = sequence.RunAsync(Receipt(), null, Current);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(Wait));
        Assert.False(sequence.IsBlocking);
        Assert.False(sequence.CanContinue);
        Assert.False(sequence.Acknowledge(sequence.PresentationId));
    }

    private sealed record PendingDelay(TimeSpan Duration, TaskCompletionSource Gate)
    {
        public void Elapse() => Gate.TrySetResult();
    }

    private sealed class FakeClock
    {
        private readonly Channel<PendingDelay> _requests = Channel.CreateUnbounded<PendingDelay>();

        public Task Delay(TimeSpan duration, CancellationToken ct)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.Writer.TryWrite(new PendingDelay(duration, gate));
            ct.Register(() => gate.TrySetCanceled(ct));
            return gate.Task.WaitAsync(ct);
        }

        /// <summary>Next delay that is still outstanding; requests cancelled by the sequence are skipped.</summary>
        public async Task<PendingDelay> NextAsync()
        {
            while (true)
            {
                var next = await _requests.Reader.ReadAsync().AsTask().WaitAsync(Wait);
                if (!next.Gate.Task.IsCompleted) return next;
            }
        }
    }
}
