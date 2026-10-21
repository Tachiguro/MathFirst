namespace MathFirst.Application.Gameplay;

using MathFirst.Application.Persistence;

public enum CyberDefensePresentationStage { None, Victory, Recap, SystemOverload }

/// <summary>Transient presentation only. The caller keeps learner progression behind this barrier.</summary>
public sealed class CyberDefensePresentationSequence
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly HashSet<string> _presented = new(StringComparer.Ordinal);
    private CancellationTokenSource? _active;
    private long _epoch = -1;
    private long _generation;
    public CyberDefensePresentationStage Stage { get; private set; }
    public CyberDefenseSectorRecap? Recap { get; private set; }
    public int CompletedSector { get; private set; }
    public bool IsBlocking => Stage != CyberDefensePresentationStage.None;
    public event Action? Changed;
    public CyberDefensePresentationSequence(Func<TimeSpan, CancellationToken, Task>? delay = null)
        => _delay = delay ?? ((duration, ct) => Task.Delay(duration, ct));

    public async Task RunAsync(CyberDefenseReceiptRecord receipt, CyberDefenseSectorRecap? recap,
        Func<CancellationToken, Task<bool>> isCurrent, CancellationToken cancellationToken = default)
    {
        var transition = receipt.TransitionResult;
        if (receipt.ReceiptKind != CyberDefenseReceiptKind.Applied || transition is null ||
            (!transition.IsSectorCompleted && !transition.IsGameOver) || receipt.ResetEpoch < _epoch) return;
        if (receipt.ResetEpoch > _epoch)
        {
            Cancel();
            _epoch = receipt.ResetEpoch;
            _presented.Clear();
        }
        if (!_presented.Add(receipt.SubmissionId)) return;
        Cancel();
        var generation = _generation;
        using var active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _active = active;
        var ct = active.Token;
        try
        {
            if (!await isCurrent(ct) || ct.IsCancellationRequested || generation != _generation) return;
            CompletedSector = transition.IsSectorCompleted ? transition.NextState.Sector - 1 : transition.TerminalSnapshot!.Sector;
            Recap = recap?.CompletionSubmissionId == receipt.SubmissionId && recap.ResetEpoch == receipt.ResetEpoch &&
                recap.Sector == CompletedSector ? recap : null;
            SetStage(transition.IsGameOver ? CyberDefensePresentationStage.SystemOverload : CyberDefensePresentationStage.Victory);
            await _delay(TimeSpan.FromSeconds(2), ct);
            if (!await isCurrent(ct) || ct.IsCancellationRequested || generation != _generation) return;
            if (transition.IsSectorCompleted)
            {
                SetStage(CyberDefensePresentationStage.Recap);
                await _delay(TimeSpan.FromSeconds(2), ct);
                await isCurrent(ct); // Observe epoch/context invalidation before the caller releases its barrier.
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            if (generation == _generation)
            {
                _active = null;
                Recap = null;
                SetStage(CyberDefensePresentationStage.None);
            }
        }
    }

    public void Cancel()
    {
        _generation++;
        var active = _active;
        _active = null;
        active?.Cancel();
        Recap = null;
        SetStage(CyberDefensePresentationStage.None);
    }

    private void SetStage(CyberDefensePresentationStage stage)
    {
        if (Stage == stage) return;
        Stage = stage;
        Changed?.Invoke();
    }
}
