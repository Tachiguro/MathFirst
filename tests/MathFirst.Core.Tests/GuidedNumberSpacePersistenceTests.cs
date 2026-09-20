namespace MathFirst.Core.Tests;

using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class GuidedNumberSpacePersistenceTests
{
    // ---------------------------------------------------------------------------
    // Request Constructor and Validation Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void Constructor_DefaultsToUnrestrictedGate()
    {
        var ownedFrontier = new[] { new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2) };
        var request = new PracticeSelectionEvidenceRequest(
            ArithmeticOperation.Multiplication,
            prospectivePracticePosition: 10,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: ownedFrontier,
            introductionFrontier: ownedFrontier);

        Assert.Same(GuidedNumberSpaceGate.Unrestricted, request.GuidedNumberSpaceGate);
        Assert.False(request.GuidedNumberSpaceGate.IsActive);
    }

    [Fact]
    public void Constructor_NullGate_ThrowsArgumentNullException()
    {
        var ownedFrontier = new[] { new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2) };

        Assert.Throws<ArgumentNullException>(() =>
            new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: ownedFrontier,
                introductionFrontier: ownedFrontier,
                currentBandIndex: 1,
                guidedNumberSpaceGate: null!));

        Assert.Throws<ArgumentNullException>(() =>
            new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: ownedFrontier,
                introductionFrontier: ownedFrontier,
                guidedNumberSpaceGate: null!));
    }

    // ---------------------------------------------------------------------------
    // Snapshot Guided Filtering Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void Snapshot_GuidedIneligibleFacts_ExcludedFromAllSemanticPools()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1); // ceiling = 4

        var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2); // 4 <= 4 (eligible)
        var highDue = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 3); // 6 > 4 (ineligible)
        var highMaint = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 4); // 8 > 4 (ineligible)
        var highRemed = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 5); // 10 > 4 (ineligible)
        var highEarly = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 6); // 12 > 4 (ineligible)
        var highFrontier = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 7); // 14 > 4 (ineligible)

        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);

        AddCandidate(itemStates, fsrsStates, eligibleFact, duePos: 1, lastReviewPos: 1);
        AddCandidate(itemStates, fsrsStates, highDue, duePos: 1, lastReviewPos: 1);
        AddCandidate(itemStates, fsrsStates, highMaint, duePos: 100, lastReviewPos: 1);
        AddCandidate(itemStates, fsrsStates, highRemed, duePos: 100, lastReviewPos: 1, needsRemediation: true);
        AddCandidate(itemStates, fsrsStates, highEarly, duePos: 100, lastReviewPos: 1);
        AddCandidate(itemStates, fsrsStates, highFrontier, duePos: 100, lastReviewPos: 1);

        var snapshot = CreateSnapshot(itemStates, fsrsStates);
        var request = new PracticeSelectionEvidenceRequest(
            ArithmeticOperation.Multiplication,
            prospectivePracticePosition: 50,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: [eligibleFact, highFrontier],
            introductionFrontier: [eligibleFact, highFrontier],
            currentBandIndex: int.MaxValue,
            guidedNumberSpaceGate: gate);

        var evidence = PracticeSelectionEvidence.FromSnapshot(snapshot, request);

        Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleFact.Id);
        Assert.DoesNotContain(evidence.DueCandidates, c => c.Fact.Id == highDue.Id);
        Assert.DoesNotContain(evidence.MaintenanceCandidates, c => c.Fact.Id == highMaint.Id);
        Assert.DoesNotContain(evidence.RemediationCandidates, c => c.Fact.Id == highRemed.Id);
        Assert.DoesNotContain(evidence.EarlyReviewCandidates, c => c.Fact.Id == highEarly.Id);
        Assert.DoesNotContain(evidence.CurrentBandCandidates, c => c.Fact.Id == highFrontier.Id);
    }

    // ---------------------------------------------------------------------------
    // SQLite Guided Filtering Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sqlite_GuidedIneligibleFacts_ExcludedFromAllSemanticPools()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1); // ceiling = 4

            var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2); // 4 <= 4 (eligible)
            var highDue = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 3); // 6 > 4 (ineligible)
            var highMaint = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 4); // 8 > 4 (ineligible)
            var highRemed = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 5); // 10 > 4 (ineligible)
            var highEarly = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 6); // 12 > 4 (ineligible)
            var highFrontier = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 7); // 14 > 4 (ineligible)

            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            AddSeedRow(seedData, eligibleFact, duePos: 1, lastReviewPos: 1);
            AddSeedRow(seedData, highDue, duePos: 1, lastReviewPos: 1);
            AddSeedRow(seedData, highMaint, duePos: 100, lastReviewPos: 1);
            AddSeedRow(seedData, highRemed, duePos: 100, lastReviewPos: 1, needsRemediation: true);
            AddSeedRow(seedData, highEarly, duePos: 100, lastReviewPos: 1);
            AddSeedRow(seedData, highFrontier, duePos: 100, lastReviewPos: 1);

            await SeedItemAndFsrsAsync(dbPath, seedData);

            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 50,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [eligibleFact, highFrontier],
                introductionFrontier: [eligibleFact, highFrontier],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

            Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleFact.Id);
            Assert.DoesNotContain(evidence.DueCandidates, c => c.Fact.Id == highDue.Id);
            Assert.DoesNotContain(evidence.MaintenanceCandidates, c => c.Fact.Id == highMaint.Id);
            Assert.DoesNotContain(evidence.RemediationCandidates, c => c.Fact.Id == highRemed.Id);
            Assert.DoesNotContain(evidence.EarlyReviewCandidates, c => c.Fact.Id == highEarly.Id);
            Assert.DoesNotContain(evidence.CurrentBandCandidates, c => c.Fact.Id == highFrontier.Id);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    // ---------------------------------------------------------------------------
    // >64 Adversarial Poisoning Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sqlite_AdversarialDuePool_65IneligibleRows_DoNotPoisonEligibleRow()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1); // ceiling = 4
            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            var ineligibleFacts = new List<ArithmeticFact>();

            // 70 high facts: mul:2*x where x = 3..72 (results 6..144 > 4). Due position = 1.
            for (var right = 3; right <= 72; right++)
            {
                var fact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, right);
                ineligibleFacts.Add(fact);
                AddSeedRow(seedData, fact, duePos: 1, lastReviewPos: 1);
            }

            Assert.True(ineligibleFacts.Count >= 70);

            // 1 low fact: mul:2*2 (result 4 <= 4). Due position = 2.
            var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2);
            AddSeedRow(seedData, eligibleFact, duePos: 2, lastReviewPos: 1);

            await SeedItemAndFsrsAsync(dbPath, seedData);

            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [eligibleFact],
                introductionFrontier: [eligibleFact],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            // Mechanically prove the adversarial precondition:
            // In SQL Due order (ORDER BY due_practice_position ASC, ..., fact_id ASC),
            // all 70 ineligible facts (due=1) precede the eligible fact (due=2).
            var unfilteredDueOrder = seedData
                .Where(x => x.Fsrs is not null && x.Fsrs.DuePracticePosition <= request.ProspectivePracticePosition)
                .OrderBy(x => x.Fsrs!.DuePracticePosition)
                .ThenBy(x => x.Fsrs?.LastReviewPracticePosition ?? 0)
                .ThenBy(x => x.Item.FactId, StringComparer.Ordinal)
                .Select(x => x.Item.FactId)
                .ToArray();

            var eligibleIndex = Array.IndexOf(unfilteredDueOrder, eligibleFact.Id);
            Assert.True(
                eligibleIndex >= PracticeSelectionEvidenceRequest.CandidateWindowSize,
                $"Precondition failed: eligible fact index in unfiltered Due order was {eligibleIndex}, expected >= {PracticeSelectionEvidenceRequest.CandidateWindowSize}.");

            Assert.DoesNotContain(
                eligibleFact.Id,
                unfilteredDueOrder.Take(PracticeSelectionEvidenceRequest.CandidateWindowSize));

            // Act
            var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

            // Assert: The eligible fact is found because ineligible rows did not consume the 64-window.
            Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleFact.Id);
            Assert.True(evidence.DueCandidates.Count <= PracticeSelectionEvidenceRequest.CandidateWindowSize);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_AdversarialRemediationPool_65IneligibleRows_DoNotPoisonEligibleRow()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1); // ceiling = 4
            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();

            // 70 high facts: mul:2*x where x = 3..72 (results 6..144 > 4). Needs remediation, lastReview = 1.
            for (var right = 3; right <= 72; right++)
            {
                var fact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, right);
                AddSeedRow(seedData, fact, duePos: 100, lastReviewPos: 1, needsRemediation: true);
            }

            // 1 low fact: mul:2*2 (result 4 <= 4). Needs remediation, lastReview = 2.
            var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2);
            AddSeedRow(seedData, eligibleFact, duePos: 100, lastReviewPos: 2, needsRemediation: true);

            await SeedItemAndFsrsAsync(dbPath, seedData);

            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10, // 10 >= 2 + 4
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [eligibleFact],
                introductionFrontier: [eligibleFact],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            // Mechanically prove adversarial precondition:
            // ORDER BY card.last_review_practice_position ASC, item.fact_id ASC
            // 70 high facts (lastReview=1) precede eligible fact (lastReview=2).
            var unfilteredRemediationOrder = seedData
                .Where(x => x.Item.NeedsRemediation
                    && x.Fsrs?.LastReviewPracticePosition is not null
                    && request.ProspectivePracticePosition >= x.Fsrs.LastReviewPracticePosition.Value + 4)
                .OrderBy(x => x.Fsrs!.LastReviewPracticePosition!.Value)
                .ThenBy(x => x.Item.FactId, StringComparer.Ordinal)
                .Select(x => x.Item.FactId)
                .ToArray();

            var eligibleIndex = Array.IndexOf(unfilteredRemediationOrder, eligibleFact.Id);
            Assert.True(
                eligibleIndex >= PracticeSelectionEvidenceRequest.CandidateWindowSize,
                $"Precondition failed: eligible index was {eligibleIndex}, expected >= {PracticeSelectionEvidenceRequest.CandidateWindowSize}.");

            Assert.DoesNotContain(
                eligibleFact.Id,
                unfilteredRemediationOrder.Take(PracticeSelectionEvidenceRequest.CandidateWindowSize));

            var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

            Assert.Contains(evidence.RemediationCandidates, c => c.Fact.Id == eligibleFact.Id);
            Assert.True(evidence.RemediationCandidates.Count <= PracticeSelectionEvidenceRequest.CandidateWindowSize);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_AdversarialMaintenancePool_65IneligibleRows_DoNotPoisonEligibleRow()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1); // ceiling = 4
            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();

            // 70 high facts: mul:2*x where x = 3..72. due > 100, lastReview = 1 (pos = 50, so 50 >= 1 + 40).
            for (var right = 3; right <= 72; right++)
            {
                var fact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, right);
                AddSeedRow(seedData, fact, duePos: 100, lastReviewPos: 1, needsRemediation: false);
            }

            // 1 low fact: mul:2*2. due = 100, lastReview = 2.
            var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2);
            AddSeedRow(seedData, eligibleFact, duePos: 100, lastReviewPos: 2, needsRemediation: false);

            await SeedItemAndFsrsAsync(dbPath, seedData);

            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 50,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [eligibleFact],
                introductionFrontier: [eligibleFact],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            // Mechanically prove adversarial precondition:
            // ORDER BY card.last_review_practice_position ASC, card.due_practice_position ASC, item.fact_id ASC
            var unfilteredMaintOrder = seedData
                .Where(x => !x.Item.NeedsRemediation
                    && x.Fsrs is not null
                    && x.Fsrs.DuePracticePosition > request.ProspectivePracticePosition
                    && x.Fsrs.LastReviewPracticePosition is not null
                    && request.ProspectivePracticePosition >= x.Fsrs.LastReviewPracticePosition.Value + 40)
                .OrderBy(x => x.Fsrs!.LastReviewPracticePosition!.Value)
                .ThenBy(x => x.Fsrs!.DuePracticePosition)
                .ThenBy(x => x.Item.FactId, StringComparer.Ordinal)
                .Select(x => x.Item.FactId)
                .ToArray();

            var eligibleIndex = Array.IndexOf(unfilteredMaintOrder, eligibleFact.Id);
            Assert.True(
                eligibleIndex >= PracticeSelectionEvidenceRequest.CandidateWindowSize,
                $"Precondition failed: eligible index was {eligibleIndex}, expected >= {PracticeSelectionEvidenceRequest.CandidateWindowSize}.");

            Assert.DoesNotContain(
                eligibleFact.Id,
                unfilteredMaintOrder.Take(PracticeSelectionEvidenceRequest.CandidateWindowSize));

            var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

            Assert.Contains(evidence.MaintenanceCandidates, c => c.Fact.Id == eligibleFact.Id);
            Assert.True(evidence.MaintenanceCandidates.Count <= PracticeSelectionEvidenceRequest.CandidateWindowSize);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_AdversarialEarlyReviewPool_65IneligibleRows_DoNotPoisonEligibleRow()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1); // ceiling = 4
            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();

            // 70 high facts: mul:2*x where x = 3..72. due = 100, lastReview = 1.
            for (var right = 3; right <= 72; right++)
            {
                var fact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, right);
                AddSeedRow(seedData, fact, duePos: 100, lastReviewPos: 1, needsRemediation: false);
            }

            // 1 low fact: mul:2*2. due = 100, lastReview = 2.
            var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2);
            AddSeedRow(seedData, eligibleFact, duePos: 100, lastReviewPos: 2, needsRemediation: false);

            await SeedItemAndFsrsAsync(dbPath, seedData);

            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [eligibleFact],
                introductionFrontier: [eligibleFact],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            // Mechanically prove adversarial precondition:
            // ORDER BY CASE WHEN card.last_review_practice_position IS NULL THEN 0 ELSE 1 END ASC,
            //          card.last_review_practice_position ASC, card.due_practice_position ASC, item.fact_id ASC
            var unfilteredEarlyOrder = seedData
                .Where(x => !x.Item.NeedsRemediation
                    && x.Fsrs is not null
                    && x.Fsrs.DuePracticePosition > request.ProspectivePracticePosition)
                .OrderBy(x => x.Fsrs?.LastReviewPracticePosition is null ? 0 : 1)
                .ThenBy(x => x.Fsrs?.LastReviewPracticePosition ?? 0)
                .ThenBy(x => x.Fsrs!.DuePracticePosition)
                .ThenBy(x => x.Item.FactId, StringComparer.Ordinal)
                .Select(x => x.Item.FactId)
                .ToArray();

            var eligibleIndex = Array.IndexOf(unfilteredEarlyOrder, eligibleFact.Id);
            Assert.True(
                eligibleIndex >= PracticeSelectionEvidenceRequest.CandidateWindowSize,
                $"Precondition failed: eligible index was {eligibleIndex}, expected >= {PracticeSelectionEvidenceRequest.CandidateWindowSize}.");

            Assert.DoesNotContain(
                eligibleFact.Id,
                unfilteredEarlyOrder.Take(PracticeSelectionEvidenceRequest.CandidateWindowSize));

            var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

            Assert.Contains(evidence.EarlyReviewCandidates, c => c.Fact.Id == eligibleFact.Id);
            Assert.True(evidence.EarlyReviewCandidates.Count <= PracticeSelectionEvidenceRequest.CandidateWindowSize);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_AdversarialCurrentBandMaterialized_65IneligibleRows_DoNotPoisonEligibleRow()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1); // ceiling = 4
            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            var frontierFacts = new List<ArithmeticFact>();

            // 70 high facts: mul:2*x where x = 10..79.
            // Notice: string "mul:2*10" to "mul:2*79" sort alphabetically BEFORE "mul:3*1".
            for (var right = 10; right <= 79; right++)
            {
                var fact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, right); // 20..158 > 4
                frontierFacts.Add(fact);
                AddSeedRow(seedData, fact, duePos: 100, lastReviewPos: 1);
            }

            // 1 low fact: mul:3*1 (result 3 <= 4).
            var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 3, 1);
            frontierFacts.Add(eligibleFact);
            AddSeedRow(seedData, eligibleFact, duePos: 100, lastReviewPos: 1);

            await SeedItemAndFsrsAsync(dbPath, seedData);

            // Verify adversarial ordering in SQL: ORDER BY item.fact_id ASC
            var sortedFrontierIds = frontierFacts
                .Select(f => f.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            var eligibleIndex = Array.IndexOf(sortedFrontierIds, eligibleFact.Id);
            Assert.True(
                eligibleIndex >= PracticeSelectionEvidenceRequest.CandidateWindowSize,
                $"Precondition failed: eligible index in sorted frontier was {eligibleIndex}, expected >= {PracticeSelectionEvidenceRequest.CandidateWindowSize}.");

            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: frontierFacts,
                introductionFrontier: frontierFacts,
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

            Assert.Contains(evidence.CurrentBandCandidates, c => c.Fact.Id == eligibleFact.Id);
            Assert.DoesNotContain(evidence.CurrentBandCandidates, c => c.Fact.Id != eligibleFact.Id);
            Assert.True(evidence.CurrentBandCandidates.Count <= PracticeSelectionEvidenceRequest.CandidateWindowSize);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    // ---------------------------------------------------------------------------
    // Snapshot / SQLite Parity Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Snapshot_Sqlite_Parity_ExposesEquivalentFactIdentities()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1); // ceiling = 4

            var eligibleDue = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2); // 4 <= 4
            var eligibleMaint = new ArithmeticFact(ArithmeticOperation.Multiplication, 1, 3); // 3 <= 4
            var eligibleRemed = new ArithmeticFact(ArithmeticOperation.Multiplication, 1, 4); // 4 <= 4
            var eligibleEarly = new ArithmeticFact(ArithmeticOperation.Multiplication, 1, 2); // 2 <= 4
            var eligibleFrontier = new ArithmeticFact(ArithmeticOperation.Multiplication, 1, 1); // 1 <= 4

            var highDue = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 5); // 10 > 4
            var highMaint = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 6); // 12 > 4
            var highRemed = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 7); // 14 > 4
            var highEarly = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 8); // 16 > 4
            var highFrontier = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 9); // 18 > 4

            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
            var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);

            void Register(ArithmeticFact fact, long duePos, long lastReviewPos, bool needsRemed = false)
            {
                AddSeedRow(seedData, fact, duePos, lastReviewPos, needsRemed);
                AddCandidate(itemStates, fsrsStates, fact, duePos, lastReviewPos, needsRemed);
            }

            Register(eligibleDue, duePos: 1, lastReviewPos: 1);
            Register(eligibleMaint, duePos: 100, lastReviewPos: 1);
            Register(eligibleRemed, duePos: 100, lastReviewPos: 1, needsRemed: true);
            Register(eligibleEarly, duePos: 100, lastReviewPos: 1);
            Register(eligibleFrontier, duePos: 100, lastReviewPos: 1);

            Register(highDue, duePos: 1, lastReviewPos: 1);
            Register(highMaint, duePos: 100, lastReviewPos: 1);
            Register(highRemed, duePos: 100, lastReviewPos: 1, needsRemed: true);
            Register(highEarly, duePos: 100, lastReviewPos: 1);
            Register(highFrontier, duePos: 100, lastReviewPos: 1);

            await SeedItemAndFsrsAsync(dbPath, seedData);

            var frontier = new[] { eligibleFrontier, highFrontier };
            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 50,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: frontier,
                introductionFrontier: frontier,
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            var snapshot = CreateSnapshot(itemStates, fsrsStates);
            var snapshotEvidence = PracticeSelectionEvidence.FromSnapshot(snapshot, request);
            var sqliteEvidence = await store.LoadPracticeSelectionEvidenceAsync(request);

            // Assert exact parity of fact identities per semantic pool
            AssertSameFactIds(snapshotEvidence.DueCandidates, sqliteEvidence.DueCandidates);
            AssertSameFactIds(snapshotEvidence.MaintenanceCandidates, sqliteEvidence.MaintenanceCandidates);
            AssertSameFactIds(snapshotEvidence.RemediationCandidates, sqliteEvidence.RemediationCandidates);
            AssertSameFactIds(snapshotEvidence.EarlyReviewCandidates, sqliteEvidence.EarlyReviewCandidates);
            AssertSameFactIds(snapshotEvidence.CurrentBandCandidates, sqliteEvidence.CurrentBandCandidates);

            // Assert exclusion of all high facts
            var highIds = new[] { highDue.Id, highMaint.Id, highRemed.Id, highEarly.Id, highFrontier.Id };
            foreach (var highId in highIds)
            {
                Assert.DoesNotContain(sqliteEvidence.DueCandidates, c => c.Fact.Id == highId);
                Assert.DoesNotContain(sqliteEvidence.MaintenanceCandidates, c => c.Fact.Id == highId);
                Assert.DoesNotContain(sqliteEvidence.RemediationCandidates, c => c.Fact.Id == highId);
                Assert.DoesNotContain(sqliteEvidence.EarlyReviewCandidates, c => c.Fact.Id == highId);
                Assert.DoesNotContain(sqliteEvidence.CurrentBandCandidates, c => c.Fact.Id == highId);
            }
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    // ---------------------------------------------------------------------------
    // Historical State Preservation (Read-Only Persistence) Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sqlite_HistoricalStatePreserved_EvidenceLoadingIsReadOnly()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var highFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 5, 5); // 25
            var highItem = ItemLearningState.CreateNew(highFact);
            highItem.TotalAttempts = 15;
            highItem.CorrectAttempts = 14;
            highItem.IncorrectAttempts = 1;
            highItem.ConsecutiveCorrectStreak = 10;
            highItem.LastLatencyMs = 600;
            highItem.RollingLatencyMs = 650;
            highItem.FluentStreak = 5;
            highItem.IsProvisionallyMastered = true;
            highItem.NeedsRemediation = false;
            highItem.LastPracticedOrder = 42;
            highItem.LastPracticedAt = DateTimeOffset.UtcNow;

            var cardId = Guid.NewGuid();
            var highFsrs = new FsrsCardState(
                highFact.Id,
                cardId,
                State: 2,
                Step: null,
                Stability: 25.5,
                Difficulty: 4.2,
                DuePracticePosition: 1,
                LastReviewPracticePosition: 1,
                LastRating: FsrsRating.Good);

            await SeedItemAndFsrsAsync(dbPath, [(highItem, highFsrs)]);

            // Capture database state before loading evidence
            var (itemBefore, fsrsBefore, attemptsCountBefore, revisionBefore) = await ReadRawFactStateAsync(dbPath, highFact.Id);

            var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1); // ceiling = 4
            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [highFact],
                introductionFrontier: [highFact],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

            // Fact is absent from all pools
            Assert.Empty(evidence.DueCandidates);
            Assert.Empty(evidence.MaintenanceCandidates);
            Assert.Empty(evidence.RemediationCandidates);
            Assert.Empty(evidence.EarlyReviewCandidates);
            Assert.Empty(evidence.CurrentBandCandidates);

            // Verify database state after loading evidence: byte/field equivalent
            var (itemAfter, fsrsAfter, attemptsCountAfter, revisionAfter) = await ReadRawFactStateAsync(dbPath, highFact.Id);

            Assert.Equal(itemBefore.TotalAttempts, itemAfter.TotalAttempts);
            Assert.Equal(itemBefore.CorrectAttempts, itemAfter.CorrectAttempts);
            Assert.Equal(itemBefore.ConsecutiveCorrectStreak, itemAfter.ConsecutiveCorrectStreak);
            Assert.Equal(itemBefore.LastLatencyMs, itemAfter.LastLatencyMs);
            Assert.Equal(itemBefore.RollingLatencyMs, itemAfter.RollingLatencyMs);
            Assert.Equal(itemBefore.IsProvisionallyMastered, itemAfter.IsProvisionallyMastered);
            Assert.Equal(itemBefore.NeedsRemediation, itemAfter.NeedsRemediation);
            Assert.Equal(itemBefore.LastPracticedOrder, itemAfter.LastPracticedOrder);

            Assert.Equal(fsrsBefore.CardId, fsrsAfter.CardId);
            Assert.Equal(fsrsBefore.Stability, fsrsAfter.Stability);
            Assert.Equal(fsrsBefore.Difficulty, fsrsAfter.Difficulty);
            Assert.Equal(fsrsBefore.DuePracticePosition, fsrsAfter.DuePracticePosition);
            Assert.Equal(fsrsBefore.LastReviewPracticePosition, fsrsAfter.LastReviewPracticePosition);

            Assert.Equal(attemptsCountBefore, attemptsCountAfter);
            Assert.Equal(revisionBefore, revisionAfter);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    // ---------------------------------------------------------------------------
    // Reactivation Tests (No Database Writes)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sqlite_CeilingIncrease_ReactivatesWithoutDatabaseWrite()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var additionCurriculum = new ArithmeticCurriculum().Addition;
            var mediumFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 3); // 6

            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            AddSeedRow(seedData, mediumFact, duePos: 1, lastReviewPos: 1);
            await SeedItemAndFsrsAsync(dbPath, seedData);

            // 1. Lower ceiling: BandIndex 1 has ceiling 4. mediumFact (6) is blocked.
            var lowerGate = GuidedNumberSpaceGate.ForGuided(additionCurriculum, 1);
            var lowerRequest = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [mediumFact],
                introductionFrontier: [mediumFact],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: lowerGate);

            var lowerEvidence = await store.LoadPracticeSelectionEvidenceAsync(lowerRequest);
            Assert.DoesNotContain(lowerEvidence.DueCandidates, c => c.Fact.Id == mediumFact.Id);

            // 2. Higher ceiling: BandIndex 2 has ceiling 6. mediumFact (6) is now allowed.
            // No writes in between.
            var higherGate = GuidedNumberSpaceGate.ForGuided(additionCurriculum, 2);
            var higherRequest = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [mediumFact],
                introductionFrontier: [mediumFact],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: higherGate);

            var higherEvidence = await store.LoadPracticeSelectionEvidenceAsync(higherRequest);
            Assert.Contains(higherEvidence.DueCandidates, c => c.Fact.Id == mediumFact.Id);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_Unrestricted_ReactivatesWithoutDatabaseWrite()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var highFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 9, 9); // 81
            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            AddSeedRow(seedData, highFact, duePos: 1, lastReviewPos: 1);
            await SeedItemAndFsrsAsync(dbPath, seedData);

            // 1. Guided request blocks the high fact.
            var guidedGate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1);
            var guidedRequest = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [highFact],
                introductionFrontier: [highFact],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: guidedGate);

            var guidedEvidence = await store.LoadPracticeSelectionEvidenceAsync(guidedRequest);
            Assert.DoesNotContain(guidedEvidence.DueCandidates, c => c.Fact.Id == highFact.Id);

            // 2. Unrestricted request allows it again. No database writes.
            var unrestrictedRequest = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [highFact],
                introductionFrontier: [highFact],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: GuidedNumberSpaceGate.Unrestricted);

            var unrestrictedEvidence = await store.LoadPracticeSelectionEvidenceAsync(unrestrictedRequest);
            Assert.Contains(unrestrictedEvidence.DueCandidates, c => c.Fact.Id == highFact.Id);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    // ---------------------------------------------------------------------------
    // Operation Boundary Tests (Subtraction & Addition)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SubtractionBoundary_HighFactRemainsEvidenceEligible()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var subFact = new ArithmeticFact(ArithmeticOperation.Subtraction, 100, 1); // 100 - 1 = 99
            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            AddSeedRow(seedData, subFact, duePos: 1, lastReviewPos: 1);
            await SeedItemAndFsrsAsync(dbPath, seedData);

            var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 0); // ceiling = 2
            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Subtraction,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [subFact],
                introductionFrontier: [subFact],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            // Both SQLite and snapshot must allow Subtraction regardless of ceiling
            var sqliteEvidence = await store.LoadPracticeSelectionEvidenceAsync(request);
            Assert.Contains(sqliteEvidence.DueCandidates, c => c.Fact.Id == subFact.Id);

            var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
            var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);
            AddCandidate(itemStates, fsrsStates, subFact, duePos: 1, lastReviewPos: 1);
            var snapshot = CreateSnapshot(itemStates, fsrsStates);

            var snapshotEvidence = PracticeSelectionEvidence.FromSnapshot(snapshot, request);
            Assert.Contains(snapshotEvidence.DueCandidates, c => c.Fact.Id == subFact.Id);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task AdditionBoundary_HighFactRemainsEvidenceEligible()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var addFact = new ArithmeticFact(ArithmeticOperation.Addition, 50, 50); // 100
            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            AddSeedRow(seedData, addFact, duePos: 1, lastReviewPos: 1);
            await SeedItemAndFsrsAsync(dbPath, seedData);

            var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 0); // ceiling = 2
            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Addition,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [addFact],
                introductionFrontier: [addFact],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            var sqliteEvidence = await store.LoadPracticeSelectionEvidenceAsync(request);
            Assert.Contains(sqliteEvidence.DueCandidates, c => c.Fact.Id == addFact.Id);

            var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
            var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);
            AddCandidate(itemStates, fsrsStates, addFact, duePos: 1, lastReviewPos: 1);
            var snapshot = CreateSnapshot(itemStates, fsrsStates);

            var snapshotEvidence = PracticeSelectionEvidence.FromSnapshot(snapshot, request);
            Assert.Contains(snapshotEvidence.DueCandidates, c => c.Fact.Id == addFact.Id);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Division_HighFactRespectsCeiling()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var eligibleDiv = new ArithmeticFact(ArithmeticOperation.Division, 4, 2); // dividend 4 <= 4
            var highDiv = new ArithmeticFact(ArithmeticOperation.Division, 6, 2); // dividend 6 > 4

            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            AddSeedRow(seedData, eligibleDiv, duePos: 1, lastReviewPos: 1);
            AddSeedRow(seedData, highDiv, duePos: 1, lastReviewPos: 1);
            await SeedItemAndFsrsAsync(dbPath, seedData);

            var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1); // ceiling = 4
            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Division,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [eligibleDiv, highDiv],
                introductionFrontier: [eligibleDiv, highDiv],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            var sqliteEvidence = await store.LoadPracticeSelectionEvidenceAsync(request);
            Assert.Contains(sqliteEvidence.DueCandidates, c => c.Fact.Id == eligibleDiv.Id);
            Assert.DoesNotContain(sqliteEvidence.DueCandidates, c => c.Fact.Id == highDiv.Id);

            var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
            var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);
            AddCandidate(itemStates, fsrsStates, eligibleDiv, duePos: 1, lastReviewPos: 1);
            AddCandidate(itemStates, fsrsStates, highDiv, duePos: 1, lastReviewPos: 1);
            var snapshot = CreateSnapshot(itemStates, fsrsStates);

            var snapshotEvidence = PracticeSelectionEvidence.FromSnapshot(snapshot, request);
            Assert.Contains(snapshotEvidence.DueCandidates, c => c.Fact.Id == eligibleDiv.Id);
            Assert.DoesNotContain(snapshotEvidence.DueCandidates, c => c.Fact.Id == highDiv.Id);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_BoundedCandidateWindow_NeverExceeds64()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            // 80 eligible facts: mul:1*x where x = 1..80, product 1..80 <= 100.
            for (var right = 1; right <= 80; right++)
            {
                var fact = new ArithmeticFact(ArithmeticOperation.Multiplication, 1, right);
                AddSeedRow(seedData, fact, duePos: 1, lastReviewPos: 1);
            }

            await SeedItemAndFsrsAsync(dbPath, seedData);

            var gate = GuidedNumberSpaceGate.Unrestricted;
            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [],
                introductionFrontier: [],
                currentBandIndex: int.MaxValue,
                guidedNumberSpaceGate: gate);

            var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);
            Assert.Equal(PracticeSelectionEvidenceRequest.CandidateWindowSize, evidence.DueCandidates.Count);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    // ---------------------------------------------------------------------------
    // Helper Methods
    // ---------------------------------------------------------------------------

    private static string GetTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"mathfirst_guided_gate_test_{Guid.NewGuid():N}.db");

    private static void TryDeleteDatabase(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static void AddSeedRow(
        List<(ItemLearningState Item, FsrsCardState? Fsrs)> seedData,
        ArithmeticFact fact,
        long duePos,
        long lastReviewPos,
        bool needsRemediation = false)
    {
        var item = ItemLearningState.CreateNew(fact);
        item.NeedsRemediation = needsRemediation;
        var fsrs = new FsrsCardState(
            fact.Id,
            Guid.NewGuid(),
            State: 2,
            Step: null,
            Stability: 10.0,
            Difficulty: 5.0,
            DuePracticePosition: duePos,
            LastReviewPracticePosition: lastReviewPos,
            LastRating: FsrsRating.Good);
        seedData.Add((item, fsrs));
    }

    private static void AddCandidate(
        Dictionary<string, ItemLearningState> itemStates,
        Dictionary<string, FsrsCardState> fsrsStates,
        ArithmeticFact fact,
        long duePos,
        long lastReviewPos,
        bool needsRemediation = false)
    {
        var item = ItemLearningState.CreateNew(fact);
        item.NeedsRemediation = needsRemediation;
        itemStates[fact.Id] = item;
        fsrsStates[fact.Id] = new FsrsCardState(
            fact.Id,
            Guid.NewGuid(),
            State: 2,
            Step: null,
            Stability: 10.0,
            Difficulty: 5.0,
            DuePracticePosition: duePos,
            LastReviewPracticePosition: lastReviewPos,
            LastRating: FsrsRating.Good);
    }

    private static LearnerSnapshot CreateSnapshot(
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        IReadOnlyDictionary<string, FsrsCardState> fsrsStates)
    {
        var progression = new LearnerProgression
        {
            SchemaVersion = 6,
            StoreRevision = 1,
            PracticePosition = 50,
            OperationProgressions = Enum.GetValues<ArithmeticOperation>().ToDictionary(
                o => o,
                o => new OperationProgression(o, 0, 0))
        };

        return new LearnerSnapshot(
            progression,
            itemStates,
            fsrsStates,
            [],
            1,
            6);
    }

    private static async Task SeedItemAndFsrsAsync(
        string dbPath,
        IEnumerable<(ItemLearningState Item, FsrsCardState? Fsrs)> seedData)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();
        foreach (var (item, fsrs) in seedData)
        {
            using (var itemCmd = conn.CreateCommand())
            {
                itemCmd.Transaction = tx;
                itemCmd.CommandText = @"
                    INSERT INTO item_learning_state (
                        fact_id, operation, left_operand, right_operand,
                        total_attempts, correct_attempts, incorrect_attempts,
                        consecutive_correct, last_latency_ms, rolling_latency_ms,
                        fluent_streak, is_mastered, needs_remediation,
                        remediation_due_order, last_practiced_order, last_practiced_at
                    ) VALUES (
                        @fact_id, @operation, @left_operand, @right_operand,
                        @total_attempts, @correct_attempts, @incorrect_attempts,
                        @consecutive_correct, @last_latency_ms, @rolling_latency_ms,
                        @fluent_streak, @is_mastered, @needs_remediation,
                        @remediation_due_order, @last_practiced_order, @last_practiced_at
                    );";
                itemCmd.Parameters.AddWithValue("@fact_id", item.FactId);
                itemCmd.Parameters.AddWithValue("@operation", item.Operation.ToString());
                itemCmd.Parameters.AddWithValue("@left_operand", item.LeftOperand);
                itemCmd.Parameters.AddWithValue("@right_operand", item.RightOperand);
                itemCmd.Parameters.AddWithValue("@total_attempts", item.TotalAttempts);
                itemCmd.Parameters.AddWithValue("@correct_attempts", item.CorrectAttempts);
                itemCmd.Parameters.AddWithValue("@incorrect_attempts", item.IncorrectAttempts);
                itemCmd.Parameters.AddWithValue("@consecutive_correct", item.ConsecutiveCorrectStreak);
                itemCmd.Parameters.AddWithValue("@last_latency_ms", item.LastLatencyMs);
                itemCmd.Parameters.AddWithValue("@rolling_latency_ms", item.RollingLatencyMs);
                itemCmd.Parameters.AddWithValue("@fluent_streak", item.FluentStreak);
                itemCmd.Parameters.AddWithValue("@is_mastered", item.IsProvisionallyMastered ? 1 : 0);
                itemCmd.Parameters.AddWithValue("@needs_remediation", item.NeedsRemediation ? 1 : 0);
                itemCmd.Parameters.AddWithValue("@remediation_due_order", item.RemediationDueOrder);
                itemCmd.Parameters.AddWithValue("@last_practiced_order", item.LastPracticedOrder);
                itemCmd.Parameters.AddWithValue("@last_practiced_at", (object?)item.LastPracticedAt?.ToString("O") ?? DBNull.Value);
                await itemCmd.ExecuteNonQueryAsync();
            }

            if (fsrs is not null)
            {
                using var fsrsCmd = conn.CreateCommand();
                fsrsCmd.Transaction = tx;
                fsrsCmd.CommandText = @"
                    INSERT INTO fsrs_card_state (
                        fact_id, card_id, state, step, stability, difficulty,
                        due_practice_position, last_review_practice_position, last_rating
                    ) VALUES (
                        @fact_id, @card_id, @state, @step, @stability, @difficulty,
                        @due_practice_position, @last_review_practice_position, @last_rating
                    );";
                fsrsCmd.Parameters.AddWithValue("@fact_id", fsrs.FactId);
                fsrsCmd.Parameters.AddWithValue("@card_id", fsrs.CardId.ToString());
                fsrsCmd.Parameters.AddWithValue("@state", fsrs.State);
                fsrsCmd.Parameters.AddWithValue("@step", (object?)fsrs.Step ?? DBNull.Value);
                fsrsCmd.Parameters.AddWithValue("@stability", (object?)fsrs.Stability ?? DBNull.Value);
                fsrsCmd.Parameters.AddWithValue("@difficulty", (object?)fsrs.Difficulty ?? DBNull.Value);
                fsrsCmd.Parameters.AddWithValue("@due_practice_position", fsrs.DuePracticePosition);
                fsrsCmd.Parameters.AddWithValue("@last_review_practice_position", (object?)fsrs.LastReviewPracticePosition ?? DBNull.Value);
                fsrsCmd.Parameters.AddWithValue("@last_rating", (object?)(int?)fsrs.LastRating ?? DBNull.Value);
                await fsrsCmd.ExecuteNonQueryAsync();
            }
        }
        await tx.CommitAsync();
    }

    private static async Task<(ItemLearningState Item, FsrsCardState Fsrs, int AttemptsCount, long Revision)> ReadRawFactStateAsync(
        string dbPath,
        string factId)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        long revision = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'store_revision';";
            var scalar = await cmd.ExecuteScalarAsync();
            if (scalar is not null)
            {
                revision = long.Parse((string)scalar);
            }
        }

        int attemptsCount = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM attempt_history WHERE fact_id = @fact_id;";
            cmd.Parameters.AddWithValue("@fact_id", factId);
            attemptsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        ItemLearningState? item = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT fact_id, operation, left_operand, right_operand, total_attempts, correct_attempts, incorrect_attempts, consecutive_correct, last_latency_ms, rolling_latency_ms, fluent_streak, is_mastered, needs_remediation, remediation_due_order, last_practiced_order, last_practiced_at FROM item_learning_state WHERE fact_id = @fact_id;";
            cmd.Parameters.AddWithValue("@fact_id", factId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                item = new ItemLearningState
                {
                    FactId = reader.GetString(0),
                    Operation = Enum.Parse<ArithmeticOperation>(reader.GetString(1)),
                    LeftOperand = reader.GetInt32(2),
                    RightOperand = reader.GetInt32(3),
                    TotalAttempts = reader.GetInt32(4),
                    CorrectAttempts = reader.GetInt32(5),
                    IncorrectAttempts = reader.GetInt32(6),
                    ConsecutiveCorrectStreak = reader.GetInt32(7),
                    LastLatencyMs = reader.GetInt64(8),
                    RollingLatencyMs = reader.GetInt64(9),
                    FluentStreak = reader.GetInt32(10),
                    IsProvisionallyMastered = reader.GetInt32(11) == 1,
                    NeedsRemediation = reader.GetInt32(12) == 1,
                    RemediationDueOrder = reader.GetInt32(13),
                    LastPracticedOrder = reader.GetInt32(14),
                    LastPracticedAt = reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15))
                };
            }
        }

        FsrsCardState? fsrs = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT fact_id, card_id, state, step, stability, difficulty, due_practice_position, last_review_practice_position, last_rating FROM fsrs_card_state WHERE fact_id = @fact_id;";
            cmd.Parameters.AddWithValue("@fact_id", factId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                fsrs = new FsrsCardState(
                    reader.GetString(0),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetInt64(7),
                    reader.IsDBNull(8) ? null : (FsrsRating)reader.GetInt32(8));
            }
        }

        return (item!, fsrs!, attemptsCount, revision);
    }

    private static void AssertSameFactIds(
        IReadOnlyList<PracticeSelectionCandidate> expected,
        IReadOnlyList<PracticeSelectionCandidate> actual)
    {
        var expectedSet = expected.Select(c => c.Fact.Id).ToHashSet(StringComparer.Ordinal);
        var actualSet = actual.Select(c => c.Fact.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedSet, actualSet);
    }
}
