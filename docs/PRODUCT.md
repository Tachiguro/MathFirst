# MathFirst Product Definition

This document defines the authoritative, implementation-independent product contract for **MathFirst**. It captures confirmed product requirements, the learning model, progression rules, platform expectations, and Minimum Viable Product (MVP) boundaries.

> [!IMPORTANT]
> The independent-operation progression, hybrid curriculum, and adaptive learning model in Sections 4–8 are the accepted product contract from [ADR-0003](decisions/ADR-0003-independent-operation-progression-and-open-ended-fact-space.md), [ADR-0004](decisions/ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md), and [ADR-0005](decisions/ADR-0005-acclimation-timing-and-rapid-dense-progression.md) (`MF-LEARN-003`). The current native applications implement learner Schema V6, hierarchical adaptive pace, answer-length acclimation deadlines, adaptive FSRS ratings and fluency, Coverage-First Dense acquisition, correctness-driven Dense progression ($C \cdot 10 \ge N \cdot 9$), Numpad default layout, role-specific selector fallback chains with Early Review liveness, session-local teaching interventions, and periodic check-ins. Web runtime implementation remains deferred.

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

### Native Product Identity

- **Product identity**: MathFirst (`com.tachiguro.mathfirst`) currently presents release version `1.0` and build `1`.
- **Visible version information**: Settings displays localized Version/Build information.
- **Native brand treatment**: A white geometric MF mark uses primary green `#176B4D`, companion green `#0F523A`, light native background `#F4F7F5`, and dark native background `#121916`.
- **Fallback surfaces**: Not Found content is localized in English, German, and Russian; the native startup placeholder is the language-neutral `MathFirst`.

---

## 4. Core Learning Model

### Learning Items
- Individual arithmetic expressions/problems represent distinct **learnable items** (e.g. `3 + 4` and `4 + 3`).
- **Canonical Identity**: Facts use stable, presentation-direction-sensitive IDs: `add:{left}+{right}`, `sub:{left}-{right}`, `mul:{left}*{right}`, and `div:{dividend}/{divisor}`. Reversed commutative presentations remain distinct because recall speed and confidence may differ by direction.
- **Lazy Materialization**: A procedurally generated fact remains conceptual until its first accepted attempt. Presentation alone creates no durable learner row. Accepted submission atomically records attempt evidence and creates or updates item, FSRS, and progression state.
- **Unique Acquisition Ownership**: Every exact fact has one acquisition-owner band per operation: the earliest band in canonical curriculum order that generates its `FactId`. Later mathematical-family overlap may inform diagnostics but never grants duplicate acquisition or coverage credit.
- The learning engine maintains state and history per materialized exact fact for granular recall tracking and review.

### Fact-Space Semantics

- **Legacy/materialized learned facts** have persisted item or FSRS state and remain reviewable regardless of the current band.
- **Curriculum-eligible facts** belong to completed bands or the current band; eligibility does not mean mastery.
- **Current acquisition-frontier facts** are owned by the operation's current band. Dense bands require exhaustive frontier acquisition; structured bands require a deterministic representative sample of 16 distinct owned-frontier facts.
- **Due review facts** are materialized facts whose FSRS due Practice Position has arrived. Advancement never removes their eligibility.

Unseen, non-sampled structured candidates from completed bands are not permanent acquisition debt. Exact-fact FSRS review and operation-level advancement are separate mechanisms.

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

#### 2. Structured Band Advancement (Rolling Window)
For Structured bands, standard rolling-window requirements apply:
- At least 40 accepted attempts for that operation after its current band began;
- In its latest 40 qualifying attempts: at least 38 correct, at least 34 fluent (`IsFluent == true`), at least 20 from the current acquisition frontier, and at least $\min(16, \text{owned-frontier-size})$ distinct current-frontier facts;
- At least 16 distinct owned-frontier introductions during the current band.

`IsFluent` consumes durable `AttemptRecord.IsFluent`, which is evaluated adaptively against expected fact pace $P_{\text{fact}}$ at presentation time (or historical $\le 2500\text{ ms}$ backfill for legacy V5 rows). Incorrect and Timeout are non-fluent; correct responses slower than the adaptive fluency threshold are correct but non-fluent. There is no automatic band regression. Isolated mistakes age out of the rolling window in Structured bands, while weak facts across all bands remain active through remediation and FSRS.

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

For next accepted global Practice Position `p`, the scheduled operation is `(p - 1) mod 4` in the order Addition, Subtraction, Multiplication, Division. Each operation independently follows this repeating role cycle:

```text
New, Due, New, Maintenance, Frontier, New, Due, New, Due, Frontier
```

Normal proportions are 40% New, 30% Due FSRS, 20% explicit Frontier reinforcement, and 10% Maintenance.

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
For the scheduled operation, candidate selection follows a three-tiered precedence:

1. **Remediation Priority Override**: A due same-session remediation overrides normal selection when a fact has `NeedsRemediation == true`, `LastReviewPracticePosition != null`, and $\text{ProspectivePosition} \ge \text{LastReviewPracticePosition} + 4$. It does not change the scheduled operation, so a weak operation cannot globally starve stronger operations.
2. **Coverage-First Dense Acquisition**: When the scheduled operation's current band is `Dense` and its owned frontier contains unmaterialized facts, the selector immediately introduces an unseen owned-frontier fact as `PracticeSelectionRole.New` across nominal `Due`, `Maintenance`, or `Frontier` turns until first-pass coverage is complete. Structured bands do not receive Coverage-First selection.
3. **Role-Specific Fallback Chains**: If neither Remediation nor Coverage-First applies, the scheduled role resolves through its dedicated fallback chain:
   ```text
   New role:         New -> Useful Frontier -> Due -> Stale Maintenance -> Early Review
   Due role:         Due -> Useful Frontier -> Stale Maintenance -> Early Review
   Maintenance role: Stale Maintenance -> Useful Frontier -> Due -> Early Review
   Frontier role:    Useful Frontier -> Due -> Stale Maintenance -> Early Review
   ```

### Authoritative Materialization Invariant
Unmaterialized facts may enter practice **only** through explicit acquisition paths:
- (A) Normal `Requested New` when unmaterialized candidates exist;
- (B) `Coverage-First Dense New` for the scheduled operation's current owned frontier.

Non-New resolved roles (`Due`, `Maintenance`, `Frontier`, `Early Review`, `Remediation`) strictly cannot materialize unmaterialized facts. Earlier-owned review facts receive no acquisition or current-band coverage credit.

`Early Review` serves as the essential liveness bridge: when an operation has exhausted its frontier and has only future-due reviews, Early Review selects the oldest-reviewed future fact to keep PracticePosition advancing deterministically without stalling or entering an artificial Caught Up state.

Within a non-empty semantic pool, normal cooldowns apply first. If every candidate is excluded:
1. The commutative-mirror cooldown relaxes within that semantic pool;
2. The exact-fact cooldown relaxes within that semantic pool;
3. Selection proceeds deterministically from that pool.

Cooldown relaxation occurs strictly inside the already selected semantic pool and never changes fallback-pool priority. An empty candidate state under supported transitions is an integrity failure and fails closed.

### Repetition Density & Diversity Constraints
- **Exact Fact Cooldown**: The selector avoids repeating the same `FactId` within the last 3 presented facts when alternative candidates exist (`ExactFactCooldownDistance = 3`).
- **Commutative Mirror Cooldown**: For Addition and Multiplication, adjacent and near-adjacent mirror pairs (e.g., `6 × 0` and `0 × 6`, `3 + 4` and `4 + 3`) are avoided within 3 positions (`MirrorFactCooldownDistance = 3`), while maintaining distinct item entities and separate FSRS states. Non-commutative Subtraction and Division are strictly exempt.
- **Operation Streak Diversity**: Limits consecutive questions of the same arithmetic operation to a maximum of 2 when alternative candidates exist (`MaxPreferredOperationStreak = 2`).

### Task Distance Virtual Time Model
MathFirst schedules arithmetic reviews not by real-world calendar days, but by **Practice Position** (the monotonic count of accepted arithmetic attempts). One practice position corresponds to one virtual day from epoch `2000-01-01T00:00:00Z`, making review intervals independent of wall-clock manipulation, timezone shifts, or gaps between study days.

Exact-fact review preserves `FSRS.Core` 1.0.7, 95% desired retention, 21 default parameters, disabled fuzzing, and deterministic per-`FactId` cards. Responses are rated (`Again`, `Hard`, `Good`, `Easy`) via adaptive pace mapping without manual self-rating buttons. Operation advancement consumes durable attempt correctness and fluency and does not depend directly on FSRS stability, difficulty, or interval values. Advancement never deletes or suspends a card.

---

## 7. Response Evaluation, Adaptive Pace, and Fluency

True arithmetic fluency requires evaluating both correctness and speed against adaptive timing boundaries:

1. **Correctness**: Whether the submitted numeric answer is mathematically correct.
2. **Response Latency**: The elapsed monotonic active time between item readiness (`ITEM_READY`) and answer submission.
3. **Hierarchical Adaptive Pace Runtime**:
   Expected response latency ($P_{\text{fact}}$) is computed dynamically from positioned, accepted, mathematically correct attempt evidence ($PracticePosition > 0$). Latency samples are clamped to $[600\text{ ms}, 12000\text{ ms}]$.

   The estimator shrinks empirically across four hierarchical layers:
   - **Static Prior Baseline ($P_0$)**: $4500\text{ ms}$
   - **Learner Pace ($P_{\text{learner}}$)**: $\text{Shrink}(P_0, 12, \text{latest 30 learner Correct latencies})$
   - **Operation Pace ($P_{\text{operation}}$)**: $\text{Shrink}(P_{\text{learner}}, 8, \text{latest 20 operation Correct latencies})$
   - **Band Pace ($P_{\text{band}}$)**: $\text{Shrink}(P_{\text{operation}}, 6, \text{latest 15 band-frontier Correct latencies})$
   - **Exact Fact Pace ($P_{\text{fact}}$)**: $\text{Shrink}(P_{\text{band}}, 4, \text{latest 5 exact-fact Correct latencies})$

   $$\text{Shrink}(P_{\text{parent}}, W, \text{samples}) = \begin{cases} P_{\text{parent}} & \text{if samples is empty} \\ \left\lfloor \frac{W \cdot P_{\text{parent}} + |\text{samples}| \cdot \text{Median}(\text{samples}) + \frac{W + |\text{samples}|}{2}}{W + |\text{samples}|} \right\rfloor & \text{otherwise} \end{cases}$$

   Empty samples return the parent pace estimate. Legacy unpositioned attempts ($PracticePosition = \text{NULL}$) and `ItemLearningState.RollingLatencyMs` do not drive adaptive pace.

4. **Single Visible Adaptive Deadline, Acclimation Floors & Countdown**:
   - Every presented arithmetic fact receives an adaptive answer deadline calculated and fixed before timing begins:
     - **Durable Fact Proof**: A fact is proven when durable `ItemLearningState.CorrectAttempts > 0`. Only successfully persisted Correct attempts produce proof; Incorrect and Timeout attempts do not prove a fact.
     - **Exact-Fact Instability Allowance**: evaluated over the latest 5 positioned attempts for that FactId (+1000 ms per Incorrect, +1500 ms per Timeout, capped at 3000 ms):
       $$\text{Allowance} = \min(3000, 1000 \cdot N_{\text{incorrect}} + 1500 \cdot N_{\text{timeout}})$$
     - **Multi-Digit Entry Allowance**: $+1000\text{ ms} \cdot \max(0, \text{DigitCount} - 1)$, derived from the canonical correct result ($0$ is 1 digit).
     - **Adaptive Deadline Formula**:
       $$\text{AdaptiveDeadlineMs} = \text{clamp}\left(\left\lceil \frac{2 \cdot P_{\text{fact}} + \text{Allowance} + \text{EntryAllowance}}{100} \right\rceil \cdot 100, 3000, 30000\right)$$
     - **Digit-Aware Novelty Floors for Unproven Facts**:
       - 1 answer digit: minimum $15000\text{ ms}$
       - 2 answer digits: minimum $20000\text{ ms}$
       - 3 answer digits: minimum $25000\text{ ms}$
       - 4 or more answer digits: minimum $30000\text{ ms}$
     - **Effective Deadline**:
       - Proven facts: $\text{AdaptiveDeadlineMs}$
       - Unproven facts: $\min(30000, \max(\text{AdaptiveDeadlineMs}, \text{NoveltyFloorMs}))$
     - **Strict Non-Interference**: Novelty deadline extension expands response opportunity only. It does not alter measured response latency, adaptive fluency thresholds, `IsFluent`, or FSRS ratings.
   - A single visible countdown bar displays live remaining time with millisecond precision (`XX.XXX s`) inside the progress bar, depleting from right to left with a smooth green-to-red color transition.
   - Timer text features a direct black glyph contour/outline (`-webkit-text-stroke: 2px #000`) for high-contrast readability across themes.
   - Practice timing is active only while the application is foreground/interactable, the Practice surface is visible, the Practice gate is `Running`, and the session is awaiting an answer. Settings, onboarding, Ready/Pause/Resume gates, and feedback states keep the active item paused without consuming deadline or creating timeout attempts.
   - **Semantic Timeout Rule**: If elapsed active time reaches or exceeds the effective deadline, the attempt is recorded as `AttemptOutcome.Timeout`. A correct numeric entry submitted at or after the deadline remains a Timeout.

5. **Adaptive FSRS Rating & Fluency Classification**:
   - Thresholds derive from expected pace $P_{\text{fact}}$:
     $$\text{EasyThresholdMs} = \text{clamp}\left(\left\lfloor \frac{85 \cdot P_{\text{fact}} + 50}{100} \right\rfloor, 600, 2000\right)$$
     $$\text{FluencyThresholdMs} = \text{clamp}\left(\left\lfloor \frac{125 \cdot P_{\text{fact}} + 50}{100} \right\rfloor, 1500, 4000\right)$$
   - **Classification Rules**:
     - `Incorrect` $\implies$ Rating `Again`, `IsFluent = false`
     - `Timeout` $\implies$ Rating `Again`, `IsFluent = false`
     - `Correct` $\le \text{EasyThresholdMs} \implies$ Rating `Easy`, `IsFluent = true`
     - `Correct` in $(\text{EasyThresholdMs}, \text{FluencyThresholdMs}] \implies$ Rating `Good`, `IsFluent = true`
     - `Correct` $> \text{FluencyThresholdMs} \implies$ Rating `Hard`, `IsFluent = false`
   - `IsFluent` is persisted on `AttemptRecord.IsFluent` and `attempt_history.is_fluent` in Schema V6. Historical rolling-window progression evaluates this persisted boolean directly.

### Behavioral Requirement
- The system distinguishes between:
  - **Automated Recall**: Fast, accurate responses at or below the adaptive fluency threshold (`IsFluent = true`).
  - **Conscious Calculation**: Correct responses requiring extended time beyond the adaptive fluency threshold (`IsFluent = false`).
  - **Incorrect Answer**: Submitted wrong numeric integer (`Outcome = Incorrect`, `IsFluent = false`).
  - **Timeout**: Elapsed adaptive deadline window without valid submission (`Outcome = Timeout`, `IsFluent = false`).
- Slowly calculated correct answers remain active in practice until retrieval is fluid.


---

## 8. Session Behavior, Check-Ins, and Error Interventions

### Queue Composition
Training sessions are automatically generated by blending items across learning categories (new items, weak items, due reviews, and items requiring remediation) under deterministic operation and role scheduling.

### Progress HUD and Practice Cleanliness
- The compact progression HUD presents Addition, Subtraction, Multiplication, and Division in that visible order with `+`, `−`, `×`, and `÷`. Its numeric value is `BandIndex + 1`: an independent progression stage, not a maximum operand, mastery percentage, global arithmetic level, or session score. Valid BandIndex 0 and 1 display stages 1 and 2; unavailable or malformed bands fail closed as presentation unavailable rather than displaying a false stage. Internal curriculum band IDs are not learner-facing.
- Ordinary widths use four bounded HUD columns; below approximately 480 px, the layout deterministically becomes two columns. Symbols are visual, while accessible labels identify operation and progression stage (including unavailable state). English, German, and Russian progression labels are complete.
- **Distraction-Free Practice Header**: The permanent score HUD display has been removed from normal active practice. Session counts are retained internally for periodic check-ins rather than cluttering active arithmetic recall.
- Home practice owns or reuses one stable `ArithmeticCurriculum` for the component lifetime. HUD diagnostics are cached by authoritative learner-state generation and Practice Position, so timer-only approximately 50 ms presentation refreshes do not reconstruct the curriculum or regenerate the full diagnostic snapshot.

### Repeated-Error Teaching Intervention
When a learner struggles repeatedly with a specific fact in the active session:
1. **Trigger**: After the **second consecutive persisted error** (`Incorrect` or `Timeout`) for the exact same `FactId`, a non-scored teaching intervention overlay is presented.
2. **Instructional Display**: A blocking modal displays the canonical equation and correct result (e.g. `7 × 8 = 56`) with concise instruction to notice the equation and result before continuing.
3. **Strict Non-Mutation**: Acknowledging the teaching overlay creates **zero learning mutations**: no attempt record, no PracticePosition increment, no score mutation, no item state mutation, no FSRS card mutation, and no store revision.
4. **Lifecycle**: The session error counter for that FactId resets to zero upon trigger (continuing mistakes trigger in pairs: 2nd, 4th, etc.), and a Correct answer resets it to zero. Error counters are session-local and clear on cold restart or reset.

### Session Check-Ins & Break Flow
To provide encouraging feedback without breaking active focus:
1. **Cadence**: Triggered every 20 accepted attempts in the active training session ($20, 40, 60, \dots$). Cadence is session-local only.
2. **Summary**: Displays correct count out of 20 and the deterministic median response latency of **Correct attempts only** (Incorrect and Timeout latencies are excluded; if 0 correct, median is unavailable).
3. **Actions**:
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
  - Supported browser/WebView input paths synchronously reject invalid prospective keyboard, selection-replacement, deletion, and paste edits before the DOM mutates. The C# numeric policy remains authoritative for submission, the on-screen keypad, fallback input handling, and tests.
  - Rejected or incomplete input creates no semantic attempt and therefore cannot affect score, Practice Position, FSRS, exposure, remediation, or progression evidence. Input is bounded to 28 characters to protect layout while leaving ample future arithmetic range.
  - The responsive answer field comfortably exposes approximately eight digits plus a decimal separator at normal Windows desktop sizes. It remains centered, never exceeds the card width, and wraps below the arithmetic expression on narrow layouts without reducing arithmetic typography.
- **Android**:
  - Touch-first user interface.
  - The shared MathFirst custom keypad is the primary answer-entry surface. The focused answer field remains compatible with external keyboards while requesting native soft-keyboard suppression through the Android-specific `inputmode="none"` and manual virtual-keyboard policy.
  - The custom keypad uses the PC Numpad order by default (`7 8 9` at the top); learners can select the Phone order (`1 2 3` at the top). Both layouts use the shared `NumericAnswerInputPolicy` and the locale-familiar decimal glyph.
  - Practice, Settings, and onboarding respect Android system-bar and display-cutout safe areas. On phone-like portrait viewports, Appearance and keypad-selection choices stack vertically at full available card width. On sufficiently wide landscape viewports those controls use clean horizontal columns without forcing overflow.
  - Short phone landscape Practice uses a dedicated two-column composition: the full-width compact metadata/timer region stays above an interaction column containing equation and answer, while the custom keypad occupies the wider right column. Blocking Ready/Pause/error dialogs may span the Practice body. Geometry-based orientation, width, and dynamic-height queries preserve normal desktop layouts. Natural vertical scrolling remains the fallback only for unusually short landscape viewports.
  - Real-device acceptance must verify native IME suppression, touch input, external keyboard input where exposed by Android, portrait choice stacking, full short-landscape keypad visibility, rotation continuity, gesture/navigation-bar clearance, auto-submit, blocking feedback, Ready/Pause/Resume gates, lifecycle timing, and persistence across restart.
- **Windows & Web**:
  - Effective physical keyboard support.
  - Physical numeric keypad (numpad) support where available.
- **Shared On-Screen Keypad**:
  - During ordinary answer entry Practice renders a centered, responsive, clickable/touchable keypad with no permanent Submit, Confirm, or Continue action. Physical digits, Backspace, decimal comma/period, and Enter remain supported through the same controlled answer model.
  - Learners choose exactly one persistent UI layout: `Numpad` (default; `7 8 9` at the top) or `Phone` (`1 2 3` at the top). The locale-familiar decimal glyph is shown, while both separators remain valid input.
  - The choice is previewed on a dedicated onboarding step between Welcome and Tutorial (presenting Numpad first/left and Phone second/right), persists only at Get Started, and can be changed immediately in Settings without resuming practice timing. Restore Defaults and Full Local Reset return it to Numpad; Reset Learning Progress preserves it. The preference is UI state and is not stored in learner SQLite data.
- **Practice Flow and Readiness Gates**:
  - After a correct answer is accepted and persisted exactly once, Practice immediately prepares the next fact without visual delay or acknowledgement. Incorrect answers and timeouts pause timing and show a blocking dialog containing the original arithmetic expression, the submitted answer when applicable, the correct answer, and a Continue action that is also activated by Enter.
  - A cold application session with onboarding already complete starts behind an opaque Ready to practice dialog. No problem, keypad, semantic timing, attempt, score, or Practice Position change is exposed before Start. Onboarding Get Started itself satisfies this gate and does not lead to a redundant second dialog.
  - Manual Pause is available beside Settings only during active answer entry. It uses an accessible CSS-drawn two-bar icon with danger action treatment (red in the current theme, `button-danger`), freezes monotonic semantic time, preserves the current fact and input, and conditionally removes the problem and keypad from rendering and accessibility until Resume practice. Start and Resume remain normal primary (green) actions whenever the Pause action is absent.
  - Leaving the application foreground converts a running awaiting-answer item to a Background Resume gate. Foreground return does not restart timing; explicit Resume is required, including after returning from Settings to an interrupted Practice item. These transient gates require no learner SQLite schema change.

### Contextual Practice-Gate Personality

The practice gate uses deterministic localized contextual copy instead of a static title. Its contexts are initial readiness, return after a short absence, return after a long absence, manual-pause resume, background resume, and neutral Ready/Paused fallbacks. A prior accepted practice is Recent when it is absent or less than 30 minutes old, a Short Absence from 30 minutes to less than 3 days, and a Long Absence at 3 days or more; future-clock skew is safely Recent.

The selected copy remains stable for one genuine gate activation: ordinary rerenders, Home/Settings navigation, route recreation, and language changes do not rotate it. Language changes retain the same language-independent message identity and re-localize its text. A later genuine gate activation may choose another variant, while reset or authoritative learner-state replacement invalidates stale presentation identity. Background-resume copy applies both immediately and after a pending persistence, advance, or recovery operation reaches its final gate state.

The physical contextual corpus supports English, German, and Russian with 55 message IDs per locale (165 localized strings total). It has ID and placeholder parity across locales, unique visible text within each locale and trigger pool, locale normalization under `LanguagePreferencePolicy`, and static Ready/Paused localization as the ultimate fallback. It is local-only: there is no runtime AI, remote copy service, network dependency, or telemetry dependency.

Tone is concise, respectful, age-neutral, and secondary to arithmetic interaction. Neutral, welcoming, semantically supported progress-aware, dry-humorous, and occasional lightly cheeky wording is allowed. The product avoids insults, humiliation, guilt, patronizing or manipulative language, exaggerated praise, false achievement claims, and assumptions about a learner’s personal circumstances. Onboarding readiness is history-neutral: “Your arithmetic practice is ready,” because restored UI preferences can replay onboarding while learning history remains.

Contextual copy is presentation behavior only. It does not change FactId, curriculum generation, Practice Position, BandIndex progression, advancement gates, evidence windows, FSRS, remediation, cooldowns, operation scheduling, answer deadlines, answer evaluation, accepted-attempt semantics, or Schema V6.
- **Windows Settings Confirmations**:
  - Restore Defaults, Reset Learning Progress, and Full Local Reset remain explicit two-step actions. After an inline confirmation is rendered, it receives programmatic focus and is scrolled into view with nearest-block behavior; reduced-motion preferences disable smooth scrolling.

---

## 10. Offline, Accounts, and Local Progress

### Offline Baseline
- The core learning loop, practice sessions, evaluation, and progression must function **100% offline**.
- An active network connection must not be required to practice or maintain progress across Android, Web, or Windows.

### User Accounts & Identity
- An account is **NOT required** for the MVP.
- All core learning functionality must be completely usable without registration, login, profile setup, or authentication.

### Local Persistence
- Learning state, item histories, and progression milestones must persist reliably in local device storage.
- The current learner persistence format is **Schema V6** (established by [ADR-0004](decisions/ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md)). It introduces `AttemptRecord.IsFluent` / SQLite `attempt_history.is_fluent`.
- Transactional migration from V5 to V6 backfills historical attempts with the fixed rule (Correct with `ResponseLatencyMs <= 2500` $\implies \text{fluent}$; otherwise non-fluent), preserving all PracticePosition, store revision, item learning states, FSRS card states, and operation progression rows without reset. Rollback leaves V5 intact upon failure.
- Local persistence must survive application restarts, browser refreshes, and device reboots.
- The shared Application layer owns persistence contracts and learning logic but no concrete SQLite implementation or `Microsoft.Data.Sqlite` package. The `MathFirst.Infrastructure.Sqlite` adapter owns the concrete Schema V6 store and is registered by the native app through dependency injection.

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
Answer time advances only while the application is foreground/interactive, the Practice/Home surface is active, the transient Practice gate is `Running`, and the session is awaiting an answer. Backgrounding, device lock, Settings, onboarding, Ready/Pause/Resume gates, and Correct/Incorrect/Timeout feedback pause semantic elapsed time without resetting the current deadline or producing an attempt. Foreground return keeps the same remaining semantic time frozen until explicit Resume practice.

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
| **Fact Catalog Strategy** | Hybrid dense/structured curriculum, deterministic procedural generation, unique acquisition ownership, stable identities, and lazy materialization. | `RESOLVED` |
| **Telemetry, Analytics, and Crash Diagnostics** | Whether and how telemetry, analytics, and crash diagnostics should operate. | `UNRESOLVED` |
| **Exact Fluency Thresholds & Rating Mapping** | Adaptive expected pace $P_{\text{fact}}$ with Easy $\le 0.85 \cdot P_{\text{fact}}$ (clamp 600..2000 ms), Fluency $\le 1.25 \cdot P_{\text{fact}}$ (clamp 1500..4000 ms), Hard $> \text{FluencyThreshold}$, and persisted `IsFluent` in Schema V6 ([ADR-0004](decisions/ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md)). | `RESOLVED` |
| **Adaptive Answer Deadline & Pace Model** | Hierarchical shrinkage pace estimation ($P_0=4500$, $P_{\text{learner}}$, $P_{\text{operation}}$, $P_{\text{band}}$, $P_{\text{fact}}$), instability allowance, entry allowance, digit-aware novelty floors (15s/20s/25s/30s) for unproven facts, clamped 3000..30000 ms ([ADR-0005](decisions/ADR-0005-acclimation-timing-and-rapid-dense-progression.md)). | `RESOLVED` |
| **Coverage-First Dense Acquisition & Rapid Progression** | Complete frontier coverage and $C \cdot 10 \ge N \cdot 9$ correctness rule with Coverage-First New selection and recoverable errors; retired Fast Acquisition and MUL-D01 exception ([ADR-0005](decisions/ADR-0005-acclimation-timing-and-rapid-dense-progression.md)). | `RESOLVED` |
| **Repeated-Error Learning Interventions** | Non-scored teaching overlay for second consecutive session error on exact FactId, displaying canonical equation and result without learning mutations ([ADR-0004](decisions/ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md)). | `RESOLVED` |
| **Exact Range Expansion Increments** | Dense bands advance via complete frontier coverage and $C \cdot 10 \ge N \cdot 9$; Structured bands advance via 40-attempt rolling window gate ([ADR-0005](decisions/ADR-0005-acclimation-timing-and-rapid-dense-progression.md)). | `RESOLVED` |
| **Commutative Cross-Seeding** | Whether and how mastery of `3 + 4` influences initial recall expectations for `4 + 3`. | `UNRESOLVED` |
| **Multi-Operation Range Sequencing** | Deterministic Addition/Subtraction/Multiplication/Division scheduling interleave with fully independent per-operation band advancement and no global checkpoint. | `RESOLVED` |
| **Manual Operation Control** | Whether users should be able to manually enable, disable, or override operation progression. | `UNRESOLVED` |
| **Session Length & Bounding** | Periodic session check-in cadence every 20 accepted attempts with correctness/median-speed summary and Keep Going vs. Take a Break flow ([ADR-0004](decisions/ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md)). | `RESOLVED` |
| **Answer Submission Trigger** | Deterministic smart auto-submit for complete canonical integer answers, with Enter as a valid explicit force-submit path and no permanent Submit action. | `RESOLVED` |
| **Progress Visualization Details** | Specific dashboard widgets, charts, and mastery visual indicators. | `UNRESOLVED` |
| **Launch Languages & Localization** | Target launch languages and string resource management structure. | `UNRESOLVED` |
| **Accessibility Acceptance Details** | Exact WCAG conformance levels and specialized motor/cognitive accommodation settings. | `UNRESOLVED` |
| **Monetization Model** | Long-term project funding structure (e.g. completely free open source, donations, or optional support). | `UNRESOLVED` |
| **Cloud Account & Sync Architecture** | Optional cloud synchronization design and account backend protocols. | `UNRESOLVED` |
| **Export/Import Specification** | Exact schema, file format, and migration rules for manual data transfer. | `UNRESOLVED` |

---


## 16. Durable Architecture Constraints

This Product Definition establishes the following functional constraints for architecture and implementation:

1. **Target Platforms**: Architecture must support **Android**, **Web**, and **Windows**.
2. **Shared Logic Objective**: Architecture should maximize reuse of core domain and application logic across supported platforms where technically sensible.
3. **Offline Core Training**: Architecture must support the complete core learning and practice loop without requiring an active network connection.
4. **Local Progress Persistence**: Architecture must support persistent learning state, item histories, and progression across sessions on each target platform.
5. **Response-Time Measurement**: Architecture must allow the application to capture answer response latency accurately enough to distinguish conscious calculation from automated recall.
6. **Account Independence**: The MVP core learning loop must operate without mandatory authentication or account infrastructure.
