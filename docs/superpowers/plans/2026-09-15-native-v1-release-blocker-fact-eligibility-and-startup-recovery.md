# Native V1 Release Blocker: Fact Eligibility and Startup Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent any arithmetic fact from appearing before its canonical curriculum acquisition owner is unlocked, preserve historical learner state without premature review, ensure candidate-window queries cannot be starved by ineligible candidates, and make the reported in-place-update startup failure reproducible and recoverable without assuming unproven causes.

**Architecture:** Implement a single canonical curriculum eligibility policy (`owner_O(F) <= current_band_index`) across all layers: (1) SQLite review queries stream ordered candidates with eligibility applied before window truncation, (2) Snapshot fallback filters before sorting/truncation, (3) Adaptive selector applies pure defense filtering across all semantic pools, (4) Persistence submission validation rejects future locked attempts, (5) Schema migrations preserve legacy/future state losslessly as dormant rows, and (6) Session startup encapsulates initialization exceptions into a fail-closed, recoverable presentation state via a test-first recovery architecture.

**Tech Stack:** .NET 10, .NET MAUI Blazor, C# 13, SQLite, Microsoft.Data.Sqlite, xUnit, FSRS.Core 1.0.7

**Spec:** This document records the user-approved release-blocker specification and implementation plan derived from manual physical-device verification of rejected candidate `bf1d1cb5c7ceab8b4c18dd1bc9204ec0444b1f10`.

---

## Global Constraints

1. **Rejected Release Candidate**: The exact Git commit `bf1d1cb5c7ceab8b4c18dd1bc9204ec0444b1f10` and production-signed AAB (`0d188406aa32001a354c140587a7c4355b44bccee879d73e7975d5737cf53cfe`) are rejected due to manual physical-device verification failure (`REAL_DEVICE_VERIFICATION_FAILED`). Google Play upload remains strictly unauthorized.
2. **Governed Operation Modes**: This implementation plan was authored under `DOCUMENT_ONLY` governance. Subsequent implementation tasks must execute under their explicitly assigned operation modes (e.g. `IMPLEMENT_SLICE`, `CHECKPOINT_COMMIT_ONLY`, `REVIEW_ONLY`) as defined in `docs/PROMPT_AND_TASK_ROUTING.md`. Executors must never silently cross lifecycle boundaries.
3. **Language Invariant**: All agent reasoning, plan documentation, technical reports, and repository artifacts must be in English. No German repository content.
4. **Git Safety Invariants**: Strictly single canonical checkout at `C:\Dev\MathFirst`. Single writer. No `git reset`, `git clean`, `git stash`, `git rebase`, `git commit --amend`, force-pushing, branch deletion, or worktree deletion.
5. **Strict Staging Policy**: No `git add .` or `git add -A`. Stage only explicit file paths during subsequent implementation checkpoint commits.
6. **No Data Destruction**: Existing learner state, historical attempts, and FSRS cards must never be deleted or reset to resolve ineligibility. Ineligible facts remain dormant until their canonical band is reached.

---

## Authoritative Product Invariant: Practice Fact Eligibility

The user has explicitly rejected the earlier product assumption that "legacy/materialized learned facts remain reviewable regardless of the current band".

### The Practice Fact Eligibility Invariant
For arithmetic operation $O$ with current operation progression BandIndex $B$:
A fact $F$ may be presented in practice if and only if its canonical acquisition-owner band satisfies:
$$\text{owner}_O(F) \le B$$

### Detailed Semantic Rules
1. **Universal Scope**: The invariant applies to **every** presentation role:
   - `PracticeSelectionRole.New`
   - `PracticeSelectionRole.Frontier`
   - `PracticeSelectionRole.Due`
   - `PracticeSelectionRole.Maintenance`
   - `PracticeSelectionRole.EarlyReview`
   - `PracticeSelectionRole.Remediation`
2. **Role-Specific Acquisition Constraints**:
   - **New**: May introduce only unmaterialized facts owned by the **current band** ($= B$). Structured bands retain the deterministic representative sample of 16 facts.
   - **Frontier**: May select only materialized facts owned by the **current band** ($= B$).
   - **Review (Due, Maintenance, EarlyReview, Remediation)**: May select materialized facts owned by completed bands ($< B$) or the current band ($= B$). Must **never** select a materialized fact whose acquisition owner is a future band ($> B$).
3. **Historical Preservation & Dormancy**:
   - Persisted items and FSRS cards for facts with $\text{owner}_O(F) > B$ are **not deleted or altered**.
   - They become **dormant**: excluded from all practice selection and evidence pools while $B < \text{owner}_O(F)$.
   - When operation progression legitimately advances such that $B \ge \text{owner}_O(F)$, those facts automatically become eligible for review with their historical FSRS intervals and item statistics intact.
4. **Universal Operation Applicability**:
   - Addition (`ADD`)
   - Subtraction (`SUB`)
   - Multiplication (`MUL`)
   - Division (`DIV`)
5. **No Simplistic Approximation**: The eligibility policy must use canonical curriculum ownership as defined by `AcquisitionOwnershipResolver`. A simplistic rule such as $\max(\text{left}, \text{right}) \le \text{stage}$ is invalid because Subtraction and Division have non-trivial inverse frontier mappings.

---

## Forensic Findings

### Confirmed Root Causes (CERTAINTY: CONFIRMED)

1. **PRODUCT.md & ADR-0003 Contract Conflict**:
   - `docs/PRODUCT.md` (lines 64, 67) and `docs/decisions/ADR-0003` (line 35) explicitly stated: *"Legacy/materialized learned facts have persisted item or FSRS state and remain reviewable regardless of the current band."*
   - This architectural decision allowed review pools to select any materialized row for an operation regardless of the learner's progression stage.

2. **Multiplication Curriculum Ownership Proof for $2 \times 8$**:
   - In `ArithmeticCurriculum.cs` (lines 22, 45-64), dense multiplication bands (`MUL-D01` to `MUL-D12`) generate square frontiers:
     - BandIndex 0 (`MUL-D01`, Stage 1): $\{0,1\} \times \{0,1\}$
     - BandIndex 1 (`MUL-D02`, Stage 2): factors with maximum operand 2 ($\{0..2\} \times \{0..2\} \setminus \text{D01}$)
     - BandIndex 2 (`MUL-D03`, Stage 3): factors with maximum operand 3
     - BandIndex 7 (`MUL-D08`, Stage 8): factors with maximum operand 8, including `mul:2*8` and `mul:8*2`.
   - `AcquisitionOwnershipResolver` assigns `mul:2*8` to BandIndex 7 (`MUL-D08`).
   - At learner progression Stage 2 (BandIndex 1) or Stage 3 (BandIndex 2), `mul:2*8` has $\text{owner} = 7 > B$, making it a future locked fact.

3. **SQLite Semantic Review Queries Lack Eligibility Constraints**:
   - In `SqliteLearnerStore.cs` (`LoadPracticeSelectionEvidenceAsync`, lines 413–452):
     - `ReadCurrentBandCandidatesAsync` correctly filters by `item.fact_id IN (@request.CurrentBandOwnedFrontier)`.
     - `Due`, `Maintenance`, `Remediation`, and `EarlyReview` execute `ReadCandidatesAsync` with queries filtering only by `item.operation = @operation` and FSRS/remediation state.
     - They do **not** check whether `item.fact_id` belongs to completed bands or the current band.

4. **Snapshot Fallback Parity Gap**:
   - In `PracticeSelectionEvidence.cs` (`FromSnapshot`, lines 153–204), candidates are selected via `snapshot.ItemStates.Values.Where(state => state.Operation == request.Operation)`.
   - Review pools (`dueCandidates`, `maintenanceCandidates`, `remediationCandidates`, `earlyReviewCandidates`) are constructed from all materialized facts for the operation without acquisition-owner filtering.

5. **Adaptive Selector Trusts Semantic Pools**:
   - In `AdaptivePracticeSelector.cs` (`SelectTargetFact`, lines 125–170):
     - `duePool`, `maintenancePool`, `earlyReviewPool`, and `remediationPool` consume facts directly from `CandidateIndex` (which reflects the evidence object).
     - The selector does not apply an independent acquisition-ownership filter on review candidates.

6. **Persistence Acceptance Lacks Fact Eligibility Defense**:
   - In `SqliteLearnerStore.cs` (`ValidateNewAcceptedSubmission` and `ValidateAttemptAndRelatedState`, lines 893–1019):
     - Submissions are validated for arithmetic correctness, outcome consistency, latency, and progression increment.
     - No validation checks whether `changeSet.Attempt.FactId` is unlocked under `storedOperationProgressions[operation].BandIndex`. An arithmetically valid future fact passes validation and is committed to disk.

7. **MF-STAB-002 Slice 3 Context**:
   - Commit `b98782e964f2dee5a5fb2f0bce4ed182b5845b7b` removed an unconditional `Dense New` override in `AdaptivePracticeSelector.cs`.
   - This correctly restored scheduled authority to `Due`, `Maintenance`, and `Frontier` roles.
   - However, because review pools were already unbounded by curriculum ownership, enabling `Due` turns caused dormant/future materialized facts in the review queue to be selected and presented immediately.

8. **Existing Test Coverage Gap**:
   - `IndependentSelectorTests.cs`, `AdaptiveReviewStabilizationTests.cs`, `SqliteEnabledSubsetPersistenceTests.cs`, and `RuntimePersistenceRegressionTests.cs` test enabled operation interleaving, Dense New, and FSRS transitions.
   - None of the existing test suites assert the negative invariant: *future materialized facts must not be selected during Due, Maintenance, Remediation, or EarlyReview*.

---

### High-Confidence Structural Risks (CERTAINTY: HIGH-CONFIDENCE STRUCTURAL RISK)

1. **Candidate-Window Poisoning & Starvation Risk**:
   - Review queries in SQLite use `LIMIT 64` (`PracticeSelectionEvidenceRequest.CandidateWindowSize`).
   - If 64 future/ineligible facts match the FSRS due condition ahead of an eligible fact, a naive in-memory filter applied **after** `LIMIT 64` would discard all 64 candidates, leaving the pool empty and starving eligible reviews or causing false liveness failures.
   - **Requirement**: Eligibility filtering must be applied **before** window truncation.

2. **Startup / In-Place Update Exception Propagation**:
   - In `Home.razor` (`OnInitializedAsync`, lines 334–349), `await Session.InitializeAsync(startTiming: false)` runs with no `try ... catch` block.
   - If any exception occurs during store initialization, migration, snapshot loading, evidence prefetch, or initial fact selection (`AdvanceToNextFact`), the exception escapes `OnInitializedAsync`.
   - In MAUI Blazor, an unhandled exception in `OnInitializedAsync` crashes the component lifecycle, rendering a blank screen, stopped state, or unhandled UI error.
   - Furthermore, existing submission recovery in `TrainingSession.cs` (`RecoverFromPersistenceFailureAsync`) requires `LastEvaluation != null` and `LastPersistenceResult != null`, and `Home.razor` assumes `CurrentFact != null` on the practice surface. Blindly reusing submission `PersistenceFailure` for startup failure would cause recovery to fail immediately and could trigger a secondary `NullReferenceException` when rendering `CurrentFact`.

3. **Stale Future Materialized State Preservation across Schema Migrations**:
   - Migrations `MigrateV3ToV4Async`, `MigrateV4ToV5Async`, and `MigrateV5ToV6Async` preserved all `item_learning_state` and `fsrs_card_state` rows.
   - If a learner in V4 had practiced higher facts (or testing injected facts), migration mapped the operation progression to a lower band (e.g. BandIndex 0 or 1) while leaving the higher facts in the store.
   - Those facts immediately entered the review queries upon update.

---

### Unproven Hypotheses (CERTAINTY: UNPROVEN / REQUIRES REPRODUCTION)

1. **Exact Historical Trigger of the Physical Device In-Place Update Failure**:
   - Because application data was cleared on the user's device, the exact database file and Android logcat trace were lost.
   - Hypotheses for the one-time failure include:
     - SQLite database lock or journal mode transition conflict during in-place APK upgrade;
     - Selector empty-candidate exception (`InvalidOperationException: No valid target practice candidate exists...`) caused by stale data interaction during initial evidence prefetch;
     - Transient corrupted state in preferences or store revision.
   - The root cause is **unproven**, but the structural vulnerability (lack of startup exception handling, unhandled null fact states, and empty candidate defense) is confirmed and must be hardened with test-first reproductions.

---

## Minimal Robust Architecture: Fact Eligibility & Candidate-Window Integrity

### 1. Canonical Curriculum Eligibility Policy
We establish a pure domain policy on `AcquisitionOwnershipResolver` that determines whether a fact is eligible for practice under a given operation progression:
$$\text{IsEligible}(\text{FactId}, B) \iff \text{TryGetOwner}(\text{FactId}, B, \text{out var owner}) \land \text{owner} \le B$$

For candidate filtering and persistence validation:
- Canonical facts owned by bands $\le B$ return `true`.
- Future facts ($\text{owner} > B$), malformed/empty/whitespace strings, cross-operation fact IDs, and curriculum-invalid fact IDs fail closed returning `false`.
- Programmatic constructor violations in domain types preserve existing argument validation exceptions.

### 2. Candidate-Window Bounded Behavior (Anti-Poisoning Architecture)
To guarantee that ineligible future facts never starve eligible facts behind them:

```
+-------------------------------------------------------------------------+
| SQLite Query (ORDER BY due_practice_position, last_review, fact_id)     |
+-------------------------------------------------------------------------+
                                    |
                                    v (Stream SqliteDataReader)
+-------------------------------------------------------------------------+
| Eligibility Filter: IsEligible(fact.Id, request.CurrentBandIndex)        |
+-------------------------------------------------------------------------+
       |                                          |
       | (Ineligible / Future fact)               | (Eligible fact)
       v                                          v
    [Skip]                              [Add to Candidate List]
                                                  |
                                                  v
                                     Count == CandidateWindowSize (64)?
                                        /                  \
                                     [Yes]                 [No]
                                       |                     |
                                [Stop Streaming]       [Read Next Row]
```

- **Why This Architecture?**:
  1. **Correctness**: Guarantees that up to 64 **eligible** candidates are returned, even if thousands of ineligible future facts precede them in FSRS due order.
  2. **Strictly Bounded Memory with Early Termination**: Guarantees strictly bounded application memory ($O(1)$ stream buffer and 64-item candidate list) with early-terminating streaming traversal bounded by the total number of materialized rows for the requested operation. Traversal stops as soon as `CandidateWindowSize = 64` eligible candidates are collected. If fewer than 64 eligible rows exist, traversal consumes the finite matching operation rows without false empty pools.
  3. **Preserves Exact Semantic Ordering**: Preserves the exact FSRS due, maintenance, remediation, and early review SQL ordering without modifying database schema.
  4. **Parity**: The exact same eligibility predicate is applied in `PracticeSelectionEvidence.FromSnapshot` (`candidates.Where(IsEligible).OrderBy(...).Take(64)`).

### 3. Multi-Layer Defense-in-Depth

| Layer | Boundary | Defense Responsibility |
|---|---|---|
| **1. Evidence Generation (SQLite)** | `SqliteLearnerStore.cs` | Stream ordered candidates; filter by `IsEligible(factId, currentBandIndex)` before truncating to `CandidateWindowSize` (64). |
| **2. Evidence Generation (Snapshot)** | `PracticeSelectionEvidence.cs` | Filter in-memory snapshot item states by `IsEligible(factId, currentBandIndex)` before sorting and truncating to 64. |
| **3. Selector Pure Defense** | `AdaptivePracticeSelector.cs` | Filter all candidate pools (`Due`, `Maintenance`, `EarlyReview`, `Remediation`, `Frontier`) by `IsEligible` against current band index before candidate selection. |
| **4. Persistence Gate** | `SqliteLearnerStore.ValidateNewAcceptedSubmission` | Validate that `changeSet.Attempt.FactId` satisfies `IsEligible(attempt.FactId, storedBandIndex)`. Fail closed with `PersistenceResult.InvalidSubmission` if violated. |
| **5. Migration Preservation** | `SqliteLearnerStore.cs` | Preserve all item and FSRS rows across migrations without modification; ineligible future facts remain dormant until progression reaches their band. |
| **6. TrainingSession & UI Startup** | `TrainingSession.cs` & `Home.razor` | Guard `InitializeAsync` and `OnInitializedAsync` with structured exception handling; transition to a recoverable UI state (via a dedicated startup error boundary or adapted recovery contract supported by RED tests) on failure rather than crashing or dereferencing a null `CurrentFact`. |

---

## File / Responsibility Map

| File Path | Component | Responsibility / Proposed Changes |
|---|---|---|
| `src/MathFirst.Domain/Curriculum/AcquisitionOwnershipResolver.cs` | Domain | Expose `IsEligible(string factId, int throughBandIndex)` helper with fail-closed edge-case semantics. |
| `src/MathFirst.Application/Persistence/PracticeSelectionEvidence.cs` | Application | Add `CurrentBandIndex` to `PracticeSelectionEvidenceRequest`. Update `FromSnapshot` to filter review pools by `IsEligible` before window truncation. |
| `src/MathFirst.Infrastructure.Sqlite/SqliteLearnerStore.cs` | Infrastructure | Update `ReadCandidatesAsync` to stream ordered rows and filter by `IsEligible` before capping at `CandidateWindowSize`. Update `ValidateNewAcceptedSubmission` to reject submissions for future locked facts. |
| `src/MathFirst.Application/Practice/AdaptivePracticeSelector.cs` | Application | Apply pure defense filtering in `SelectTargetFact` across `duePool`, `maintenancePool`, `earlyReviewPool`, and `remediationPool`. |
| `src/MathFirst.Application/TrainingSession.cs` | Application | Pass `currentProgression.BandIndex` into `PracticeSelectionEvidenceRequest`. Structure `InitializeAsync` error handling for test-proven startup recovery. |
| `src/MathFirst.App/Components/Pages/Home.razor` | UI | Add structured exception handling in `OnInitializedAsync` to render recoverable retry UI on startup error without dereferencing `CurrentFact`. |
| `tests/MathFirst.Core.Tests/FactEligibilityRegressionTests.cs` | Tests | **[NEW]** Comprehensive test suite covering concrete $2 \times 8$ regression, all 4 operations, window poisoning, unlock transitions, persistence defense, edge cases, and snapshot parity. |
| `tests/MathFirst.Core.Tests/StartupRecoveryRegressionTests.cs` | Tests | **[NEW]** Synthetic startup failure tests verifying initialization resilience, safe UI rendering without `CurrentFact`, and non-destructive recovery. |
| `docs/PRODUCT.md` | Docs | Supersede Section 4 legacy review text with the authoritative Practice Fact Eligibility Invariant. |
| `docs/decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md` | Docs | **[NEW (Planned)]** Authoritative ADR superseding ADR-0003 fact-space semantics and recording startup recovery design. |

---

## Bite-Sized Implementation Tasks

### Task 1: Domain Eligibility Contract and Resolver Extensions

- **Primary Files**:
  - `src/MathFirst.Domain/Curriculum/AcquisitionOwnershipResolver.cs`
- **Interfaces Consumed**: `OperationCurriculum`, `CurriculumBand`, `ArithmeticFact`
- **Interfaces Produced**: `bool IsEligible(string factId, int throughBandIndex)`
- **Minimal Implementation**:
  Add `public bool IsEligible(string factId, int throughBandIndex)` to `AcquisitionOwnershipResolver`.
  The method must fail closed (return `false`) for null, empty, whitespace-only, malformed, cross-operation, curriculum-invalid, or negative/out-of-range band indices.
  When valid, it returns `TryGetOwner(factId, throughBandIndex, out var owner) && owner <= throughBandIndex`.
- **Explicit RED Tests**:
  - `AcquisitionOwnershipResolverTests.IsEligible_Multiplication_2x8_IsIneligibleAtBand1And2_EligibleAtBand7`
  - `AcquisitionOwnershipResolverTests.IsEligible_Addition_TensFact_IsIneligibleAtDenseBand0_EligibleAtStructuredBand`
  - `AcquisitionOwnershipResolverTests.IsEligible_Subtraction_InverseFact_IsIneligibleAtDenseBand0_EligibleAtInverseBand`
  - `AcquisitionOwnershipResolverTests.IsEligible_Division_LargeDivisor_IsIneligibleAtBand1_EligibleAtDivBand`
  - `AcquisitionOwnershipResolverTests.IsEligible_MalformedOrWhitespaceFactId_FailsClosedReturningFalse`
  - `AcquisitionOwnershipResolverTests.IsEligible_CrossOperationFactId_FailsClosedReturningFalse`
  - `AcquisitionOwnershipResolverTests.IsEligible_CurriculumInvalidOrDivisionByZeroFactId_FailsClosedReturningFalse`
  - `AcquisitionOwnershipResolverTests.IsEligible_NegativeOrBeyondCurriculumBandIndex_FailsClosedReturningFalse`
- **Test Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~AcquisitionOwnershipResolverTests"`
- **Expected RED Reason**: Method `IsEligible` does not exist on `AcquisitionOwnershipResolver`.
- **Verification GREEN Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~AcquisitionOwnershipResolverTests"`
- **Eventual Staging Paths**:
  - `src/MathFirst.Domain/Curriculum/AcquisitionOwnershipResolver.cs`
  - `tests/MathFirst.Core.Tests/AcquisitionOwnershipResolverTests.cs`

---

### Task 2: Snapshot Fallback Eligibility and Bounded-Window Parity

- **Primary Files**:
  - `src/MathFirst.Application/Persistence/PracticeSelectionEvidence.cs`
- **Interfaces Consumed**: `AcquisitionOwnershipResolver`, `LearnerSnapshot`, `PracticeSelectionEvidenceRequest`
- **Interfaces Produced**: `PracticeSelectionEvidenceRequest.CurrentBandIndex`, `PracticeSelectionEvidence.FromSnapshot` with eligibility filtering
- **Minimal Implementation**:
  1. Add `int CurrentBandIndex { get; }` to `PracticeSelectionEvidenceRequest` (validated $\ge 0$).
  2. In `PracticeSelectionEvidence.FromSnapshot`, instantiate `AcquisitionOwnershipResolver` for `request.Operation` and filter `snapshot.ItemStates.Values` where `ownership.IsEligible(state.FactId, request.CurrentBandIndex)` before building `remediationCandidates`, `dueCandidates`, `maintenanceCandidates`, and `earlyReviewCandidates`.
- **Explicit RED Tests**:
  - `FactEligibilityRegressionTests.Snapshot_FutureDueFact_ExcludedFromEvidencePools`
  - `FactEligibilityRegressionTests.Snapshot_EligibleEarlierFact_RetainedInEvidencePools`
  - `FactEligibilityRegressionTests.Snapshot_WindowPoisoning_EligibleFactDiscoveredBehind64IneligibleFacts`
- **Test Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~FactEligibilityRegressionTests.Snapshot_"`
- **Expected RED Reason**: `PracticeSelectionEvidenceRequest` constructor does not accept `currentBandIndex`; future facts appear in `dueCandidates`.
- **Verification GREEN Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~FactEligibilityRegressionTests.Snapshot_"`
- **Eventual Staging Paths**:
  - `src/MathFirst.Application/Persistence/PracticeSelectionEvidence.cs`
  - `tests/MathFirst.Core.Tests/FactEligibilityRegressionTests.cs`

---

### Task 3: SQLite Store Eligibility Streaming & Anti-Poisoning Window Filtering

- **Primary Files**:
  - `src/MathFirst.Infrastructure.Sqlite/SqliteLearnerStore.cs`
- **Interfaces Consumed**: `ILearnerStore`, `AcquisitionOwnershipResolver`, `PracticeSelectionEvidenceRequest`
- **Interfaces Produced**: `SqliteLearnerStore.LoadPracticeSelectionEvidenceAsync` with pre-truncation streaming filter
- **Minimal Implementation**:
  1. In `SqliteLearnerStore.ReadCandidatesAsync`, remove hard `LIMIT @limit` from the SQL query.
  2. Stream rows via `SqliteDataReader` (memory-bounded $O(1)$ stream buffer).
  3. For each row, construct the fact and check `ownership.IsEligible(fact.Id, request.CurrentBandIndex)`.
  4. If eligible, add to candidate list.
  5. When `candidateList.Count == request.CandidateWindowSize` (64), break reader loop and return immediately.
- **Explicit RED Tests**:
  - `FactEligibilityRegressionTests.Sqlite_Multiplication_2x8_NotReturnedInDuePoolAtBand1`
  - `FactEligibilityRegressionTests.Sqlite_WindowPoisoning_70FutureDueFacts_DoNotStarveEligibleDueFact`
  - `FactEligibilityRegressionTests.Sqlite_AllFourOperations_FutureFactsDormant`
  - `FactEligibilityRegressionTests.SqliteAndSnapshot_Parity_IdenticalOutcome`
- **Test Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~FactEligibilityRegressionTests.Sqlite_"`
- **Expected RED Reason**: `SqliteLearnerStore` returns `mul:2*8` in Due pool at BandIndex 1; 70 future facts push eligible fact beyond window.
- **Verification GREEN Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~FactEligibilityRegressionTests.Sqlite_"`
- **Eventual Staging Paths**:
  - `src/MathFirst.Infrastructure.Sqlite/SqliteLearnerStore.cs`
  - `tests/MathFirst.Core.Tests/FactEligibilityRegressionTests.cs`

---

### Task 4: Selector Pure Defense Layer

- **Primary Files**:
  - `src/MathFirst.Application/Practice/AdaptivePracticeSelector.cs`
- **Interfaces Consumed**: `PracticeSelectionContext`, `AcquisitionOwnershipResolver`
- **Interfaces Produced**: Defense-in-depth candidate filtering in `SelectTargetFact`
- **Minimal Implementation**:
  In `AdaptivePracticeSelector.SelectTargetFact`:
  Even if malformed evidence reaches the selector, filter `duePool`, `maintenancePool`, `earlyReviewPool`, and `remediationPool` using `ownership.IsEligible(fact.Id, progression.BandIndex)` before resolving roles and selecting target candidate.
- **Explicit RED Tests**:
  - `FactEligibilityRegressionTests.Selector_MalformedEvidenceWithFutureFact_RejectedByPureDefense`
  - `FactEligibilityRegressionTests.Selector_LowMultiplication_NeverSelects2x8`
- **Test Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~FactEligibilityRegressionTests.Selector_"`
- **Expected RED Reason**: Selector currently blindly trusts `CandidateIndex.DueFacts`.
- **Verification GREEN Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~FactEligibilityRegressionTests.Selector_"`
- **Eventual Staging Paths**:
  - `src/MathFirst.Application/Practice/AdaptivePracticeSelector.cs`
  - `tests/MathFirst.Core.Tests/FactEligibilityRegressionTests.cs`

---

### Task 5: Persistence Acceptance Validation Gate

- **Primary Files**:
  - `src/MathFirst.Infrastructure.Sqlite/SqliteLearnerStore.cs`
- **Interfaces Consumed**: `SubmissionChangeSet`, `ValidateNewAcceptedSubmission`
- **Interfaces Produced**: Atomic rejection of attempts for locked future or invalid facts
- **Minimal Implementation**:
  In `SqliteLearnerStore.ValidateNewAcceptedSubmission`:
  Resolve ownership of `changeSet.Attempt.FactId` using `curriculum.GetCurriculum(changeSet.Attempt.Operation)`.
  Assert `ownership.IsEligible(changeSet.Attempt.FactId, stored.BandIndex)` against `storedOperationProgressions[changeSet.Attempt.Operation].BandIndex`.
  If false, throw `InvalidOperationException("The attempted fact is not unlocked by the operation's current progression.")`.
  This fails closed before any `INSERT` or `UPDATE`, causing transaction rollback and returning `PersistenceResult.InvalidSubmission`.
- **Explicit RED Tests**:
  - `FactEligibilityRegressionTests.Persistence_FutureLockedFactSubmission_RejectedWithoutStateMutation`
  - `FactEligibilityRegressionTests.Persistence_ValidCurrentBandSubmission_Succeeds`
  - `FactEligibilityRegressionTests.Persistence_MalformedOrCrossOperationFactSubmission_RejectedWithoutStateMutation`
- **Test Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~FactEligibilityRegressionTests.Persistence_"`
- **Expected RED Reason**: `CommitSubmissionAsync` commits future fact submission successfully.
- **Verification GREEN Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~FactEligibilityRegressionTests.Persistence_"`
- **Eventual Staging Paths**:
  - `src/MathFirst.Infrastructure.Sqlite/SqliteLearnerStore.cs`
  - `tests/MathFirst.Core.Tests/FactEligibilityRegressionTests.cs`

---

### Task 6: Migration Stale State Dormancy & Upgrade Parity

- **Primary Files**:
  - `src/MathFirst.Infrastructure.Sqlite/SqliteLearnerStore.cs`
- **Interfaces Consumed**: `MigrateV4ToV5Async`, `MigrateV5ToV6Async`
- **Minimal Implementation**:
  Verify and ensure that schema migrations preserve all legacy rows without deletion, while the new eligibility policy guarantees that migrated future facts remain dormant until progression reaches their band.
- **Explicit RED Tests**:
  - `FactEligibilityRegressionTests.Migration_V4WithFutureFacts_MigratesSuccessfullyAndKeepsFutureFactsDormant`
  - `FactEligibilityRegressionTests.Migration_ProgressionAdvanceToBand7_UnlocksHistorical2x8`
- **Test Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~FactEligibilityRegressionTests.Migration_"`
- **Expected RED Reason**: After migration, future facts in V4 DB appear in review selection immediately.
- **Verification GREEN Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~FactEligibilityRegressionTests.Migration_"`
- **Eventual Staging Paths**:
  - `src/MathFirst.Infrastructure.Sqlite/SqliteLearnerStore.cs`
  - `tests/MathFirst.Core.Tests/FactEligibilityRegressionTests.cs`

---

### Task 7: TrainingSession & Startup Exception Recovery Contract

- **Primary Files**:
  - `src/MathFirst.Application/TrainingSession.cs`
  - `src/MathFirst.App/Components/Pages/Home.razor`
- **Interfaces Consumed**: `TrainingSession.InitializeAsync`, `Home.razor.OnInitializedAsync`
- **Interfaces Produced**: Test-proven, non-destructive startup recovery contract
- **Architectural Decision Boundary**:
  The implementation must NOT assume that standard post-submission `PersistenceFailure` is compatible with startup failure. Existing submission recovery requires `LastEvaluation != null` and `LastPersistenceResult != null`, and `Home.razor` renders `Session.CurrentFact` on active practice surfaces.
  The implementer must execute RED tests first and select the smallest compatible architecture:
  - **Option A (Dedicated Startup/Initialization Failure State or UI Boundary - Preferred)**: Introduce a dedicated startup error state / UI boundary in `Home.razor` and/or `TrainingSession` that captures initialization exceptions, renders a safe retry UI without referencing `Session.CurrentFact` or `LastEvaluation`, and re-invokes `InitializeAsync` on retry.
  - **Option B (Generalized Persistence Recovery Contract)**: Adapt `TrainingSession.RecoverFromPersistenceFailureAsync` to support `LastEvaluation == null` when recovering from initialization failure, ensuring `Home.razor` safely handles `CurrentFact == null` during recovery presentation.
- **Minimal Implementation**:
  1. In `TrainingSession.LoadSingleOperationEvidenceAsync`, pass `progression.BandIndex` to `PracticeSelectionEvidenceRequest`.
  2. Author RED tests reproducing initialization failure (evidence loading failure, database transient lock).
  3. Implement the chosen recovery option (Option A or Option B) ensuring:
     - Initialization failure never throws unhandled exceptions out of `Home.razor.OnInitializedAsync`.
     - Error presentation never evaluates properties on a null `CurrentFact`.
     - Retry cleanly re-runs initialization.
     - Successful retry yields a valid `CurrentFact` and normal practice state.
     - No learner data is deleted or reset.
- **Explicit RED Tests**:
  - `StartupRecoveryRegressionTests.InitializationEvidenceFailure_DoesNotRequireCurrentFactToRenderRecovery`
  - `StartupRecoveryRegressionTests.InitializationEvidenceFailure_RetryReinitializesSessionWithoutLearnerDataReset`
  - `StartupRecoveryRegressionTests.InitializationFailure_DoesNotUseSubmissionRecoveryPreconditionsUnlessContractExplicitlySupportsIt`
  - `StartupRecoveryRegressionTests.StartupRecovery_AfterTransientEvidenceFailure_ProducesValidCurrentFact`
  - `StartupRecoveryRegressionTests.Restart_PreservesLearnerStateAndMaintainsEligibility`
- **Test Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~StartupRecoveryRegressionTests"`
- **Expected RED Reason**: `InitializeAsync` throws unhandled exceptions and crashes component initialization on store failure.
- **Verification GREEN Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~StartupRecoveryRegressionTests"`
- **Eventual Staging Paths**:
  - `src/MathFirst.Application/TrainingSession.cs`
  - `src/MathFirst.App/Components/Pages/Home.razor`
  - `tests/MathFirst.Core.Tests/StartupRecoveryRegressionTests.cs`

---

### Task 8: Long-Run Deterministic Simulation & Full Regression Suite

- **Primary Files**:
  - `tests/MathFirst.Core.Tests/FactEligibilityRegressionTests.cs`
  - `tests/MathFirst.Core.Tests/LongRunIndependentProgressionTests.cs`
- **Minimal Implementation**:
  Execute 500-step deterministic practice simulations starting from various low progression bands across all 4 operations. Assert that every single presented `CurrentFact` satisfies $\text{owner}_O(F) \le \text{BandIndex}$ at the exact moment of presentation.
- **Explicit RED Tests**:
  - `FactEligibilityRegressionTests.LongRun_500Steps_LowProgression_ZeroIneligiblePresentations`
  - `FactEligibilityRegressionTests.UnlockTransition_FactBecomesEligibleImmediatelyUponBandAdvance`
- **Test Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj --filter "FullyQualifiedName~FactEligibilityRegressionTests.LongRun_"`
- **Verification GREEN Command**:
  `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj`
- **Eventual Staging Paths**:
  - `tests/MathFirst.Core.Tests/FactEligibilityRegressionTests.cs`

---

### Task 9: Documentation Reconciliation & ADR-0007 Authoring

- **Primary Files**:
  - `docs/PRODUCT.md`
  - `docs/decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md`
  - `docs/TESTING.md`
  - `docs/PROJECT_STATE.md`
  - `docs/CURRENT_WORK.md`
  - `docs/ROADMAP.md`
  - `CHANGELOG.md`
- **Minimal Implementation**:
  1. Author `ADR-0007` formally superseding ADR-0003 fact-space semantics and recording the Practice Fact Eligibility Invariant.
  2. Update `docs/PRODUCT.md` Section 4 to reflect that review is bounded by curriculum ownership ($\text{owner} \le B$).
  3. Update `docs/TESTING.md`, `PROJECT_STATE.md`, `CURRENT_WORK.md`, `ROADMAP.md`, and `CHANGELOG.md` to document the rejection of `bf1d1cb` and the remediation plan.
- **Eventual Staging Paths**:
  - All listed documentation files.

---

## Release Consequence & Post-Remediation Lifecycle

### Authoritative Rejected Release Candidate
- **Source Git SHA**: `bf1d1cb5c7ceab8b4c18dd1bc9204ec0444b1f10`
- **Signed Android AAB SHA-256**: `0d188406aa32001a354c140587a7c4355b44bccee879d73e7975d5737cf53cfe`
- **Verification Status**: `REAL_DEVICE_VERIFICATION_FAILED`
- **Release Status**: `RELEASE_CANDIDATE_REJECTED_PENDING_REMEDIATION`
- **Action**: The signed AAB is preserved as historical evidence only. Publication to Google Play is strictly forbidden.

### Post-Remediation Verification Protocol
Once the implementation tasks are completed, the release pipeline must be repeated in full:
1. **REVIEW_ONLY**: Comprehensive technical review of the forensic plan and code changes.
2. **Commit & Full Validation**: Execute all unit, integration, and regression suites.
3. **Merge to Main**: Merge remediation branch into `main` via approved Pull Request.
4. **Post-Merge Sync**: Fast-forward local canonical checkout to the new `main` HEAD SHA.
5. **New Release Candidate Established**: Establish new exact candidate SHA from `main`.
6. **Package & Sign**: Execute production release packaging and APK/AAB signing.
7. **Technical Smoke Verification**: Install signed artifact on a clean physical Android device; verify native launch, IME suppression, touch keypad, and database creation.
8. **Manual Physical-Device Functional Verification**: Verify that only facts from unlocked stages appear in practice (e.g. at Stage 2/3 multiplication, no facts beyond $2 \times 2$ or $3 \times 3$ appear).
9. **Release Gate**: Only upon successful completion of all steps may Google Play upload be reconsidered.
