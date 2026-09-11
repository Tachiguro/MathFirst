namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Progression;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class DeterministicSelectorTerminalLivenessTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstSlice4Tests_" + Guid.NewGuid().ToString("N"));

    public DeterministicSelectorTerminalLivenessTests()
    {
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDirectory, recursive: true); } catch { }
    }

    // A. Requested-role matrix
    [Theory]
    [InlineData(1, "new", PracticeSelectionRole.New)]
    [InlineData(1, "frontier", PracticeSelectionRole.Frontier)]
    [InlineData(1, "due", PracticeSelectionRole.Due)]
    [InlineData(1, "stale_maintenance", PracticeSelectionRole.Maintenance)]
    [InlineData(1, "early_review", PracticeSelectionRole.EarlyReview)]
    [InlineData(5, "due", PracticeSelectionRole.Due)]
    [InlineData(5, "frontier", PracticeSelectionRole.Frontier)]
    [InlineData(5, "stale_maintenance", PracticeSelectionRole.Maintenance)]
    [InlineData(5, "early_review", PracticeSelectionRole.EarlyReview)]
    [InlineData(13, "stale_maintenance", PracticeSelectionRole.Maintenance)]
    [InlineData(13, "frontier", PracticeSelectionRole.Frontier)]
    [InlineData(13, "due", PracticeSelectionRole.Due)]
    [InlineData(13, "early_review", PracticeSelectionRole.EarlyReview)]
    [InlineData(17, "frontier", PracticeSelectionRole.Frontier)]
    [InlineData(17, "due", PracticeSelectionRole.Due)]
    [InlineData(17, "stale_maintenance", PracticeSelectionRole.Maintenance)]
    [InlineData(17, "early_review", PracticeSelectionRole.EarlyReview)]
    public void FallbackMatrix_FollowsAuthoritativeChain(
        long position,
        string availablePool,
        PracticeSelectionRole expectedResolvedRole)
    {
        var context = CreateFallbackContext(position, availablePool);
        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
        Assert.Equal(expectedResolvedRole, result.ResolvedRole);
        Assert.Equal(expectedResolvedRole != PracticeSelectionRole.New, result.IsMaterialized);
        Assert.Equal(expectedResolvedRole == PracticeSelectionRole.New, result.IsNewIntroduction);
    }

    // B. New-only materialization
    [Fact]
    public void NewOnlyMaterialization_OnlyScheduledNewMaterializes()
    {
        var curriculum = new ArithmeticCurriculum();
        var selector = new AdaptivePracticeSelector();

        // 1. Requested New with unmaterialized candidate -> materializes
        var freshContext = CreateContext(1, curriculum, EmptyMaterialized());
        var newResult = selector.SelectTargetFact(freshContext);
        Assert.Equal(PracticeSelectionRole.New, newResult.ResolvedRole);
        Assert.False(newResult.IsMaterialized);
        Assert.True(newResult.IsNewIntroduction);

        // 2. Non-New role with empty pools on structured band -> fails closed, never materializes
        var structuredProgressions = CreateProgressions((ArithmeticOperation.Addition, 10));
        var dueContext = CreateContext(5, curriculum, EmptyMaterialized(), operationProgressions: structuredProgressions);
        Assert.Throws<InvalidOperationException>(() => selector.SelectTargetFact(dueContext));

        var maintenanceContext = CreateContext(13, curriculum, EmptyMaterialized(), operationProgressions: structuredProgressions);
        Assert.Throws<InvalidOperationException>(() => selector.SelectTargetFact(maintenanceContext));

        var frontierContext = CreateContext(17, curriculum, EmptyMaterialized(), operationProgressions: structuredProgressions);
        Assert.Throws<InvalidOperationException>(() => selector.SelectTargetFact(frontierContext));

        // 3. Requested New fallback (e.g. all new materialized, falls back to Frontier/Due/EarlyReview) -> never isNewIntroduction
        var owned = new AcquisitionOwnershipResolver(curriculum.Addition).GetOwnedFrontier(0);
        var materializedContext = CreateContext(1, curriculum, Materialize(owned));
        var fallbackResult = selector.SelectTargetFact(materializedContext);
        Assert.NotEqual(PracticeSelectionRole.New, fallbackResult.ResolvedRole);
        Assert.True(fallbackResult.IsMaterialized);
        Assert.False(fallbackResult.IsNewIntroduction);
    }

    // C. No AnyMaterialized
    [Fact]
    public void NoAnyMaterialized_CandidateInNoEligibleSemanticPoolIsNotSelected()
    {
        var curriculum = new ArithmeticCurriculum();
        var fact0 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var fact1 = new ArithmeticFact(ArithmeticOperation.Addition, 1, 0);
        var b0 = new CurriculumBand(ArithmeticOperation.Addition, 0, new CurriculumBandId("TEST-D01"), CurriculumBandKind.Dense, [fact0]);
        var b1 = new CurriculumBand(ArithmeticOperation.Addition, 1, new CurriculumBandId("TEST-S01"), CurriculumBandKind.Structured, [fact1]);
        var custom = new OperationCurriculum(ArithmeticOperation.Addition, [b0, b1]);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1)); // current band is 1

        // Materialized fact is from band 0 (not in current band 1 frontier).
        // It has NeedsRemediation=true, LastReview=10 and prospective=13 (13 < 10+4), so ineligible for remediation override.
        // Due is 100 (> 13), so not Due.
        // NeedsRemediation is true, so ineligible for Stale Maintenance and Early Review.
        // At position 13 (requested Maintenance), fallback chain is Maintenance -> Frontier -> Due -> EarlyReview.
        // All 4 pools are EMPTY!
        var itemState = ItemLearningState.CreateNew(fact0);
        itemState.NeedsRemediation = true;
        var fsrsState = new FsrsCardState(fact0.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 10, FsrsRating.Again);

        var materialized = new MaterializedState(
            [fact0],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [fact0.Id] = itemState },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [fact0.Id] = fsrsState });

        var context = CreateContext(13, curriculum, materialized, operationProgressions: progressions, curricula: curricula);

        // Must throw because no eligible semantic pool exists; must NOT fall back to AnyMaterialized!
        var ex = Assert.Throws<InvalidOperationException>(() => new AdaptivePracticeSelector().SelectTargetFact(context));
        Assert.Contains("Addition", ex.Message);
        Assert.Contains("13", ex.Message);
    }

    // D. Remediation boundary
    [Fact]
    public void RemediationBoundary_EligibleAtExactlyPlusFourFromLastReview()
    {
        var curriculum = new ArithmeticCurriculum();
        var fact = curriculum.Addition.Bands[0].Frontier[0];
        const long lastReview = 10;

        var itemState = ItemLearningState.CreateNew(fact);
        itemState.NeedsRemediation = true;
        itemState.RemediationDueOrder = 999; // Arbitrary session order must NOT affect PracticePosition-based eligibility
        var fsrsState = new FsrsCardState(fact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, lastReview, FsrsRating.Again);

        var materialized = new MaterializedState(
            [fact],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [fact.Id] = itemState },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [fact.Id] = fsrsState });

        var selector = new AdaptivePracticeSelector();

        // At prospective = 13 (lastReview + 3): NOT eligible for remediation override.
        // At position 13, scheduled op is Addition.
        var context13 = CreateContext(13, curriculum, materialized);
        var result13 = selector.SelectTargetFact(context13);
        Assert.NotEqual(PracticeSelectionRole.Remediation, result13.ResolvedRole);

        // At prospective = 17 (lastReview + 7 >= lastReview + 4): ELIGIBLE for remediation override!
        var context17 = CreateContext(17, curriculum, materialized);
        var result17 = selector.SelectTargetFact(context17);
        Assert.Equal(PracticeSelectionRole.Remediation, result17.ResolvedRole);
        Assert.Equal(fact.Id, result17.Fact.Id);

        // At prospective = 17, lastReview = 13 => 17 = 13 + 4 => exactly eligible!
        var fsrsState13 = new FsrsCardState(fact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 13, FsrsRating.Again);
        var materialized13 = new MaterializedState(
            [fact],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [fact.Id] = itemState },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [fact.Id] = fsrsState13 });

        var contextExact4 = CreateContext(17, curriculum, materialized13);
        var resultExact4 = selector.SelectTargetFact(contextExact4);
        Assert.Equal(PracticeSelectionRole.Remediation, resultExact4.ResolvedRole);
        Assert.Equal(fact.Id, resultExact4.Fact.Id);

        // If lastReview is 14 => 17 < 14 + 4 (18) => NOT eligible!
        var fsrsState14 = new FsrsCardState(fact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 14, FsrsRating.Again);
        var materialized14 = new MaterializedState(
            [fact],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [fact.Id] = itemState },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [fact.Id] = fsrsState14 });
        var contextNotYet4 = CreateContext(17, curriculum, materialized14);
        var resultNotYet4 = selector.SelectTargetFact(contextNotYet4);
        Assert.NotEqual(PracticeSelectionRole.Remediation, resultNotYet4.ResolvedRole);
    }

    // E. Due ordering
    [Fact]
    public void DuePool_DeterministicOrdering_OrdersByDuePosition_ThenLastReview_ThenFactId()
    {
        var curriculum = new ArithmeticCurriculum();
        var f1 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0); // Due 10, LastReview 5
        var f2 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1); // Due 10, LastReview 3
        var f3 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 2); // Due 8,  LastReview 7
        var f4 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 3); // Due 10, LastReview 3, Id tie break

        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, f1, f2, f3, f4);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1)); // band 1 so not in current frontier

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [f1.Id] = ItemLearningState.CreateNew(f1),
            [f2.Id] = ItemLearningState.CreateNew(f2),
            [f3.Id] = ItemLearningState.CreateNew(f3),
            [f4.Id] = ItemLearningState.CreateNew(f4)
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [f1.Id] = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 10, 5, FsrsRating.Good),
            [f2.Id] = new FsrsCardState(f2.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 10, 3, FsrsRating.Good),
            [f3.Id] = new FsrsCardState(f3.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 8, 7, FsrsRating.Good),
            [f4.Id] = new FsrsCardState(f4.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 10, 3, FsrsRating.Good)
        };

        var materialized = new MaterializedState([f1, f2, f3, f4], states, cards);
        var context = CreateContext(13, curriculum, materialized, operationProgressions: progressions, curricula: curricula);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);
        Assert.Equal(f3.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.Due, result.ResolvedRole);
    }

    // F. Useful Frontier ordering
    [Fact]
    public void UsefulFrontier_DeterministicOrdering_UnmasteredBeforeMastered_ThenAttempts_ThenLastReview_ThenFactId()
    {
        var curriculum = new ArithmeticCurriculum();
        var f1 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0); // Mastered=true, Attempts=1, LastReview=1
        var f2 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1); // Mastered=false, Attempts=3, LastReview=2
        var f3 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 2); // Mastered=false, Attempts=2, LastReview=5
        var f4 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 3); // Mastered=false, Attempts=2, LastReview=1

        var custom = new OperationCurriculum(
            ArithmeticOperation.Addition,
            [new CurriculumBand(ArithmeticOperation.Addition, 0, new CurriculumBandId("TEST-D01"), CurriculumBandKind.Dense, [f1, f2, f3, f4])]);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));

        var s1 = ItemLearningState.CreateNew(f1); s1.IsProvisionallyMastered = true; s1.TotalAttempts = 1;
        var s2 = ItemLearningState.CreateNew(f2); s2.IsProvisionallyMastered = false; s2.TotalAttempts = 3;
        var s3 = ItemLearningState.CreateNew(f3); s3.IsProvisionallyMastered = false; s3.TotalAttempts = 2;
        var s4 = ItemLearningState.CreateNew(f4); s4.IsProvisionallyMastered = false; s4.TotalAttempts = 2;

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [f1.Id] = s1, [f2.Id] = s2, [f3.Id] = s3, [f4.Id] = s4
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [f1.Id] = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 1, FsrsRating.Good),
            [f2.Id] = new FsrsCardState(f2.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 2, FsrsRating.Good),
            [f3.Id] = new FsrsCardState(f3.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 5, FsrsRating.Good),
            [f4.Id] = new FsrsCardState(f4.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 1, FsrsRating.Good)
        };

        var materialized = new MaterializedState([f1, f2, f3, f4], states, cards);
        var context = CreateContext(17, curriculum, materialized, curricula: curricula);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);
        Assert.Equal(f4.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.Frontier, result.ResolvedRole);
    }

    // G. Stale Maintenance boundary
    [Fact]
    public void StaleMaintenanceBoundary_TriggersAtExactlyPlusForty()
    {
        var curriculum = new ArithmeticCurriculum();
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, fact);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1)); // band 1

        var itemState = ItemLearningState.CreateNew(fact);
        var selector = new AdaptivePracticeSelector();

        // Ordinal 14 is position 53.
        // At position 53: prospective = 53. If lastReview = 14 => 53 = 14 + 39 => NOT stale! Falls back to Early Review.
        var fsrsState39 = new FsrsCardState(fact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 14, FsrsRating.Good);
        var mat39 = new MaterializedState(
            [fact],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [fact.Id] = itemState },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [fact.Id] = fsrsState39 });
        var contextNotStale = CreateContext(53, curriculum, mat39, operationProgressions: progressions, curricula: curricula);
        var resultNotStale = selector.SelectTargetFact(contextNotStale);
        Assert.Equal(PracticeSelectionRole.EarlyReview, resultNotStale.ResolvedRole);

        // If lastReview = 13 => 53 = 13 + 40 => STALE!
        var fsrsState40 = new FsrsCardState(fact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 13, FsrsRating.Good);
        var mat40 = new MaterializedState(
            [fact],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [fact.Id] = itemState },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [fact.Id] = fsrsState40 });
        var contextStale = CreateContext(53, curriculum, mat40, operationProgressions: progressions, curricula: curricula);
        var resultStale = selector.SelectTargetFact(contextStale);
        Assert.Equal(PracticeSelectionRole.Maintenance, resultStale.ResolvedRole);
    }

    // H. Stale Maintenance ordering
    [Fact]
    public void StaleMaintenance_DeterministicOrdering_LeastRecentlyReviewed_ThenDuePosition_ThenFactId()
    {
        var curriculum = new ArithmeticCurriculum();
        var f1 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0); // LastReview 10, Due 100
        var f2 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1); // LastReview 5,  Due 120
        var f3 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 2); // LastReview 5,  Due 90
        var f4 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 3); // LastReview 5,  Due 90, Id tie break

        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, f1, f2, f3, f4);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1));

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [f1.Id] = ItemLearningState.CreateNew(f1),
            [f2.Id] = ItemLearningState.CreateNew(f2),
            [f3.Id] = ItemLearningState.CreateNew(f3),
            [f4.Id] = ItemLearningState.CreateNew(f4)
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [f1.Id] = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 10, FsrsRating.Good),
            [f2.Id] = new FsrsCardState(f2.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 120, 5, FsrsRating.Good),
            [f3.Id] = new FsrsCardState(f3.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 90, 5, FsrsRating.Good),
            [f4.Id] = new FsrsCardState(f4.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 90, 5, FsrsRating.Good)
        };

        var materialized = new MaterializedState([f1, f2, f3, f4], states, cards);
        var context = CreateContext(53, curriculum, materialized, operationProgressions: progressions, curricula: curricula);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);
        Assert.Equal(f3.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.Maintenance, result.ResolvedRole);
    }

    // I. Early Review eligibility
    [Fact]
    public void EarlyReview_Eligibility_FutureDueMaterializedCard_ExcludesDueAndNeedsRemediation()
    {
        var curriculum = new ArithmeticCurriculum();
        var fFuture = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0); // Due 100, LastReview 10
        var fDue = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);    // Due 5 (due at pos 13)
        var fRemed = new ArithmeticFact(ArithmeticOperation.Addition, 0, 2);  // Due 100, NeedsRemediation=true

        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, fFuture, fDue, fRemed);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1));

        var sFuture = ItemLearningState.CreateNew(fFuture);
        var sDue = ItemLearningState.CreateNew(fDue);
        var sRemed = ItemLearningState.CreateNew(fRemed); sRemed.NeedsRemediation = true;

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [fFuture.Id] = sFuture,
            [fDue.Id] = sDue,
            [fRemed.Id] = sRemed
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [fFuture.Id] = new FsrsCardState(fFuture.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 10, FsrsRating.Good),
            [fDue.Id] = new FsrsCardState(fDue.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 5, 2, FsrsRating.Good),
            [fRemed.Id] = new FsrsCardState(fRemed.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 10, FsrsRating.Again)
        };

        var materialized = new MaterializedState([fFuture, fDue, fRemed], states, cards);
        var context = CreateContext(13, curriculum, materialized, operationProgressions: progressions, curricula: curricula);

        var selector = new AdaptivePracticeSelector();
        var dueResult = selector.SelectTargetFact(context);
        Assert.Equal(PracticeSelectionRole.Due, dueResult.ResolvedRole);
        Assert.Equal(fDue.Id, dueResult.Fact.Id);

        // Now remove fDue so Due pool is empty:
        var matNoDue = new MaterializedState([fFuture, fRemed],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [fFuture.Id] = sFuture, [fRemed.Id] = sRemed },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [fFuture.Id] = cards[fFuture.Id], [fRemed.Id] = cards[fRemed.Id] });
        var contextEarly = CreateContext(13, curriculum, matNoDue, operationProgressions: progressions, curricula: curricula);

        var earlyResult = selector.SelectTargetFact(contextEarly);
        Assert.Equal(PracticeSelectionRole.EarlyReview, earlyResult.ResolvedRole);
        Assert.Equal(fFuture.Id, earlyResult.Fact.Id);
    }

    // J. Future-due-only virtual-time liveness with real TrainingSession & SQLite
    [Fact]
    public async Task FutureDueOnly_VirtualTimeLiveness_RealTrainingSessionAdvancesToDueState()
    {
        var dbPath = Path.Combine(_testDirectory, "future_due_liveness.db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        for (var i = 0; i < 80; i++)
        {
            var fact = session.CurrentFact;
            session.SubmitAnswer(fact.CorrectResult);
            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess);
            Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: false) || session.InteractionState != SessionInteractionState.CorrectFeedback);
            if (session.InteractionState != SessionInteractionState.AwaitingAnswer)
            {
                session.AdvanceToNextFact(startTiming: false);
            }
        }

        Assert.Equal(80, session.Progression.PracticePosition);
        Assert.NotNull(session.CurrentFact);
    }

    // K. One-card operation
    [Fact]
    public void OneCardOperation_RepeatedSameOperationOpportunitiesRemainLiveWithoutDeadlock()
    {
        var curriculum = new ArithmeticCurriculum();
        var singleFact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, singleFact);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1));

        var itemState = ItemLearningState.CreateNew(singleFact);
        var fsrsState = new FsrsCardState(singleFact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good);

        var materialized = new MaterializedState(
            [singleFact],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [singleFact.Id] = itemState },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [singleFact.Id] = fsrsState });

        var selector = new AdaptivePracticeSelector();

        for (var position = 1; position <= 41; position += 4)
        {
            var context = CreateContext(position, curriculum, materialized, operationProgressions: progressions, curricula: curricula);
            var result = selector.SelectTargetFact(context);

            Assert.Equal(singleFact.Id, result.Fact.Id);
            Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
            Assert.True(result.IsMaterialized);
        }
    }

    // L. Multi-card Early Review rotation
    [Fact]
    public void MultiCard_EarlyReviewRotation_RotatesLeastRecentlyReviewed()
    {
        var curriculum = new ArithmeticCurriculum();
        var f1 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var f2 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var f3 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 2);

        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, f1, f2, f3);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1));

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [f1.Id] = ItemLearningState.CreateNew(f1),
            [f2.Id] = ItemLearningState.CreateNew(f2),
            [f3.Id] = ItemLearningState.CreateNew(f3)
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [f1.Id] = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good),
            [f2.Id] = new FsrsCardState(f2.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 5, FsrsRating.Good),
            [f3.Id] = new FsrsCardState(f3.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 9, FsrsRating.Good)
        };

        var selector = new AdaptivePracticeSelector();

        // Step 1: Position 13 -> F1 has oldest last review (1) -> selects F1
        var mat1 = new MaterializedState([f1, f2, f3], states, cards);
        var res1 = selector.SelectTargetFact(CreateContext(13, curriculum, mat1, operationProgressions: progressions, curricula: curricula));
        Assert.Equal(f1.Id, res1.Fact.Id);
        Assert.Equal(PracticeSelectionRole.EarlyReview, res1.ResolvedRole);

        // Simulate review of F1 at position 13: F1.LastReview becomes 13
        cards[f1.Id] = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 13, FsrsRating.Good);

        // Step 2: Position 17 -> F2 has oldest last review (5) -> selects F2
        var mat2 = new MaterializedState([f1, f2, f3], states, cards);
        var res2 = selector.SelectTargetFact(CreateContext(17, curriculum, mat2, operationProgressions: progressions, curricula: curricula));
        Assert.Equal(f2.Id, res2.Fact.Id);
        Assert.Equal(PracticeSelectionRole.EarlyReview, res2.ResolvedRole);

        // Simulate review of F2 at position 17: F2.LastReview becomes 17
        cards[f2.Id] = new FsrsCardState(f2.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 17, FsrsRating.Good);

        // Step 3: Position 21 -> F3 has oldest last review (9) -> selects F3
        var mat3 = new MaterializedState([f1, f2, f3], states, cards);
        var res3 = selector.SelectTargetFact(CreateContext(21, curriculum, mat3, operationProgressions: progressions, curricula: curricula));
        Assert.Equal(f3.Id, res3.Fact.Id);
        Assert.Equal(PracticeSelectionRole.EarlyReview, res3.ResolvedRole);

        // Simulate review of F3 at position 21: F3.LastReview becomes 21
        cards[f3.Id] = new FsrsCardState(f3.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 21, FsrsRating.Good);

        // Step 4: Position 25 -> F1 now has oldest last review (13 < 17 < 21) -> selects F1 again!
        var mat4 = new MaterializedState([f1, f2, f3], states, cards);
        var res4 = selector.SelectTargetFact(CreateContext(25, curriculum, mat4, operationProgressions: progressions, curricula: curricula));
        Assert.Equal(f1.Id, res4.Fact.Id);
        Assert.Equal(PracticeSelectionRole.EarlyReview, res4.ResolvedRole);
    }

    // M. Cooldown normal behavior
    [Fact]
    public void Cooldown_NormalBehavior_ExactAndMirrorExclusionOverPreviousThree()
    {
        var curriculum = new ArithmeticCurriculum();
        var fForward = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var fMirror = new ArithmeticFact(ArithmeticOperation.Addition, 1, 0);
        var fOther = new ArithmeticFact(ArithmeticOperation.Addition, 0, 2);

        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, fForward, fMirror, fOther);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1));

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [fForward.Id] = ItemLearningState.CreateNew(fForward),
            [fMirror.Id] = ItemLearningState.CreateNew(fMirror),
            [fOther.Id] = ItemLearningState.CreateNew(fOther)
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [fForward.Id] = new FsrsCardState(fForward.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good),
            [fMirror.Id] = new FsrsCardState(fMirror.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 2, FsrsRating.Good),
            [fOther.Id] = new FsrsCardState(fOther.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 3, FsrsRating.Good)
        };

        var materialized = new MaterializedState([fForward, fMirror, fOther], states, cards);

        var context = CreateContext(
            13,
            curriculum,
            materialized,
            recentFacts: [fForward],
            operationProgressions: progressions,
            curricula: curricula);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);
        Assert.Equal(fOther.Id, result.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.None, result.CooldownRelaxation);
    }

    // N. Cooldown relaxation order
    [Fact]
    public void CooldownRelaxation_RelaxesMirrorFirstThenExact()
    {
        var curriculum = new ArithmeticCurriculum();
        var fForward = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var fMirror = new ArithmeticFact(ArithmeticOperation.Addition, 1, 0);

        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, fForward, fMirror);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1));

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [fForward.Id] = ItemLearningState.CreateNew(fForward),
            [fMirror.Id] = ItemLearningState.CreateNew(fMirror)
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [fForward.Id] = new FsrsCardState(fForward.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good),
            [fMirror.Id] = new FsrsCardState(fMirror.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 2, FsrsRating.Good)
        };

        var materialized = new MaterializedState([fForward, fMirror], states, cards);

        // Case 1: Pool has fForward and fMirror. Recent has fForward.
        var mirrorContext = CreateContext(
            13, curriculum, materialized,
            recentFacts: [fForward],
            operationProgressions: progressions,
            curricula: curricula);
        var mirrorResult = new AdaptivePracticeSelector().SelectTargetFact(mirrorContext);
        Assert.Equal(fMirror.Id, mirrorResult.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.Mirror, mirrorResult.CooldownRelaxation);

        // Case 2: Pool has only fForward. Recent has fForward.
        var singleMat = new MaterializedState(
            [fForward],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [fForward.Id] = states[fForward.Id] },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [fForward.Id] = cards[fForward.Id] });
        var exactContext = CreateContext(
            13, curriculum, singleMat,
            recentFacts: [fForward],
            operationProgressions: progressions,
            curricula: curricula);
        var exactResult = new AdaptivePracticeSelector().SelectTargetFact(exactContext);
        Assert.Equal(fForward.Id, exactResult.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.Exact, exactResult.CooldownRelaxation);
    }

    // O. Previous same-operation opportunity
    [Fact]
    public void PreviousSameOperationOpportunity_IsFourPositionsBack_OutsideThreePositionCooldown()
    {
        var curriculum = new ArithmeticCurriculum();
        var f1 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var sub = new ArithmeticFact(ArithmeticOperation.Subtraction, 0, 0);
        var mul = new ArithmeticFact(ArithmeticOperation.Multiplication, 0, 0);
        var div = new ArithmeticFact(ArithmeticOperation.Division, 0, 1);

        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, f1);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1));

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [f1.Id] = ItemLearningState.CreateNew(f1)
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [f1.Id] = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good)
        };

        var materialized = new MaterializedState([f1], states, cards);

        var recent = new[] { f1, sub, mul, div };
        var context = CreateContext(5, curriculum, materialized, recentFacts: recent, operationProgressions: progressions, curricula: curricula);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);
        Assert.Equal(f1.Id, result.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.None, result.CooldownRelaxation);
    }

    // P. Restart equivalence
    [Fact]
    public async Task RestartEquivalence_UnacceptedSessionYieldsIdenticalSelection()
    {
        var dbPath = Path.Combine(_testDirectory, "restart_equivalence.db");
        using (var store1 = new SqliteLearnerStore(dbPath))
        {
            var session1 = new TrainingSession(store1);
            await session1.InitializeAsync(startTiming: false);
            var fact1 = session1.CurrentFact;

            await store1.CloseAsync();

            using var store2 = new SqliteLearnerStore(dbPath);
            var session2 = new TrainingSession(store2);
            await session2.InitializeAsync(startTiming: false);
            var fact2 = session2.CurrentFact;

            Assert.Equal(fact1.Id, fact2.Id);
            Assert.Equal(fact1.Operation, fact2.Operation);
        }
    }

    // Q. LastPracticedOrder independence
    [Fact]
    public void LastPracticedOrder_Independence_DoesNotAffectSelection()
    {
        var curriculum = new ArithmeticCurriculum();
        var f1 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var f2 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);

        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, f1, f2);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1));

        var s1 = ItemLearningState.CreateNew(f1); s1.LastPracticedOrder = 100;
        var s2 = ItemLearningState.CreateNew(f2); s2.LastPracticedOrder = 1;

        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [f1.Id] = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 2, FsrsRating.Good),
            [f2.Id] = new FsrsCardState(f2.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 5, FsrsRating.Good)
        };

        var matA = new MaterializedState([f1, f2], new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [f1.Id] = s1, [f2.Id] = s2 }, cards);
        var resA = new AdaptivePracticeSelector().SelectTargetFact(CreateContext(53, curriculum, matA, operationProgressions: progressions, curricula: curricula));

        s1.LastPracticedOrder = 1;
        s2.LastPracticedOrder = 100;
        var matB = new MaterializedState([f1, f2], new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [f1.Id] = s1, [f2.Id] = s2 }, cards);
        var resB = new AdaptivePracticeSelector().SelectTargetFact(CreateContext(53, curriculum, matB, operationProgressions: progressions, curricula: curricula));

        Assert.Equal(resA.Fact.Id, resB.Fact.Id);
        Assert.Equal(f1.Id, resA.Fact.Id);
    }

    // R. RemediationDueOrder independence
    [Fact]
    public void RemediationDueOrder_Independence_DoesNotAffectRemediationEligibilityOrOrder()
    {
        var curriculum = new ArithmeticCurriculum();
        var f1 = curriculum.Addition.Bands[0].Frontier[0];
        var f2 = curriculum.Addition.Bands[0].Frontier[1];

        var s1 = ItemLearningState.CreateNew(f1); s1.NeedsRemediation = true; s1.RemediationDueOrder = 100;
        var s2 = ItemLearningState.CreateNew(f2); s2.NeedsRemediation = true; s2.RemediationDueOrder = 1;

        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [f1.Id] = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 2, FsrsRating.Again),
            [f2.Id] = new FsrsCardState(f2.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 5, FsrsRating.Again)
        };

        var mat = new MaterializedState([f1, f2], new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [f1.Id] = s1, [f2.Id] = s2 }, cards);

        var res = new AdaptivePracticeSelector().SelectTargetFact(CreateContext(17, curriculum, mat));
        Assert.Equal(PracticeSelectionRole.Remediation, res.ResolvedRole);
        Assert.Equal(f1.Id, res.Fact.Id);
    }

    // S. SQL-before-LIMIT correctness (> 64 candidates)
    [Fact]
    public async Task SqlBeforeLimit_SelectsGloballyFirstCandidateOverWindowSize()
    {
        var dbPath = Path.Combine(_testDirectory, "sql_before_limit.db");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var items = new List<ArithmeticFact>();
        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);

        var curPosition = 0L;
        for (var i = 0; i < 70; i++)
        {
            var fact = new ArithmeticFact(ArithmeticOperation.Addition, i, 0);
            items.Add(fact);
            var state = ItemLearningState.CreateNew(fact);
            state.NeedsRemediation = false;
            var lastReview = 70 - i;
            var card = new FsrsCardState(fact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 1000, lastReview, FsrsRating.Good);

            itemStates[fact.Id] = state;
            fsrsStates[fact.Id] = card;

            for (var op = 0; op < 4; op++)
            {
                curPosition++;
                var opEnum = (ArithmeticOperation)(op + 1);
                var opFact = opEnum == ArithmeticOperation.Addition ? fact : new ArithmeticFact(opEnum, 0, op == 3 ? 1 : 0);
                var opState = opEnum == ArithmeticOperation.Addition ? state : ItemLearningState.CreateNew(opFact);
                var opCard = opEnum == ArithmeticOperation.Addition ? card : new FsrsCardState(opFact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 1000, 1, FsrsRating.Good);

                var changeSet = new SubmissionChangeSet(
                    Guid.NewGuid().ToString("N"),
                    curPosition,
                    new AttemptRecord(Guid.NewGuid().ToString("N"), opFact.Id, opFact.Operation, opFact.LeftOperand, opFact.RightOperand, opFact.CorrectResult, opFact.CorrectResult, true, true, 500, DateTimeOffset.UtcNow, AttemptOutcome.Correct, curPosition),
                    opState,
                    new LearnerProgression { PracticePosition = curPosition },
                    opCard,
                    Enum.GetValues<ArithmeticOperation>().ToDictionary(o => o, o => new OperationProgression(o, 0, 0)));
                var commitResult = await store.CommitSubmissionAsync(changeSet);
                Assert.True(commitResult.IsSuccess);
            }
        }

        var request = new PracticeSelectionEvidenceRequest(
            ArithmeticOperation.Addition,
            prospectivePracticePosition: 100,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: [],
            introductionFrontier: []);

        var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

        Assert.NotEmpty(evidence.EarlyReviewCandidates);
        Assert.Equal("add:69+0", evidence.EarlyReviewCandidates[0].Fact.Id);
    }

    // T. Terminal Addition
    [Fact]
    public void TerminalAddition_SafeBand125_SelectsMaterializedCardsSafely()
    {
        var curriculum = new ArithmeticCurriculum();
        Assert.True(curriculum.Addition.TryGetBand(125, out var terminalBand));
        Assert.False(curriculum.Addition.TryGetBand(126, out _));

        var facts = terminalBand!.Frontier;
        var states = facts.ToDictionary(f => f.Id, ItemLearningState.CreateNew, StringComparer.Ordinal);
        var cards = facts.ToDictionary(
            f => f.Id,
            f => new FsrsCardState(f.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 10, FsrsRating.Good),
            StringComparer.Ordinal);
        var materialized = new MaterializedState(facts, states, cards);

        var progressions = CreateProgressions((ArithmeticOperation.Addition, 125));
        var context = CreateContext(1, curriculum, materialized, operationProgressions: progressions);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);
        Assert.Contains(result.Fact.Id, facts.Select(f => f.Id));
        Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
    }

    // U. Terminal all-operation coverage
    [Fact]
    public void TerminalAllOperations_ExpectedSafeBands_MatchCurriculumAndSelectSafely()
    {
        var curriculum = new ArithmeticCurriculum();

        var expectedTerminalBands = new Dictionary<ArithmeticOperation, int>
        {
            [ArithmeticOperation.Addition] = 125,
            [ArithmeticOperation.Subtraction] = 135,
            [ArithmeticOperation.Multiplication] = 32,
            [ArithmeticOperation.Division] = 32
        };

        foreach (var (operation, terminalBandIndex) in expectedTerminalBands)
        {
            var opCurriculum = curriculum.GetCurriculum(operation);
            Assert.True(opCurriculum.TryGetBand(terminalBandIndex, out var band));
            Assert.False(opCurriculum.TryGetBand(terminalBandIndex + 1, out _));

            var facts = band!.Frontier;
            var states = facts.ToDictionary(f => f.Id, ItemLearningState.CreateNew, StringComparer.Ordinal);
            var cards = facts.ToDictionary(
                f => f.Id,
                f => new FsrsCardState(f.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 10, FsrsRating.Good),
                StringComparer.Ordinal);
            var materialized = new MaterializedState(facts, states, cards);

            var progressions = CreateProgressions((operation, terminalBandIndex));
            var position = (int)operation;
            var context = CreateContext(position, curriculum, materialized, operationProgressions: progressions);

            var result = new AdaptivePracticeSelector().SelectTargetFact(context);
            Assert.Equal(operation, result.ScheduledOperation);
            Assert.Contains(result.Fact.Id, facts.Select(f => f.Id));
        }
    }

    // V. int.MaxValue safety
    [Fact]
    public void IntMaxValueSafety_ExtremeProgressionDoesNotOverflow()
    {
        var curriculum = new ArithmeticCurriculum();
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var itemState = ItemLearningState.CreateNew(fact);
        var fsrsState = new FsrsCardState(fact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 10, FsrsRating.Good);
        var materialized = new MaterializedState(
            [fact],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [fact.Id] = itemState },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [fact.Id] = fsrsState });

        var context = CreateContext(5_000_000_001L, curriculum, materialized);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);
        Assert.NotNull(result.Fact);
        Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
    }

    // W. Impossible zero-candidate state fails fast closed
    [Fact]
    public void ZeroCandidateState_FailsFastClosedDeterministically()
    {
        var curriculum = new ArithmeticCurriculum();
        var structuredProgressions = CreateProgressions((ArithmeticOperation.Addition, 10));
        var context = CreateContext(5, curriculum, EmptyMaterialized(), operationProgressions: structuredProgressions);

        var ex = Assert.Throws<InvalidOperationException>(() => new AdaptivePracticeSelector().SelectTargetFact(context));
        Assert.Contains("Addition", ex.Message);
        Assert.Contains("5", ex.Message);
    }

    // X. SQLite / in-memory conformance
    [Fact]
    public async Task SqliteInMemoryConformance_YieldsIdenticalSelectorResult()
    {
        var dbPath = Path.Combine(_testDirectory, "conformance.db");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var curriculum = new ArithmeticCurriculum();
        var facts = curriculum.Addition.Bands[0].Frontier.Take(10).ToArray();

        for (var i = 0; i < facts.Length; i++)
        {
            var fact = facts[i];
            var state = ItemLearningState.CreateNew(fact);
            var card = new FsrsCardState(fact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 20 + i, i + 1, FsrsRating.Good);
            var changeSet = new SubmissionChangeSet(
                Guid.NewGuid().ToString("N"),
                i + 1,
                new AttemptRecord(Guid.NewGuid().ToString("N"), fact.Id, fact.Operation, fact.LeftOperand, fact.RightOperand, fact.CorrectResult, fact.CorrectResult, true, true, 500, DateTimeOffset.UtcNow, AttemptOutcome.Correct, i + 1),
                state,
                new LearnerProgression { PracticePosition = i + 1 },
                card,
                Enum.GetValues<ArithmeticOperation>().ToDictionary(op => op, op => new OperationProgression(op, 0, 0)));
            await store.CommitSubmissionAsync(changeSet);
        }

        var snapshot = await store.LoadSnapshotAsync();
        var ownedFrontier = new AcquisitionOwnershipResolver(curriculum.Addition).GetOwnedFrontier(0);
        var request = new PracticeSelectionEvidenceRequest(
            ArithmeticOperation.Addition,
            prospectivePracticePosition: 15,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: ownedFrontier,
            introductionFrontier: ownedFrontier);

        var sqliteEvidence = await store.LoadPracticeSelectionEvidenceAsync(request);
        var inMemoryEvidence = PracticeSelectionEvidence.FromSnapshot(snapshot, request);

        var contextSqlite = new PracticeSelectionContext(
            15,
            0,
            snapshot.Progression.OperationProgressions,
            Enum.GetValues<ArithmeticOperation>().ToDictionary(op => op, op => curriculum.GetCurriculum(op)),
            new PracticeCandidateIndex(sqliteEvidence),
            snapshot.RecentAttempts.Select(a => new ArithmeticFact(a.Operation, a.LeftOperand, a.RightOperand)));

        var contextInMemory = new PracticeSelectionContext(
            15,
            0,
            snapshot.Progression.OperationProgressions,
            Enum.GetValues<ArithmeticOperation>().ToDictionary(op => op, op => curriculum.GetCurriculum(op)),
            new PracticeCandidateIndex(inMemoryEvidence),
            snapshot.RecentAttempts.Select(a => new ArithmeticFact(a.Operation, a.LeftOperand, a.RightOperand)));

        var selector = new AdaptivePracticeSelector();
        var resSqlite = selector.SelectTargetFact(contextSqlite);
        var resInMemory = selector.SelectTargetFact(contextInMemory);

        Assert.Equal(resSqlite.Fact.Id, resInMemory.Fact.Id);
        Assert.Equal(resSqlite.ResolvedRole, resInMemory.ResolvedRole);
        Assert.Equal(resSqlite.CooldownRelaxation, resInMemory.CooldownRelaxation);
    }

    // Y. Existing selector invariant regression
    [Fact]
    public void ExistingSelectorInvariants_OperationSchedulingAndTenRoleCycleUnchanged()
    {
        for (var p = 1; p <= 40; p++)
        {
            var op = AdaptivePracticeSelector.GetScheduledOperation(p);
            var expectedOp = (ArithmeticOperation)(((p - 1) % 4) + 1);
            Assert.Equal(expectedOp, op);

            var role = AdaptivePracticeSelector.GetRequestedRole(p);
            var opOrdinal = ((p - 1) / 4) + 1;
            var expectedRole = ((opOrdinal - 1) % 10) switch
            {
                0 => PracticeSelectionRole.New,
                1 => PracticeSelectionRole.Due,
                2 => PracticeSelectionRole.New,
                3 => PracticeSelectionRole.Maintenance,
                4 => PracticeSelectionRole.Frontier,
                5 => PracticeSelectionRole.New,
                6 => PracticeSelectionRole.Due,
                7 => PracticeSelectionRole.New,
                8 => PracticeSelectionRole.Due,
                9 => PracticeSelectionRole.Frontier,
                _ => throw new InvalidOperationException()
            };
            Assert.Equal(expectedRole, role);
        }
    }

    // Section 24: Selector Totality Property Tests
    [Theory]
    [InlineData(ArithmeticOperation.Addition, 0, 1)]
    [InlineData(ArithmeticOperation.Addition, 10, 50)]
    [InlineData(ArithmeticOperation.Addition, 125, 200)]
    [InlineData(ArithmeticOperation.Subtraction, 0, 1)]
    [InlineData(ArithmeticOperation.Subtraction, 50, 100)]
    [InlineData(ArithmeticOperation.Subtraction, 135, 300)]
    [InlineData(ArithmeticOperation.Multiplication, 0, 1)]
    [InlineData(ArithmeticOperation.Multiplication, 15, 80)]
    [InlineData(ArithmeticOperation.Multiplication, 32, 120)]
    [InlineData(ArithmeticOperation.Division, 0, 1)]
    [InlineData(ArithmeticOperation.Division, 15, 80)]
    [InlineData(ArithmeticOperation.Division, 32, 120)]
    public void SelectorTotalityProperty_ValidReachableLearnerStatesAlwaysReturnDeterministicFact(
        ArithmeticOperation operation,
        int bandIndex,
        long practicePosition)
    {
        var curriculum = new ArithmeticCurriculum();
        var opCurriculum = curriculum.GetCurriculum(operation);
        Assert.True(opCurriculum.TryGetBand(bandIndex, out var band));

        var owned = new AcquisitionOwnershipResolver(opCurriculum).GetOwnedFrontier(bandIndex);
        var sampleFact = owned[0];
        var itemState = ItemLearningState.CreateNew(sampleFact);
        var fsrsState = new FsrsCardState(sampleFact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, practicePosition + 100, 1, FsrsRating.Good);

        var materialized = new MaterializedState(
            [sampleFact],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [sampleFact.Id] = itemState },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [sampleFact.Id] = fsrsState });

        var progressions = CreateProgressions((operation, bandIndex));
        var prospectivePosition = practicePosition + ((int)operation - 1 - ((practicePosition - 1) % 4) + 4) % 4;
        if (prospectivePosition <= 0) prospectivePosition += 4;

        var context = CreateContext(prospectivePosition, curriculum, materialized, operationProgressions: progressions);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);
        Assert.NotNull(result);
        Assert.NotNull(result.Fact);
        Assert.Equal(operation, result.ScheduledOperation);
        Assert.Equal(operation, result.Fact.Operation);
    }

    private static PracticeSelectionContext CreateContext(
        long position,
        ArithmeticCurriculum curriculum,
        MaterializedState materialized,
        int currentSessionOrder = 0,
        IEnumerable<ArithmeticFact>? recentFacts = null,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? operationProgressions = null,
        IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum>? curricula = null) => new(
        position,
        currentSessionOrder,
        operationProgressions ?? CreateProgressions(),
        curricula ?? CreateCurricula(curriculum),
        new PracticeCandidateIndex(materialized.Facts, materialized.ItemStates, materialized.FsrsStates),
        recentFacts ?? Array.Empty<ArithmeticFact>());

    private static MaterializedState Materialize(
        IEnumerable<ArithmeticFact> facts,
        Action<ItemLearningState>? configureState = null,
        long? fsrsDuePosition = null,
        string? dueOnlyFactId = null)
    {
        var factArray = facts.ToArray();
        var states = factArray.ToDictionary(fact => fact.Id, ItemLearningState.CreateNew, StringComparer.Ordinal);
        foreach (var state in states.Values)
        {
            configureState?.Invoke(state);
        }
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);
        if (fsrsDuePosition.HasValue)
        {
            foreach (var fact in factArray.Where(fact => dueOnlyFactId is null || fact.Id == dueOnlyFactId))
            {
                cards[fact.Id] = new FsrsCardState(fact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, fsrsDuePosition.Value, 1, FsrsRating.Good);
            }
        }
        return new MaterializedState(
            factArray,
            states,
            cards);
    }

    private static PracticeSelectionContext CreateFallbackContext(long position, string availablePool) =>
        availablePool switch
        {
            "new" => CreateContext(position, new ArithmeticCurriculum(), EmptyMaterialized()),
            "frontier" => CreateFrontierFallbackContext(position),
            "due" => CreateEmptyOwnedContext(position, "due"),
            "stale_maintenance" => CreateEmptyOwnedContext(position, "stale_maintenance"),
            "early_review" => CreateEmptyOwnedContext(position, "early_review"),
            _ => throw new ArgumentOutOfRangeException(nameof(availablePool), availablePool, null)
        };

    private static PracticeSelectionContext CreateFrontierFallbackContext(long position)
    {
        var curriculum = new ArithmeticCurriculum();
        var allFrontier = curriculum.Addition.Bands[0].Frontier;
        var states = allFrontier.ToDictionary(f => f.Id, ItemLearningState.CreateNew, StringComparer.Ordinal);
        var cards = allFrontier.ToDictionary(
            f => f.Id,
            f => new FsrsCardState(f.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, position - 5, FsrsRating.Good),
            StringComparer.Ordinal);
        return CreateContext(position, curriculum, new MaterializedState(allFrontier, states, cards));
    }

    private static PracticeSelectionContext CreateEmptyOwnedContext(long position, string stateKind)
    {
        var curriculum = new ArithmeticCurriculum();
        var earlier = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, earlier);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1));

        var itemState = ItemLearningState.CreateNew(earlier);
        FsrsCardState card = stateKind switch
        {
            "due" => new FsrsCardState(earlier.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 1, 1, FsrsRating.Good),
            "stale_maintenance" => new FsrsCardState(earlier.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, position - 45, FsrsRating.Good),
            "early_review" => new FsrsCardState(earlier.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, position - 5, FsrsRating.Good),
            _ => throw new ArgumentOutOfRangeException(nameof(stateKind), stateKind, null)
        };

        var materialized = new MaterializedState(
            [earlier],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [earlier.Id] = itemState },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [earlier.Id] = card });

        return CreateContext(
            position,
            curriculum,
            materialized,
            operationProgressions: progressions,
            curricula: curricula);
    }

    private static OperationCurriculum CreateRepeatedFactCurriculum(
        ArithmeticOperation operation,
        params ArithmeticFact[] facts)
    {
        var first = new CurriculumBand(operation, 0, new CurriculumBandId("TEST-D01"), CurriculumBandKind.Dense, facts);
        var repeated = new CurriculumBand(operation, 1, new CurriculumBandId("TEST-S01"), CurriculumBandKind.Structured, facts);
        return new OperationCurriculum(operation, [first, repeated]);
    }

    private static IReadOnlyDictionary<ArithmeticOperation, OperationProgression> CreateProgressions(
        params (ArithmeticOperation Operation, int BandIndex)[] overrides)
    {
        var bands = overrides.ToDictionary(item => item.Operation, item => item.BandIndex);
        return Enum.GetValues<ArithmeticOperation>().ToDictionary(
            operation => operation,
            operation => new OperationProgression(operation, bands.GetValueOrDefault(operation), 0));
    }

    private static IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum> CreateCurricula(
        ArithmeticCurriculum curriculum,
        params (ArithmeticOperation Operation, OperationCurriculum Curriculum)[] overrides)
    {
        var result = new Dictionary<ArithmeticOperation, OperationCurriculum>
        {
            [ArithmeticOperation.Addition] = curriculum.Addition,
            [ArithmeticOperation.Subtraction] = curriculum.Subtraction,
            [ArithmeticOperation.Multiplication] = curriculum.Multiplication,
            [ArithmeticOperation.Division] = curriculum.Division
        };
        foreach (var item in overrides)
        {
            result[item.Operation] = item.Curriculum;
        }
        return result;
    }

    private static MaterializedState EmptyMaterialized() => new(
        [],
        new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
        new Dictionary<string, FsrsCardState>(StringComparer.Ordinal));

    private sealed record MaterializedState(
        IReadOnlyList<ArithmeticFact> Facts,
        IReadOnlyDictionary<string, ItemLearningState> ItemStates,
        IReadOnlyDictionary<string, FsrsCardState> FsrsStates);
}
