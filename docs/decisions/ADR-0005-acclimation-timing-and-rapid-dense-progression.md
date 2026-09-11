# ADR-0005: Acclimation Timing and Rapid Dense Progression

## Status

Accepted

## Date

2026-09-11

## Context

Following the implementation of adaptive pace estimation and Schema V6 in [ADR-0004](ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md), real-user practice revealed several significant usability and pedagogical friction points:

1. **Premature Deadline Compression on Unfamiliar Facts**: Early exposure to trivial `0` and `1` facts quickly compressed learner and operation pace estimates ($P_{\text{learner}}$, $P_{\text{operation}}$), causing subsequent newly introduced facts (such as multi-digit multiplication or subtraction) to receive overly aggressive 3–4 second deadlines before the learner had time to mentally calculate or locate the keys.
2. **Motor and Physical Input Search Latency**: Keypad layout search on touch screens and physical keyboards imposed non-trivial motor friction, particularly for multi-digit answers, causing unfair timeout penalties on mathematically straightforward facts.
3. **Repetitive Zero/One Loops in Dense Foundations**: In small dense bands, the standard 40-attempt rolling window forced learners through dozens of redundant encounters of elementary facts (e.g. `0 + 0 = 0`, `0 × 0 = 0`), creating unnecessary fatigue.
4. **Brittleness of Fast Acquisition**: The Fast Acquisition policy introduced in ADR-0004 required an unbroken string of first-encounter correct responses under 2000 ms. A single early motor hesitation, typographical mistake, or timeout permanently disqualified Fast Acquisition for that band instance, forcing the learner back into a full 40-attempt rolling window without recourse.
5. **Inadequate Rolling Window Evidence for Dense Progression**: The rolling 40-attempt recent window was insufficient to prove authoritative, restart-stable mastery across the dense frontier when errors occurred and were subsequently corrected.
6. **Keypad Layout Defaults**: The previous default `Phone` keypad layout (`1 2 3` at top) diverged from standard numeric keypad expectations for desktop/calculator users.

This decision defines the architecture for `MF-LEARN-003`: answer-length acclimation deadline floors and entry allowances, durable fact proof, default Numpad layout, Coverage-First Dense selection, authoritative latest-per-frontier persistence evidence, Schema V6 partial indexing, correctness-driven Dense band progression ($C \cdot 10 \ge N \cdot 9$), candidate overlay before persistence, recoverable error/timeout votes, and retirement of Fast Acquisition and the `MUL-D01` special bootstrap exception.

### Relationship to Prior ADRs

ADR-0005 supersedes and refines specific operational clauses of [ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md) and [ADR-0004](ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md) while preserving foundational architecture:
- **Preserved Invariants**: Independent operation progression, Practice Position virtual time, unique acquisition ownership, hybrid dense/structured curriculum, lazy fact materialization, Schema V6 persistence format, FSRS-6 spaced repetition, and `Int32` checked arithmetic safety remain fully authoritative.
- **Superseded Clauses**:
  - **ADR-0004 Section 5 (Fast Acquisition)** is retired and removed from the active architecture.
  - **ADR-0003 / ADR-0004 MUL-D01 Bootstrap Profile** is retired; `MUL-D01` now follows the universal Dense progression rule.
  - **ADR-0003 Standard Rolling Window for Dense Bands** is isolated strictly to Structured bands; standard rolling advancement no longer advances Dense bands.
- **Refined Clauses**:
  - **ADR-0004 Section 2 (Adaptive Deadlines)** is refined with answer-length novelty floors and entry allowances for unproven facts.
  - **ADR-0004 Section 7 (Practice Selector)** is refined with Coverage-First Dense acquisition precedence.

---

## Decision

### 1. Answer-Length Acclimation Deadlines and Durable Fact Proof

To provide learners adequate opportunity to mentally calculate and enter answers for newly introduced facts without distorting fluency measurement:

1. **Durable Fact Proof**: A fact is considered **proven** when durable `ItemLearningState.CorrectAttempts > 0`.
   - Only a successfully persisted `Correct` outcome produces durable proof.
   - `Incorrect` and `Timeout` outcomes do not prove a fact.
   - Conceptual/unmaterialized facts and facts with 0 correct attempts are **unproven**.
2. **Canonical Digit Count**: The digit count ($D$) is derived from the canonical non-negative integer correct result using integer arithmetic ($D = \text{result} == 0 ? 1 : \lfloor \log_{10}(\text{result}) \rfloor + 1$). Zero is exactly 1 digit.
3. **Digit-Aware Novelty Floor**: Unproven facts receive a generous minimum response deadline based on correct answer length:
   - **1 answer digit**: minimum $15000\text{ ms}$
   - **2 answer digits**: minimum $20000\text{ ms}$
   - **3 answer digits**: minimum $25000\text{ ms}$
   - **4 or more answer digits**: minimum $30000\text{ ms}$
4. **Multi-Digit Entry Allowance**: To account for physical multi-key entry:
   $$\text{EntryAllowanceMs} = 1000\text{ ms} \cdot \max(0, D - 1)$$
5. **Adaptive Deadline Formula**:
   $$\text{AdaptiveDeadlineMs} = \text{clamp}\left(\left\lceil \frac{2 \cdot P_{\text{fact}} + \text{InstabilityAllowanceMs} + \text{EntryAllowanceMs}}{100} \right\rceil \cdot 100, 3000, 30000\right)$$
6. **Effective Deadline Resolution**:
   - **Proven fact**: $\text{FinalDeadlineMs} = \text{AdaptiveDeadlineMs}$
   - **Unproven fact**: $\text{FinalDeadlineMs} = \min(30000, \max(\text{AdaptiveDeadlineMs}, \text{NoveltyFloorMs}))$
7. **Strict Non-Interference**: Novelty deadline extension expands response opportunity only. It does **not** alter:
   - Measured `ResponseLatencyMs`;
   - Adaptive fluency thresholds (`EasyThresholdMs`, `FluencyThresholdMs`);
   - Fluency classification (`IsFluent`);
   - FSRS rating mapping (`Again`, `Hard`, `Good`, `Easy`).
8. **Incomplete Multi-Digit Editability & Auto-Submission Authority**:
   - Answer-length acclimation encompasses learner-controlled editing of incomplete multi-digit inputs.
   - When the entered buffer length is strictly less than the expected canonical digit count ($D$), the input remains pending and editable via Backspace/Delete without premature evaluation, attempt recording, or learning-state mutation.
   - Automatic submission occurs when the expected digit count is reached ($|validInput| \ge D$) or upon exact numeric equality. Prefix mismatch alone does not trigger early submission.
   - Single-digit expected answers ($D = 1$) preserve immediate auto-submission upon first digit entry.

---

### 2. Keypad Layout Defaults and Ordering

Numeric keypad layout and visual selection are standardized to Numpad:

1. **Enum Compatibility**: Storage enum values remain `Phone = 0`, `Numpad = 1`. No enum renumbering is performed.
2. **Default & Fallback**: Missing, uninitialized, or invalid preference values normalize to `Numpad`.
3. **Visual Choice Order**: In Onboarding and Settings, options are displayed with **Numpad first (left)** and **Phone second (right)**.
4. **Preference Preservation**: Existing explicitly stored user preferences (`Phone` or `Numpad`) are preserved verbatim.
5. **UI & Reset Baseline**: Home practice initial backing state and Full Local Reset default to `Numpad`. Keypad geometry itself remains unchanged.

---

### 3. Coverage-First Dense Acquisition and Materialization Invariant

To ensure learners encounter all facts in an active dense band before excessive repetition of already-seen items:

1. **Selector Precedence**:
   1. **Eligible Remediation**: `NeedsRemediation == true` AND `LastReviewPracticePosition != null` AND $\text{ProspectivePracticePosition} \ge \text{LastReviewPracticePosition} + 4$;
   2. **Dense Coverage-First New**: Active when scheduled operation's current band is `Dense` and its owned frontier contains unmaterialized facts;
   3. **Ordinary Requested-Role Chain**: Resolves via the nominal role (`New`, `Due`, `Maintenance`, `Frontier`) fallback chains.
2. **Coverage-First Role Mapping**: Coverage-First resolves through `PracticeSelectionRole.New` while preserving the nominal requested role for scheduling tracking. Coverage-First may therefore introduce an unseen Dense fact during nominal `Due`, `Maintenance`, or `Frontier` turns.
3. **Structured Band Isolation**: Structured bands do not receive Coverage-First selection and continue using standard representative sampling.
4. **Authoritative Materialization Invariant**: Unmaterialized facts may enter practice **only** through explicit acquisition paths:
   - (A) Normal `Requested New` when unmaterialized candidates exist;
   - (B) `Coverage-First Dense New` for the scheduled operation's current owned frontier.
   - Non-New resolved roles (`Due`, `Maintenance`, `Frontier`, `Early Review`, `Remediation`) strictly cannot materialize unmaterialized facts.

---

### 4. Authoritative Dense Evidence and Schema V6 Partial Index

To support deterministic, restart-stable Dense progression with error recovery:

1. **Persistence Contract**: `ILearnerStore.LoadLatestFrontierAttemptsAsync` retrieves at most one latest positioned `AttemptRecord` per requested current-frontier `FactId` satisfying:
   - `Operation == requestedOperation`;
   - `PracticePosition IS NOT NULL`;
   - `PracticePosition > BandStartedPracticePosition`;
   - `FactId IN (requestedFrontierFactIds)`.
2. **Bounded Frontier Size**: The requested frontier is bounded by the current Dense band (maximum frontier size: 25 facts).
3. **Schema V6 Partial Index**: A physical index is maintained on SQLite storage:
   ```sql
   CREATE INDEX IF NOT EXISTS ix_attempt_history_operation_fact_position
   ON attempt_history(operation, fact_id, practice_position DESC)
   WHERE practice_position IS NOT NULL;
   ```
   - Created/repaired idempotently across fresh V6 databases, existing V6 databases, and V5 $\to$ V6 migrations.
   - Schema version remains **Schema V6** (no Schema V7). Index maintenance is physical storage optimization and does not increment store revision or add migration markers.

---

### 5. Correctness-Driven Dense Band Progression

Dense bands advance based on demonstrated correctness across the complete owned frontier:

1. **Evaluation Procedure**:
   1. Determine the exact current owned frontier ($N = |\text{frontier}|$).
   2. Load authoritative persisted latest-per-frontier attempts for $\text{PracticePosition} > \text{BandStartedPracticePosition}$.
   3. Overlay the in-memory current candidate attempt as the authoritative latest vote for its `FactId`.
   4. Require complete frontier coverage (every owned frontier fact must have at least one positioned attempt in the current band instance).
   5. Count facts whose latest outcome is `Correct` ($C$).
   6. Evaluate the progression inequality using exact integer arithmetic:
      $$C \cdot 10 \ge N \cdot 9$$
   7. Verify that a complete, `Int32`-safe successor band exists.
   8. Advance exactly one band; set new $\text{BandStartedPracticePosition} = \text{TriggeringPracticePosition}$.
2. **Small-Band Threshold Consequences**:
   - $N=2 \implies 2$ Correct required (100%)
   - $N=3 \implies 3$ Correct required (100%)
   - $N=4 \implies 4$ Correct required (100%)
   - $N=5 \implies 5$ Correct required (100%)
   - $N=9 \implies 9$ Correct required (100%)
   - $N=10 \implies 9$ Correct required (90%)
   - $N=11 \implies 10$ Correct required (90.9%)
   - $N=12 \implies 11$ Correct required (91.7%)
   - $N=20 \implies 18$ Correct required (90%)
   - $N=25 \implies 23$ Correct required (92%)
3. **Recoverable Outcome Semantics (Latest-Vote Authority)**:
   - `Incorrect` / `Timeout` followed later by `Correct` $\implies$ latest vote is `Correct`.
   - `Correct` followed later by `Incorrect` / `Timeout` $\implies$ latest vote is `Incorrect` / `Timeout`.
   - Mistakes do not permanently poison a band instance.
4. **Decoupling from Fluency & Latency**:
   - Dense progression does **not** depend on response latency $\le 2000\text{ ms}$, `IsFluent == true`, `Easy`, or `Good` ratings.
   - A slow `Correct` counts as `Correct` for progression.
   - Response latency remains evidence for adaptive pace, FSRS ratings, and future spaced reviews.
5. **Weak-Fact Continuity**: When larger bands advance at $\ge 90\%$ with 1–2 lingering weak facts, those facts remain materialized in `ItemLearningState`, FSRS card state, and remediation queues. Advancement does not clear or reset weakness.
6. **Structured Band Progression Retained**: Structured bands continue to use the standard 40-attempt rolling window gate ($\ge 38$ Correct, $\ge 34$ Fluent, $\ge 20$ frontier attempts, $\ge \min(16, N)$ distinct frontier facts, and 16 introductions).
7. **Retirement of Fast Acquisition and MUL-D01 Exception**:
   - `FastAcquisitionEvaluator` and all associated clean-prefix / 2000 ms rules are removed.
   - The special 12-attempt `MUL-D01` rule is removed; `MUL-D01` advances under the universal Dense rule ($N=4 \implies 4$ Correct).

---

### 6. Persistence Atomicity and Restart Stability

1. **Candidate Overlay Pre-Evaluation**: Progression is evaluated in memory combining persisted frontier evidence and the candidate submission before writing to disk.
2. **Atomic Persistence**: Progression changes, attempt records, item states, and FSRS updates commit atomically. If persistence fails, no progression or attempt state is published or mutated.
3. **Restart Equivalence**: Because eligibility derives entirely from durable `OperationProgression`, `BandStartedPracticePosition`, and `attempt_history`, evaluation is completely stable across application restarts without ephemeral session markers.

---

## Consequences

### Benefits

- **Rapid Early Expansion**: Learners move through elementary small-number dense bands in 2 to 10 attempts instead of being locked into 40 attempts.
- **Acclimation Without Penalties**: Multi-digit and newly introduced facts provide generous response time (15–30s) during initial learning without corrupting long-term fluency and FSRS ratings.
- **Recoverable Progression**: Learner typos and early mistakes can be corrected in subsequent practice without permanently disqualifying fast band completion.
- **Keypad Ergonomics**: Default Numpad alignment matches standard numeric keypad hardware and calculator mental models.
- **Spaced Repetition Continuity**: Weak facts in advanced bands remain protected and scheduled for review even after curriculum expansion.
- **Predictable Performance**: Bounded latest-per-frontier queries ($N \le 25$) supported by a partial index execute with sub-millisecond efficiency.

### Costs and Risks

- **Distinct Evaluators for Dense vs Structured**: The learning core maintains separate progression evaluation models for Dense (latest-per-frontier $\ge 90\%$) and Structured (rolling 40-attempt window).
- **Near-100% Barrier for Small Bands**: Small frontiers ($N \le 9$) mathematically require 100% latest correctness due to integer rounding ($C \cdot 10 \ge N \cdot 9$).
- **Residual Weak Facts**: In large dense bands ($N \ge 10$), advancing with 1–2 non-correct facts relies on FSRS and remediation to ensure eventual recall.

---

## Alternatives Considered

1. **Retain Fast Acquisition with Expanded Prefix Tolerances**:
   - *Rejected*: Adding exception rules and retry allowances to the first-encounter prefix added complexity while remaining fragile. Latest-per-frontier correctness provides a robust, policy-clean model.
2. **Floating-Point Percentage Thresholds (e.g. $C / N \ge 0.90$)**:
   - *Rejected*: Floating-point arithmetic introduces potential platform-specific IEEE 754 precision divergence. Integer cross-multiplication ($C \cdot 10 \ge N \cdot 9$) is exact, deterministic, and platform-independent.
3. **Extend Rolling 40-Attempt Window to All Bands**:
   - *Rejected*: Created intolerable repetition for introductory 0/1 bands and failed to solve the early pace compression problem.
4. **Upgrade to Schema V7 for Index Changes**:
   - *Rejected*: The partial index requires no structural table or column modifications; creating it idempotently in Schema V6 avoids unnecessary migration version churn.
