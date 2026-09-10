namespace MathFirst.Application.Copy;

using System.Text;
using MathFirst.Domain;

/// <summary>
/// Deterministic contextual copy selector for MF-UX-002.
///
/// Algorithm:
/// 1. Obtains the ordered pool of message IDs for the context trigger from <see cref="IPracticeCopyLibrary"/>.
/// 2. Filters out IDs in the in-memory per-trigger recency exclusion set.
/// 3. Computes a stable uint hash from (Trigger, PracticePosition % 97).
/// 4. Selects pool[hash % pool.Count].
/// 5. Records the selected ID in an ordered recency window (capped at min(pool.Count - 1, 5)).
///
/// Fallback chain (when pool is empty or no text found):
///   trigger-specific neutral pool → NeutralReady/NeutralPaused pool → no contextual result
///
/// Invariants:
/// - Does NOT mutate any TrainingSession state.
/// - Does NOT perform I/O.
/// - Is NOT thread-safe. Its singleton registration intentionally makes recency application-scoped,
///   and the MAUI/Blazor UI invokes it on the UI thread.
/// </summary>
public sealed class PracticeCopySelector
{
    private readonly IPracticeCopyLibrary _library;

    /// <summary>Per-trigger ordered recency windows. Resets when the application-scoped selector is re-created.</summary>
    private readonly Dictionary<PracticeCopyTrigger, RecencyWindow> _recentlyShown = new();

    /// <summary>Maximum recency window per trigger (capped to pool size − 1 during selection).</summary>
    private const int MaxRecencyWindow = 5;

    /// <summary>
    /// Prime modulus applied to PracticePosition before hashing.
    /// Large enough to create variety; prime to minimize collision clustering.
    /// </summary>
    private const long PositionModulus = 97L;

    public PracticeCopySelector(IPracticeCopyLibrary library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
    }

    /// <summary>
    /// Selects a contextual copy message for the given context.
    /// Returns <c>null</c> when neither the contextual nor neutral corpus can resolve a message.
    /// The presentation owner then uses the application's established localization fallback key.
    /// </summary>
    public PracticeCopyResult? Select(PracticeCopyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var trigger = context.Trigger;
        var locale = LanguagePreferencePolicy.Normalize(context.Locale);

        // 1. Attempt selection from the trigger-specific pool.
        var result = TrySelectFromPool(trigger, context.PracticePosition, locale);
        if (result is not null)
        {
            return result;
        }

        // 2. Try the neutral fallback pool for the gate class.
        var neutralTrigger = IsReadyGate(trigger)
            ? PracticeCopyTrigger.NeutralReady
            : PracticeCopyTrigger.NeutralPaused;

        if (neutralTrigger != trigger)
        {
            result = TrySelectFromPool(neutralTrigger, context.PracticePosition, locale);
            if (result is not null)
            {
                return new PracticeCopyResult(result.MessageId, result.LocalizedText, trigger, IsFallback: true);
            }
        }

        return null;
    }

    /// <summary>
    /// Re-localizes a stable message ID without selecting or mutating recency state.
    /// </summary>
    public PracticeCopyResult? Relocalize(
        string messageId,
        PracticeCopyTrigger trigger,
        string locale,
        bool isFallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        var text = ResolveText(messageId, LanguagePreferencePolicy.Normalize(locale));
        return text is null
            ? null
            : new PracticeCopyResult(messageId, text, trigger, isFallback);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private PracticeCopyResult? TrySelectFromPool(
        PracticeCopyTrigger trigger,
        long practicePosition,
        string locale)
    {
        var allIds = _library.GetMessageIds(trigger, locale);
        if (allIds.Count == 0)
        {
            return null;
        }

        // Effective recency window: min(pool.Count - 1, MaxRecencyWindow).
        // If pool has 1 item, the window is 0 → nothing is excluded.
        var recencyWindow = Math.Min(allIds.Count - 1, MaxRecencyWindow);

        var recent = GetOrCreateRecentWindow(trigger);

        // Build the eligible pool by excluding recently shown IDs (if window > 0).
        IReadOnlyList<string> eligibleIds;
        if (recencyWindow == 0 || recent.Count == 0)
        {
            eligibleIds = allIds;
        }
        else
        {
            var filtered = allIds.Where(id => !recent.Membership.Contains(id)).ToList();
            eligibleIds = filtered.Count > 0 ? filtered : allIds; // safety: never block entirely
        }

        // Deterministic hash selection.
        var hash = ComputeHash(trigger, practicePosition);
        var selected = eligibleIds[(int)(hash % (uint)eligibleIds.Count)];

        // Resolve localized text.
        var text = ResolveText(selected, locale);

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        // Record in recency set (trim to window size).
        if (recencyWindow > 0)
        {
            recent.Order.Enqueue(selected);
            recent.Membership.Add(selected);
            while (recent.Order.Count > recencyWindow)
            {
                recent.Membership.Remove(recent.Order.Dequeue());
            }
        }

        return new PracticeCopyResult(selected, text, trigger, IsFallback: false);
    }

    private string? ResolveText(string messageId, string locale)
    {
        var text = _library.GetText(messageId, locale);
        if (string.IsNullOrEmpty(text) && locale != LanguagePreferencePolicy.EnglishLanguageCode)
        {
            text = _library.GetText(messageId, LanguagePreferencePolicy.EnglishLanguageCode);
        }
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static bool IsReadyGate(PracticeCopyTrigger trigger) =>
        trigger is PracticeCopyTrigger.InitialReady
            or PracticeCopyTrigger.ReturnShortAbsence
            or PracticeCopyTrigger.ReturnLongAbsence
            or PracticeCopyTrigger.NeutralReady;

    private RecencyWindow GetOrCreateRecentWindow(PracticeCopyTrigger trigger)
    {
        if (!_recentlyShown.TryGetValue(trigger, out var window))
        {
            window = new RecencyWindow();
            _recentlyShown[trigger] = window;
        }
        return window;
    }

    /// <summary>
    /// Computes a deterministic uint hash from the selection inputs.
    /// Uses FNV-1a (32-bit) for stability, simplicity, and absence of external dependencies.
    /// </summary>
    private static uint ComputeHash(PracticeCopyTrigger trigger, long practicePosition)
    {
        // FNV-1a constants
        const uint FnvPrime = 16777619u;
        const uint FnvOffset = 2166136261u;

        uint hash = FnvOffset;

        // Trigger name bytes
        var triggerBytes = Encoding.UTF8.GetBytes(trigger.ToString());
        foreach (var b in triggerBytes)
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        // Practice position modulus (prime modulus reduces cyclical patterns)
        var positionValue = (uint)(practicePosition % PositionModulus);
        hash ^= positionValue;
        hash *= FnvPrime;

        return hash;
    }

    private sealed class RecencyWindow
    {
        public Queue<string> Order { get; } = new();
        public HashSet<string> Membership { get; } = new(StringComparer.Ordinal);
        public int Count => Order.Count;
    }
}
