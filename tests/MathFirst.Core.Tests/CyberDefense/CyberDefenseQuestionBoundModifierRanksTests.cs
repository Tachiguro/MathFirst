namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// MF-CYBER-007 Slice 13B: question-bound Critical Strike / Overdrive rank capture at presentation,
/// purchase notifications, cache safety and durable staging. No combat modifier damage is active.
/// </summary>
public sealed class CyberDefenseQuestionBoundModifierRanksTests
{
    private static CyberDefenseCombatModifierSnapshot Snap(int crit, int od, long revision, long epoch = 0) =>
        new(crit, od, CyberDefenseCombatModifierState.Initial, revision, epoch,
            new GameplayResetIntentRecord(false, epoch, epoch, null, DateTimeOffset.UtcNow));

    private static (CyberDefenseCombatCoordinator Coordinator, ModifierFakeGameplayStore Store, CapturingConsumer Consumer) Create(
        CyberDefenseCombatModifierSnapshot? initial = null)
    {
        var store = new ModifierFakeGameplayStore { Current = initial ?? Snap(0, 0, 1) };
        var consumer = new CapturingConsumer();
        return (new CyberDefenseCombatCoordinator(store, consumer), store, consumer);
    }

    private static async Task<CyberDefenseQuestionBoundModifierRanks> EventuallyBindsAsync(
        ICyberDefenseCombatCoordinator coordinator, Func<CyberDefenseQuestionBoundModifierRanks, bool> predicate, long firstProbeKey)
    {
        // Every probe is a brand-new question, so a probe can never be rebound after it has been observed.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        for (long key = firstProbeKey; DateTime.UtcNow < deadline; key++)
        {
            var ranks = await coordinator.GetOrBindCombatModifierRanksAsync(key);
            if (predicate(ranks))
            {
                return ranks;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Future question never received the warmed snapshot ranks.");
    }

    // ---------------------------------------------------------------- contract type

    [Fact]
    public void RankType_RejectsOutOfRangeRanks_AndZeroIsConservative()
    {
        Assert.Equal(0, CyberDefenseQuestionBoundModifierRanks.Zero.BoundCriticalStrikeRank);
        Assert.Equal(0, CyberDefenseQuestionBoundModifierRanks.Zero.BoundOverdriveRank);
        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(2, 2), new CyberDefenseQuestionBoundModifierRanks(2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CyberDefenseQuestionBoundModifierRanks(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CyberDefenseQuestionBoundModifierRanks(CyberDefenseCriticalStrikePolicy.MaxRank + 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CyberDefenseQuestionBoundModifierRanks(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CyberDefenseQuestionBoundModifierRanks(0, CyberDefenseOverdrivePolicy.MaxRank + 1));
    }

    // ---------------------------------------------------------------- presentation-time capture

    [Fact]
    public async Task InitialRanks_AreZeroZero()
    {
        var (coordinator, _, _) = Create();
        await coordinator.InitializeAsync();

        var ranks = await coordinator.GetOrBindCombatModifierRanksAsync(1);

        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, ranks);
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, coordinator.GetBoundCombatModifierRanksOrDefault(1));
    }

    [Fact]
    public async Task PurchasedRanks_AreCapturedSynchronouslyAtPresentation_WhenCacheIsWarm()
    {
        var (coordinator, store, _) = Create(Snap(1, 2, 7));
        await coordinator.InitializeAsync();
        int readsBefore = store.ReadCount;
        store.ReadGate = new TaskCompletionSource<bool>(); // any further store read would never complete

        var task = coordinator.GetOrBindCombatModifierRanksAsync(5);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(1, 2), await task);
        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(1, 2), coordinator.GetBoundCombatModifierRanksOrDefault(5));
        Assert.Equal(readsBefore, store.ReadCount);
    }

    [Fact]
    public async Task CriticalStrikePurchaseDuringActiveQuestion_KeepsQuestionRanks_AndNextQuestionGetsPurchase()
    {
        var (coordinator, store, _) = Create();
        await coordinator.InitializeAsync();
        var q = await coordinator.GetOrBindCombatModifierRanksAsync(10);

        store.Raise(Snap(1, 0, 2));

        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, q);
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(10));
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, coordinator.GetBoundCombatModifierRanksOrDefault(10));
        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(1, 0), await coordinator.GetOrBindCombatModifierRanksAsync(11));
    }

    [Fact]
    public async Task OverdrivePurchaseDuringActiveQuestion_KeepsQuestionRanks_AndNextQuestionGetsPurchase()
    {
        var (coordinator, store, _) = Create(Snap(1, 0, 3));
        await coordinator.InitializeAsync();
        await coordinator.GetOrBindCombatModifierRanksAsync(10);

        store.Raise(Snap(1, 1, 4));
        store.Raise(Snap(1, 2, 5));

        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(1, 0), coordinator.GetBoundCombatModifierRanksOrDefault(10));
        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(1, 2), await coordinator.GetOrBindCombatModifierRanksAsync(11));
    }

    [Fact]
    public async Task RepeatedBindingOfSameQuestion_IsIdempotentAndImmutable()
    {
        var (coordinator, store, _) = Create(Snap(1, 1, 3));
        await coordinator.InitializeAsync();
        var first = await coordinator.GetOrBindCombatModifierRanksAsync(20);

        store.Raise(Snap(2, 2, 9));
        var second = await coordinator.GetOrBindCombatModifierRanksAsync(20);
        var third = await coordinator.GetOrBindCombatModifierRanksAsync(20);

        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(1, 1), first);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(first, coordinator.GetBoundCombatModifierRanksOrDefault(20));
    }

    [Fact]
    public async Task NegativeQuestionKey_FailsClosedWithoutBinding()
    {
        var (coordinator, _, _) = Create(Snap(2, 2, 3));
        await coordinator.InitializeAsync();

        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(-1));
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, coordinator.GetBoundCombatModifierRanksOrDefault(-1));
    }

    [Fact]
    public async Task MissingPresentationBinding_FailsClosedToZero_EvenWhenPurchasedRanksAreCached()
    {
        var (coordinator, _, _) = Create(Snap(2, 2, 3));
        await coordinator.InitializeAsync();

        // Never presented/bound: submission-time lookup must not acquire the purchased ranks.
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, coordinator.GetBoundCombatModifierRanksOrDefault(99));
    }

    [Fact]
    public async Task ModifierBinding_DoesNotChangeAttackDamageOrFirewallBinding()
    {
        var (coordinator, _, _) = Create(Snap(2, 2, 3));
        await coordinator.InitializeAsync();
        int damageBefore = await coordinator.GetOrBindEffectiveAttackDamageAsync(30);

        await coordinator.GetOrBindCombatModifierRanksAsync(30);

        Assert.Equal(damageBefore, coordinator.GetBoundEffectiveAttackDamageOrDefault(30));
        Assert.Equal(0, coordinator.GetBoundFirewallRankOrDefault(30));
    }

    // ---------------------------------------------------------------- cold cache / delayed snapshots

    [Fact]
    public async Task ColdSnapshot_BindsConservativeZeroImmediately_WithoutWaitingForTheStore()
    {
        var (coordinator, store, _) = Create(Snap(2, 2, 5));
        store.ReadGate = new TaskCompletionSource<bool>();

        var task = coordinator.GetOrBindCombatModifierRanksAsync(40);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await task);
        store.ReadGate.SetResult(true);
    }

    [Fact]
    public async Task DelayedSnapshot_CannotRetroactivelyUpgradePresentedQuestion_ButWarmsFutureQuestions()
    {
        var (coordinator, store, _) = Create(Snap(2, 2, 5));
        store.ReadGate = new TaskCompletionSource<bool>();
        var q = await coordinator.GetOrBindCombatModifierRanksAsync(50);
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, q);

        store.ReadGate.SetResult(true);
        var future = await EventuallyBindsAsync(coordinator, r => r.BoundCriticalStrikeRank == 2, firstProbeKey: 1000);

        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(2, 2), future);
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, coordinator.GetBoundCombatModifierRanksOrDefault(50));
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(50));
    }

    [Fact]
    public async Task ColdSnapshotStoreFailure_FallsBackToZero_WithoutThrowing()
    {
        var (coordinator, store, _) = Create(Snap(2, 2, 5));
        store.ThrowOnRead = true;

        var ranks = await coordinator.GetOrBindCombatModifierRanksAsync(60);
        await Task.Delay(50);

        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, ranks);
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(61));
    }

    [Fact]
    public async Task CancelledToken_DoesNotBind_AndThrowsCancellation()
    {
        var (coordinator, _, _) = Create(Snap(2, 2, 5));
        await coordinator.InitializeAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.GetOrBindCombatModifierRanksAsync(70, cts.Token));

        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, coordinator.GetBoundCombatModifierRanksOrDefault(70));
    }

    // ---------------------------------------------------------------- cache monotonicity / fencing

    [Fact]
    public async Task OutOfOrderCallbacks_CannotRegressTheAuthoritativeCache()
    {
        var (coordinator, store, _) = Create();
        await coordinator.InitializeAsync();

        store.Raise(Snap(2, 2, 9));
        store.Raise(Snap(0, 0, 3));
        store.Raise(Snap(1, 1, 8));

        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(2, 2), await coordinator.GetOrBindCombatModifierRanksAsync(80));
    }

    [Fact]
    public async Task Reset_ClearsOldBindings_AndOldBindingsAreNotRestored()
    {
        var (coordinator, store, _) = Create(Snap(2, 2, 9));
        await coordinator.InitializeAsync();
        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(2, 2), await coordinator.GetOrBindCombatModifierRanksAsync(90));

        store.Raise(Snap(0, 0, 1, epoch: 1));

        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, coordinator.GetBoundCombatModifierRanksOrDefault(90));
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(90));
    }

    [Fact]
    public async Task OldEpochCallbacks_AreIgnoredAfterReset()
    {
        var (coordinator, store, _) = Create(Snap(2, 2, 9));
        await coordinator.InitializeAsync();
        store.Raise(Snap(0, 0, 1, epoch: 1));

        store.Raise(Snap(2, 2, 99, epoch: 0));

        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(100));
    }

    [Fact]
    public async Task EpochAdvanceWithPurchase_UsesNewEpochRanksForNextQuestion()
    {
        var (coordinator, store, _) = Create(Snap(2, 2, 9));
        await coordinator.InitializeAsync();
        store.Raise(Snap(0, 0, 1, epoch: 1));
        store.Raise(Snap(1, 0, 2, epoch: 1));

        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(1, 0), await coordinator.GetOrBindCombatModifierRanksAsync(101));
    }

    [Fact]
    public async Task InvalidateState_PreventsStaleWarmSnapshotFromRestoringRanks()
    {
        var (coordinator, store, _) = Create(Snap(2, 2, 5));
        store.ReadGate = new TaskCompletionSource<bool>();
        await coordinator.GetOrBindCombatModifierRanksAsync(110); // starts the gated warm-up
        coordinator.InvalidateState();

        store.ReadGate.SetResult(true);
        await Task.Delay(150);

        // Stale generation must not have repopulated the cache: a new question stays conservative.
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(111));
    }

    [Fact]
    public async Task InvalidateState_ClearsBindingsAndCache()
    {
        var (coordinator, _, _) = Create(Snap(2, 2, 5));
        await coordinator.InitializeAsync();
        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(2, 2), await coordinator.GetOrBindCombatModifierRanksAsync(120));

        coordinator.InvalidateState();

        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, coordinator.GetBoundCombatModifierRanksOrDefault(120));
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(121));
    }

    [Fact]
    public async Task Dispose_UnsubscribesAndPreventsStaleCallbacksAndWarmups()
    {
        var (coordinator, store, _) = Create(Snap(2, 2, 5));
        store.ReadGate = new TaskCompletionSource<bool>();
        await coordinator.GetOrBindCombatModifierRanksAsync(130); // gated warm-up in flight
        Assert.Equal(1, store.ModifierHandlerCount);

        coordinator.Dispose();
        Assert.Equal(0, store.ModifierHandlerCount);
        store.Raise(Snap(2, 2, 50));
        store.ReadGate.SetResult(true);
        await Task.Delay(100);

        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(131));
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, coordinator.GetBoundCombatModifierRanksOrDefault(131));
    }

    [Fact]
    public async Task BoundedHistoryEviction_FailsClosedAndNeverRebindsEvictedQuestionAtNewerRanks()
    {
        var (coordinator, store, _) = Create(Snap(1, 0, 3));
        await coordinator.InitializeAsync();
        for (long key = 1; key <= 20; key++)
        {
            await coordinator.GetOrBindCombatModifierRanksAsync(key);
        }

        store.Raise(Snap(2, 2, 10));

        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(1));
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, coordinator.GetBoundCombatModifierRanksOrDefault(1));
        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(1, 0), coordinator.GetBoundCombatModifierRanksOrDefault(20));
        Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(2, 2), await coordinator.GetOrBindCombatModifierRanksAsync(21));
    }

    [Fact]
    public async Task ConcurrentBindingAndPurchases_NeverChangeAnyBoundQuestion()
    {
        var (coordinator, store, _) = Create();
        await coordinator.InitializeAsync();
        var bound = new System.Collections.Concurrent.ConcurrentDictionary<long, CyberDefenseQuestionBoundModifierRanks>();

        var raiser = Task.Run(() =>
        {
            for (int rev = 2; rev <= 40; rev++)
            {
                store.Raise(Snap(Math.Min(2, rev / 10), Math.Min(2, rev / 15), rev));
            }
        });
        var binders = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (long key = 1; key <= 14; key++)
            {
                var first = await coordinator.GetOrBindCombatModifierRanksAsync(key);
                var seen = bound.GetOrAdd(key, first);
                Assert.Equal(seen, await coordinator.GetOrBindCombatModifierRanksAsync(key));
                Assert.Equal(seen, coordinator.GetBoundCombatModifierRanksOrDefault(key));
            }
        })).ToArray();

        await Task.WhenAll(binders.Append(raiser));
    }

    // ---------------------------------------------------------------- staging

    [Fact]
    public async Task StageIntent_PassesBoundRanksAndBaseDamageToConsumer()
    {
        var (coordinator, _, consumer) = Create();

        await coordinator.StageIntentAsync("sub-1", "add:2+3", true, 450, true, effectiveAttackDamage: 3, boundCriticalStrikeRank: 1, boundOverdriveRank: 2);

        var intent = Assert.Single(consumer.Staged);
        Assert.Equal("sub-1", intent.SubmissionId);
        Assert.Equal("add:2+3", intent.FactId);
        Assert.True(intent.IsCorrect);
        Assert.True(intent.IsEligible);
        Assert.Equal(450, intent.ResponseLatencyMs);
        Assert.Equal(3, intent.EffectiveAttackDamage);
        Assert.Equal(1, intent.BoundCriticalStrikeRank);
        Assert.Equal(2, intent.BoundOverdriveRank);
    }

    [Fact]
    public async Task StageIntent_LegacyOverloadsDefaultToRankZero()
    {
        var (coordinator, _, consumer) = Create();

        await coordinator.StageIntentAsync("sub-a", "add:1+1", true, 100, true);
        await coordinator.StageIntentAsync("sub-b", "add:1+2", false, 200, false, effectiveAttackDamage: 2);

        Assert.Equal(2, consumer.Staged.Count);
        Assert.All(consumer.Staged, i =>
        {
            Assert.Equal(0, i.BoundCriticalStrikeRank);
            Assert.Equal(0, i.BoundOverdriveRank);
        });
        Assert.Equal(2, consumer.Staged[1].EffectiveAttackDamage);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(3, 0)]
    [InlineData(99, 0)]
    [InlineData(0, 99)]
    [InlineData(1, -1)]
    [InlineData(2, 99)]
    [InlineData(-1, 2)]
    public async Task StageIntent_InvalidExplicitRanks_AreRejectedWithoutStagingToConsumer(int crit, int od)
    {
        var (coordinator, _, consumer) = Create(Snap(2, 2, 5));
        await coordinator.InitializeAsync();

        var exception = await Record.ExceptionAsync(() =>
            coordinator.StageIntentAsync("sub-r", "add:1+1", true, 100, true, 1, crit, od));

        Assert.Null(exception);
        Assert.Empty(consumer.Staged);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    public async Task StageIntent_ValidExplicitRanks_AreStagedUnchanged(int crit, int od)
    {
        var (coordinator, _, consumer) = Create();

        await coordinator.StageIntentAsync("sub-valid", "add:1+1", true, 100, true, 2, crit, od);

        var intent = Assert.Single(consumer.Staged);
        Assert.Equal("sub-valid", intent.SubmissionId);
        Assert.Equal(2, intent.EffectiveAttackDamage);
        Assert.Equal(crit, intent.BoundCriticalStrikeRank);
        Assert.Equal(od, intent.BoundOverdriveRank);
    }

    [Fact]
    public async Task StageIntent_MissingPresentationBinding_DefaultsToZeroZero()
    {
        var (coordinator, _, consumer) = Create(Snap(2, 2, 5));
        await coordinator.InitializeAsync();

        var missingBinding = coordinator.GetBoundCombatModifierRanksOrDefault(999);
        Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, missingBinding);

        await coordinator.StageIntentAsync(
            "sub-missing-binding",
            "add:1+1",
            true,
            100,
            true,
            1,
            missingBinding.BoundCriticalStrikeRank,
            missingBinding.BoundOverdriveRank);

        var intent = Assert.Single(consumer.Staged);
        Assert.Equal(0, intent.BoundCriticalStrikeRank);
        Assert.Equal(0, intent.BoundOverdriveRank);
    }

    [Fact]
    public async Task StageIntent_PreCanceledToken_DoesNotStageIntent_EvenWhenStoreIgnoresCancellation()
    {
        var (coordinator, _, consumer) = Create(Snap(1, 2, 5));
        await coordinator.InitializeAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exception = await Record.ExceptionAsync(() =>
            coordinator.StageIntentAsync("sub-canceled", "add:1+1", true, 100, true, 1, 1, 2, cts.Token));

        Assert.Null(exception);
        Assert.Empty(consumer.Staged);
    }

    [Fact]
    public void Home_BindsModifierRanksAtPresentationBeforeAsyncWork_AndStagesBoundRanksAtSubmission()
    {
        string home = File.ReadAllText(RepoPath("src", "MathFirst.App", "Components", "Pages", "Home.razor")).Replace("\r\n", "\n");

        int bindMethod = home.IndexOf("private void BindCurrentQuestionDamage()", StringComparison.Ordinal);
        Assert.True(bindMethod >= 0);
        string bindBody = home[bindMethod..];
        bindBody = bindBody[..bindBody.IndexOf("\n    }\n", StringComparison.Ordinal)];
        int modifierBind = bindBody.IndexOf("GetOrBindCombatModifierRanksAsync(Session.FactInstanceRevision)", StringComparison.Ordinal);
        Assert.True(modifierBind >= 0, "Presentation must bind modifier ranks for the presented question.");
        Assert.True(modifierBind < bindBody.IndexOf("ActivateQuestionBoundFirewallCapacityAsync(", StringComparison.Ordinal));
        Assert.True(modifierBind < bindBody.IndexOf("GetOrBindEffectiveAttackDamageAsync(", StringComparison.Ordinal));

        int submit = home.IndexOf("Session.SubmitAnswer(parsedAnswer)", StringComparison.Ordinal);
        int commit = home.IndexOf("Session.CommitCurrentEvaluationAsync()", submit, StringComparison.Ordinal);
        string submitSection = home[submit..commit];
        Assert.Contains("GetBoundCombatModifierRanksOrDefault(Session.FactInstanceRevision)", submitSection, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrBindCombatModifierRanksAsync", submitSection, StringComparison.Ordinal);
        Assert.Contains("boundCriticalStrikeRank:", submitSection, StringComparison.Ordinal);
        Assert.Contains("boundOverdriveRank:", submitSection, StringComparison.Ordinal);
        Assert.Contains("effectiveAttackDamage: boundDamage", submitSection, StringComparison.Ordinal);

        int stage = submitSection.IndexOf("StageIntentAsync(", StringComparison.Ordinal);
        Assert.True(submitSection.IndexOf("GetBoundCombatModifierRanksOrDefault(", StringComparison.Ordinal) < stage);
        Assert.True(home.IndexOf("ConsumeCommittedAttemptAsync(submissionId)", commit, StringComparison.Ordinal) > commit);
    }

    private static string RepoPath(params string[] segments)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "MathFirst.slnx")))
            {
                return Path.Combine(new[] { current }.Concat(segments).ToArray());
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}

/// <summary>Scriptable in-memory store: no SQLite, no real purchases.</summary>
internal sealed class ModifierFakeGameplayStore : IGameplayStore
{
    private readonly object _handlerLock = new();
    private Action<CyberDefenseCombatModifierSnapshot>? _handlers;
    private int _readCount;

    public CyberDefenseCombatModifierSnapshot Current { get; set; } = null!;
    public TaskCompletionSource<bool>? ReadGate { get; set; }
    public bool ThrowOnRead { get; set; }
    public int ReadCount => Volatile.Read(ref _readCount);
    public int ModifierHandlerCount { get { lock (_handlerLock) { return _handlers?.GetInvocationList().Length ?? 0; } } }

    public event Action<CyberDefenseCombatModifierSnapshot>? CombatModifierSnapshotChanged
    {
        add { lock (_handlerLock) { _handlers += value; } }
        remove { lock (_handlerLock) { _handlers -= value; } }
    }

    public void Raise(CyberDefenseCombatModifierSnapshot snapshot)
    {
        Action<CyberDefenseCombatModifierSnapshot>? handlers;
        lock (_handlerLock) { handlers = _handlers; }
        handlers?.Invoke(snapshot);
    }

    public async Task<CyberDefenseCombatModifierSnapshot> GetCombatModifierSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _readCount);
        var snapshot = Current; // The read is as-of call time, like a delayed store response.
        if (ThrowOnRead)
        {
            throw new InvalidOperationException("Simulated modifier snapshot failure.");
        }

        var gate = ReadGate;
        if (gate != null)
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return snapshot;
    }

    public string StoragePath => "in-memory";
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(CyberDefenseRunState.InitialRun());
    public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current.ResetEpoch);
    public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current.StoreRevision);
    public Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow));
    public Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow).ToAttackUpgradeSnapshot());
    public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged { add { } remove { } }
    public Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow).ToFirewallUpgradeSnapshot());
    public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged { add { } remove { } }
    public Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CyberDefenseCombatModifierSnapshot> PurchaseCriticalStrikeUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CyberDefenseCombatModifierSnapshot> PurchaseOverdriveUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => Task.FromResult<CyberDefenseReceiptRecord?>(null);
    public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => Task.FromResult<CyberDefensePendingIntentRecord?>(null);
    public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CyberDefensePendingIntentRecord>>(Array.Empty<CyberDefensePendingIntentRecord>());
    public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new GameplayResetIntentRecord(false, Current.ResetEpoch, Current.ResetEpoch, null, DateTimeOffset.UtcNow));
    public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Dispose() { }
}

internal sealed class CapturingConsumer : ICyberDefenseSubmissionConsumer
{
    public List<CyberDefensePendingIntentRecord> Staged { get; } = new();

    public Task StageIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default)
    {
        Staged.Add(intent);
        return Task.CompletedTask;
    }

    public Task<CyberDefenseConsumptionResult> ConsumeAttemptAsync(string submissionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<CyberDefenseRecoveryResult> RecoverPendingIntentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CyberDefenseRecoveryResult.Empty());
}

/// <summary>Real SQLite store behavior for Slice 13B: notifications, reset epochs and durable staging.</summary>
[Collection("Combat modifier purchase SQLite isolation")]
public sealed class CyberDefenseQuestionBoundModifierRanksSqliteTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirst_Slice13B_" + Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_directory, "gameplay.db");
    private string LearnerPath => Path.Combine(_directory, "learner.db");

    public CyberDefenseQuestionBoundModifierRanksSqliteTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<SqliteGameplayStore> OpenAsync()
    {
        var store = new SqliteGameplayStore(DatabasePath);
        await store.InitializeAsync();
        return store;
    }

    private async Task ExecuteAsync(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarLongAsync(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private Task SeedOwnedRanksOnlyAsync(int crit, int od) => ExecuteAsync($"UPDATE gameplay_progression SET critical_strike_rank = {crit}, overdrive_rank = {od} WHERE id = 1;");

    private Task SeedPurchasingPowerAsync() => ExecuteAsync(
        "UPDATE gameplay_progression SET player_level = 80, earned_sp = 40, spent_sp = 0, available_sp = 40 WHERE id = 1;");

    // ---------------------------------------------------------------- store notifications

    [Fact]
    public async Task Purchase_PublishesExactCommittedSnapshot_OnlyAfterCommit_AndOutsideTheConnectionGate()
    {
        using var store = await OpenAsync();
        await SeedPurchasingPowerAsync();
        var received = new List<CyberDefenseCombatModifierSnapshot>();
        long committedRankSeenInHandler = -1;
        long? rereadRevisionInHandler = null;
        Exception? handlerFailure = null;
        store.CombatModifierSnapshotChanged += snapshot =>
        {
            received.Add(snapshot);
            try
            {
                committedRankSeenInHandler = ScalarLongAsync("SELECT critical_strike_rank FROM gameplay_progression WHERE id = 1;").GetAwaiter().GetResult();
                // A held gate would make this block; a nested gate acquisition would deadlock.
                var reread = Task.Run(() => store.GetCombatModifierSnapshotAsync()).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                rereadRevisionInHandler = reread.StoreRevision;
            }
            catch (Exception ex)
            {
                handlerFailure = ex;
            }
        };

        var returned = await store.PurchaseCriticalStrikeUpgradeAsync(0, 0);

        Assert.Null(handlerFailure);
        var published = Assert.Single(received);
        Assert.Equal(returned, published);
        Assert.Equal(1, published.CriticalStrikeRank);
        Assert.Equal(1, committedRankSeenInHandler);
        Assert.Equal(returned.StoreRevision, rereadRevisionInHandler);
    }

    [Fact]
    public async Task OverdrivePurchase_PublishesExactCommittedSnapshot()
    {
        using var store = await OpenAsync();
        await SeedPurchasingPowerAsync();
        var received = new List<CyberDefenseCombatModifierSnapshot>();
        store.CombatModifierSnapshotChanged += received.Add;

        var returned = await store.PurchaseOverdriveUpgradeAsync(0, 0);

        Assert.Equal(returned, Assert.Single(received));
        Assert.Equal(1, returned.OverdriveRank);
    }

    [Fact]
    public async Task RejectedPurchase_PublishesNothing()
    {
        using var store = await OpenAsync();
        await SeedPurchasingPowerAsync();
        int calls = 0;
        store.CombatModifierSnapshotChanged += _ => calls++;

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PurchaseCriticalStrikeUpgradeAsync(1, 0)); // wrong expected rank
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PurchaseOverdriveUpgradeAsync(0, 7)); // wrong epoch

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task SubscriberException_DoesNotRollBackPurchase_OrBlockOtherSubscribers()
    {
        using var store = await OpenAsync();
        await SeedPurchasingPowerAsync();
        var received = new List<CyberDefenseCombatModifierSnapshot>();
        store.CombatModifierSnapshotChanged += _ => throw new InvalidOperationException("subscriber failure");
        store.CombatModifierSnapshotChanged += received.Add;
        store.CombatModifierSnapshotChanged += _ => throw new ApplicationException("second failure");

        var returned = await store.PurchaseCriticalStrikeUpgradeAsync(0, 0);

        Assert.Equal(1, returned.CriticalStrikeRank);
        Assert.Equal(returned, Assert.Single(received));
        Assert.Equal(1, await ScalarLongAsync("SELECT critical_strike_rank FROM gameplay_progression WHERE id = 1;"));
        Assert.Equal(1, (await store.GetCombatModifierSnapshotAsync()).CriticalStrikeRank);
        var next = await store.PurchaseOverdriveUpgradeAsync(0, 0);
        Assert.Equal(2, received.Count);
        Assert.Equal(next, received[1]);
    }

    [Fact]
    public async Task ResetNotification_IsPublishedOnlyAfterIntentCompletion_WithCoherentBaseline()
    {
        using var store = await OpenAsync();
        await SeedPurchasingPowerAsync();
        await store.PurchaseCriticalStrikeUpgradeAsync(0, 0);
        var received = new List<CyberDefenseCombatModifierSnapshot>();
        store.CombatModifierSnapshotChanged += received.Add;

        var intent = await store.BeginOrGetResetIntentAsync();
        Assert.True(intent.IsPending);
        Assert.Empty(received);
        await store.ResetGameplayStateAsync(intent.TargetEpoch);
        Assert.Empty(received); // gameplay rows are reset but the intent is still pending/fenced
        await store.ClearResetIntentAsync(intent.TargetEpoch);

        var baseline = Assert.Single(received);
        Assert.Equal(0, baseline.CriticalStrikeRank);
        Assert.Equal(0, baseline.OverdriveRank);
        Assert.Equal(CyberDefenseCombatModifierState.Initial, baseline.ModifierState);
        Assert.Equal(intent.TargetEpoch, baseline.ResetEpoch);
        Assert.Equal(1, baseline.StoreRevision);
        Assert.False(baseline.ResetIntent.IsPending);
        Assert.Equal(await store.GetCombatModifierSnapshotAsync(), baseline);
    }

    // ---------------------------------------------------------------- coordinator + real store

    private async Task<(SqliteGameplayStore Store, SqliteLearnerStore Learner, CyberDefenseCombatCoordinator Coordinator)> OpenCoordinatorAsync()
    {
        var store = await OpenAsync();
        var learner = new SqliteLearnerStore(LearnerPath);
        await learner.InitializeAsync();
        var coordinator = new CyberDefenseCombatCoordinator(store, new CyberDefenseSubmissionConsumer(store, learner));
        await coordinator.InitializeAsync();
        return (store, learner, coordinator);
    }

    [Fact]
    public async Task EndToEnd_PurchasesDuringQuestions_BindPerQuestion_AndStageDurableRanks()
    {
        var (store, learner, coordinator) = await OpenCoordinatorAsync();
        using (store)
        using (learner)
        {
            await SeedPurchasingPowerAsync();
            await coordinator.RefreshStateAsync();
            var q1 = await coordinator.GetOrBindCombatModifierRanksAsync(1);

            await store.PurchaseCriticalStrikeUpgradeAsync(0, 0);
            var q2 = await coordinator.GetOrBindCombatModifierRanksAsync(2);
            await store.PurchaseOverdriveUpgradeAsync(0, 0);
            await store.PurchaseOverdriveUpgradeAsync(1, 0);
            var q3 = await coordinator.GetOrBindCombatModifierRanksAsync(3);

            Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, q1);
            Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(1, 0), q2);
            Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(1, 2), q3);
            Assert.Equal(q1, coordinator.GetBoundCombatModifierRanksOrDefault(1));
            Assert.Equal(q2, coordinator.GetBoundCombatModifierRanksOrDefault(2));

            var bound = coordinator.GetBoundCombatModifierRanksOrDefault(3);
            await coordinator.StageIntentAsync("sub-q3", "add:2+3", true, 800, true, 1, bound.BoundCriticalStrikeRank, bound.BoundOverdriveRank);

            var persisted = await store.GetPendingIntentAsync("sub-q3");
            Assert.NotNull(persisted);
            Assert.Equal(1, persisted.BoundCriticalStrikeRank);
            Assert.Equal(2, persisted.BoundOverdriveRank);
            Assert.Equal(1, persisted.EffectiveAttackDamage); // BASE Attack damage, no modifier composition
            Assert.Equal(1, await ScalarLongAsync("SELECT bound_critical_strike_rank FROM gameplay_pending_intent WHERE submission_id = 'sub-q3';"));
            Assert.Equal(2, await ScalarLongAsync("SELECT bound_overdrive_rank FROM gameplay_pending_intent WHERE submission_id = 'sub-q3';"));
        }
    }

    [Fact]
    public async Task EndToEnd_ResetThroughStore_ClearsBindingsAndRebindsAtBaseline()
    {
        var (store, learner, coordinator) = await OpenCoordinatorAsync();
        using (store)
        using (learner)
        {
            await SeedPurchasingPowerAsync();
            await coordinator.RefreshStateAsync();
            await store.PurchaseCriticalStrikeUpgradeAsync(0, 0);
            Assert.Equal(new CyberDefenseQuestionBoundModifierRanks(1, 0), await coordinator.GetOrBindCombatModifierRanksAsync(5));

            var intent = await store.BeginOrGetResetIntentAsync();
            await store.ResetGameplayStateAsync(intent.TargetEpoch);
            await store.ClearResetIntentAsync(intent.TargetEpoch);

            Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, coordinator.GetBoundCombatModifierRanksOrDefault(5));
            Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(5));
            Assert.Equal(CyberDefenseQuestionBoundModifierRanks.Zero, await coordinator.GetOrBindCombatModifierRanksAsync(6));
        }
    }

    [Fact]
    public async Task DuplicateSubmission_WithConflictingRanks_IsRejectedAndOriginalSurvives()
    {
        var (store, learner, coordinator) = await OpenCoordinatorAsync();
        using (store)
        using (learner)
        {
            await SeedPurchasingPowerAsync();
            await store.PurchaseCriticalStrikeUpgradeAsync(0, 0);
            await store.PurchaseCriticalStrikeUpgradeAsync(1, 0);

            await coordinator.StageIntentAsync("sub-dup", "add:1+1", true, 500, true, 1, 1, 0);
            await coordinator.StageIntentAsync("sub-dup", "add:1+1", true, 500, true, 1, 2, 0); // conflicting retry

            var persisted = await store.GetPendingIntentAsync("sub-dup");
            Assert.NotNull(persisted);
            Assert.Equal(1, persisted.BoundCriticalStrikeRank);
            Assert.Single(await store.GetPendingIntentsAsync(0));
        }
    }

    [Fact]
    public async Task NonzeroRankIntents_RemainDurablyBlocked_WithoutReceiptOrCombatMutation_AndSurviveReopen()
    {
        var (store, learner, coordinator) = await OpenCoordinatorAsync();
        CyberDefenseRunState before;
        using (store)
        using (learner)
        {
            await SeedOwnedRanksOnlyAsync(1, 0);
            before = await store.GetRunStateAsync();

            await coordinator.StageIntentAsync("sub-blocked", "add:1+1", true, 500, true, 1, 1, 0);
            var commit = await learner.CommitSubmissionAsync(LearnerChangeSet("sub-blocked", "add:1+1", true, 500));
            Assert.True(commit.IsSuccess);

            Assert.Null(await coordinator.ConsumeCommittedAttemptAsync("sub-blocked")); // guard rejects, learner untouched
            var guard = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new CyberDefenseSubmissionConsumer(store, learner).ConsumeAttemptAsync("sub-blocked"));
            Assert.Contains("before combat modifier execution is integrated", guard.Message, StringComparison.Ordinal);

            var pending = await store.GetPendingIntentAsync("sub-blocked");
            Assert.NotNull(pending);
            Assert.Equal(1, pending.BoundCriticalStrikeRank);
            Assert.Null(await store.GetReceiptAsync("sub-blocked"));
            var after = await store.GetRunStateAsync();
            Assert.Equal(before.PlayerCurrentHp, after.PlayerCurrentHp);
            Assert.Equal(before.CurrentOpponent.CurrentHp, after.CurrentOpponent.CurrentHp);
            Assert.Equal(before.OpponentIndex, after.OpponentIndex);
            Assert.Equal(0, (await store.GetCombatModifierSnapshotAsync()).ModifierState.CriticalCorrectAttackCount);
        }

        // Startup recovery after a restart neither consumes nor discards the blocked intent.
        var (store2, learner2, coordinator2) = await OpenCoordinatorAsync();
        using (store2)
        using (learner2)
        {
            var pending = await store2.GetPendingIntentAsync("sub-blocked");
            Assert.NotNull(pending);
            Assert.Equal(1, pending.BoundCriticalStrikeRank);
            Assert.Null(await store2.GetReceiptAsync("sub-blocked"));
            Assert.Equal(before.PlayerCurrentHp, (await store2.GetRunStateAsync()).PlayerCurrentHp);
            Assert.NotNull(coordinator2.CurrentViewModel);
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(3, 0)]
    [InlineData(99, 0)]
    [InlineData(0, 99)]
    [InlineData(1, -1)]
    [InlineData(2, 99)]
    [InlineData(-1, 2)]
    public async Task InvalidExplicitRanks_AreRejectedWithoutPendingRowReceiptOrStateMutation_WhileLearnerCommitSucceeds(int crit, int od)
    {
        var (store, learner, coordinator) = await OpenCoordinatorAsync();
        using (store)
        using (learner)
        {
            await SeedOwnedRanksOnlyAsync(2, 2);
            var runBefore = await store.GetRunStateAsync();
            var progBefore = await store.GetProgressionAsync();
            var modBefore = await store.GetCombatModifierSnapshotAsync();
            var revBefore = await store.GetStoreRevisionAsync();

            var stageException = await Record.ExceptionAsync(() =>
                coordinator.StageIntentAsync("sub-invalid", "add:1+1", true, 500, true, 1, crit, od));
            Assert.Null(stageException);

            // Learning-first isolation: learner submission still commits cleanly.
            var commit = await learner.CommitSubmissionAsync(LearnerChangeSet("sub-invalid", "add:1+1", true, 500));
            Assert.True(commit.IsSuccess);
            Assert.NotNull(await learner.GetCommittedAttemptEvidenceAsync("sub-invalid"));

            // No pending SQLite row (neither invalid nor altered 0/0) was inserted.
            Assert.Null(await store.GetPendingIntentAsync("sub-invalid"));
            Assert.Empty(await store.GetPendingIntentsAsync(0));
            Assert.Equal(0, await ScalarLongAsync("SELECT COUNT(*) FROM gameplay_pending_intent;"));

            // Consumption finds no pending intent, creates no receipt, and mutates no combat or progression state.
            var consumeResult = await coordinator.ConsumeCommittedAttemptAsync("sub-invalid");
            Assert.NotNull(consumeResult);
            Assert.Equal(CyberDefenseConsumptionStatus.NoPendingIntent, consumeResult.Status);
            Assert.Null(consumeResult.Receipt);
            Assert.Null(await store.GetReceiptAsync("sub-invalid"));
            Assert.Equal(0, await ScalarLongAsync("SELECT COUNT(*) FROM gameplay_receipt_ledger;"));

            var runAfter = await store.GetRunStateAsync();
            var progAfter = await store.GetProgressionAsync();
            var modAfter = await store.GetCombatModifierSnapshotAsync();
            var revAfter = await store.GetStoreRevisionAsync();

            Assert.Equal(runBefore, runAfter);
            Assert.Equal(progBefore, progAfter);
            Assert.Equal(modBefore, modAfter);
            Assert.Equal(revBefore, revAfter);
        }
    }

    [Fact]
    public async Task PreCanceledStageIntent_InsertsNoPendingSqliteRow_AndCreatesNoReceipt()
    {
        var (store, learner, coordinator) = await OpenCoordinatorAsync();
        using (store)
        using (learner)
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var stageException = await Record.ExceptionAsync(() =>
                coordinator.StageIntentAsync("sub-cancel-sqlite", "add:1+1", true, 500, true, 1, 0, 0, cts.Token));
            Assert.Null(stageException);

            Assert.Null(await store.GetPendingIntentAsync("sub-cancel-sqlite"));
            Assert.Equal(0, await ScalarLongAsync("SELECT COUNT(*) FROM gameplay_pending_intent;"));
            Assert.Null(await store.GetReceiptAsync("sub-cancel-sqlite"));
        }
    }

    [Fact]
    public async Task RankZeroIntents_StillConsumeNormally_EvenWhenModifiersAreOwned()
    {
        var (store, learner, coordinator) = await OpenCoordinatorAsync();
        using (store)
        using (learner)
        {
            await SeedOwnedRanksOnlyAsync(1, 1);
            // Question was presented before the purchase: legacy rank-zero call shape.
            await coordinator.StageIntentAsync("sub-zero", "add:1+1", true, 500, true);
            Assert.True((await learner.CommitSubmissionAsync(LearnerChangeSet("sub-zero", "add:1+1", true, 500))).IsSuccess);

            var result = await coordinator.ConsumeCommittedAttemptAsync("sub-zero");

            Assert.NotNull(result);
            Assert.Equal(CyberDefenseConsumptionStatus.Success, result.Status);
            Assert.NotNull(await store.GetReceiptAsync("sub-zero"));
            Assert.Null(await store.GetPendingIntentAsync("sub-zero"));
        }
    }

    private static SubmissionChangeSet LearnerChangeSet(string submissionId, string factId, bool isCorrect, long latencyMs)
    {
        var parts = factId["add:".Length..].Split('+');
        int left = int.Parse(parts[0]);
        int right = int.Parse(parts[1]);
        int answer = left + right;
        var attempt = new AttemptRecord(
            submissionId: submissionId,
            factId: factId,
            operation: ArithmeticOperation.Addition,
            leftOperand: left,
            rightOperand: right,
            submittedAnswer: isCorrect ? answer : answer + 1,
            correctAnswer: answer,
            isCorrect: isCorrect,
            isFluent: isCorrect,
            responseLatencyMs: latencyMs,
            timestamp: DateTimeOffset.UtcNow,
            outcome: isCorrect ? AttemptOutcome.Correct : AttemptOutcome.Incorrect,
            practicePosition: 1);
        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;
        progression.CurriculumStage = CurriculumStage.Stage1_Addition;
        var itemState = new ItemLearningState
        {
            FactId = factId,
            Operation = ArithmeticOperation.Addition,
            LeftOperand = left,
            RightOperand = right,
            TotalAttempts = 1,
            CorrectAttempts = isCorrect ? 1 : 0,
            IncorrectAttempts = isCorrect ? 0 : 1,
            ConsecutiveCorrectStreak = isCorrect ? 1 : 0,
            LastLatencyMs = latencyMs,
            RollingLatencyMs = latencyMs,
            FluentStreak = isCorrect ? 1 : 0,
            LastPracticedOrder = 1,
            LastPracticedAt = DateTimeOffset.UtcNow
        };
        return new SubmissionChangeSet(
            submissionId: submissionId,
            ExpectedRevision: 1,
            attempt: attempt,
            updatedItemState: itemState,
            updatedProgression: progression,
            updatedFsrsState: null,
            operationProgressions: progression.OperationProgressions);
    }
}
