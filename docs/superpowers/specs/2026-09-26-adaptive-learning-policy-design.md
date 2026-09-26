# MathFirst Adaptive Learning Policy Design Specification

**Status:** Approved Design Specification — Implementation Pending  
**Date:** 2026-09-26  
**Document Mode:** `DOCUMENT_ONLY`  
**Governing Architecture Records:** [ADR-0003](../../decisions/ADR-0003-independent-operation-progression-and-open-ended-fact-space.md), [ADR-0004](../../decisions/ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md), [ADR-0005](../../decisions/ADR-0005-acclimation-timing-and-rapid-dense-progression.md), [ADR-0007](../../decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md), [ADR-0008](../../decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md), [ADR-0009](../../decisions/ADR-0009-guided-four-operation-number-space-gate.md), [ADR-0010](../../decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md)  
**Authoritative Product Contract:** [docs/PRODUCT.md](../../PRODUCT.md)  

---

## 1. Goals

1. **Evidence-Adaptive Discovery**: Replace the conceptual static 10-slot practice cycle with an evidence-adaptive fact-selection policy where discovery pressure dynamically emerges from demonstrated learner competence rather than hardcoded ratios.
2. **Fluent-Learner Velocity**: Allow fluent learners demonstrating consistent, clean mastery to progress close to the curriculum-valid theoretical minimum without artificial review stalls on non-due or redundant items.
3. **Struggling-Learner Scaffolding**: Automatically throttle new-material discovery when broad or unresolved arithmetic weakness is detected, shifting practice capacity into consolidation, due review, and targeted remediation.
4. **Absolute No-Immediate-Fact-Repetition Invariant**: Enforce a strict repository-wide guarantee that after an accepted presentation of `FactId X`, the immediately following presented fact must never be `FactId X`, regardless of outcome or aesthetic ladder constraints.
5. **Tiered Weakness & Targeted Remediation**: Preserve the distinction between an isolated error, repeated local weakness, and broad cross-operation weakness. Provide tighter remediation spacing for persistent local errors while preventing an isolated error from globally suppressing progress in unrelated operations.
6. **Preservation of Dense Mastery**: Uphold the established Dense band progression requirement of 100% owned-frontier coverage and $\ge 90\%$ latest correctness ($C \cdot 10 \ge N \cdot 9$) without dilution.
7. **Guided Gate Soft Decoupling (G3)**: Maintain the protective Addition-governed number-space ceiling for Multiplication and Division during introductory bands (Bands 0, 1, and 2), and softly decouple each operation once it independently proves competence by advancing to `BandIndex >= 3`.
8. **Strong-Learner Acquisition Benchmark**: Establish the native theoretical benchmark of **482 global accepted attempts** for a 100%-correct fluent learner in four-operation Guided Mode to progress from cold start through `ADD-D10 -> ADD-P1-ANCHOR` (expanding AdditionCeiling from 20 to 180).
9. **Empirically Calibrated Pace**: Establish pace calibration readiness at $\ge 24$ positioned Correct attempts, ensuring learner-relative speed thresholds stabilize (drift $< 10\%$) before granting speed-contingent game mechanics.
10. **Downstream Game Authority Boundary**: Isolate combat and gamification mechanics (such as Cyber Defense Critical Hits) as strictly downstream presentation consumers with zero authority over learning telemetry, FSRS scheduling, or curriculum progression.
11. **Lossless Returning-Learner Continuity**: Retain durable curriculum progress, FSRS states, and attempt histories across application restarts without artificial startup placement tests.
12. **Persistence Integrity**: Implement all approved behaviors entirely within Schema V6 with zero SQLite table or column migrations.

---

## 2. Non-Goals

1. **No Production Implementation in this Specification**: This document establishes the approved technical design. Production code, test modifications, and implementation branches are strictly deferred to subsequent governed packages.
2. **No FSRS Core Modification**: The underlying FSRS-6 algorithm, parameters, and 95% desired retention target remain untouched.
3. **No Weakening of Dense Mastery**: The $\ge 90\%$ latest-correctness threshold ($C \cdot 10 \ge N \cdot 9$) is not reduced to 80% or any lower percentage.
4. **No Static Global Ratio**: The architecture will not encode a hardcoded global "90% New" or "80% New" ratio. Discovery pressure must be dynamically derived from learner evidence.
5. **No Sudden Multiplicative Ceiling Jump**: Post-decoupling, Multiplication and Division do not jump to an arbitrary magic ceiling (such as 100). Progression continues procedurally under the operation's own canonical curriculum.
6. **No Pre-Practice Placement Test**: MathFirst will not introduce an artificial initial diagnostic test. Regular adaptive practice remains the placement and progression mechanism.
7. **No Reverse Telemetry from Game to Learning Core**: Combat losses, combos, enemy HP, and Critical Hits must never alter `AttemptOutcome`, FSRS card stability, item learning states, or band advancement.
8. **No Dynamic Operation Turn Stealing**: Turn allocation across enabled operations remains strictly deterministic via bounded permutation bags (equal share; 25% nominal in four-operation mode).

---

## 3. Current Failure Modes and Analysis

The current learning runtime (governed by ADR-0004, ADR-0005, ADR-0008, and ADR-0009) contains four primary architectural failure modes identified during diagnostic testing and multi-operation simulation:

### 3.1 Static Discovery Stall for Fluent Learners
Under the repeating 10-slot per-operation role cycle ([ADR-0008](../../decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)), only slots 1, 3, 6, and 8 nominally request `New` material (40% nominal share). For a rapid, fluent learner encountering a new band:
- Slots 2, 7, and 9 request `Due`;
- Slot 4 requests `Maintenance`;
- Slots 5 and 10 request `Frontier`.

When the learner has no genuinely due cards (`DuePracticePosition > prospectivePosition`) and the current owned frontier contains unmaterialized facts, non-New requested roles fall back through `Useful Frontier` or `Early Review`. Because non-New roles cannot materialize new facts (the Authoritative Materialization Invariant), the selector repeatedly surfaces already-seen facts as artificial review filler. This forces fluent learners to execute redundant repetitions before reaching the band's remaining unseen facts, inflating total attempts far beyond the curriculum-valid theoretical minimum.

### 3.2 Immediate Exact-Fact Duplicate Relaxation
In `AdaptivePracticeSelector.SelectTargetCandidate`, candidate filtering applies three relaxation tiers:
1. Tier 1: Exact cooldown (distance 3) + Commutative mirror cooldown (distance 3) + Same-operation repeat guard + Anti-ladder.
2. Tier 2: Mirror relaxed + Exact cooldown preserved + Anti-ladder.
3. Tier 3: Exact cooldown relaxed + Anti-ladder.

When a semantic candidate pool contains only one viable fact (common in small dense bands, introductory prefixes, or isolated remediation), Tier 1 and Tier 2 fail. In Tier 3, exact cooldown is completely relaxed. If the only available fact is the fact that was presented on the immediately preceding turn ($t-1$), Tier 3 selects it. Furthermore, if anti-ladder filtering discards alternatives, the selector can fall back to the exact same `FactId`. Consequently, learners experience back-to-back duplicate presentations (`X -> X`), which feels broken, repetitive, and defeats spaced retrieval.

### 3.3 Uniform Remediation Spacing for Persistent Errors
Under the existing remediation override policy, any fact with `NeedsRemediation == true` becomes eligible for remediation when:
$$\text{ProspectivePosition} \ge \text{LastReviewPracticePosition} + 4$$
This fixed distance of 4 applies identically to:
- An accidental motor slip on an otherwise mastered fact (isolated error);
- A severe, recurring misconception where the learner has failed the fact 3 or 4 consecutive times.

For repeated local misconceptions, a cooldown of 4 turns in a 4-operation rotation (amounting to 16 global attempts) allows too much time to elapse, allowing the misconception to solidify before the next intervention. Conversely, treating an isolated error with the same urgency as a persistent breakdown creates unnecessary review churn.

### 3.4 Hard Guided-Gate Coupling Deadlock
Under [ADR-0009](../../decisions/ADR-0009-guided-four-operation-number-space-gate.md), Multiplication and Division presentation eligibility in Guided Mode is strictly bounded by `AdditionCeiling`:
$$\text{owner}_{\text{MUL}}(F) \le \text{BandIndex}_{\text{MUL}} \quad \text{AND} \quad F.\text{CorrectResult} \le \text{AdditionCeiling}$$
$$\text{owner}_{\text{DIV}}(F) \le \text{BandIndex}_{\text{DIV}} \quad \text{AND} \quad F.\text{LeftOperand} \le \text{AdditionCeiling}$$

While this protects early learners during the very first steps, `AdditionCeiling` expands only when Addition advances bands. If a learner practices all four operations and demonstrates effortless mastery of Multiplication factors 0, 1, 2, and 3, Multiplication cannot advance past products $> \text{AdditionCeiling}$ until Addition moves through multiple multi-fact bands. Multiplication becomes throttled and locked into repetitive reviews of elementary facts despite proven competency in introductory multiplication.

---

## 4. Evidence-Adaptive Fact-Selection and Discovery Model

The architecture transitions from a static 10-slot role assignment to an **Evidence-Adaptive Discovery Policy**.

### 4.1 Discovery Opportunity Promotion
The nominal role cycle continues to provide a deterministic baseline scaffolding. However, when a scheduled turn resolves to a review or consolidation role (`Due`, `Frontier`, or `Maintenance`), the engine evaluates whether that role contains **pedagogically useful work**:

1. **Pedagogically Useful Work Definition**:
   - `Due`: At least one eligible materialized fact has $\text{DuePracticePosition} \le \text{ProspectivePracticePosition}$.
   - `Frontier Consolidation`: At least one materialized current-band fact is unmastered (`IsProvisionallyMastered == false` or latest outcome is non-correct).
   - `Remediation`: At least one eligible fact has `NeedsRemediation == true`.
   - `Maintenance`: At least one materialized fact has not been reviewed within the stale threshold ($\ge 40$ positions).

2. **Promotion Rule**:
   If the scheduled review/frontier role has **zero pedagogically useful work** AND the current band contains eligible unmaterialized material (`New` pool is non-empty) AND learner evidence is **Clean/Strong** (Section 5), the scheduled turn is **promoted to `PracticeSelectionRole.New`**.

3. **Emergent Discovery Ratio**:
   - For a fluent learner with zero errors, zero overdue cards, and all active frontier facts mastered, review slots 2, 4, 5, 7, 9, 10 promote to New. Discovery pressure rises naturally toward 100% of available band material.
   - For a learner with overdue cards or active misconceptions, review slots are preserved for consolidation. Discovery pressure naturally drops to 40% or lower.
   - No static "90% New" ratio is hardcoded; the ratio is a mathematical consequence of live learner evidence.

---

## 5. Local vs. Broad Weakness Model

To ensure adaptations are proportionate and pedagogically sound, the learning engine distinguishes three levels of learner weakness:

```mermaid
flowchart TD
    A["Learner Attempt Outcome"] --> B{"Outcome == Correct?"}
    B -->|Yes| C["Record Correct Evidence"]
    C --> D{"Fact In Remediation?"}
    D -->|Yes| E["Clear NeedsRemediation<br/>Restore Normal Spacing"]
    D -->|No| F["Continue Clean Progression"]
    
    B -->|No (Incorrect / Timeout)| G["Flag Fact: NeedsRemediation = true"]
    G --> H{"Error History on this Fact?"}
    H -->|First Error| I["Classification: Local Weakness (Isolated)<br/>Cooldown = 4 turns"]
    H -->|Consecutive Error| J["Classification: Repeated Local Weakness<br/>Cooldown = 2 turns"]
    
    I --> K{"Active Remediation Count Across All Operations?"}
    J --> K
    K -->|< 2 Facts| L["Scope: Local Only<br/>Unrelated operations progress freely"]
    K -->|>= 2 Facts| M["Scope: Broad Weakness<br/>Suppress New Discovery across affected operations<br/>Force Due & Consolidation"]
```

### 5.1 Local Weakness (Isolated Mistake)
- **Definition**: A single fact $F$ incurs an `Incorrect` or `Timeout` outcome after a history of correct responses, or as an initial hesitation.
- **Classification**: Local only. It does not classify the learner as globally struggling.
- **Impact**:
  - Fact $F$ sets `NeedsRemediation = true`.
  - Ordinary remediation spacing applies (tested Candidate-C cooldown: $\text{distance} \ge 4$).
  - Unrelated facts within the same operation and all other arithmetic operations continue normal discovery and progression unhindered.

### 5.2 Repeated Local Weakness (Persistent Misconception)
- **Definition**: The same fact $F$ incurs $\ge 2$ consecutive errors (`Incorrect` or `Timeout`), or fails again during a remediation presentation.
- **Classification**: Local persistent misconception.
- **Impact**:
  - Activates **tightened remediation spacing** (Section 6; tested Candidate-C cooldown: $\text{distance} \ge 2$).
  - Compatibility with existing presentation-layer teaching intervention mechanisms (such as the ADR-0004 equation lockout dialog) is preserved as existing/deferred presentation behavior where applicable, but teaching interventions are not a newly mandated requirement of this learning policy.
  - Still does not lock down unrelated operations unless the broad threshold is reached.

### 5.3 Broad Weakness (Systemic Distress)
- **Definition**: Multiple simultaneously unresolved remediation facts within the currently relevant learning context indicate broad weakness and suppress aggressive New introduction while meaningful remediation/reinforcement work exists.
  In Candidate-C simulation testing, this rule was empirically evaluated as:
  $$\text{Count}(\text{Active unresolved remediation facts in context}) \ge 2$$
  The exact evaluation scope and context of active unresolved remediation facts will be pinned with unit and contract tests during implementation planning (`MF-LEARN-006`). No arbitrary rolling-accuracy percentage or window threshold is imposed.
- **Classification**: Systemic/Broad Weakness.
- **Impact**:
  - **New Discovery Suppression**: Promotion of review slots to New (Section 4) is disabled while broad weakness is active.
  - Nominal `Requested New` slots prioritize remediation or frontier consolidation if unmaterialized material cannot be safely assimilated.
  - The practice engine focuses entirely on repairing existing vulnerabilities before introducing further cognitive load.

### 5.4 Recovery Dynamics
When a weak fact is answered `Correct` with valid response latency during remediation:
- `NeedsRemediation` is cleared to `false`;
- The fact transitions back to normal FSRS scheduling with updated stability;
- When active unresolved remediation facts drop below the broad weakness threshold, broad weakness suppression automatically lifts, and normal discovery potential is restored.

---

## 6. Remediation Behavior and Cooldown Spacing

Remediation intercepts practice turns to repair errors before they become ingrained habits.

### 6.1 Remediation Precedence
1. **Preemption Authority**: Eligible remediation preempts non-New roles (`Due`, `Maintenance`, `Frontier`) within the scheduled operation.
2. **Protected New Exception**: In accordance with MF-LEARN-005 / ADR-0004 Amendment, if the requested role is `New` and the current band has eligible unmaterialized facts, the New opportunity is **protected** to guarantee curriculum prefix acquisition. Once unmaterialized candidates in the active frontier are exhausted, remediation may preempt New.

### 6.2 Remediation Cooldown Spacing
The required attempt spacing between the previous attempt of fact $F$ and its remediation presentation is governed by error recurrence:

$$\text{RemediationCooldown}(F) = \begin{cases} 2 \text{ operation attempts} & \text{if } F \text{ has repeated/consecutive errors} \\ 4 \text{ operation attempts} & \text{if } F \text{ has an isolated error} \end{cases}$$

- **Tighter Spacing for Repeated Errors**: A repeated mistake must be reinforced quickly (tested Candidate-C cooldown of 2 operation turns, corresponding to roughly 8 global attempts in 4-operation mode) to catch and resolve the persistent misconception before memory traces decay.
- **Isolated Error Spacing**: An isolated mistake allows a standard 4-turn buffer, preventing the learner from feeling harassed by accidental slips.

---

## 7. Absolute No-Immediate-Fact-Repetition Invariant

### 7.1 Formal Invariant Definition
> **Invariant**: For any accepted presentation at global sequence index $t$ presenting $\text{FactId}(t)$, the immediately following presented fact at sequence index $t+1$ **MUST NOT** have the same identifier:
> $$\text{FactId}(t+1) \ne \text{FactId}(t)$$

### 7.2 Universal Application Scope
This invariant is unconditional and strictly holds after:
- Mathematically `Correct` responses (both fast and slow);
- `Incorrect` responses;
- `Timeout` responses;
- Remediation turns;
- Teaching interventions;
- Operation switches (if cross-operation fact overlap were ever possible);
- Single-operation practice configurations.

### 7.3 Priority Over Aesthetics
**Exact duplicate prevention strictly outranks anti-ladder aesthetics.**  
If excluding the immediately previous `FactId` leaves only candidates that form an arithmetic ladder (e.g. `LeftOperand` difference of 1), the selector **MUST** choose the ladder candidate rather than relaxing into an exact duplicate presentation.

### 7.4 Single-Candidate Pool Resolution
If the requested semantic pool contains *only* $\text{FactId}(t)$:
1. Selection must not relax into $\text{FactId}(t)$.
2. The selector must inspect alternative valid semantic pools for the scheduled operation (`Useful Frontier`, `Due`, `Maintenance`, `Early Review`) to find a candidate where $\text{FactId} \ne \text{FactId}(t)$.
3. In multi-operation modes (such as Guided Mode), if the scheduled operation has literally no other valid fact in existence, the scheduler must select an alternative enabled operation for that turn rather than repeating the exact fact back-to-back.
4. If the entire repository state contains only one fact (e.g. single-operation mode with exactly one materialized fact and zero unmaterialized facts in Band 0), the system must enforce a non-repeating instructional interaction or fail closed; under no circumstances may the same exercise be rendered twice consecutively without intervening activity.

---

## 8. Dense Progression Invariants

The Dense band advancement model established in [ADR-0005](../../decisions/ADR-0005-acclimation-timing-and-rapid-dense-progression.md) is preserved without compromise:

1. **Frontier Coverage Requirement**: Every exact fact owned by the current Dense band frontier ($N = |\text{frontier}|$) must have at least one positioned attempt in the current band instance ($PracticePosition > \text{BandStartedPracticePosition}$).
2. **Correctness Mastery Inequality**:
   Let $C$ be the number of owned-frontier facts whose authoritative latest attempt in the current band instance is `Correct`. Advancement occurs if and only if:
   $$C \cdot 10 \ge N \cdot 9$$
3. **No Weakening to 80%**: Proposals to relax the mastery requirement to 80% ($C \cdot 10 \ge N \cdot 8$) are explicitly **rejected**. In elementary arithmetic foundations, allowing a 20% error rate creates compounding learning debt that destabilizes multi-digit arithmetic.
4. **Integer Cross-Multiplication**: All progression checks use exact integer cross-multiplication, avoiding IEEE 754 floating-point ambiguity.
5. **Recoverable Mistakes**: An earlier mistake or timeout does not permanently block advancement; a subsequent `Correct` on that fact updates its latest vote to `Correct`.

---

## 9. Guided Gate G3 Soft-Decoupling Design

### 9.1 The Problem with Hard Coupling
In [ADR-0009](../../decisions/ADR-0009-guided-four-operation-number-space-gate.md), Multiplication and Division are constrained by the maximum number in the unlocked Addition prefix (`AdditionCeiling`). While pedagogically sound for initial grounding, locking Multiplication to Addition indefinitely ignores demonstrated multiplicative competence:
- Multiplication Bands 0, 1, and 2 cover factors 0, 1, 2, and 3 (`0x0` through `3x3`).
- Advancing past Band 2 proves that the learner has mastered the complete 0, 1, 2, and 3 times tables under Dense $\ge 90\%$ rules.
- Holding Multiplication back after this milestone creates artificial stagnation.

### 9.2 Approved Solution: G3 Evidence-Based Soft Decoupling
Guided Mode remains active whenever all four operations are enabled (`Addition`, `Subtraction`, `Multiplication`, `Division`).

```mermaid
flowchart TD
    subgraph Guided Mode [All 4 Operations Enabled]
        A["Multiplication / Division Turn"] --> B{"Current Operation<br/>BandIndex >= 3?"}
        B -->|No (Bands 0, 1, 2)| C["Hard Constrained by AdditionCeiling<br/>Requires: Result / Dividend <= AdditionCeiling"]
        B -->|Yes (Band 3+)| D["Operation-Specific Soft Decoupling<br/>AdditionCeiling check bypassed!"]
        D --> E["Autonomous Canonical Progression<br/>MUL-D04 -> MUL-D05 -> ...<br/>Curriculum controls gradual expansion"]
    end
    
    subgraph Custom Mode [Any Other Subset]
        F["Custom Mode Active"] --> G["Unrestricted: Guided Gate Inactive"]
    end
```

#### Decoupling Rules:
1. **Initial Shared Foundation (Bands 0–2)**:
   - While `Multiplication.OperationProgression.BandIndex < 3`, Multiplication remains constrained by `AdditionCeiling`:
     $$\text{fact.CorrectResult} \le \text{AdditionCeiling}$$
   - While `Division.OperationProgression.BandIndex < 3`, Division remains constrained by `AdditionCeiling`:
     $$\text{fact.LeftOperand} \le \text{AdditionCeiling}$$
2. **Operation-Specific Decoupling Trigger**:
   - Multiplication decouples from `AdditionCeiling` if and only if:
     $$\text{Multiplication.OperationProgression.BandIndex} \ge 3$$
   - Division decouples from `AdditionCeiling` if and only if:
     $$\text{Division.OperationProgression.BandIndex} \ge 3$$
3. **Rationale**:
   Reaching `BandIndex >= 3` proves that the learner has completely covered and achieved $\ge 90\%$ latest correctness across Bands 0, 1, and 2 in that specific operation. For Multiplication, this directly verifies mastery of the foundational 0, 1, 2, and 3 factor space.
4. **Gradual Expansion, No Magic Leap**:
   Decoupling does **not** jump Multiplication or Division to an arbitrary magic ceiling (such as 100). Instead, the operation's own canonical curriculum resumes authoritative control. `MUL-D04` (factor 4), `MUL-D05` (factor 5), etc., expand the number space in gradual, pedagogically designed increments.
5. **Independent Decoupling**:
   Multiplication and Division decouple independently based on their own evidence. If Multiplication advances to Band 3 while Division remains in Band 1, Multiplication decouples while Division remains gated by `AdditionCeiling`.
6. **Custom Mode Invariance**:
   Custom Mode (any configuration other than all four operations enabled) remains completely unrestricted as established in ADR-0009.
7. **Durable Reconstruction**:
   Decoupling is evaluated directly from persisted `OperationProgression.BandIndex`. No new schema columns, database flags, or migration steps are required.

---

## 10. Strong-Learner 482-Attempt Benchmark

To prevent future regressions in learning velocity, the repository establishes an authoritative strong-learner acquisition benchmark.

### 10.1 The Canonical Milestone
The native milestone is reaching the completion of Addition Dense Band 10 and entering the decimal anchor:
$$\text{ADD-D10} \longrightarrow \text{ADD-P1-ANCHOR}$$
This milestone transitions the Guided `AdditionCeiling` from **20** to **180**. There is no artificial ~100 Addition milestone.

### 10.2 Mathematical Derivation of the Lower Bound
1. **Addition Fact Requirements**:
   - Dense Addition foundation requires all ordered pairs $(a, b)$ for $a, b \in [0, 10]$.
   - Total unique Addition facts: $11 \times 11 = \mathbf{121\text{ facts}}$.
2. **Ideal Fluent Learner Conditions**:
   - 100% correct responses;
   - Immediate recall under adaptive fluency thresholds;
   - Zero errors, zero timeouts;
   - Every Addition presentation introduces a new unseen fact until Band 10 is complete ($N=121$ attempts).
3. **Four-Operation Bounded Permutation Scheduling**:
   - In Guided Mode with all four operations enabled, `DeterministicOperationScheduler` schedules operations in 4-element permutation bags with equal nominal share (25% per operation).
   - Serving 121 Addition turns requires:
     $$121 \times 4 = 484\text{ global attempts}$$
   - Accounting for deterministic permutation bag alignment (where the final Addition turn occurs in the 2nd slot of the final bag), the exact theoretical minimum is:
     $$\mathbf{482\text{ global accepted attempts}}$$

### 10.3 Benchmark Enforcement
Future integration tests and policy simulations must use this 482-attempt figure as the reference lower bound for strong-learner performance. The evidence-adaptive fact selector should enable a 100%-correct simulated learner to achieve ADD-D10 completion at or very near 482 attempts.

---

## 11. Pace Calibration at 24 Positioned Correct Attempts

### 11.1 Rejection of n=12 Proposal
An earlier draft proposed declaring pace calibration complete at $n \ge 12$ attempts. Empirical simulation of the production shrinkage estimator hierarchy:
$$\text{Static Prior } (P_0) \longrightarrow P_{\text{learner}} \longrightarrow P_{\text{operation}} \longrightarrow P_{\text{band}} \longrightarrow P_{\text{fact}} \longrightarrow \text{EasyThresholdMs}$$
demonstrated that at $n=12$, expected pace and `EasyThresholdMs` remain highly volatile, with subsequent drift exceeding 25%.

### 11.2 The n=24 Stability Criterion
Testing the hierarchy against representative intermediate and strong learner latency profiles in four-operation Guided Mode revealed:

| Checkpoint | Intermediate Learner Drift to $n=30$ | Strong Learner Drift to $n=30$ | Stability Assessment |
|---|---|---|---|
| **$n = 12$** | $> 20.0\%$ | $> 30.0\%$ | Unstable / Volatile |
| **$n = 20$** | $9.7\%$ | $17.9\%$ | Marginally Unstable |
| **$n = 24$** | **$4.6\%$** | **$9.0\%$** | **Stable ($< 10\%$ drift)** |
| **$n = 30$** | Baseline ($0\%$) | Baseline ($0\%$) | Fully Converged |

At $n=24$, both intermediate and strong learners exhibit subsequent `EasyThresholdMs` movement of less than $10\%$ relative to full convergence at $n=30$.

### 11.3 Calibration Readiness Definition
> **Pace Calibration is READY if and only if:**
> $$\text{Count}\left( \text{attempt} \in \text{attempt\_history} \mid \text{PracticePosition} > 0 \land \text{Outcome} == \text{AttemptOutcome.Correct} \right) \ge 24$$

- **Positioned Correct Only**: Unpositioned attempts, errors (`Incorrect`), and timeouts (`Timeout`) do not count toward pace calibration.
- **Fixed Durable Criterion**: Calibration readiness is a simple, deterministic count evaluated against durable attempt evidence. No sliding stability trackers or complex heuristics are required.

---

## 12. Critical Hit Learning and Game Authority Boundary

### 12.1 Presentation-Only Consumer Contract
Gamification and combat mechanics (such as the Cyber Defense battle shell) are strictly **downstream presentation consumers** of learning telemetry.

```text
+--------------------------------------------------------------------------+
|                           LEARNING CORE (AUTHORITATIVE)                 |
|  - Arithmetic Fact Selection       - FSRS Retention State & Cards        |
|  - Response Correctness            - PracticePosition Monotonic Advance   |
|  - Monotonic Latency Measurement   - Adaptive Pace Shrinkage Estimator   |
|  - Curriculum Band Advancement     - Dense Mastery Evaluation (>=90%)    |
+--------------------------------------------------------------------------+
                                    |
                                    | Telemetry Published
                                    v
+--------------------------------------------------------------------------+
|                     PRESENTATION / GAME LAYER (DOWNSTREAM)               |
|  - Cyber Defense Encounter State   - Enemy HP / Boss HP                   |
|  - Visual Shields & Radar Arcs     - Critical Hit Calculation (1 vs 2 HP) |
|  - Combat Combos & Effects         - Audio / Haptic Impact Feedback       |
+--------------------------------------------------------------------------+
```

### 12.2 Critical Hit Damage Contract
1. **Uncalibrated Phase ($n < 24$ positioned Correct attempts)**:
   - Every mathematically `Correct` answer deals exactly **1 HP normal damage**.
   - No speed bonuses or Critical Hits are awarded.
   - Prevents rewarding lucky early clicks or penalizing initial motor search latency.
2. **Calibrated Phase ($n \ge 24$ positioned Correct attempts)**:
   - A mathematically `Correct` answer submitted with $\text{ResponseLatencyMs} \le \text{CurrentFactEasyThresholdMs}$ scores a **Critical Hit** dealing **2 HP damage**.
   - A mathematically `Correct` answer submitted with $\text{ResponseLatencyMs} > \text{CurrentFactEasyThresholdMs}$ deals **1 HP normal damage**.
   - Correct-but-slow is celebrated as a mathematical success and deals full base damage.
3. **Non-Correct Outcomes**:
   - `Incorrect` and `Timeout` outcomes deal 0 damage and inflict shield penalties in the combat layer.

### 12.3 Absolute Non-Mutation Invariant
Critical Hit status, combat damage, enemy defeats, sector clears, and shield breaks **MUST NEVER**:
- Alter `AttemptOutcome` (it remains `Correct`, `Incorrect`, or `Timeout`);
- Alter `AttemptRecord.IsFluent` or FSRS ratings (`Again`, `Hard`, `Good`, `Easy`);
- Alter FSRS card stability, difficulty, or scheduled intervals;
- Alter `ItemLearningState` or error counters;
- Alter `OperationProgression`, `BandIndex`, or band advancement gates;
- Alter `PracticePosition` or persistence transactions.

---

## 13. Returning-Learner Behavior

MathFirst respects the learner's time and history across sessions and cold application restarts:

1. **Durable State Preservation**:
   - `OperationProgression` (including `BandIndex` and `BandStartedPracticePosition`) is loaded verbatim from SQLite.
   - FSRS card states and intervals are fully preserved.
   - Lifetime `attempt_history` remains the source of truth for practice counts.
2. **Zero Redundant Acquisition**:
   - Returning learners do not restart introductory bands or repeat mastered elementary facts.
   - Practice resumes immediately at the active frontier and due review queue.
3. **Decoupling Continuity**:
   - Guided Gate G3 soft decoupling is derived directly from persisted `OperationProgression.BandIndex`. If a returning learner previously achieved `BandIndex >= 3` in Multiplication, Multiplication remains decoupled upon restart without needing to re-prove Addition.
4. **Calibration Continuity**:
   - Pace calibration readiness is derived directly from existing durable attempts (`Count(PracticePosition > 0 AND Outcome == Correct) >= 24`). A returning learner who has completed $\ge 24$ correct attempts is immediately calibrated upon restart.
5. **No Artificial Startup Placement Test**:
   - Practice itself acts as the continuous placement test.

---

## 14. Persistence and Schema Impact

### 14.1 Schema V6 Preservation
All approved architecture behaviors are implemented **strictly within Schema V6**:
- **Zero Schema Migration**: No upgrade to Schema V7.
- **Zero New Tables**: No new persistent tables.
- **Zero New Columns**: No new persistent columns on `attempt_history`, `item_learning_state`, `fsrs_card_state`, or `learner_progression`.

### 14.2 Derivation from Existing Durable Evidence
- **Operation-Specific Decoupling**: Derived from `operation_progression.band_index >= 3`.
- **Pace Calibration Readiness**: Derived via query `SELECT COUNT(*) FROM attempt_history WHERE practice_position > 0 AND is_correct = 1`.
- **Latest Frontier Mastery**: Supported by existing partial index `ix_attempt_history_operation_fact_position`.
- **Per-Operation Attempt Counts**: Reconstructed from `SUM(item_learning_state.total_attempts)`.

---

## 15. Determinism and Testability Requirements

1. **Deterministic Selection**: Given an identical database state, prospective `PracticePosition`, and recent history sequence, `AdaptivePracticeSelector` must produce the exact same `ArithmeticFact` and `PracticeSelectionResult`.
2. **Zero Unseeded Randomness**: Candidate ranking, anti-ladder tie-breaking, permutation bag generation, and fallback resolution must not use `System.Random` or unseeded RNG.
3. **Independent Reproducibility**: Unit, integration, and simulation tests must be completely deterministic and runnable in parallel without shared mutable state.

---

## 16. Acceptance Criteria

### Scenario 1: Early Child / Hesitant Learner
- **Profile**: 6-year-old child, hesitant motor input, latency 4000–7000 ms, occasional misclicks.
- **Behavior**:
  - Receives answer-length novelty floors (15–30s) and multi-digit editing allowances.
  - Before 24 correct attempts, Critical Hits are inactive; every correct answer deals 1 HP and validates learning without speed pressure.
  - An occasional mistake on `1 + 2 = 3` triggers local remediation with a 4-turn cooldown; it does not lock down Subtraction or Multiplication.
  - While in Bands 0–2, Multiplication is safely bounded by `AdditionCeiling`, preventing overwhelming quantities.

### Scenario 2: Intermediate Learner with Local Misconception
- **Profile**: 10-year-old learner, generally proficient, struggles specifically with `7 × 8 = 56`.
- **Behavior**:
  - Learner fails `7 × 8` twice consecutively.
  - Second consecutive error triggers existing non-mutating teaching intervention (ADR-0004) displaying `7 × 8 = 56`.
  - Enters Repeated Local Weakness: remediation cooldown tightens from 4 to 2 operation turns.
  - No-immediate-repeat invariant guarantees `7 × 8` is never presented on the turn immediately following an error or intervention.
  - Because only one fact is struggling, broad weakness is not triggered; Addition, Subtraction, and Division continue normal progression.
  - Once answered correctly, `NeedsRemediation` clears, and normal spacing resumes.

### Scenario 3: Strong Fluent Learner
- **Profile**: Fluent learner, 100% correct, rapid automated recall (<1200 ms).
- **Behavior**:
  - Review slots with zero due cards automatically promote to New.
  - Multiplication reaches `BandIndex >= 3` and softly decouples from `AdditionCeiling`, continuing gradual curriculum expansion without stalling.
  - After 24 positioned correct attempts, pace calibration activates, enabling Critical Hits for rapid correct answers.
  - The learner reaches completion of Addition Band 10 (`ADD-D10 -> ADD-P1-ANCHOR`) at or very near the theoretical lower bound of **482 global accepted attempts**.

---

## 17. Regression Risks and Mitigations

| Risk | Description | Mitigation |
|---|---|---|
| **Candidate Starvation** | Strict no-immediate-repeat invariant could cause selector to fail closed if a pool has only 1 fact. | Multi-tier fallback seeks alternative semantic pools (`Useful Frontier`, `Due`, `Early Review`) or alternative enabled operations before failing closed. |
| **Runaway New Discovery** | Over-aggressive promotion to New could overwhelm a struggling learner if error tracking fails. | Multi-fact broad weakness check (multiple simultaneously unresolved remediation facts, tested as active remediation facts $\ge 2$) immediately halts New promotion across all review slots. |
| **Premature Decoupling** | Multiplicative operations decoupling before basic grounding. | Decoupling requires `BandIndex >= 3`, which mathematically requires 100% coverage and $\ge 90\%$ latest correctness across Bands 0, 1, and 2. |
| **Premature Speed Rating** | Granting Critical Hits before pace has stabilized. | Hard requirement of $\ge 24$ positioned Correct attempts, verified to have $< 10\%$ subsequent threshold drift. |

---

## 18. Explicit Deferred Work

The following items are recognized and explicitly deferred from this specification:
1. **Dynamic Operation Turn Stealing**: Bounded permutation scheduling continues to allocate equal nominal turn share to all enabled operations.
2. **Sliding-Window Stability Estimator**: Pace calibration uses the fixed $n \ge 24$ criterion. Dynamic standard-deviation tracking is deferred.
3. **Web Runtime Implementation**: WebAssembly/PWA delivery remains deferred until Native V1 release hardening is completed.
4. **Extended Gamification Metas**: Player levels, XP curves, unlockable cosmetic themes, and multiplayer features remain separate downstream work.
