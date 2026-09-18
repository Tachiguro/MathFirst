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
  3. **Coverage-First Dense Selection**: Scheduled turns prioritize unmaterialized owned-frontier facts until first-pass coverage is complete (subsequently refined by MF-STAB-002 to restore requested-role review authority).
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
- **Status**: `Completed` (Merged through PR #18 at `a051518420db3b45f8ca1074bac27e9b4d1b799d`)
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

### MF-DOC-003: Post-Merge Project State and V1 Baseline Reconciliation

- **ID**: `MF-DOC-003`
- **Title**: Post-Merge Project State and V1 Baseline Reconciliation
- **Type**: `Documentation`
- **Status**: `Completed` (Merged through PR #19 at `6e137471a16ffced5f5a62daa1a3f5143b5bbb7a`)
- **Dependencies**: `MF-SET-001` complete and merged to `main`
- **Description**:
  Reconcile repository documentation after the completed merge of `MF-SET-001` across `CHANGELOG.md`, `docs/BACKLOG.md`, `docs/CURRENT_WORK.md`, `docs/NEW_CHAT_BOOTSTRAP.md`, `docs/PROJECT_STATE.md`, and `docs/ROADMAP.md` to establish an accurate documentation baseline on `main` before downstream work.

---

### MathFirst Privacy Policy Documentation

- **ID**: `PRIVACY.md`
- **Title**: MathFirst Standalone Privacy Policy
- **Type**: `Documentation`
- **Status**: `Completed` (Merged through PR #20 at `c4ae75a99f7971033ca9887bb2277b217979be03`)
- **Dependencies**: None
- **Description**:
  Establish standalone offline privacy policy documentation in `PRIVACY.md` satisfying Google Play Store policy requirements for offline apps with zero network data collection.

---

### MF-STAB-002: Deterministic Practice Selection Diversity, Bounded Operation Scheduling, and Adaptive Review Balance

- **ID**: `MF-STAB-002`
- **Title**: Deterministic Practice Selection Diversity, Bounded Operation Scheduling, and Adaptive Review Balance
- **Type**: `Feature`
- **Status**: `Completed` (Merged across Slices 1–3 through PR #21, PR #22, and PR #23 at `2d59464fd3de8cefa5b0f404c00e9bcc74c98dc9`)
- **Dependencies**: `MF-DOC-003` complete and merged to `main`
- **Description**:
  Stabilize practice selection diversity, multi-operation turn distribution, and review balance across three focused implementation slices:
  1. **Slice 1 (Practice Diversity)** (PR #21 at `7cc6caebec1798d5cfb3c48172b6f78360fb2442`): Deterministic fact candidate ranking, anti-ladder candidate selection when alternatives exist, preserved exact (3-fact) and commutative mirror (3-fact) cooldown semantics, and deterministic role-specific ranking domains.
  2. **Slice 2 (Bounded Operation Scheduling)** (PR #22 at `33d745d270745c2de65e47519cb98959278bbcbd`): Deterministic bounded operation permutation bags ensuring every enabled operation appears exactly once per bag without RNG or persisted scheduler state; enabled-operation order independence; possible same-operation adjacency across bag boundaries.
  3. **Slice 3 (Review Stabilization)** (PR #23 at `2d59464fd3de8cefa5b0f404c00e9bcc74c98dc9`): Restore requested-role authority during Dense acquisition, bounding New introductions to the 4 requested New opportunities per 10 per-operation attempts while Due, Maintenance, and Frontier provide review/retention opportunities without global suppression by unseen Dense material; preserved remediation priority, FSRS virtual time, persistence, and progression rules; verified with 1074 passing Core tests.

---

### MF-DOC-004: Post-Stabilization Project State Reconciliation

- **ID**: `MF-DOC-004`
- **Title**: Post-Stabilization Project State Reconciliation
- **Type**: `Documentation`
- **Status**: `Completed` (Merged through PR #24 at `3e471e20e2d452ee1e383579ed3d0831e1825d3e`)
- **Dependencies**: `MF-STAB-002` complete (merged through PR #23)
- **Description**:
  Reconcile repository documentation after the completed merge of `MF-DOC-003` (PR #19), MathFirst Privacy Policy (PR #20), and `MF-STAB-002` (Slices 1–3 through PR #21, PR #22, and PR #23 at `2d59464fd3de8cefa5b0f404c00e9bcc74c98dc9`) across `CHANGELOG.md`, `docs/BACKLOG.md`, `docs/CURRENT_WORK.md`, `docs/NEW_CHAT_BOOTSTRAP.md`, `docs/PROJECT_STATE.md`, and `docs/ROADMAP.md` to establish an accurate documentation baseline before downstream UX and release work.

---

### MF-UX-004: Keypad Press Feedback and Responsive Validation

- **ID**: `MF-UX-004`
- **Title**: Keypad Press Feedback and Responsive Validation
- **Type**: `Feature`
- **Status**: `Completed` (Merged through PR #25 at `46a7158d3c7fbdf6bc43fe120c35863ac55bb78b`)
- **Dependencies**: `MF-STAB-002` and `MF-DOC-004` complete
- **Description**:
  Refine numeric keypad visual feedback on active press states and perform responsive layout validation across narrow and wide viewport dimensions. Implemented CSS active-press visual styling for enabled keypad buttons, preserved `:focus-visible` outline contract and disabled button opacity, and validated responsive layout contracts across viewports without introducing JavaScript or Blazor pointer-state machinery.

---

### MF-REL-002: Tester Distribution / Release Hardening

- **ID**: `MF-REL-002`
- **Title**: Tester Distribution / Release Hardening
- **Type**: `Feature`
- **Status**: `Completed` (Merged through PR #26 at `79e0d48c058747e8112388d31d721a049ec2857a`)
- **Dependencies**: `MF-UX-004` complete
- **Description**:
  Provide tester distribution workflows, verify release build profiles, and harden release verification artifacts prior to final candidate validation and packaging. Generalized `SourceCandidate` packaging policy to accept any clean attached non-`main` branch; established strict validator-approved five-file evidence promotion (`<ArtifactId>.aab`, `<ArtifactId>.provenance.json`, `<ArtifactId>.validation.json`, `TESTER_README.md`, `SHA256SUMS`); bound immutable provenance bytes; implemented ValidationReceipt Schema v1; added deterministic `TESTER_README.md` generation; defined exact `SHA256SUMS` contract; characterized release build profiles including unpackaged Windows release properties; and confirmed no production signing, Google Play upload, or physical device verification was performed.

---

### MF-UX-005: Native UX, Responsiveness, and Interaction Polish

- **ID**: `MF-UX-005`
- **Title**: Native UX, Responsiveness, and Interaction Polish
- **Type**: `Feature`
- **Status**: `Active` (In flight across iterative implementation slices; operational tracking in [docs/CURRENT_WORK.md](CURRENT_WORK.md))
- **Dependencies**: Native V1 Forensic Remediation complete (merged through PR #28)
- **Description**:
  Comprehensive native UX, layout responsiveness, and interaction polish package. Established authoritative decisions registry:
  1. **Answer Submission**: Keep current single-digit auto-submit behavior. No confirmation button, no Enter-only submission, no grace period, no correction delay, no separate Learning/Sprint mode. Backspace remains for partial multi-digit editing.
  2. **`(Enter)` Button Copy**: Keep current `(Enter)` labels on Continue / Keep Going actions.
  3. **Pause Button Color**: Implemented in Slice 3 — amber/yellow non-destructive styling (`.button-pause`) with accessible contrast in Light and Dark modes.
  4. **No Time Pressure Mode**: Implemented in Slice 5 — Practice Time setting measuring and persisting active latency without deadlines or automatic timeouts, displaying count-up elapsed time, preserving standard adaptive pace calculations, and supporting full localization.
  5. **Timer Typography**: Implemented in Slice 3 — removed text stroke in favor of clean bold sans-serif with subtle translucent pill contrast backing.
  6. **Practice Vertical Layout**: No forced compression; keypad anchored toward bottom; normal vertical whitespace acceptable.
  7. **Didactic Tips / Visual Math Explanations**: Rejected for current scope (no zero-rule hints, mnemonic tips, ten-frames, or per-fact explanations).
  8. **Error Remediation Spacing**: Keep current spaced in-session remediation (`LearningPolicy.RemediationInterveningCount = 3`).
  9. **Repeated-Error Teaching Lock**: Implemented in Slice 3 — 3-second visible lockout with localized countdown feedback on teaching intervention modal before Continue enables.
  10. **Haptic Feedback**: Implemented in Slice 4 — distinguishable tactile feedback for KeyTap (`Click`), Correct (`40ms pulse`), and Incorrect/Timeout (`120ms pulse`); default enabled, local persisted preference, Settings On/Off toggle with preview, Onboarding Step 2 integration without adding a sixth step, restored to enabled on Restore Defaults and Full Local Reset, preserved on Reset Learning Progress, unsupported platforms safely no-op; physical tactile-quality verification remains pending on Android hardware.
  11. **Streak Feedback**: Implemented in Slice 6 — positive, age-neutral consecutive correct streak feedback without gamified pressure or learning mutations (visible at $\ge 3$).
  12. **Confirmation / Learning Mode**: Rejected — auto-submit remains authoritative.
  13. **Pause Information**: Implemented in Slice 6 — lightweight current-session metrics on Pause overlay (Completed, Correct, Current streak, Median correct latency).
  14. **Startup White Flash**: Implemented in Slice 7 — neutral brand-continuity startup handoff (#176B4D native splash -> #176B4D Android WebView canvas -> #176B4D static HTML surface -> first rendered Light/Dark Blazor UI), empty app root container, no localStorage theme duplication, physical verification pending.
  15. **Installed Size / App Data**: Accepted investigation (pending later slice) — analysis of debug vs release payloads, native libs, WebView runtime, SQLite storage.
  16. **Tester Ergonomics**: Pending scope (build identity, diagnostic copy, repeatable tester artifacts).
  17. **KnownFirst-Style Onboarding Action Layout**: Implemented in Slice 2 — vertically stacked actions with primary on top, Back below, 5-step flow intact, no Skip shortcut, draft state preserved.
  18. **Android Back Behavior**: Implemented in Slice 2 — `IAppBackNavigationCoordinator` handling Onboarding steps 2–5 back navigation, Onboarding step 1 safe background pass-through, Settings return to `/` with question/input/timing preservation, and Root Practice safe background pass-through with timing freeze.

---

> [!NOTE]
> Active work is tracked in [docs/CURRENT_WORK.md](CURRENT_WORK.md). High-level development phases and sequencing are outlined in [docs/ROADMAP.md](ROADMAP.md).
