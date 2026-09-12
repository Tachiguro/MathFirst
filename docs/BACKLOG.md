# MathFirst Backlog Registry

This document serves as the authoritative registry for **accepted inactive work** on MathFirst.

---

## 1. Backlog Governance Rules

1. **Authoritative Registry**: All accepted deferred packages, features, and non-immediate improvements must be recorded in this registry.
2. **Stable Package Identifiers**: Every backlog item is assigned a permanent, unique identifier in the canonical format `MF-<CATEGORY>-<NUMBER>` (e.g. `MF-GOV-001`, `MF-CALC-001`, `MF-UX-001`, `MF-PERSIST-001`).
   - Category prefixes are short, stable, and domain-meaningful.
   - The numeric portion is zero-padded (e.g. `001`, `002`).
3. **No Renumbering or Reuse**: Once assigned, an identifier is never renumbered, renamed, or reused for another purpose, even if the item is completed, declined, or superseded.
4. **No Speculative Items**: Do not add speculative ideas or placeholder packages without explicit product definition and approval.
5. **Inactive Work Only**: Active work in flight is tracked operationally in [docs/CURRENT_WORK.md](CURRENT_WORK.md), not here.

---

## 2. Item Schema Definition

When items are accepted into the backlog, they are recorded with:
- **ID**: Unique canonical `MF-<CATEGORY>-<NUMBER>` identifier.
- **Title**: Descriptive title.
- **Type**: `Governance`, `Product`, `Architecture`, `Feature`, `Refactoring`, or `Documentation`.
- **Status**: `Proposed`, `Accepted`, `Deferred`, `Superseded`, or `Completed`.
- **Dependencies**: Prerequisites required before implementation.
- **Description**: Brief scope summary and acceptance criteria.

---

## 3. Current Inactive Backlog Registry


### MF-LEARN-002: Adaptive Pace, Fast Acquisition, and Practice Interventions

- **ID**: `MF-LEARN-002`
- **Title**: Adaptive Pace, Fast Acquisition, and Practice Interventions
- **Type**: `Feature`
- **Status**: `Completed` (Merged through PR #15 at `b2c5a43707167d5f6720d66834ebbbd3c6ede1d1`)
- **Dependencies**: `MF-STAB-001` complete (merged through PR #13)
- **Description**:
  Comprehensive adaptive learning model enhancement focusing on dual optimization of arithmetic correctness and retrieval speed:
  1. **Adaptive Pace & Deadlines**: Hierarchical shrinkage pace estimation ($P_0 = 4500\text{ ms}$; learner $W=12, N=30$; operation $W=8, N=20$; band $W=6, N=15$; fact $W=4, N=5$) clamped to $[600, 12000]\text{ ms}$. Exact-fact instability allowance adds $+1000\text{ ms}$ for Incorrect and $+1500\text{ ms}$ for Timeout across the last 5 attempts (clamped to $[0, 3000]\text{ ms}$). Answer deadlines are calculated as $\lceil (2 \cdot P_{\text{fact}} + \text{Allowance}) / 100 \rceil \cdot 100\text{ ms}$, clamped to $[3000, 30000]\text{ ms}$, with a cold baseline of $9000\text{ ms}$.
  2. **Adaptive Fluency & Ratings**: FSRS ratings adapt to fact pace: Easy $\le \text{clamp}(\lfloor 0.85 \cdot P_{\text{fact}} \rceil, 600, 2000)\text{ ms}$, Good $\le \text{clamp}(\lfloor 1.25 \cdot P_{\text{fact}} \rceil, 1500, 4000)\text{ ms}$, Hard $> \text{FluencyThreshold}$, and Again on error or timeout. Persisted `is_fluent` in Schema V6.
  3. **Fast Acquisition**: Dense bands with owned frontier size $N \in [1, 12]$ advance immediately when all $N$ facts achieve 100% Correct and raw response latency $\le 2000\text{ ms}$ on their first positioned encounter after band start, with a clean qualifying prefix within the phase-aware $N$-th requested-New role horizon. (Note: Fast Acquisition was subsequently retired by MF-LEARN-003 / ADR-0005).
  4. **Selector Model**: Role-specific selector chains (`Requested New`, `Requested Due`, `Requested Maintenance`, `Requested Frontier`) eliminate `AnyMaterialized`. `Early Review` ($\text{DuePracticePosition} > \text{prospectivePosition}$, not in remediation) acts as a strictly bounded liveness bridge. Cooldown relaxation occurs strictly within the selected semantic pool. Remediation priority override triggers when `NeedsRemediation == true` and $\text{prospectivePosition} \ge \text{LastReviewPracticePosition} + 4$.
  5. **Teaching Interventions**: Non-mutating canonical equation teaching overlay triggers on a second consecutive session error on the same exact `FactId`, requiring deliberate acknowledgement without generating attempt records, advancing Practice Position, or mutating FSRS card state.
  6. **Session Check-ins**: Periodic checkpoint occurs every 20 accepted attempts, presenting correct count and median latency of correct attempts only, offering "Keep Going" and "Take a Break" choices with guaranteed zero-timing pause semantics.
  7. **Clean Practice HUD**: Distraction-free practice screen with transient session score and progress indicators removed; danger-styled Pause button in red.

---

### MF-LEARN-003: Acclimation Timing, Rapid Dense Expansion, and Keypad Defaults

- **ID**: `MF-LEARN-003`
- **Title**: Acclimation Timing, Rapid Dense Expansion, and Keypad Defaults
- **Type**: `Feature`
- **Status**: `Completed` (Merged through PR #16 at `9a9e5c43d1f1685cf21e766e0890b790ab09040c`)
- **Dependencies**: `MF-LEARN-002` complete (merged through PR #15)
- **Description**:
  1. **Acclimation Timing & Deadlines**: Answer-length acclimation deadlines and entry allowances for unproven facts (`CorrectAttempts == 0`) with novelty floors (15s/20s/25s/30s).
  2. **Keypad Defaults**: Default to `Numpad` across Onboarding, Settings, and default UI state.
  3. **Coverage-First Dense Selection**: Scheduled turns prioritize unmaterialized owned-frontier facts until first-pass coverage is complete.
  4. **Latest-per-Frontier Persistence**: `LoadLatestFrontierAttemptsAsync` queries latest attempts per frontier fact, supported by partial index `ix_attempt_history_operation_fact_position`.
  5. **Correctness-Driven Dense Progression**: Dense bands advance when complete frontier coverage is met and $C \cdot 10 \ge N \cdot 9$ with recoverable errors; retirement of Fast Acquisition and the `MUL-D01` special bootstrap.
  6. **Editable Multi-Digit Input**: Incomplete multi-digit answers remain editable until full expected length is reached.

---

### MF-REL-001: Android Internal AAB Packaging and Release Automation

- **ID**: `MF-REL-001`
- **Title**: Android Internal AAB Packaging and Release Automation
- **Type**: `Feature`
- **Status**: `Completed` (Merged through PR #17 at `e1a0ae5a4a557f434f29ae04b7f8bcd32f3d2d88`)
- **Dependencies**: `MF-LEARN-003` complete and merged to `main`
- **Description**:
  Establish repeatable Android App Bundle (`.aab`) packaging, `tools/MathFirst.ReleaseTool` release architecture, keystore management protocols, exact-candidate provenance tracking (Schema v1), and offline local packaging validation harness for internal distribution and future testing readiness.

---

### MF-SET-001: Practice Configuration: Operation Selection and Adjustable Practice Time

- **ID**: `MF-SET-001`
- **Title**: Practice Configuration: Operation Selection and Adjustable Practice Time
- **Type**: `Feature`
- **Status**: `Accepted` (Implementation complete across 10 commits to final HEAD `f453501412b7c9fce39356a0837a5b862d6221fd`; reviewed `REVIEW_PASS`; 1037 Core tests passed; physical Android device validation confirmed on debug APK `MathFirst-MF-SET-001-f453501-debug.apk` SHA-256 `97f85e448d078e46d813aa1244e49b95933e51e1c9d64732faf3a614ae31db12`; `DOCUMENT_ONLY` active, next `COMMIT_ONLY`. Unpushed, no PR, not merged.)
- **Dependencies**: `MF-REL-001` complete and merged to `main`
- **Description**:
  Provide general user-configurable arithmetic practice options in Settings and Onboarding for learners who require customized operation focus or additional exercise time:
  1. **Operation Selection, Onboarding, and HUD**: Settings and Onboarding allow selecting any non-empty subset of the four arithmetic operations. Disabled operations are excluded from new practice and hidden from the operation progress HUD while their learner state remains preserved. A Settings change preserves the current question and applies to the next generated question.
  2. **Configurable Practice Time**: Settings provide Standard adaptive timing or explicit practice-time floors (30s, 45s, 60s) enforcing a minimum response deadline via $\max(\text{adaptiveDeadline}, \text{explicitFloor})$ without altering raw latency measurement, fluency thresholds, or FSRS ratings.
  3. **Learning Progress Continuity & Returning Overview**: Disabling an operation preserves all attempt history, item strength, FSRS state, and band progression for later reactivation. Returning learners with past practice view a progress overview on the readiness gate.
  4. **Schedule-Agnostic Persistence & Evidence Lifecycle**: `AdaptivePracticeSelector` owns scheduling; SQLite persistence remains schedule-policy agnostic. Selection evidence is scoped to operation and prospective Practice Position, required evidence loads on demand, disabled-operation prefetch is best effort, and recovery after a durable write is exactly once.
  5. **Answer Entry Reliability & Fact Instance Input Reset**: Incomplete multi-digit input remains editable via Backspace before complete submission; single-digit correct answers auto-submit; new problems start with clean empty buffers (enforced via fact instance revision and DOM element keying while preserving active exercise input).
  6. **Session Check-Ins & Timer Cleanup**: Periodic check-in every 20 accepted attempts displays completed count, correct count, stage progressions, and median latency of correct attempts only. Manual Pause, Settings, and same-process background time do not consume active answer time; cold restart resets only the in-flight timer. The developer Statistics / Diagnostics section is removed without deleting learner data.
  7. **Scope Boundary**: General practice configuration only. Child accounts, multi-user profiles, parental controls, age detection, and restricted child authentication remain separate future scope outside MF-SET-001.

---

> [!NOTE]
> Active work is tracked in [docs/CURRENT_WORK.md](CURRENT_WORK.md). High-level development phases and sequencing are outlined in [docs/ROADMAP.md](ROADMAP.md).
