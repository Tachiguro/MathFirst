namespace MathFirst.Application.Copy;

/// <summary>
/// Component-owned presentation state for one visible practice-gate activation.
/// Selection occurs only in <see cref="Activate"/>; reading <see cref="Current"/>
/// during rendering has no side effects.
/// </summary>
public sealed class PracticeGateCopyState
{
    private readonly PracticeCopySelector _selector;

    public PracticeGateCopyState(PracticeCopySelector selector)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
    }

    public PracticeCopyResult? Current { get; private set; }

    public void Activate(PracticeCopyContext context, string localizedFallback)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(localizedFallback);

        Current = _selector.Select(context) ?? new PracticeCopyResult(
            $"Fallback.{context.Trigger}",
            localizedFallback,
            context.Trigger,
            IsFallback: true);
    }

    public void Relocalize(string locale, string localizedFallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localizedFallback);
        if (Current is null)
        {
            return;
        }

        Current = _selector.Relocalize(
                Current.MessageId,
                Current.Trigger,
                locale,
                Current.IsFallback)
            ?? Current with { LocalizedText = localizedFallback, IsFallback = true };
    }

    public void Clear() => Current = null;
}
