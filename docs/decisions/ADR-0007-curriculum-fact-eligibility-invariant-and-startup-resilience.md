# ADR-0007: Curriculum Fact Eligibility Invariant and Startup Resilience

## Status

Accepted

## Date

2026-09-18

## Context

During manual physical-device verification of the native V1 release candidate, two release-blocking defects were identified:

1. **Curriculum Fact Eligibility Violation in Review Pools**:
   - Facts owned by future, locked curriculum bands could appear in practice through historical and review evidence paths (`Due`, `Maintenance`, `Early Review`, `Remediation`).
   - For example, a multiplication fact such as `mul:2*8` (canonically owned by BandIndex 7, `MUL-D08`, Stage 8) could be selected and presented while the learner was progressing through Stage 2 (`MUL-D02`, BandIndex 1) or Stage 3 (`MUL-D03`, BandIndex 2).
   - Investigation revealed that [ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md) and [docs/PRODUCT.md](../PRODUCT.md) previously specified that *"Legacy/materialized learned facts ... remain reviewable regardless of the learner's current acquisition band."*
   - Furthermore, review candidate queries in SQLite (`SqliteLearnerStore.cs`) and snapshot generation (`PracticeSelectionEvidence.cs`) selected candidates based solely on operation and FSRS/remediation state without checking curriculum progression, and `AdaptivePracticeSelector` trusted those review pools.
   - When commit `b98782e` removed an unconditional Dense New override to restore scheduled review turns (`MF-STAB-002` Slice 3), dormant/future materialized facts in the review queue were immediately exposed.
   - This defect directly violated curriculum progression and invalidated the native V1 release candidate.

2. **In-Place Update and Session Startup Resilience Gap**:
   - An in-place APK update test on physical hardware reported a startup hang / initialization failure.
   - In `Home.razor`, `Session.InitializeAsync` was invoked during `OnInitializedAsync` without structured exception handling, allowing store or evidence loading errors to escape the Blazor component lifecycle and crash the UI.
   - Existing persistence recovery in `TrainingSession.cs` (`RecoverFromPersistenceFailureAsync`) was designed strictly for post-submission errors, assuming non-null `LastEvaluation` and `LastPersistenceResult`, while `Home.razor` assumed a non-null `CurrentFact` on active practice surfaces. Reusing submission recovery for startup failures led to secondary exceptions.

## Rejected Release Candidate

The identified defects resulted in the formal rejection of the native V1 release candidate:

- **Rejected source Git SHA**: `bf1d1cb5c7ceab8b4c18dd1bc9204ec0444b1f10`
- **Rejected signed Android AAB SHA-256**: `0d188406aa32001a354c140587a7c4355b44bccee879d73e7975d5737cf53cfe`
- **Verification status**: `REAL_DEVICE_VERIFICATION_FAILED`
- **Release status**: `RELEASE_CANDIDATE_REJECTED_PENDING_REMEDIATION`

The rejected signed Android App Bundle is preserved as historical evidence only. It MUST NOT be represented as releasable or publishable, and upload to Google Play remains strictly unauthorized.

## Decision

### 1. The Practice Fact Eligibility Invariant

MathFirst establishes the **Practice Fact Eligibility Invariant**:

For any arithmetic fact $F$ of operation $O$ presented at the current operation progression band $B$:
$$\text{owner}_O(F) \le B$$

#### Semantic Rules:
1. **Universal Role Scope**: The invariant applies unconditionally to **every** presentation role:
   - `PracticeSelectionRole.New`
   - `PracticeSelectionRole.Frontier`
   - `PracticeSelectionRole.Due`
   - `PracticeSelectionRole.Maintenance`
   - `PracticeSelectionRole.EarlyReview`
   - `PracticeSelectionRole.Remediation`
2. **Canonical Acquisition Ownership**:
   - Canonical acquisition ownership is determined solely by `AcquisitionOwnershipResolver` via canonical curriculum generation formulas.
   - Every exact arithmetic fact has exactly one canonical acquisition-owner band per operation.
   - Simplistic heuristic bounds (such as $\max(\text{left}, \text{right}) \le \text{stage}$) are invalid because Subtraction and Division have non-trivial inverse frontier mappings.
3. **Role-Specific Constraints**:
   - **New**: May introduce only unmaterialized facts owned by the current band ($= B$).
   - **Frontier**: May select only materialized facts owned by the current band ($= B$).
   - **Review (`Due`, `Maintenance`, `EarlyReview`, `Remediation`)**: May select materialized facts owned by completed bands ($< B$) or the current band ($= B$). Must **never** select a fact owned by a future band ($> B$).
4. **Historical Preservation & Future Fact Dormancy**:
   - Persisted items and FSRS cards for facts with $\text{owner}_O(F) > B$ remain valid durable learner history and are **never deleted, reset, or altered**.
   - Future-owned facts remain **dormant**: excluded from all practice selection and review evidence pools while $B < \text{owner}_O(F)$.
   - When operation progression legitimately advances such that $B \ge \text{owner}_O(F)$, those facts automatically become eligible for review with their historical FSRS intervals and item statistics intact.
5. **Durable Distinction**:
   $$\text{PERSISTED} \ne \text{CURRENTLY PRESENTABLE}$$
   $$\text{DUE} \ne \text{AUTOMATICALLY ELIGIBLE}$$
   FSRS due status optimizes review scheduling among eligible facts, but does not grant authority to bypass canonical curriculum ownership.

### 2. Supersession of ADR-0003 Fact-Space Semantics

This decision explicitly **supersedes** the review-eligibility semantics of [ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md).

Specifically, the superseded rule from ADR-0003 Section 3 ("Fact-space terminology", Item 1):
> *"Legacy/materialized learned facts have persisted item or FSRS state. They remain reviewable regardless of the learner's current acquisition band."*

is superseded and replaced by:
> Materialized facts remain durable historical learner evidence, but presentation and review eligibility is strictly bounded by canonical curriculum ownership ($\text{owner}_O(F) \le B$). A Due fact is not automatically review-eligible merely because its FSRS due Practice Position has arrived.

ADR-0003 remains fully authoritative for all architecture not superseded here, including:
- Independent operation progression;
- Hybrid curriculum model (exhaustive dense foundation followed by structured families);
- Canonical fact identity and presentation-direction distinction;
- Unique acquisition ownership;
- Open-ended fact-space architecture and `Int32` checked arithmetic safety;
- Schema V5 durable progression concepts and migration principles.

### 3. Multi-Layer Defense-in-Depth

The Practice Fact Eligibility Invariant is enforced across eight complementary layers:

1. **Domain Eligibility Contract**: `AcquisitionOwnershipResolver.IsEligible(string factId, int throughBandIndex)` provides a pure domain predicate that evaluates canonical ownership and fails closed (`false`) for null, empty, whitespace-only, malformed, cross-operation, division-by-zero, or out-of-range band index arguments.
2. **Snapshot Evidence Filtering**: `PracticeSelectionEvidence.FromSnapshot` receives `CurrentBandIndex` via `PracticeSelectionEvidenceRequest` and filters snapshot item states using `IsEligible` before constructing review pools (`dueCandidates`, `maintenanceCandidates`, `remediationCandidates`, `earlyReviewCandidates`).
3. **SQLite Streaming & Anti-Poisoning Window Filtering**: `SqliteLearnerStore.ReadCandidatesAsync` removes SQL `LIMIT 64`, streams rows through `SqliteDataReader` ($O(1)$ memory buffer), applies `IsEligible(fact.Id, request.CurrentBandIndex)` per row, and terminates streaming as soon as `CandidateWindowSize = 64` eligible candidates are gathered. This guarantees that up to 64 eligible candidates are discovered even if hundreds of ineligible future due facts precede them in SQL ordering.
4. **Selector Pure Defense**: `AdaptivePracticeSelector.SelectTargetFact` defensively filters all semantic pools (`duePool`, `maintenancePool`, `earlyReviewPool`, `remediationPool`) by `IsEligible` against current `progression.BandIndex` before candidate ranking or role resolution.
5. **Persistence Acceptance Gate**: `SqliteLearnerStore.ValidateNewAcceptedSubmission` verifies `ownership.IsEligible(changeSet.Attempt.FactId, stored.BandIndex)` against stored progression before executing any SQL write. Submissions for future locked facts throw `InvalidOperationException` and trigger transactional rollback (`PersistenceResult.InvalidSubmission`).
6. **Migration & Restart Dormancy**: Schema migrations (V3 $\to$ V4 $\to$ V5 $\to$ V6) preserve all historical item and FSRS rows without data deletion, while the eligibility policy ensures migrated future facts remain dormant until progression unlocks their band.
7. **Session Request Wiring**: `TrainingSession.LoadSingleOperationEvidenceAsync` extracts the real `progression.BandIndex` and passes it into `PracticeSelectionEvidenceRequest`.
8. **Runtime Invariant Verification**: Continuous 500-step deterministic simulation suites verify that 100% of presented facts satisfy $\text{owner}_O(F) \le \text{BandIndex}_O$ across all operations and progression transitions.

### 4. Startup Resilience and Recovery Contract

To protect the application against startup failures and corrupted/transient store conditions:

1. **Initialization Contract**: `TrainingSession.InitializeAsync` is considered successful only when store initialization, snapshot loading, and initial fact preparation (`AdvanceToNextFact`) complete successfully, leaving `IsInitialized == true`.
2. **Fail-Closed Presentation**: If an exception occurs during `TrainingSession.InitializeAsync`, the session remains in `IsInitialized == false`. `Home.razor` catches the exception in `OnInitializedAsync`, prevents lifecycle termination, and renders a dedicated startup error boundary (`_startupFailed = true`).
3. **Safe UI State without `CurrentFact`**: The startup error boundary renders an explicit error notice and a retry action without evaluating `Session.CurrentFact`, `LastEvaluation`, or `LastPersistenceResult`, avoiding `NullReferenceException`.
4. **Non-Destructive Retry**: Activating retry in `Home.razor` re-invokes `Session.InitializeAsync(startTiming: false)`. On success, `_startupFailed` is cleared and the session proceeds to the `InitialReadyGate`. Learner progress is never deleted or reset.
5. **Separation from Submission Persistence Recovery**: Startup recovery is architecturally separated from `SessionInteractionState.PersistenceFailure` / `RecoverFromPersistenceFailureAsync`, which remains dedicated to post-submission save failures.

## Consequences

### Positive
- Future-band facts can never appear in practice before their canonical curriculum band is unlocked.
- Spaced repetition and review scheduling are curriculum-bounded while preserving historical learner data losslessly.
- Candidate-window queries cannot be starved or poisoned by ineligible future rows.
- Application startup fails closed safely and provides user-accessible retry without crashing the MAUI Blazor host.
- Historical FSRS cards automatically reactivate with preserved intervals when progression advances to their band.

### Negative and Operational Costs
- Review candidate retrieval in SQLite streams ordered rows and performs domain ownership checks in application memory.
- Schema migrations preserve dormant rows that require filtering during active review queries.
- Release candidates require full post-remediation verification across all automated and physical testing layers.

## Release Consequence & Post-Remediation Lifecycle

Authoring and implementing this remediation does **not** constitute an approved release candidate. Reaching release readiness requires completing the strict post-remediation lifecycle:

1. **Comprehensive `REVIEW_ONLY`**: Complete review of forensic remediation documentation and code.
2. **Full Branch Validation (`FULL_VALIDATION`)**: Execute full unit, integration, and regression suites.
3. **Pull Request & Explicit Merge Authorization**: Submit PR to `main` and obtain affirmative user approval before merging.
4. **Merge to `main` & Local Checkout Sync**: Fast-forward canonical local checkout `C:\Dev\MathFirst` to the merged `main` HEAD.
5. **Exact Candidate SHA Establishment**: Establish new exact candidate SHA from synchronized `main`.
6. **Production Packaging & Signing**: Execute `MathFirst.ReleaseTool` to produce and sign a `Distributable` AAB.
7. **Technical Smoke Verification**: Verify native installation, IME suppression, touch keypad, and database creation on clean physical hardware.
8. **Manual Physical-Device Functional Verification**: Verify that only facts from unlocked stages appear in active practice across all operations.
9. **Release Gate**: Reconsider Google Play Store publication only after all preceding steps pass unconditionally.

## Alternatives Considered

1. **Deleting or Resetting Future Materialized Rows on Migration / Startup**:
   - *Rejected*: Violates core data-preservation invariants and destroys legitimate learner study history.
2. **In-Memory Filtering After SQL `LIMIT 64`**:
   - *Rejected*: Causes candidate-window poisoning and pool starvation when 64 ineligible future facts precede an eligible fact in FSRS due order.
3. **Allowing FSRS Due Cards to Bypass Curriculum Ownership**:
   - *Rejected*: Directly caused the reported $2 \times 8$ regression and violates the foundational pedagogical principle of progressive scaffolding.
4. **Reusing Submission `PersistenceFailure` for Startup Recovery**:
   - *Rejected*: Submission recovery requires non-null evaluation and persistence result objects; reusing it causes secondary null dereference crashes when `CurrentFact` is null.
