# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: `P2-DOCUMENTATION-SYNC` (P2 Direct-to-Practice Start / Remove Onboarding Documentation Synchronization).
- **Active Package**: `P2`: Direct-to-Practice Start / Remove Onboarding.
- **Branch / Upstream State**: Task branch `feat/p2-direct-to-practice` based on `main` (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`).
- **Implementation & Integration Milestones**:
  - Slice 1: `ca07624cb89f4764b145ea08b4a28a03b88cc56b` (`feat(practice): default initial operation preference to addition only`)
  - Slice 2: `083fc2c3e5d42bd38f2acc9ef0cdb7a50ce288b8` (`feat(practice): start fresh learners directly in practice`)
  - Slice 3: `b91f91614d3e8e59341ee6211b46b36f5530d298` (`feat(app): remove onboarding startup gate`)
  - Slice 4: `d44c9bf3acf2a724e349100c1c605bc29d02c25d` (`refactor(app): remove obsolete onboarding assets`)
  - Slice 5: `84070a6951bc1853e0b9209204a915865e00cb62` (`refactor(preferences): remove obsolete onboarding state`)
  - Docs Sync Checkpoint: `42d8c0884ad3350cad5774ecd5b0098d13ed3e74` (`docs: synchronize P2 direct-to-practice state`) [Candidate failed FULL_VALIDATION: `FULL_VALIDATION_FAILED`]
  - Validation-Fix Checkpoint: `085b929f058eb1ba4477f2bdc1412a09c218d648` (`fix(practice): separate app defaults from domain fallback`) [Candidate review approved: `P2_VALIDATION_FIX_REVIEW_APPROVED`]
- **Live State Discovery**: Live local Git and GitHub repository state always takes precedence over documentation. The live branch HEAD commit SHA, divergence from `origin/main`, working tree status, review state, mergeability, CI/checks, and merge state must be discovered dynamically from:
  - `git status`
  - `git rev-parse HEAD`
  - `git fetch origin`
  - `git log origin/main..HEAD`
  - `gh repo view Tachiguro/MathFirst`
  - `gh pr list --state open`
- **Status of Active Work**:
  - **P2 Implementation & Validation-Fix Status**: Implementation complete across five original feature checkpoints, one documentation checkpoint (`42d8c0884ad3350cad5774ecd5b0098d13ed3e74`), and one validation-fix checkpoint (`085b929f058eb1ba4477f2bdc1412a09c218d648`).
  - **Validation & Review History**:
    - Original candidate `42d8c0884ad3350cad5774ecd5b0098d13ed3e74` passed initial review (`P2_REVIEW_APPROVED`) but **FAILED** formal `FULL_VALIDATION` (`FULL_VALIDATION_FAILED`: Debug Core suite aborted after 1,941 passed / 34 failed / 0 skipped when the 5-minute blame-hang detector fired; full Release suite not executed; Windows/Android Release builds and NuGet vulnerability audit passed with 0 warnings/errors/vulnerabilities, but did not override validation failure).
    - Root cause: Over-broad default / abstraction regression in Slice 1 incorrectly conflated the application preference default (fresh install / reset defaults to Addition-only via `MauiPreferenceStore` missing-key semantics) with generic domain fallback (which must normalize null or empty selections to `AllOperations`).
    - Corrective checkpoint `085b929f058eb1ba4477f2bdc1412a09c218d648` restored the abstraction boundary (`PracticeOperationPreferencePolicy.NormalizeEnabledOperations(null)` and `(empty)` $\to$ `AllOperations`), leaving `MauiPreferenceStore` unchanged and preserving fresh Addition-only behavior.
    - Corrected candidate `085b929f058eb1ba4477f2bdc1412a09c218d648` passed independent review (`P2_VALIDATION_FIX_REVIEW_APPROVED`).
    - `FULL_VALIDATION` is pending from scratch on the corrected candidate.
  - **Durable P2 Behavioral Contract**:
    1. **Fresh / No-History Learner**:
       $$\text{Router} \longrightarrow \text{Home} \longrightarrow \text{no onboarding} \longrightarrow \text{no Initial Ready Gate} \longrightarrow \text{direct active Practice} \longrightarrow \text{Addition only default}$$
       Active interaction timing begins only after practice surface activation.
    2. **Returning Learner with Accepted Practice History**:
       $$\text{Router} \longrightarrow \text{Home} \longrightarrow \text{Initial Ready Gate} \longrightarrow \text{progress overview} \longrightarrow \text{explicit Start/Resume} \longrightarrow \text{active Practice}$$
    3. **Default Preferences**:
       - Language: System/device language
       - Theme: System
       - Keypad: Numpad (`7 8 9` at top)
       - Haptics: enabled
       - Enabled operations: Addition only
       - Practice timing: normal-practice no-deadline-failure behavior
    4. **Settings**: Language, Theme, Keypad, Haptics, and Operation selection remain configurable. Practice Time control remains present as transitional UX debt pending P4. No P3 unlock enforcement exists yet; no broad P4 Settings simplification has occurred.
    5. **Reset Learning Progress**: Learner progress/state is reset; live preferences are preserved; no onboarding state exists.
    6. **Reset UI Preferences**: Learner history/progress is preserved; Language $\to$ System, Theme $\to$ System, Keypad $\to$ Numpad, Haptics $\to$ enabled, practice preferences $\to$ defaults (Addition only); navigates to `/`; returning/fresh behavior then depends on whether accepted practice history exists; no onboarding is shown.
    7. **Full Local Reset**: Learner state is reset, live preferences are reset, installation ID is cleared, share cache purge behavior is preserved, navigates to `/`; no accepted practice history remains; direct active Addition Practice follows; no onboarding is shown.
    8. **Onboarding Removal**: Five-step `OnboardingHost` removed; onboarding router gate removed; onboarding localization removed; onboarding-specific CSS removed; onboarding preference API/key removed; no production `onboarding` references remain under `src`; historical physical MAUI preference key on upgraded devices requires no migration because it is no longer read.
    9. **Step 55 / Build 4**: Roadmap Step 55 remains NOT EXECUTED / NOT AUTHORIZED. Build 4 does not exist.
  - **Verification Evidence on Task Branch**:
    - Slice 1: focused tests: 59 passed; targeted regressions: 40 passed.
    - Slice 2: focused startup contracts: 7 passed; targeted lifecycle group: 28 passed; timer/interruption/flow group: 85 passed.
    - Slice 3: focused/reset suite: 38 passed; startup regression suite: 28 passed; MathFirst.App Windows build: 0 warnings / 0 errors.
    - Slice 4: targeted groups: 120 passed, 116 passed, 13 passed; MathFirst.App Windows build: 0 warnings / 0 errors.
    - Slice 5: focused contract: 1 passed; Core test project build: 0 warnings / 0 errors; targeted preference/reset regressions: 97 passed; targeted startup/recovery/timing regressions: 54 passed; MathFirst.App Windows build: 0 warnings / 0 errors.
    - Original candidate review evidence: 184 targeted tests passed, 0 failed.
    - Failed candidate `42d8c0884ad3350cad5774ecd5b0098d13ed3e74` FULL_VALIDATION evidence: Debug Core suite 1,975 completed before abort (1,941 passed, 34 failed, 0 skipped, 5-minute hang detector fired, exit code 1); Release full Core suite not executed; Windows Release build 0 warnings / 0 errors; Android Release build 0 warnings / 0 errors; NuGet vulnerability audit 0 vulnerable packages; focused P2 Release confirmation 76 passed, 0 failed. Overall verdict: `FULL_VALIDATION_FAILED`.
    - Validation-fix targeted evidence (`085b929f058eb1ba4477f2bdc1412a09c218d648`): RED confirmed 3 boundary failures; GREEN confirmed `PracticeConfigurationTests` 46 passed; regression suites passed: `IndependentSelectorTests` (52), `GuidedNumberSpaceSelectionTests` (10), `DenseProgressionTests` (27), `FactEligibilityRegressionTests` (23), `LongRunIndependentProgressionTests` (7, duplicate-position failures resolved), `DeterministicSelectorTerminalLivenessTests` (57, no hang), `FinalIntegrationCoverageTests` (2), `BoundedSelectionIntegrationTests` + `SubmissionIntegrityAndPublishBoundaryTests` + `StaleSelectionEvidenceRemediationTests` (33); focused P2 contract regression set (77 passed). Overlapping runs are not summed into fabricated totals. This targeted evidence is NOT full validation.
    - Note on evidence boundaries: FULL_VALIDATION has NOT yet been performed on the corrected candidate (`085b929f058eb1ba4477f2bdc1412a09c218d648`).
  - **Integration & Procedural State**:
    - Branch `feat/p2-direct-to-practice` is local-only (7 commits ahead of `main`, unpushed, no open PR; live ahead/behind divergence derived dynamically via `git log origin/main..HEAD`).
    - Next lifecycle step: `REVIEW_ONLY` (independent review of documentation synchronization before commit/push and subsequent `FULL_VALIDATION`).
  - **Prior Work**:
    - **P0**: Zero-Answer / `0 + 0` Core-Flow Freeze resolved and merged via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`).
    - **P1**: Normal Practice Without Deadline Failure resolved and merged via PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`).
    - **P1b**: Active Thinking Time / Interruption Safety resolved and merged via PR #62 (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`).
  - **Downstream Scope**: P3 (Cumulative Operation Unlock Progression), P4 (Settings Simplification), P5, P6, P8. Step 55 is **NOT AUTHORIZED**.

### 1.1 Pre-Step55 Refinement Program Sequence

The canonical pre-production program ([docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md)) governs downstream work:
1. **P0**: Zero-Answer / `0 + 0` Core-Flow Freeze (`Merged` — PR #60 at `4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`)
2. **P1**: Normal Practice Without Deadline Failure (`Merged` — PR #61 at `4e3ca4943c5809cbe470a4b0ac4f192b24b66795`)
3. **P1b**: Active Thinking Time / Interruption Safety (`Merged` — PR #62 at `a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`)
4. **P2**: Direct-to-Practice Start / Remove Onboarding (`Implemented, Corrected & Reviewed` — 7 checkpoints on task branch `feat/p2-direct-to-practice`, corrected candidate `085b929f058eb1ba4477f2bdc1412a09c218d648`; prior candidate `42d8c0884ad3350cad5774ecd5b0098d13ed3e74` failed FULL_VALIDATION; validation-fix review approved; documentation reconciliation prepared; candidate unmerged / unpushed, FULL_VALIDATION pending from scratch)
5. **P3**: Cumulative Operation Unlock Progression ($+ \to + - \to + - \times \to + - \times \div$; demonstrated mathematical evidence, 3 simulation personas — *Downstream*)
6. **P4**: Settings Simplification (streamlined settings, remove practice time selection, prevent unlock bypass — *Downstream*)
7. **P5**: Cyber Defense Visual Consistency (dark technical surfaces, restrained neon/cyber accents — *Downstream*)
8. **P6**: Tester Diagnostics / Telemetry Release Boundary (hard compile/profile boundary isolating Tester diagnostic controls from Distributable UI — *Downstream*)
9. **P8**: Test-Coverage Audit & Targeted Hardening (factual baseline, critical invariant coverage — *Downstream*)
10. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*)

### 1.2 Current Verified Quality State

#### Merged Main Baseline (`main@a7b579b` / PR #62 `P1b`)
- **Merge Commit**: `a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912` (PR #62 `Merge pull request #62 from Tachiguro/feat/p1b-active-thinking-time`).
- **Post-Merge Verification**: Schema V8 persistence, telemetry export schema v2, active thinking time / interruption safety, and Dual-Window structured progression integrated cleanly; 2,010 Core tests passing, clean Windows and Android builds.

#### Verified P2 Quality Evidence (`feat/p2-direct-to-practice`)
- **Package Commit Chain**:
  - Slice 1 (`ca07624cb89f4764b145ea08b4a28a03b88cc56b`): `feat(practice): default initial operation preference to addition only`.
  - Slice 2 (`083fc2c3e5d42bd38f2acc9ef0cdb7a50ce288b8`): `feat(practice): start fresh learners directly in practice`.
  - Slice 3 (`b91f91614d3e8e59341ee6211b46b36f5530d298`): `feat(app): remove onboarding startup gate`.
  - Slice 4 (`d44c9bf3acf2a724e349100c1c605bc29d02c25d`): `refactor(app): remove obsolete onboarding assets`.
  - Slice 5 (`84070a6951bc1853e0b9209204a915865e00cb62`): `refactor(preferences): remove obsolete onboarding state`.
  - Docs Checkpoint (`42d8c0884ad3350cad5774ecd5b0098d13ed3e74`): `docs: synchronize P2 direct-to-practice state` (Candidate `FULL_VALIDATION_FAILED`).
  - Validation-Fix Checkpoint (`085b929f058eb1ba4477f2bdc1412a09c218d648`): `fix(practice): separate app defaults from domain fallback` (Candidate `P2_VALIDATION_FIX_REVIEW_APPROVED`).
- **Validation History & Evidence**:
  - Original review on five implementation checkpoints passed (`P2_REVIEW_APPROVED`, 184 targeted tests passed, 0 failed).
  - Formal FULL_VALIDATION on candidate `42d8c0884ad3350cad5774ecd5b0098d13ed3e74` failed (`FULL_VALIDATION_FAILED`: 1,941 passed / 34 failed / 0 skipped in Debug full Core suite before 5-minute hang abort; Release full Core suite not executed; Windows/Android Release builds passed with 0w/0e; NuGet vulnerability audit passed with 0 vulnerable packages; focused P2 Release confirmation passed with 76/76).
  - Root cause diagnosed as over-broad default coupling application default with domain fallback in Slice 1.
  - Validation fix implemented in `085b929f058eb1ba4477f2bdc1412a09c218d648` and approved in review (`P2_VALIDATION_FIX_REVIEW_APPROVED`).
  - Validation-fix targeted evidence: `PracticeConfigurationTests` 46 passed; regression suites passed (`IndependentSelectorTests` 52, `GuidedNumberSpaceSelectionTests` 10, `DenseProgressionTests` 27, `FactEligibilityRegressionTests` 23, `LongRunIndependentProgressionTests` 7, `DeterministicSelectorTerminalLivenessTests` 57, `FinalIntegrationCoverageTests` 2, `BoundedSelectionIntegrationTests` + `SubmissionIntegrityAndPublishBoundaryTests` + `StaleSelectionEvidenceRemediationTests` 33); focused P2 contract regression set 77 passed; zero hangs, zero duplicate-position errors. Overlapping runs are not summed into fabricated totals.
  - FULL_VALIDATION is pending from scratch on corrected candidate `085b929f058eb1ba4477f2bdc1412a09c218d648`.
  - Packaging boundaries strictly preserved: zero APK/AAB packaging, zero signing, zero store actions.

---

## 2. Historical Merged Implementation Packages

- **P1b (Active Thinking Time / Interruption Safety)**: Merged via PR #62 (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`). Pauses active timing across lifecycle interruptions; records durable empirical boolean `AttemptRecord.IsInterrupted` in Schema V8; derives timing evidence eligibility; establishes Structured Band Dual-Window progression; updates telemetry export to schema version 2 (16 properties); preserves FSRS rating and calibration thresholds. Post-merge validation: 2,010 Core tests passed.
- **P1 (Normal Practice Without Deadline Failure)**: Merged via PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`). Removed automatic timeout question termination from normal practice (`HasEnforcedDeadline = false`); graded late answers strictly by mathematical correctness; preserved authentic response latency and adaptive pace modeling; recorded `PresentedDeadlineMs = null` in Schema V7 presentation context.
- **P0 (Zero-Answer / `0 + 0` Core-Flow Freeze Blocker)**: Merged via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`). Diagnosed selector terminal-liveness starvation at StoreRevision 5 (practice position 4 -> 5). Added generic terminal New fallback in `AdaptivePracticeSelector.GetFallbackChain` for `Requested Due`, `Requested Maintenance`, and `Requested Frontier`, preserving review priority, curriculum ownership, number-space gating, and immediate-predecessor exclusion. Verified with 144 fixture tests across 5 permanent regression fixtures and 1,917 full Core tests.
- **MF-TELEM-001 (Tester Telemetry Export & Share)**: Merged via PR #56 (`bbdf62652927efa26475a9f2d83778de6465f5e1`). Delivered Schema V7 persistence with 5 nullable presentation-context columns, pseudonymous persistent random installation UUID, complete-history JSON telemetry export (`telemetry_export_schema_v1`), sandboxed native platform sharing (`telemetry-share`), Full Local Reset cleanup, and learning non-interference regression coverage. Post-merge validation: 1,906 Core tests passed.
- **MF-UX-008 (Static Combat Layout & Boss Presentation)**: Merged via PR #55 (`76116d11b8563b0407188ba53ccefd998eda958d`). Delivered static combat layout positional stability (keypad and arithmetic typography stationary across combat transitions), layout-isolated boss presentation (`clamp(90px, 16vh, 140px)`), visual layering (`z-index: 4` + scrim protection), progressive opponent scaling (0.65 to 1.50), tier-preserving feedback scale composition (`SCALE_PRESERVED_ACROSS_ALL_STATES`), and scoped active-gameplay scroll suppression (`.training-host.active-gameplay`). Post-merge validation: 1,786 Core tests passed.
- **MF-LEARN-006 (Adaptive Learning Policy & 482 Benchmark)**: Merged via PR #54 (`bc7471b098e2f79262ff6e71302820bd281a14d5`). Delivered Option-B Evidence-Adaptive Discovery, universal no-immediate-fact-repetition invariant ($\text{FactId}(t+1) \ne \text{FactId}(t)$), tiered remediation (cooldown 4 / 2; broad weakness threshold 2), Guided Gate G3 soft decoupling at `BandIndex >= 3`, pace calibration readiness at $\ge 24$ positioned Correct attempts, calibrated Cyber Defense Critical Hits, and the exact 482 strong-learner benchmark. Post-merge validation: 1,772 Core tests passed.
- **MF-UX-007 (Progress Presentation Cleanup)**: Merged via PR #47 (`d37fbe3347679220bf847b06c83f7f9366738d03`). Delivered concise learner-facing Stage terminology, returning learner Ready Gate overview, active HUD accessibility (`Training_OperationProgressGroupAriaLabel`), and responsive layout preservation. Post-merge validation: 1,605 Core tests passed.

---

## 3. Downstream Release Context & Planned Sequence

### Release Context:
- **Build 2 Rejection**: Historical. Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to the selector crash/starvation bug on operation reconfiguration and restart, resolved by `MF-STAB-003`.
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, `MF-LEARN-006`, `MF-UX-008`, `MF-TELEM-001`, `P0`, `P1`, and `P1b` and no longer represents current repository source.
- **Step 52 Tester Build**: Built from `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f` (`com.tachiguro.mathfirst.tester`, version `1.0 / versionCode 1`, SHA-256 `40e2b5e3f23e90a2dbe695db4d724e4375c5f2ef51709a801fa81cf6e06f4039`), installed and manually validated on Samsung Galaxy S26 Ultra in Steps 53–54 (`STEP_54_MANUAL_VALIDATION_PASS`).
- **Future Production Candidate**:
  - Any future production candidate packaging after pre-Step55 refinement will have `versionCode >= 4`.
  - Build 4 does **not** exist yet (not packaged, not signed, not tested).
  - Roadmap Step 55 production packaging is **NOT AUTHORIZED**.

### Authorized Downstream Project Sequence:
1. P0 is integrated and merged into `main` via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`).
2. P1 is integrated and merged into `main` via PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`).
3. P1b is integrated and merged into `main` via PR #62 (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`).
4. P2 implementation, documentation checkpoint, and validation-fix are complete across seven commits on branch `feat/p2-direct-to-practice` (corrected candidate `085b929f058eb1ba4477f2bdc1412a09c218d648`). Prior candidate `42d8c0884ad3350cad5774ecd5b0098d13ed3e74` failed FULL_VALIDATION; validation-fix review is approved; documentation reconciliation and independent review precede full validation rerun, commit, push, and PR.
5. Downstream P-item sequence ($\text{P3} \to \text{P4} \to \text{P5} \to \text{P6} \to \text{P8}$) must be completed and merged before Step 55 may be proposed.
6. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. Build 4 does not exist yet (not packaged, not signed, not tested). No production packaging, release signing, ADB, or release action is authorized without explicit user dispatch.
