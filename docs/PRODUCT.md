# MathFirst Product Definition

This document defines the authoritative, implementation-independent product contract for **MathFirst**. It captures confirmed product requirements, the learning model, progression rules, platform expectations, and Minimum Viable Product (MVP) boundaries.

> [!IMPORTANT]
> The independent-operation progression and hybrid curriculum in Sections 4–8 are the accepted and implemented MF-LEARN-001 product contract from [ADR-0003](decisions/ADR-0003-independent-operation-progression-and-open-ended-fact-space.md). The current native applications implement learner Schema V5, independent operation progression, deterministic bounded selection, and transactional V4-to-V5 migration. Web runtime implementation remains deferred.

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

Dense advancement means complete lifetime exposure to the band's owned frontier plus recent operation-specific evidence of readiness. Structured advancement means representative fluency sufficient to begin the next arithmetic family; it does not claim exhaustive mastery of every candidate or arbitrary arithmetic at that magnitude.

The standard advancement profile applies to Addition, Subtraction, Division, and Multiplication BandIndex 1 and later. An operation advances only on the atomic accepted submission that completes all of these requirements:

1. At least 40 accepted attempts for that operation after its current band began.
2. In its latest 40 qualifying attempts: at least 38 correct, at least 34 fluent, at least 20 from the current acquisition frontier, and at least `min(16, owned-frontier-size)` distinct current-frontier facts.
3. Dense coverage includes a lifetime accepted attempt for every owned-frontier fact; structured coverage includes at least 16 distinct owned-frontier introductions during the band.

The sole exception is the initial Multiplication band: Multiplication BandIndex 0 (`MUL-D01`) advances after 12 qualifying accepted multiplication attempts following `BandStartedPracticePosition`, with at least 11 correct, 11 fluent, 8 owned-frontier attempts, all four owned `MUL-D01` facts represented distinctly, and complete dense lifetime coverage of `0 × 0`, `0 × 1`, `1 × 0`, and `1 × 1`. After advancement, `MUL-D02` becomes active and factor-2 facts enter only through the existing deterministic selector; no factor-2 injection bypasses normal selection.

Fluent means Correct with response latency at or below 2500 ms. Incorrect and Timeout are incorrect and non-fluent; a slower correct response is correct but non-fluent. There is no automatic band regression. Isolated mistakes age out of the rolling window, while weak facts remain active through remediation and FSRS.

### Open-Ended Arithmetic Scope

There is no artificial fixed catalog or level-10 ceiling. Bands continue procedurally while the next complete band is safe in `Int32`; when it is not, that operation remains in maintenance. Generation must use checked arithmetic. Addition requires a checked result, Subtraction remains non-negative, Multiplication requires a checked product, and Division requires a positive divisor and exact non-negative integer result.

### Initial MVP Arithmetic Boundary
Negative subtraction, division with remainders, decimal-result arithmetic, and division by zero remain outside the current product boundary. `long` and `BigInteger` expansion are not part of MF-LEARN-001.

---

## 6. Adaptive Training Loop & Spaced Repetition (FSRS-6)

For next accepted global Practice Position `p`, the scheduled operation is `(p - 1) mod 4` in the order Addition, Subtraction, Multiplication, Division. Each operation independently follows this repeating role cycle:

```text
New, Due, New, Maintenance, Frontier, New, Due, New, Due, Frontier
```

Normal proportions are 40% New, 30% Due FSRS, 20% explicit Frontier reinforcement, and 10% Maintenance. The semantic candidate pools are:

- **New**: an unmaterialized exact fact owned by the scheduled operation's current band.
- **Frontier**: a materialized exact fact owned by the scheduled operation's current band.
- **Due**: a materialized exact fact for the scheduled operation that is due under FSRS.
- **Maintenance**: a materialized exact fact for the scheduled operation eligible for non-due maintenance under the existing practice policy.
- **AnyMaterialized**: any eligible materialized exact fact for the scheduled operation.

A due same-session remediation for the scheduled operation overrides that operation's normal role. It does not override operation scheduling, so a weak operation cannot globally starve stronger operations. After that override is considered, the scheduled role uses the first non-empty pool in its exact fallback chain:

```text
New role:        New -> Frontier -> Due -> Maintenance -> AnyMaterialized
Due role:        Due -> Frontier -> Maintenance -> AnyMaterialized
Maintenance role: Maintenance -> Frontier -> Due -> AnyMaterialized
Frontier role:   Frontier -> Due -> Maintenance -> AnyMaterialized
```

The selector never changes the scheduled operation because a preferred pool is empty. Only a scheduled New role may select an unmaterialized fact. Due, Maintenance, Frontier, remediation, and AnyMaterialized paths never introduce a new exact fact. Earlier-owned review facts receive no acquisition or current-band coverage credit. Normal introduction therefore remains capped at four opportunities per ten accepted attempts for an operation.

In a dense band, New selects unmaterialized owned-frontier facts until the frontier is fully materialized, then falls back to Frontier. In a structured band, New falls back to Frontier after 16 distinct owned-frontier introductions. A fresh operation's first role is New and its initial acquisition frontier is non-empty; after its first accepted attempt, at least one materialized fact exists. Migrated learners retain their materialized facts and select New or Frontier according to migration state. This makes the fallback system total without a persisted candidate cursor.

Within a non-empty semantic pool, normal cooldowns apply first. If every candidate is excluded, the commutative-mirror cooldown relaxes first, then the exact-fact cooldown, after which selection proceeds deterministically from that pool. Soft cooldowns cannot make selection non-total.

Without remediation overrides, four New roles and two explicit Frontier roles per ten operation attempts provide at least six current-frontier opportunities. An exhausted New pool falls back to Frontier and remains a current-frontier attempt, so a normal 40-attempt operation window provides at least 24 frontier opportunities and 16 New opportunities. Due and Maintenance fallback to Frontier can only increase frontier exposure. The 20-frontier and 16-distinct structured gates are therefore mechanically reachable.

Forty attempts are the minimum evidence-window size, not a guarantee of advancement exactly at attempt 40. Same-session remediation or weak-fact obligations may delay simultaneous satisfaction of all gates. A stable learner can advance once those obligations clear, and no operation is permanently blocked by an empty role pool.

The selector is deterministic: identical durable learner state yields the same operation, and identical operation/band/role/state yields the same candidate. Candidate ordering cannot depend on dictionary/hash iteration or `Random.Shared`. Presentation without accepted submission changes no durable progression.

### Repetition Density & Diversity Constraints
- **Exact Fact Cooldown**: The selector avoids repeating the same `FactId` within the last 3 presented facts when alternative candidates exist (`ExactFactCooldownDistance = 3`).
- **Commutative Mirror Cooldown**: For Addition and Multiplication, adjacent and near-adjacent mirror pairs (e.g., `6 × 0` and `0 × 6`, `3 + 4` and `4 + 3`) are avoided within 3 positions (`MirrorFactCooldownDistance = 3`), while maintaining distinct item entities and separate FSRS states. Non-commutative Subtraction and Division are strictly exempt.
- **Operation Streak Diversity**: Limits consecutive questions of the same arithmetic operation to a maximum of 2 when alternative candidates exist (`MaxPreferredOperationStreak = 2`).
- **Soft Constraint Relaxation**: Diversity constraints relax in a fixed deterministic order so selection cannot deadlock.

### Task Distance Virtual Time Model
MathFirst schedules arithmetic reviews not by real-world calendar days, but by **Practice Position** (the monotonic count of accepted arithmetic attempts). One practice position corresponds to one virtual day from epoch `2000-01-01T00:00:00Z`, making review intervals independent of wall-clock manipulation, timezone shifts, or gaps between study days. Responses are automatically rated (`Again`, `Hard`, `Good`, `Easy`) via deterministic latency mapping without manual self-rating buttons.

Exact-fact review preserves `FSRS.Core` 1.0.7, 95% desired retention, the existing 21 parameters, disabled fuzzing, and deterministic per-`FactId` cards. Operation advancement consumes raw correctness and latency evidence and does not depend directly on FSRS stability, difficulty, or interval values. Advancement never deletes or suspends a card.

---

## 7. Response Evaluation, Timing, and Fluency

True arithmetic fluency requires evaluating both correctness and speed against distinct timing boundaries:

1. **Correctness**: Whether the submitted numeric answer is mathematically correct.
2. **Response Latency**: The elapsed monotonic time between item presentation (`ITEM_READY`) and answer submission.
3. **Answer Deadline & Visible Countdown**:
   - Every newly presented arithmetic fact receives a fixed **30,000 ms** answer deadline, independent of `ConsecutiveCorrectStreak`, operation, BandIndex, FactId, FSRS state, and prior latency. The timeout boundary is based on that fixed deadline; historical `ConsecutiveCorrectStreak` remains stored and maintained where already required.
   - A visible countdown bar displays live remaining time with millisecond precision (`XX.XXX s`) inside the progress bar, depleting from right to left with a smooth green-to-red color transition.
   - Timer text features a direct black glyph contour/outline (`-webkit-text-stroke: 2px #000`) for crystal-clear readability directly over all dynamic fill colors without needing an enclosing dark badge.
   - Practice timing is active only while the application is foreground/interactable, the Practice/Home surface is visible, the transient Practice gate is `Running`, and the session is awaiting an answer. Settings, onboarding, Ready/Pause/Resume gates, and feedback states keep the active item paused. Paused time does not affect response latency, deadlines, semantic attempt history, Practice Position, or session score.
   - The onboarding **Get Started** action is the authoritative transition into active arithmetic practice. A fresh first fact starts with its full 30-second deadline only after that action; an existing paused fact resumes with its unchanged remaining time after onboarding triggered by restoring defaults.
   - Completed feedback states (`TimeoutFeedback`, `IncorrectFeedback`, `CorrectFeedback`) survive Settings or onboarding navigation without restarting the timer or generating duplicate attempt records.
   - Visual UI refresh (~50 ms cadence) is presentation-only; monotonic time is authoritative and immune to UI rendering drift.
   - Response latency and FSRS scheduling remain strictly separate: the fixed deadline determines only when a timeout occurs, while actual response latency is measured independently and rated deterministically (`Easy` $\le 1000\text{ ms}$, `Good` $1001..2500\text{ ms}$, `Hard` $> 2500\text{ ms}$, `Again` on Incorrect/Timeout). A correct answer at 15,000 ms is still `Hard`; the progression fluency threshold remains 2500 ms.

### Behavioral Requirement
- The system must distinguish between:
  - **Automated Recall**: Fast, accurate responses indicating memorized mastery.
  - **Conscious Calculation**: Correct responses that required noticeable calculation time.
  - **Incorrect Answer**: Submitted wrong numeric integer.
  - **Timeout**: Elapsed fixed 30-second answer-deadline window without valid submission.
- A slowly calculated correct answer must not be treated as equivalent to an automated recall; it must remain active in practice until retrieval is fluid.

---

## 8. Session Behavior and Error Feedback

### Queue Composition
Training sessions are automatically generated by blending items across learning categories (new items, weak items, due reviews, and items requiring remediation).

### Session Score and Progress HUD

- The transient session score is `SessionCorrectCount / SessionTotalCount`. It is hidden in Initial Ready before first Start, then shown once in the top-right header/action region after Start, during manual Pause, Resume, relevant background-resume states, feedback, and persistence recovery according to retained session context. It is neither learner persistence nor a mastery metric.
- The compact progression HUD presents Addition, Subtraction, Multiplication, and Division in that visible order with `+`, `−`, `×`, and `÷`. Its numeric value is `BandIndex + 1`: an independent progression stage, not a maximum operand, mastery percentage, global arithmetic level, or session score. Valid BandIndex 0 and 1 display stages 1 and 2; unavailable or malformed bands fail closed as presentation unavailable rather than displaying a false stage. Internal curriculum band IDs are not learner-facing.
- Ordinary widths use four bounded HUD columns; below approximately 480 px, the layout deterministically becomes two columns. Symbols are visual, while accessible labels identify operation and progression stage (including unavailable state). English, German, and Russian progression labels are complete; no broad keypad or training-card redesign is implied.
- Home practice owns or reuses one stable `ArithmeticCurriculum` for the component lifetime. HUD diagnostics are cached by authoritative learner-state generation and Practice Position, so timer-only approximately 50 ms presentation refreshes do not reconstruct the curriculum or regenerate the full diagnostic snapshot. Authoritative progression, reload, recovery, and reset changes refresh the presentation. This is an architectural regression-prevention contract, not a performance benchmark.

### Explicit Error Feedback and In-Session Remediation
When a learner provides an incorrect answer or times out during a session:
1. **Explicit Error Feedback**: The outcome must prominently show the original arithmetic expression, clearly indicate the mistake with error styling, display the learner's submitted answer (for incorrect attempts) or a time-expired notice (for timeouts), and show the mathematically correct result.
2. **Explicit Acknowledgement**: A blocking in-Practice dialog replaces the answer controls and requires deliberate learner acknowledgement (via Enter key or Continue action) before advancing to the next fact. Incorrect or timed-out items never automatically skip forward.
3. **Same-Session Remediation**: The missed fact is scheduled for recurrence later within the **same training session** to solidify memory before session conclusion, avoiding immediate lock-step retries on the exact same screen.

---

## 9. Input and Interaction Requirements

Input ergonomics are critical to measuring true arithmetic recall rather than motor typing friction:

- **Shared Numeric Answer Contract**:
  - The answer editor accepts only canonical unsigned decimal notation with ASCII digits and at most one decimal separator. The integer part is either exactly `0` or begins with `1` through `9`; redundant leading-zero forms such as `00`, `01`, `0004`, and `00.5` are rejected. Leading decimal forms such as `.5` and `,5` remain valid.
  - Both period and comma are accepted in every UI language; signs, whitespace, exponent notation, alphabetic characters, and multiple/mixed separators are rejected.
  - Empty input plus trailing-separator states such as `12.` and `12,` remain valid while editing. Submission normalizes comma or period to an invariant exact `decimal` value; equivalent representations such as `14`, `14.0`, and `14,00` compare numerically.
  - Complete integer answers auto-submit deterministically: exact equality submits immediately; a canonical digit-count match submits as correct or incorrect; and an integer prefix that cannot become the correct canonical representation may submit early as incorrect. Still-plausible shorter prefixes remain non-semantic indefinitely, with no debounce timeout. Enter remains an explicit force-submit path for any valid complete numeric value.
  - Supported browser/WebView input paths synchronously reject invalid prospective keyboard, selection-replacement, deletion, and paste edits before the DOM mutates. The C# numeric policy remains authoritative for submission, the on-screen keypad, fallback input handling, and tests.
  - Rejected or incomplete input creates no semantic attempt and therefore cannot affect score, Practice Position, FSRS, exposure, remediation, or progression evidence. Input is bounded to 28 characters to protect layout while leaving ample future arithmetic range.
  - The responsive answer field comfortably exposes approximately eight digits plus a decimal separator at normal Windows desktop sizes. It remains centered, never exceeds the card width, and wraps below the arithmetic expression on narrow layouts without reducing arithmetic typography.
- **Android**:
  - Touch-first user interface.
  - The shared MathFirst custom keypad is the primary answer-entry surface. The focused answer field remains compatible with external keyboards while requesting native soft-keyboard suppression through the Android-specific `inputmode="none"` and manual virtual-keyboard policy.
  - Phone orientation uses the Phone keypad order by default (`1 2 3` at the top); learners can select the PC Numpad order (`7 8 9` at the top). Both layouts use the shared `NumericAnswerInputPolicy` and the locale-familiar decimal glyph.
  - Practice, Settings, and onboarding respect Android system-bar and display-cutout safe areas. On phone-like portrait viewports, Appearance and keypad-selection choices stack vertically at full available card width. On sufficiently wide landscape viewports those controls use clean horizontal columns without forcing overflow.
  - Short phone landscape Practice uses a dedicated two-column composition: the full-width compact metadata/timer region stays above an interaction column containing equation and answer, while the custom keypad occupies the wider right column. Blocking Ready/Pause/error dialogs may span the Practice body. Geometry-based orientation, width, and dynamic-height queries preserve normal desktop layouts. Natural vertical scrolling remains the fallback only for unusually short landscape viewports.
  - Real-device acceptance must verify native IME suppression, touch input, external keyboard input where exposed by Android, portrait choice stacking, full short-landscape keypad visibility, rotation continuity, gesture/navigation-bar clearance, auto-submit, blocking feedback, Ready/Pause/Resume gates, lifecycle timing, and persistence across restart.
- **Windows & Web**:
  - Effective physical keyboard support.
  - Physical numeric keypad (numpad) support where available.
- **Shared On-Screen Keypad**:
  - During ordinary answer entry Practice renders a centered, responsive, clickable/touchable keypad with no permanent Submit, Confirm, or Continue action. Physical digits, Backspace, decimal comma/period, and Enter remain supported through the same controlled answer model.
  - Learners choose exactly one persistent UI layout: `Phone` (default; `1 2 3` at the top) or `Numpad` (`7 8 9` at the top). The locale-familiar decimal glyph is shown, while both separators remain valid input.
  - The choice is previewed on a dedicated onboarding step between Welcome and Tutorial, persists only at Get Started, and can be changed immediately in Settings without resuming practice timing. Restore Defaults and Full Local Reset return it to Phone; Reset Learning Progress preserves it. The preference is UI state and is not stored in learner SQLite data.
- **Practice Flow and Readiness Gates**:
  - After a correct answer is accepted and persisted exactly once, Practice immediately prepares the next fact without visual delay or acknowledgement. Incorrect answers and timeouts pause timing and show a blocking dialog containing the original arithmetic expression, the submitted answer when applicable, the correct answer, and a Continue action that is also activated by Enter.
  - A cold application session with onboarding already complete starts behind an opaque Ready to practice dialog. No problem, keypad, semantic timing, attempt, score, or Practice Position change is exposed before Start. Onboarding Get Started itself satisfies this gate and does not lead to a redundant second dialog.
  - Manual Pause is available beside Settings only during active answer entry. It is presented with the danger action treatment (red in the current theme), freezes monotonic semantic time, preserves the current fact and input, and conditionally removes the problem and keypad from rendering and accessibility until Resume practice. Start and Resume remain normal primary (green) actions whenever the Pause action is absent.
  - Leaving the application foreground converts a running awaiting-answer item to a Background Resume gate. Foreground return does not restart timing; explicit Resume is required, including after returning from Settings to an interrupted Practice item. These transient gates require no learner SQLite schema change.

### Contextual Practice-Gate Personality

The practice gate uses deterministic localized contextual copy instead of a static title. Its contexts are initial readiness, return after a short absence, return after a long absence, manual-pause resume, background resume, and neutral Ready/Paused fallbacks. A prior accepted practice is Recent when it is absent or less than 30 minutes old, a Short Absence from 30 minutes to less than 3 days, and a Long Absence at 3 days or more; future-clock skew is safely Recent.

The selected copy remains stable for one genuine gate activation: ordinary rerenders, Home/Settings navigation, route recreation, and language changes do not rotate it. Language changes retain the same language-independent message identity and re-localize its text. A later genuine gate activation may choose another variant, while reset or authoritative learner-state replacement invalidates stale presentation identity. Background-resume copy applies both immediately and after a pending persistence, advance, or recovery operation reaches its final gate state.

The physical contextual corpus supports English, German, and Russian with 55 message IDs per locale (165 localized strings total). It has ID and placeholder parity across locales, unique visible text within each locale and trigger pool, locale normalization under `LanguagePreferencePolicy`, and static Ready/Paused localization as the ultimate fallback. It is local-only: there is no runtime AI, remote copy service, network dependency, or telemetry dependency.

Tone is concise, respectful, age-neutral, and secondary to arithmetic interaction. Neutral, welcoming, semantically supported progress-aware, dry-humorous, and occasional lightly cheeky wording is allowed. The product avoids insults, humiliation, guilt, patronizing or manipulative language, exaggerated praise, false achievement claims, and assumptions about a learner’s personal circumstances. Onboarding readiness is history-neutral: “Your arithmetic practice is ready,” because restored UI preferences can replay onboarding while learning history remains.

Contextual copy is presentation behavior only. It does not change FactId, curriculum generation, Practice Position, BandIndex progression, advancement gates, evidence windows, FSRS, remediation, cooldowns, operation scheduling, answer deadlines, answer evaluation, accepted-attempt semantics, or Schema V5.
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
- MF-STAB-001 retains Schema V5 and introduces no migration, learner reset, card deletion, FactId change, band reindexing, FSRS reset, persisted session score, or persisted HUD cache. Existing Multiplication BandIndex-0 learners can advance under the bootstrap profile if their retained valid evidence satisfies it; BandIndex-1-or-later learners retain the standard profile, and no learner is intentionally moved backward.
- Local persistence must survive application restarts, browser refreshes, and device reboots.
- The shared Application layer owns persistence contracts and learning logic but no concrete SQLite implementation or `Microsoft.Data.Sqlite` package. The `MathFirst.Infrastructure.Sqlite` adapter owns the concrete Schema V5 store and is registered by the native app through dependency injection.

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
| **Exact Fluency Thresholds** | Advancement fluency is Correct at `<= 2500 ms`; FSRS retains its current Easy/Good/Hard latency mapping. | `RESOLVED` |
| **Exact Range Expansion Increments** | Independent canonical bands and the 40-attempt correctness/fluency/frontier/coverage advancement gate defined by ADR-0003. | `RESOLVED` |
| **Commutative Cross-Seeding** | Whether and how mastery of `3 + 4` influences initial recall expectations for `4 + 3`. | `UNRESOLVED` |
| **Multi-Operation Range Sequencing** | Deterministic Addition/Subtraction/Multiplication/Division scheduling interleave with fully independent per-operation band advancement and no global checkpoint. | `RESOLVED` |
| **Manual Operation Control** | Whether users should be able to manually enable, disable, or override operation progression. | `UNRESOLVED` |
| **Session Length & Bounding** | Exact rules determining when a training session concludes (e.g. dynamic item count, queue exhaustion, or time limits). | `UNRESOLVED` |
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
