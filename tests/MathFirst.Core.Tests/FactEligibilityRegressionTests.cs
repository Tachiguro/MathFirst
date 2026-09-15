namespace MathFirst.Core.Tests;

using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
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
}