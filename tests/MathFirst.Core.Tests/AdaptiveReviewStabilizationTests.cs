namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class AdaptiveReviewStabilizationTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstSlice3Tests_" + Guid.NewGuid().ToString("N"));

    public AdaptiveReviewStabilizationTests()
    {
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDirectory, recursive: true); } catch { }
    }

    private string GetTempDbPath() => Path.Combine(_testDirectory, $"test_{Guid.NewGuid():N}.db");

    private static long GetOpPosition(ArithmeticOperation operation, long attemptOrdinal, int enabledCount = 4)
    {
        var bagIndex = attemptOrdinal - 1;
        for (var p = (bagIndex * (long)enabledCount) + 1; p <= (bagIndex + 1) * (long)enabledCount; p++)
        {
            if (AdaptivePracticeSelector.GetScheduledOperation(p) == operation)
            {
                return p;
            }
        }
        throw new InvalidOperationException($"Operation {operation} not found in bag {bagIndex}.");
    }

    // A. Dense New no longer overrides Due
    [Fact]
    public void DenseBand_UnseenFactsAndEligibleDue_SelectsDueNotNew()
    {
        var curriculum = new ArithmeticCurriculum();
        var f1 = curriculum.Addition.Bands[0].Frontier[0];
        var f2 = curriculum.Addition.Bands[0].Frontier[1];

        // f1 is materialized and Due at posDue
        var posDue = GetOpPosition(ArithmeticOperation.Addition, 2); // ordinal 2 requests Due
        var state1 = ItemLearningState.CreateNew(f1);
        var card1 = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, DuePracticePosition: posDue - 1, LastReviewPracticePosition: 1, LastRating: FsrsRating.Good);

        var materialized = new MaterializedState(
            [f1],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [f1.Id] = state1 },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [f1.Id] = card1 });

        var context = CreateContext(posDue, curriculum, materialized, scheduledOperationAttemptOrdinal: 2);
        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Due, result.ResolvedRole);
        Assert.True(result.IsMaterialized);
        Assert.False(result.IsNewIntroduction);
        Assert.Equal(f1.Id, result.Fact.Id);
    }

    // B. Dense New no longer overrides Maintenance
    [Fact]
    public void DenseBand_UnseenFactsAndEligibleMaintenance_SelectsMaintenanceNotNew()
    {
        var curriculum = new ArithmeticCurriculum();
        var f1 = curriculum.Addition.Bands[0].Frontier[0];

        // posMaintenance is ordinal 4 (requests Maintenance). posMaintenance >= lastReview + 40
        var posMaintenance = GetOpPosition(ArithmeticOperation.Addition, 14); // ordinal 14 requests Maintenance (14-1 % 10 = 3 -> Maintenance)
        var state1 = ItemLearningState.CreateNew(f1);
        // lastReview = 1. posMaintenance >= 1 + 40 -> eligible! Due is in the future (500)
        var card1 = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, DuePracticePosition: 500, LastReviewPracticePosition: 1, LastRating: FsrsRating.Good);

        var materialized = new MaterializedState(
            [f1],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [f1.Id] = state1 },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [f1.Id] = card1 });

        var context = CreateContext(posMaintenance, curriculum, materialized, scheduledOperationAttemptOrdinal: 14);
        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
        Assert.Equal(PracticeSelectionRole.Maintenance, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Maintenance, result.ResolvedRole);
        Assert.True(result.IsMaterialized);
        Assert.False(result.IsNewIntroduction);
        Assert.Equal(f1.Id, result.Fact.Id);
    }

    // C. Dense Frontier remains consolidation
    [Fact]
    public void DenseBand_UnseenFactsAndRequestedFrontier_SelectsMaterializedFrontierNotNew()
    {
        var curriculum = new ArithmeticCurriculum();
        var f1 = curriculum.Addition.Bands[0].Frontier[0];

        var posFrontier = GetOpPosition(ArithmeticOperation.Addition, 5); // ordinal 5 requests Frontier
        var state1 = ItemLearningState.CreateNew(f1);
        var card1 = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, DuePracticePosition: 500, LastReviewPracticePosition: posFrontier - 1, LastRating: FsrsRating.Good);

        var materialized = new MaterializedState(
            [f1],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [f1.Id] = state1 },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [f1.Id] = card1 });

        var context = CreateContext(posFrontier, curriculum, materialized, scheduledOperationAttemptOrdinal: 5);
        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
        Assert.Equal(PracticeSelectionRole.Frontier, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Frontier, result.ResolvedRole);
        Assert.True(result.IsMaterialized);
        Assert.False(result.IsNewIntroduction);
        Assert.Equal(f1.Id, result.Fact.Id);
    }

    // D. New slots still introduce rapidly
    [Fact]
    public void DenseBand_RequestedNewSlots_IntroduceUnmaterializedFacts()
    {
        var curriculum = new ArithmeticCurriculum();
        var selector = new AdaptivePracticeSelector();
        var introduced = new List<ArithmeticFact>();

        var newOrdinals = new long[] { 1, 3, 6, 8 };
        foreach (var ordinal in newOrdinals)
        {
            var pos = GetOpPosition(ArithmeticOperation.Addition, ordinal);
            var context = CreateContext(pos, curriculum, Materialize(introduced), scheduledOperationAttemptOrdinal: ordinal);
            var result = selector.SelectTargetFact(context);

            Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
            Assert.Equal(PracticeSelectionRole.New, result.RequestedRole);
            Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
            Assert.False(result.IsMaterialized);
            Assert.True(result.IsNewIntroduction);
            Assert.DoesNotContain(result.Fact.Id, introduced.Select(f => f.Id));
            introduced.Add(result.Fact);
        }

        Assert.Equal(4, introduced.Count);
        Assert.Equal(curriculum.Addition.Bands[0].Frontier.Select(f => f.Id).ToHashSet(), introduced.Select(f => f.Id).ToHashSet());
    }

    // E. Bounded New introduction cadence: exactly 4 New introductions per 10 attempts
    [Fact]
    public void BoundedNewCadence_TwentyAttempts_ProducesAtMostEightNewIntroductions()
    {
        var curriculum = new ArithmeticCurriculum();
        // Use Addition Band 4 (ADD-D05) which has 11 owned facts in a Dense band
        var band4Progressions = CreateProgressions((ArithmeticOperation.Addition, 4));
        var band4 = curriculum.Addition.Bands[4];
        Assert.Equal(CurriculumBandKind.Dense, band4.Kind);
        Assert.Equal(11, band4.Frontier.Count);

        var selector = new AdaptivePracticeSelector();
        var allMaterialized = new List<ArithmeticFact>();
        var newCount = 0;
        var expectedNewOrdinals = new HashSet<long> { 1, 3, 6, 8, 11, 13, 16, 18 };

        for (var ordinal = 1L; ordinal <= 20; ordinal++)
        {
            var pos = GetOpPosition(ArithmeticOperation.Addition, ordinal);
            var context = CreateContext(pos, curriculum, Materialize(allMaterialized), scheduledOperationAttemptOrdinal: ordinal, operationProgressions: band4Progressions);
            var result = selector.SelectTargetFact(context);

            var unseenCount = band4.Frontier.Count(f => !allMaterialized.Any(m => m.Id == f.Id));

            if (expectedNewOrdinals.Contains(ordinal))
            {
                // New opportunity: unseen facts exist, requested New resolves to New introduction
                Assert.True(unseenCount > 0, $"Expected unseen facts available at ordinal {ordinal}.");
                Assert.Equal(PracticeSelectionRole.New, result.RequestedRole);
                Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
                Assert.True(result.IsNewIntroduction);
                Assert.False(result.IsMaterialized);
                newCount++;
                allMaterialized.Add(result.Fact);
            }
            else
            {
                // Non-New opportunity: even though unseen facts remain available, role is non-New and does not introduce
                Assert.True(unseenCount > 0, $"Expected unseen facts available at non-New ordinal {ordinal}.");
                Assert.NotEqual(PracticeSelectionRole.New, result.RequestedRole);
                Assert.NotEqual(PracticeSelectionRole.New, result.ResolvedRole);
                Assert.False(result.IsNewIntroduction);
                Assert.True(result.IsMaterialized);
                Assert.Contains(result.Fact.Id, allMaterialized.Select(m => m.Id));
            }
        }

        // Exactly 8 New introductions occurred over 20 attempts under 4/10 bounded cadence
        Assert.Equal(8, newCount);
    }

    // F. Remediation still overrides regardless of requested role
    [Fact]
    public void Remediation_OverridesRequestedRole_EvenWhenDenseUnseenFactsExist()
    {
        var curriculum = new ArithmeticCurriculum();
        var f1 = curriculum.Addition.Bands[0].Frontier[0];

        // Ordinal 1 requests New, but f1 needs remediation and is eligible (spacing >= 4)
        var posNew = GetOpPosition(ArithmeticOperation.Addition, 1);
        var state1 = ItemLearningState.CreateNew(f1);
        state1.NeedsRemediation = true;
        var card1 = new FsrsCardState(f1.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, posNew - 5, FsrsRating.Again);

        var materialized = new MaterializedState(
            [f1],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [f1.Id] = state1 },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [f1.Id] = card1 });

        var context = CreateContext(posNew, curriculum, materialized, scheduledOperationAttemptOrdinal: 1);
        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.New, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Remediation, result.ResolvedRole);
        Assert.Equal(f1.Id, result.Fact.Id);
        Assert.True(result.IsMaterialized);
    }

    // G. Structured behavior unchanged
    [Fact]
    public void StructuredBands_UnifiedRoleResolution_PreservesSampleIntroductionsAndConsolidation()
    {
        var curriculum = new ArithmeticCurriculum();
        var structuredProgressions = CreateProgressions((ArithmeticOperation.Addition, 10)); // Structured band 10
        var selector = new AdaptivePracticeSelector();

        // Ordinal 1 requests New -> introduces sample fact
        var pos1 = GetOpPosition(ArithmeticOperation.Addition, 1);
        var ctx1 = CreateContext(pos1, curriculum, EmptyMaterialized(), scheduledOperationAttemptOrdinal: 1, operationProgressions: structuredProgressions);
        var res1 = selector.SelectTargetFact(ctx1);

        Assert.Equal(PracticeSelectionRole.New, res1.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, res1.ResolvedRole);
        Assert.True(res1.IsNewIntroduction);

        // Ordinal 2 requests Due -> with res1 materialized, falls back to Frontier consolidation
        var pos2 = GetOpPosition(ArithmeticOperation.Addition, 2);
        var mat1 = Materialize([res1.Fact]);
        var ctx2 = CreateContext(pos2, curriculum, mat1, scheduledOperationAttemptOrdinal: 2, operationProgressions: structuredProgressions);
        var res2 = selector.SelectTargetFact(ctx2);

        Assert.Equal(PracticeSelectionRole.Due, res2.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Frontier, res2.ResolvedRole);
        Assert.Equal(res1.Fact.Id, res2.Fact.Id);
    }

    // H. Long-Run Deterministic Simulation: Strong Learner
    [Fact]
    public async Task StrongLearner_LongRunSimulation_AdvancesBandsWhilePeriodicallyReviewing()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        var newIntroductions = 0;
        var reviewAttempts = 0;
        var seenFacts = new HashSet<string>(StringComparer.Ordinal);

        for (var p = 1L; p <= 80; p++)
        {
            var fact = session.CurrentFact;
            var eval = session.SubmitAnswer(fact.CorrectResult);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

            if (eval.ChangeSet.UpdatedItemState.TotalAttempts == 1)
            {
                newIntroductions++;
                Assert.True(seenFacts.Add(fact.Id));
            }
            else
            {
                reviewAttempts++;
                Assert.Contains(fact.Id, seenFacts);
            }

            if (p < 80)
            {
                if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);
                    session.ContinuePractice(startTiming: false);
                }
            }
        }

        // Verify progression continues: all 4 operations advanced past Band 0
        Assert.All(Enum.GetValues<ArithmeticOperation>(), op =>
            Assert.True(session.Progression.OperationProgressions[op].BandIndex >= 1));

        // Over 80 turns (20 per op), bounded New produces at most 8 New slots per op (32 max).
        // Materialized review/consolidation is interleaved periodically across the run.
        Assert.True(newIntroductions >= 20, $"Expected >= 20 new introductions, got {newIntroductions}");
        Assert.True(reviewAttempts >= 40, $"Expected >= 40 review attempts, got {reviewAttempts}");
        Assert.Equal(80, newIntroductions + reviewAttempts);
    }

    private static MaterializedState Materialize(IEnumerable<ArithmeticFact> facts)
    {
        var factArray = facts.ToArray();
        var states = factArray.ToDictionary(f => f.Id, ItemLearningState.CreateNew, StringComparer.Ordinal);
        var cards = factArray.ToDictionary(
            f => f.Id,
            f => new FsrsCardState(f.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good),
            StringComparer.Ordinal);
        return new MaterializedState(factArray, states, cards);
    }

    private static MaterializedState EmptyMaterialized() =>
        new([], new Dictionary<string, ItemLearningState>(StringComparer.Ordinal), new Dictionary<string, FsrsCardState>(StringComparer.Ordinal));

    private static PracticeSelectionContext CreateContext(
        long position,
        ArithmeticCurriculum curriculum,
        MaterializedState materialized,
        long scheduledOperationAttemptOrdinal,
        int currentSessionOrder = 0,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? operationProgressions = null,
        IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum>? curricula = null) => new(
        position,
        currentSessionOrder,
        operationProgressions ?? CreateProgressions(),
        curricula ?? CreateCurricula(curriculum),
        new PracticeCandidateIndex(materialized.Facts, materialized.ItemStates, materialized.FsrsStates),
        Array.Empty<ArithmeticFact>(),
        scheduledOperationAttemptOrdinal);

    private static IReadOnlyDictionary<ArithmeticOperation, OperationProgression> CreateProgressions(
        params (ArithmeticOperation Operation, int BandIndex)[] overrides)
    {
        var dict = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(op => op, op => new OperationProgression(op, 0, 0));
        foreach (var (op, bandIndex) in overrides)
        {
            dict[op] = new OperationProgression(op, bandIndex, 0);
        }
        return dict;
    }

    private static IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum> CreateCurricula(
        ArithmeticCurriculum curriculum,
        params (ArithmeticOperation Operation, OperationCurriculum Curriculum)[] overrides)
    {
        var dict = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(op => op, op => curriculum.GetCurriculum(op));
        foreach (var (op, curr) in overrides)
        {
            dict[op] = curr;
        }
        return dict;
    }

    private sealed record MaterializedState(
        IReadOnlyList<ArithmeticFact> Facts,
        IReadOnlyDictionary<string, ItemLearningState> ItemStates,
        IReadOnlyDictionary<string, FsrsCardState> FsrsStates);
}
