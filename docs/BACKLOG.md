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
  1. **Operation Selection, Onboarding, and HUD**: Settings and Onboarding allow selecting any non-empty subset of the four arithmetic operations. Disabled operations are excluded from new practice and hidden from the operation progress HUD while their learner state remains preserved. (Note: The original MF-SET-001 behavior preserving unsubmitted questions unconditionally was refined by MF-STAB-003 / ADR-0008 to replace unsubmitted questions cleanly with zero learning mutations if their operation was disabled in Settings).
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
- **Status**: `Completed` (Merged through PR #38 at `d1705bbdc0013372e44eadf7310ff2f313ecdcef`; implementation and investigation scope complete)
- **Dependencies**: Native V1 Forensic Remediation complete (merged through PR #29 at `156d5afd32299ba19d8ca2a8f2f56a7babfe31d8`)
- **Description**:
  Comprehensive native UX, layout responsiveness, and interaction polish package. Established authoritative decisions registry:
  1. **Answer Submission**: Keep current single-digit auto-submit behavior. No confirmation button, no Enter-only submission, no grace period, no correction delay, no separate Learning/Sprint mode. Backspace remains for partial multi-digit editing.
  2. **`(Enter)` Button Copy**: Keep current `(Enter)` labels on Continue / Keep Going actions.
  3. **Pause Button Color**: Implemented in Slice 3 (PR #30) — amber/yellow non-destructive styling (`.button-pause`) with accessible contrast in Light and Dark modes.
  4. **No Time Pressure Mode**: Implemented in Slice 5 (PR #30) — Practice Time setting measuring and persisting active latency without deadlines or automatic timeouts, displaying count-up elapsed time, preserving standard adaptive pace calculations, and supporting full localization.
  5. **Timer Typography**: The translucent Timer pill introduced by Slice 3 (PR #30) is historical and was superseded by PR #38. Current Timer text uses bold white tabular numerals with a restrained local dark shadow/contour, no backing pill, and no heavy text stroke; countdown/progress behavior is unchanged, and No Time Pressure keeps a visible elapsed count-up.
  6. **Practice Vertical Layout**: No forced compression; keypad anchored toward bottom; normal vertical whitespace acceptable.
  7. **Didactic Tips / Visual Math Explanations**: Rejected for current scope (no zero-rule hints, mnemonic tips, ten-frames, or per-fact explanations).
  8. **Error Remediation Spacing**: Keep current spaced in-session remediation (`LearningPolicy.RemediationInterveningCount = 3`).
  9. **Repeated-Error Teaching Lock**: Implemented in Slice 3 (PR #30) — 3-second visible lockout with localized countdown feedback on teaching intervention modal before Continue enables.
  10. **Haptic Feedback**: Implemented in Slice 4 (PR #30) — supported cues are exactly `KeyTap`, `Correct`, `Incorrect`, and `Timeout`; default enabled, locally persisted, configurable in Settings and Onboarding, and safely no-op on unsupported platforms.
  11. **Streak Feedback**: Implemented in Slice 6 (PR #30) — restrained textual session indicator only, visible from streak $\ge 3$, with no flame icon or animation and no learning mutation.
  12. **Confirmation / Learning Mode**: Rejected — auto-submit remains authoritative.
  13. **Pause Information**: Implemented in Slice 6 (PR #30) — lightweight current-session metrics on Pause overlay (Completed, Correct, Current streak, Median correct latency).
  14. **Startup White Flash**: Implemented in Slice 7 (PR #31) — neutral brand-continuity startup handoff (#176B4D native splash -> #176B4D Android WebView canvas -> #176B4D static HTML surface -> first rendered Light/Dark Blazor UI), empty app root container, no localStorage theme duplication; physical hardware verification pending.
  15. **Installed Size / App Data / RAM**: Investigation completed with evidence limitations — exact internal App Data attribution and current Play delivery size were not available; RAM verdict is `NO_CLEAR_LEAK_SIGNAL`, not proof that no leak can exist.
  16. **Tester Ergonomics & Workflow**: Completed — runtime build identity metadata (PR #33), safe copyable diagnostics / Settings UX (PR #34), and repeatable Tester APK workflow through `MathFirst.ReleaseTool` plus `scripts/package-android-tester-apk.ps1` and `scripts/validate-android-apk.ps1` (PR #36).
  17. **KnownFirst-Style Onboarding Action Layout**: Implemented in Slice 2 (PR #30) — vertically stacked actions with primary on top, Back below, 5-step flow intact, no Skip shortcut, draft state preserved.
  18. **Android Back Behavior**: Implemented in Slice 2 (PR #30) — `IAppBackNavigationCoordinator` handling Onboarding steps 2–5 back navigation, Onboarding step 1 safe background pass-through, Settings return to `/` with question/input/timing preservation, and Root Practice safe background pass-through with timing freeze.
  19. **Release Startup Resource Order Fix**: Implemented and merged via PR #32 (`cf1d2a3f4779c16f1c6104fe8736f405eb14d94e`) — resolved Android Release startup `XamlParseException` by deferring `MainPage` resolution until `CreateWindow()`.
  20. **Release Size Hygiene**: Implemented and merged via PR #37 (`dbc6bf045454a78a1ba32793fa02245f83b50437`) — excludes `wwwroot\lib\bootstrap\dist\css\bootstrap.min.css.map` only for Release packaging.
  21. **Timer Visual Remediation**: Implemented and merged via PR #38 (`d1705bbdc0013372e44eadf7310ff2f313ecdcef`) — removed the backing pill and introduced the current restrained Timer text treatment, explicitly accepted in a Timer-specific physical `TEST_ONLY` run.

---

### MF-UX-006: V1 Privacy, Copy, and Localization Hardening

- **ID**: `MF-UX-006`
- **Title**: V1 Privacy, Copy, and Localization Hardening
- **Type**: `Feature`
- **Status**: `Completed` (Merged through PR #40 at `ae69f4ae27397fc6edf36a23bb671b0410680be1`)
- **Dependencies**: `MF-UX-005` complete (merged through PR #39)
- **Description**:
  Hardening of V1 privacy architecture, localization copy, and fallback surfaces:
  1. **Reset Copy & Terminology Polish**: Restore Default Settings copy across English, German, and Russian explicitly specifies PC numpad (`Keypad_Numpad`) default layout; resolved duplicate punctuation in Russian haptic feedback confirmation; corrected Russian HUD accessibility progression terminology (`Стадия прогресса`); added format token and key parity contracts.
  2. **Offline In-App Privacy Surface**: Added dedicated offline Blazor component at `/privacy` (`src/MathFirst.App/Components/Pages/Privacy.razor`) structured across Overview, No Remote Collection or Sharing, Local Storage and Device Transfer, Removing Local Data, and Contact sections; added Settings privacy entry card with description and action; integrated `IAppBackNavigationCoordinator` handling system Back from `/privacy` to `/settings`; complete EN/DE/RU localization dictionaries; zero-network permissions preserved.
  3. **Fatal Host Fallback Hardening**: Replaced English-only fatal host error text in `src/MathFirst.App/wwwroot/index.html` with language-neutral error title and static multilingual reload links in English (`Reload`), German (`Neu laden`), and Russian (`Перезагрузить`), eliminating runtime localization dependencies while preserving `#176B4D` startup handoff and dark mode CSS styling.

---

### MF-DOC-005: Post-MF-UX-006 Merge State Reconciliation

- **ID**: `MF-DOC-005`
- **Title**: Post-MF-UX-006 Merge State Reconciliation
- **Type**: `Documentation`
- **Status**: `Completed` (Merged through PR #41 at `284d7cf2c50be6e2d4f219c00aa20d92387338f9`)
- **Dependencies**: `MF-UX-006` complete (merged through PR #40)
- **Description**:
  Reconcile repository documentation following the merge of PR #40 across `CHANGELOG.md`, `docs/CURRENT_WORK.md`, `docs/PROJECT_STATE.md`, `docs/NEW_CHAT_BOOTSTRAP.md`, `docs/BACKLOG.md`, and `docs/ROADMAP.md` to synchronize baseline state on `main`.

---

### MF-STAB-003: Enabled-Subset Scheduling and Current-Fact Reconciliation

- **ID**: `MF-STAB-003`
- **Title**: Enabled-Subset Scheduling and Current-Fact Reconciliation
- **Type**: `Feature`
- **Status**: `Completed` (Merged through PR #42 at `caffe0e883f83249bee2c9a1f2122543e88c9ab0`)
- **Dependencies**: `MF-DOC-005` complete on `main`
- **Description**:
  Resolve practice selection crash/starvation and configuration reconciliation defects when learners dynamically adjust enabled arithmetic operations:
  1. **Independent Per-Operation Role Ordinals**: Practice selection role schedules (`New`, `Due`, `Maintenance`, `Frontier`) are derived strictly from per-operation attempt counts via `NextOperationAttemptOrdinal(O) = AcceptedAttemptCount(O) + 1` across the repeating 10-slot cycle (1 New, 2 Due, 3 New, 4 Maintenance, 5 Frontier, 6 New, 7 Due, 8 New, 9 Due, 10 Frontier; partition $\{0, 2, 5, 7\} \to \text{New}$, $\{1, 6, 8\} \to \text{Due}$, $\{3\} \to \text{Maintenance}$, $\{4, 9\} \to \text{Frontier}$ for $i = (\text{ordinal} - 1) \bmod 10$) ([ADR-0008](decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)), decoupling role progression from global `PracticePosition`. Per-operation count is authoritative strictly for role-cycle ordinals.
  2. **Global Practice Position Authority**: Global `PracticePosition` retains authority for attempt sequencing, bounded permutation bag operation scheduling, FSRS virtual time distance, and database persistence transaction boundaries. Existing coverage, pace, fluency, progression, and item-state authorities remain untouched.
  3. **Durable Schema V6 Count Reconstruction**: Operation attempt counts are reconstructed from `SUM(item_learning_state.total_attempts WHERE operation = O)` upon session initialization, avoiding SQLite schema changes or migrations.
  4. **Settings & Current-Fact Reconciliation**: `TrainingSession.ReconcilePracticeConfigurationAsync` provides deterministic reconciliation when Settings changes active operations:
     - **Case A & C (Invalid unsubmitted fact)**: Immediately discarded and replaced with a valid fact for the same prospective `PracticePosition`. Strictly zero learning mutations created (no attempt record, no timeout, no score mutation, no FSRS mutation, no progression mutation, no attempt count increment);
     - **Case B (Valid unsubmitted fact)**: Retained with exact identity, `FactInstanceRevision`, partial input, and remaining timer state preserved;
     - **Accepted Feedback Deferral**: If the active exercise has already accepted submission feedback, reconciliation is deferred until next question preparation, ensuring learner feedback is never erased.
  5. **Awaited Settings Navigation**: `Settings.razor` awaits session reconciliation before navigating back to practice.
  6. **Release Blocker Resolution**: Fixes the physical device blocker (Build 2 rejected at Step 31) where practicing Addition, disabling Addition in favor of Subtraction, and restarting crashed or starved the selector in `PracticeSelectionRole.Due`. Verified with 1,516 Core tests. Validated candidate `766d8ea7692d139425e2301121f93af7901cf238` achieved `FULL_VALIDATION_PASS` and exact tree identity with merge commit `fcab56b3a886ed0c5018d4f8a16304ee83378b26`.

---

### MF-LEARN-004: Guided Four-Operation Number-Space Gate

- **ID**: `MF-LEARN-004`
- **Title**: Guided Four-Operation Number-Space Gate
- **Type**: `Feature`
- **Status**: `Completed` (Merged through PR #44 at `8f4ae59110abf6ea9d365733297a0c15d4c296ea`)
- **Dependencies**: `MF-STAB-003` complete and merged to `main` (PR #42)
- **Description**:
  Provide guided onboarding and progression gating across arithmetic number spaces, ensuring multiplicative quantities remain grounded in the learner's established additive number space when practicing multi-operation arithmetic ([ADR-0009](decisions/ADR-0009-guided-four-operation-number-space-gate.md)):
  1. **Guided vs. Custom Mode**: Cross-operation number-space gating is active iff exactly all four operations are enabled (`Guided Mode`). Any other non-empty subset (`Custom Mode`) operates unrestricted without an Addition ceiling, allowing targeted single- or multi-operation practice without artificial constraints.
  2. **Addition-Governed Multiplicative Number Space**: In Guided Mode, the maximum represented number across the complete unlocked canonical Addition curriculum prefix ($0..\text{BandIndex}_{\text{ADD}}$) defines the ceiling. Multiplication presentation requires $\text{CorrectResult} \le \text{AdditionCeiling}$ and Division requires $\text{LeftOperand} \le \text{AdditionCeiling}$. Addition and Subtraction remain invariant under cross-operation gating.
  3. **State & Schema Preservation**: The gate governs presentation eligibility only (`PERSISTED != CURRENTLY PRESENTABLE`). All attempt history, item strength, FSRS card states, band progression indices, accepted attempt counts, and global `PracticePosition` are preserved losslessly in Schema V6 without migration. Dormant facts automatically regain presentation eligibility when the Addition ceiling expands or when switching to Custom Mode.
  4. **Settings & Current-Fact Reconciliation**: Integrates with MF-STAB-003 reconciliation: unsubmitted active questions that become Guided-ineligible are cleanly replaced at the same prospective `PracticePosition` with zero learning mutations; eligible questions retain exact identity, partial answer input, and paused timer state; accepted feedback is preserved until deliberate dismissal.
  5. **Defense-in-Depth & Anti-Poisoning**: Streaming SQLite candidate filtering in `SqliteLearnerStore.ReadCandidateRowsAsync` excludes Guided-ineligible facts before candidate partitioning and window truncation, ensuring dormant higher-number facts do not consume any of the bounded 64 candidate window slots. Reinforced in snapshot evidence, selector candidate pools, and final selection assertions.
  6. **Evidence Cache Semantic Identity**: In-memory selection evidence cache incorporates semantic gate identity (`GateIdentity(bool IsActive, int? AdditionCeiling)`), ensuring cache entries invalidate and refresh when the Addition ceiling expands while treating Addition and Subtraction as gate-invariant.
  7. **Scheduler & Role Invariants**: Bounded permutation bag operation turn scheduling via `DeterministicOperationScheduler` remains unchanged (25% nominal turn share per operation in all-four mode); per-operation role progression remains derived strictly from $\text{AcceptedAttemptCount}(O) + 1$ across the 10-slot cycle.
  8. **Test-Helper Maintenance Resolution**: Resolved the historical MF-STAB-003 test-helper maintenance item in `tests/MathFirst.Core.Tests/BoundedSelectionIntegrationTests.cs`, aligning test-helper role-ordinal derivation with per-operation attempt counts.
  9. **Delivery & Lifecycle Status**: Implementation completed across 4 checkpoint commits on task branch `codex/mf-learn-004-guided-number-space-gate` at checkpoint `c962c33fe62edd633bcae003728391bec502e7eb` (base `a9d232f78e2f9ebcbc431bd18109a0aeb08a9303`). Verified `REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 2 Note) with 1,590 passing Core tests in `MathFirst.Core.Tests` (0 failed, 0 skipped). Validated candidate `51a2bd9907ebdcf738a348bc90de29d31d6b68b6` achieved `FULL_VALIDATION_PASS` and exact tree identity (`053311fed6a6827af690a6138fd83cc2c63bb7de`) with merge commit `8f4ae59110abf6ea9d365733297a0c15d4c296ea` via PR #44. Build 4 does not exist yet.

---

### MF-UX-007: Progress Presentation Cleanup

- **ID**: `MF-UX-007`
- **Title**: Progress Presentation Cleanup
- **Type**: `Feature`
- **Status**: `Completed` (Merged through PR #47 at `d37fbe3347679220bf847b06c83f7f9366738d03`)
- **Dependencies**: `MF-LEARN-004` complete (merged through PR #44)
- **Description**:
  Refine and polish learner-facing progress HUD, returning learner overview displays, and operation stage metrics across supported viewports and languages.

---

### MF-DOC-008: Post-MF-UX-007 Merge State Reconciliation

- **ID**: `MF-DOC-008`
- **Title**: Post-MF-UX-007 Merge State Reconciliation
- **Type**: `Documentation`
- **Status**: `Active`
- **Dependencies**: `MF-UX-007` complete and merged through PR #47
- **Description**:
  Reconcile repository baseline documentation following the MF-UX-007 merge so operational, roadmap, backlog, changelog, bootstrap, and durable project-state documentation match live main.

---

> [!NOTE]
> Active work is tracked in [docs/CURRENT_WORK.md](CURRENT_WORK.md). High-level development phases and sequencing are outlined in [docs/ROADMAP.md](ROADMAP.md).
