namespace MathFirst.Core.Tests;

using MathFirst.Application.Persistence;
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

    // ---------------------------------------------------------------------------
    // Test helper methods
    // ---------------------------------------------------------------------------

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
}