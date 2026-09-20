namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// Task 2 RED tests for snapshot fallback eligibility and bounded-window parity.
/// These tests verify that PracticeSelectionEvidence.FromSnapshot filters ineligible
/// (future-band) facts BEFORE constructing semantic review pools and truncating
/// to CandidateWindowSize.
/// </summary>
public sealed class FactEligibilityRegressionTests
{
    // ---------------------------------------------------------------------------
    // Constructor validation
    // ---------------------------------------------------------------------------

    [Fact]
    public void NegativeCurrentBandIndex_ThrowsArgumentOutOfRangeException()
    {
        var curriculum = new ArithmeticCurriculum();
        var ownedFrontier = new AcquisitionOwnershipResolver(curriculum.Multiplication)
            .GetOwnedFrontier(1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: ownedFrontier,
                introductionFrontier: ownedFrontier,
                currentBandIndex: -1));
    }

    // ---------------------------------------------------------------------------
    // Test 1: Future due fact is excluded from evidence pools
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Regression: mul:2*8 is owned by BandIndex 7 (MUL-D08).
    /// At BandIndex 1, mul:2*8 is a future locked fact and must NOT appear
    /// in any review candidate pool produced by FromSnapshot.
    /// The item state is deliberately kept in the snapshot to prove dormancy,
    /// not deletion.
    /// </summary>
    [Fact]
    public void Snapshot_FutureDueFact_ExcludedFromEvidencePools()
    {
        var curriculum = new ArithmeticCurriculum();
        var mulCurriculum = curriculum.Multiplication;

        // Current progression: BandIndex 1 (MUL-D02, max operand 2).
        const int currentBandIndex = 1;
        var ownership = new AcquisitionOwnershipResolver(mulCurriculum);

        // Verify the canonical owner of mul:2*8 is BandIndex 7.
        Assert.True(ownership.TryGetOwner("mul:2*8", 11, out var ownerBandIndex));
        Assert.Equal(7, ownerBandIndex);
        Assert.False(ownership.IsEligible("mul:2*8", currentBandIndex));

        // Build a snapshot that includes mul:2*8 as a materialized item with a due FSRS state.
        var futureFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 8);
        var futureItemState = ItemLearningState.CreateNew(futureFact);

        // FSRS state: due at position 1 (well due), so it would appear in DueCandidates
        // under the old unfiltered implementation.
        var futureFsrsState = new FsrsCardState(
            FactId: futureFact.Id,
            CardId: Guid.NewGuid(),
            State: 2,
            Step: null,
            Stability: 30.0,
            Difficulty: 5.0,
            DuePracticePosition: 1,
            LastReviewPracticePosition: 1,
            LastRating: FsrsRating.Good);

        // Also add one eligible fact from BandIndex 1 so the snapshot is non-empty
        // and CurrentBandCandidates works correctly.
        var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2);
        var eligibleItemState = ItemLearningState.CreateNew(eligibleFact);
        var eligibleFsrsState = new FsrsCardState(
            FactId: eligibleFact.Id,
            CardId: Guid.NewGuid(),
            State: 2,
            Step: null,
            Stability: 5.0,
            Difficulty: 5.0,
            DuePracticePosition: 1,
            LastReviewPracticePosition: 1,
            LastRating: FsrsRating.Good);

        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [futureFact.Id] = futureItemState,
            [eligibleFact.Id] = eligibleItemState,
        };

        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [futureFact.Id] = futureFsrsState,
            [eligibleFact.Id] = eligibleFsrsState,
        };

        var progression = new LearnerProgression
        {
            PracticePosition = 10,
            OperationProgressions = Enum.GetValues<ArithmeticOperation>()
                .ToDictionary(
                    o => o,
                    o => new OperationProgression(
                        o,
                        o == ArithmeticOperation.Multiplication ? currentBandIndex : 0,
                        0))
        };

        var snapshot = new LearnerSnapshot(
            progression,
            itemStates,
            fsrsStates,
            [],
            1,
            6);

        var ownedFrontier = ownership.GetOwnedFrontier(currentBandIndex);
        var request = new PracticeSelectionEvidenceRequest(
            ArithmeticOperation.Multiplication,
            prospectivePracticePosition: 11,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: ownedFrontier,
            introductionFrontier: ownedFrontier,
            currentBandIndex: currentBandIndex);

        // Act
        var evidence = PracticeSelectionEvidence.FromSnapshot(snapshot, request);

        // Assert: mul:2*8 must be absent from every review pool.
        // The item state must still exist in the snapshot (dormancy, not deletion).
        Assert.Contains(futureFact.Id, snapshot.ItemStates.Keys, StringComparer.Ordinal);

        Assert.DoesNotContain(evidence.DueCandidates, c => c.Fact.Id == futureFact.Id);
        Assert.DoesNotContain(evidence.MaintenanceCandidates, c => c.Fact.Id == futureFact.Id);
        Assert.DoesNotContain(evidence.RemediationCandidates, c => c.Fact.Id == futureFact.Id);
        Assert.DoesNotContain(evidence.EarlyReviewCandidates, c => c.Fact.Id == futureFact.Id);
        Assert.DoesNotContain(evidence.CurrentBandCandidates, c => c.Fact.Id == futureFact.Id);
    }

    // ---------------------------------------------------------------------------
    // Test 2: Eligible earlier fact is retained in evidence pools
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The eligibility fix must not solve future-fact leakage by suppressing
    /// legitimate review of facts from bands &lt;= CurrentBandIndex.
    /// </summary>
    [Fact]
    public void Snapshot_EligibleEarlierFact_RetainedInEvidencePools()
    {
        var curriculum = new ArithmeticCurriculum();
        var mulCurriculum = curriculum.Multiplication;

        // Progress to BandIndex 7, so mul:2*8 (owner=7) is now eligible.
        const int currentBandIndex = 7;
        var ownership = new AcquisitionOwnershipResolver(mulCurriculum);

        // Verify mul:2*8 is eligible at BandIndex 7.
        Assert.True(ownership.IsEligible("mul:2*8", currentBandIndex));

        // Build a snapshot with mul:2*8 as a materialized due fact.
        var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 8);
        var eligibleItemState = ItemLearningState.CreateNew(eligibleFact);

        var eligibleFsrsState = new FsrsCardState(
            FactId: eligibleFact.Id,
            CardId: Guid.NewGuid(),
            State: 2,
            Step: null,
            Stability: 10.0,
            Difficulty: 5.0,
            DuePracticePosition: 1,
            LastReviewPracticePosition: 1,
            LastRating: FsrsRating.Good);

        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [eligibleFact.Id] = eligibleItemState,
        };

        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [eligibleFact.Id] = eligibleFsrsState,
        };

        var progression = new LearnerProgression
        {
            PracticePosition = 100,
            OperationProgressions = Enum.GetValues<ArithmeticOperation>()
                .ToDictionary(
                    o => o,
                    o => new OperationProgression(
                        o,
                        o == ArithmeticOperation.Multiplication ? currentBandIndex : 0,
                        0))
        };

        var snapshot = new LearnerSnapshot(
            progression,
            itemStates,
            fsrsStates,
            [],
            1,
            6);

        var ownedFrontier = ownership.GetOwnedFrontier(currentBandIndex);
        var request = new PracticeSelectionEvidenceRequest(
            ArithmeticOperation.Multiplication,
            prospectivePracticePosition: 101,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: ownedFrontier,
            introductionFrontier: ownedFrontier,
            currentBandIndex: currentBandIndex);

        // Act
        var evidence = PracticeSelectionEvidence.FromSnapshot(snapshot, request);

        // Assert: the eligible fact is present in DueCandidates.
        Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleFact.Id);
    }

    // ---------------------------------------------------------------------------
    // Test 3: Window poisoning -- eligible fact discovered behind 64+ ineligible facts
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Constructs more than CandidateWindowSize (64) ineligible future facts with
    /// a due FSRS state. One eligible fact is placed so that under the PRE-FIX
    /// implementation (Take(64) before eligibility filter), the eligible fact is
    /// starved behind the 160 future facts. The POST-FIX implementation applies
    /// eligibility BEFORE truncation so the eligible fact is always discovered.
    ///
    /// Uses real canonical multiplication curriculum facts from bands 2-11 at
    /// BandIndex 1 to generate the 160 ineligible candidates without inventing
    /// malformed data.
    /// </summary>
    [Fact]
    public void Snapshot_WindowPoisoning_EligibleFactDiscoveredBehind64IneligibleFacts()
    {
        var curriculum = new ArithmeticCurriculum();
        var mulCurriculum = curriculum.Multiplication;

        // Current progression: BandIndex 1. All bands >= 2 are future/ineligible.
        const int currentBandIndex = 1;
        var ownership = new AcquisitionOwnershipResolver(mulCurriculum);

        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);

        // Collect canonical future facts from bands 2-11.
        // Bands 2-11 have 7+9+11+13+15+17+19+21+23+25 = 160 facts total.
        // Use DuePracticePosition = 1 for ALL of them (all maximally due).
        var futureFacts = new List<ArithmeticFact>();
        for (var bandIdx = 2; bandIdx <= 11; bandIdx++)
        {
            if (!mulCurriculum.TryGetBand(bandIdx, out var band) || band is null)
                continue;

            foreach (var fact in band.Frontier)
            {
                // Skip if already added (owned by earlier band in resolver).
                if (itemStates.ContainsKey(fact.Id))
                    continue;

                // Verify this fact is genuinely ineligible at BandIndex 1.
                if (ownership.IsEligible(fact.Id, currentBandIndex))
                    continue;

                futureFacts.Add(fact);
                itemStates[fact.Id] = ItemLearningState.CreateNew(fact);
                fsrsStates[fact.Id] = new FsrsCardState(
                    FactId: fact.Id,
                    CardId: Guid.NewGuid(),
                    State: 2,
                    Step: null,
                    Stability: 30.0,
                    Difficulty: 5.0,
                    DuePracticePosition: 1,
                    LastReviewPracticePosition: 1,
                    LastRating: FsrsRating.Good);
            }
        }

        // Verify we have strictly more than 64 ineligible candidates.
        Assert.True(futureFacts.Count > PracticeSelectionEvidenceRequest.CandidateWindowSize,
            $"Expected >64 ineligible future facts but found {futureFacts.Count}.");

        // Add exactly ONE eligible fact from BandIndex 1.
        // Use mul:2*2 (owned by BandIndex 1).
        // Give it DuePracticePosition = 2 so that under PRE-FIX unfiltered ordering
        // (OrderBy DuePracticePosition, LastReview, Id), all 160 future facts (Due=1)
        // sort ahead of the eligible fact (Due=2), starving the eligible fact out of Take(64).
        // Under POST-FIX, future facts are removed before Take(64), so the eligible fact appears.
        var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2);
        Assert.True(ownership.IsEligible(eligibleFact.Id, currentBandIndex),
            "The chosen eligible fact must be owned at or before currentBandIndex.");

        itemStates[eligibleFact.Id] = ItemLearningState.CreateNew(eligibleFact);
        fsrsStates[eligibleFact.Id] = new FsrsCardState(
            FactId: eligibleFact.Id,
            CardId: Guid.NewGuid(),
            State: 2,
            Step: null,
            Stability: 30.0,
            Difficulty: 5.0,
            DuePracticePosition: 2,
            LastReviewPracticePosition: 1,
            LastRating: FsrsRating.Good);

        var progression = new LearnerProgression
        {
            PracticePosition = 200,
            OperationProgressions = Enum.GetValues<ArithmeticOperation>()
                .ToDictionary(
                    o => o,
                    o => new OperationProgression(
                        o,
                        o == ArithmeticOperation.Multiplication ? currentBandIndex : 0,
                        0))
        };

        var snapshot = new LearnerSnapshot(
            progression,
            itemStates,
            fsrsStates,
            [],
            1,
            6);

        var ownedFrontier = ownership.GetOwnedFrontier(currentBandIndex);
        var request = new PracticeSelectionEvidenceRequest(
            ArithmeticOperation.Multiplication,
            prospectivePracticePosition: 201,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: ownedFrontier,
            introductionFrontier: ownedFrontier,
            currentBandIndex: currentBandIndex);

        // Precondition proof: construct the PRE-FIX unfiltered Due candidate order.
        // Under the pre-fix implementation, candidates were sorted by DuePracticePosition,
        // LastReviewPracticePosition, then FactId (Ordinal) before taking the first 64 items.
        // With future facts at DuePracticePosition = 1 and the eligible fact at DuePracticePosition = 2,
        // all 160 future facts precede the eligible fact, placing the eligible fact at index 160.
        // This explicitly proves that pre-fix Take(64) would have starved the eligible candidate.
        var unfilteredDueOrder = itemStates.Values
            .Where(state => state.Operation == ArithmeticOperation.Multiplication)
            .Select(state => (
                FactId: state.FactId,
                FsrsState: fsrsStates.GetValueOrDefault(state.FactId)))
            .Where(x => x.FsrsState is not null && x.FsrsState.DuePracticePosition <= request.ProspectivePracticePosition)
            .OrderBy(x => x.FsrsState!.DuePracticePosition)
            .ThenBy(x => x.FsrsState?.LastReviewPracticePosition ?? 0)
            .ThenBy(x => x.FactId, StringComparer.Ordinal)
            .Select(x => x.FactId)
            .ToArray();

        var eligibleIndex = Array.IndexOf(unfilteredDueOrder, eligibleFact.Id);
        Assert.True(
            eligibleIndex >= PracticeSelectionEvidenceRequest.CandidateWindowSize,
            $"Adversarial precondition failed: eligible fact index in unfiltered Due order was {eligibleIndex}, expected >= {PracticeSelectionEvidenceRequest.CandidateWindowSize}.");

        Assert.DoesNotContain(
            eligibleFact.Id,
            unfilteredDueOrder.Take(PracticeSelectionEvidenceRequest.CandidateWindowSize));

        // Act
        var evidence = PracticeSelectionEvidence.FromSnapshot(snapshot, request);

        // Assert POST-FIX behavior:
        // 1. The eligible fact IS discovered in DueCandidates.
        Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleFact.Id);

        // 2. None of the ineligible future facts appear in DueCandidates.
        var futureFactIds = futureFacts.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(evidence.DueCandidates, c => futureFactIds.Contains(c.Fact.Id));

        // 3. The bounded window invariant is preserved.
        Assert.True(evidence.DueCandidates.Count <= PracticeSelectionEvidenceRequest.CandidateWindowSize,
            $"DueCandidates must be bounded to {PracticeSelectionEvidenceRequest.CandidateWindowSize} " +
            $"but was {evidence.DueCandidates.Count}.");
        Assert.True(evidence.MaintenanceCandidates.Count <= PracticeSelectionEvidenceRequest.CandidateWindowSize);
        Assert.True(evidence.RemediationCandidates.Count <= PracticeSelectionEvidenceRequest.CandidateWindowSize);
        Assert.True(evidence.EarlyReviewCandidates.Count <= PracticeSelectionEvidenceRequest.CandidateWindowSize);
    }

    // ---------------------------------------------------------------------------
    // CurrentBandCandidates must remain discoverable
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Verifies that eligibility filtering does not accidentally suppress
    /// current-band materialized frontier facts from CurrentBandCandidates.
    /// </summary>
    [Fact]
    public void Snapshot_CurrentBandCandidates_CurrentBandFrontierFactsRemainDiscoverable()
    {
        var curriculum = new ArithmeticCurriculum();
        var mulCurriculum = curriculum.Multiplication;

        const int currentBandIndex = 1;
        var ownership = new AcquisitionOwnershipResolver(mulCurriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(currentBandIndex);

        // Materialize all current-band frontier facts.
        var itemStates = ownedFrontier.ToDictionary(
            f => f.Id,
            f => ItemLearningState.CreateNew(f),
            StringComparer.Ordinal);

        var progression = new LearnerProgression
        {
            PracticePosition = 50,
            OperationProgressions = Enum.GetValues<ArithmeticOperation>()
                .ToDictionary(
                    o => o,
                    o => new OperationProgression(
                        o,
                        o == ArithmeticOperation.Multiplication ? currentBandIndex : 0,
                        0))
        };

        var snapshot = new LearnerSnapshot(
            progression,
            itemStates,
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal),
            [],
            1,
            6);

        var request = new PracticeSelectionEvidenceRequest(
            ArithmeticOperation.Multiplication,
            prospectivePracticePosition: 51,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: ownedFrontier,
            introductionFrontier: ownedFrontier,
            currentBandIndex: currentBandIndex);

        // Act
        var evidence = PracticeSelectionEvidence.FromSnapshot(snapshot, request);

        // Assert: all current-band frontier facts appear in CurrentBandCandidates.
        var currentBandIds = evidence.CurrentBandCandidates.Select(c => c.Fact.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var fact in ownedFrontier)
        {
            Assert.Contains(fact.Id, currentBandIds);
        }
    }

    // ===========================================================================
    // Task 3: SQLite Store Eligibility Streaming & Anti-Poisoning Window Filtering
    // ===========================================================================

    [Fact]
    public async Task Sqlite_Multiplication_2x8_NotReturnedInDuePoolAtBand1()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var curriculum = new ArithmeticCurriculum();
            var mulCurriculum = curriculum.Multiplication;
            const int currentBandIndex = 1;
            var ownership = new AcquisitionOwnershipResolver(mulCurriculum);

            // mul:2*8 is owned by BandIndex 7. At BandIndex 1, it is a future locked fact.
            Assert.True(ownership.TryGetOwner("mul:2*8", 11, out var ownerBandIndex));
            Assert.Equal(7, ownerBandIndex);
            Assert.False(ownership.IsEligible("mul:2*8", currentBandIndex));

            var futureFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 8);
            var futureItem = ItemLearningState.CreateNew(futureFact);
            var futureFsrs = new FsrsCardState(futureFact.Id, Guid.NewGuid(), 2, null, 30.0, 5.0, 1, 1, FsrsRating.Good);

            var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2);
            var eligibleItem = ItemLearningState.CreateNew(eligibleFact);
            var eligibleFsrs = new FsrsCardState(eligibleFact.Id, Guid.NewGuid(), 2, null, 5.0, 5.0, 1, 1, FsrsRating.Good);

            await SeedItemAndFsrsAsync(dbPath, [(futureItem, futureFsrs), (eligibleItem, eligibleFsrs)]);

            var ownedFrontier = ownership.GetOwnedFrontier(currentBandIndex);
            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 11,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: ownedFrontier,
                introductionFrontier: ownedFrontier,
                currentBandIndex: currentBandIndex);

            var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

            Assert.DoesNotContain(evidence.DueCandidates, c => c.Fact.Id == futureFact.Id);
            Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleFact.Id);

            // Verify the future fact remains persisted and dormant in the store.
            var snapshot = await store.LoadSnapshotAsync();
            Assert.Contains(futureFact.Id, snapshot.ItemStates.Keys, StringComparer.Ordinal);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_WindowPoisoning_70FutureDueFacts_DoNotStarveEligibleDueFact()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var curriculum = new ArithmeticCurriculum();
            var mulCurriculum = curriculum.Multiplication;
            const int currentBandIndex = 1;
            var ownership = new AcquisitionOwnershipResolver(mulCurriculum);

            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            var futureFacts = new List<ArithmeticFact>();
            var addedIds = new HashSet<string>(StringComparer.Ordinal);

            // Collect >= 70 canonical future multiplication facts from bands 2..11.
            // Bands 2..11 have 160 facts total.
            for (var bandIdx = 2; bandIdx <= 11; bandIdx++)
            {
                if (!mulCurriculum.TryGetBand(bandIdx, out var band) || band is null)
                    continue;

                foreach (var fact in band.Frontier)
                {
                    if (addedIds.Contains(fact.Id) || ownership.IsEligible(fact.Id, currentBandIndex))
                        continue;

                    addedIds.Add(fact.Id);
                    futureFacts.Add(fact);
                    var item = ItemLearningState.CreateNew(fact);
                    var fsrs = new FsrsCardState(fact.Id, Guid.NewGuid(), 2, null, 30.0, 5.0, 1, 1, FsrsRating.Good);
                    seedData.Add((item, fsrs));
                }
            }

            Assert.True(futureFacts.Count >= 70, $"Expected >= 70 future facts but found {futureFacts.Count}.");

            // Add exactly ONE eligible fact with DuePracticePosition = 2.
            var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2);
            Assert.True(ownership.IsEligible(eligibleFact.Id, currentBandIndex));

            var eligibleItem = ItemLearningState.CreateNew(eligibleFact);
            var eligibleFsrs = new FsrsCardState(eligibleFact.Id, Guid.NewGuid(), 2, null, 30.0, 5.0, 2, 1, FsrsRating.Good);
            seedData.Add((eligibleItem, eligibleFsrs));

            await SeedItemAndFsrsAsync(dbPath, seedData);

            var ownedFrontier = ownership.GetOwnedFrontier(currentBandIndex);
            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 201,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: ownedFrontier,
                introductionFrontier: ownedFrontier,
                currentBandIndex: currentBandIndex);

            // Mechanically prove the adversarial precondition:
            // Under pre-fix SQL Due ordering (ORDER BY due_practice_position ASC, ..., fact_id ASC),
            // all 160 future facts (due=1) sort before the eligible fact (due=2).
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
                $"Precondition failed: eligible index was {eligibleIndex}, expected >= {PracticeSelectionEvidenceRequest.CandidateWindowSize}.");

            Assert.DoesNotContain(
                eligibleFact.Id,
                unfilteredDueOrder.Take(PracticeSelectionEvidenceRequest.CandidateWindowSize));

            // Act
            var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

            // Assert POST-FIX behavior:
            // 1. The eligible fact IS discovered in DueCandidates.
            Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleFact.Id);

            // 2. None of the future facts appear in DueCandidates.
            var futureFactIds = futureFacts.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
            Assert.DoesNotContain(evidence.DueCandidates, c => futureFactIds.Contains(c.Fact.Id));

            // 3. DueCandidates count is bounded to <= 64.
            Assert.True(evidence.DueCandidates.Count <= PracticeSelectionEvidenceRequest.CandidateWindowSize);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Theory]
    [InlineData(ArithmeticOperation.Addition, 0)]
    [InlineData(ArithmeticOperation.Subtraction, 0)]
    [InlineData(ArithmeticOperation.Multiplication, 1)]
    [InlineData(ArithmeticOperation.Division, 0)]
    public async Task Sqlite_AllFourOperations_FutureFactsDormant(ArithmeticOperation operation, int currentBandIndex)
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var curriculum = new ArithmeticCurriculum().GetCurriculum(operation);
            var ownership = new AcquisitionOwnershipResolver(curriculum);

            // Get an eligible fact (owned at currentBandIndex)
            var eligibleFrontier = ownership.GetOwnedFrontier(currentBandIndex);
            var eligibleFact = eligibleFrontier.First();
            Assert.True(ownership.IsEligible(eligibleFact.Id, currentBandIndex));

            // Get a future fact (find first fact in a band > currentBandIndex)
            ArithmeticFact? futureFact = null;
            for (var b = currentBandIndex + 1; ; b++)
            {
                if (!curriculum.TryGetBand(b, out var band) || band is null)
                    break;
                foreach (var f in band.Frontier)
                {
                    if (!ownership.IsEligible(f.Id, currentBandIndex))
                    {
                        futureFact = f;
                        break;
                    }
                }
                if (futureFact is not null) break;
            }
            Assert.NotNull(futureFact);
            Assert.False(ownership.IsEligible(futureFact.Id, currentBandIndex));

            var eligibleItem = ItemLearningState.CreateNew(eligibleFact);
            var futureItem = ItemLearningState.CreateNew(futureFact);

            var eligibleFsrs = new FsrsCardState(eligibleFact.Id, Guid.NewGuid(), 2, null, 10.0, 5.0, 1, 1, FsrsRating.Good);
            var futureFsrs = new FsrsCardState(futureFact.Id, Guid.NewGuid(), 2, null, 10.0, 5.0, 1, 1, FsrsRating.Good);

            await SeedItemAndFsrsAsync(dbPath, [(eligibleItem, eligibleFsrs), (futureItem, futureFsrs)]);

            var request = new PracticeSelectionEvidenceRequest(
                operation,
                prospectivePracticePosition: 10,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: eligibleFrontier,
                introductionFrontier: eligibleFrontier,
                currentBandIndex: currentBandIndex);

            var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

            Assert.DoesNotContain(evidence.DueCandidates, c => c.Fact.Id == futureFact.Id);
            Assert.DoesNotContain(evidence.MaintenanceCandidates, c => c.Fact.Id == futureFact.Id);
            Assert.DoesNotContain(evidence.RemediationCandidates, c => c.Fact.Id == futureFact.Id);
            Assert.DoesNotContain(evidence.EarlyReviewCandidates, c => c.Fact.Id == futureFact.Id);
            Assert.DoesNotContain(evidence.CurrentBandCandidates, c => c.Fact.Id == futureFact.Id);

            Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleFact.Id);

            var snapshot = await store.LoadSnapshotAsync();
            Assert.Contains(futureFact.Id, snapshot.ItemStates.Keys, StringComparer.Ordinal);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task SqliteAndSnapshot_Parity_IdenticalOutcome()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();

            var curriculum = new ArithmeticCurriculum();
            var mulCurriculum = curriculum.Multiplication;
            const int currentBandIndex = 1;
            var ownership = new AcquisitionOwnershipResolver(mulCurriculum);
            var ownedFrontier = ownership.GetOwnedFrontier(currentBandIndex);

            var seedData = new List<(ItemLearningState Item, FsrsCardState? Fsrs)>();
            var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
            var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);

            // Eligible CurrentBand Frontier facts
            foreach (var fact in ownedFrontier)
            {
                var item = ItemLearningState.CreateNew(fact);
                item.TotalAttempts = 5;
                item.IsProvisionallyMastered = fact.Id == "mul:2*2";
                item.LastPracticedOrder = 1;
                var fsrs = new FsrsCardState(fact.Id, Guid.NewGuid(), 2, null, 10.0, 5.0, 50, 10, FsrsRating.Good);
                seedData.Add((item, fsrs));
                itemStates[fact.Id] = item;
                fsrsStates[fact.Id] = fsrs;
            }

            // Eligible Due fact (mul:0*2)
            if (fsrsStates.TryGetValue("mul:0*2", out var existingDueFsrs))
            {
                var updatedDue = existingDueFsrs with { DuePracticePosition = 10, LastReviewPracticePosition = 5 };
                fsrsStates["mul:0*2"] = updatedDue;
                seedData.RemoveAll(x => x.Item.FactId == "mul:0*2");
                seedData.Add((itemStates["mul:0*2"], updatedDue));
            }

            // Eligible Remediation fact (mul:1*2)
            if (itemStates.TryGetValue("mul:1*2", out var remedItem))
            {
                remedItem.NeedsRemediation = true;
                var updatedRemed = fsrsStates["mul:1*2"] with { LastReviewPracticePosition = 20, DuePracticePosition = 10 };
                fsrsStates["mul:1*2"] = updatedRemed;
                seedData.RemoveAll(x => x.Item.FactId == "mul:1*2");
                seedData.Add((remedItem, updatedRemed));
            }

            // Eligible Maintenance fact (mul:2*0)
            if (fsrsStates.TryGetValue("mul:2*0", out var maintFsrs))
            {
                var updatedMaint = maintFsrs with { DuePracticePosition = 500, LastReviewPracticePosition = 10 };
                fsrsStates["mul:2*0"] = updatedMaint;
                seedData.RemoveAll(x => x.Item.FactId == "mul:2*0");
                seedData.Add((itemStates["mul:2*0"], updatedMaint));
            }

            // Eligible EarlyReview fact (mul:2*1)
            if (fsrsStates.TryGetValue("mul:2*1", out var earlyFsrs))
            {
                var updatedEarly = earlyFsrs with { DuePracticePosition = 500, LastReviewPracticePosition = 80 };
                fsrsStates["mul:2*1"] = updatedEarly;
                seedData.RemoveAll(x => x.Item.FactId == "mul:2*1");
                seedData.Add((itemStates["mul:2*1"], updatedEarly));
            }

            // Future facts across all 4 roles:
            // Future Due: mul:2*8 (owner 7)
            var futureDue = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 8);
            var futureDueItem = ItemLearningState.CreateNew(futureDue);
            var futureDueFsrs = new FsrsCardState(futureDue.Id, Guid.NewGuid(), 2, null, 10.0, 5.0, 10, 5, FsrsRating.Good);
            seedData.Add((futureDueItem, futureDueFsrs));
            itemStates[futureDue.Id] = futureDueItem;
            fsrsStates[futureDue.Id] = futureDueFsrs;

            // Future Remediation: mul:3*8 (owner 8)
            var futureRemed = new ArithmeticFact(ArithmeticOperation.Multiplication, 3, 8);
            var futureRemedItem = ItemLearningState.CreateNew(futureRemed);
            futureRemedItem.NeedsRemediation = true;
            var futureRemedFsrs = new FsrsCardState(futureRemed.Id, Guid.NewGuid(), 2, null, 10.0, 5.0, 10, 20, FsrsRating.Good);
            seedData.Add((futureRemedItem, futureRemedFsrs));
            itemStates[futureRemed.Id] = futureRemedItem;
            fsrsStates[futureRemed.Id] = futureRemedFsrs;

            // Future Maintenance: mul:4*8 (owner 8)
            var futureMaint = new ArithmeticFact(ArithmeticOperation.Multiplication, 4, 8);
            var futureMaintItem = ItemLearningState.CreateNew(futureMaint);
            var futureMaintFsrs = new FsrsCardState(futureMaint.Id, Guid.NewGuid(), 2, null, 10.0, 5.0, 500, 10, FsrsRating.Good);
            seedData.Add((futureMaintItem, futureMaintFsrs));
            itemStates[futureMaint.Id] = futureMaintItem;
            fsrsStates[futureMaint.Id] = futureMaintFsrs;

            // Future EarlyReview: mul:5*8 (owner 8)
            var futureEarly = new ArithmeticFact(ArithmeticOperation.Multiplication, 5, 8);
            var futureEarlyItem = ItemLearningState.CreateNew(futureEarly);
            var futureEarlyFsrs = new FsrsCardState(futureEarly.Id, Guid.NewGuid(), 2, null, 10.0, 5.0, 500, 80, FsrsRating.Good);
            seedData.Add((futureEarlyItem, futureEarlyFsrs));
            itemStates[futureEarly.Id] = futureEarlyItem;
            fsrsStates[futureEarly.Id] = futureEarlyFsrs;

            await SeedItemAndFsrsAsync(dbPath, seedData);

            var progression = new LearnerProgression
            {
                PracticePosition = 100,
                OperationProgressions = Enum.GetValues<ArithmeticOperation>()
                    .ToDictionary(
                        o => o,
                        o => new OperationProgression(
                            o,
                            o == ArithmeticOperation.Multiplication ? currentBandIndex : 0,
                            0))
            };

            var snapshot = new LearnerSnapshot(
                progression,
                itemStates,
                fsrsStates,
                [],
                1,
                6);

            // mul:1*2 is the eligible Remediation fact. It is deliberately scoped OUT of
            // the request's current-band frontier so that both evidence builders apply
            // identical frontier scoping to CurrentBandCandidates (Task 3 leaves that
            // pool's store-side behavior unchanged). The fact is still exercised through
            // the Remediation pool, which is role-scoped and not frontier-scoped.
            var currentBandFrontier = ownedFrontier
                .Where(fact => fact.Id != "mul:1*2")
                .ToArray();

            var request = new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Multiplication,
                prospectivePracticePosition: 100,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: currentBandFrontier,
                introductionFrontier: currentBandFrontier,
                currentBandIndex: currentBandIndex);

            var sqliteEvidence = await store.LoadPracticeSelectionEvidenceAsync(request);
            var snapshotEvidence = PracticeSelectionEvidence.FromSnapshot(snapshot, request);

            Assert.Equal(
                snapshotEvidence.CurrentBandCandidates.Select(c => c.Fact.Id),
                sqliteEvidence.CurrentBandCandidates.Select(c => c.Fact.Id));
            Assert.Equal(
                snapshotEvidence.DueCandidates.Select(c => c.Fact.Id),
                sqliteEvidence.DueCandidates.Select(c => c.Fact.Id));
            Assert.Equal(
                snapshotEvidence.MaintenanceCandidates.Select(c => c.Fact.Id),
                sqliteEvidence.MaintenanceCandidates.Select(c => c.Fact.Id));
            Assert.Equal(
                snapshotEvidence.RemediationCandidates.Select(c => c.Fact.Id),
                sqliteEvidence.RemediationCandidates.Select(c => c.Fact.Id));
            Assert.Equal(
                snapshotEvidence.EarlyReviewCandidates.Select(c => c.Fact.Id),
                sqliteEvidence.EarlyReviewCandidates.Select(c => c.Fact.Id));
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    // ===========================================================================
    // Task 4: Selector Pure Defense Layer
    // ===========================================================================

    /// <summary>
    /// Task 4: proves that the selector itself rejects a future locked fact even
    /// when malformed bounded evidence incorrectly contains it as a legitimate
    /// review candidate.
    ///
    /// Fixture: multiplication at BandIndex 1 (mul:2*2 is owned by BandIndex 1;
    /// mul:2*8 is owned by BandIndex 7). The malformed evidence injects mul:2*8 as
    /// the sole Due candidate; the single eligible rescue fact (mul:2*2) is only
    /// reachable through the Frontier pool. The deterministic schedule at position
    /// 6 targets multiplication with a Due request, so the malformed review pool is
    /// exercised directly rather than only via fallback.
    ///
    /// Pre-fix the selector selects mul:2*8 and this test fails; post-fix the
    /// pure defense layer rejects the future fact and the selector falls back to
    /// the eligible mul:2*2, which proves rejection rather than a missing-candidate
    /// exception.
    /// </summary>
    [Fact]
    public void Selector_MalformedEvidenceWithFutureFact_RejectedByPureDefense()
    {
        const int currentBandIndex = 1;
        const long prospectivePosition = 6;

        var curriculum = new ArithmeticCurriculum();
        var ownership = new AcquisitionOwnershipResolver(curriculum.Multiplication);

        // Fixture precondition: canonical ownership proves the fact bands.
        Assert.True(ownership.TryGetOwner("mul:2*8", 11, out var ownerBandIndex));
        Assert.Equal(7, ownerBandIndex);
        Assert.False(ownership.IsEligible("mul:2*8", currentBandIndex));
        Assert.True(ownership.IsEligible("mul:2*2", currentBandIndex));

        // Fixture precondition: the deterministic schedule targets multiplication/Due.
        Assert.Equal(
            ArithmeticOperation.Multiplication,
            AdaptivePracticeSelector.GetScheduledOperation(prospectivePosition));
        Assert.Equal(
            PracticeSelectionRole.Due,
            AdaptivePracticeSelector.GetRequestedRole(((prospectivePosition - 1) / 4) + 1));

        var futureFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 8);
        var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2);
        var futureItem = ItemLearningState.CreateNew(futureFact);
        var eligibleItem = ItemLearningState.CreateNew(eligibleFact);
        var futureFsrs = new FsrsCardState(
            futureFact.Id,
            Guid.NewGuid(),
            State: 2,
            Step: null,
            Stability: 30.0,
            Difficulty: 5.0,
            DuePracticePosition: 1,
            LastReviewPracticePosition: 1,
            LastRating: FsrsRating.Good);

        // Malformed evidence: the future fact is the sole Due candidate.
        var evidence = new PracticeSelectionEvidence(
            ArithmeticOperation.Multiplication,
            prospectivePosition,
            currentBandCandidates: [new PracticeSelectionCandidate(eligibleFact, eligibleItem, null)],
            dueCandidates: [new PracticeSelectionCandidate(futureFact, futureItem, futureFsrs)],
            maintenanceCandidates: [],
            remediationCandidates: [],
            earlyReviewCandidates: []);

        var context = CreateSelectorContext(curriculum, prospectivePosition, currentBandIndex, evidence);

        // Act
        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        // Assert: the future fact is rejected and the eligible rescue fact is selected.
        Assert.NotEqual(futureFact.Id, result.Fact.Id);
        Assert.True(ownership.IsEligible(result.Fact.Id, currentBandIndex));
        Assert.Equal(eligibleFact.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
    }

    /// <summary>
    /// Task 4: proves the invariant at the final selector boundary under low
    /// multiplication progression. mul:2*8 (owner BandIndex 7) is materialized and
    /// maliciously injected into the evidence review pools, including Remediation,
    /// while a valid eligible candidate (mul:2*2) exists. The selector at BandIndex 1
    /// must never return mul:2*8 regardless of its pool membership, and a future fact
    /// must never trigger remediation preemption.
    ///
    /// Pre-fix the future fact triggers remediation preemption and the selector
    /// returns mul:2*8, failing this test; post-fix the pure defense layer removes it
    /// from every review pool, preemption does not occur, and the selector falls
    /// back to the eligible frontier fact.
    /// </summary>
    [Fact]
    public void Selector_LowMultiplication_NeverSelects2x8()
    {
        const int currentBandIndex = 1;
        const long prospectivePosition = 6;

        var curriculum = new ArithmeticCurriculum();
        var ownership = new AcquisitionOwnershipResolver(curriculum.Multiplication);

        // Fixture precondition: canonical ownership proves the fact bands.
        Assert.True(ownership.TryGetOwner("mul:2*8", 11, out var ownerBandIndex));
        Assert.Equal(7, ownerBandIndex);
        Assert.False(ownership.IsEligible("mul:2*8", currentBandIndex));
        Assert.True(ownership.IsEligible("mul:2*2", currentBandIndex));

        // Fixture precondition: the deterministic schedule targets multiplication/Due.
        Assert.Equal(
            ArithmeticOperation.Multiplication,
            AdaptivePracticeSelector.GetScheduledOperation(prospectivePosition));
        Assert.Equal(
            PracticeSelectionRole.Due,
            AdaptivePracticeSelector.GetRequestedRole(((prospectivePosition - 1) / 4) + 1));

        var futureFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 8);
        var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2);

        // The future fact is maliciously flagged for remediation so that, pre-fix,
        // it can trigger the selector's remediation preemption path.
        var futureItem = ItemLearningState.CreateNew(futureFact);
        futureItem.NeedsRemediation = true;
        var eligibleItem = ItemLearningState.CreateNew(eligibleFact);
        var futureFsrs = new FsrsCardState(
            futureFact.Id,
            Guid.NewGuid(),
            State: 2,
            Step: null,
            Stability: 30.0,
            Difficulty: 5.0,
            DuePracticePosition: prospectivePosition,
            LastReviewPracticePosition: 1,
            LastRating: FsrsRating.Good);

        // Malformed evidence: the future fact is injected into the Remediation and
        // Due review pools; the eligible fact is only reachable via the Frontier pool.
        var futureCandidate = new PracticeSelectionCandidate(futureFact, futureItem, futureFsrs);
        var eligibleCandidate = new PracticeSelectionCandidate(eligibleFact, eligibleItem, null);
        var evidence = new PracticeSelectionEvidence(
            ArithmeticOperation.Multiplication,
            prospectivePosition,
            currentBandCandidates: [eligibleCandidate],
            dueCandidates: [futureCandidate],
            maintenanceCandidates: [],
            remediationCandidates: [futureCandidate],
            earlyReviewCandidates: []);

        var context = CreateSelectorContext(curriculum, prospectivePosition, currentBandIndex, evidence);

        // Act
        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        // Assert: invariant at the final boundary - the future fact is never
        // selected, and the selected fact is eligible at BandIndex 1.
        Assert.NotEqual(futureFact.Id, result.Fact.Id);
        Assert.True(ownership.IsEligible(result.Fact.Id, currentBandIndex));
        Assert.Equal(eligibleFact.Id, result.Fact.Id);
    }

    // ===========================================================================
    // Task 5: Persistence Acceptance Validation Gate
    // ===========================================================================

    [Fact]
    public async Task Persistence_FutureLockedFactSubmission_RejectedWithoutStateMutation()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();
            var before = await store.LoadSnapshotAsync();

            var curriculum = new ArithmeticCurriculum();
            var mulOwnership = new AcquisitionOwnershipResolver(curriculum.Multiplication);
            var storedBandIndex = before.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex;

            // mul:2*8 canonical owner is BandIndex 7 (MUL-D08). At BandIndex 0 (fresh store), it is future-locked.
            Assert.True(mulOwnership.TryGetOwner("mul:2*8", 11, out var ownerBandIndex));
            Assert.Equal(7, ownerBandIndex);
            Assert.False(mulOwnership.IsEligible("mul:2*8", storedBandIndex));

            var futureFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 8);
            var item = ItemLearningState.CreateNew(futureFact);
            item.TotalAttempts = 1;
            item.CorrectAttempts = 1;
            item.ConsecutiveCorrectStreak = 1;
            item.LastLatencyMs = 900;

            var progression = LearnerProgression.CreateFresh();
            progression.PracticePosition = 1;

            var submissionId = Guid.NewGuid().ToString("N");
            var attempt = new AttemptRecord(
                submissionId,
                futureFact.Id,
                futureFact.Operation,
                futureFact.LeftOperand,
                futureFact.RightOperand,
                submittedAnswer: 16,
                correctAnswer: 16,
                isCorrect: true,
                isFluent: true,
                responseLatencyMs: 900,
                timestamp: DateTimeOffset.UtcNow,
                practicePosition: 1);

            var fsrs = new FsrsCardState(
                futureFact.Id,
                Guid.NewGuid(),
                State: 2,
                Step: null,
                Stability: 30.0,
                Difficulty: 5.0,
                DuePracticePosition: 5,
                LastReviewPracticePosition: 1,
                LastRating: FsrsRating.Good);

            var changeSet = new SubmissionChangeSet(submissionId, before.Revision, attempt, item, progression, updatedFsrsState: fsrs);

            var result = await store.CommitSubmissionAsync(changeSet);

            Assert.False(result.IsSuccess);
            Assert.Equal(PersistenceStatus.InvalidSubmission, result.Status);
            Assert.Equal("The attempted fact is not unlocked by the operation's current progression.", result.Message);

            var after = await store.LoadSnapshotAsync();
            Assert.Equal(before.Revision, after.Revision);
            Assert.Equal(before.Progression.PracticePosition, after.Progression.PracticePosition);
            Assert.Equal(before.Progression.OperationProgressions, after.Progression.OperationProgressions);
            Assert.Empty(after.RecentAttempts);
            Assert.DoesNotContain(futureFact.Id, after.ItemStates.Keys);
            Assert.DoesNotContain(futureFact.Id, after.FsrsStates.Keys);
            Assert.Empty(after.ItemStates);
            Assert.Empty(after.FsrsStates);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Persistence_ValidCurrentBandSubmission_Succeeds()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();
            var before = await store.LoadSnapshotAsync();

            var storedBandIndex = before.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex;
            var curriculum = new ArithmeticCurriculum();
            var mulOwnership = new AcquisitionOwnershipResolver(curriculum.Multiplication);

            var frontier = mulOwnership.GetOwnedFrontier(storedBandIndex);
            var validFact = frontier.First();
            Assert.True(mulOwnership.IsEligible(validFact.Id, storedBandIndex));

            var item = ItemLearningState.CreateNew(validFact);
            item.TotalAttempts = 1;
            item.CorrectAttempts = 1;
            item.ConsecutiveCorrectStreak = 1;
            item.LastLatencyMs = 850;

            var progression = LearnerProgression.CreateFresh();
            progression.PracticePosition = 1;

            var submissionId = Guid.NewGuid().ToString("N");
            var attempt = new AttemptRecord(
                submissionId,
                validFact.Id,
                validFact.Operation,
                validFact.LeftOperand,
                validFact.RightOperand,
                submittedAnswer: validFact.CorrectResult,
                correctAnswer: validFact.CorrectResult,
                isCorrect: true,
                isFluent: true,
                responseLatencyMs: 850,
                timestamp: DateTimeOffset.UtcNow,
                practicePosition: 1);

            var fsrs = new FsrsCardState(
                validFact.Id,
                Guid.NewGuid(),
                State: 2,
                Step: null,
                Stability: 10.0,
                Difficulty: 5.0,
                DuePracticePosition: 10,
                LastReviewPracticePosition: 1,
                LastRating: FsrsRating.Good);

            var changeSet = new SubmissionChangeSet(submissionId, before.Revision, attempt, item, progression, updatedFsrsState: fsrs);

            var result = await store.CommitSubmissionAsync(changeSet);

            Assert.True(result.IsSuccess);
            Assert.Equal(before.Revision + 1, result.NewRevision);

            var after = await store.LoadSnapshotAsync();
            Assert.Equal(before.Revision + 1, after.Revision);
            Assert.Equal(1, after.Progression.PracticePosition);
            Assert.Single(after.RecentAttempts);
            Assert.Equal(validFact.Id, after.RecentAttempts[0].FactId);
            Assert.True(after.ItemStates.ContainsKey(validFact.Id));
            Assert.True(after.FsrsStates.ContainsKey(validFact.Id));
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Theory]
    [InlineData("add:1+1", ArithmeticOperation.Multiplication, 1, 1, 1)]
    [InlineData("mul:01*01", ArithmeticOperation.Multiplication, 1, 1, 1)]
    [InlineData("invalid_id", ArithmeticOperation.Multiplication, 1, 1, 1)]
    public async Task Persistence_MalformedOrCrossOperationFactSubmission_RejectedWithoutStateMutation(
        string invalidFactId,
        ArithmeticOperation operation,
        int leftOperand,
        int rightOperand,
        int answer)
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            await store.InitializeAsync();
            var before = await store.LoadSnapshotAsync();

            var dummyFact = new ArithmeticFact(operation, leftOperand, rightOperand);
            var item = ItemLearningState.CreateNew(dummyFact);
            item.TotalAttempts = 1;
            item.CorrectAttempts = 1;
            item.ConsecutiveCorrectStreak = 1;
            item.LastLatencyMs = 900;

            var progression = LearnerProgression.CreateFresh();
            progression.PracticePosition = 1;

            var submissionId = Guid.NewGuid().ToString("N");
            var attempt = new AttemptRecord(
                submissionId,
                invalidFactId,
                operation,
                leftOperand,
                rightOperand,
                submittedAnswer: answer,
                correctAnswer: answer,
                isCorrect: true,
                isFluent: true,
                responseLatencyMs: 900,
                timestamp: DateTimeOffset.UtcNow,
                practicePosition: 1);

            var changeSet = new SubmissionChangeSet(submissionId, before.Revision, attempt, item, progression);

            var result = await store.CommitSubmissionAsync(changeSet);

            Assert.False(result.IsSuccess);
            Assert.Equal(PersistenceStatus.InvalidSubmission, result.Status);

            var after = await store.LoadSnapshotAsync();
            Assert.Equal(before.Revision, after.Revision);
            Assert.Equal(before.Progression.PracticePosition, after.Progression.PracticePosition);
            Assert.Equal(before.Progression.OperationProgressions, after.Progression.OperationProgressions);
            Assert.Empty(after.RecentAttempts);
            Assert.Empty(after.ItemStates);
            Assert.Empty(after.FsrsStates);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    // ===========================================================================
    // Task 6: Migration Stale State Dormancy & Upgrade Parity
    // ===========================================================================

    [Fact]
    public async Task Migration_V4WithFutureFacts_MigratesSuccessfullyAndKeepsFutureFactsDormant()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var futureCardId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
            var eligibleCardId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

            var curriculum = new ArithmeticCurriculum();
            var mulCurriculum = curriculum.Multiplication;
            var mulOwnership = new AcquisitionOwnershipResolver(mulCurriculum);

            // Verify canonical owner of mul:2*8 is BandIndex 7 (MUL-D08).
            Assert.True(mulOwnership.TryGetOwner("mul:2*8", 11, out var mul2x8Owner));
            Assert.Equal(7, mul2x8Owner);

            // Select an eligible control fact from BandIndex 1 (e.g. mul:2*2).
            var band1Frontier = mulOwnership.GetOwnedFrontier(1);
            var eligibleControlFact = band1Frontier.First(f => mulOwnership.IsEligible(f.Id, 1));
            Assert.True(mulOwnership.IsEligible(eligibleControlFact.Id, 1));

            const long initialPracticePosition = 17;
            const long initialStoreRevision = 7;

            // Seed genuine V4 database with future fact mul:2*8 and eligible control fact.
            await CreateTask6V4DatabaseAsync(
                dbPath,
                initialStoreRevision,
                initialPracticePosition,
                multiplicationMaxOperand: 2, // Maps to BandIndex = 1 (max - 1)
                futureCardId,
                eligibleControlFact,
                eligibleCardId);

            // Act 1: Initialize SqliteLearnerStore, executing V4 -> V5 -> V6 migration.
            using (var store = new SqliteLearnerStore(dbPath))
            {
                await store.InitializeAsync();

                var snapshot = await store.LoadSnapshotAsync();

                // Invariant A & D: Migration succeeded and reached V6.
                Assert.Equal(6, snapshot.SchemaVersion);
                Assert.Equal(initialStoreRevision, snapshot.Revision);
                Assert.Equal(initialPracticePosition, snapshot.Progression.PracticePosition);

                // Invariant E: Migrated Multiplication BandIndex is 1 (< 7).
                var mulProgression = snapshot.OperationProgressions![ArithmeticOperation.Multiplication];
                Assert.Equal(1, mulProgression.BandIndex);
                Assert.Equal(initialPracticePosition, mulProgression.BandStartedPracticePosition);
                Assert.False(mulOwnership.IsEligible("mul:2*8", mulProgression.BandIndex));

                // Invariant B, C, D: mul:2*8 item and FSRS rows survived migration losslessly with stable identity.
                Assert.True(snapshot.ItemStates.TryGetValue("mul:2*8", out var migratedFutureItem));
                Assert.Equal(ArithmeticOperation.Multiplication, migratedFutureItem.Operation);
                Assert.Equal(2, migratedFutureItem.LeftOperand);
                Assert.Equal(8, migratedFutureItem.RightOperand);
                Assert.Equal(5, migratedFutureItem.TotalAttempts);
                Assert.Equal(4, migratedFutureItem.CorrectAttempts);
                Assert.Equal(1, migratedFutureItem.IncorrectAttempts);
                Assert.False(migratedFutureItem.NeedsRemediation);
                Assert.Equal(10, migratedFutureItem.LastPracticedOrder);

                Assert.True(snapshot.FsrsStates.TryGetValue("mul:2*8", out var migratedFutureFsrs));
                Assert.Equal(futureCardId, migratedFutureFsrs.CardId);
                Assert.Equal(2, migratedFutureFsrs.State);
                Assert.Equal(15.0, migratedFutureFsrs.Stability);
                Assert.Equal(4.5, migratedFutureFsrs.Difficulty);
                Assert.Equal(5, migratedFutureFsrs.DuePracticePosition); // Due relative to prospective position 20
                Assert.Equal(1, migratedFutureFsrs.LastReviewPracticePosition);
                Assert.Equal(FsrsRating.Good, migratedFutureFsrs.LastRating);

                // Verify eligible control fact survived migration as well.
                Assert.True(snapshot.ItemStates.ContainsKey(eligibleControlFact.Id));
                Assert.True(snapshot.FsrsStates.TryGetValue(eligibleControlFact.Id, out var migratedEligibleFsrs));
                Assert.Equal(eligibleCardId, migratedEligibleFsrs.CardId);

                // Invariant F & G: Evidence request with explicit currentBandIndex = 1
                var ownedFrontier = mulOwnership.GetOwnedFrontier(mulProgression.BandIndex);
                var request = new PracticeSelectionEvidenceRequest(
                    ArithmeticOperation.Multiplication,
                    prospectivePracticePosition: 20,
                    currentSessionOrder: 0,
                    currentBandOwnedFrontier: ownedFrontier,
                    introductionFrontier: ownedFrontier,
                    currentBandIndex: mulProgression.BandIndex);

                var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);

                // mul:2*8 must NOT appear in any review candidate pool because it is locked at BandIndex 1.
                Assert.DoesNotContain(evidence.DueCandidates, c => c.Fact.Id == "mul:2*8");
                Assert.DoesNotContain(evidence.MaintenanceCandidates, c => c.Fact.Id == "mul:2*8");
                Assert.DoesNotContain(evidence.EarlyReviewCandidates, c => c.Fact.Id == "mul:2*8");
                Assert.DoesNotContain(evidence.RemediationCandidates, c => c.Fact.Id == "mul:2*8");
                Assert.DoesNotContain(evidence.CurrentBandCandidates, c => c.Fact.Id == "mul:2*8");

                // Eligible control fact DOES appear in DueCandidates.
                Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleControlFact.Id);

                await store.CloseAsync();
            }

            // Invariant J: Reopen fresh store instance from disk and verify durability.
            using (var reopenedStore = new SqliteLearnerStore(dbPath))
            {
                await reopenedStore.InitializeAsync();
                var reopenedSnapshot = await reopenedStore.LoadSnapshotAsync();

                Assert.Equal(6, reopenedSnapshot.SchemaVersion);
                Assert.Equal(initialStoreRevision, reopenedSnapshot.Revision);
                Assert.True(reopenedSnapshot.ItemStates.ContainsKey("mul:2*8"));
                Assert.True(reopenedSnapshot.FsrsStates.TryGetValue("mul:2*8", out var reopenedFsrs));
                Assert.Equal(futureCardId, reopenedFsrs.CardId);

                var mulProgression = reopenedSnapshot.OperationProgressions![ArithmeticOperation.Multiplication];
                var ownedFrontier = mulOwnership.GetOwnedFrontier(mulProgression.BandIndex);
                var request = new PracticeSelectionEvidenceRequest(
                    ArithmeticOperation.Multiplication,
                    prospectivePracticePosition: 20,
                    currentSessionOrder: 0,
                    currentBandOwnedFrontier: ownedFrontier,
                    introductionFrontier: ownedFrontier,
                    currentBandIndex: mulProgression.BandIndex);

                var evidence = await reopenedStore.LoadPracticeSelectionEvidenceAsync(request);
                Assert.DoesNotContain(evidence.DueCandidates, c => c.Fact.Id == "mul:2*8");
                Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleControlFact.Id);

                await reopenedStore.CloseAsync();
            }
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Migration_ProgressionAdvanceToBand7_UnlocksHistorical2x8()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var futureCardId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");
            var eligibleCardId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");

            var curriculum = new ArithmeticCurriculum();
            var mulCurriculum = curriculum.Multiplication;
            var mulOwnership = new AcquisitionOwnershipResolver(mulCurriculum);

            Assert.True(mulOwnership.TryGetOwner("mul:2*8", 11, out var mul2x8Owner));
            Assert.Equal(7, mul2x8Owner);

            var band1Frontier = mulOwnership.GetOwnedFrontier(1);
            var eligibleControlFact = band1Frontier.First(f => mulOwnership.IsEligible(f.Id, 1));

            const long initialPracticePosition = 17;
            const long initialStoreRevision = 7;

            // Seed genuine V4 database.
            await CreateTask6V4DatabaseAsync(
                dbPath,
                initialStoreRevision,
                initialPracticePosition,
                multiplicationMaxOperand: 2, // Maps to BandIndex 1
                futureCardId,
                eligibleControlFact,
                eligibleCardId);

            // Step 1: Migrate V4 -> V5 -> V6 and verify dormancy at low BandIndex 1.
            using (var store = new SqliteLearnerStore(dbPath))
            {
                await store.InitializeAsync();
                var snapshot = await store.LoadSnapshotAsync();
                Assert.Equal(1, snapshot.OperationProgressions![ArithmeticOperation.Multiplication].BandIndex);
                Assert.False(mulOwnership.IsEligible("mul:2*8", 1));

                var request = new PracticeSelectionEvidenceRequest(
                    ArithmeticOperation.Multiplication,
                    prospectivePracticePosition: 20,
                    currentSessionOrder: 0,
                    currentBandOwnedFrontier: mulOwnership.GetOwnedFrontier(1),
                    introductionFrontier: mulOwnership.GetOwnedFrontier(1),
                    currentBandIndex: 1);

                var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);
                Assert.DoesNotContain(evidence.DueCandidates, c => c.Fact.Id == "mul:2*8");
                Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleControlFact.Id);

                await store.CloseAsync();
            }

            // Step 2: Advance ONLY durable Multiplication progression to BandIndex 7 via direct SQL update.
            // Satisfies invariant: band_started_practice_position <= learner practice_position.
            await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                using var updateCmd = conn.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE operation_progression
                    SET band_index = 7, band_started_practice_position = 10
                    WHERE operation = 'Multiplication';";
                var updatedRows = await updateCmd.ExecuteNonQueryAsync();
                Assert.Equal(1, updatedRows);
            }

            // Step 3: Reopen fresh store instance and verify auto-unlock.
            using (var reopenedStore = new SqliteLearnerStore(dbPath))
            {
                await reopenedStore.InitializeAsync();
                var snapshot = await reopenedStore.LoadSnapshotAsync();

                Assert.Equal(6, snapshot.SchemaVersion);
                Assert.Equal(initialStoreRevision, snapshot.Revision);

                var mulProgression = snapshot.OperationProgressions![ArithmeticOperation.Multiplication];
                Assert.Equal(7, mulProgression.BandIndex);
                Assert.Equal(10, mulProgression.BandStartedPracticePosition);

                // Verify domain eligibility at BandIndex 7.
                Assert.True(mulOwnership.IsEligible("mul:2*8", 7));

                // Verify the SAME historical row and FSRS card state are preserved without duplication.
                Assert.True(snapshot.ItemStates.TryGetValue("mul:2*8", out var historicalItem));
                Assert.Equal(5, historicalItem.TotalAttempts);
                Assert.Equal(4, historicalItem.CorrectAttempts);
                Assert.Equal(1, historicalItem.IncorrectAttempts);

                Assert.True(snapshot.FsrsStates.TryGetValue("mul:2*8", out var historicalFsrs));
                Assert.Equal(futureCardId, historicalFsrs.CardId);
                Assert.Equal(15.0, historicalFsrs.Stability);
                Assert.Equal(4.5, historicalFsrs.Difficulty);
                Assert.Equal(5, historicalFsrs.DuePracticePosition);

                // Only 2 facts total were seeded (mul:2*8 and eligibleControlFact); verify no extra/duplicate rows.
                Assert.Equal(2, snapshot.ItemStates.Count);
                Assert.Equal(2, snapshot.FsrsStates.Count);

                // Build evidence request at BandIndex 7.
                var band7Frontier = mulOwnership.GetOwnedFrontier(7);
                var request = new PracticeSelectionEvidenceRequest(
                    ArithmeticOperation.Multiplication,
                    prospectivePracticePosition: 20,
                    currentSessionOrder: 0,
                    currentBandOwnedFrontier: band7Frontier,
                    introductionFrontier: band7Frontier,
                    currentBandIndex: 7);

                var evidence = await reopenedStore.LoadPracticeSelectionEvidenceAsync(request);

                // Invariant I: mul:2*8 now automatically appears in DueCandidates without data rewrite or rematerialization.
                Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == "mul:2*8");
                var mul2x8Candidate = evidence.DueCandidates.First(c => c.Fact.Id == "mul:2*8");
                Assert.Equal(futureCardId, mul2x8Candidate.FsrsState?.CardId);

                // Eligible control fact also remains present in Due pool.
                Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == eligibleControlFact.Id);

                await reopenedStore.CloseAsync();
            }

            // Invariant J: Close and reopen again to verify persistence across restarts.
            using (var secondReopen = new SqliteLearnerStore(dbPath))
            {
                await secondReopen.InitializeAsync();
                var snapshot = await secondReopen.LoadSnapshotAsync();
                Assert.Equal(7, snapshot.OperationProgressions![ArithmeticOperation.Multiplication].BandIndex);
                Assert.True(snapshot.ItemStates.ContainsKey("mul:2*8"));
                Assert.True(snapshot.FsrsStates.TryGetValue("mul:2*8", out var fsrs));
                Assert.Equal(futureCardId, fsrs.CardId);

                var request = new PracticeSelectionEvidenceRequest(
                    ArithmeticOperation.Multiplication,
                    prospectivePracticePosition: 20,
                    currentSessionOrder: 0,
                    currentBandOwnedFrontier: mulOwnership.GetOwnedFrontier(7),
                    introductionFrontier: mulOwnership.GetOwnedFrontier(7),
                    currentBandIndex: 7);

                var evidence = await secondReopen.LoadPracticeSelectionEvidenceAsync(request);
                Assert.Contains(evidence.DueCandidates, c => c.Fact.Id == "mul:2*8");

                await secondReopen.CloseAsync();
            }
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    // ===========================================================================
    // Task 8: Long-Run Deterministic Simulation & Full Regression Suite
    // ===========================================================================

    [Fact]
    public async Task LongRun_500Steps_LowProgression_ZeroIneligiblePresentations()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);

            var curriculum = new ArithmeticCurriculum();
            var resolvers = Enum.GetValues<ArithmeticOperation>()
                .ToDictionary(
                    op => op,
                    op => new AcquisitionOwnershipResolver(curriculum.GetCurriculum(op)));

            var operationPresentationCounts = Enum.GetValues<ArithmeticOperation>()
                .ToDictionary(op => op, _ => 0);

            // Starting state: verify all operations begin at low progression (BandIndex 0).
            foreach (var op in Enum.GetValues<ArithmeticOperation>())
            {
                var initialBandIndex = session.Progression.OperationProgressions[op].BandIndex;
                Assert.Equal(0, initialBandIndex);
            }

            for (var step = 1; step <= 500; step++)
            {
                // 1. Capture Session.CurrentFact BEFORE submitting the answer
                var fact = session.CurrentFact;
                Assert.NotNull(fact);

                // 2. Identify its operation
                var operation = fact.Operation;
                operationPresentationCounts[operation]++;

                // 3. Read that operation's current progression BandIndex BEFORE submission
                var progression = session.Progression.OperationProgressions[operation];
                var currentBandIndex = progression.BandIndex;

                // 4. Use canonical ArithmeticCurriculum and AcquisitionOwnershipResolver
                var ownership = resolvers[operation];

                // 5. Assert the fact is eligible at that exact BandIndex
                var isEligible = ownership.IsEligible(fact.Id, currentBandIndex);
                ownership.TryGetOwner(fact.Id, 50, out var ownerBandIndex);

                Assert.True(
                    isEligible,
                    $"Ineligible fact presentation detected at step {step} (Position={session.Progression.PracticePosition}): " +
                    $"FactId='{fact.Id}', Operation={operation}, CurrentBandIndex={currentBandIndex}, OwnerBandIndex={ownerBandIndex}.");

                // Submit deterministic valid answer and commit through real runtime pipeline
                session.SubmitAnswer(fact.CorrectResult);
                var commitResult = await session.CommitCurrentEvaluationAsync();
                Assert.True(
                    commitResult.IsSuccess,
                    $"Failed to commit submission at step {step}: {commitResult.Status} - {commitResult.Message}");

                // Advance through normal TrainingSession lifecycle, handling check-in summaries
                if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);
                    session.ContinuePractice(startTiming: false);
                }
            }

            // Post-simulation verifications:
            // 1. All 500 practice steps were completed and accepted.
            Assert.Equal(500, session.Progression.PracticePosition);

            // 2. All four operations were observed and scheduled deterministically.
            Assert.All(Enum.GetValues<ArithmeticOperation>(), op =>
            {
                Assert.True(operationPresentationCounts[op] > 0, $"Operation {op} was not observed during the 500-step run.");
                Assert.Equal(125, operationPresentationCounts[op]);
            });

            // 3. Natural progression advancement occurred across all operations from low progression.
            Assert.All(Enum.GetValues<ArithmeticOperation>(), op =>
            {
                var finalBandIndex = session.Progression.OperationProgressions[op].BandIndex;
                Assert.True(
                    finalBandIndex > 0,
                    $"Operation {op} was expected to advance beyond initial BandIndex 0, but remained at {finalBandIndex}.");
            });

            await store.CloseAsync();
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task UnlockTransition_FactBecomesEligibleImmediatelyUponBandAdvance()
    {
        var dbPath = GetTempDbPath();
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);

            var curriculum = new ArithmeticCurriculum();
            var mulCurriculum = curriculum.Multiplication;
            var mulOwnership = new AcquisitionOwnershipResolver(mulCurriculum);
            var resolvers = Enum.GetValues<ArithmeticOperation>()
                .ToDictionary(
                    op => op,
                    op => new AcquisitionOwnershipResolver(curriculum.GetCurriculum(op)));

            // Choose canonical fact from BandIndex 1 (MUL-D02), e.g. mul:2*2.
            const string targetFactId = "mul:2*2";
            Assert.True(mulOwnership.TryGetOwner(targetFactId, 11, out var targetOwnerBandIndex));
            Assert.Equal(1, targetOwnerBandIndex);

            // A. Immediately before band advancement:
            // Initial progression for Multiplication is BandIndex 0.
            var initialMulProgression = session.Progression.OperationProgressions[ArithmeticOperation.Multiplication];
            Assert.Equal(0, initialMulProgression.BandIndex);
            Assert.True(targetOwnerBandIndex > initialMulProgression.BandIndex);
            Assert.False(mulOwnership.IsEligible(targetFactId, initialMulProgression.BandIndex));

            // B. Perform real TrainingSession practice steps to satisfy the progression gate
            // and advance the operation band.
            var bandAdvanced = false;
            for (var step = 1; step <= 40; step++)
            {
                var fact = session.CurrentFact;
                Assert.NotNull(fact);

                var op = fact.Operation;
                var opProgression = session.Progression.OperationProgressions[op];
                var ownership = resolvers[op];

                // Ensure NO fact presented during the transition violates the eligibility invariant
                var isEligible = ownership.IsEligible(fact.Id, opProgression.BandIndex);
                ownership.TryGetOwner(fact.Id, 50, out var ownerBandIndex);
                Assert.True(
                    isEligible,
                    $"Fact {fact.Id} (owner: {ownerBandIndex}) was ineligible for {op} at BandIndex {opProgression.BandIndex} before submission.");

                session.SubmitAnswer(fact.CorrectResult);
                var commitResult = await session.CommitCurrentEvaluationAsync();
                Assert.True(commitResult.IsSuccess);

                var currentMulBandIndex = session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex;
                if (currentMulBandIndex > initialMulProgression.BandIndex)
                {
                    // C. Immediately after progression advances:
                    // Progression BandIndex has reached the target fact's owner band (1)
                    Assert.Equal(targetOwnerBandIndex, currentMulBandIndex);

                    // Target fact is immediately eligible under the new BandIndex
                    Assert.True(
                        mulOwnership.IsEligible(targetFactId, currentMulBandIndex),
                        $"Fact '{targetFactId}' must be immediately eligible upon reaching BandIndex {currentMulBandIndex}.");

                    bandAdvanced = true;
                    break;
                }

                if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);
                    session.ContinuePractice(startTiming: false);
                }
            }

            Assert.True(bandAdvanced, "Multiplication did not advance to BandIndex 1 within the expected number of steps.");

            // Post-advance continuation: verify that subsequent practice steps continue safely
            // and maintain the eligibility invariant.
            for (var step = 1; step <= 20; step++)
            {
                if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);
                    session.ContinuePractice(startTiming: false);
                }

                var fact = session.CurrentFact;
                Assert.NotNull(fact);

                var op = fact.Operation;
                var opProgression = session.Progression.OperationProgressions[op];
                var ownership = resolvers[op];

                var isEligible = ownership.IsEligible(fact.Id, opProgression.BandIndex);
                ownership.TryGetOwner(fact.Id, 50, out var ownerBandIndex);
                Assert.True(
                    isEligible,
                    $"Post-advance fact {fact.Id} (owner: {ownerBandIndex}) was ineligible for {op} at BandIndex {opProgression.BandIndex} before submission.");

                session.SubmitAnswer(fact.CorrectResult);
                var commitResult = await session.CommitCurrentEvaluationAsync();
                Assert.True(commitResult.IsSuccess);
            }

            await store.CloseAsync();
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    // ---------------------------------------------------------------------------
    // Test helper methods
    // ---------------------------------------------------------------------------

    private static PracticeSelectionContext CreateSelectorContext(
        ArithmeticCurriculum curriculum,
        long prospectivePosition,
        int multiplicationBandIndex,
        PracticeSelectionEvidence evidence,
        long scheduledOperationAttemptOrdinal = 2)
    {
        var operationProgressions = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(
                operation => operation,
                operation => new OperationProgression(
                    operation,
                    operation == ArithmeticOperation.Multiplication ? multiplicationBandIndex : 0,
                    0));

        var curricula = new Dictionary<ArithmeticOperation, OperationCurriculum>
        {
            [ArithmeticOperation.Addition] = curriculum.Addition,
            [ArithmeticOperation.Subtraction] = curriculum.Subtraction,
            [ArithmeticOperation.Multiplication] = curriculum.Multiplication,
            [ArithmeticOperation.Division] = curriculum.Division,
        };

        return new PracticeSelectionContext(
            prospectivePosition,
            0,
            operationProgressions,
            curricula,
            new PracticeCandidateIndex(evidence),
            [],
            scheduledOperationAttemptOrdinal);
    }

    private static string GetTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"mathfirst_test_{Guid.NewGuid():N}.db");

    private static void TryDeleteDatabase(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
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

    private static async Task CreateTask6V4DatabaseAsync(
        string path,
        long storeRevision,
        long practicePosition,
        int multiplicationMaxOperand,
        Guid futureCardId,
        ArithmeticFact eligibleFact,
        Guid eligibleCardId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info VALUES ('schema_version', '4');
                INSERT INTO schema_info VALUES ('store_revision', @store_revision);

                CREATE TABLE learner_progression (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    current_operation TEXT NOT NULL,
                    current_max_operand INTEGER NOT NULL,
                    operation_max_operands_json TEXT NOT NULL,
                    practice_position INTEGER NOT NULL,
                    completed_checkpoint_level INTEGER NOT NULL,
                    active_checkpoint_level INTEGER,
                    checkpoint_attempt_count INTEGER NOT NULL,
                    checkpoint_correct_count INTEGER NOT NULL,
                    updated_at TEXT NOT NULL);

                INSERT INTO learner_progression VALUES (
                    1, 'Multiplication', @mul_max,
                    @max_operands_json, @practice_pos,
                    0, NULL, 0, 0, '2026-09-09T00:00:00Z');

                CREATE TABLE item_learning_state (
                    fact_id TEXT PRIMARY KEY,
                    operation TEXT NOT NULL,
                    left_operand INTEGER NOT NULL,
                    right_operand INTEGER NOT NULL,
                    total_attempts INTEGER NOT NULL,
                    correct_attempts INTEGER NOT NULL,
                    incorrect_attempts INTEGER NOT NULL,
                    consecutive_correct INTEGER NOT NULL,
                    last_latency_ms INTEGER NOT NULL,
                    rolling_latency_ms INTEGER NOT NULL,
                    fluent_streak INTEGER NOT NULL,
                    is_mastered INTEGER NOT NULL,
                    needs_remediation INTEGER NOT NULL,
                    remediation_due_order INTEGER NOT NULL,
                    last_practiced_order INTEGER NOT NULL,
                    last_practiced_at TEXT);

                CREATE TABLE attempt_history (
                    submission_id TEXT PRIMARY KEY,
                    fact_id TEXT NOT NULL,
                    operation TEXT NOT NULL,
                    left_operand INTEGER NOT NULL,
                    right_operand INTEGER NOT NULL,
                    submitted_answer INTEGER,
                    correct_answer INTEGER NOT NULL,
                    is_correct INTEGER NOT NULL,
                    outcome TEXT NOT NULL,
                    response_latency_ms INTEGER NOT NULL,
                    timestamp TEXT NOT NULL);

                CREATE TABLE fsrs_card_state (
                    fact_id TEXT PRIMARY KEY,
                    card_id TEXT NOT NULL,
                    state INTEGER NOT NULL,
                    step INTEGER,
                    stability REAL,
                    difficulty REAL,
                    due_practice_position INTEGER NOT NULL,
                    last_review_practice_position INTEGER,
                    last_rating INTEGER);
            ";
            cmd.Parameters.AddWithValue("@store_revision", storeRevision.ToString());
            cmd.Parameters.AddWithValue("@mul_max", multiplicationMaxOperand);
            cmd.Parameters.AddWithValue("@practice_pos", practicePosition);
            var operandsJson = $"{{\"Addition\":2,\"Subtraction\":1,\"Multiplication\":{multiplicationMaxOperand},\"Division\":1}}";
            cmd.Parameters.AddWithValue("@max_operands_json", operandsJson);
            await cmd.ExecuteNonQueryAsync();
        }

        // Future fact: mul:2*8
        using (var futureCmd = connection.CreateCommand())
        {
            futureCmd.Transaction = transaction;
            futureCmd.CommandText = @"
                INSERT INTO item_learning_state VALUES (
                    'mul:2*8', 'Multiplication', 2, 8,
                    5, 4, 1, 3,
                    950, 950, 3, 0, 0,
                    0, 10, '2026-09-09T00:00:00Z');

                INSERT INTO attempt_history VALUES (
                    'sub-future-1', 'mul:2*8', 'Multiplication', 2, 8,
                    16, 16, 1, 'Correct',
                    950, '2026-09-09T00:00:00Z');

                INSERT INTO fsrs_card_state VALUES (
                    'mul:2*8', @future_card_id, 2, NULL,
                    15.0, 4.5, 5, 1, 3);
            ";
            futureCmd.Parameters.AddWithValue("@future_card_id", futureCardId.ToString());
            await futureCmd.ExecuteNonQueryAsync();
        }

        // Eligible control fact
        using (var eligibleCmd = connection.CreateCommand())
        {
            eligibleCmd.Transaction = transaction;
            eligibleCmd.CommandText = @"
                INSERT INTO item_learning_state VALUES (
                    @fact_id, @operation, @left, @right,
                    4, 4, 0, 4,
                    800, 800, 4, 0, 0,
                    0, 9, '2026-09-09T00:00:00Z');

                INSERT INTO attempt_history VALUES (
                    'sub-eligible-1', @fact_id, @operation, @left, @right,
                    @correct_result, @correct_result, 1, 'Correct',
                    800, '2026-09-09T00:00:00Z');

                INSERT INTO fsrs_card_state VALUES (
                    @fact_id, @eligible_card_id, 2, NULL,
                    10.0, 4.0, 5, 1, 3);
            ";
            eligibleCmd.Parameters.AddWithValue("@fact_id", eligibleFact.Id);
            eligibleCmd.Parameters.AddWithValue("@operation", eligibleFact.Operation.ToString());
            eligibleCmd.Parameters.AddWithValue("@left", eligibleFact.LeftOperand);
            eligibleCmd.Parameters.AddWithValue("@right", eligibleFact.RightOperand);
            eligibleCmd.Parameters.AddWithValue("@correct_result", eligibleFact.CorrectResult);
            eligibleCmd.Parameters.AddWithValue("@eligible_card_id", eligibleCardId.ToString());
            await eligibleCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}