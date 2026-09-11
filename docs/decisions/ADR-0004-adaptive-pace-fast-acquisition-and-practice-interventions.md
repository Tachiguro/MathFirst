# ADR-0004: Adaptive Pace, Fast Acquisition, and Practice Interventions

## Status

Accepted

## Date

2026-09-11

## Context

Following the implementation of independent operation progression in [ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md) and practice stabilization in `MF-STAB-001`, MathFirst established an offline-first learning foundation with independent per-operation progression, deterministic scheduling, and FSRS-6 spaced repetition.

However, several pedagogical and operational limitations remained:
1. **Rigid Answer Deadlines**: Every presented fact used a fixed 30,000 ms answer deadline, providing no gentle time pressure to promote mental arithmetic automation or adjust for easy versus complex facts.
2. **Static Latency Thresholds**: FSRS ratings and fluency classifications used hardcoded cutoffs (<=1000 ms Easy, <=2500 ms Good, >2500 ms Hard) regardless of learner proficiency, arithmetic operation, place-value magnitude, or exact fact history.
3. **Artificial Repetition in Small Dense Bands**: Small introductory bands (such as `0 + 0 = 0` or factor-0/1 multiplication) required full 40-attempt windows even when a learner demonstrated immediate, effortless mastery on every fact.
4. **Selector Non-Due Repetition and Coarse Fallback**: Empty role pools fell back through `AnyMaterialized`, which could surface arbitrary non-due mastered items as repetitive filler rather than preserving spaced-repetition integrity.
5. **Absence of In-Session Teaching**: When learners repeatedly failed the same fact within a session, no instructional remediation occurred beyond normal retry scheduling.
6. **Practice Distraction & Timing Gaps**: Permanent score HUD displays created cognitive clutter during practice, and pausing after session milestones required strict timing isolation.

This decision defines the architecture for `MF-LEARN-002`: hierarchical adaptive pace estimation, dynamic per-fact answer deadlines, adaptive FSRS ratings and fluency classification, Schema V6 persistence, deterministic fast acquisition for dense bands, refined semantic selector pools with terminal liveness, session-local repeated-error teaching interventions, periodic session check-ins, and distraction-free practice UX.

### Relationship to ADR-0003

ADR-0004 refines and supersedes specific operational policies of ADR-0003 while preserving its fundamental architectural invariants:
- **Preserved ADR-0003 Invariants**: Independent operation progression, PracticePosition virtual time, unique acquisition ownership, dense foundation and structured place-value families, deterministic generation, lazy fact materialization, and `Int32` checked arithmetic safety remain authoritative.
- **Superseded / Refined ADR-0003 Details**:
  - Replaces fixed 30s deadline and static 1000/2500 ms latency thresholds with hierarchical adaptive pace calculation and per-fact adaptive thresholds;
  - Adds the Fast Acquisition advancement path alongside the standard 40-attempt rolling window and `MUL-D01` 12-attempt bootstrap;
  - Replaces the generic `AnyMaterialized` fallback chain with role-specific semantic candidate chains (`Useful Frontier`, `Due`, `Stale Maintenance`, `Early Review`);
  - Upgrades durable persistence from Schema V5 to Schema V6 with persisted per-attempt `IsFluent` status.

---

## Decision

### 1. Hierarchical Adaptive Pace Runtime

Adaptive expected pace ($P_{\text{fact}}$) is calculated dynamically from positioned, accepted, mathematically correct attempt evidence ($PracticePosition > 0$). Latency samples are clamped to the valid range $[600\text{ ms}, 12000\text{ ms}]$.

A hierarchical empirical shrinkage estimator adjusts across four levels of granularity:

1. **Static Prior Baseline ($P_0$)**: $4500\text{ ms}$.
2. **Learner Pace ($P_{\text{learner}}$)**: Shrinks $P_0$ against the latest 30 positioned Correct attempts across all operations with weight $W = 12$:
   $$\text{Shrink}(P_0, 12, \text{latest 30 learner Correct latencies})$$
3. **Operation Pace ($P_{\text{operation}}$)**: Shrinks $P_{\text{learner}}$ against the latest 20 positioned Correct attempts for the scheduled operation with weight $W = 8$:
   $$\text{Shrink}(P_{\text{learner}}, 8, \text{latest 20 operation Correct latencies})$$
4. **Band Pace ($P_{\text{band}}$)**: Shrinks $P_{\text{operation}}$ against the latest 15 positioned Correct attempts for facts belonging to the operation's current owned frontier with weight $W = 6$:
   $$\text{Shrink}(P_{\text{operation}}, 6, \text{latest 15 band-frontier Correct latencies})$$
5. **Exact Fact Pace ($P_{\text{fact}}$)**: Shrinks $P_{\text{band}}$ against the latest 5 positioned Correct attempts for the exact `FactId` with weight $W = 4$:
   $$\text{Shrink}(P_{\text{band}}, 4, \text{latest 5 exact-fact Correct latencies})$$

#### Shrinkage & Median Formulation
$$\text{Shrink}(P_{\text{parent}}, W, \text{samples}) = \begin{cases} P_{\text{parent}} & \text{if samples is empty} \\ \left\lfloor \frac{W \cdot P_{\text{parent}} + |\text{samples}| \cdot \text{Median}(\text{samples}) + \frac{W + |\text{samples}|}{2}}{W + |\text{samples}|} \right\rfloor & \text{otherwise} \end{cases}$$

All division uses deterministic integer arithmetic with round-half-up rounding. Legacy unpositioned attempts ($PracticePosition = \text{NULL}$) and `ItemLearningState.RollingLatencyMs` do not contribute to adaptive pace estimation.

---

### 2. Adaptive Answer Deadline and Instability Allowance

Every presentation receives an adaptive answer deadline calculated and fixed before timing begins:

1. **Instability Allowance**: Evaluates the latest 5 positioned attempts for the exact `FactId`:
   - Each `Incorrect` attempt adds $+1000\text{ ms}$;
   - Each `Timeout` attempt adds $+1500\text{ ms}$;
   - Total allowance is capped at $3000\text{ ms}$:
     $$\text{Allowance} = \min(3000, 1000 \cdot N_{\text{incorrect}} + 1500 \cdot N_{\text{timeout}})$$
2. **Deadline Calculation**:
   $$\text{RawDeadline} = 2 \cdot P_{\text{fact}} + \text{Allowance}$$
   $$\text{RoundedDeadline} = \left\lceil \frac{\text{RawDeadline}}{100} \right\rceil \cdot 100$$
   $$\text{DeadlineMs} = \text{clamp}(\text{RoundedDeadline}, 3000, 30000)$$
3. **Cold Baseline**: For a fresh learner on a new fact ($P_{\text{fact}} = 4500\text{ ms}$, $\text{Allowance} = 0$), the initial deadline is exactly $9000\text{ ms}$.
4. **Semantic Timeout Rule**: If elapsed monotonic active time reaches or exceeds `DeadlineMs`, the attempt is evaluated as `AttemptOutcome.Timeout`. A correct numeric entry submitted at or after the deadline remains a Timeout.

---

### 3. Adaptive Rating and Fluency Classification

FSRS ratings and fluency classifications are determined relative to the adaptive expected pace $P_{\text{fact}}$:

$$\text{EasyThresholdMs} = \text{clamp}\left(\left\lfloor \frac{85 \cdot P_{\text{fact}} + 50}{100} \right\rfloor, 600, 2000\right)$$
$$\text{FluencyThresholdMs} = \text{clamp}\left(\left\lfloor \frac{125 \cdot P_{\text{fact}} + 50}{100} \right\rfloor, 1500, 4000\right)$$

#### Classification Mapping:
- **`Incorrect`**: Rating `Again`, `IsFluent = false`
- **`Timeout`**: Rating `Again`, `IsFluent = false`
- **`Correct` $\le \text{EasyThresholdMs}$**: Rating `Easy`, `IsFluent = true`
- **`Correct` $> \text{EasyThresholdMs}$ and $\le \text{FluencyThresholdMs}$**: Rating `Good`, `IsFluent = true`
- **`Correct` $> \text{FluencyThresholdMs}$**: Rating `Hard`, `IsFluent = false`

The resulting `IsFluent` flag is stored durably on `AttemptRecord` and `attempt_history.is_fluent`. Historical rolling-window progression checks consume this durable boolean rather than recomputing fluency against later pace estimates.

---

### 4. Schema V6 Persistence

The durable SQLite schema is upgraded to Schema V6:
- **`attempt_history.is_fluent`**: Integer column (`0` or `1`) with table-level constraint:
  `CHECK (is_fluent IN (0, 1)) CHECK (is_fluent = 0 OR (is_correct = 1 AND outcome = 'Correct'))`
- **Transactional Migration from V5 to V6**:
  - Recreates `attempt_history` with the `is_fluent` column;
  - Backfills historical V5 attempts using the historical fixed rule: Correct with `response_latency_ms <= 2500` $\implies \text{is\_fluent} = 1$; otherwise $0$;
  - Preserves `practice_position` (including NULL for legacy V4 attempts), timestamps, outcomes, and latencies;
  - Preserves learner progression, store revision, item learning states, FSRS card states, and operation progression rows without reset;
  - Atomic verification ensures source and destination row counts match before updating `schema_version` to `6`.
- **Non-Persisted Ephemeral State**: Per-attempt thresholds (`FluencyThresholdMs`, `EasyThresholdMs`), fast-acquisition activation flags, session teaching counters, and check-in cadence counts are deliberately not stored in the schema.

---

### 5. Deterministic Fast Acquisition for Dense Bands

To eliminate unnecessary repetition in small dense bands, an operation may advance immediately when a learner demonstrates confident recall on every owned frontier fact:

1. **Eligibility**:
   - Current band is `CurriculumBandKind.Dense`;
   - Owned frontier size $N$ is between 1 and 12 facts inclusive ($1 \le N \le 12$);
   - Evaluated only on positioned accepted attempts within the current band instance ($PracticePosition > \text{BandStartedPracticePosition}$);
   - Bounded prefix completeness: all scheduled operation positions from band start through the latest attempt must be present without gaps;
   - Complete successor band exists and is safe in `Int32`.
2. **Advancement Criteria**:
   - **Error-Free Prefix**: Every qualifying attempt in the prefix must be `Correct`; any `Incorrect` or `Timeout` permanently disqualifies Fast Acquisition for that band instance;
   - **First-Encounter Absolute Fluency**: The first positioned attempt for every owned frontier fact must be `Correct` with raw $\text{ResponseLatencyMs} \le 2000\text{ ms}$;
   - **Horizon Bound**: All $N$ owned frontier facts must be completed within the phase-aware $N$-th requested-New role horizon for that operation:
     $$\text{endPosition} \le \text{CalculateRequestedNewHorizon}(\text{operation}, \text{startPosition}, N)$$
3. **Properties**:
   - Uses raw absolute latency ($\le 2000\text{ ms}$), not adaptive `IsFluent`;
   - Subsequent attempts cannot repair a failed first encounter;
   - No retroactive startup advancement occurs;
   - If Fast Acquisition is not met, the operation continues seamlessly under standard rolling-window advancement.

---

### 6. Ordinary Band Advancement

Standard advancement profiles remain fully functional and are not weakened:
- **Standard Profile** (Addition, Subtraction, Division, Multiplication $\text{BandIndex} \ge 1$):
  - Requires 40 accepted attempts after `BandStartedPracticePosition`;
  - Latest 40 attempts must contain $\ge 38$ Correct, $\ge 34$ fluent (`IsFluent == true`), $\ge 20$ owned-frontier attempts, and $\ge \min(16, |\text{frontier}|)$ distinct owned-frontier facts;
  - Complete coverage for band type (Dense: lifetime exposure on all owned facts; Structured: 16 distinct introductions during current band).
- **Multiplication Bootstrap Profile** (`MUL-D01`):
  - Requires 12 accepted attempts after `BandStartedPracticePosition`;
  - $\ge 11$ Correct, $\ge 11$ fluent, $\ge 8$ owned-frontier attempts, 4 distinct owned facts (`0*0`, `0*1`, `1*0`, `1*1`), and complete dense lifetime coverage.

---

### 7. Refined Semantic Selector Pools and Role-Specific Fallback

The practice selector eliminates `AnyMaterialized` and replaces generic fallbacks with role-specific deterministic chains across five distinct semantic pools:

#### Candidate Semantic Pools:
1. **`New`**: Unmaterialized facts owned by the scheduled operation's current band (structured bands use deterministic 16-fact sample).
2. **`Useful Frontier`**: Materialized facts owned by current band, prioritized by:
   1. Unmastered first (`IsProvisionallyMastered == false`);
   2. Fewer `TotalAttempts` ascending;
   3. Older `LastReviewPracticePosition` ascending (nulls first);
   4. `FactId` ordinal ascending.
3. **`Due`**: Materialized facts with valid FSRS cards where $\text{DuePracticePosition} \le \text{ProspectivePosition}$, ordered by `DuePracticePosition`, `LastReviewPracticePosition`, `FactId`.
4. **`Stale Maintenance`**: Materialized facts not in remediation with future FSRS due position where $\text{ProspectivePosition} \ge \text{LastReviewPracticePosition} + 40$, ordered by `LastReviewPracticePosition`, `DuePracticePosition`, `FactId`.
5. **`Early Review`**: Materialized facts with valid FSRS cards not in remediation with future FSRS due position ($\text{DuePracticePosition} > \text{ProspectivePosition}$), ordered by `LastReviewPracticePosition` (nulls first), `DuePracticePosition`, `FactId`.

#### Remediation Priority Override:
Before applying role chains, if any materialized fact for the scheduled operation has `NeedsRemediation == true` with $\text{ProspectivePosition} \ge \text{LastReviewPracticePosition} + 4$, it immediately overrides selection as a `Remediation` role.

#### Role-Specific Fallback Chains:
- **`Requested New`**: $\text{New} \to \text{Useful Frontier} \to \text{Due} \to \text{Stale Maintenance} \to \text{Early Review}$
- **`Requested Due`**: $\text{Due} \to \text{Useful Frontier} \to \text{Stale Maintenance} \to \text{Early Review}$
- **`Requested Maintenance`**: $\text{Stale Maintenance} \to \text{Useful Frontier} \to \text{Due} \to \text{Early Review}$
- **`Requested Frontier`**: $\text{Useful Frontier} \to \text{Due} \to \text{Stale Maintenance} \to \text{Early Review}$

#### Invariants:
- Only the first `New` entry of a `Requested New` chain may materialize an unmaterialized fact. Non-New requested roles never materialize new facts.
- `Early Review` serves as the liveness bridge ensuring PracticePosition advances continuously even when all cards are future-due.
- Cooldown relaxation (exact fact distance 3, mirror distance 3 for Addition/Multiplication) operates strictly within the chosen semantic pool; it never shifts selection to a different fallback pool.
- An empty candidate state under supported transitions is an integrity failure and fails closed.

---

### 8. Terminal Curriculum Safety & Boundaries

Curriculum generation enforces strict safety limits bounded by `Int32` capacity:
- **Addition Terminal Band**: Index 125 (`ADD-P8-D0`)
- **Subtraction Terminal Band**: Index 135 (`SUB-P8-D0`)
- **Multiplication Terminal Band**: Index 32 (`MUL-P7-SCALED`)
- **Division Terminal Band**: Index 32 (`DIV-P7-SCALED`)

When an operation reaches its terminal band, it does not advance to invalid or overflowing bands; practice continues indefinitely through materialized semantic pools (`Due`, `Stale Maintenance`, `Early Review`, `Remediation`).

---

### 9. Session-Local Repeated-Error Teaching Intervention

When a learner struggles repeatedly with a specific fact within the active training session:
1. **Trigger Condition**: Tracked via session-local counter `_sessionConsecutiveErrors`. After the **second consecutive persisted error** (`Incorrect` or `Timeout`) for the exact same `FactId`, a teaching intervention is triggered (`SessionInteractionState.TeachingIntervention`).
2. **Presentation**: A blocking modal displays the canonical equation and correct result (e.g. `7 × 8 = 56`) with concise instruction to observe the fact.
3. **Acknowledgement & Non-Mutation**: The learner acknowledges the display (via Continue or Enter). Acknowledgement produces **zero learning mutations**: no attempt record, no PracticePosition increment, no FSRS card update, no item state mutation, and no store revision.
4. **Lifecycle**: The counter resets to 0 upon trigger, so continuing mistakes trigger in pairs (attempts 2, 4, etc.). A Correct answer resets the counter to 0. Counters are session-local and reset cleanly on session restart.

---

### 10. Session Check-Ins and Practice Flow

To provide meaningful feedback without distracting during active arithmetic recall:
1. **Cadence**: Triggered every 20 accepted attempts in the active session ($20, 40, 60, \dots$).
2. **Summary**: Displays correct count out of 20 and the deterministic median response latency of **Correct attempts only** (timeouts and incorrect answers are excluded from latency metrics).
3. **Actions**:
   - **`Keep Going`**: Continues practice, preparing the next deterministic fact with a full adaptive deadline starting from 0.
   - **`Take a Break`**: Immediately transitions the practice gate to `ManualPause` **before** preparing the next fact, guaranteeing that accumulated active elapsed time remains exactly zero while paused. Monotonic timing activates only upon explicit learner Resume.

---

### 11. Practice User Experience Polish

- **Minimalist HUD**: Permanent score display is removed from the normal practice header. Progress is monitored via the 4-operation HUD (`+`, `−`, `×`, `÷`) and periodic check-in modals.
- **Pause Control**: Rendered with an accessible CSS-drawn two-bar pause icon and styled with explicit danger/red treatment (`button-danger`).
- **Onboarding Copy**: Updated across English, German, and Russian with clear, human-oriented explanations of accuracy, speed, adaptive deadlines, and spaced repetition without exposing internal scheduler jargon.

---

## Consequences

### Benefits
- Learners experience personalized, responsive answer deadlines tailored to their actual proficiency.
- Rapid acquisition allows advanced learners to advance through trivial dense bands in 1 to 12 attempts rather than 40.
- Spaced repetition integrity is protected: mastered facts appear only when genuinely due or stale, avoiding repetitive filler.
- Repeated mistakes receive immediate visual remediation without corrupting spaced repetition statistics.
- Zero-timing pause guarantees eliminate accidental timeout penalties during break intervals.
- Schema V6 migration is fully transactional, lossless, and backward-compatible.

### Costs and Risks
- Adaptive pace calculation requires bounded query evidence across recent attempts on each fact transition.
- Complex multi-pool selector requires comprehensive deterministic unit and integration test suites.
- Fast acquisition eligibility rules require strict prefix verification to prevent accidental skip-ahead on incomplete data.

---

## Alternatives Considered

1. **Fixed 5% Deadline Reduction Step**:
   - *Rejected*: Too sluggish for strong learners encountering elementary facts; does not account for variance across arithmetic operations or places.
2. **Streak-Based Consecutive Fluency for Fast Acquisition**:
   - *Rejected*: Prone to race conditions and inconsistent evidence windows. Prefix-complete first-encounter evaluation over the owned frontier is mathematically robust.
3. **Dual Timers (Speed Goal + Hard Deadline)**:
   - *Rejected*: Creates unnecessary visual clutter and cognitive anxiety. A single adaptive timer cleanly unifies goal pacing and timeout limits.
4. **Persisting Ephemeral Adaptation Markers in SQLite**:
   - *Rejected*: Ephemeral presentation states (check-in counts, teaching pauses) pollute long-term learner records and introduce unnecessary migration friction.
