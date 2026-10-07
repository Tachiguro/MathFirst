# ADR-0012: Cumulative Operation Unlock Progression and Monotonic Curriculum Stage

## Status

Accepted

## Date

2026-10-07

## Context

In MathFirst, elementary arithmetic practice originally allowed all four arithmetic operations (Addition, Subtraction, Multiplication, Division) to be enabled simultaneously from initial application setup, with operation availability managed primarily through user preferences ([ADR-0008](ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)). While [ADR-0009](ADR-0009-guided-four-operation-number-space-gate.md) and [ADR-0010](ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md) introduced cross-operation number-space gating (AdditionCeiling) when all four operations were active, this model exhibited fundamental pedagogical and architectural limitations:

1. **Premature Multi-Operation Cognitive Overload**:
   Beginner learners starting practice were immediately exposed to multiple arithmetic operations before establishing basic fluency in addition. While [P2](../../CHANGELOG.md) set fresh installations to default to Addition only, the underlying mechanism remained preference-driven rather than an evidence-based curriculum progression.

2. **Flawed Preference-Intersection Coupling**:
   An initial design proposal suggested deriving active practice operations via the intersection of user preferences and unlocked operations (`PreferenceEnabledOperations ∩ UnlockedOperations`). However, because fresh installations start with Addition-only preferences, an intersection rule prevented newly unlocked operations (such as Subtraction) from ever entering practice until the learner manually navigated to Settings and toggled the operation on. Operation preferences doubling as scheduling authority conflicted with automatic curriculum progression.

3. **Risk of Regressive Stage Locking**:
   If curriculum stage and operation availability were derived dynamically on every turn from transient fact states, subsequent errors or broad weakness in earlier operations could cause an already-unlocked operation (e.g., Subtraction) to suddenly disappear from practice. Such regression creates confusing learner UX and disrupts active retrieval schedules.

4. **Prerequisite Band Deadlock Vulnerability**:
   Strict dense-band advancement requires $\ge 90\%$ latest correctness across the full frontier ($C \cdot 10 \ge N \cdot 9$). If stage unlock required strict completion of `BandIndex >= 1`, a learner struggling on a single difficult fact in the introductory band (e.g. `1+1` or `2+0`) could become permanently blocked from unlocking Subtraction, even after thoroughly practicing all introductory facts.

5. **Semantic Ambiguity in Mode Detection**:
   Previously, Guided Mode vs. Custom Mode was inferred solely from whether the active operation set equaled exactly all four operations (`EnabledOperations.Count == 4`). In a cumulative model where Stage 3 introduces Multiplication alongside Addition and Subtraction (a 3-operation set), the system requires explicit semantic mode distinction (`PracticeMode.CurriculumManaged` vs. `PracticeMode.Custom`) to properly apply Guided number-space gating to Stage 3 without misclassifying it as unrestricted custom practice.

---

## Decision

### 1. Four-Stage Cumulative Progression Model

MathFirst establishes a canonical, monotonic four-stage cumulative operation progression for normal practice (`PracticeMode.CurriculumManaged`):

- **Stage 1**: Addition ($+$)
- **Stage 2**: Addition ($+$) and Subtraction ($-$)
- **Stage 3**: Addition ($+$), Subtraction ($-$), and Multiplication ($\times$)
- **Stage 4**: Addition ($+$), Subtraction ($-$), Multiplication ($\times$), and Division ($\div$)

Normal product practice operates under `PracticeMode.CurriculumManaged`, where available operations are derived directly from the persisted `CurriculumStage`.

### 2. Monotonic Curriculum Stage Invariant

1. **Monotonic Forward Progression**:
   `CurriculumStage` is persisted as a durable learning field (`1 <= CurriculumStage <= 4`). During normal learning, `CurriculumStage` is strictly monotonic:
   $$\text{CurriculumStage}_{t+1} \ge \text{CurriculumStage}_t$$
   An earned stage is never revoked or relocked during normal practice.
2. **Weakness Boundary**:
   Systemic broad weakness ($\ge 2$ unresolved `NeedsRemediation` facts across active operations) blocks advancement to the *next* stage, but never regresses the current earned stage.
3. **Reset Semantics**:
   `CurriculumStage` resets to Stage 1 only through explicit, destructive learning resets:
   - *Reset Learning Progress* (database wipe/re-init)
   - *Full Local Reset* (database, preferences, and telemetry purge)
   UI preference resets (*Reset to Defaults*) do NOT alter `CurriculumStage`.

### 3. Tolerant Prerequisite Unlock Predicates

Stage advancement requires demonstrated introductory mastery without vulnerability to single-fact deadlocks:

1. **Prerequisite D01 Frontier Readiness**:
   An introductory band (`ADD-D01`, `SUB-D01`, `MUL-D01`) is considered ready when:
   - Complete canonical frontier coverage is achieved: every fact in the band's owned frontier has been materialized and attempted ($\text{MaterializedCount} == \text{FrontierSize}$);
   - At most one fact in that prerequisite frontier is currently in `NeedsRemediation` state ($\text{RemediationCount} \le 1$);
   - Alternatively, standard band progression has advanced beyond the introductory band ($\text{BandIndex} \ge 1$).

2. **Stage Transition Rules**:
   - **Stage 1 $\to$ Stage 2**: `ADD-D01` is ready AND aggregate `HasBroadWeakness` is false across active operations.
   - **Stage 2 $\to$ Stage 3**: Current stage is $\ge 2$ AND `SUB-D01` is ready AND aggregate `HasBroadWeakness` is false across active operations.
   - **Stage 3 $\to$ Stage 4**: Current stage is $\ge 3$ AND `MUL-D01` is ready AND aggregate `HasBroadWeakness` is false across active operations.

3. **Orthogonality to Non-Curriculum Factors**:
   Stage unlocks depend strictly on mathematical attempt evidence. Unlocks do not depend on learner age, school grade, onboarding selections, arbitrary question count quotas, response latency, pace calibration readiness, Cyber Defense Critical Hits, or fluency alone.

### 4. Canonical Broad Weakness Aggregation (MF-LEARN-006 Compatibility)

P3 reuses the canonical broad weakness semantics defined in [ADR-0010](ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md):
- Broad weakness is defined as $\ge 2$ eligible `NeedsRemediation` facts aggregated across all currently active operations.
- Fact eligibility strictly respects canonical curriculum ownership ($\text{owner}_O(F) \le B_O$) and the effective `GuidedNumberSpaceGate`.
- For example, in Stage 2, having 1 unresolved weak Addition fact and 1 unresolved weak Subtraction fact produces aggregate broad weakness ($1 + 1 = 2 \ge 2$), which safely blocks unlocking Stage 3 while keeping Stage 2 active.

### 5. Schema V9 Persistence and Atomic Commit

1. **Schema V9 Evolution**:
   SQLite persistence schema evolves from V8 to V9 by adding the `curriculum_stage` column to `learner_progression`:
   ```sql
   ALTER TABLE learner_progression ADD COLUMN curriculum_stage INTEGER NOT NULL DEFAULT 1 CHECK (curriculum_stage BETWEEN 1 AND 4);
   ```
2. **Fresh Installation**:
   Fresh learner state initializes with `CurriculumStage = 1`.
3. **Atomic Evaluation & Commit**:
   `CurriculumStage` is evaluated upon accepted submission and committed atomically in the same SQLite transaction alongside `AttemptRecord`, `ItemLearningState`, `OperationProgression`, FSRS card state, `PracticePosition`, and `StoreRevision`.
4. **Publishing Integrity**:
   In-memory unlock events are published to the UI only after successful, un-rolled-back SQLite transaction commit.

### 6. Conservative V8 $\to$ V9 Schema Migration

Migration from Schema V8 to Schema V9 determines initial `CurriculumStage` conservatively from historical mathematical evidence, completely independent of legacy preference toggles:
- **Prerequisite Evaluation**:
  - Addition ready: `BandIndex >= 1` OR (complete `ADD-D01` frontier materialized AND $\le 1$ remediation fact).
  - Subtraction ready: `BandIndex >= 1` OR (complete `SUB-D01` frontier materialized AND $\le 1$ remediation fact).
  - Multiplication ready: `BandIndex >= 1` OR (complete `MUL-D01` frontier materialized AND $\le 1$ remediation fact).
- **Migration Stage Assignment**:
  - If Addition, Subtraction, and Multiplication are all ready $\implies$ **Stage 4**
  - Else if Addition and Subtraction are both ready $\implies$ **Stage 3**
  - Else if Addition is ready $\implies$ **Stage 2**
  - Else $\implies$ **Stage 1**
- **Dormant Evidence Preservation**:
  Division attempt history, historical Custom mode attempts, or advanced band data for locked operations do not grant migration stage authority. However, all historical `ItemLearningState`, `OperationProgression`, FSRS cards, attempt records, and accepted attempt counts are preserved losslessly. When a dormant operation is subsequently unlocked, its historical learning state resumes seamlessly.

### 7. Explicit PracticeMode & Guided Number-Space Gating

1. **`PracticeMode.CurriculumManaged` vs. `PracticeMode.Custom`**:
   - `PracticeMode.CurriculumManaged`: Default normal practice. Available operations are strictly governed by `CurriculumStage`. Semantically **Guided** for number-space gating.
   - `PracticeMode.Custom`: Explicit custom practice (used for targeted testing, diagnostic subsets, or legacy custom mode). Available operations are governed by `PracticeOperationPreferences`.
2. **Guided Stage 3 & Stage 4 Gating**:
   - In `CurriculumManaged` Stage 3 (Addition + Subtraction + Multiplication), Multiplication is semantically Guided and bounded by `AdditionCeiling` until soft decoupling at `MUL BandIndex >= 3` per [ADR-0010](ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md).
   - In `CurriculumManaged` Stage 4, Multiplication and Division follow standard Guided Gate G3 rules.
   - In `Custom` mode, custom subsets (e.g. Multiplication only, or Addition + Subtraction + Multiplication) remain Unrestricted per [ADR-0009](ADR-0009-guided-four-operation-number-space-gate.md), while custom all-four retains Guided behavior.

### 8. Read-Only Settings Curriculum Status

In normal product practice:
- Settings operation controls display read-only status derived from `CurriculumStage` (e.g. "Unlocked" vs. "Locked").
- UI controls do not mutate operation preference toggles, cannot bypass locked operations, and cannot disable unlocked curriculum operations.
- Legacy preference store APIs remain intact for Custom mode and future P4 Settings refinement.

### 9. Preserved Scheduler and 482 Normative Benchmark Invariants

1. **MF-STAB-003 Scheduler Invariants**:
   - Global `PracticePosition` remains authoritative for total sequence ordering.
   - Per-operation role ordinal is derived strictly from $\text{NextOperationAttemptOrdinal}(O) = \text{AcceptedAttemptCount}(O) + 1$.
   - A stage unlock does not advance `PracticePosition`. Newly available operations take effect prospectively for future practice turns.
   - Historical accepted attempt counts for previously practiced operations are preserved and continue to determine role ordinals upon unlock.
2. **MF-LEARN-006 Normative Benchmark**:
   - The 482-attempt strong learner benchmark ([ADR-0010](ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md)) remains the exact normative benchmark for Option-B adaptive discovery.
   - The benchmark executes in `PracticeMode.Custom` with all four operations enabled from start to isolate MF-LEARN-006 discovery mechanics from cumulative P3 onboarding progression.

---

## Consequences

### Positive
- **Pedagogical Soundness**: Learners build solid additive foundations before encountering subtraction, multiplication, and division.
- **Frictionless Progress**: Unlocks occur automatically upon proven competence without requiring manual Settings navigation.
- **Monotonic Stability**: Learners never experience confusing relocking of earned operations due to temporary mistakes.
- **Deadlock Resistance**: Single-fact difficulties in introductory bands do not trap learners in perpetual Stage 1.
- **Clean Migration**: Existing user data migrates safely without data loss or unearned stage grants.

### Neutral
- `learner_progression` table schema is updated to V9.
- Settings operation toggles become informational status indicators in normal practice.

### Negative / Trade-offs
- Learners wishing to practice only Multiplication or Division immediately on a fresh profile must use explicit Custom mode rather than normal default practice.

---

## Relationship to Prior ADRs

- **[ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md)**: **Preserved**. Within each operation, independent band progression, frontier coverage, and open-ended fact space remain fully active.
- **[ADR-0008](ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)**: **Clarified & Preserved**. Per-operation role ordinals ($\text{AcceptedAttemptCount}(O) + 1$) and zero-mutation unsubmitted fact replacement remain authoritative. Operation preference toggles control operation availability for `PracticeMode.Custom`, whereas `CurriculumStage` governs operation availability for `PracticeMode.CurriculumManaged`.
- **[ADR-0009](ADR-0009-guided-four-operation-number-space-gate.md)**: **Clarified & Extended**. Gating semantics (`AdditionCeiling`, `GuidedNumberSpaceGate`) are preserved. Semantic mode classification is extended: `PracticeMode.CurriculumManaged` at Stage 3 and Stage 4 is semantically Guided, while raw operation-set size alone is no longer the sole determinant of Guided vs. Custom mode.
- **[ADR-0010](ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md)**: **Preserved**. G3 soft decoupling thresholds (`BandIndex >= 3`), pace calibration readiness ($\ge 24$ attempts), Option-B discovery, and aggregate broad weakness ($\ge 2$ remediation facts) remain identical.
