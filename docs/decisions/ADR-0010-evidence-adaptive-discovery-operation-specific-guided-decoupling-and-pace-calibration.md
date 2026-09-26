# ADR-0010: Evidence-Adaptive Discovery, Operation-Specific Guided Decoupling, and Pace Calibration

## Status

Accepted

## Date

2026-09-26

## Context

Following the establishment of independent operation progression in [ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md), adaptive pace estimation in [ADR-0004](ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md), rapid dense progression in [ADR-0005](ADR-0005-acclimation-timing-and-rapid-dense-progression.md), independent role ordinals in [ADR-0008](ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md), and Guided four-operation number-space gating in [ADR-0009](ADR-0009-guided-four-operation-number-space-gate.md), comprehensive multi-operation simulations, strong-learner acquisition audits, and device testing revealed several systemic pedagogical and architectural limitations:

1. **Inflexible Discovery Throttling for Fluent Learners**:
   The repeating 10-slot per-operation role cycle allocated only 4 slots (40% nominal share) to `New` facts. When a fluent learner had no overdue cards or unmastered frontier items, the remaining 6 slots queried review or frontier pools containing zero pedagogically useful work. Because non-New roles strictly cannot materialize new facts, the selector repeatedly served already-mastered facts as filler, slowing down fluent learners and inflating the attempt count needed to master elementary bands far above the curriculum-valid theoretical minimum.
2. **Immediate Exact-Fact Repetition on Cooldown Relaxation**:
   When candidate pools were small (such as single-fact frontier prefixes or isolated remediation items), third-tier candidate relaxation relaxed exact-fact cooldowns completely. If the only available candidate was the fact presented on the immediately preceding turn ($t-1$), the selector presented the exact same fact back-to-back (`X -> X`), violating the core expectation of spaced practice and creating poor learner ergonomics.
3. **Rigid Remediation Spacing for Persistent Errors**:
   The existing remediation override policy enforced a uniform minimum spacing of 4 turns between review attempts for any fact with `NeedsRemediation == true`. This treated a minor, isolated slip identically to a persistent, recurring misconception. In multi-operation practice, a 4-turn operation spacing allowed too many intervening turns, delaying crucial feedback for persistent errors.
4. **Multiplicative Stagnation Under Hard Guided-Gate Coupling**:
   Under ADR-0009, Multiplication and Division presentation eligibility was bound indefinitely by the Addition ceiling (`AdditionCeiling`). While essential for initial onboarding, this hard coupling prevented Multiplication from advancing beyond introductory facts even when the learner demonstrated complete, effortless mastery across factor bands 0, 1, and 2. Multiplication became starved for new material while waiting for slower Addition advancement.
5. **Uncalibrated Pace Instability and Speed Bonus Prematurity**:
   Empirical testing of the hierarchical pace shrinkage estimator revealed that early response latencies ($n < 24$) exhibit substantial volatility. Speed-dependent mechanics (such as Cyber Defense Critical Hits) awarded on uncalibrated estimates penalized young learners with motor hesitation or prematurely rewarded early lucky inputs before pace estimates stabilized.
6. **Gamification Authority Ambiguity**:
   The introduction of combat-style practice presentations (Cyber Defense) created a potential risk that game mechanics (enemy HP, boss phases, streaks, Critical Hits) could improperly influence mathematical evaluation, FSRS retention tracking, or band progression.

This decision defines the approved architecture: Evidence-Adaptive Discovery, an absolute No-Immediate-Fact-Repetition Invariant, Tiered Remediation Spacing, Evidence-Based Soft Decoupling (G3) for the Guided Number-Space Gate, Empirical Pace Calibration at $\ge 24$ positioned Correct attempts, Downstream Gamification Authority Boundaries, and the Strong-Learner 482-Attempt Benchmark.

---

## Decision

### 1. Evidence-Adaptive Discovery Policy

The static 10-slot role assignment is replaced by an **Evidence-Adaptive Discovery Policy**:

1. **Promotion of Non-Due Review Turns**:
   When a scheduled operation turn resolves to a review or consolidation role (`Due`, `Maintenance`, or `Frontier`), the selector checks whether the pool contains **pedagogically useful work**:
   - `Due`: Any eligible fact has $\text{DuePracticePosition} \le \text{ProspectivePracticePosition}$.
   - `Frontier`: Any materialized current-frontier fact is unmastered (`IsProvisionallyMastered == false` or latest outcome is non-correct).
   - `Maintenance`: Any materialized fact is stale ($\ge 40$ positions since last review).
2. **Dynamic Promotion to New**:
   If the scheduled review role contains **zero pedagogically useful work**, the current band contains eligible unmaterialized facts, and the learner exhibits **Clean/Strong Evidence** (clean evidence without active broad weakness), the turn is **promoted to `PracticeSelectionRole.New`**.
3. **Emergent Ratio**:
   No fixed global ratio (such as "90% New") is hardcoded. Discovery velocity emerges dynamically from demonstrated learner competence: fluent learners progress rapidly toward band completion, while struggling learners automatically receive consolidation turns.

---

### 2. Absolute No-Immediate-Fact-Repetition Invariant

A strict, universal repository invariant is established:

> **Invariant**: After an accepted presentation of `FactId X`, the immediately following presented fact **MUST NOT** be `FactId X`.

1. **Universal Scope**:
   Applies unconditionally across all outcomes: `Correct` (fast or slow), `Incorrect`, `Timeout`, and `Remediation`.
2. **Priority Over Aesthetics**:
   Exact duplicate prevention **outranks anti-ladder aesthetics**. If avoiding an immediate duplicate requires presenting a ladder neighbor (e.g. `LeftOperand` difference of 1), the selector must choose the ladder candidate rather than repeating the exact same `FactId`.
3. **Single-Candidate Resolution**:
   If the candidate pool contains only the immediately previous `FactId`, the selector must seek a candidate from alternative semantic pools (`Useful Frontier`, `Due`, `Early Review`) or an alternative enabled operation rather than relaxing into an immediate duplicate.

---

### 3. Tiered Weakness Model and Remediation Spacing

MathFirst distinguishes three levels of learner weakness:

1. **Local Weakness (Isolated Error)**:
   - A single error on a previously mastered or new fact.
   - Remains localized: fact enters remediation with ordinary remediation spacing (tested Candidate-C cooldown: $\ge 4$ operation turns).
   - Unrelated facts and operations continue progressing freely.
2. **Repeated Local Weakness (Persistent Misconception)**:
   - Repeated error on the same `FactId`, or re-failing during remediation.
   - Activates **tightened remediation spacing** (tested Candidate-C cooldown: $\ge 2$ operation turns) to reinforce the correct mental model before memory traces decay.
   - Compatibility with existing presentation-layer teaching intervention mechanisms (e.g. ADR-0004 equation lockout) is preserved where applicable, but teaching interventions are not a newly mandated learning-engine requirement of ADR-0010.
3. **Broad Weakness (Systemic Distress)**:
   - Multiple simultaneously unresolved remediation facts within the currently relevant learning context indicate broad weakness and suppress aggressive New introduction while meaningful remediation/reinforcement work exists.
   - Tested Candidate-C rule: active unresolved remediation facts $\ge 2$ (with the exact scope/context of active remediation facts pinned by tests during implementation planning).
   - **New Discovery Suppression**: Promotion of review slots to New is disabled while broad weakness exists. Practice prioritizes consolidation and repair until active weaknesses clear.
4. **Recovery**:
   - A `Correct` response in remediation clears `NeedsRemediation` and restores normal spacing.

---

### 4. Preservation of Dense Progression Invariants

The Dense band progression gate established in [ADR-0005](ADR-0005-acclimation-timing-and-rapid-dense-progression.md) is upheld without dilution:

1. Requires **complete owned-frontier coverage** ($N = |\text{frontier}|$).
2. Requires **$\ge 90\%$ latest correctness**:
   $$C \cdot 10 \ge N \cdot 9$$
   where $C$ is the count of owned-frontier facts whose authoritative latest attempt in the band instance is `Correct`.
3. Proposals to weaken the threshold to 80% are explicitly **rejected**. High foundational accuracy is essential to prevent cumulative arithmetic debt.

---

### 5. Guided Number-Space Gate: Approved G3 Evidence-Based Soft Decoupling

The hard coupling between Addition and multiplicative operations in Guided Mode ([ADR-0009](ADR-0009-guided-four-operation-number-space-gate.md)) is replaced with **Evidence-Based Soft Decoupling (G3)**:

1. **Initial Shared Foundation (Bands 0–2)**:
   - While `Multiplication.OperationProgression.BandIndex < 3`, Multiplication remains constrained by `AdditionCeiling` ($\text{CorrectResult} \le \text{AdditionCeiling}$).
   - While `Division.OperationProgression.BandIndex < 3`, Division remains constrained by `AdditionCeiling` ($\text{LeftOperand} \le \text{AdditionCeiling}$).
2. **Operation-Specific Soft Decoupling Trigger**:
   - Multiplication decouples from `AdditionCeiling` if and only if:
     $$\text{Multiplication.OperationProgression.BandIndex} \ge 3$$
   - Division decouples from `AdditionCeiling` if and only if:
     $$\text{Division.OperationProgression.BandIndex} \ge 3$$
3. **Rationale**:
   Advancing to `BandIndex >= 3` proves that the learner has completed Bands 0, 1, and 2 under the strict Dense requirements (complete coverage and $\ge 90\%$ latest correctness), demonstrating verified competence across the introductory 0, 1, 2, and 3 factor space.
4. **Autonomous Canonical Progression Post-Decoupling**:
   Decoupling does **not** cause an artificial leap to a magic ceiling like 100. The operation's own canonical curriculum continues gradual, structured progression (`MUL-D04`, `MUL-D05`, etc.).
5. **Custom Mode Invariance**:
   Custom Mode remains completely unrestricted.
6. **Zero Migration**:
   Decoupling status is derived directly from durable `OperationProgression.BandIndex`.

---

### 6. Strong-Learner Acquisition Benchmark (482 Attempts)

The native repository benchmark for strong-learner velocity is:
$$\text{ADD-D10} \longrightarrow \text{ADD-P1-ANCHOR}$$
advancing Guided `AdditionCeiling` from **20** to **180**.

- Completing Addition Dense Band 10 requires mastering all 121 unique Addition facts ($0+0$ through $10+10$).
- Under deterministic four-operation bounded permutation scheduling with equal turn allocation, 121 Addition turns yield a mathematical lower bound of **482 global accepted attempts**.
- This benchmark serves as the reference standard for future learning-engine regression tests.

---

### 7. Pace Calibration Readiness at 24 Positioned Correct Attempts

Pace calibration is declared **READY** if and only if:
$$\text{Count}\left( \text{attempt} \in \text{attempt\_history} \mid \text{PracticePosition} > 0 \land \text{Outcome} == \text{AttemptOutcome.Correct} \right) \ge 24$$

1. **Rejection of n=12**:
   Empirical simulation demonstrated that at $n=12$, shrinkage pace estimates drift by $>25\%$, producing erratic speed thresholds.
2. **Empirical Basis for n=24**:
   In four-operation Guided Mode, subsequent `EasyThresholdMs` drift from $n=24$ to full convergence ($n=30$) is **$4.6\%$** for intermediate learners and **$9.0\%$** for strong learners. Thus, $n=24$ is the earliest fixed checkpoint where representative learner drift remains reliably below $10\%$.
3. **Durability**:
   Calibration is derived directly from durable attempt history without complex ephemeral stability trackers.

---

### 8. Downstream Gamification & Critical Hit Contract

Game mechanics are strictly **downstream presentation consumers** of learning telemetry:

1. **Before Calibration ($n < 24$ positioned Correct attempts)**:
   - All mathematically `Correct` answers deal **1 HP normal damage**.
   - No Critical Hits or speed bonuses are awarded.
2. **After Calibration ($n \ge 24$ positioned Correct attempts)**:
   - A `Correct` answer submitted with $\text{ResponseLatencyMs} \le \text{CurrentFactEasyThresholdMs}$ scores a **Critical Hit**, dealing **2 HP damage**.
   - A `Correct` answer submitted with $\text{ResponseLatencyMs} > \text{CurrentFactEasyThresholdMs}$ deals **1 HP normal damage**.
3. **Strict Non-Mutation Boundary**:
   Combat damage, Critical Hits, enemy HP, combos, and defeats **MUST NEVER** alter:
   - `AttemptOutcome` or response latency;
   - `AttemptRecord.IsFluent` or FSRS ratings (`Again`, `Hard`, `Good`, `Easy`);
   - FSRS card stability or scheduled intervals;
   - `ItemLearningState`, error counts, or remediation state;
   - `OperationProgression` or band advancement gates;
   - Global `PracticePosition`.

---

### 9. Returning Learner Continuity & Schema V6 Preservation

1. **Lossless Persistence**:
   Returning learners resume existing curriculum progression, FSRS cards, and attempt counts without artificial diagnostic placement tests.
2. **Schema V6 Preservation**:
   All features are implemented strictly within **Schema V6**. No database tables, columns, or migration scripts are added.

---

## Consequences

### Positive
- **Optimal Learner Velocity**: Fluent learners avoid redundant review filler and progress toward the theoretical 482-attempt benchmark without artificial friction.
- **Cognitive Safety**: Struggling learners are protected from premature new introductions through automatic broad-weakness discovery suppression.
- **Ergonomic Integrity**: The absolute no-immediate-repeat invariant completely eliminates repetitive back-to-back presentations (`X -> X`).
- **Targeted Misconception Repair**: Repeated errors receive responsive, tighter remediation without penalizing unrelated operations.
- **Multiplicative Unlocking**: Evidence-based soft decoupling allows learners demonstrating early multiplicative mastery to continue progressing naturally.
- **Fair Gamification**: Speed-based Critical Hits activate only after pace estimates have empirically stabilized ($n \ge 24$).
- **Pedagogical Purity**: Learning telemetry remains untainted by downstream game mechanics.
- **Zero Storage Friction**: Implemented fully within Schema V6 with zero migration risk.

### Negative and Operational Considerations
- The practice selector logic requires clear state evaluation to distinguish between useful review work and empty review slots.
- Bounded fallback chains must carefully handle edge cases where candidate pools are constrained to ensure the no-immediate-repeat invariant is satisfied.

---

## Alternatives Considered and Rejected

1. **Fixed Global "90% New" Discovery Ratio**:
   - *Rejected*: A hardcoded ratio is pedagogically blind. It pushes new material onto struggling learners and fails to adapt to varying band sizes. An evidence-adaptive policy cleanly derives the discovery rate from live learner performance.
2. **Diluting Dense Progression Mastery to 80%**:
   - *Rejected*: In foundational arithmetic, an 80% threshold allows 1 in 5 facts to remain unmastered, creating compound weaknesses in later structured families. Mastery must strictly require $\ge 90\%$ latest correctness ($C \cdot 10 \ge N \cdot 9$).
3. **Allowing Immediate Exact Duplicate on Single-Candidate Pools**:
   - *Rejected*: Repeating the exact same fact immediately after presentation feels defective to learners and bypasses retrieval spacing. The selector must seek valid alternative pools or defer the role.
4. **Retaining Hard Guided-Gate Coupling Without Decoupling**:
   - *Rejected*: Trapped capable learners in repetitive introductory multiplication practice while waiting for multi-fact addition bands to complete.
5. **Decoupling Directly to a Magic Ceiling (e.g. 100)**:
   - *Rejected*: Violates the progressive scaffolding principle of MathFirst. Decoupling must return progression authority to the canonical curriculum (`MUL-D04`, `MUL-D05`), not jump to an arbitrary number.
6. **Declaring Pace Calibration Ready at n=12 or n=20**:
   - *Rejected*: Empirical evidence proved drift exceeds $25\%$ at $n=12$ and reaches $17.9\%$ at $n=20$. Only at $n=24$ does drift reliably stay below $10\%$.
7. **Allowing Combat Defeats or Combos to Influence Learning Telemetry**:
   - *Rejected*: Merging game state into learning telemetry corrupts FSRS memory models and violates core repository safety.
8. **Introducing an Initial Placement Diagnostic**:
   - *Rejected*: Placement tests create anxiety and produce artificial boundaries. Continuous adaptive practice serves as the placement mechanism.
