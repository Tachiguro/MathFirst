namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Copy;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
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
        var result1 = selector.Select(ctx)!;
        var selector2 = new PracticeCopySelector(library);
        var result2 = selector2.Select(ctx)!;

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

        var id1 = selector.Select(ctx1)!.MessageId;
        var id2 = selector.Select(ctx2)!.MessageId;

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

        var readyResult = selector.Select(ready)!;
        var pausedResult = selector.Select(paused)!;

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

        var r1 = selector1.Select(ctx)!;
        var r2 = selector2.Select(ctx)!;

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

        var first = selector.Select(ctx)!.MessageId;
        // Calling again with the same context after the first selection recorded recency
        // should prefer the other variant (when pool has 2 items).
        var second = selector.Select(ctx)!.MessageId;

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

        var first = selector.Select(ctx)!.MessageId;
        // Second call with pool size 1 — the exclusion must not apply (pool size 1 means
        // window is capped at 0, so the single variant is always eligible).
        var second = selector.Select(ctx)!.MessageId;

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void Selector_ProductionPool_DoesNotRepeatWithinRecencyWindowAcrossMultipleRollovers()
    {
        var selector = new PracticeCopySelector(new PracticeCopyLibrary());
        var ctx = MakeContext(PracticeCopyTrigger.InitialReady, practicePosition: 42, locale: "en");
        var selectedIds = Enumerable.Range(0, 24)
            .Select(_ => selector.Select(ctx)!.MessageId)
            .ToArray();

        for (var index = 0; index < selectedIds.Length; index++)
        {
            var priorWindow = selectedIds
                .Skip(Math.Max(0, index - 5))
                .Take(Math.Min(5, index));
            Assert.DoesNotContain(selectedIds[index], priorWindow);
        }

        Assert.True(selectedIds.Distinct(StringComparer.Ordinal).Count() > 5);
    }

    [Fact]
    public void Selector_OrderedRecency_EvictsOldestAndRetainsNewest()
    {
        var selector = new PracticeCopySelector(new SixVariantFakeCopyLibrary());
        var context = MakeContext(PracticeCopyTrigger.InitialReady, 42, "en");
        var selectedIds = Enumerable.Range(0, 7)
            .Select(_ => selector.Select(context)!.MessageId)
            .ToArray();

        Assert.Equal(6, selectedIds.Take(6).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(selectedIds[0], selectedIds[6]);
        Assert.NotEqual(selectedIds[5], selectedIds[6]);
    }

    // ---------------------------------------------------------------------------
    // C. Fallback — the component-owned state uses established localized copy
    //    when contextual and neutral pools are unavailable
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(PracticeCopyTrigger.InitialReady)]
    [InlineData(PracticeCopyTrigger.ReturnShortAbsence)]
    [InlineData(PracticeCopyTrigger.ReturnLongAbsence)]
    [InlineData(PracticeCopyTrigger.ResumeManualPause)]
    [InlineData(PracticeCopyTrigger.ResumeBackground)]
    [InlineData(PracticeCopyTrigger.NeutralReady)]
    [InlineData(PracticeCopyTrigger.NeutralPaused)]
    public void Selector_AllTriggers_FallbackReturnsNonEmptyText(PracticeCopyTrigger trigger)
    {
        var state = new PracticeGateCopyState(new PracticeCopySelector(new EmptyFakeCopyLibrary()));

        var ctx = MakeContext(trigger, practicePosition: 0, locale: "en");
        state.Activate(ctx, "Localized fallback");

        Assert.NotNull(state.Current);
        Assert.Equal("Localized fallback", state.Current.LocalizedText);
        Assert.True(state.Current.IsFallback);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Selector_AllLocales_FallbackReturnsNonEmptyText(string locale)
    {
        var state = new PracticeGateCopyState(new PracticeCopySelector(new EmptyFakeCopyLibrary()));

        var ctx = MakeContext(PracticeCopyTrigger.InitialReady, practicePosition: 1, locale: locale);
        state.Activate(ctx, $"fallback-{locale}");

        Assert.Equal($"fallback-{locale}", state.Current!.LocalizedText);
    }

    // ---------------------------------------------------------------------------
    // D. Trigger resolution — correct trigger derived from context
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PracticeCopyContext_FreshOrResetLearningState_UsesNeutralInitialReadyCopy()
    {
        var session = new TrainingSession(new SnapshotStore(CreateSnapshot()));
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();

        var context = PracticeCopyContext.FromSession(
            session,
            "en",
            new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(PracticeCopyTrigger.InitialReady, context.Trigger);
    }

    [Fact]
    public async Task PracticeCopyContext_UsesPersistedLatestAttemptWhenMaterializedItemStateIsBoundedAway()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var latestAcceptedAt = now.AddDays(-10);
        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;
        var latestAttempt = new AttemptRecord(
            "latest-persisted",
            "add:9+9",
            ArithmeticOperation.Addition,
            9,
            9,
            18,
            18,
            true,
            true,
            900,
            latestAcceptedAt,
            practicePosition: 1);
        var snapshot = CreateSnapshot(progression, [latestAttempt], latestAcceptedAt);
        var session = new TrainingSession(new SnapshotStore(snapshot));
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();

        Assert.DoesNotContain("add:9+9", session.ItemStates.Keys);

        var context = PracticeCopyContext.FromSession(session, "en", now);

        Assert.Equal(PracticeCopyTrigger.ReturnLongAbsence, context.Trigger);
    }

    [Theory]
    [InlineData(AbsenceBucket.Recent)]
    [InlineData(AbsenceBucket.ShortAbsence)]
    [InlineData(AbsenceBucket.LongAbsence)]
    public void AbsenceBucket_AllValues_DefinedAndDistinct(AbsenceBucket bucket)
    {
        // Ensures the enum exists and all values are individually addressable.
        Assert.True(Enum.IsDefined(typeof(AbsenceBucket), bucket));
    }

    [Theory]
    [InlineData(PracticeCopyTrigger.InitialReady)]
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
        var result = selector.Select(ctx)!;

        Assert.Equal(PracticeCopyTrigger.ResumeBackground, result.Trigger);
    }

    [Fact]
    public void Selector_Result_MessageIdContainsTriggerName()
    {
        var library = new FakeCopyLibrary();
        var selector = new PracticeCopySelector(library);

        var ctx = MakeContext(PracticeCopyTrigger.ReturnLongAbsence, practicePosition: 200, locale: "en");
        var result = selector.Select(ctx)!;

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
    public void AbsenceBucketHelper_Recent_WhenUnder30Minutes(int minutesAgo)
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var lastPracticed = now.AddMinutes(-minutesAgo);
        var bucket = AbsenceBucketHelper.Classify(lastPracticed, now);

        Assert.Equal(AbsenceBucket.Recent, bucket);
    }

    [Theory]
    [InlineData(30)]         // exactly 30 minutes
    [InlineData(60)]              // 1 hour
    [InlineData(24 * 60)]         // exactly 1 day
    [InlineData(3 * 24 * 60 - 1)] // just under 3 days
    public void AbsenceBucketHelper_ShortAbsence_WhenBetween30MinutesAnd3Days(int minutesAgo)
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var lastPracticed = now.AddMinutes(-minutesAgo);
        var bucket = AbsenceBucketHelper.Classify(lastPracticed, now);

        Assert.Equal(AbsenceBucket.ShortAbsence, bucket);
    }

    [Theory]
    [InlineData(3 * 24 * 60)]     // exactly 3 days
    [InlineData(7 * 24 * 60)]     // 1 week
    [InlineData(30 * 24 * 60)]    // 1 month
    public void AbsenceBucketHelper_LongAbsence_WhenOver3Days(int minutesAgo)
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var lastPracticed = now.AddMinutes(-minutesAgo);
        var bucket = AbsenceBucketHelper.Classify(lastPracticed, now);

        Assert.Equal(AbsenceBucket.LongAbsence, bucket);
    }

    [Fact]
    public void AbsenceBucketHelper_NullTimestamp_ReturnsRecent()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var bucket = AbsenceBucketHelper.Classify(null, now);

        Assert.Equal(AbsenceBucket.Recent, bucket);
    }

    [Fact]
    public void AbsenceBucketHelper_UsesExactFrozenClockBoundaries()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            AbsenceBucket.Recent,
            AbsenceBucketHelper.Classify(now - TimeSpan.FromMinutes(30) + TimeSpan.FromTicks(1), now));
        Assert.Equal(
            AbsenceBucket.ShortAbsence,
            AbsenceBucketHelper.Classify(now - TimeSpan.FromMinutes(30), now));
        Assert.Equal(
            AbsenceBucket.LongAbsence,
            AbsenceBucketHelper.Classify(now - TimeSpan.FromDays(3), now));
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

    [Fact]
    public void GatePresentation_RepeatedReadsRemainStableUntilNextActivation()
    {
        var state = new PracticeGateCopyState(new PracticeCopySelector(new FakeCopyLibrary()));
        state.Activate(MakeContext(PracticeCopyTrigger.InitialReady, 42, "en"), "Ready");
        var activated = state.Current!;

        for (var index = 0; index < 20; index++)
        {
            Assert.Same(activated, state.Current);
            Assert.Equal(activated.MessageId, state.Current!.MessageId);
            Assert.Equal(activated.LocalizedText, state.Current.LocalizedText);
        }
    }

    [Fact]
    public void GatePresentation_MeaningfulTransitionCanSelectNewMessage()
    {
        var state = new PracticeGateCopyState(new PracticeCopySelector(new FakeCopyLibrary()));
        state.Activate(MakeContext(PracticeCopyTrigger.InitialReady, 42, "en"), "Ready");
        var readyId = state.Current!.MessageId;

        state.Activate(MakeContext(PracticeCopyTrigger.ResumeManualPause, 42, "en"), "Paused");

        Assert.NotEqual(readyId, state.Current!.MessageId);
        Assert.Equal(PracticeCopyTrigger.ResumeManualPause, state.Current.Trigger);
    }

    [Fact]
    public void GatePresentation_LanguageChangePreservesMessageIdentity()
    {
        var library = new PracticeCopyLibrary();
        var state = new PracticeGateCopyState(new PracticeCopySelector(library));
        state.Activate(MakeContext(PracticeCopyTrigger.InitialReady, 42, "en"), "Ready");
        var messageId = state.Current!.MessageId;
        var english = state.Current.LocalizedText;

        state.Relocalize("de-DE", "Bereit");

        Assert.Equal(messageId, state.Current!.MessageId);
        Assert.Equal(library.GetText(messageId, "de"), state.Current.LocalizedText);
        Assert.NotEqual(english, state.Current.LocalizedText);
    }

    [Fact]
    public void GatePresentation_ConsumerRecreationForSameActivation_DoesNotSelectAgain()
    {
        var selector = new PracticeCopySelector(new FakeCopyLibrary());
        var context = MakeContext(PracticeCopyTrigger.InitialReady, 42, "en");
        var applicationState = new PracticeGateCopyState(selector);
        applicationState.Synchronize(new PracticeGatePresentationIdentity(3, 7), context, "Ready");
        var selectedId = applicationState.Current!.MessageId;

        // A replacement Home consumer synchronizes the same application state.
        applicationState.Synchronize(new PracticeGatePresentationIdentity(3, 7), context, "Ready");

        Assert.Equal(selectedId, applicationState.Current!.MessageId);
    }

    [Fact]
    public void GatePresentation_SameActivationRelocalizesWithoutSelection_ButNewActivationMaySelectAgain()
    {
        var library = new PracticeCopyLibrary();
        var state = new PracticeGateCopyState(new PracticeCopySelector(library));
        var englishContext = MakeContext(PracticeCopyTrigger.InitialReady, 42, "en");
        state.Synchronize(new PracticeGatePresentationIdentity(3, 7), englishContext, "Ready");
        var messageId = state.Current!.MessageId;

        state.Synchronize(
            new PracticeGatePresentationIdentity(3, 7),
            englishContext with { Locale = "de" },
            "Bereit");

        Assert.Equal(messageId, state.Current!.MessageId);
        Assert.Equal(library.GetText(messageId, "de"), state.Current.LocalizedText);

        state.Synchronize(new PracticeGatePresentationIdentity(3, 8), englishContext, "Ready");

        Assert.NotEqual(messageId, state.Current!.MessageId);
    }

    [Fact]
    public void GatePresentation_SameActivationNumberInNewLearnerGeneration_SelectsAgain()
    {
        var state = new PracticeGateCopyState(new PracticeCopySelector(new FakeCopyLibrary()));
        var context = MakeContext(PracticeCopyTrigger.InitialReady, 42, "en");
        state.Synchronize(new PracticeGatePresentationIdentity(1, 7), context, "Ready");
        var oldMessageId = state.Current!.MessageId;

        state.Synchronize(new PracticeGatePresentationIdentity(2, 7), context, "Ready");

        Assert.NotEqual(oldMessageId, state.Current!.MessageId);
    }

    [Fact]
    public void Home_UsesApplicationLifetimeGateCopyStateAndSessionActivationIdentity()
    {
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        Assert.Contains("@inject PracticeGateCopyState PracticeGateCopyState", home, StringComparison.Ordinal);
        Assert.Contains("Session.LearnerStateGenerationRevision", home, StringComparison.Ordinal);
        Assert.Contains("Session.PracticeGateActivationRevision", home, StringComparison.Ordinal);
        Assert.DoesNotContain("new PracticeGateCopyState", home, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GatePresentation_ActivationDoesNotMutateLearningState()
    {
        var session = new TrainingSession(new SnapshotStore(CreateSnapshot()));
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();
        var fact = session.CurrentFact;
        var practicePosition = session.Progression.PracticePosition;
        var sessionOrder = session.SessionOrderCounter;
        var itemCount = session.ItemStates.Count;
        var state = new PracticeGateCopyState(new PracticeCopySelector(new PracticeCopyLibrary()));

        state.Activate(
            PracticeCopyContext.FromSession(
                session,
                "en",
                new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero)),
            "Ready");

        Assert.Equal(practicePosition, session.Progression.PracticePosition);
        Assert.Equal(sessionOrder, session.SessionOrderCounter);
        Assert.Equal(itemCount, session.ItemStates.Count);
        Assert.Equal(fact, session.CurrentFact);
        Assert.Equal(0, session.SessionCorrectCount);
        Assert.Equal(0, session.SessionTotalCount);
    }

    [Fact]
    public void PracticeGateTitle_DoesNotSelectCopyDuringRendering()
    {
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));
        var propertyStart = home.IndexOf("private string PracticeGateTitle", StringComparison.Ordinal);
        var propertyEnd = home.IndexOf("private string PracticeGateAction", propertyStart, StringComparison.Ordinal);

        Assert.True(propertyStart >= 0 && propertyEnd > propertyStart);
        var property = home[propertyStart..propertyEnd];
        Assert.DoesNotContain("CopySelector.Select", property, StringComparison.Ordinal);
        Assert.DoesNotContain("PracticeCopyContext.FromSession", property, StringComparison.Ordinal);
    }

    [Fact]
    public void PracticeCopyLibrary_NormalizesLocaleWithApplicationPolicy()
    {
        var library = new PracticeCopyLibrary();
        var id = library.GetMessageIds(PracticeCopyTrigger.InitialReady, "de")[0];

        Assert.Equal("Bereit zum Üben?", library.GetText(id, " de_DE "));
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
            AbsenceBucket: AbsenceBucket.Recent,
            Locale: locale);

    private static LearnerSnapshot CreateSnapshot(
        LearnerProgression? progression = null,
        IReadOnlyList<AttemptRecord>? recentAttempts = null,
        DateTimeOffset? latestAcceptedPracticeAt = null) =>
        new(
            progression ?? LearnerProgression.CreateFresh(),
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
            new Dictionary<string, MathFirst.Application.Scheduling.FsrsCardState>(StringComparer.Ordinal),
            recentAttempts ?? [],
            1,
            LearnerProgression.DefaultSchemaVersion,
            null,
            latestAcceptedPracticeAt);

    private static string GetRepositoryPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "MathFirst.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine([current!.FullName, .. segments]);
    }

    private sealed class SnapshotStore(LearnerSnapshot snapshot) : ILearnerStore
    {
        public string StoragePath => "inmemory://practice-copy";

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(snapshot.RecentAttempts, operation, bandStartedPracticePosition, frontierFactIds));

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
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

internal sealed class SixVariantFakeCopyLibrary : IPracticeCopyLibrary
{
    private static readonly string[] Ids = Enumerable.Range(1, 6)
        .Select(index => $"InitialReady.Neutral.{index:000}")
        .ToArray();

    public IReadOnlyList<string> GetMessageIds(PracticeCopyTrigger trigger, string locale) =>
        trigger == PracticeCopyTrigger.InitialReady ? Ids : [];

    public string? GetText(string messageId, string locale) =>
        Ids.Contains(messageId, StringComparer.Ordinal) ? messageId : null;
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
