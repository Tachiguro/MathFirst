namespace MathFirst.Core.Tests.CyberDefense;

using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using System.Threading.Channels;

public sealed class CyberDefensePresentationSequenceTests
{
    private static CyberDefenseReceiptRecord Receipt(bool victory = true, long epoch = 0, string id = "completion")
    {
        var run = victory
            ? CyberDefenseRunState.CreateActive(1, 5, 100, new OpponentState(OpponentKind.Boss, 12, 1))
            : CyberDefenseRunState.CreateActive(1, 0, 1, new OpponentState(OpponentKind.Normal, 2, 2));
        return CyberDefenseReceiptRecord.CreateApplied(id, "1+1", victory, true, 1234, epoch, DateTimeOffset.UnixEpoch,
            CyberDefenseStateMachine.ApplyAttempt(run, victory, 1));
    }

    private static Task<bool> Current(CancellationToken _) => Task.FromResult(true);

    [Fact]
    public async Task VictoryThenRecapThenReveal_UsesTwoSecondStages_AndBlocksCaller()
    {
        var clock = new ControlledDelay();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var recap = new CyberDefenseSectorRecap("completion", 0, 1, 24, 22, 24000, 178, 1);
        var stages = new List<CyberDefensePresentationStage>();
        sequence.Changed += () => stages.Add(sequence.Stage);
        var pending = sequence.RunAsync(Receipt(), recap, Current);
        Assert.Equal(CyberDefensePresentationStage.Victory, sequence.Stage);
        Assert.True(sequence.IsBlocking);
        Assert.False(pending.IsCompleted);
        Assert.Equal(TimeSpan.FromSeconds(2), clock.Duration);
        var recapShown = StageReached(sequence, CyberDefensePresentationStage.Recap);
        await clock.Elapse();
        await recapShown;
        Assert.Equal(CyberDefensePresentationStage.Recap, sequence.Stage);
        Assert.Same(recap, sequence.Recap);
        Assert.False(pending.IsCompleted);
        await clock.Elapse();
        await pending;
        Assert.False(sequence.IsBlocking);
        Assert.Equal(new[] { CyberDefensePresentationStage.Victory, CyberDefensePresentationStage.Recap, CyberDefensePresentationStage.None }, stages);
    }

    [Fact]
    public async Task GameOver_ShowsRespectfulRebootOnly_AndDoesNotChangeAuthoritativeRun()
    {
        var clock = new ControlledDelay();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var receipt = Receipt(false);
        var pending = sequence.RunAsync(receipt, null, Current);
        Assert.Equal(CyberDefensePresentationStage.SystemOverload, sequence.Stage);
        await clock.Elapse();
        await pending;
        Assert.False(sequence.IsBlocking);
        Assert.Equal(1, receipt.TransitionResult!.NextState.Sector);
        Assert.Equal(100, receipt.TransitionResult.NextState.PlayerCurrentHp);
    }

    [Theory]
    [InlineData("background")]
    [InlineData("mode")]
    [InlineData("epoch")]
    [InlineData("disposal")]
    public async Task InvalidatedContext_CancelsPresentationAndNeverShowsRecap(string reason)
    {
        var clock = new ControlledDelay();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var current = true;
        var pending = sequence.RunAsync(Receipt(), null, _ => Task.FromResult(current));
        Assert.True(sequence.IsBlocking);
        current = false;
        if (reason is "background" or "disposal") sequence.Cancel();
        else await clock.Elapse();
        await pending;
        Assert.False(sequence.IsBlocking);
        Assert.Null(sequence.Recap);
    }

    [Fact]
    public async Task DuplicateRecoveredReceipt_IsPresentedOnceAcrossReentry_ButNewEpochCanReuseIdentity()
    {
        var sequence = new CyberDefensePresentationSequence((_, _) => Task.CompletedTask);
        var victories = 0;
        sequence.Changed += () => { if (sequence.Stage == CyberDefensePresentationStage.Victory) victories++; };
        await sequence.RunAsync(Receipt(), null, Current);
        await sequence.RunAsync(Receipt(), null, Current);
        Assert.Equal(1, victories);
        await sequence.RunAsync(Receipt(epoch: 1), null, Current);
        await sequence.RunAsync(Receipt(epoch: 0, id: "stale"), null, Current);
        Assert.Equal(2, victories);
    }

    [Fact]
    public async Task NormalDefeatAndSuppressedReceipt_DoNotStartBlockingPresentation()
    {
        var sequence = new CyberDefensePresentationSequence((_, _) => throw new InvalidOperationException("Unexpected timer"));
        var normal = CyberDefenseStateMachine.ApplyAttempt(CyberDefenseRunState.InitialRun(), true, 2);
        await sequence.RunAsync(CyberDefenseReceiptRecord.CreateApplied("normal", "1+1", true, true, 1000, 0,
            DateTimeOffset.UnixEpoch, normal), null, Current);
        await sequence.RunAsync(CyberDefenseReceiptRecord.CreateCalmModeSuppressed("calm", "1+1", true, false,
            1000, 0, DateTimeOffset.UnixEpoch), null, Current);
        Assert.False(sequence.IsBlocking);
    }

    [Fact]
    public async Task CancellationToken_ReleasesBarrierWithoutPresentingRecap()
    {
        var clock = new ControlledDelay();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        using var cts = new CancellationTokenSource();
        var pending = sequence.RunAsync(Receipt(), null, Current, cts.Token);
        Assert.True(sequence.IsBlocking);
        cts.Cancel();
        await pending;
        Assert.False(sequence.IsBlocking);
    }

    private static Task StageReached(CyberDefensePresentationSequence sequence, CyberDefensePresentationStage stage)
    {
        var reached = new TaskCompletionSource();
        void Check()
        {
            if (sequence.Stage != stage) return;
            sequence.Changed -= Check;
            reached.TrySetResult();
        }
        sequence.Changed += Check;
        Check();
        return reached.Task;
    }

    private sealed class ControlledDelay
    {
        private readonly Channel<TaskCompletionSource> _requests = Channel.CreateUnbounded<TaskCompletionSource>();
        public TimeSpan Duration { get; private set; }
        public Task Delay(TimeSpan duration, CancellationToken ct)
        {
            Duration = duration;
            var pending = new TaskCompletionSource();
            _requests.Writer.TryWrite(pending);
            return pending.Task.WaitAsync(ct);
        }
        public async Task Elapse() => (await _requests.Reader.ReadAsync()).TrySetResult();
    }
}
