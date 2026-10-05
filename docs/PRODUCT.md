# MathFirst Product Definition

This document defines the authoritative, implementation-independent product contract for **MathFirst**. It captures confirmed product requirements, the learning model, progression rules, platform expectations, and Minimum Viable Product (MVP) boundaries.

> [!IMPORTANT]
> The independent-operation progression, hybrid curriculum, adaptive learning model, curriculum fact eligibility invariant, independent per-operation role ordinals, Guided number-space gating, adaptive learning policy, tester telemetry export architecture, active thinking time / interruption safety, and Dual-Window structured progression in Sections 4–8 and 10 are the accepted product contract from [ADR-0003](decisions/ADR-0003-independent-operation-progression-and-open-ended-fact-space.md), [ADR-0004](decisions/ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md), [ADR-0005](decisions/ADR-0005-acclimation-timing-and-rapid-dense-progression.md) (`MF-LEARN-003`), [ADR-0007](decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md), [ADR-0008](decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md) (`MF-STAB-003`), [ADR-0009](decisions/ADR-0009-guided-four-operation-number-space-gate.md) (`MF-LEARN-004`), [ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md) (`MF-LEARN-006`), and [ADR-0011](decisions/ADR-0011-tester-telemetry-persistence-export-and-share.md) (`MF-TELEM-001`), extended with practice configuration in `MF-SET-001`, adaptive practice balance in `MF-LEARN-005`, static combat layout in `MF-UX-008`, P0 zero-answer fix, P1 normal practice without deadline failure, P1b Active Thinking Time / Interruption Safety, and P2 Direct-to-Practice Startup / Onboarding Removal. The current native applications implement learner Schema V8 persistence (with presentation context, `is_interrupted`, and pseudonymous installation UUID), complete-history JSON telemetry export `telemetry_export_schema_v2` (16 properties) and native sharing, Dual-Window structured band progression (Window A: 40 math attempts; Window B: 40 timing-eligible attempts), Evidence-Adaptive Discovery, Guided Gate G3 soft decoupling, durable pace calibration readiness at $\ge 24$ timing-eligible positioned Correct attempts, the exact 482 strong-learner benchmark, absolute no-immediate-fact-repetition, tiered remediation, hierarchical adaptive pace, normal practice without deadline failure, active interaction latency measurement, adaptive FSRS ratings and fluency, configurable enabled-subset operation scheduling (Addition-only default for fresh learners), direct-to-practice startup, independent per-operation role ordinals, zero-mutation Settings/current-fact reconciliation, requested-role review authority during Dense acquisition, protected New foundational acquisition, same-operation candidate diversity, correctness-driven Dense progression ($C \cdot 10 \ge N \cdot 9$), Numpad default layout, role-specific selector fallback chains with Early Review liveness, session-local teaching interventions, and periodic check-ins. Web runtime implementation remains deferred.


---

## 1. Purpose and Value Proposition

MathFirst is a mathematics learning application dedicated to helping learners automate fundamental arithmetic facts through repeated, adaptive practice.

- **Primary Goal**: Facilitate the transition from conscious, effortful calculation of elementary arithmetic facts to rapid, reliable, and effortless mental recall.
- **Problem Solved**: Many learners struggle with higher-level mathematical concepts because basic arithmetic facts (e.g. `7 + 8`, `6 × 7`) are not automated, consuming working memory during complex problem-solving.
- **Value Proposition**: A distraction-free, highly responsive, and intelligent practice environment that continuously identifies and targets an individual's specific arithmetic weaknesses until complete fluency is achieved.

---

## 2. Target Users

MathFirst is designed to be **age-neutral**. It serves any learner seeking to build or restore arithmetic fluency:

- **Children**: Elementary learners acquiring fundamental arithmetic operations for the first time.
- **Teenagers**: Students addressing foundational calculation gaps to succeed in secondary mathematics.
- **Adults**: Self-directed learners and adults rebuilding arithmetic confidence or mental agility.

### User Experience Tone
- Clear, focused, and respectful.
- Serious and non-patronizing across all age groups.
- Avoids presentation or gamification that makes the experience unnecessarily juvenile, patronizing, or distracting.
- Designed to treat the learner's time with dignity.

---

## 3. Product Principles

1. **Fluency Over Mere Correctness**: An answer is truly mastered only when it can be recalled reliably and without hesitation.
2. **Adaptive Focus**: Practice must prioritize what the learner finds difficult, slow, or unfamiliar, while allowing mastered facts to gradually recede into maintenance.
3. **Progressive Scaffolding**: The problem space expands incrementally as mastery is proven, avoiding early cognitive overwhelm.
4. **Frictionless Interaction**: Fast, low-latency input tailored to the ergonomics of each supported platform.
5. **Offline Core and Account Independence**: Core learning must function without an active network connection, the MVP does not require a user account, and learning progress must persist locally across sessions.

### Core Learning-First Invariants

The following invariants govern all design, gamification, progression, and timing systems (see [docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md)):

1. **Learning First**: Every user, regardless of age or initial skill, must become better at mental arithmetic through continued use. Cyber Defense and all game systems exist to motivate continued learning. Game mechanics must never reduce learning quality or cognitive clarity.
2. **Gameplay Must Not Control Learning Truth**: Game state has zero authority over curriculum selection, mathematical progression, FSRS spaced repetition, remediation, fact scheduling, correctness evaluation, or durable learning progress. Game systems consume learning events; they never redefine them.
3. **Minimal Friction**: Normal use requires as few non-mathematical interactions as reasonably possible. Mandatory interstitial clicks between arithmetic questions are avoided. Settings, shops, dialogs, animations, and game screens must not contaminate measured arithmetic response time.
4. **No Loss of Learning Progress From Game Failure**: Losing shields, battles, bosses, encounters, streaks, or game resources must never erase or roll back mathematical learning progress.
5. **Skill-Based, Not Age-Based**: Progression adapts strictly to demonstrated mathematical ability and attempt evidence, never gating learning by age, school grade, or arbitrary question counts when learner evidence can determine readiness.

### Native Product Identity

- **Product identity**: MathFirst (`com.tachiguro.mathfirst`) currently presents release version `1.0` and build `1`.
- **Visible version information**: Settings displays localized Version/Build information.
- **Native brand treatment**: A white geometric MF mark uses primary green `#176B4D`, companion green `#0F523A`, light native background `#F4F7F5`, and dark native background `#121916`.
- **Fallback surfaces**: Not Found content is localized in English, German, and Russian; the native startup surface uses language-neutral brand-green handoff (`#176B4D`) before Blazor mounts; fatal static host recovery in `index.html` provides static reload links in English, German, and Russian without depending on runtime localization or Blazor initialization.

---

## 4. Core Learning Model

### Learning Items
- Individual arithmetic expressions/problems represent distinct **learnable items** (e.g. `3 + 4` and `4 + 3`).
- **Canonical Identity**: Facts use stable, presentation-direction-sensitive IDs: `add:{left}+{right}`, `sub:{left}-{right}`, `mul:{left}*{right}`, and `div:{dividend}/{divisor}`. Reversed commutative presentations remain distinct because recall speed and confidence may differ by direction.
- **Lazy Materialization**: A procedurally generated fact remains conceptual until its first accepted attempt. Presentation alone creates no durable learner row. Accepted submission atomically records attempt evidence and creates or updates item, FSRS, and progression state.
- **Unique Acquisition Ownership**: Every exact fact has one acquisition-owner band per operation: the earliest band in canonical curriculum order that generates its `FactId`. Later mathematical-family overlap may inform diagnostics but never grants duplicate acquisition or coverage credit.
- The learning engine maintains state and history per materialized exact fact for granular recall tracking and review.

### Fact-Space Semantics

- **Persisted / Materialized Facts**: Materialized facts with historical attempt, item, or FSRS state remain durable learner evidence. They are never deleted or reset upon curriculum advancement or schema migration.
- **Practice Fact Eligibility Invariant**: For any arithmetic fact $F$ of operation $O$ presented at current operation band $B$, presentation and review eligibility is strictly bounded by canonical acquisition ownership:
  $$\text{owner}_O(F) \le B$$
  A fact is eligible for practice presentation if and only if its canonical acquisition owner band has been reached or completed. Persisted facts owned by future bands ($\text{owner}_O(F) > B$) remain dormant until progression legitimately advances to their owner band.
- **Current Acquisition-Frontier Facts**: Facts whose single acquisition owner is the operation's current band ($\text{owner}_O(F) == B$). Dense bands require exhaustive frontier acquisition; structured bands require a deterministic representative sample of 16 distinct owned-frontier facts.
- **Due Review Facts**: Materialized facts whose FSRS due Practice Position has arrived. Due status optimizes review scheduling among eligible facts but does **not** bypass curriculum ownership: a due fact is presentable only when $\text{owner}_O(F) \le B$.
- **Historical Preservation vs. Presentation Eligibility**: `PERSISTED != CURRENTLY PRESENTABLE` and `DUE != AUTOMATICALLY ELIGIBLE`. Advancing an operation unlocks new facts while preserving prior facts as eligible for review; locked future facts remain dormant.
- **Guided Four-Operation Number-Space Gate ([ADR-0009](decisions/ADR-0009-guided-four-operation-number-space-gate.md))**:
  - **Guided Mode vs. Custom Mode**:
    - **Guided Mode**: Active **if and only if** exactly all four operations are enabled (`Addition`, `Subtraction`, `Multiplication`, `Division`). Enforces cross-operation number-space gating to ensure multiplicative concepts remain grounded in the learner's demonstrated additive number space.
    - **Custom Mode**: Active whenever any other valid non-empty subset is selected (e.g. Multiplication only, Multiplication + Division, Addition + Subtraction). Cross-operation gating is inactive, and operations progress independently without an Addition ceiling.
  - **Addition Ceiling Authority**:
    - The Addition ceiling is the maximum represented number across all facts in the complete unlocked canonical Addition curriculum prefix (from the initial band through the learner's current Addition band):
      $$\text{AdditionCeiling} = \max_{b \in [0, B_{\text{ADD}}]} \left( \max_{F \in \text{Frontier}(b)} \left( \max(F.\text{LeftOperand}, F.\text{RightOperand}, F.\text{CorrectResult}) \right) \right)$$
  - **Multiplicative Presentation Eligibility**:
    - **Multiplication**: A fact $F$ is presentable in Guided Mode iff $\text{owner}_{\text{MUL}}(F) \le B_{\text{MUL}}$ AND $F.\text{CorrectResult} \le \text{AdditionCeiling}$. Facts whose product exceeds the Addition ceiling (e.g. $2 \times 2 = 4$ when the Addition ceiling is 2) cannot be presented until Addition advances.
    - **Division**: A fact $F$ is presentable in Guided Mode iff $\text{owner}_{\text{DIV}}(F) \le B_{\text{DIV}}$ AND $F.\text{LeftOperand} \le \text{AdditionCeiling}$ (since dividend is the total quantity partitioned). Facts whose dividend exceeds the Addition ceiling (e.g. $4 \div 2 = 2$ or $6 \div 2 = 3$ when the Addition ceiling is 2) cannot be presented until Addition advances.
    - **Addition & Subtraction Invariance**: Addition establishes the ceiling and is not cross-operation gated. Subtraction is the inverse family within the same elementary number space and is not cross-operation gated.
  - **Approved G3 Evidence-Based Soft Decoupling ([ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md))**:
    - **Initial Shared Foundation**: Multiplication and Division remain constrained by `AdditionCeiling` while their respective `BandIndex < 3` (introductory Bands 0, 1, and 2).
    - **Operation-Specific Decoupling**:
      - Multiplication softly decouples from `AdditionCeiling` once `Multiplication.OperationProgression.BandIndex >= 3`.
      - Division softly decouples from `AdditionCeiling` once `Division.OperationProgression.BandIndex >= 3`.
    - **Rationale & Gradual Expansion**: Reaching `BandIndex >= 3` proves complete frontier coverage and $\ge 90\%$ latest correctness across Bands 0, 1, and 2, establishing verified mastery across the 0/1/2/3 factor space. Post-decoupling, the operation does not jump to a magic ceiling (such as 100); its own canonical curriculum resumes gradual expansion (`MUL-D04`, `MUL-D05`, etc.). Returning learners derive decoupling purely from durable `OperationProgression`.
  - **State and Progress Preservation**:
    - The gate governs **presentation eligibility only**. It does not delete, demote, or alter band progression indices, item learning states, attempt history, FSRS cards, fluency evidence, remediation tracking, accepted attempt counts, or global Practice Position.
    - Gated facts remain dormant in local storage and automatically regain presentation and review eligibility when the Addition ceiling expands, when soft decoupling is achieved, or when the learner switches to Custom Mode.
  - **Practice Configuration Reconciliation**:
    - When practice configuration changes make an unsubmitted displayed problem Guided-ineligible, it is cleanly replaced at the same prospective practice position with zero learning mutations. Valid problems preserve partial input and timer state; accepted feedback is preserved until deliberate dismissal.


Unseen, non-sampled structured candidates from completed bands are not permanent acquisition debt. Exact-fact FSRS review and operation-level advancement are separate mechanisms governed by the Practice Fact Eligibility Invariant ([ADR-0007](decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md)).

---

## 5. Arithmetic Progression

### Independent Per-Operation Band Progression

All four operations start in their first dense band and advance independently. A stronger operation never waits for a weaker one, and there is no global Mixed Checkpoint or shared "all introductions complete" state. Operations are interleaved deterministically for presentation, but interleaving does not create progression lockstep.

### Hybrid Curriculum

MathFirst uses two acquisition modes:

1. **Dense exhaustive acquisition** requires exposure to every exact fact owned by the band. The dense foundations are:
   - Addition: all ordered `a + b` for `a,b` in `0..10` — 121 facts.
   - Subtraction: the complete non-negative inverse family of that Addition region — 121 facts, including the `SUB-I11` through `SUB-I20` extension after the existing triangular `SUB-D01` through `SUB-D10` bands.
   - Multiplication: all ordered `a * b` for `a,b` in `0..12` — 169 facts.
   - Division: all `(d * q) / d = q` for `d` in `1..12` and `q` in `0..12` — 156 facts.
   - The dense total is 567 exact facts; presentation-direction mirrors remain distinct.
2. **Structured representative acquisition** follows the dense foundation. Bounded deterministic families teach decimal anchors, transfer, rounding, decomposition, two-digit multiplication, decimal shifts, and scaled multiplication, with Subtraction and Division defined through exact inverse families. Each structured band normally introduces exactly 16 distinct owned-frontier facts. Mathematical candidates outside that acquisition sample remain available conceptually without becoming permanent mastery debt.

The detailed canonical band order, pure generation formulas, candidate counts, and ownership counts are normative in [ADR-0003](decisions/ADR-0003-independent-operation-progression-and-open-ended-fact-space.md).

### Advancement Meaning and Gate

Dense advancement means complete coverage of the band's owned frontier and demonstrated correctness across that frontier. Structured advancement means representative fluency sufficient to begin the next arithmetic family; it does not claim exhaustive mastery of every candidate or arbitrary arithmetic at that magnitude.

Advancement evaluates distinct models for Dense versus Structured bands:

#### 1. Dense Band Advancement (Coverage & Correctness)
For all Dense bands (including `MUL-D01`), advancement occurs immediately upon meeting the correctness threshold across the complete owned frontier:
- Evaluated on positioned accepted attempts within the current band instance ($PracticePosition > \text{BandStartedPracticePosition}$);
- Complete frontier coverage: Every fact in the band's owned frontier ($N = |\text{frontier}|$) must have at least one attempt in the current band instance;
- Authoritative latest-outcome correctness: Let $C$ be the number of owned-frontier facts whose latest attempt is `Correct`. Advancement occurs iff:
  $$C \cdot 10 \ge N \cdot 9$$
- Evaluated using exact integer arithmetic (no floating-point rounding);
- Recoverable errors: Earlier mistakes or timeouts are repaired whenever a subsequent attempt for that fact is `Correct`;
- Decoupled from fluency: Advancement does not require raw latency $\le 2000\text{ ms}$, `IsFluent == true`, `Easy`, or `Good` ratings. A slowly calculated `Correct` counts as `Correct` for curriculum expansion;
- Weak-fact continuity: Advancing with $\le 10\%$ weak facts in larger bands ($N \ge 10$) preserves those facts in `ItemLearningState`, FSRS card state, and remediation queues;
- A complete, `Int32`-safe successor band must exist.

#### 2. Structured Band Advancement (Dual-Window Progression)
For Structured bands, the canonical **Dual Window** policy applies:
- Canonical numerical requirements:
  - Total window size: 40 attempts
  - Required correct attempts: 38
  - Required fluent attempts: 34
  - Required frontier attempts: 20
  - Maximum required distinct frontier facts: 16 (or $\min(16, \text{owned-frontier-size})$)
- **Window A (Mathematical Window)**:
  - Evaluates the latest 40 qualifying mathematical attempts in the current band instance ($PracticePosition > \text{BandStartedPracticePosition}$).
  - Interrupted attempts remain in Window A.
  - Used for: sufficient mathematical window ($N \ge 40$), Correct count ($\ge 38$), frontier count ($\ge 20$), distinct frontier coverage ($\ge 16$), and representative sample coverage.
- **Window B (Fluency Window)**:
  - Evaluates the latest 40 **timing-eligible** qualifying attempts in the same band instance (`TimingEvidenceEligible == true`, i.e., `!IsInterrupted`).
  - Requires a full 40 timing-eligible attempts before advancement.
  - At least 34 of those 40 timing-eligible attempts must be fluent (`IsFluent == true`).
- `IsFluent` consumes durable `AttemptRecord.IsFluent`, evaluated adaptively against expected fact pace $P_{\text{fact}}$ at presentation time. Interrupted attempts may have `IsFluent == true` based on active interaction time, but are excluded from Window B timing eligibility. Incorrect and Timeout are non-fluent. There is no automatic band regression. Isolated mistakes age out of the rolling window in Structured bands, while weak facts across all bands remain active through remediation and FSRS.

### Native Strong-Learner Benchmark ([ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md))

The canonical native strong-learner acquisition benchmark is defined as advancing through Addition Dense Band 10 into the first decimal anchor:
$$\text{ADD-D10} \longrightarrow \text{ADD-P1-ANCHOR}$$
which transitions the Guided `AdditionCeiling` from 20 to 180. There is no artificial ~100 Addition milestone.

For the canonical 100%-correct, fully fluent learner fixture practicing in four-operation Guided Mode (zero errors, zero remediation, no broad weakness):
- Mastering the 121 unique Addition facts through ADD-D10 requires 121 successful Addition presentations;
- With deterministic bounded permutation scheduling allocating equal turn share (25% nominal per operation), this yields the exact theoretical lower bound of **482 global accepted attempts** (accounting for deterministic permutation bag alignment where Addition attempt #121 occurs at global position 482);
- **Normative Fixture Benchmark**: The learning engine must realize this milestone at **exactly 482 global accepted attempts** for this canonical fixture, ensuring clean learners encounter required new material without premature review stalls.


### Open-Ended Arithmetic Scope and Terminal Safety

There is no artificial fixed catalog or level-10 ceiling. Bands continue procedurally while the entire next defined band is safe in `Int32`; when it is not, that operation remains in maintenance at its safe terminal band. Generation must use checked arithmetic. Addition requires a checked result, Subtraction remains non-negative, Multiplication requires a checked product, and Division requires a positive divisor and exact non-negative integer result.

The reviewed complete safe final band indices are:
- **Addition**: BandIndex 125 (`ADD-P8-D0`)
- **Subtraction**: BandIndex 135 (`SUB-P8-D0`)
- **Multiplication**: BandIndex 32 (`MUL-P7-SCALED`)
- **Division**: BandIndex 32 (`DIV-P7-SCALED`)

Terminal learner state continues through materialized semantic pools (`Due`, `Stale Maintenance`, `Early Review`, `Remediation`) without unsafe successor-band advancement.

### Initial MVP Arithmetic Boundary
Negative subtraction, division with remainders, decimal-result arithmetic, and division by zero remain outside the current product boundary. `long` and `BigInteger` expansion are deferred.

---

## 6. Adaptive Training Loop & Spaced Repetition (FSRS-6)

For the next accepted global Practice Position `p`, practice schedules the next operation from the currently enabled subset using deterministic bounded permutation bags (`MF-STAB-002`). Each enabled operation independently tracks its own requested role cycle based strictly on its authoritative per-operation accepted-attempt count (`NextOperationAttemptOrdinal(O) = AcceptedAttemptCount(O) + 1` from [ADR-0008](decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)):

$$\text{RoleOrdinal} = \text{NextOperationAttemptOrdinal}(O), \quad i = (\text{RoleOrdinal} - 1) \bmod 10$$

| 1-Based Ordinal (mod 10) | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 (0) |
|---|---|---|---|---|---|---|---|---|---|---|
| **0-Based Remainder $i$** | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
| **Role** | New | Due | New | Maintenance | Frontier | New | Due | New | Due | Frontier |

Partition across 0-based remainder indices $i \in \{0, \dots, 9\}$:
- **`New`**: $i \in \{0, 2, 5, 7\}$ (40% nominal share)
- **`Due`**: $i \in \{1, 6, 8\}$ (30% nominal share)
- **`Maintenance`**: $i \in \{3\}$ (10% nominal share)
- **`Frontier`**: $i \in \{4, 9\}$ (20% nominal share)

**Authority Boundary**: Global `PracticePosition` remains authoritative for total attempt ordering, deterministic permutation bag operation scheduling, FSRS spaced repetition virtual time elapsed, `DuePracticePosition`, `BandStartedPracticePosition`, and persistence validation. Per-operation accepted attempt count is authoritative **strictly** for that operation's role ordinal within the 10-slot cycle. Existing curriculum, progression, coverage, pace, fluency, and item-state authorities remain unchanged.

### Semantic Candidate Pools
The practice selector resolves candidates from five semantic pools:

1. **`New`**: an unmaterialized exact fact whose acquisition owner is the scheduled operation's current band (structured bands use the deterministic 16-fact sample).
2. **`Useful Frontier`**: a materialized exact fact owned by the scheduled operation's current band, prioritized by:
   1. Unmastered first (`IsProvisionallyMastered == false`);
   2. Fewer `TotalAttempts` ascending;
   3. Older `LastReviewPracticePosition` ascending (nulls first);
   4. `FactId` ordinal ascending.
3. **`Due`**: a materialized exact fact for the scheduled operation with a valid FSRS card where $\text{DuePracticePosition} \le \text{ProspectivePosition}$, ordered by `DuePracticePosition`, `LastReviewPracticePosition`, `FactId`.
4. **`Stale Maintenance`**: a materialized exact fact for the scheduled operation that is not in remediation, has a future FSRS due position, and has $\text{ProspectivePosition} \ge \text{LastReviewPracticePosition} + 40$, ordered by `LastReviewPracticePosition`, `DuePracticePosition`, `FactId`.
5. **`Early Review`**: a materialized exact fact for the scheduled operation with a valid FSRS card, not in remediation, and with a future due position ($\text{DuePracticePosition} > \text{ProspectivePosition}$), ordered by `LastReviewPracticePosition` (nulls first), `DuePracticePosition`, `FactId`.

### Selector Precedence and Candidate Resolution
For the scheduled operation, candidate selection follows an authoritative, role-governed precedence:

1. **Remediation Priority and Protected New Acquisition**:
   - Eligible remediation normally overrides requested-role resolution for the scheduled operation when a fact has `NeedsRemediation == true`, `LastReviewPracticePosition != null`, and $\text{ProspectivePosition} \ge \text{LastReviewPracticePosition} + 4$. Remediation operates within the scheduled operation and does not alter operation turn scheduling.
   - **Protected New Opportunity (MF-LEARN-005)**: As an explicit exception, if `requestedRole == PracticeSelectionRole.New` AND the current operation has at least one eligible unmaterialized candidate in its active introduction frontier, remediation does **not** preempt that requested New opportunity. That New opportunity is protected to prevent foundational acquisition starvation.
   - When the eligible New pool is exhausted (no eligible unmaterialized candidate remains in the active introduction frontier), eligible remediation may again preempt requested New.
   - For non-New roles (`Due`, `Maintenance`, `Frontier`), eligible remediation retains priority over normal role resolution.
2. **Authoritative Requested-Role Determination**: When remediation does not apply (or when a requested New turn is protected), the requested role is derived strictly from the operation's authoritative accepted attempt count ($\text{NextOperationAttemptOrdinal}(O) = \text{AcceptedAttemptCount}(O) + 1$) mapped across the 10-slot cycle (New at slots 1, 3, 6, 8; Due at 2, 7, 9; Maintenance at 4; Frontier at 5, 10).
3. **Requested-Role Review Authority Preserved**: Unseen Dense material does **not** globally or unconditionally override non-New roles (`Due`, `Maintenance`, `Frontier`). Dense New introductions occur during designated `Requested New` opportunities (4 slots per 10-attempt period), giving learners balanced review and retention opportunities alongside new curriculum acquisition.
4. **Role-Specific Fallback Chains**: The scheduled requested role resolves through its dedicated fallback chain across semantic pools, with all candidate pools filtered by the Practice Fact Eligibility Invariant ([ADR-0007](decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md)) and, in Guided Mode, the Guided Number-Space Gate ([ADR-0009](decisions/ADR-0009-guided-four-operation-number-space-gate.md)):
   ```text
   New role:         New -> Useful Frontier -> Due -> Stale Maintenance -> Early Review
   Due role:         Due -> Useful Frontier -> Stale Maintenance -> Early Review
   Maintenance role: Stale Maintenance -> Useful Frontier -> Due -> Early Review
   Frontier role:    Useful Frontier -> Due -> Stale Maintenance -> Early Review
   ```

### Authoritative Materialization Invariant
Unmaterialized facts may enter practice **strictly** through explicit `Requested New` turns when unmaterialized candidates exist and are presentation-eligible. Non-New resolved roles (`Due`, `Maintenance`, `Frontier`, `Early Review`, `Remediation`) strictly cannot materialize unmaterialized facts. Earlier-owned review facts receive no acquisition or current-band coverage credit.

`Early Review` serves as the essential liveness bridge: when an operation has exhausted its frontier and has only future-due reviews, Early Review selects the oldest-reviewed future fact to keep PracticePosition advancing deterministically without stalling or entering an artificial Caught Up state.

Within a non-empty semantic pool, normal cooldowns apply first. If every candidate is excluded:
1. The commutative-mirror cooldown relaxes within that semantic pool;
2. The exact-fact cooldown relaxes within that semantic pool;
3. Selection proceeds deterministically from that pool.

Cooldown relaxation occurs strictly inside the already selected semantic pool and never changes fallback-pool priority. An empty candidate state under supported transitions is an integrity failure and fails closed.

### Repetition Density & Diversity Constraints
- **Exact Fact Cooldown**: The selector avoids repeating the same `FactId` within the last 3 presented facts when alternative candidates exist (`ExactFactCooldownDistance = 3`).
- **Same-Operation Diversity (MF-LEARN-005)**: Inside the selected semantic pool, strict candidate selection avoids immediately repeating the previous same-operation `FactId` when another viable `FactId` exists. This does not change the selected operation, change requested role, switch semantic pools, alter global cooldown constants, or persist new state. Existing relaxation still permits repetition when necessary for terminal liveness.
- **Commutative Mirror Cooldown**: For Addition and Multiplication, adjacent and near-adjacent mirror pairs (e.g., `6 × 0` and `0 × 6`, `3 + 4` and `4 + 3`) are avoided within 3 positions (`MirrorFactCooldownDistance = 3`), while maintaining distinct item entities and separate FSRS states. Non-commutative Subtraction and Division are strictly exempt.
- **Operation Streak Diversity**: Limits consecutive questions of the same arithmetic operation to a maximum of 2 when alternative candidates exist (`MaxPreferredOperationStreak = 2`).

### Approved Evidence-Adaptive Discovery Policy ([ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md))
- **Dynamic Role Promotion**: While the 10-slot cycle provides initial baseline scaffolding, review and consolidation slots (`Due`, `Maintenance`, `Frontier`) are dynamically promoted to `PracticeSelectionRole.New` whenever:
  1. The scheduled review pool contains **zero acquisition-blocking work** (no facts with non-Correct latest outcome or active remediation; clean Correct facts that are FSRS-due, unmastered by repeat-count criteria, or stale do not block New discovery while unmaterialized band material remains);
  2. The current band contains eligible unmaterialized facts;
  3. The learner exhibits **Clean Evidence** (absence of Broad Weakness, $\ge 2$ active unresolved remediation facts).
- **Emergent Discovery Velocity**: Discovery pressure is not governed by a hardcoded ratio (such as 90%). For a rapid, fluent learner, review slots naturally promote to New, driving acquisition velocity to the normative 482-attempt benchmark for clean acquisition. For a learner experiencing errors, review slots remain dedicated to consolidation, naturally reducing New discovery until misconceptions are repaired.

### Approved Absolute No-Immediate-Fact-Repetition Invariant ([ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md))
- **Universal Hard Invariant**:
  $$\text{FactId}(t+1) \ne \text{FactId}(t)$$
  After an accepted presentation of `FactId X`, the immediately following presented fact **MUST NOT** be `FactId X`, regardless of outcome (`Correct`, `Incorrect`, `Timeout`, slow correct, or remediation).
- **Priority Over Aesthetics**: Exact duplicate prevention strictly outranks anti-ladder aesthetics. If avoiding an immediate repeat forces a ladder neighbor, the ladder candidate must be selected rather than repeating the exact same fact.
- **Single-Candidate Resolution**: If the requested pool contains only the immediately preceding fact, the selector must seek an alternative candidate from other semantic pools or alternate operations rather than relaxing into an exact duplicate.

### Approved Tiered Weakness & Remediation Model ([ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md))
- **Local Weakness (Isolated Error)**: An isolated error enters remediation with ordinary spacing (tested Candidate-C cooldown: $\ge 4$ operation attempts). It remains strictly local and does not suppress new introductions in unrelated operations.
- **Repeated Local Weakness**: Repeated local weakness on the same fact activates tightened remediation spacing (tested Candidate-C cooldown: $\ge 2$ operation attempts) to reinforce the fact before memory traces decay. Compatibility with existing presentation-layer teaching intervention mechanisms is preserved where applicable, but teaching interventions are not a newly mandated learning-engine requirement of ADR-0010.
- **Broad Weakness**: Multiple simultaneously unresolved remediation facts within the currently relevant learning context (tested as active unresolved remediation facts $\ge 2$) indicate broad weakness and suppress aggressive New introduction while meaningful remediation and reinforcement work exists.
- **Mastery Preservation**: Dense band progression remains strictly bound by 100% frontier coverage and $\ge 90\%$ latest correctness ($C \cdot 10 \ge N \cdot 9$).


### Task Distance Virtual Time Model
MathFirst schedules arithmetic reviews not by real-world calendar days, but by **Practice Position** (the monotonic count of accepted arithmetic attempts). One practice position corresponds to one virtual day from epoch `2000-01-01T00:00:00Z`, making review intervals independent of wall-clock manipulation, timezone shifts, or gaps between study days.

Exact-fact review preserves `FSRS.Core` 1.0.7, 95% desired retention, 21 default parameters, disabled fuzzing, and deterministic per-`FactId` cards. Responses are rated (`Again`, `Hard`, `Good`, `Easy`) via adaptive pace mapping without manual self-rating buttons. Operation advancement consumes durable attempt correctness and fluency and does not depend directly on FSRS stability, difficulty, or interval values. Advancement never deletes or suspends a card.

---

## 7. Response Evaluation, Adaptive Pace, and Fluency

True arithmetic fluency requires evaluating both correctness and speed against adaptive timing boundaries:

1. **Correctness**: Whether the submitted numeric answer is mathematically correct.
2. **Active Interaction Latency**:
   - `ResponseLatencyMs` represents the accumulated **active interaction time** between item readiness (`ITEM_READY`) and answer submission.
   - Active timing is segmented across lifecycle interruptions: manual Pause, Settings navigation, app backgrounding, and practice-surface deactivation pause active timing. Inactive duration is excluded from measured response latency.
   - An uninterrupted learner who thinks slowly remains genuinely slow. There is no naive global wall-clock cutoff.
3. **Interruption Fact & Timing Evidence Eligibility (P1b)**:
   - `AttemptRecord.IsInterrupted` is a durable empirical boolean fact persisted in SQLite Schema V8 (`attempt_history.is_interrupted`).
   - Recorded as `true` when at least one genuine lifecycle interruption occurred after active timing began and before submission. Resets cleanly for each new fact.
   - Interrupted Correct remains `Correct`; interrupted Incorrect remains `Incorrect`. Interruption never creates a `Timeout` outcome.
   - `TimingEvidenceEligible` is derived at runtime as `!IsInterrupted` (not separately persisted or exported).
   - `IsFluent` represents active interaction latency classification against the adaptive fluency threshold; `IsFluent == true && IsInterrupted == true` is valid.
4. **Item Learning State Mutations**:
   - **Interrupted Correct**: Updates mathematical state normally (`TotalAttempts` increments, `CorrectAttempts` increments, `ConsecutiveCorrectStreak` increments, active remediation is cleared); timing/fluency state is preserved neutral (`FluentStreak`, `IsProvisionallyMastered`, `LastLatencyMs`, and `RollingLatencyMs` remain unchanged).
   - **Interrupted Incorrect**: Applies standard mathematical error updates (`TotalAttempts` increments, `IncorrectAttempts` increments, `ConsecutiveCorrectStreak` resets, `FluentStreak` resets, provisional mastery is revoked, remediation becomes due); contaminated latency is excluded from `LastLatencyMs` and `RollingLatencyMs`.
5. **Hierarchical Adaptive Pace Runtime**:
   Expected response latency ($P_{\text{fact}}$) is computed dynamically from timing-eligible, positioned, accepted, mathematically correct attempt evidence ($PracticePosition > 0 \land \text{TimingEvidenceEligible} == \text{true}$). Latency samples are clamped to $[600\text{ ms}, 12000\text{ ms}]$.

   Interrupted Correct attempts are excluded from latency shrinkage across all four hierarchical layers:
   - **Static Prior Baseline ($P_0$)**: $4500\text{ ms}$
   - **Learner Pace ($P_{\text{learner}}$)**: $\text{Shrink}(P_0, 12, \text{latest 30 timing-eligible learner Correct latencies})$
   - **Operation Pace ($P_{\text{operation}}$)**: $\text{Shrink}(P_{\text{learner}}, 8, \text{latest 20 timing-eligible operation Correct latencies})$
   - **Band Pace ($P_{\text{band}}$)**: $\text{Shrink}(P_{\text{operation}}, 6, \text{latest 15 timing-eligible band-frontier Correct latencies})$
   - **Exact Fact Pace ($P_{\text{fact}}$)**: $\text{Shrink}(P_{\text{band}}, 4, \text{latest 5 timing-eligible exact-fact Correct latencies})$

   $$\text{Shrink}(P_{\text{parent}}, W, \text{samples}) = \begin{cases} P_{\text{parent}} & \text{if samples is empty} \\ \left\lfloor \frac{W \cdot P_{\text{parent}} + |\text{samples}| \cdot \text{Median}(\text{samples}) + \frac{W + |\text{samples}|}{2}}{W + |\text{samples}|} \right\rfloor & \text{otherwise} \end{cases}$$

   Interrupted Incorrect and historical Timeout attempts continue to contribute their existing outcome/instability evidence to fact instability allowances (+1000 ms per Incorrect, +1500 ms per Timeout, capped at 3000 ms).
6. **Normal Practice Without Deadline Failure (P1)**:
   - Normal MathFirst practice never terminates a question or forces an arithmetic failure due to elapsed wall-clock time.
   - A learner who takes extended active time and computes the correct answer receives a mathematically `Correct` outcome.
   - Configurable practice-time limits and forced deadline timeouts are removed from normal practice.
   - Historical / explicit `Timeout` outcomes from legacy sessions remain supported for backward compatibility and test verification.
7. **Adaptive FSRS Rating & Fluency Classification**:
   - Thresholds derive from expected pace $P_{\text{fact}}$:
     $$\text{EasyThresholdMs} = \text{clamp}\left(\left\lfloor \frac{85 \cdot P_{\text{fact}} + 50}{100} \right\rfloor, 600, 2000\right)$$
     $$\text{FluencyThresholdMs} = \text{clamp}\left(\left\lfloor \frac{125 \cdot P_{\text{fact}} + 50}{100} \right\rfloor, 1500, 4000\right)$$
   - **Classification Rules**:
     - `Incorrect` $\implies$ Rating `Again`, `IsFluent = false`
     - `Timeout` (historical/explicit) $\implies$ Rating `Again`, `IsFluent = false`
     - `Correct` $\le \text{EasyThresholdMs} \implies$ Rating `Easy`, `IsFluent = true`
     - `Correct` in $(\text{EasyThresholdMs}, \text{FluencyThresholdMs}] \implies$ Rating `Good`, `IsFluent = true`
     - `Correct` $> \text{FluencyThresholdMs} \implies$ Rating `Hard`, `IsFluent = false`
   - `IsFluent` is persisted on `AttemptRecord.IsFluent` and `attempt_history.is_fluent` in Schema V8.
   - FSRS-6 rating and scheduling mechanics operate identically for uninterrupted and interrupted attempts based on active latency classification.

### Behavioral Requirement
- The system distinguishes between:
  - **Automated Recall**: Fast, accurate responses at or below the adaptive fluency threshold (`IsFluent = true`).
  - **Conscious Calculation**: Correct responses requiring extended time beyond the adaptive fluency threshold (`IsFluent = false`).
  - **Incorrect Answer**: Submitted wrong numeric integer (`Outcome = Incorrect`, `IsFluent = false`).
  - **Historical Timeout**: Expired deadline without valid submission in legacy/explicit modes (`Outcome = Timeout`, `IsFluent = false`).
- Slowly calculated correct answers remain active in practice until retrieval is fluid.

### Pace Calibration Readiness ([ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md))

Pace calibration is declared **READY** once a learner accumulates:
$$\text{Count}\left( \text{attempt} \in \text{attempt\_history} \mid \text{PracticePosition} > 0 \land \text{Outcome} == \text{AttemptOutcome.Correct} \land \text{TimingEvidenceEligible} == \text{true} \right) \ge 24$$

- **Threshold**: Remains exactly 24 timing-eligible positioned Correct attempts (`PaceCalibrationCorrectAttemptThreshold == 24`).
- **Runtime Property**: Evaluated via `PositionedCorrectAttemptCount`, which represents timing-eligible positioned Correct attempts.
- **Returning Learners**: Returning learners with $\ge 24$ durable timing-eligible positioned Correct attempts are immediately recognized as calibrated upon cold launch.

### Downstream Gamification & Critical Hit Contract ([ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md))

Combat and gamification mechanics (such as Cyber Defense) are strictly **downstream presentation consumers** of learning telemetry:
- **Uncalibrated ($n < 24$ timing-eligible positioned Correct attempts)**: Every mathematically `Correct` answer deals **1 HP normal damage**. No speed bonuses or Critical Hits are awarded.
- **Calibrated ($n \ge 24$ timing-eligible positioned Correct attempts)**: A `Correct` answer submitted with $\text{ResponseLatencyMs} \le \text{CurrentFactEasyThresholdMs}$ scores a **Critical Hit** dealing **2 HP damage**. A `Correct` answer with $\text{ResponseLatencyMs} > \text{CurrentFactEasyThresholdMs}$ deals **1 HP normal damage**.
- **Correct-But-Slow**: Correct answers submitted past the easy threshold remain mathematically correct and are celebrated with standard 1 HP damage.
- **Absolute Non-Mutation Boundary**: Critical Hit outcomes, combat damage, enemy HP, combos, or defeats **must never** alter `AttemptOutcome`, response latency, FSRS ratings (`Again`, `Hard`, `Good`, `Easy`), card stability, `ItemLearningState`, `OperationProgression`, `PracticePosition`, or band advancement.



---

## 8. Session Behavior, Check-Ins, and Error Interventions

### Queue Composition
Training sessions are automatically generated by blending items across learning categories (new items, weak items, due reviews, and items requiring remediation) under deterministic operation and role scheduling.

### Progress HUD and Practice Cleanliness
- The compact progression HUD presents only the operations currently enabled in Settings, in Addition, Subtraction, Multiplication, Division order with `+`, `−`, `×`, and `÷`. One enabled operation produces one indicator, two produce two, three produce three, and all four produce four. Disabled operations disappear from the HUD without losing progress.
- Each numeric value is `BandIndex + 1`: an independent progression stage, not a maximum operand, mastery percentage, global arithmetic level, or session score. Symbols are visual, while accessible labels identify the operation and progression stage. Internal curriculum band IDs are not learner-facing.
- **Distraction-Free Practice Header & Restrained Streak Feedback**: The permanent score HUD display has been removed from normal active practice. A restrained consecutive correct streak badge (e.g. `Streak: 3` / `Serie: 3`) appears in the metadata header during active practice only when the learner achieves $\ge 3$ consecutive correct answers in the current session. It disappears immediately upon an incorrect answer or timeout, with zero celebratory fanfare, XP, or gamification.
- Home practice owns or reuses one stable `ArithmeticCurriculum` for the component lifetime. HUD diagnostics are cached by authoritative learner-state generation and Practice Position, so timer-only approximately 50 ms presentation refreshes do not reconstruct the curriculum or regenerate the full diagnostic snapshot.

### Repeated-Error Teaching Intervention
When a learner struggles repeatedly with a specific fact in the active session:
1. **Trigger**: After the **second consecutive persisted error** (`Incorrect` or `Timeout`) for the exact same `FactId`, a non-scored teaching intervention overlay is presented.
2. **Instructional Display**: A blocking modal displays the canonical equation and correct result (e.g. `7 × 8 = 56`) with concise instruction to notice the equation and result before continuing.
3. **Strict Non-Mutation**: Acknowledging the teaching overlay creates **zero learning mutations**: no attempt record, no PracticePosition increment, no score mutation, no item state mutation, no FSRS card mutation, and no store revision.
4. **Lifecycle**: The session error counter for that FactId resets to zero upon trigger (continuing mistakes trigger in pairs: 2nd, 4th, etc.), and a Correct answer resets it to zero. Error counters are session-local and clear on cold restart or reset.

### Session Check-Ins, Break Flow & Pause Summary
To provide encouraging feedback without breaking active focus:
1. **Cadence**: Triggered every 20 accepted attempts in the active training session ($20, 40, 60, \dots$). Cadence is session-local only.
2. **Summary**: Displays correct count out of 20, completed attempts, stage progressions achieved, and the deterministic median response latency of **Correct attempts only** (Incorrect and Timeout latencies are excluded; if 0 correct, median is unavailable).
3. **Manual Pause Session Summary**: The Manual Pause dialog displays lightweight current-session statistics: Completed attempts, Correct attempts, Current correct streak, and Median correct response time (or `—` if no correct answers).
4. **Actions**:
   - **`Keep Going`**: Prepares the next deterministic fact and resumes active practice with its full adaptive deadline starting from zero.
   - **`Take a Break`**: Transitions the practice gate to `ManualPause` **before** next-fact preparation, guaranteeing that accumulated active elapsed time remains exactly zero while paused. The prepared fact receives its full adaptive deadline upon explicit learner Resume.

### Explicit Error Feedback and In-Session Remediation
When a learner provides an incorrect answer or times out:
1. **Explicit Error Feedback**: Prominently shows the arithmetic expression, highlights the mistake, shows the submitted answer (for incorrect attempts) or a time-expired notice (for timeouts), and displays the correct result.
2. **Deliberate Acknowledgement**: Requires deliberate acknowledgement (via Enter key or Continue action) before advancing.
3. **Same-Session Remediation**: The missed fact is scheduled for recurrence via the selector's remediation priority override ($\ge 4$ positions later) within the same session.

---

## 9. Input and Interaction Requirements

Input ergonomics are critical to measuring true arithmetic recall rather than motor typing friction:

- **Shared Numeric Answer Contract**:
  - The answer editor accepts only canonical unsigned decimal notation with ASCII digits and at most one decimal separator. The integer part is either exactly `0` or begins with `1` through `9`; redundant leading-zero forms such as `00`, `01`, `0004`, and `00.5` are rejected. Leading decimal forms such as `.5` and `,5` remain valid.
  - Both period and comma are accepted in every UI language; signs, whitespace, exponent notation, alphabetic characters, and multiple/mixed separators are rejected.
  - Empty input plus trailing-separator states such as `12.` and `12,` remain valid while editing. Submission normalizes comma or period to an invariant exact `decimal` value; equivalent representations such as `14`, `14.0`, and `14,00` compare numerically.
  - Complete integer answers auto-submit deterministically based on answer completeness:
    - Single-digit expected answers auto-submit immediately upon entering the first digit (evaluating as Correct or Incorrect).
    - Multi-digit answers remain editable until the expected answer digit count is reached, allowing the learner to correct a mistyped partial answer with Backspace/Delete before submission.
    - Automatic submission occurs once the entered buffer reaches the expected canonical digit count (evaluated as Correct or Incorrect) or upon exact numeric equality. Prefix mismatch alone does not trigger early submission.
    - If the active answer deadline expires while a partial multi-digit input is present, the attempt is recorded as a Timeout rather than an incorrect submission.
    - Enter remains an explicit force-submit path for any valid complete numeric value.
    - Every newly generated problem starts with an empty answer input buffer (managed via process-local fact instance revision tracking and DOM element keying, ensuring that new exercises always begin with an empty buffer while the active exercise retains partial input across transient UI interactions).
  - Supported browser/WebView input paths synchronously reject invalid prospective keyboard, selection-replacement, deletion, and paste edits before the DOM mutates. The C# numeric policy remains authoritative for submission, the on-screen keypad, fallback input handling, and tests.
  - Rejected or incomplete input creates no semantic attempt and therefore cannot affect score, Practice Position, FSRS, exposure, remediation, or progression evidence. Input is bounded to 28 characters to protect layout while leaving ample future arithmetic range.
  - The responsive answer field comfortably exposes approximately eight digits plus a decimal separator at normal Windows desktop sizes. It remains centered, never exceeds the card width, and wraps below the arithmetic expression on narrow layouts without reducing arithmetic typography.
- **Android**:
  - Touch-first user interface.
  - The shared MathFirst custom keypad is the primary answer-entry surface. The focused answer field remains compatible with external keyboards while requesting native soft-keyboard suppression through the Android-specific `inputmode="none"` and manual virtual-keyboard policy.
  - The custom keypad uses the PC Numpad order by default (`7 8 9` at the top); learners can select the Phone order (`1 2 3` at the top). Both layouts use the shared `NumericAnswerInputPolicy` and the locale-familiar decimal glyph.
  - Practice and Settings respect Android system-bar and display-cutout safe areas (historical onboarding removed in P2). On phone-like portrait viewports, Appearance and keypad-selection choices stack vertically at full available card width. On sufficiently wide landscape viewports those controls use clean horizontal columns without forcing overflow.
  - Short phone landscape Practice uses a dedicated two-column composition: the full-width compact metadata/timer region stays above an interaction column containing equation and answer, while the custom keypad occupies the wider right column. Blocking Ready/Pause/error dialogs may span the Practice body. Geometry-based orientation, width, and dynamic-height queries preserve normal desktop layouts. Natural vertical scrolling remains the fallback only for unusually short landscape viewports.
  - Real-device acceptance must verify native IME suppression, touch input, external keyboard input where exposed by Android, portrait choice stacking, full short-landscape keypad visibility, rotation continuity, gesture/navigation-bar clearance, auto-submit, blocking feedback, Ready/Pause/Resume gates, lifecycle timing, and persistence across restart.
- **Windows & Web**:
  - Effective physical keyboard support.
  - Physical numeric keypad (numpad) support where available.
- **Shared On-Screen Keypad & First-Launch Experience**:
  - During ordinary answer entry Practice renders a centered, responsive, clickable/touchable keypad with no permanent Submit, Confirm, or Continue action. Physical digits, Backspace, decimal comma/period, and Enter remain supported through the same controlled answer model.
  - Learners choose exactly one persistent UI layout: `Numpad` (default; `7 8 9` at the top) or `Phone` (`1 2 3` at the top). The locale-familiar decimal glyph is shown, while both separators remain valid input.
  - First-time learners start directly in active Practice on `/` with Addition enabled by default, Numpad layout, System theme/language, and haptics enabled. The initial multi-step onboarding wizard has been removed (P2). Practice preferences can be customized at any time in Settings. Restore Default Settings and Full Local Reset restore defaults (including Addition-only operation default); Reset Learning Progress clears learner attempt history and mastery while preserving user preferences.
- **Practice Flow, Readiness Gates, and Returning Learner Feedback**:
  - After a correct answer is accepted and persisted exactly once, Practice immediately prepares the next fact without visual delay or acknowledgement. Incorrect answers and timeouts pause timing and show a blocking dialog containing the original arithmetic expression, the submitted answer when applicable, the correct answer, and a Continue action that is also activated by Enter.
  - A true save failure reports “Progress could not be saved.” A failure that occurs only while loading the next exercise after the answer was already saved reports “Next exercise could not be loaded.” Retry never saves the same completed attempt twice. Equivalent localized wording is provided in German and Russian.
  - A cold application session starts directly in active Practice on `/` for fresh learners (without completed practice history), with timing activating only after practice surface activation. Returning learners with accepted practice history start behind an opaque Ready to practice dialog presenting a progress overview. No problem, keypad, semantic timing, attempt, score, or Practice Position change is exposed before Start / Resume.
  - Manual Pause is available beside Settings only during active answer entry. It uses an accessible CSS-drawn two-bar icon with danger action treatment (red in the current theme, `button-danger`), freezes monotonic semantic time, preserves the current fact and input, and conditionally removes the problem and keypad from rendering and accessibility until Resume practice. Start and Resume remain normal primary (green) actions whenever the Pause action is absent.
  - Opening Settings freezes active timing and pauses the current question. Modifying practice operations or settings in Settings executes authoritative reconciliation ([ADR-0008](decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)):
    - If the unsubmitted current fact remains enabled and presentation-eligible, it is retained with its exact identity, `FactInstanceRevision`, partial answer input, and remaining timer deadline preserved;
    - If the unsubmitted current fact becomes invalid because its operation was disabled or it is no longer presentation-eligible, it is immediately discarded and replaced with a valid fact from the updated subset for the same prospective global `PracticePosition`;
    - Discarding an invalid unsubmitted fact creates zero durable or transient learning mutations (no attempt, no timeout, no score mutation, no FSRS mutation, no progression mutation, and no attempt-count increment);
    - If an answer has already been accepted (in `CorrectFeedback`, `IncorrectFeedback`, `TimeoutFeedback`, `TeachingIntervention`, `SessionCheckIn`, or another post-accepted state), the accepted learner state is preserved intact and configuration changes apply to the preparation of the next exercise.
  - Leaving the application foreground converts a running awaiting-answer item to a Background Resume gate. Foreground return does not restart timing; explicit Resume is required. These transient gates require no learner SQLite schema change.

### Contextual Practice-Gate Personality

The practice gate uses deterministic localized contextual copy instead of a static title. Its contexts are initial readiness, return after a short absence, return after a long absence, manual-pause resume, background resume, and neutral Ready/Paused fallbacks. A prior accepted practice is Recent when it is absent or less than 30 minutes old, a Short Absence from 30 minutes to less than 3 days, and a Long Absence at 3 days or more; future-clock skew is safely Recent.

The selected copy remains stable for one genuine gate activation: ordinary rerenders, Home/Settings navigation, route recreation, and language changes do not rotate it. Language changes retain the same language-independent message identity and re-localize its text. A later genuine gate activation may choose another variant, while reset or authoritative learner-state replacement invalidates stale presentation identity. Background-resume copy applies both immediately and after a pending persistence, advance, or recovery operation reaches its final gate state.

The physical contextual corpus supports English, German, and Russian with 55 message IDs per locale (165 localized strings total). It has ID and placeholder parity across locales, unique visible text within each locale and trigger pool, locale normalization under `LanguagePreferencePolicy`, and static Ready/Paused localization as the ultimate fallback. It is local-only: there is no runtime AI, remote copy service, network dependency, or telemetry dependency.

Tone is concise, respectful, age-neutral, and secondary to arithmetic interaction. Neutral, welcoming, semantically supported progress-aware, dry-humorous, and occasional lightly cheeky wording is allowed. The product avoids insults, humiliation, guilt, patronizing or manipulative language, exaggerated praise, false achievement claims, and assumptions about a learner’s personal circumstances. Practice readiness copy remains history-aware and respectful across fresh and returning practice sessions.

Contextual copy is presentation behavior only. It does not change FactId, curriculum generation, Practice Position, BandIndex progression, advancement gates, evidence windows, FSRS, remediation, cooldowns, operation scheduling, answer deadlines, answer evaluation, accepted-attempt semantics, or Schema V6.
- **Settings Actions and Reset Confirmations**:
  - *Practice Operations*: Settings always shows controls for Addition, Subtraction, Multiplication, and Division. Addition is enabled by default for fresh learners; users may choose any non-empty subset, and the last enabled operation cannot be disabled. Disabled operations are excluded from newly generated practice and hidden from the HUD while retaining all learning progress for later resumption.
  - *Practice Time*: Standard uses the adaptive deadline unchanged. The 30 s, 45 s, and 60 s options set a minimum answer-time floor. The No Time Pressure option removes deadlines and automatic timeouts while continuing to measure active latency and display live elapsed time. None of these options change response-speed evaluation thresholds or FSRS rating rules.
  - *Statistics / Diagnostics*: The developer-facing Statistics / Diagnostics section is no longer shown in Settings. This UI removal does not delete attempt history, FSRS scheduling data, operation progress, item learning state, or progression calculations.
  - *Privacy Policy*: Settings provides an entry card and navigation action to the offline in-app `/privacy` surface. Privacy information is readable entirely offline without requiring network connectivity, user accounts, or external browser integration. System Back navigation returns to Settings.
  - *Restore Default Settings (Reset UI Preferences)*: Restores UI and practice preferences to defaults (Addition only, Standard practice time, Numpad keypad layout, System theme, System language, haptics enabled), preserves learner progress/history, and navigates to `/` without onboarding.
  - *Reset Learning Progress*: Resets learner attempts, item states, FSRS states, and progression while preserving UI preferences; no onboarding state exists.
  - *Full Local Reset*: Clears all learner progress, restores all preferences to defaults (including Addition-only operation default), purges telemetry share cache, clears the persistent installation ID, and navigates to `/` into direct Addition practice without onboarding.
  - All three reset actions remain explicit two-step confirmations. After an inline confirmation is rendered, it receives programmatic focus and is scrolled into view with nearest-block behavior; reduced-motion preferences disable smooth scrolling.

### Active Combat Positional Stability (MF-UX-008)

During active gameplay in gamified modes (such as Cyber Defense), the interactive mathematical canvas must maintain strict positional stability:
- **Positional Stability Invariant**: The on-screen numeric keypad coordinates, arithmetic problem typography, and answer input area must never shift, bounce, or resize in response to enemy spawn, normal enemy replacement, boss appearance, HP/shield mutations, combat floating feedback overlays (Hit, Crit, Blocked), or dynamic status copy.
- **Layout-Isolated Boss Presentation**: Boss enemies must appear visually commanding, larger, and more threatening than standard enemies, but their visual scale must remain completely decoupled from layout geometry (`visual scale != layout scale`). Boss presentation uses compositor-driven CSS transforms (`transform: scale(...)`) on isolated artwork layers within fixed-dimension bounding boxes. Boss presence must never expand the surrounding DOM containers, stage wrappers, or combat layout boxes.
- **Scoped Scroll Boundary**: The active gameplay surface (`.training-host.active-gameplay`) strictly suppresses horizontal and vertical scrolling via bounded layout geometry, definite height chain, `overflow: hidden;`, and defensive `overscroll-behavior: none;` (`touch-action: manipulation` is preserved for responsive tap interactions without double-tap delay, while scroll prevention is achieved through layout bounding). The active battle view must fit within the fixed viewport without scrolling on mobile and desktop devices. This scroll containment is strictly scoped: Settings (`.settings-page`), Privacy (`.privacy-page`), and long modal dialogs must retain natural scrolling (`overflow-y: auto`) (historical `.onboarding-host` removed in P2).
- **Deferred Future Topics**:
  - *Light-Theme Cyber Defense Visual Reconciliation (`DEFERRED`)*: Visual harmonization of combat scenes when operating under light appearance mode.

---

## 10. Offline, Accounts, and Local Progress

### Offline Baseline
- The core learning loop, practice sessions, evaluation, progression, and in-app privacy surface function **100% offline**.
- An active network connection must not be required to practice, review privacy details, or maintain progress across Android, Web, or Windows.

### User Accounts & Identity
- An account is **NOT required** for the MVP.
- All core learning functionality and in-app privacy information are completely usable without registration, login, profile setup, or authentication.

### Local Persistence
- Learning state, item histories, and progression milestones must persist reliably in local device storage.
- The current learner persistence format is **Schema V8** (established by [ADR-0011](decisions/ADR-0011-tester-telemetry-persistence-export-and-share.md) Amendment for P1b). It enriches SQLite `attempt_history` with `is_interrupted INTEGER NOT NULL DEFAULT 0 CHECK (is_interrupted IN (0, 1))` alongside the five presentation-context columns (`attempt_context_version`, `presented_deadline_ms`, `expected_pace_ms`, `resolved_role`, `operation_band_before`) captured atomically at presentation time with attempt evaluation, while losslessly preserving all historical attempts.
- Historical migrations (V4 $\to$ V5 for independent progression, V5 $\to$ V6 for persisted fluency, V6 $\to$ V7 for presentation context, V7 $\to$ V8 for interruption safety) execute transactionally, preserving all PracticePosition, store revision, item learning states, FSRS card states, and operation progression rows without reset.
- Local persistence must survive application restarts, browser refreshes, and device reboots.
- The shared Application layer owns persistence contracts, telemetry export interfaces, and learning logic but no concrete SQLite implementation or `Microsoft.Data.Sqlite` package. The `MathFirst.Infrastructure.Sqlite` adapter owns the concrete Schema V8 store and is registered by the native app through dependency injection.
- Telemetry export serializes complete attempt history to canonical JSON adhering to `telemetry_export_schema_v2` with 16 properties (including boolean `is_interrupted`).
- Enabled-operation preferences, Practice Time preferences, and the pseudonymous installation UUID are stored in application preferences separately from learner SQLite progress. Only completed attempt latency, outcome, and interruption fact are durable; an in-flight question's elapsed time, remaining deadline, and pause/active segment timestamps are not restored after a cold process restart.

### Android Runtime and App-Data Location
- The native MAUI application targets Android and Windows (`net10.0-android` and `net10.0-windows10.0.19041.0`); Web, iOS, and Mac Catalyst are not activated by the Android V1 runtime package.
- Android stores `mathfirst_learner.db` below the private `FileSystem.AppDataDirectory`. MathFirst requests no shared/external storage permission and introduces no application networking.

### Windows Runtime Identity and App-Data Location
- The canonical production application identifier is `com.tachiguro.mathfirst`.
- The unpackaged Windows runtime publisher is `Tachiguro`; it is intentionally separate from the development manifest signing identity.
- MathFirst continues to obtain its writable root from `FileSystem.AppDataDirectory`. The resulting logical Windows data root is `%LOCALAPPDATA%\Tachiguro\com.tachiguro.mathfirst\Data`, with the learner database at `mathfirst_learner.db` below that root.
- Legacy template-identity data is not automatically migrated or deleted.

### Privacy & Data Integrity
- The MVP does not require personal user identity information for the core learning loop.
- Detailed policies regarding telemetry, analytics, crash diagnostics, and potential future online services remain unresolved.

---

## 11. Platform Requirements

MathFirst must support three mandatory target platforms:

| Platform | Key Product Requirements |
|---|---|
| **Android** | Touch-first interaction, efficient numeric answer entry, 100% offline core training loop. |
| **Web** | Effective physical keyboard support, numpad support where available, 100% offline core training loop. |
| **Windows** | Effective physical keyboard support, numpad support where available, 100% offline core training loop. |

### Cross-Platform Design Objective
MathFirst aims to maximize shared domain and application logic across Android, Web, and Windows where technically sensible, while permitting platform-specific UI or integration adaptations where justified.

### Practice Timer Lifecycle
Answer time advances only while the application is foreground/interactive, the Practice/Home surface is active, the transient Practice gate is `Running`, and the session is awaiting an answer. Manual Pause, backgrounding, device lock, Settings, Ready/Pause/Resume gates, and Correct/Incorrect/Timeout feedback pause semantic elapsed time without resetting the current deadline or producing an attempt. Returning from Settings resumes the preserved valid question or the reconciled replacement question with fresh/preserved timer state according to the reconciliation contract ([ADR-0008](decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)). Same-process foreground return keeps the remaining time frozen until explicit Resume practice. A cold process restart gives the in-flight question a fresh timer without resetting learning progress or stored Practice Time.

---

## 12. MVP Product Boundary

The primary objective of the initial MVP is to prove the complete end-to-end MathFirst learning loop and progression model.

### Mandatory Capabilities (MUST for MVP)
- Present arithmetic learning items clearly.
- Capture numeric input efficiently across touch and keyboard environments.
- Evaluate answer correctness.
- Measure response latency / speed.
- Maintain a local learning state per item.
- Prioritize weak, slow, incorrect, and new items adaptively.
- Reinforce incorrect answers and reintroduce them later within the same session.
- Progress each operation independently through dense and structured arithmetic bands based on demonstrated correctness, fluency, and coverage.
- Function 100% offline without network connectivity.
- Operate completely without user accounts or authentication.
- Implement at least one core arithmetic operation (e.g. Addition) to validate the end-to-end learning loop.

---

## 13. Later Product Capabilities

The following capabilities are recognized as potential future extensions beyond the current approved native learning scope:

- **Extended Arithmetic Scope**: Negative subtraction results and division with remainders.
- **Larger Numeric Representation**: Arithmetic bands beyond the complete safe `Int32` boundary.
- **Optional Cross-Device Cloud Synchronization**: Optional multi-device synchronization and optional cloud accounts.
- **Data Portability**: Manual progress export and import functionality.
- **Richer Progress Presentation**: Additional learning statistics, visualization widgets, and practice customization.

*(Note: Specific dashboard implementations, visualization formats, and playlist features are non-binding illustrative possibilities and not approved roadmap commitments.)*

---

## 14. Current Scope Boundaries and Non-Goals

For the current product definition and MVP scope, the following boundaries apply:

1. **Focus on Fundamental Arithmetic Fact Automation**: Current product scope is strictly dedicated to automating elementary arithmetic facts. Narrative word problems, algebraic equations, geometry, and higher-level mathematics are not part of the current product definition.
2. **No Mandatory Accounts or Cloud Dependence for MVP**: The core learning experience must not require user registration, mandatory logins, or remote server dependencies.
3. **Low-Distraction Learning Environment**: In line with the age-neutral, respectful tone, the current product avoids unnecessary or distracting gamification (such as cartoon avatars, loot mechanics, or intrusive animations) that would detract from arithmetic focus.
4. **Unresolved Commercial and Telemetry Scope**: Monetization models, advertising policies, and telemetry/diagnostic frameworks are explicitly unresolved (see Section 15) and are not established as permanent product prohibitions.

---

## 15. Product Decision Status

The following register contains both resolved and unresolved product decisions. `RESOLVED` rows are normative product requirements. `UNRESOLVED` rows remain open and must not be treated as finalized requirements until formally resolved.

| Decision Area | Description | Status |
|---|---|---|
| **Scheduler Mathematics** | FSRS-6 exact-fact scheduling with Practice Position virtual time, 95% desired retention, existing 21 parameters, and disabled fuzzing. | `RESOLVED` |
| **Fact Catalog Strategy** | Hybrid dense/structured curriculum, deterministic procedural generation, unique acquisition ownership, stable identities, lazy materialization, and curriculum-bounded practice fact eligibility ($\text{owner}_O(F) \le B$) with dormant future-fact preservation ([ADR-0003](decisions/ADR-0003-independent-operation-progression-and-open-ended-fact-space.md), [ADR-0007](decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md)). | `RESOLVED` |
| **Telemetry, Analytics, and Crash Diagnostics** | Whether and how telemetry, analytics, and crash diagnostics should operate. | `UNRESOLVED` |
| **Exact Fluency Thresholds & Rating Mapping** | Adaptive expected pace $P_{\text{fact}}$ with Easy $\le 0.85 \cdot P_{\text{fact}}$ (clamp 600..2000 ms), Fluency $\le 1.25 \cdot P_{\text{fact}}$ (clamp 1500..4000 ms), Hard $> \text{FluencyThreshold}$, and persisted `is_fluent` in Schema V8 ([ADR-0004](decisions/ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md), [ADR-0011](decisions/ADR-0011-tester-telemetry-persistence-export-and-share.md)). | `RESOLVED` |
| **Adaptive Answer Deadline & Pace Model** | Hierarchical shrinkage pace estimation ($P_0=4500$, $P_{\text{learner}}$, $P_{\text{operation}}$, $P_{\text{band}}$, $P_{\text{fact}}$), instability allowance, entry allowance, digit-aware novelty floors (15s/20s/25s/30s) for unproven facts, clamped 3000..30000 ms; normal practice operates without deadline failure ([ADR-0005](decisions/ADR-0005-acclimation-timing-and-rapid-dense-progression.md), `P1`). | `RESOLVED` |
| **Coverage-First Dense Acquisition & Rapid Progression** | Complete frontier coverage and $C \cdot 10 \ge N \cdot 9$ correctness rule with Coverage-First New selection and recoverable errors; retired Fast Acquisition and MUL-D01 exception ([ADR-0005](decisions/ADR-0005-acclimation-timing-and-rapid-dense-progression.md)). | `RESOLVED` |
| **Repeated-Error Learning Interventions** | Non-scored teaching overlay for second consecutive session error on exact FactId, displaying canonical equation and result without learning mutations ([ADR-0004](decisions/ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md)). | `RESOLVED` |
| **Exact Range Expansion Increments** | Dense bands advance via complete frontier coverage and $C \cdot 10 \ge N \cdot 9$; Structured bands advance via the Dual-Window progression gate (Window A: 40 qualifying mathematical attempts; Window B: 40 timing-eligible attempts with $\ge 34$ fluent) ([ADR-0005](decisions/ADR-0005-acclimation-timing-and-rapid-dense-progression.md), `P1b`). | `RESOLVED` |
| **Commutative Cross-Seeding** | Whether and how mastery of `3 + 4` influences initial recall expectations for `4 + 3`. | `UNRESOLVED` |
| **Multi-Operation Range Sequencing** | Deterministic Addition/Subtraction/Multiplication/Division scheduling interleave with fully independent per-operation band advancement and no global checkpoint. | `RESOLVED` |
| **Manual Operation Control** | Users can independently enable/disable Addition, Subtraction, Multiplication, and Division in Settings (at least one enabled; deterministic bounded operation scheduling; independent per-operation role ordinals; progress preserved across toggles; zero-mutation Settings reconciliation) (`MF-SET-001`, `MF-STAB-002`, [ADR-0008](decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)). | `RESOLVED` |
| **Practice Time Configuration** | Normal practice no longer enforces deadlines across any setting (`HasEnforcedDeadline = false`); existing Practice Time choices (Standard, 30s, 45s, 60s, No Time Pressure) remain visible as accepted transitional UX debt pending P4 (Settings Simplification) (`MF-SET-001`, `MF-UX-005`, `P1`, `P1b`). | `RESOLVED` |
| **Direct-to-Practice Startup & Onboarding Removal** | Fresh learners start directly in active Practice on `/` with Addition enabled by default, Numpad layout, System theme/language, and haptics enabled; initial multi-step onboarding wizard removed; Initial Ready Gate and progress overview retained strictly for returning learners with completed practice history (`P2`). | `RESOLVED` |
| **Session Length & Bounding** | Periodic session check-in cadence every 20 accepted attempts with correctness/median-speed summary and Keep Going vs. Take a Break flow ([ADR-0004](decisions/ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md)). | `RESOLVED` |
| **Answer Submission Trigger** | Deterministic smart auto-submit for complete canonical integer answers, with Enter as a valid explicit force-submit path and no permanent Submit action. | `RESOLVED` |
| **Progress Visualization Details** | Specific dashboard widgets, charts, and mastery visual indicators. | `UNRESOLVED` |
| **Launch Languages & Localization** | Target launch languages and string resource management structure. | `UNRESOLVED` |
| **Accessibility Acceptance Details** | Exact WCAG conformance levels and specialized motor/cognitive accommodation settings. | `UNRESOLVED` |
| **Monetization Model** | Long-term project funding structure (e.g. completely free open source, donations, or optional support). | `UNRESOLVED` |
| **Cloud Account & Sync Architecture** | Optional cloud synchronization design and account backend protocols. | `UNRESOLVED` |
| **Export/Import Specification** | Exact schema, file format, and migration rules for manual data transfer. | `UNRESOLVED` |
| **Active Combat Positional Stability** | Positional stability invariant (fixed keypad coordinates, layout-isolated boss presentation via compositor transforms, scoped gameplay scroll suppression) (`MF-UX-008`). | `RESOLVED` |
| **Adaptive Timing Calibration** | Pace calibration readiness declared at $\ge 24$ timing-eligible positioned Correct attempts; normal practice operates without deadline failure; active interaction timing is segmented across interruptions ([ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md), `P1`, `P1b`). | `RESOLVED` |
| **Tester Telemetry Export** | Privacy-preserving manual export of complete attempt telemetry for testing via native system share dialog with sandboxed FileProvider security (`MF-TELEM-001`, `P1b`, [ADR-0011](decisions/ADR-0011-tester-telemetry-persistence-export-and-share.md), `telemetry_export_schema_v2`). | `RESOLVED` |
| **Light-Theme Combat Background** | Visual background styling and contrast harmonization for Cyber Defense in light theme. | `DEFERRED` |

---


## 16. Durable Architecture Constraints

This Product Definition establishes the following functional constraints for architecture and implementation:

1. **Target Platforms**: Architecture must support **Android**, **Web**, and **Windows**.
2. **Shared Logic Objective**: Architecture should maximize reuse of core domain and application logic across supported platforms where technically sensible.
3. **Offline Core Training**: Architecture must support the complete core learning and practice loop without requiring an active network connection.
4. **Local Progress Persistence**: Architecture must support persistent learning state, item histories, and progression across sessions on each target platform.
5. **Response-Time Measurement**: Architecture must allow the application to capture answer response latency accurately enough to distinguish conscious calculation from automated recall.
6. **Account Independence**: The MVP core learning loop must operate without mandatory authentication or account infrastructure.
