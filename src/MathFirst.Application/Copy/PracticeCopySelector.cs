namespace MathFirst.Application.Copy;

using System.Text;

/// <summary>
/// Deterministic contextual copy selector for MF-UX-002.
///
/// Algorithm:
/// 1. Obtains the ordered pool of message IDs for the context trigger from <see cref="IPracticeCopyLibrary"/>.
/// 2. Filters out IDs in the in-memory per-trigger recency exclusion set.
/// 3. Computes a stable uint hash from (Trigger, PracticePosition % 97, Locale).
/// 4. Selects pool[hash % pool.Count].
/// 5. Records the selected ID in the recency set (capped at min(pool.Count - 1, 5)).
///
/// Fallback chain (when pool is empty or no text found):
///   trigger-specific neutral pool → NeutralReady/NeutralPaused pool → static string constant
///
/// Invariants:
/// - Does NOT mutate any TrainingSession state.
/// - Does NOT perform I/O.
/// - Is NOT thread-safe (intended as a scoped/singleton per UI session on the UI thread).
/// </summary>
public sealed class PracticeCopySelector
{
    private readonly IPracticeCopyLibrary _library;

    /// <summary>Per-trigger in-memory recency exclusion sets. Resets when the selector is re-created (app restart).</summary>
    private readonly Dictionary<PracticeCopyTrigger, HashSet<string>> _recentlyShown = new();

    /// <summary>Maximum recency window per trigger (capped to pool size − 1 during selection).</summary>
    private const int MaxRecencyWindow = 5;

    /// <summary>
    /// Prime modulus applied to PracticePosition before hashing.
    /// Large enough to create variety; prime to minimize collision clustering.
    /// </summary>
    private const long PositionModulus = 97L;

    /// <summary>Static fallback texts by locale for when the library has no matching entry.</summary>
    private static readonly Dictionary<string, (string Ready, string Paused)> StaticFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = ("Ready to practice?", "Paused"),
        ["de"] = ("Bereit zum Üben?", "Pausiert"),
        ["ru"] = ("Готовы заниматься?", "Пауза"),
    };

    public PracticeCopySelector(IPracticeCopyLibrary library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
    }

    /// <summary>
    /// Selects a contextual copy message for the given context.
    /// Always returns a non-null result with non-empty <see cref="PracticeCopyResult.LocalizedText"/>.
    /// </summary>
    public PracticeCopyResult Select(PracticeCopyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var trigger = context.Trigger;
        var locale = context.Locale ?? "en";

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

        // 3. Static string fallback — always succeeds.
        return StaticFallback(trigger, locale);
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

        var recent = GetOrCreateRecentSet(trigger);

        // Build the eligible pool by excluding recently shown IDs (if window > 0).
        IReadOnlyList<string> eligibleIds;
        if (recencyWindow == 0 || recent.Count == 0)
        {
            eligibleIds = allIds;
        }
        else
        {
            var filtered = allIds.Where(id => !recent.Contains(id)).ToList();
            eligibleIds = filtered.Count > 0 ? filtered : allIds; // safety: never block entirely
        }

        // Deterministic hash selection.
        var hash = ComputeHash(trigger, practicePosition, locale);
        var selected = eligibleIds[(int)(hash % (uint)eligibleIds.Count)];

        // Resolve localized text.
        var text = _library.GetText(selected, locale);
        if (string.IsNullOrEmpty(text))
        {
            // ID exists but text missing — try English fallback.
            text = _library.GetText(selected, "en");
        }

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        // Record in recency set (trim to window size).
        if (recencyWindow > 0)
        {
            recent.Add(selected);
            while (recent.Count > recencyWindow)
            {
                recent.Remove(recent.First());
            }
        }

        return new PracticeCopyResult(selected, text, trigger, IsFallback: false);
    }

    private static PracticeCopyResult StaticFallback(PracticeCopyTrigger trigger, string locale)
    {
        var lang = locale.Length >= 2 ? locale[..2].ToLowerInvariant() : "en";
        if (!StaticFallbacks.TryGetValue(lang, out var pair))
        {
            pair = StaticFallbacks["en"];
        }

        var text = IsReadyGate(trigger) ? pair.Ready : pair.Paused;
        return new PracticeCopyResult(
            MessageId: $"Fallback.{trigger}",
            LocalizedText: text,
            Trigger: trigger,
            IsFallback: true);
    }

    private static bool IsReadyGate(PracticeCopyTrigger trigger) =>
        trigger is PracticeCopyTrigger.InitialReady
            or PracticeCopyTrigger.FirstEverReady
            or PracticeCopyTrigger.ReturnShortAbsence
            or PracticeCopyTrigger.ReturnLongAbsence
            or PracticeCopyTrigger.NeutralReady;

    private HashSet<string> GetOrCreateRecentSet(PracticeCopyTrigger trigger)
    {
        if (!_recentlyShown.TryGetValue(trigger, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            _recentlyShown[trigger] = set;
        }
        return set;
    }

    /// <summary>
    /// Computes a deterministic uint hash from the selection inputs.
    /// Uses FNV-1a (32-bit) for stability, simplicity, and absence of external dependencies.
    /// </summary>
    private static uint ComputeHash(PracticeCopyTrigger trigger, long practicePosition, string locale)
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

        // Locale (first 2 chars)
        var localeNorm = (locale.Length >= 2 ? locale[..2] : locale).ToLowerInvariant();
        var localeBytes = Encoding.UTF8.GetBytes(localeNorm);
        foreach (var b in localeBytes)
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return hash;
    }
}
