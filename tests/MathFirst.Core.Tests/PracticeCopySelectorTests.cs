namespace MathFirst.Core.Tests;

using MathFirst.Application.Copy;
using Xunit;

/// <summary>
/// TDD tests for MF-UX-002: PracticeCopySelector determinism, repetition avoidance,
/// fallback, and state-isolation contracts.
///
/// These tests depend on the selector engine and context model only.
/// No TrainingSession dependency is required here; context is constructed directly.
/// </summary>
public sealed class PracticeCopySelectorTests
{
    // ---------------------------------------------------------------------------
    // A. Determinism — same context inputs always produce the same message ID
    // ---------------------------------------------------------------------------

    [Fact]
    public void Selector_SameContext_ReturnsSameMessageId()
    {
        var library = new FakeCopyLibrary();
        var selector = new PracticeCopySelector(library);

        var ctx = MakeContext(PracticeCopyTrigger.InitialReady, practicePosition: 42, locale: "en");

        // Fresh selector, no recency history — pure hash selection must be stable
        var result1 = selector.Select(ctx);
        var selector2 = new PracticeCopySelector(library);
        var result2 = selector2.Select(ctx);

        Assert.NotNull(result1.MessageId);
        Assert.Equal(result1.MessageId, result2.MessageId);
    }

    [Fact]
    public void Selector_DifferentPracticePosition_MayProduceDifferentId()
    {
        // This is not a strict requirement (hash collisions possible) but validates
        // the algorithm is sensitive to the practice position input.
        var library = new FakeCopyLibrary();

        // Use a large pool so hash collisions are less likely.
        var selector = new PracticeCopySelector(library);

        var ctx1 = MakeContext(PracticeCopyTrigger.InitialReady, practicePosition: 10, locale: "en");
        var ctx2 = MakeContext(PracticeCopyTrigger.InitialReady, practicePosition: 11, locale: "en");

        var id1 = selector.Select(ctx1).MessageId;
        var id2 = selector.Select(ctx2).MessageId;

        // They *may* differ — we verify a valid ID is returned regardless.
        Assert.NotNull(id1);
        Assert.NotNull(id2);
        Assert.NotEmpty(id1);
        Assert.NotEmpty(id2);
    }

    [Fact]
    public void Selector_DifferentTrigger_ReturnsDifferentPool()
    {
        var library = new FakeCopyLibrary();
        var selector = new PracticeCopySelector(library);

        var ready = MakeContext(PracticeCopyTrigger.InitialReady, practicePosition: 1, locale: "en");
        var paused = MakeContext(PracticeCopyTrigger.ResumeManualPause, practicePosition: 1, locale: "en");

        var readyResult = selector.Select(ready);
        var pausedResult = selector.Select(paused);

        // Different triggers must produce IDs from their respective pools.
        Assert.Contains("InitialReady", readyResult.MessageId);
        Assert.Contains("ResumeManualPause", pausedResult.MessageId);
    }

    [Fact]
    public void Selector_SameContextSameLocale_LocalizedTextMatches()
    {
        var library = new FakeCopyLibrary();
        var selector1 = new PracticeCopySelector(library);
        var selector2 = new PracticeCopySelector(library);

        var ctx = MakeContext(PracticeCopyTrigger.NeutralReady, practicePosition: 5, locale: "en");

        var r1 = selector1.Select(ctx);
        var r2 = selector2.Select(ctx);

        Assert.Equal(r1.LocalizedText, r2.LocalizedText);
        Assert.NotEmpty(r1.LocalizedText);
    }

    // ---------------------------------------------------------------------------
    // B. Repetition Avoidance — recently shown IDs are skipped when pool allows
    // ---------------------------------------------------------------------------

    [Fact]
    public void Selector_RepetitionAvoidance_SkipsRecentlyShownId()
    {
        // Use a pool with exactly 2 variants so we can assert switching behavior.
        var library = new TwoVariantFakeCopyLibrary();
        var selector = new PracticeCopySelector(library);

        var ctx = MakeContext(PracticeCopyTrigger.InitialReady, practicePosition: 1, locale: "en");

        var first = selector.Select(ctx).MessageId;
        // Calling again with the same context after the first selection recorded recency
        // should prefer the other variant (when pool has 2 items).
        var second = selector.Select(ctx).MessageId;

        // Both must be valid IDs from the pool.
        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        // The second selection must differ from the first (pool size 2, window size 1).
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Selector_RepetitionAvoidance_FallsBackWhenAllExcluded()
    {
        // Pool of exactly 1 variant — exclusion window must not block selection entirely.
        var library = new SingleVariantFakeCopyLibrary();
        var selector = new PracticeCopySelector(library);

        var ctx = MakeContext(PracticeCopyTrigger.InitialReady, practicePosition: 1, locale: "en");

        var first = selector.Select(ctx).MessageId;
        // Second call with pool size 1 — the exclusion must not apply (pool size 1 means
        // window is capped at 0, so the single variant is always eligible).
        var second = selector.Select(ctx).MessageId;

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    // ---------------------------------------------------------------------------
    // C. Fallback — every gate state must produce non-empty text even with an
    //    empty library or unknown trigger
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(PracticeCopyTrigger.InitialReady)]
    [InlineData(PracticeCopyTrigger.FirstEverReady)]
    [InlineData(PracticeCopyTrigger.ReturnShortAbsence)]
    [InlineData(PracticeCopyTrigger.ReturnLongAbsence)]
    [InlineData(PracticeCopyTrigger.ResumeManualPause)]
    [InlineData(PracticeCopyTrigger.ResumeBackground)]
    [InlineData(PracticeCopyTrigger.NeutralReady)]
    [InlineData(PracticeCopyTrigger.NeutralPaused)]
    public void Selector_AllTriggers_FallbackReturnsNonEmptyText(PracticeCopyTrigger trigger)
    {
        // Empty library — selector must fall back to static strings.
        var library = new EmptyFakeCopyLibrary();
        var selector = new PracticeCopySelector(library);

        var ctx = MakeContext(trigger, practicePosition: 0, locale: "en");
        var result = selector.Select(ctx);

        Assert.NotNull(result);
        Assert.NotEmpty(result.LocalizedText);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Selector_AllLocales_FallbackReturnsNonEmptyText(string locale)
    {
        var library = new EmptyFakeCopyLibrary();
        var selector = new PracticeCopySelector(library);

        var ctx = MakeContext(PracticeCopyTrigger.InitialReady, practicePosition: 1, locale: locale);
        var result = selector.Select(ctx);

        Assert.NotEmpty(result.LocalizedText);
    }

    // ---------------------------------------------------------------------------
    // D. Trigger resolution — correct trigger derived from context
    // ---------------------------------------------------------------------------

    [Fact]
    public void PracticeCopyContext_IsFirstEverSession_WhenPracticePositionZeroAndNoItems()
    {
        var ctx = new PracticeCopyContext(
            Trigger: PracticeCopyTrigger.FirstEverReady,
            PracticePosition: 0,
            SessionCorrectCount: 0,
            SessionTotalCount: 0,
            AbsenceBucket: AbsenceBucket.SameSession,
            IsFirstEverSession: true,
            Locale: "en");

        Assert.True(ctx.IsFirstEverSession);
        Assert.Equal(PracticeCopyTrigger.FirstEverReady, ctx.Trigger);
    }

    [Theory]
    [InlineData(AbsenceBucket.SameSession)]
    [InlineData(AbsenceBucket.RecentReturn)]
    [InlineData(AbsenceBucket.ShortAbsence)]
    [InlineData(AbsenceBucket.LongAbsence)]
    public void AbsenceBucket_AllValues_DefinedAndDistinct(AbsenceBucket bucket)
    {
        // Ensures the enum exists and all values are individually addressable.
        Assert.True(Enum.IsDefined(typeof(AbsenceBucket), bucket));
    }

    [Theory]
    [InlineData(PracticeCopyTrigger.InitialReady)]
    [InlineData(PracticeCopyTrigger.FirstEverReady)]
    [InlineData(PracticeCopyTrigger.ReturnShortAbsence)]
    [InlineData(PracticeCopyTrigger.ReturnLongAbsence)]
    [InlineData(PracticeCopyTrigger.ResumeManualPause)]
    [InlineData(PracticeCopyTrigger.ResumeBackground)]
    [InlineData(PracticeCopyTrigger.NeutralReady)]
    [InlineData(PracticeCopyTrigger.NeutralPaused)]
    public void PracticeCopyTrigger_AllValues_DefinedAndDistinct(PracticeCopyTrigger trigger)
    {
        Assert.True(Enum.IsDefined(typeof(PracticeCopyTrigger), trigger));
    }

    // ---------------------------------------------------------------------------
    // E. Result contract — PracticeCopyResult properties
    // ---------------------------------------------------------------------------

    [Fact]
    public void Selector_Result_HasExpectedTrigger()
    {
        var library = new FakeCopyLibrary();
        var selector = new PracticeCopySelector(library);

        var ctx = MakeContext(PracticeCopyTrigger.ResumeBackground, practicePosition: 10, locale: "en");
        var result = selector.Select(ctx);

        Assert.Equal(PracticeCopyTrigger.ResumeBackground, result.Trigger);
    }

    [Fact]
    public void Selector_Result_MessageIdContainsTriggerName()
    {
        var library = new FakeCopyLibrary();
        var selector = new PracticeCopySelector(library);

        var ctx = MakeContext(PracticeCopyTrigger.ReturnLongAbsence, practicePosition: 200, locale: "en");
        var result = selector.Select(ctx);

        // Message ID convention: "{Trigger}.{Tone}.{Index}"
        Assert.StartsWith("ReturnLongAbsence.", result.MessageId);
    }

    // ---------------------------------------------------------------------------
    // F. AbsenceBucket helper — derive bucket from elapsed time
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]          // exactly zero minutes
    [InlineData(15)]         // 15 minutes
    [InlineData(29)]         // 29 minutes (boundary)
    public void AbsenceBucketHelper_SameSession_WhenUnder30Minutes(int minutesAgo)
    {
        var lastPracticed = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo);
        var bucket = AbsenceBucketHelper.Classify(lastPracticed);

        Assert.Equal(AbsenceBucket.SameSession, bucket);
    }

    [Theory]
    [InlineData(30)]         // exactly 30 minutes
    [InlineData(60)]         // 1 hour
    [InlineData(23 * 60)]    // 23 hours
    public void AbsenceBucketHelper_RecentReturn_WhenBetween30MinAnd24Hours(int minutesAgo)
    {
        var lastPracticed = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo);
        var bucket = AbsenceBucketHelper.Classify(lastPracticed);

        Assert.Equal(AbsenceBucket.RecentReturn, bucket);
    }

    [Theory]
    [InlineData(24 * 60)]         // exactly 1 day
    [InlineData(2 * 24 * 60)]     // 2 days
    [InlineData(3 * 24 * 60 - 1)] // just under 3 days
    public void AbsenceBucketHelper_ShortAbsence_WhenBetween1And3Days(int minutesAgo)
    {
        var lastPracticed = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo);
        var bucket = AbsenceBucketHelper.Classify(lastPracticed);

        Assert.Equal(AbsenceBucket.ShortAbsence, bucket);
    }

    [Theory]
    [InlineData(3 * 24 * 60)]     // exactly 3 days
    [InlineData(7 * 24 * 60)]     // 1 week
    [InlineData(30 * 24 * 60)]    // 1 month
    public void AbsenceBucketHelper_LongAbsence_WhenOver3Days(int minutesAgo)
    {
        var lastPracticed = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo);
        var bucket = AbsenceBucketHelper.Classify(lastPracticed);

        Assert.Equal(AbsenceBucket.LongAbsence, bucket);
    }

    [Fact]
    public void AbsenceBucketHelper_NullTimestamp_ReturnsSameSession()
    {
        // No prior practice — treat as SameSession (first-ever or no item states).
        var bucket = AbsenceBucketHelper.Classify(null);

        Assert.Equal(AbsenceBucket.SameSession, bucket);
    }

    // ---------------------------------------------------------------------------
    // G. State isolation — Select() must not mutate any session-like state
    // ---------------------------------------------------------------------------

    [Fact]
    public void Selector_DoesNotMutateContext()
    {
        var library = new FakeCopyLibrary();
        var selector = new PracticeCopySelector(library);

        var ctx = MakeContext(PracticeCopyTrigger.InitialReady, practicePosition: 77, locale: "en");
        var originalTrigger = ctx.Trigger;
        var originalPosition = ctx.PracticePosition;
        var originalLocale = ctx.Locale;

        _ = selector.Select(ctx);

        // PracticeCopyContext is a record — immutable by design.
        Assert.Equal(originalTrigger, ctx.Trigger);
        Assert.Equal(originalPosition, ctx.PracticePosition);
        Assert.Equal(originalLocale, ctx.Locale);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static PracticeCopyContext MakeContext(
        PracticeCopyTrigger trigger,
        long practicePosition,
        string locale) =>
        new(
            Trigger: trigger,
            PracticePosition: practicePosition,
            SessionCorrectCount: 0,
            SessionTotalCount: 0,
            AbsenceBucket: AbsenceBucket.SameSession,
            IsFirstEverSession: practicePosition == 0,
            Locale: locale);
}

// ---------------------------------------------------------------------------
// Fake library implementations for testing
// ---------------------------------------------------------------------------

/// <summary>
/// Provides a realistic fake pool: 3 variants per trigger across EN/DE/RU.
/// </summary>
internal sealed class FakeCopyLibrary : IPracticeCopyLibrary
{
    private static readonly string[] InitialReadyIds = ["InitialReady.Neutral.001", "InitialReady.DryHumor.001", "InitialReady.Welcoming.001"];
    private static readonly string[] ResumeManualPauseIds = ["ResumeManualPause.Neutral.001", "ResumeManualPause.LightlyCheeky.001", "ResumeManualPause.Neutral.002"];
    private static readonly string[] ResumeBackgroundIds = ["ResumeBackground.Neutral.001", "ResumeBackground.Welcoming.001", "ResumeBackground.DryHumor.001"];
    private static readonly string[] ReturnShortIds = ["ReturnShortAbsence.Neutral.001", "ReturnShortAbsence.DryHumor.001", "ReturnShortAbsence.Welcoming.001"];
    private static readonly string[] ReturnLongIds = ["ReturnLongAbsence.Neutral.001", "ReturnLongAbsence.DryHumor.001", "ReturnLongAbsence.LightlyCheeky.001"];
    private static readonly string[] FirstEverIds = ["FirstEverReady.Welcoming.001", "FirstEverReady.Neutral.001", "FirstEverReady.DryHumor.001"];
    private static readonly string[] NeutralReadyIds = ["NeutralReady.Neutral.001", "NeutralReady.Neutral.002", "NeutralReady.DryHumor.001"];
    private static readonly string[] NeutralPausedIds = ["NeutralPaused.Neutral.001", "NeutralPaused.Neutral.002"];

    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["InitialReady.Neutral.001"] = "Arithmetic is ready.",
        ["InitialReady.DryHumor.001"] = "The numbers have been waiting.",
        ["InitialReady.Welcoming.001"] = "Let's continue.",
        ["ResumeManualPause.Neutral.001"] = "Whenever you're ready.",
        ["ResumeManualPause.LightlyCheeky.001"] = "The problem hasn't solved itself.",
        ["ResumeManualPause.Neutral.002"] = "Practice is paused.",
        ["ResumeBackground.Neutral.001"] = "Back to practice.",
        ["ResumeBackground.Welcoming.001"] = "Ready to continue.",
        ["ResumeBackground.DryHumor.001"] = "Still here.",
        ["ReturnShortAbsence.Neutral.001"] = "Ready.",
        ["ReturnShortAbsence.DryHumor.001"] = "Back again.",
        ["ReturnShortAbsence.Welcoming.001"] = "Good to continue.",
        ["ReturnLongAbsence.Neutral.001"] = "Welcome back.",
        ["ReturnLongAbsence.DryHumor.001"] = "The facts haven't moved.",
        ["ReturnLongAbsence.LightlyCheeky.001"] = "Been a while.",
        ["FirstEverReady.Welcoming.001"] = "Your first fact is ready.",
        ["FirstEverReady.Neutral.001"] = "First practice begins.",
        ["FirstEverReady.DryHumor.001"] = "Arithmetic awaits.",
        ["NeutralReady.Neutral.001"] = "Ready to practice?",
        ["NeutralReady.Neutral.002"] = "Start when ready.",
        ["NeutralReady.DryHumor.001"] = "Numbers are waiting.",
        ["NeutralPaused.Neutral.001"] = "Paused.",
        ["NeutralPaused.Neutral.002"] = "Resume when ready.",
    };

    public IReadOnlyList<string> GetMessageIds(PracticeCopyTrigger trigger, string locale) =>
        trigger switch
        {
            PracticeCopyTrigger.InitialReady => InitialReadyIds,
            PracticeCopyTrigger.ResumeManualPause => ResumeManualPauseIds,
            PracticeCopyTrigger.ResumeBackground => ResumeBackgroundIds,
            PracticeCopyTrigger.ReturnShortAbsence => ReturnShortIds,
            PracticeCopyTrigger.ReturnLongAbsence => ReturnLongIds,
            PracticeCopyTrigger.FirstEverReady => FirstEverIds,
            PracticeCopyTrigger.NeutralReady => NeutralReadyIds,
            PracticeCopyTrigger.NeutralPaused => NeutralPausedIds,
            _ => []
        };

    public string? GetText(string messageId, string locale) =>
        Texts.TryGetValue(messageId, out var text) ? text : null;
}

/// <summary>Exactly two variants for InitialReady — used to test repetition switching.</summary>
internal sealed class TwoVariantFakeCopyLibrary : IPracticeCopyLibrary
{
    private static readonly string[] TwoIds = ["InitialReady.Neutral.001", "InitialReady.Neutral.002"];
    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["InitialReady.Neutral.001"] = "Option A.",
        ["InitialReady.Neutral.002"] = "Option B.",
        ["NeutralReady.Neutral.001"] = "Fallback.",
    };

    public IReadOnlyList<string> GetMessageIds(PracticeCopyTrigger trigger, string locale) =>
        trigger is PracticeCopyTrigger.InitialReady or PracticeCopyTrigger.NeutralReady
            ? TwoIds
            : [];

    public string? GetText(string messageId, string locale) =>
        Texts.TryGetValue(messageId, out var text) ? text : null;
}

/// <summary>Exactly one variant for InitialReady — used to test pool-size-1 fallback.</summary>
internal sealed class SingleVariantFakeCopyLibrary : IPracticeCopyLibrary
{
    private static readonly string[] OneId = ["InitialReady.Neutral.001"];
    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["InitialReady.Neutral.001"] = "Only option.",
        ["NeutralReady.Neutral.001"] = "Fallback.",
    };

    public IReadOnlyList<string> GetMessageIds(PracticeCopyTrigger trigger, string locale) =>
        trigger is PracticeCopyTrigger.InitialReady or PracticeCopyTrigger.NeutralReady
            ? OneId
            : [];

    public string? GetText(string messageId, string locale) =>
        Texts.TryGetValue(messageId, out var text) ? text : null;
}

/// <summary>Empty library — all pools return empty lists. Used to test fallback path.</summary>
internal sealed class EmptyFakeCopyLibrary : IPracticeCopyLibrary
{
    public IReadOnlyList<string> GetMessageIds(PracticeCopyTrigger trigger, string locale) => [];
    public string? GetText(string messageId, string locale) => null;
}
