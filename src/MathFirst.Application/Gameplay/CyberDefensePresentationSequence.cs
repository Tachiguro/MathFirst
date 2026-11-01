namespace MathFirst.Application.Gameplay;

using MathFirst.Application.Persistence;

/// <summary>
/// <see cref="Recap"/> is the single boss result card of a sector completion (Issue #138): the victory heading and
/// flourish, the sector recap and Continue are presented together from the start.
/// </summary>
public enum CyberDefensePresentationStage { None, Recap, SystemOverload }

/// <summary>
/// Transient presentation only. The caller keeps learner progression behind this barrier.
/// Timers only reveal the System Restart's Continue and re-observe context validity; only <see cref="Acknowledge"/>
/// can complete a presentation.
/// </summary>
public sealed class CyberDefensePresentationSequence
{
    /// <summary>System Restart only; the boss result card offers Continue from its first presentation.</summary>
    public static readonly TimeSpan ContinueRevealDelay = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan RevalidationInterval = TimeSpan.FromSeconds(2);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly HashSet<string> _acknowledged = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private CancellationTokenSource? _active;
    private TaskCompletionSource? _release;
    private long _epoch = -1;
    private long _generation;
    private bool _canContinue;
    public CyberDefensePresentationStage Stage { get; private set; }
    public CyberDefenseSectorRecap? Recap { get; private set; }
    public int CompletedSector { get; private set; }
    public bool IsBlocking => Stage != CyberDefensePresentationStage.None;

    /// <summary>True only while the current presentation shows its Continue control and has not been acknowledged.</summary>
    public bool CanContinue { get { lock (_gate) return _canContinue; } }

    /// <summary>Identity of the presentation that is currently blocking (0 when none). Never reused by a later presentation.</summary>
    public long PresentationId { get { lock (_gate) return IsBlocking ? _generation : 0; } }
    public event Action? Changed;
    public CyberDefensePresentationSequence(Func<TimeSpan, CancellationToken, Task>? delay = null)
        => _delay = delay ?? ((duration, ct) => Task.Delay(duration, ct));

    /// <summary>True for the receipts that end in a sector recap or System Restart, which only the user may dismiss.</summary>
    public static bool RequiresPresentation(CyberDefenseReceiptRecord receipt)
        => receipt.ReceiptKind == CyberDefenseReceiptKind.Applied &&
           receipt.TransitionResult is { } transition && (transition.IsSectorCompleted || transition.IsGameOver);

    /// <summary>
    /// True only when the user already acknowledged this exact receipt in the current reset epoch.
    /// Cancelled, failed or invalidated presentations are never recorded, so they cannot satisfy the barrier.
    /// </summary>
    public bool HasAcknowledged(CyberDefenseReceiptRecord receipt)
    {
        lock (_gate) return receipt.ResetEpoch == _epoch && _acknowledged.Contains(receipt.SubmissionId);
    }

    /// <summary>
    /// Presents a sector victory or System Restart until the user acknowledges it.
    /// Returns true only when the user acknowledged the current, still valid presentation; every other exit
    /// (cancellation, invalidation, failure, already acknowledged or non-presentable receipt) releases the caller
    /// without acknowledgement. A presentation that was not acknowledged may be presented again.
    /// </summary>
    public async Task<bool> RunAsync(CyberDefenseReceiptRecord receipt, CyberDefenseSectorRecap? recap,
        Func<CancellationToken, Task<bool>> isCurrent, CancellationToken cancellationToken = default)
    {
        var transition = receipt.TransitionResult;
        if (!RequiresPresentation(receipt) || transition is null || receipt.ResetEpoch < _epoch) return false;
        if (receipt.ResetEpoch > _epoch)
        {
            Cancel();
            lock (_gate)
            {
                _epoch = receipt.ResetEpoch;
                _acknowledged.Clear();
            }
        }
        if (HasAcknowledged(receipt)) return false;
        Cancel();
        long generation;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            generation = _generation;
            _release = release;
        }
        using var active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _active = active;
        var ct = active.Token;
        async Task<bool> StillCurrent() => await isCurrent(ct) && !ct.IsCancellationRequested && generation == _generation;
        var acknowledged = false;
        try
        {
            if (!await StillCurrent()) return false;
            CompletedSector = transition.IsSectorCompleted ? transition.NextState.Sector - 1 : transition.TerminalSnapshot!.Sector;
            Recap = recap?.CompletionSubmissionId == receipt.SubmissionId && recap.ResetEpoch == receipt.ResetEpoch &&
                recap.Sector == CompletedSector ? recap : null;
            if (transition.IsSectorCompleted)
            {
                // One card, one stage: Continue is available in the same change that first shows the card.
                PresentWithContinue(generation, CyberDefensePresentationStage.Recap);
            }
            else
            {
                SetStage(CyberDefensePresentationStage.SystemOverload);
                await _delay(ContinueRevealDelay, ct);
                if (!await StillCurrent()) return false;
                RevealContinue(generation);
            }

            // A timer may only re-observe validity here; the wait ends solely on the user's acknowledgement.
            while (!release.Task.IsCompleted)
            {
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var wait = _delay(RevalidationInterval, tick.Token);
                var finished = await Task.WhenAny(release.Task, wait);
                tick.Cancel();
                _ = wait.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                if (ct.IsCancellationRequested || generation != _generation) return false;
                if (finished == wait && !release.Task.IsCompleted)
                {
                    await wait;
                    if (!await StillCurrent()) return false;
                }
            }

            // Observe epoch/context invalidation before the caller releases its barrier.
            acknowledged = await StillCurrent();
            if (acknowledged)
            {
                lock (_gate) _acknowledged.Add(receipt.SubmissionId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _active = null;
                    _release = null;
                    _canContinue = false;
                }
            }
            if (generation == _generation)
            {
                Recap = null;
                SetStage(CyberDefensePresentationStage.None);
            }
        }
        return acknowledged;
    }

    /// <summary>
    /// Accepts the user's Continue for exactly the presentation identified by <paramref name="presentationId"/>.
    /// Returns false when Continue is not revealed, the identity is stale, or it was already accepted.
    /// </summary>
    public bool Acknowledge(long presentationId)
    {
        TaskCompletionSource release;
        lock (_gate)
        {
            if (!_canContinue || _release is null || !IsBlocking || presentationId != _generation) return false;
            _canContinue = false;
            release = _release;
        }
        release.TrySetResult();
        Changed?.Invoke();
        return true;
    }

    public void Cancel()
    {
        CancellationTokenSource? active;
        lock (_gate)
        {
            _generation++;
            active = _active;
            _active = null;
            _release = null;
            _canContinue = false;
        }
        active?.Cancel();
        Recap = null;
        SetStage(CyberDefensePresentationStage.None);
    }

    private void RevealContinue(long generation)
    {
        lock (_gate)
        {
            if (generation != _generation || _release is null) return;
            _canContinue = true;
        }
        Changed?.Invoke();
    }

    private void PresentWithContinue(long generation, CyberDefensePresentationStage stage)
    {
        lock (_gate)
        {
            if (generation != _generation || _release is null) return;
            _canContinue = true;
            Stage = stage;
        }
        Changed?.Invoke();
    }

    private void SetStage(CyberDefensePresentationStage stage)
    {
        if (Stage == stage) return;
        Stage = stage;
        Changed?.Invoke();
    }
}
