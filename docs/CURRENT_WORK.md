# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: `P6-DOCUMENTATION-REMEDIATION` (P6 Tester Diagnostics / Telemetry Release Boundary Documentation Remediation).
- **Active Package**: `P6`: Tester Diagnostics / Telemetry Release Boundary.
- **Branch / Upstream State**: Task branch `feat/p6-tester-diagnostics-release-boundary` based on `main` (`aeb7bc46e8b425d9da95493a367f99f7ed330871`). Historical anchor: implementation committed locally in `a711c07d80ab2cc3873cbb5a0de96803fabe116b`.
- **Implementation & Integration Milestones**:
  - Slice 1: Property, Symbol, Profile Mapping, and Compile Boundary (implemented & review-approved: `P6_SLICE_1_REVIEW_APPROVED`).
  - Slice 2: Component & DI Boundary, Cache Purge Split, and Reset Invariant (implemented & review-approved: `P6_SLICE_2_REVIEW_APPROVED`).
  - Slice 3: Regression Suite, Test Matrix, and Release Hardening (implemented & review-approved: `P6_SLICE_3_REVIEW_APPROVED`).
  - Complete Candidate Review: Consolidated package review returned `P6_COMPLETE_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 0 Note).
  - Implementation Commit: P6 implementation committed locally in commit `a711c07d80ab2cc3873cbb5a0de96803fabe116b`.
  - First Formal FULL_VALIDATION Attempt: Failed fail-closed at the documentation-current-state gate (`FINDING-P6-DOC-STALE-STATE` / `P6_FULL_VALIDATION_FAILED`) against candidate `a711c07d80ab2cc3873cbb5a0de96803fabe116b` because committed docs still asserted pre-commit lifecycle state. Downstream expensive test/build gates were skipped fail-closed.
  - Remediation & Integration Status: Documentation state is reconciled on `feat/p6-tester-diagnostics-release-boundary` before fresh exact-candidate `FULL_VALIDATION`. The branch remains outside `main` until validation passes, followed by `PUSH_ONLY`, `PR_ONLY`, manual user merge, and `POST_MERGE_SYNC_ONLY`.
- **Live State Discovery**: Live local Git and GitHub repository state always takes precedence over documentation. The live branch HEAD commit SHA, divergence from `origin/main`, working tree status, review state, mergeability, CI/checks, and merge state must be discovered dynamically from:
  - `git status`
  - `git rev-parse HEAD`
  - `git fetch origin`
  - `git log origin/main..HEAD`
  - `gh repo view Tachiguro/MathFirst`
  - `gh pr list --state open`
- **Status of Active Work**:
  - **P6 Implementation & Review Status**: Implementation complete. Consolidated package review returned `P6_COMPLETE_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 0 Note). 2,228 Core review tests passing (0 failed, 0 skipped), 117 focused P6 contract and regression tests across 11 suites, 18 P5 UI regression tests across 3 suites. Windows compile matrix (Debug, Release, Release with diagnostics=true) and Android profile-like compile matrix (Tester-like, SourceCandidate-like, Production-like) compile with 0 warnings and 0 errors.
  - **P6 Integration & Procedural State**: P6 implementation was committed locally in implementation commit `a711c07d80ab2cc3873cbb5a0de96803fabe116b`. The first formal `FULL_VALIDATION` attempt against that candidate failed at the documentation-current-state gate (`FINDING-P6-DOC-STALE-STATE`) because committed docs still described pre-commit lifecycle state. No code/test/build failure was observed; downstream expensive gates were skipped fail-closed. Documentation was therefore reconciled as part of the same P6 task branch before fresh exact-candidate `FULL_VALIDATION`. No successful P6 `FULL_VALIDATION_PASS` exists yet at this documented checkpoint. Candidate integration requires a successful exact-candidate `FULL_VALIDATION` before `PUSH_ONLY`. P6 remains unpushed and unmerged; exact live branch HEAD, ahead-count, remote branch, and PR state must be discovered dynamically from live Git/GitHub.
  - **Durable P6 Technical Contract & Architecture**:
    1. **Build Property & Compile Symbol Boundary**:
       - `MathFirstEnableTesterDiagnostics` controls compile symbol `MATHFIRST_TESTER_DIAGNOSTICS`.
       - Defaults to `true` in Debug, `false` in non-Debug/Release; explicit caller values override defaults.
       - Compile symbol `MATHFIRST_TESTER_DIAGNOSTICS` is defined if and only if `MathFirstEnableTesterDiagnostics == true`.
    2. **Release Profiles Matrix**:
       - *Local Debug*: Diagnostics enabled by default, `BuildClassification = Local`, source commit local unless supplied; developer ergonomics and testing.
       - *Local Release*: Diagnostics disabled by default, `BuildClassification = Local`, empty non-Tester component stub, no Tester DI services.
       - *Tester*: Diagnostics enabled, `BuildClassification = Tester`, `SourceCommit = <HEAD>` supplied by ReleaseTool, `ApplicationId = com.tachiguro.mathfirst.tester`, APK format, tester diagnostics/export/share available.
       - *SourceCandidate*: Diagnostics disabled, `BuildClassification = SourceCandidate`, `SourceCommit = <HEAD>`, `ApplicationId = com.tachiguro.mathfirst`, non-Tester compile parity, no Tester DI services.
       - *Distributable / Production*: Diagnostics disabled, `BuildClassification = Production`, `SourceCommit = <HEAD>`, `ApplicationId = com.tachiguro.mathfirst`, non-Tester compile parity, no Tester DI services, production signing semantics unchanged.
    3. **Tester Diagnostics Component Isolation (`TesterDiagnosticsSection`)**:
       - `Settings.razor` no longer directly owns `IAppPlatformInfo`, `IClipboardService`, `TelemetryExportCoordinator`, `CopyDiagnosticsAsync`, `ExportTelemetryAsync`, or diagnostic/export local state.
       - Dedicated `TesterDiagnosticsSection` owns diagnostic and export UI actions. Under `MATHFIRST_TESTER_DIAGNOSTICS`, renders active "Copy diagnostic info", "Export & Share Telemetry", status feedback, and busy state. When the symbol is absent, compiles to an empty dependency-free stub with no diagnostic UI rendered.
    4. **Conditional DI Composition**:
       - Tester-only services (`IAppPlatformInfo`, `IClipboardService`, `ITelemetryJsonSerializer`, `ITelemetryShareService`, `TelemetryExportCoordinator`) are registered conditionally under `MATHFIRST_TESTER_DIAGNOSTICS`.
       - Normal shared services (`AppBuildInfo`/`IAppBuildInfo`, `IInstallationIdStore`, `IInstallationIdProvider`, `ITelemetryShareCacheCleaner`, `IAppResetCoordinator`, learner infrastructure) remain registered in all profiles.
    5. **Share / Cache Responsibility Split**:
       - `ITelemetryShareService` owns only preparing telemetry share files and dispatching platform share UI.
       - `ITelemetryShareCacheCleaner` owns purging the telemetry-share cache.
       - `TelemetryShareCachePaths` is the single application runtime authority for the `telemetry-share` directory under `FileSystem.CacheDirectory`.
    6. **Full Local Reset Contract**:
       - Full Local Reset remains standard product behavior across ALL profiles: 1. pause active item timing; 2. reset learner progress; 3. reset all preferences; 4. clear installation ID; 5. purge telemetry-share cache.
       - Cache cleanup is performed in all profiles (including Production and SourceCandidate) to guarantee privacy and clean data resets. `IOException` and `UnauthorizedAccessException` during purge are handled best-effort and do not fail the reset.
    7. **Preserved Telemetry & Persistence Contracts**:
       - P6 does NOT alter SQLite Schema V9, `attempt_history`, `ILearnerStore` telemetry persistence, FSRS learning evidence, `telemetry_export_schema_v2` (16 properties), or manual opt-in export semantics. No migration, no background uploads, no network transfer.
    8. **Release Metadata Propagation**:
       - ReleaseTool publish invocations propagate authoritative build classification and HEAD source commit metadata (`Tester` $\to$ `Tester`, `SourceCandidate` $\to$ `SourceCandidate`, `Distributable` $\to$ `Production`), preventing packaged builds from falling back to passive local defaults.
    9. **No New ADR**:
       - P6 implements and refines boundaries already established in [ADR-0006](decisions/ADR-0006-android-packaging-signing-and-manifest-release-security.md) and [ADR-0011](decisions/ADR-0011-tester-telemetry-persistence-export-and-share.md) plus existing Full Local Reset product semantics.
  - **Verification Evidence on P6 Candidate**:
    - Dedicated permanent P6 contract and regression suites (117 tests total across 11 suites):
      - `ReleaseProfileContractTests.cs`: 5 passed
      - `TesterApkPackagingContractTests.cs`: 23 passed
      - `TesterDiagnosticsContractTests.cs`: 9 passed
      - `SettingsTelemetryUiContractTests.cs`: 10 passed
      - `SettingsUnlockContractTests.cs`: 12 passed
      - `SettingsSimplificationContractTests.cs`: 8 passed
      - `NativeIdentityContractTests.cs`: 6 passed
      - `AndroidFileProviderContractTests.cs`: 16 passed
      - `AppResetCoordinatorTests.cs`: 7 passed
      - `ResetWorkflowTests.cs`: 8 passed
      - `TelemetryExportCoordinatorTests.cs`: 13 passed
    - P5 secondary UI regression suites: 18 passed across 3 suites.
    - Full Core review: 2,228 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
    - Windows compile matrix (Debug, Release, Release with diagnostics=true): all 0 warnings / 0 errors.
    - Android profile-like compile matrix (Tester-like, SourceCandidate-like, Production-like): all 0 warnings / 0 errors.
    - `git diff --check`: PASS.
    - **Evidence Boundary**: This evidence is implementation and review evidence. The first formal `FULL_VALIDATION` attempt against implementation commit `a711c07d80ab2cc3873cbb5a0de96803fabe116b` failed at the documentation-current-state gate (`FINDING-P6-DOC-STALE-STATE` / `P6_FULL_VALIDATION_FAILED`), skipping downstream test/build gates fail-closed. Fresh exact-candidate `FULL_VALIDATION` remains required before `PUSH_ONLY`. No packaging or signing was performed; no physical-device / ADB testing was performed.
- **Prior Merged Work**:
  - **P5 (Cyber Defense Visual Consistency)**: Merged via PR #67 at merge commit `aeb7bc46e8b425d9da95493a367f99f7ed330871` (validated candidate `33dd87b646c0a0c94519fa76b7100346c4f30c6a`, review `P5_COMPLETE_REVIEW_APPROVED`, 2,222 Core tests). Aligned secondary application surfaces (Settings, Privacy, dialogs, overlays, Not Found) with Option A Scoped Cyber Defense visual language while preserving full Light/Dark/System theme fidelity, keyboard focus rings (`.keypad-choice-card:focus-visible`), reduced-motion suppression, and MF-UX-008 gameplay stability.
  - **P4 (Settings Simplification)**: Merged via PR #66 at merge commit `8400151ff080caecf024a418a9b6b8ada4873c2d` (validated candidate `edcc150039f369b8f809982499a5e1b2714e064c`, `FULL_VALIDATION_PASS` with 2,204 Core Debug / Release tests). Removed user-facing Practice Time selection from normal Settings, retained read-only curriculum operation status, preserved lower-level Practice Time plumbing, and established permanent 8-test contract coverage in `SettingsSimplificationContractTests.cs`.
  - **P3 (Cumulative Operation Unlock Progression)**: Merged via PR #65 at merge commit `759389650778f5d7b6a334b15556c5f31f6de5d0` (validated candidate `d1e794cf529598e1d57ae39dbe790bc51ede81d5`, 2,196 Core tests). Delivered four-stage cumulative progression ($+ \to + - \to + - \times \to + - \times \div$), monotonic `CurriculumStage` in `learner_progression`, Schema V9 persistence, tolerant D01 unlock predicates, aggregate broad weakness gating, Guided G3 soft decoupling, and read-only Settings operation status in `CurriculumManaged`.
  - **P2b (Gameplay and Startup Refinements)**: Merged via PR #64 at merge commit `f580a7154a4043a5097ffd852b5cf454be2cc397` (validated candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`, `FULL_VALIDATION_PASS` with 2,027 Core tests). Delivered digit-scaled Critical Hit timing ($T_{\text{crit}} = T_{\text{easy}} \times \text{DigitCount}$), shared radar/damage authority (`Session.CurrentFactCriticalHitThresholdMs`), and fresh startup `InitialReadyGate` orientation without active timing before explicit Start.
  - **P2 (Direct-to-Practice Start / Remove Onboarding)**: Merged via PR #63 at merge commit `1b485091755294221b6f242e174d99c168fc8e9d` (validated candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`, 2,014 Core tests).
  - **P1b (Active Thinking Time / Interruption Safety)**: Merged via PR #62 (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`, 2,010 Core tests).
  - **P1 (Normal Practice Without Deadline Failure)**: Merged via PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`, 1,936 Core tests).
  - **P0 (Zero-Answer / `0 + 0` Core-Flow Freeze Blocker)**: Merged via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`, 1,917 Core tests).
- **Downstream Scope**:
  - **P8 (Test Coverage Audit & Targeted Hardening)**: Downstream.
  - **P7 (Deferred Game Polish)**: Deferred / Post-Core.
  - Roadmap Step 55 is **NOT AUTHORIZED**. Build 4 does **NOT EXIST**.

### 1.1 Pre-Step55 Refinement Program Sequence

The canonical pre-production program ([docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md)) governs downstream work:
1. **P0**: Zero-Answer / `0 + 0` Core-Flow Freeze (`Merged` — PR #60 at `4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`)
2. **P1**: Normal Practice Without Deadline Failure (`Merged` — PR #61 at `4e3ca4943c5809cbe470a4b0ac4f192b24b66795`)
3. **P1b**: Active Thinking Time / Interruption Safety (`Merged` — PR #62 at `a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`)
4. **P2**: Direct-to-Practice Start / Remove Onboarding (`Merged` — PR #63 at `1b485091755294221b6f242e174d99c168fc8e9d`, candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`)
5. **P2b**: Gameplay and Startup Refinements (`Merged` — PR #64 at `f580a7154a4043a5097ffd852b5cf454be2cc397`, candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`)
6. **P3**: Cumulative Operation Unlock Progression (`Merged` — PR #65 at `759389650778f5d7b6a334b15556c5f31f6de5d0`, candidate `d1e794cf529598e1d57ae39dbe790bc51ede81d5`)
7. **P4**: Settings Simplification (`Merged` — PR #66 at `8400151ff080caecf024a418a9b6b8ada4873c2d`, candidate `edcc150039f369b8f809982499a5e1b2714e064c`)
8. **P5**: Cyber Defense Visual Consistency (`Merged` — PR #67 at `aeb7bc46e8b425d9da95493a367f99f7ed330871`, candidate `33dd87b646c0a0c94519fa76b7100346c4f30c6a`)
9. **P6**: Tester Diagnostics / Telemetry Release Boundary (`Implementation Complete & Review Approved — In Validation / Integration Lifecycle` — task branch `feat/p6-tester-diagnostics-release-boundary`, implementation commit `a711c07d80ab2cc3873cbb5a0de96803fabe116b`, review `P6_COMPLETE_REVIEW_APPROVED`; first `FULL_VALIDATION` attempt failed on documentation-current-state gate; documentation reconciled; fresh exact-candidate full validation required before push/PR)
10. **P8**: Test-Coverage Audit & Targeted Hardening (factual baseline, critical invariant coverage — *Downstream*)
11. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*)

### 1.2 Current Verified Quality State

#### Merged Main Baseline (`main@aeb7bc4` / PR #67 `P5`)
- **Merge Commit**: `aeb7bc46e8b425d9da95493a367f99f7ed330871` (PR #67 `Merge pull request #67 from Tachiguro/feat/p5-cyber-defense-visual-consistency`).
- **Merged Candidate**: `33dd87b646c0a0c94519fa76b7100346c4f30c6a`.
- **Post-Merge Baseline**: Cyber Defense secondary surface visual alignment across Settings, Privacy, dialogs, overlays, and Not Found; full Light/Dark/System theme fidelity; keyboard focus rings on `.keypad-choice-card:focus-visible`; reduced-motion suppression; MF-UX-008 gameplay stability; Settings simplification (removal of Practice Time UI, retention of read-only curriculum status, lower-level practice-time plumbing preserved, durable Settings cards retained); four-stage cumulative progression ($+ \to + - \to + - \times \to + - \times \div$); monotonic `CurriculumStage` in `learner_progression`; Schema V9 persistence; tolerant D01 unlock predicates; aggregate broad weakness gating; Guided G3 soft decoupling; 2,222 Core tests passing.

#### Verified P6 Quality Evidence (`feat/p6-tester-diagnostics-release-boundary`)
- **Candidate State**:
  - Slice 1: Property, Symbol, Profile Mapping, and Compile Boundary.
  - Slice 2: Component & DI Boundary, Cache Purge Split, and Reset Invariant.
  - Slice 3: Regression Suite, Test Matrix, and Release Hardening.
- **Review Status & Evidence**:
  - Consolidated package review: `P6_COMPLETE_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 0 Note).
  - Implementation review test evidence on candidate: 2,228 Core tests passed (0 failed, 0 skipped), including 117 focused P6 contract/regression tests and 18 P5 UI regression tests.
  - Windows compile matrix (Debug, Release, Release with diagnostics=true): all 0 warnings / 0 errors.
  - Android profile-like compile matrix (Tester-like, SourceCandidate-like, Production-like): all 0 warnings / 0 errors.
  - Formal `FULL_VALIDATION` against implementation commit `a711c07d80ab2cc3873cbb5a0de96803fabe116b` failed at the documentation-current-state gate (`P6_FULL_VALIDATION_FAILED`); fresh exact-candidate `FULL_VALIDATION` remains required following documentation remediation.
  - Packaging boundaries strictly preserved: zero APK/AAB packaging, zero signing, zero store actions.

---

## 2. Historical Merged Implementation Packages

- **P5 (Cyber Defense Visual Consistency)**: Merged via PR #67 (`aeb7bc46e8b425d9da95493a367f99f7ed330871`, candidate `33dd87b646c0a0c94519fa76b7100346c4f30c6a`). Aligned secondary application surfaces (Settings, Privacy, dialogs, overlays, Not Found) with Option A Scoped Cyber Defense visual language while preserving full Light/Dark/System theme fidelity, keyboard focus rings (`.keypad-choice-card:focus-visible`), reduced-motion suppression, and MF-UX-008 gameplay stability. Post-merge validation: 2,222 Core tests passed.
- **P4 (Settings Simplification)**: Merged via PR #66 (`8400151ff080caecf024a418a9b6b8ada4873c2d`, candidate `edcc150039f369b8f809982499a5e1b2714e064c`). Removed user-facing Practice Time selection (Standard, No Time Pressure, 30s, 45s, 60s) from normal Settings, retained read-only curriculum operation status, preserved lower-level Practice Time plumbing, and added permanent 8-test contract coverage in `SettingsSimplificationContractTests.cs`. Post-merge validation: 2,204 Core tests passed.
- **P3 (Cumulative Operation Unlock Progression)**: Merged via PR #65 (`759389650778f5d7b6a334b15556c5f31f6de5d0`, candidate `d1e794cf529598e1d57ae39dbe790bc51ede81d5`). Delivered four-stage cumulative progression ($+ \to + - \to + - \times \to + - \times \div$), monotonic `CurriculumStage` in `learner_progression`, Schema V9 persistence, tolerant D01 unlock predicates, aggregate broad weakness gating, Guided G3 soft decoupling, and read-only Settings operation status in `CurriculumManaged`. Post-merge validation: 2,196 Core tests passed.
- **P2b (Gameplay and Startup Refinements)**: Merged via PR #64 (`f580a7154a4043a5097ffd852b5cf454be2cc397`, candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`). Digit-scaled Cyber Defense Critical Hit timing ($T_{\text{crit}} = T_{\text{easy}} \times \text{DigitCount}$), aligned radar countdown arc and damage scoring to `Session.CurrentFactCriticalHitThresholdMs`, placed fresh practice startup behind `InitialReadyGate` without active timing before explicit Start, and preserved learning telemetry and FSRS state without mutation. Post-merge validation: 2,027 Core tests passed.
- **P2 (Direct-to-Practice Start / Remove Onboarding)**: Merged via PR #63 (`1b485091755294221b6f242e174d99c168fc8e9d`, candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`). Eliminated 5-step onboarding wizard; fresh learners launch directly into practice; default preferences set to System language/theme, Numpad, haptics enabled, Addition only; restored domain fallback boundary (`PracticeOperationPreferencePolicy.NormalizeEnabledOperations` $\to$ `AllOperations`). Post-merge validation: 2,014 Core tests passed.
- **P1b (Active Thinking Time / Interruption Safety)**: Merged via PR #62 (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`). Pauses active timing across lifecycle interruptions; records durable empirical boolean `AttemptRecord.IsInterrupted` in Schema V8; derives timing evidence eligibility; establishes Structured Band Dual-Window progression; updates telemetry export to schema version 2 (16 properties); preserves FSRS rating and calibration thresholds. Post-merge validation: 2,010 Core tests passed.
- **P1 (Normal Practice Without Deadline Failure)**: Merged via PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`). Removed automatic timeout question termination from normal practice (`HasEnforcedDeadline = false`); graded late answers strictly by mathematical correctness; preserved authentic response latency and adaptive pace modeling; recorded `PresentedDeadlineMs = null` in Schema V7 presentation context. Post-merge validation: 1,936 Core tests passed.
- **P0 (Zero-Answer / `0 + 0` Core-Flow Freeze Blocker)**: Merged via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`). Diagnosed selector terminal-liveness starvation at StoreRevision 5 (practice position 4 -> 5). Added generic terminal New fallback in `AdaptivePracticeSelector.GetFallbackChain` for `Requested Due`, `Requested Maintenance`, and `Requested Frontier`, preserving review priority, curriculum ownership, number-space gating, and immediate-predecessor exclusion. Post-merge validation: 1,917 Core tests passed.
- **MF-TELEM-001 (Tester Telemetry Export & Share)**: Merged via PR #56 (`bbdf62652927efa26475a9f2d83778de6465f5e1`). Delivered Schema V7 persistence with 5 nullable presentation-context columns, pseudonymous persistent random installation UUID, complete-history JSON telemetry export (`telemetry_export_schema_v1`), sandboxed native platform sharing (`telemetry-share`), Full Local Reset cleanup, and learning non-interference regression coverage. Post-merge validation: 1,906 Core tests passed.
- **MF-UX-008 (Static Combat Layout & Boss Presentation)**: Merged via PR #55 (`76116d11b8563b0407188ba53ccefd998eda958d`). Delivered static combat layout positional stability, layout-isolated boss presentation, visual layering, progressive opponent scaling, tier-preserving feedback scale composition, and scoped active-gameplay scroll suppression. Post-merge validation: 1,786 Core tests passed.
- **MF-LEARN-006 (Adaptive Learning Policy & 482 Benchmark)**: Merged via PR #54 (`bc7471b098e2f79262ff6e71302820bd281a14d5`). Delivered Option-B Evidence-Adaptive Discovery, universal no-immediate-fact-repetition invariant ($\text{FactId}(t+1) \ne \text{FactId}(t)$), tiered remediation, Guided Gate G3 soft decoupling at `BandIndex >= 3`, pace calibration readiness at $\ge 24$ positioned Correct attempts, calibrated Cyber Defense Critical Hits, and the exact 482 strong-learner benchmark. Post-merge validation: 1,772 Core tests passed.
- **MF-UX-007 (Progress Presentation Cleanup)**: Merged via PR #47 (`d37fbe3347679220bf847b06c83f7f9366738d03`). Delivered concise learner-facing Stage terminology, returning learner Ready Gate overview, active HUD accessibility, and responsive layout preservation. Post-merge validation: 1,605 Core tests passed.

---

## 3. Downstream Release Context & Planned Sequence

### Release Context:
- **Build 2 Rejection**: Historical. Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to the selector crash/starvation bug on operation reconfiguration and restart, resolved by `MF-STAB-003`.
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, `MF-LEARN-006`, `MF-UX-008`, `MF-TELEM-001`, `P0`, `P1`, `P1b`, `P2`, `P2b`, `P3`, `P4`, and `P5` and no longer represents current repository source.
- **Step 52 Tester Build**: Built from `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f` (`com.tachiguro.mathfirst.tester`, version `1.0 / versionCode 1`, SHA-256 `40e2b5e3f23e90a2dbe695db4d724e4375c5f2ef51709a801fa81cf6e06f4039`), installed and manually validated on Samsung Galaxy S26 Ultra in Steps 53–54 (`STEP_54_MANUAL_VALIDATION_PASS`).
- **Future Production Candidate**:
  - Any future production candidate packaging after pre-Step55 refinement will have `versionCode >= 4`.
  - Build 4 does **not** exist yet (not packaged, not signed, not tested).
  - Roadmap Step 55 production packaging is **NOT AUTHORIZED**.

### Authorized Downstream Project Sequence:
1. P0 is integrated and merged into `main` via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`).
2. P1 is integrated and merged into `main` via PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`).
3. P1b is integrated and merged into `main` via PR #62 (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`).
4. P2 is integrated and merged into `main` via PR #63 (`1b485091755294221b6f242e174d99c168fc8e9d`).
5. P2b is integrated and merged into `main` via PR #64 (`f580a7154a4043a5097ffd852b5cf454be2cc397`).
6. P3 is integrated and merged into `main` via PR #65 (`759389650778f5d7b6a334b15556c5f31f6de5d0`).
7. P4 is integrated and merged into `main` via PR #66 (`8400151ff080caecf024a418a9b6b8ada4873c2d`).
8. P5 is integrated and merged into `main` via PR #67 (`aeb7bc46e8b425d9da95493a367f99f7ed330871`).
9. P6 implementation and review are complete on task branch `feat/p6-tester-diagnostics-release-boundary` (implementation commit `a711c07d80ab2cc3873cbb5a0de96803fabe116b`, review `P6_COMPLETE_REVIEW_APPROVED`). First formal `FULL_VALIDATION` failed at documentation current-state consistency; documentation remediation is followed by review, remediation commit, fresh exact-candidate `FULL_VALIDATION`, push, PR, and manual merge.
10. Downstream P-item sequence ($\text{P8}$) must be completed and merged before Step 55 may be proposed.
11. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. Build 4 does not exist yet (not packaged, not signed, not tested). No production packaging, release signing, ADB, or release action is authorized without explicit user dispatch.
