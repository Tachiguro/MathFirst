# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: `P1-PR61-DOCUMENTATION-RECONCILIATION` (P1 PR #61 Integration Documentation Reconciliation).
- **Active Package**: `P1`: Normal Practice Without Deadline Failure.
- **Active Integration Vehicle**: Pull Request [#61](https://github.com/Tachiguro/MathFirst/pull/61) (`feat(practice): remove deadline failure from normal practice`).
- **Branch / Upstream State**: Task branch `feat/p1-no-deadline-failure` based on `main` (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`).
- **Implementation & Integration Milestones**:
  - Slice 1: `e58ea6b6f210b367afce48720a87cda4272b4e83` (`feat(practice): grade late normal-practice submissions mathematically`)
  - Slice 2: `aa3ae267f2615671ea1a9a320ccf8c403505ea97` (`feat(practice): disable automatic normal-practice deadlines`)
  - Initial Documentation Reconciliation: `e1ad6914650560e5a586531d213f5becad51580b` (`docs: reconcile P1 completion state`)
  - Full Validation: `P1_FULL_VALIDATION_PASSED` executed at `e1ad6914650560e5a586531d213f5becad51580b`
  - Push: `P1_PUSH_COMPLETE` (branch `feat/p1-no-deadline-failure` pushed to `origin`)
  - Pull Request: `P1_PR_COMPLETE` (opened PR #61 targeting `main`)
- **Live State Discovery**: Live local Git and GitHub repository state always takes precedence over documentation. The live status, review state, mergeability, CI/checks, and merge state must be discovered dynamically from:
  - `git status`
  - `git rev-parse HEAD`
  - `git fetch origin`
  - `gh repo view Tachiguro/MathFirst`
  - `gh pr view 61`
  - `gh pr list --state open`
- **Status of Active Work**:
  - **P1 Implementation Status**: Complete across Slice 1 and Slice 2 implementation checkpoints.
  - **P1 Package Review Verdict**: `P1_REVIEW_APPROVED_WITH_DOCS` (zero CRITICAL, zero IMPORTANT findings; implementation package-complete; 1,936 Core tests independently verified passing).
  - **Durable P1 Behavioral Contract**:
    1. Normal practice no longer fails academically solely because wall-clock time elapsed.
    2. A learner may continue calculating and answering beyond former deadline thresholds without question termination or timeout penalty.
    3. Submitted answers are graded strictly by mathematical correctness.
    4. Late correct answers are recorded as `AttemptOutcome.Correct`, `IsCorrect = true`, authentic `ResponseLatencyMs` retained, `IsFluent = false` (when latency exceeds fluency threshold), and rated FSRS `Hard`.
    5. Late incorrect answers are recorded as `AttemptOutcome.Incorrect`, authentic `ResponseLatencyMs` and submitted wrong answer value retained, triggering standard remediation and FSRS `Again`.
    6. Normal practice has no enforced deadline across all `PracticeTimeSetting` values (`HasEnforcedDeadline = false`).
    7. Clock passage alone creates no attempt, commits no learner evidence, advances no `PracticePosition`, advances no fact, clears no partial answer input, and generates no timeout.
    8. Normal practice UI (`Home.razor`) no longer wires the automatic timeout callback.
    9. `PracticeCountdownTimer` remains generic for future timed modes but cannot auto-timeout normal practice because `HasEnforcedDeadline` is false.
    10. Historical and explicit compatibility `AttemptOutcome.Timeout` semantics remain supported (e.g. legacy records, telemetry deserialization, synthetic compatibility fixtures).
    11. New normal-practice attempts record `PresentedDeadlineMs = null` in Schema V7 presentation context.
    12. No SQLite schema migration was introduced; selector/P0 internals were untouched.
    13. P1b interruption-aware timing, P4 Settings simplification, and Boss/combat timers are not implemented in P1.
    14. Existing Practice Time Settings controls remain as accepted transitional UX debt pending P4.
  - **Verification Evidence on Task Branch**:
    - Implementation / Package Review Evidence: 1,936 Core tests passed, 0 failed, 0 skipped.
    - Full Validation Evidence (executed at candidate commit `e1ad6914650560e5a586531d213f5becad51580b`):
      - Complete Core test suite: 1,936 passed, 0 failed, 0 skipped.
      - Focused P1 test suite: 244 passed, 0 failed, 0 skipped.
      - P0 selector regression suite: 144 passed, 0 failed, 0 skipped.
      - Persistence & telemetry focused suite: 43 passed, 0 failed, 0 skipped.
      - Windows Release build: 0 warnings, 0 errors.
      - Android Release compilation: `net10.0-android36.0` Release compile-only (Target `Compile`) SUCCESS (0 warnings, 0 errors).
      - Whitespace & formatting check (`git diff --check`): clean.
      - Tracked working tree: clean after validation.
      - Step 55 & Packaging Boundary: Zero APK/AAB packaging performed; zero signing; zero deployment; zero ADB/device/emulator actions; no repository artifacts introduced.
      - Note: Subsequent documentation-only integration reconciliation does not alter production/test behavior; production builds were not re-executed for documentation-only edits.
  - **Integration & Procedural State**:
    - P1 is in repository integration via PR #61 on branch `feat/p1-no-deadline-failure`.
    - Live PR review, mergeability, CI/check, and merge state must be discovered dynamically from GitHub.
    - Subsequent lifecycle steps: push reconciled documentation (`PUSH_ONLY`), PR review / integration decision, and user merge decision.
    - Merging PR #61 requires explicit affirmative user authorization; auto-merge is not authorized.
    - Merge is NOT authorized by this `DOCUMENT_ONLY` task.
  - **Prior Work (P0)**: P0 (Zero-Answer / `0 + 0` Core-Flow Freeze, selector terminal-liveness starvation) is complete, fully validated, and merged into `main` via PR #60 at commit `4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`.
  - **Downstream Scope**: P1b (Active Thinking Time / Interruption Safety) and P4 (Settings Simplification) are downstream work and are **NOT** authorized by completing P1.
  - **Roadmap Step 55 Status**: **NOT EXECUTED / NOT AUTHORIZED** (explicitly declined by user after Step 54; deferred pending pre-Step55 refinement program; no production AAB, no versionCode 4, no release signing, no Google Play upload).
  - **Completed Testing Steps (Steps 51–54)**:
    - **Step 51 (Final V1 Gap Audit)**: `STEP_51_READY_FOR_STEP_52` (read-only audit of `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f`, zero Step-52 blockers found).
    - **Step 52 (Tester APK Packaging & Offline Validation)**: `STEP_52_TESTER_APK_PASS` (built from `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f`, package `com.tachiguro.mathfirst.tester`, version `1.0 / versionCode 1`, APK SHA-256 `40e2b5e3f23e90a2dbe695db4d724e4375c5f2ef51709a801fa81cf6e06f4039`, development/debug signed, repository remained clean).
    - **Step 53 (Physical Installation on S26 Ultra)**: `STEP_53_INSTALL_PASS` (Samsung SM-S948B / m3q, Android 16 / API 36, package `com.tachiguro.mathfirst.tester`, version `1.0 / versionCode 1`, production package unmodified).
    - **Step 54 (Manual Physical-Device Validation)**: `STEP_54_MANUAL_VALIDATION_PASS` (physical device Samsung Galaxy S26 Ultra; manual validation covered launch, practice, correctness, pause/resume, Cyber Defense combat presentation, multi-question continuity, background/resume, Settings, Privacy, telemetry export/share, localization; zero manual findings reported; source repository unchanged).
  - **Established Refinement Plan**: Canonical pre-production program documented in [docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md).

### 1.1 Pre-Step55 Refinement Program Sequence

The canonical pre-production program ([docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md)) governs downstream work:
1. **P0**: Zero-Answer / `0 + 0` Core-Flow Freeze (`P0 Blocker` — implemented, fully validated, and merged into `main` via PR #60 at `4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`)
2. **P1**: Normal Practice Without Deadline Failure (`Active Package` — implemented across checkpoints `e58ea6b6` and `aa3ae267`, review passed `P1_REVIEW_APPROVED_WITH_DOCS`, fully validated `P1_FULL_VALIDATION_PASSED` at `e1ad6914`, repository integration tracked via PR #61)
3. **P1b**: Active Thinking Time / Interruption Safety (pause on interruptions, neutralize contaminated latency — *Downstream*)
4. **P2**: Direct-to-Practice Start / Remove Onboarding (direct launch, system defaults, Addition only — *Downstream*)
5. **P3**: Cumulative Operation Unlock Progression ($+ \to + - \to + - \times \to + - \times \div$; demonstrated mathematical evidence, 3 simulation personas — *Downstream*)
6. **P4**: Settings Simplification (streamlined settings, remove practice time selection, prevent unlock bypass — *Downstream*)
7. **P5**: Cyber Defense Visual Consistency (dark technical surfaces, restrained neon/cyber accents — *Downstream*)
8. **P6**: Tester Diagnostics / Telemetry Release Boundary (hard compile/profile boundary isolating Tester diagnostic controls from Distributable UI — *Downstream*)
9. **P8**: Test-Coverage Audit & Targeted Hardening (factual baseline, critical invariant coverage — *Downstream*)
10. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*)

### 1.2 Current Verified Quality State

#### Merged Main Baseline (`main@4df7a5f` / PR #60 `P0`)
- **Merge Commit**: `4df7a5f4c8230700b427f0c1d9ebdabfcd98d823` (PR #60 `fix: preserve selector liveness with terminal new fallback`).
- **Post-Merge Verification**: Resolved selector terminal-liveness starvation at StoreRevision 5 via generic terminal New fallback in `AdaptivePracticeSelector.GetFallbackChain`; 1,917 Core tests passing, clean Windows Release build, clean Android Release compilation (Target `Compile`).
- **Historical Merged Baselines**: PR #56 `MF-TELEM-001` (Schema V7, 1,906 Core tests), PR #55 `MF-UX-008` (Static combat layout, 1,786 Core tests), PR #54 `MF-LEARN-006` (Adaptive learning policy, 482 benchmark, 1,772 Core tests).

#### Verified P1 Quality Evidence (`feat/p1-no-deadline-failure` / PR #61)
- **Package Review Verdict**: `P1_REVIEW_APPROVED_WITH_DOCS` (zero CRITICAL, zero IMPORTANT findings, implementation complete; 1,936 Core tests independently verified passing).
- **Implementation Checkpoints**:
  - Slice 1 (`e58ea6b6f210b367afce48720a87cda4272b4e83`): `feat(practice): grade late normal-practice submissions mathematically`.
  - Slice 2 (`aa3ae267f2615671ea1a9a320ccf8c403505ea97`): `feat(practice): disable automatic normal-practice deadlines`.
- **Full Validation Acceptance at Candidate HEAD `e1ad6914650560e5a586531d213f5becad51580b` (`P1_FULL_VALIDATION_PASSED`)**:
  - Complete Core test suite: 1,936 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
  - Focused P1 suite: 244 passed, 0 failed, 0 skipped (`NormalPracticeNoDeadlinePolicyTests`, `AdaptivePaceRuntimeTests`, `NoTimePressureModeTests`, `TimedTrainingOutcomeTests`, `AttemptContextEnrichmentTests`, `PracticeVisibilityAndTimerLifecycleTests`, `CyberDefenseUiContractTests`).
  - P0 selector regression suite: 144 passed, 0 failed, 0 skipped (`DeterministicSelectorTerminalLivenessTests`, `IndependentSelectorTests`, `NoImmediateFactRepetitionTests`, `TieredRemediationAndBroadWeaknessTests`, `EarlyStateSchedulerFailureCharacterizationTests`).
  - Persistence & telemetry focused suite: 43 passed, 0 failed, 0 skipped (`SqliteEnabledSubsetPersistenceTests`, `RuntimePersistenceRegressionTests`, `PersistenceRecoveryAndLifecycleTests`, `SubmissionIntegrityAndPublishBoundaryTests`).
  - Windows Release build: 0 warnings, 0 errors.
  - Android Release compilation: `net10.0-android36.0` Release compile-only (Target `Compile`) SUCCESS (0 warnings, 0 errors).
  - Whitespace & formatting check (`git diff --check`): clean.
  - Tracked working tree: clean after validation.
  - Step 55 & Packaging Boundary: Zero APK/AAB packaging performed; zero signing; zero deployment; zero ADB/device/emulator actions; no repository artifacts introduced.
  - Note: Subsequent documentation-only integration reconciliation does not alter production/test behavior.
- **Transitional Scope Note**: Settings UI retains practice time controls as accepted transitional UX debt pending P4.
- **Repository Integration Status**:
  - Technical implementation, package review, and full validation are complete.
  - Integration vehicle is PR #61 (https://github.com/Tachiguro/MathFirst/pull/61) on branch `feat/p1-no-deadline-failure`.
  - Live review status, CI/checks, mergeability, and merge state are discovered dynamically from GitHub.
  - Next lifecycle step: PR review / integration decision.

---

## 2. Historical Merged Implementation Packages

- **P0 (Zero-Answer / `0 + 0` Core-Flow Freeze Blocker)**: Merged via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`). Diagnosed selector terminal-liveness starvation at StoreRevision 5 (practice position 4 -> 5). Added generic terminal New fallback in `AdaptivePracticeSelector.GetFallbackChain` for `Requested Due`, `Requested Maintenance`, and `Requested Frontier`, preserving review priority, curriculum ownership, number-space gating, and immediate-predecessor exclusion. Verified with 144 fixture tests across 5 permanent regression fixtures and 1,917 full Core tests.
- **MF-TELEM-001 (Tester Telemetry Export & Share)**: Merged via PR #56 (`bbdf62652927efa26475a9f2d83778de6465f5e1`). Delivered Schema V7 persistence with 5 nullable presentation-context columns, pseudonymous persistent random installation UUID, complete-history JSON telemetry export (`telemetry_export_schema_v1`), sandboxed native platform sharing (`telemetry-share`), Full Local Reset cleanup, and learning non-interference regression coverage. Post-merge validation: 1,906 Core tests passed.
- **MF-UX-008 (Static Combat Layout & Boss Presentation)**: Merged via PR #55 (`76116d11b8563b0407188ba53ccefd998eda958d`). Delivered static combat layout positional stability (keypad and arithmetic typography stationary across combat transitions), layout-isolated boss presentation (`clamp(90px, 16vh, 140px)`), visual layering (`z-index: 4` + scrim protection), progressive opponent scaling (0.65 to 1.50), tier-preserving feedback scale composition (`SCALE_PRESERVED_ACROSS_ALL_STATES`), and scoped active-gameplay scroll suppression (`.training-host.active-gameplay`). Post-merge validation: 1,786 Core tests passed.
- **MF-LEARN-006 (Adaptive Learning Policy & 482 Benchmark)**: Merged via PR #54 (`bc7471b098e2f79262ff6e71302820bd281a14d5`). Delivered Option-B Evidence-Adaptive Discovery, universal no-immediate-fact-repetition invariant ($\text{FactId}(t+1) \ne \text{FactId}(t)$), tiered remediation (cooldown 4 / 2; broad weakness threshold 2), Guided Gate G3 soft decoupling at `BandIndex >= 3`, pace calibration readiness at $\ge 24$ positioned Correct attempts, calibrated Cyber Defense Critical Hits, and the exact 482 strong-learner benchmark. Post-merge validation: 1,772 Core tests passed.
- **MF-UX-007 (Progress Presentation Cleanup)**: Merged via PR #47 (`d37fbe3347679220bf847b06c83f7f9366738d03`). Delivered concise learner-facing Stage terminology, returning learner Ready Gate overview, active HUD accessibility (`Training_OperationProgressGroupAriaLabel`), and responsive layout preservation. Post-merge validation: 1,605 Core tests passed.

---

## 3. Downstream Release Context & Planned Sequence

### Release Context:
- **Build 2 Rejection**: Historical. Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to the selector crash/starvation bug on operation reconfiguration and restart, resolved by `MF-STAB-003`.
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, `MF-LEARN-006`, `MF-UX-008`, `MF-TELEM-001`, and `P0` and no longer represents current repository source.
- **Step 52 Tester Build**: Built from `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f` (`com.tachiguro.mathfirst.tester`, version `1.0 / versionCode 1`, SHA-256 `40e2b5e3f23e90a2dbe695db4d724e4375c5f2ef51709a801fa81cf6e06f4039`), installed and manually validated on Samsung Galaxy S26 Ultra in Steps 53–54 (`STEP_54_MANUAL_VALIDATION_PASS`).
- **Future Production Candidate**:
  - Any future production candidate packaging after pre-Step55 refinement will have `versionCode >= 4`.
  - Build 4 does **not** exist yet (not packaged, not signed, not tested).
  - Roadmap Step 55 production packaging is **NOT AUTHORIZED**.

### Authorized Downstream Project Sequence:
1. P0 is integrated and merged into `main` via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`).
2. P1 implementation, package review, and full validation are complete (`P1_FULL_VALIDATION_PASSED` at `e1ad6914650560e5a586531d213f5becad51580b`). Repository integration proceeds through PR #61 on branch `feat/p1-no-deadline-failure`.
3. Live PR integration lifecycle phase (review, merge decision, merge) must be discovered dynamically from Git and GitHub state.
4. Merging PR #61 requires PR review approval (`PR_REVIEW_PASS`), `MERGE_DECISION`, and explicit affirmative user authorization. Auto-merge is not authorized.
5. Downstream P-item sequence ($\text{P1b} \to \text{P2} \to \text{P3} \to \text{P4} \to \text{P5} \to \text{P6} \to \text{P8}$) must be completed and merged before Step 55 may be proposed.
6. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. Build 4 does not exist yet (not packaged, not signed, not tested). No production packaging, release signing, ADB, or release action is authorized without explicit user dispatch.
