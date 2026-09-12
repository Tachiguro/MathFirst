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
- **Status**: `Accepted` (In flight; active operational tracking in [docs/CURRENT_WORK.md](CURRENT_WORK.md))
- **Dependencies**: `MF-LEARN-003` complete and merged to `main`
- **Description**:
  Establish repeatable Android App Bundle (`.aab`) packaging, `tools/MathFirst.ReleaseTool` release architecture, keystore management protocols, exact-candidate provenance tracking (Schema v1), and offline local packaging validation harness for internal distribution and future testing readiness.

---

### MF-SET-001: Practice Configuration: Operation Selection and Adjustable Base Time

- **ID**: `MF-SET-001`
- **Title**: Practice Configuration: Operation Selection and Adjustable Base Time
- **Type**: `Feature`
- **Status**: `Proposed` (Inactive; planned next product package)
- **Dependencies**: `MF-REL-001` complete and merged to `main`
- **Description**:
  Provide user-configurable arithmetic practice options in Settings to make MathFirst accessible for children and learners who require customized operation focus or additional exercise time:
  1. **Operation Selection**: Settings allow individual arithmetic operations (Addition, Subtraction, Multiplication, Division) to be enabled or disabled. Disabled operations are excluded from new practice selection while disabled.
  2. **Configurable Base Exercise Time**: A configurable default/base exercise time is accessible in Settings for learners needing additional time.
  3. **Learning Progress Continuity**: Changing operation availability or base timing must not erase, reset, or alter existing learning progress. Disabled operations preserve all attempt history, item strength, FSRS state, and band progression for later reactivation.
  4. **Scope Boundary**: This package is inactive and Proposed until its own `PLAN_ONLY` lifecycle begins. Exact minimum/maximum time bounds, increment steps, default seconds, UI controls (slider vs. numeric), lock vs. validation messaging, adaptive pace interaction, parental controls, profiles/accounts, and gamification remain future `PLAN_ONLY` decisions.

---

> [!NOTE]
> Active work is tracked in [docs/CURRENT_WORK.md](CURRENT_WORK.md). High-level development phases and sequencing are outlined in [docs/ROADMAP.md](ROADMAP.md).
