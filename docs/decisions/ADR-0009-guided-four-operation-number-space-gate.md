# ADR-0009: Guided Four-Operation Number-Space Gate

## Status

Accepted

## Date

2026-09-20

## Context

In MathFirst, elementary arithmetic practice supports independent progression across Addition, Subtraction, Multiplication, and Division ([ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md)). When a beginner learner starts with all four operations enabled (the default onboarding configuration), each operation independently tracks its own progression and role-cycle ordinals ([ADR-0008](ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)).

However, independent introduction created a pedagogical friction point:
- In the default four-operation configuration, Multiplication and Division could introduce or present facts whose products or dividends exceeded the learner's established additive number space.
- For example, while Addition was still in its initial band (e.g. sums up to 2: `0+0`, `0+1`, `1+0`, `1+1`, `0+2`, `2+0`), Multiplication could present facts like `2 × 2 = 4`, or Division could present `4 ÷ 2 = 2` or `6 ÷ 2 = 3`.
- Presenting multiplicative quantities beyond the learner's current addition number space creates cognitive disorientation for elementary learners who rely on additive grounding to understand multiplication as repeated addition and division as sharing/partitioning.
- Conversely, experienced learners or older students often choose specialized subsets (e.g. Multiplication and Division only) to remediate specific operations; such custom selections must not be artificially constrained by unpracticed Addition progress.

This decision defines the architecture implemented in `MF-LEARN-004`: the **Guided Four-Operation Number-Space Gate**, establishing an Addition-governed multiplicative ceiling in Guided Mode, complete preservation of learner progress and persistence schema, multi-layer candidate anti-poisoning, and seamless integration with practice configuration reconciliation. (Implemented by `MF-LEARN-004` and merged to `main` through Pull Request #44 at merge commit `8f4ae59110abf6ea9d365733297a0c15d4c296ea`).

---

## Decision

### 1. Guided Mode vs. Custom Mode

MathFirst formally defines two operational modes based on the learner's configured enabled operations:

1. **Guided Mode**:
   - Active **if and only if** exactly all four operations are enabled:
     $$\text{EnabledOperations} = \{\text{Addition}, \text{Subtraction}, \text{Multiplication}, \text{Division}\}$$
   - Represents the guided, comprehensive curriculum experience.
   - Enforces the cross-operation multiplicative number-space gate governed by Addition.

2. **Custom Mode**:
   - Active whenever any other valid, non-empty subset of operations is enabled (e.g., Multiplication only, Addition + Subtraction, Multiplication + Division).
   - In Custom Mode, cross-operation gating is **inactive** (`GuidedNumberSpaceGate.Unrestricted`).
   - Multiplication and Division progress independently within their own canonical curriculum structures without being constrained by Addition.

---

### 2. Addition Ceiling Authority

In Guided Mode, Addition defines the authoritative number-space ceiling for multiplicative operations:

1. **Definition of Addition Ceiling**:
   The Addition ceiling is the maximum represented number across the complete unlocked canonical Addition curriculum prefix from BandIndex 0 through the learner's current Addition `BandIndex` inclusive:
   $$\text{AdditionCeiling} = \max_{b \in [0, \text{BandIndex}_{\text{ADD}}]} \left( \max_{F \in \text{Frontier}(b)} \left( \max(F.\text{LeftOperand}, F.\text{RightOperand}, F.\text{CorrectResult}) \right) \right)$$

2. **Derivation Characteristics**:
   - Derived deterministically from the canonical, immutable Addition curriculum prefix and the learner's persisted Addition progression (`BandIndex`).
   - Evaluates over the small canonical curriculum prefix without dynamic allocations or runtime caching dependencies.
   - For initial Addition bands (e.g. BandIndex 0, sums $\le 2$), $\text{AdditionCeiling} = 2$. As Addition progression advances through subsequent bands, the ceiling expands monotonically.

---

### 3. Multiplicative Presentation Eligibility Rules

In Guided Mode, the gate restricts presentation eligibility for Multiplication and Division:

1. **Multiplication Presentation Eligibility**:
   A Multiplication fact $F$ is eligible for presentation if and only if:
   $$\text{owner}_{\text{MUL}}(F) \le \text{BandIndex}_{\text{MUL}} \quad \text{AND} \quad F.\text{CorrectResult} \le \text{AdditionCeiling}$$

2. **Division Presentation Eligibility**:
   A Division fact $F$ is eligible for presentation if and only if:
   $$\text{owner}_{\text{DIV}}(F) \le \text{BandIndex}_{\text{DIV}} \quad \text{AND} \quad F.\text{LeftOperand} \le \text{AdditionCeiling}$$
   *(Note: For division $a \div b = c$, the dividend is `LeftOperand`, representing the total quantity partitioned).*

3. **Addition and Subtraction Invariance**:
   Addition and Subtraction are **never** cross-operation gated:
   - Addition operates as the governing anchor.
   - Subtraction is the direct inverse family of Addition within the same elementary number space and remains governed exclusively by its own canonical progression.
   - Gate evaluation for Addition and Subtraction always returns `true`.

---

### 4. Presentation Eligibility vs. Progression Authority

The Guided gate governs **presentation eligibility only**:

1. **No Data Deletion or Demotion**:
   The gate strictly does **not** delete, decrement, or reset:
   - Operation progression `BandIndex` or `BandStartedPracticePosition`;
   - Item learning state (`ItemLearningState.TotalAttempts`, `CorrectAttempts`, `IsProvisionallyMastered`);
   - Attempt history records (`AttemptRecord`);
   - Spaced repetition card state (`FsrsCardState`);
   - Attempt fluency evidence (`is_fluent`);
   - In-session error or remediation tracking;
   - Authoritative per-operation accepted-attempt counts (`AcceptedAttemptCount(O)`);
   - Global `PracticePosition`.

2. **Dormant Fact State (`PERSISTED != CURRENTLY PRESENTABLE`)**:
   Historical or materialized facts exceeding the current Addition ceiling remain preserved losslessly in SQLite as dormant learner evidence.
   - They cannot be selected or presented while gated.
   - They automatically regain presentation and review eligibility without data migration as soon as the Addition ceiling expands or when the learner switches to Custom Mode.

3. **Schema Preservation**:
   No database migration or schema modification is introduced. Persistence remains **Schema V6** ([ADR-0004](ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md), [ADR-0008](ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)).

---

### 5. Multi-Layer Defense-in-Depth and Window Anti-Poisoning

Guided number-space eligibility is enforced across four defensive layers:

1. **Layer 1: SQLite Candidate Streaming and Anti-Poisoning (`SqliteLearnerStore.ReadCandidateRowsAsync`)**:
   - When streaming candidate rows from SQLite, `guidedGate.Allows(fact)` is evaluated *prior* to accumulating candidates into candidate pools.
   - **Anti-Poisoning Invariant**: Gated historical facts do not occupy any of the bounded 64 candidate window slots (`CandidateWindowSize = 64`). This prevents candidate window poisoning and starvation of valid lower-magnitude review facts.

2. **Layer 2: Snapshot Evidence Filtering (`PracticeSelectionEvidence.CreateFromCandidates`)**:
   - In-memory candidate partition into `Due`, `Maintenance`, `Remediation`, and `EarlyReview` pools filters on gate allowance.

3. **Layer 3: Selector Semantic Pools (`AdaptivePracticeSelector`)**:
   - Pure domain filtering verifies `context.GuidedNumberSpaceGate.Allows(fact)` across all semantic candidate pools: `New`, `Useful Frontier`, `Due`, `Maintenance`, `Early Review`, and `Remediation`.

4. **Layer 4: Final Selection Boundary Assertion**:
   - `AdaptivePracticeSelector.SelectNextPracticeFact` verifies that the final chosen fact satisfies `context.GuidedNumberSpaceGate.Allows(fact)` before returning, throwing `InvalidOperationException` if an ineligible fact reaches the boundary.

---

### 6. Evidence Cache Authority and Identity

In `TrainingSession`, cached selection evidence is optimized using semantic gate identity:

1. **Semantic Identity vs. Reference Identity**:
   - Cached evidence identity includes `GateIdentity(bool IsActive, int? AdditionCeiling)`.
   - Gate identity compares by value (active flag and ceiling value), **not** by object reference.
   - For Addition and Subtraction, `GateIdentity` is always `(false, null)` because these operations are invariant under the gate.
   - For Multiplication and Division, `GateIdentity` reflects `(gate.IsActive, gate.AdditionCeiling)`.

2. **Cache Invalidation & Lifecycle**:
   - If the effective Addition ceiling changes (e.g. Addition advances a band) or operations are reconfigured, mismatched cache entries are invalidated and reloaded asynchronously.
   - Selection evidence cache is purely in-memory transient runtime optimization; no gate identity is persisted.

---

### 7. Settings Reconciliation Integration

The Guided gate integrates cleanly with the authoritative Settings/current-fact reconciliation established by [ADR-0008](ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md):

1. **Case C Integration (Gated Active Fact)**:
   - If a Settings change (such as enabling all four operations) makes an unsubmitted active fact Guided-ineligible:
     - The unsubmitted fact is immediately discarded;
     - A valid replacement fact is generated for the **same prospective global `PracticePosition`**;
     - Strictly zero durable or transient learning mutations occur (no attempt record, no timeout, no FSRS mutation, no progression mutation, no count increment).

2. **Case B Preservation**:
   - If the unsubmitted current fact remains eligible, its exact identity, `FactInstanceRevision`, partial answer input, and paused timer state are preserved intact.

3. **Accepted Feedback Deferral**:
   - If an answer has already been accepted (in feedback or intervention modals), reconciliation is deferred until the learner dismisses the feedback.

---

### 8. Scheduler and Role Invariants

MF-LEARN-004 preserves all foundational scheduling and role progress contracts:

1. **Operation Scheduling Unchanged**:
   - `DeterministicOperationScheduler` remains authoritative. Bounded permutation bags schedule operations deterministically; turn allocation among enabled operations is unchanged.
   - With all four operations enabled, Guided Mode governs fact eligibility *within* Multiplication and Division turns, but does not alter the frequency or order of operation turns.

2. **Per-Operation Role Ordinal Authority**:
   - Role progression within each operation remains strictly derived from per-operation accepted attempts:
     $$\text{NextOperationAttemptOrdinal}(O) = \text{AcceptedAttemptCount}(O) + 1$$
   - The 10-slot cycle (1 New, 2 Due, 3 New, 4 Maintenance, 5 Frontier, 6 New, 7 Due, 8 New, 9 Due, 10 Frontier) remains authoritative.
   - Global `PracticePosition` is not the role ordinal. (A minor test-helper defect in `BoundedSelectionIntegrationTests.cs` that previously passed prospective position to role derivation was corrected to pass per-operation count).

---

### 9. Relationship to Prior ADRs

1. **[ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md)**: Independent operation progression, unique acquisition ownership, and open-ended fact space remain authoritative. ADR-0009 refines presentation eligibility in Guided Mode without creating progression lockstep.
2. **[ADR-0005](ADR-0005-acclimation-timing-and-rapid-dense-progression.md)**: Acclimation timing and correctness-driven Dense progression remain authoritative. As previously refined by MF-STAB-002 Slice 3 and ADR-0008, requested-role review authority is preserved, and unseen Dense material does not globally override Due, Maintenance, or Frontier turns.
3. **[ADR-0007](ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md)**: Practice Fact Eligibility Invariant ($\text{owner}_O(F) \le B$) and dormancy semantics are preserved and reinforced by the Guided gate.
4. **[ADR-0008](ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)**: Independent per-operation role ordinals and practice-configuration reconciliation remain authoritative. ADR-0009 utilizes the Case C reconciliation path for gated facts.

---

## Consequences

### Positive
- **Pedagogical Alignment**: Elementary learners are protected from multiplicative facts beyond their demonstrated additive number space during Guided practice.
- **Learner Autonomy**: Custom Mode allows older or focused learners to practice specialized operation subsets without artificial constraints.
- **Zero Data Loss**: Historical progress, FSRS cards, and band indices are completely preserved; dormant facts reactivate seamlessly.
- **Anti-Poisoning Protection**: Candidate window streaming prevents dormant facts from choking the 64-item review window.
- **Zero Schema Churn**: Implemented fully within Schema V6 without migration or storage alterations.

### Neutral
- Multiplicative progression in Guided Mode may temporarily pause introducing higher-magnitude facts until Addition advances sufficiently to raise the ceiling.
- Memory caching of selection evidence incorporates gate state.

### Negative / Limitations
- Global turn allocation remains uniform (25% per operation in 4-operation mode); weak Addition does not receive extra turns automatically.
- Extreme learner failure patterns where an operation cannot advance require separate pedagogical investigation (see below).

---

## Rejected and Deferred Alternatives

1. **Hard Curriculum Progression Lockstep**:
   - *Rejected*: Forcing all operations to advance together would break independent operation progression and penalize fast learners in specific operations.
2. **Dynamic Turn Stealing / Adaptive Operation Frequency**:
   - *Deferred*: Dynamically allocating more practice turns to weaker operations alters the deterministic turn guarantee and requires separate pedagogical simulation and product planning.
3. **Cross-Operation Gating for Subtraction**:
   - *Rejected*: Subtraction is the arithmetic inverse of Addition and is structured within the same foundational number space; cross-gating is redundant and unnecessary.
4. **Restricting Custom Mode**:
   - *Rejected*: When a learner explicitly enables a custom subset (e.g. Multiplication only), they have expressed an explicit intent to focus on those operations.

---

## Future Follow-Up Boundary

During extended diagnostic simulations (e.g. 1000-attempt continuous runs with simulated complete Addition failure), a separate behavioral characteristic was identified: complete failure in an operation can cause practice in that operation to cycle through a very small set of repeated facts due to remediation and frontier bounds.

MF-LEARN-004 intentionally does **not** alter:
- Global operation turn allocation;
- Remediation precedence or spacing;
- Weak-frontier repetition dynamics;
- FSRS rating formulas or scheduler weights.

This finding is recognized as a deferred educational/architectural inquiry that requires a separate `PLAN_ONLY` package and product decision. No speculative implementation package is accepted or active in this lifecycle.
