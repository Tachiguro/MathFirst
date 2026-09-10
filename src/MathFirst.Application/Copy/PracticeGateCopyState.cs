namespace MathFirst.Application.Copy;

public readonly record struct PracticeGatePresentationIdentity(
    long LearnerStateGenerationRevision,
    long PracticeGateActivationRevision);

/// <summary>
/// Application-session presentation state for practice-gate activations.
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
    public PracticeGatePresentationIdentity? CurrentIdentity { get; private set; }

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

    /// <summary>
    /// Selects once for a session-owned learner-generation/gate-activation identity;
    /// later consumers of the same identity only re-localize the established
    /// language-independent message ID.
    /// </summary>
    public void Synchronize(
        PracticeGatePresentationIdentity identity,
        PracticeCopyContext context,
        string localizedFallback)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(localizedFallback);

        if (CurrentIdentity == identity && Current is not null)
        {
            Relocalize(context.Locale, localizedFallback);
            return;
        }

        Activate(context, localizedFallback);
        CurrentIdentity = identity;
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

    public void Clear()
    {
        Current = null;
        CurrentIdentity = null;
    }
}
