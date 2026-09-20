# ADR-0008: Independent Per-Operation Role Ordinals and Practice-Configuration Reconciliation

## Status

Accepted

## Date

2026-09-20

## Context

During physical-device verification of native V1 Build 2 (Step 31), a release-blocking defect was identified when switching operation subsets:

1. **Restart Role Misassignment and Crash Regression**:
   - A learner practiced Addition through 26 accepted attempts, navigated to Settings, changed the enabled practice operations to Subtraction only, and restarted the application.
   - Upon restart, the application failed to select valid practice or misassigned roles because `AdaptivePracticeSelector` derived the next requested role from global `PracticePosition` via:
     $$\text{RoleOrdinal} = \left\lfloor \frac{\text{PracticePosition} - 1}{\text{EnabledOperationCount}} \right\rfloor + 1$$
   - This formula incorrectly assumed that the currently enabled operation subset had existed for the learner's entire lifetime practice history. For a learner with 26 Addition attempts and 0 Subtraction attempts, the first Subtraction turn was assigned role ordinal $27 \equiv 7 \implies \text{PracticeSelectionRole.Due}$, immediately attempting to query Due or Frontier review facts before any Subtraction facts were materialized or due.
   - In small initial bands or cold operations, this caused candidate starvation, invalid fallback behavior, or assertion failures.

2. **Unconditional Question Preservation Defect**:
   - [MF-SET-001](../../docs/BACKLOG.md#mf-set-001-practice-configuration-operation-selection-and-adjustable-practice-time) documentation and runtime previously stated that *"A Settings change preserves the currently displayed question."*
   - If an unsubmitted question was displayed (e.g. an Addition problem) and the learner disabled Addition in Settings, returning to practice presented the now-disabled operation, violating the learner's explicit configuration choice.
   - Additionally, fire-and-forget background reconciliation created potential race conditions between UI navigation and session state updates.

This decision defines the architecture implemented in `MF-STAB-003`: independent per-operation role ordinals, durable count reconstruction in Schema V6, awaited Settings reconciliation, and a zero-mutation replacement contract for invalid unsubmitted facts.

---

## Decision

### 1. Global vs. Per-Operation Authority

MathFirst formally separates global practice ordering from per-operation role-cycle progression:

1. **Global `PracticePosition` Authority**:
   Global `PracticePosition` (monotonic count of lifetime accepted attempts across all operations) remains authoritative for:
   - Total accepted-attempt sequencing and deterministic ordering;
   - Deterministic enabled-operation scheduling via bounded permutation bags;
   - FSRS spaced repetition virtual time;
   - `DuePracticePosition` review calculations;
   - `BandStartedPracticePosition` progression boundaries;
   - SQLite store revision increments and persistence validation.

2. **Per-Operation Accepted-Attempt Count Authority**:
   The count of accepted attempts for a specific arithmetic operation $O$ is authoritative **only** for determining that operation's next requested role-cycle ordinal:
   $$\text{NextOperationAttemptOrdinal}(O) = \text{AcceptedAttemptCount}(O) + 1$$
   It is NOT the general authority for dense-band coverage tracking, pace estimates, fluency estimates, curriculum progression, or item learning state. Those subsystems retain their existing domain and persistence authorities (e.g. positioned attempt evidence within band instances, hierarchical latency shrinkage, evaluated attempt fluency, band advancement gates, and `ItemLearningState`).

3. **Repeating 10-Attempt Role Cycle**:
   For any scheduled operation $O$, its requested role is determined strictly by its 1-based attempt ordinal mapped across the repeating 10-slot cycle via 0-based remainder index $i = (\text{NextOperationAttemptOrdinal}(O) - 1) \bmod 10$:

   | 1-Based Ordinal (mod 10) | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 (0) |
   |---|---|---|---|---|---|---|---|---|---|---|
   | **0-Based Remainder $i$** | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
   | **Role** | New | Due | New | Maintenance | Frontier | New | Due | New | Due | Frontier |

   Equivalently, the role partition across 0-based remainder indices $i \in \{0, \dots, 9\}$ is:
   - **`New`**: $i \in \{0, 2, 5, 7\}$ (1st, 3rd, 6th, 8th attempts in cycle — 40% nominal share)
   - **`Due`**: $i \in \{1, 6, 8\}$ (2nd, 7th, 9th attempts in cycle — 30% nominal share)
   - **`Maintenance`**: $i \in \{3\}$ (4th attempt in cycle — 10% nominal share)
   - **`Frontier`**: $i \in \{4, 9\}$ (5th, 10th attempts in cycle — 20% nominal share)

4. **Explicit Rejection of Global Role Derivation**:
   The superseded formula $\lfloor(\text{PracticePosition} - 1) / k\rfloor + 1$ is explicitly rejected. Role derivation depends exclusively on how many times that specific operation has been practiced, ensuring that a newly enabled or cold operation always begins at Ordinal 1 (`New`, remainder $i = 0$) regardless of how many attempts have occurred in other operations.

---

### 2. Durable Count Authority & Schema V6 Preservation

1. **Schema V6 Authority**:
   No new schema version (e.g. Schema V7) or redundant persistent counter column is introduced. The persistence contract remains **Schema V6**.

2. **Durable Count Reconstruction**:
   For any operation $O$, `AcceptedAttemptCount(O)` is reconstructed directly from existing durable learner state in SQLite:
   $$\text{AcceptedAttemptCount}(O) = \sum_{\text{item} \in \text{item\_learning\_state}, \text{item.operation} = O} \text{item.total\_attempts}$$
   Because every accepted attempt increments `item_learning_state.total_attempts` exactly once, the sum of total attempts across all materialized items for an operation exactly equals the total accepted attempts for that operation.

3. **Runtime Tracking & Invariant Maintenance**:
   - `LearnerSnapshot` and `TrainingSession` maintain the four operation attempt counts in memory after initialization.
   - **Exact-Once Increment**: Successful SQLite transaction commit increments the corresponding operation count by exactly 1.
   - **Duplicate Commit Idempotency**: Replaying an already-committed `SubmissionId` returns success without incrementing attempt counts.
   - **Persistence Failure Safety**: A failed persistence write does not increment in-memory counts or mutate session state.
   - **Revision-Conflict Recovery**: Recovering from a store revision conflict cleanly reloads the authoritative counts from SQLite.
   - **Reset Learning Progress**: Clears all counts to exactly 0 for all four operations.
   - **Migration Continuity**: Migrated learner databases (V3 $\to$ V4 $\to$ V5 $\to$ V6) naturally reconstruct accurate operation attempt counts without data loss.

4. **Curriculum Open-Endedness**:
   Operation attempt counts are unrestricted non-negative integers (`Int32`). MathFirst does not impose an artificial ~400-fact or level-10 catalog limit; bands advance procedurally while safe in `Int32`.

---

### 3. Authoritative Settings & Current-Fact Reconciliation Contract

When the learner modifies practice configuration (e.g., enabling/disabling operations in Settings):

1. **Case A: Current unsubmitted fact becomes invalid because its operation is disabled**:
   - Discard the transient unsubmitted fact;
   - Immediately prepare a valid replacement fact from the updated enabled subset for the **same prospective global `PracticePosition`**;
   - Apply the zero-mutation contract.

2. **Case B: Current unsubmitted fact remains enabled and presentation-eligible**:
   - Retain the exact current fact;
   - Preserve `TrainingSession.FactInstanceRevision`;
   - Preserve unsubmitted partial answer input (`TrainingSession.CurrentAnswerInput`);
   - Preserve paused timer state and remaining deadline.

3. **Case C: Operation remains enabled but current fact is no longer presentation-eligible**:
   - If a fact is no longer eligible under current rules, apply the same zero-mutation replacement semantics as Case A.

---

### 4. Strict Zero-Mutation Replacement Contract

Discarding an invalid unsubmitted fact creates **zero durable or transient learning mutations**:
- No attempt record;
- No Skip outcome;
- No Timeout outcome;
- No Incorrect outcome;
- No `PracticePosition` increment;
- No `StoreRevision` increment;
- No `ItemLearningState` mutation;
- No FSRS card mutation;
- No in-session error counter or remediation mutation;
- No `OperationProgression` or `BandIndex` mutation;
- No operation accepted-attempt-count mutation;
- No streak or session check-in counter mutation.

Only transient in-memory presentation state changes to render the new valid question.

---

### 5. Accepted Feedback Deferral

If an answer has already been evaluated and accepted, and the session is in a post-accepted state:
- `CorrectFeedback`
- `IncorrectFeedback`
- `TimeoutFeedback`
- `TeachingIntervention`
- `SessionCheckIn`
- Any other post-accepted modal state

The accepted learner state is **never erased, rolled back, or replaced**. Configuration reconciliation is deferred until the learner deliberately acknowledges the feedback, and the updated configuration applies to the preparation of the next exercise (`AdvanceToNextFact`).

---

### 6. Settings Durability Domains & Awaited Reconciliation

1. **Separate Durability Domains**:
   Preferences (UI, enabled operations, practice time, haptics) and learner SQLite progress (`mathfirst_learner.db`) are strictly separate durability domains.

2. **Save-First & Awaited Reconciliation**:
   - Preference persistence executes first;
   - `TrainingSession.ReconcilePracticeConfigurationAsync` is explicitly **awaited** before completing Settings navigation;
   - Fire-and-forget reconciliation is eliminated.

3. **Reconciliation Failure & Recovery**:
   - If session reconciliation throws an exception, the persisted preference remains authoritative;
   - Learner SQLite state remains unmodified;
   - The UI displays a recoverable localized error notice;
   - Retrying reconciliation uses the saved preference;
   - A cold application restart naturally loads the saved preference and initializes a valid active fact.

---

### 7. Supersession and Architectural Invariants

#### Superseded Specifications:
1. **ADR-0003 & ADR-0004**: The global derivation formula for role-cycle slots ($\lfloor(\text{PracticePosition} - 1) / k\rfloor + 1$) is superseded by per-operation attempt count authority ($\text{NextOperationAttemptOrdinal}(O) = \text{AcceptedAttemptCount}(O) + 1$).
2. **MF-SET-001 Documentation**: The unconditional rule that *"Settings changes preserve the current question and apply only to the next generated question"* is superseded by the conditional reconciliation contract (Case A replacement vs. Case B retention).

#### Preserved Invariants:
1. **[ADR-0001](ADR-0001-cross-platform-application-topology-and-stack-baseline.md)**: Cross-platform topology, shared application core, and platform host abstractions.
2. **[ADR-0002](ADR-0002-offline-execution-and-local-persistence-boundary.md)**: Offline execution, local SQLite persistence, atomic transactions, and zero-network boundary.
3. **[ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md)**: Independent operation progression, hybrid curriculum, unique acquisition ownership, and `Int32` checked arithmetic safety.
4. **[ADR-0004](ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md)**: Hierarchical adaptive pace estimation, adaptive FSRS rating mapping, Schema V6 persistence, teaching interventions, and 20-attempt session check-ins.
5. **[ADR-0005](ADR-0005-acclimation-timing-and-rapid-dense-progression.md)**: Acclimation timing, durable proof, Numpad defaults, and Dense progression ($C \cdot 10 \ge N \cdot 9$).
6. **[ADR-0006](ADR-0006-android-packaging-signing-and-manifest-release-security.md)**: Android packaging profiles, multi-generation backup rules, and manifest security.
7. **[ADR-0007](ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md)**: Practice Fact Eligibility Invariant ($\text{owner}_O(F) \le B$) and session startup error boundaries.

---

## Consequences

### Positive
- Completely resolves the restart role-misassignment defect where toggling operations caused role corruption or crashes.
- Ensures a newly enabled or cold operation begins cleanly at Ordinal 1 (`New`) with appropriate introductory scaffolding.
- Disabling an operation immediately removes invalid unsubmitted facts without exposing disabled operations to the learner.
- Guarantees zero learning mutations (no score loss, phantom attempts, or FSRS distortion) when invalid unsubmitted facts are replaced.
- Preserves accepted answer feedback and learner achievements across Settings navigation.
- Eliminates race conditions via awaited configuration reconciliation.
- Avoids schema migration overhead by reconstructing authoritative counts from existing Schema V6 tables.

### Negative and Operational Costs
- Session initialization and snapshot creation require computing `SUM(total_attempts)` per operation across `item_learning_state`.
- Settings navigation must coordinate asynchronously with `TrainingSession` reconciliation.

---

## Alternatives Considered

1. **Schema V7 with a Dedicated `operation_attempt_counts` Table**:
   - *Rejected*: Redundant data storage creates synchronization risks; `SUM(total_attempts)` from `item_learning_state` is fast, authoritative, and requires no schema migration.
2. **Unconditionally Retaining the Current Question Regardless of Operation State**:
   - *Rejected*: Violates user intent by presenting questions from disabled operations.
3. **Recording a "Skipped" or "Cancelled" Attempt on Replaced Facts**:
   - *Rejected*: Distorts FSRS stability, increments Practice Position, and corrupts progression evidence for unattempted items.
4. **Fire-and-Forget Background Reconciliation**:
   - *Rejected*: Creates race conditions where the user returns to the practice page before new evidence is ready.
