# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: `P0-PR60-MERGE-SAFE-DOCUMENTATION-RECONCILIATION` (P0 PR #60 Merge-Safe Documentation Reconciliation).
- **Active Package**: `P0`: Zero-Answer / `0 + 0` Core-Flow Freeze (Selector Terminal-Liveness Starvation).
- **Integration Vehicle**: Pull Request [#60](https://github.com/Tachiguro/MathFirst/pull/60) (`fix: preserve selector liveness with terminal new fallback`) on remote task branch `diag/p0-zero-answer-runtime-trace`.
- **Live State Discovery**: Live local Git and GitHub repository state always takes precedence over documentation. The live integration status and current lifecycle step must be discovered dynamically from:
  - `git status`
  - `git rev-parse HEAD`
  - `git fetch origin`
  - `gh pr view 60 --repo Tachiguro/MathFirst`
  - `gh pr list --state open`
- **Status of Active Work**:
  - **P0 Technical Status**: P0 investigation, exact root-cause reproduction (`NARROWLY_PROVEN`), TDD RED confirmation (`TDD_RED_CONFIRMED`), production fix (`AdaptivePracticeSelector.GetFallbackChain`, `P0_SELECTOR_FIX_GREEN_CONFIRMED`), diagnostic cleanup (`P0_DIAGNOSTIC_CLEANUP_COMPLETE`), and final full validation (`P0_FULL_VALIDATION_PASS`) are complete.
  - **Repository Integration Milestones**:
    - Initial implementation commit: `37c075b0f17ecdf15d7fabed648b82ba3710fef9` (`fix: preserve selector liveness with terminal new fallback`).
    - Remote task branch: `diag/p0-zero-answer-runtime-trace`.
    - Pull Request: [#60](https://github.com/Tachiguro/MathFirst/pull/60).
    - Historical Review Milestone: Initial PR review confirmed that code and permanent regression tests are merge-safe, but required a documentation-only reconciliation before merge (`PR_CHANGES_REQUIRED`) to remove stale pre-commit/pre-PR lifecycle statements.
    - Live PR / Merge State: Tracked through PR #60; live state (open, review status, mergeability) is derived dynamically from GitHub.
  - **P0 Root Cause & Diagnosis**: The apparent core-flow freeze on physical hardware after entering `0` on `0 + 0` was not caused by the numeric answer 0 or the fact `add:0+0` itself (zero was not rejected, auto-submission completed, and 0 itself did not cause the freeze). The true failure was selector terminal-liveness starvation:
    - In fresh four-operation practice, Task 1 (`mul:0*0`, submitted 0, Correct, 4042 ms, non-fluent, FSRS Hard) was followed by Task 2 (`sub:0-0`, submitted 1, Incorrect, 7928 ms, FSRS Again) and Task 3 (`div:0/1`, Timeout, 15070 ms, FSRS Again).
    - Subtraction and Division both contained unresolved remediation work, triggering broad weakness (`HasBroadWeakness = true`).
    - Task 4 was Addition (`add:0+0`, submitted 0, Correct, 3314 ms, fluent, FSRS Good).
    - Following Task 4, at PracticePosition 4 / StoreRevision 5, the deterministic operation scheduler legitimately scheduled Addition again for prospective position 5 across a permutation bag boundary.
    - Addition had 1 prior accepted attempt, making prospective Addition accepted-attempt ordinal 2 (`Requested Due`).
    - The only materialized Addition fact was `add:0+0`, which was the immediate predecessor and was therefore hard-excluded by the universal no-immediate-fact-repetition invariant ($\text{FactId}(t+1) \ne \text{FactId}(t)$).
    - Broad weakness suppressed the opportunistic New-promotion path.
    - Non-New fallback chains (`Requested Due`, `Requested Maintenance`, `Requested Frontier`) did not include New.
    - The selector exhausted all valid materialized candidates and threw `InvalidOperationException`, terminating practice selection despite eligible unmaterialized Addition facts existing in the curriculum.
  - **Integration & Procedural Routing**:
    - P0 is in repository integration via PR #60.
    - Exact lifecycle phase must be discovered dynamically from live Git and GitHub state.
    - Procedural action following this documentation reconciliation edit: stage and commit the reconciled documentation files, push to `diag/p0-zero-answer-runtime-trace`, await PR re-review (`REVIEW_PR`), and await `MERGE_DECISION` and explicit affirmative user authorization before merge.
    - Downstream work (P1) requires PR #60 integration to be complete on `main` and separate explicit user dispatch.
  - **Downstream Scope**: P1 (Normal Practice Without Deadline Failure) is downstream work and is **NOT** authorized by completing P0.
  - **Roadmap Step 55 Status**: **NOT EXECUTED / NOT AUTHORIZED** (explicitly declined by user after Step 54; deferred pending pre-Step55 refinement program; no production AAB, no versionCode 4, no release signing, no Google Play upload).
  - **Completed Testing Steps (Steps 51–54)**:
    - **Step 51 (Final V1 Gap Audit)**: `STEP_51_READY_FOR_STEP_52` (read-only audit of `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f`, zero Step-52 blockers found).
    - **Step 52 (Tester APK Packaging & Offline Validation)**: `STEP_52_TESTER_APK_PASS` (built from `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f`, package `com.tachiguro.mathfirst.tester`, version `1.0 / versionCode 1`, APK SHA-256 `40e2b5e3f23e90a2dbe695db4d724e4375c5f2ef51709a801fa81cf6e06f4039`, development/debug signed, repository remained clean).
    - **Step 53 (Physical Installation on S26 Ultra)**: `STEP_53_INSTALL_PASS` (Samsung SM-S948B / m3q, Android 16 / API 36, package `com.tachiguro.mathfirst.tester`, version `1.0 / versionCode 1`, production package unmodified).
    - **Step 54 (Manual Physical-Device Validation)**: `STEP_54_MANUAL_VALIDATION_PASS` (physical device Samsung Galaxy S26 Ultra; manual validation covered launch, practice, correctness, pause/resume, Cyber Defense combat presentation, multi-question continuity, background/resume, Settings, Privacy, telemetry export/share, localization; zero manual findings reported; source repository unchanged).
  - **Established Refinement Plan**: Canonical pre-production program documented in [docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md).

### 1.1 Pre-Step55 Refinement Program Sequence

The canonical pre-production program ([docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md)) governs downstream work:
1. **P0**: Zero-Answer / `0 + 0` Core-Flow Freeze (`P0 Blocker` — implemented and fully validated (`P0_FULL_VALIDATION_PASS`); repository integration tracked via PR #60)
2. **P1**: Normal Practice Without Deadline Failure (remove timeout failure, preserve response latency & pace modeling — *Downstream / Not Started / Not Authorized*)
3. **P1b**: Active Thinking Time / Interruption Safety (pause on interruptions, neutralize contaminated latency — *Downstream*)
4. **P2**: Direct-to-Practice Start / Remove Onboarding (direct launch, system defaults, Addition only — *Downstream*)
5. **P3**: Cumulative Operation Unlock Progression ($+ \to + - \to + - \times \to + - \times \div$; demonstrated mathematical evidence, 3 simulation personas — *Downstream*)
6. **P4**: Settings Simplification (streamlined settings, remove practice time selection, prevent unlock bypass — *Downstream*)
7. **P5**: Cyber Defense Visual Consistency (dark technical surfaces, restrained neon/cyber accents — *Downstream*)
8. **P6**: Tester Diagnostics / Telemetry Release Boundary (hard compile/profile boundary isolating Tester diagnostic controls from Distributable UI — *Downstream*)
9. **P8**: Test-Coverage Audit & Targeted Hardening (factual baseline, critical invariant coverage — *Downstream*)
10. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*)

### 1.2 Current Verified Quality State

#### Historical Merged Baseline (`main@cb15d1c` / PR #56 `MF-TELEM-001`)
- **Complete-Package Review**: `REVIEW_PASS` (`MF-TELEM-001` completed with zero Blocker, zero Major, zero Minor findings).
- **Merge Commit**: `bbdf62652927efa26475a9f2d83778de6465f5e1` (PR #56).
- **Post-Merge Verification**: `POST_MERGE_SYNC_COMPLETE` on canonical checkout `C:\Dev\MathFirst`; post-merge Core tests: `1,906 passed, 0 failed, 0 skipped`; Android and Windows builds: `0 warnings, 0 errors`.
- **Benchmark & Determinism**: Exact 482 strong-learner benchmark (`ADD-D10 -> ADD-P1-ANCHOR` at global accepted attempt 482; 121 Addition attempts), real-SQLite restart equivalence, and cold-restart next-selection determinism locked.
- **Persistence Contract**: Schema V7 live.

#### Verified P0 Validation Evidence (`diag/p0-zero-answer-runtime-trace` / PR #60)
- **Root Cause Verified**: `NARROWLY_PROVEN` selector terminal-liveness starvation at StoreRevision 5 (practice position 4 -> 5). Not caused by the numeric answer 0 or the fact `add:0+0` itself.
- **TDD RED Confirmed**: `TDD_RED_CONFIRMED` on exact revision-5 reproduction across 13 failing characterization and regression cases prior to fix.
- **Production Fix Green**: `P0_SELECTOR_FIX_GREEN_CONFIRMED` after implementing generic terminal New fallback in `AdaptivePracticeSelector.GetFallbackChain`.
- **Exact Live & Restart Verification**: Exact live revision-5 fix GREEN; exact cold-restart revision-5 fix GREEN.
- **Exact Characterization**: 4 / 4 passed (`EarlyStateSchedulerFailureCharacterizationTests`).
- **Permanent Regression Fixtures**: 144 / 144 passed across 5 permanent regression test fixtures (`DeterministicSelectorTerminalLivenessTests`, `IndependentSelectorTests`, `NoImmediateFactRepetitionTests`, `TieredRemediationAndBroadWeaknessTests`, `EarlyStateSchedulerFailureCharacterizationTests`).
- **Broader Selector / Scheduler Regressions**: 187 / 187 passed across broader selector and scheduler regression suites.
- **Diagnostic Cleanup**: `P0_DIAGNOSTIC_CLEANUP_COMPLETE`. All temporary physical-device runtime tracing and temporary diagnostic test methods removed (exactly 9 temporary diagnostic tests removed; pre-cleanup 1,926 count is historical only); runtime files restored exactly to pre-diagnostic HEAD state.
- **Full Permanent Core Suite**: 1,917 / 1,917 passed (0 failed, 0 skipped).
- **Windows Release Build**: `net10.0-windows10.0.19041.0` Release build SUCCESS (0 warnings, 0 errors).
- **Android Release Compilation**: `net10.0-android36.0` Release compile-only (Target `Compile`) SUCCESS (0 warnings, 0 errors).
- **Packaging Boundary**: Zero APK/AAB packaging performed; zero signing; zero deployment; zero ADB/device/emulator actions; no repository artifacts introduced.
- **Final Full Validation Acceptance**: `P0_FULL_VALIDATION_PASS` (30 / 30 acceptance criteria passed).
- **Repository Integration Status**: Technical implementation and full validation are complete (`P0_FULL_VALIDATION_PASS`). Initial implementation commit `37c075b0f17ecdf15d7fabed648b82ba3710fef9` was integrated onto branch `diag/p0-zero-answer-runtime-trace` and opened as PR #60. Current integration, review, and merge status are tracked through PR #60 and verified dynamically via GitHub.

---

## 2. Historical Merged Implementation Packages

- **MF-UX-008 (Static Combat Layout & Boss Presentation)**: Merged via PR #55 (`76116d11b8563b0407188ba53ccefd998eda958d`). Delivered static combat layout positional stability (keypad and arithmetic typography stationary across combat transitions), layout-isolated boss presentation (`clamp(90px, 16vh, 140px)`), visual layering (`z-index: 4` + scrim protection), progressive opponent scaling (0.65 to 1.50), tier-preserving feedback scale composition (`SCALE_PRESERVED_ACROSS_ALL_STATES`), and scoped active-gameplay scroll suppression (`.training-host.active-gameplay`). Post-merge validation: 1,786 Core tests passed.
- **MF-LEARN-006 (Adaptive Learning Policy & 482 Benchmark)**: Merged via PR #54 (`bc7471b098e2f79262ff6e71302820bd281a14d5`). Delivered Option-B Evidence-Adaptive Discovery, universal no-immediate-fact-repetition invariant ($\text{FactId}(t+1) \ne \text{FactId}(t)$), tiered remediation (cooldown 4 / 2; broad weakness threshold 2), Guided Gate G3 soft decoupling at `BandIndex >= 3`, pace calibration readiness at $\ge 24$ positioned Correct attempts, calibrated Cyber Defense Critical Hits, and the exact 482 strong-learner benchmark. Post-merge validation: 1,772 Core tests passed.
- **MF-UX-007 (Progress Presentation Cleanup)**: Merged via PR #47 (`d37fbe3347679220bf847b06c83f7f9366738d03`). Delivered concise learner-facing Stage terminology, returning learner Ready Gate overview, active HUD accessibility (`Training_OperationProgressGroupAriaLabel`), and responsive layout preservation. Post-merge validation: 1,605 Core tests passed.

---

## 3. Downstream Release Context & Planned Sequence

### Release Context:
- **Build 2 Rejection**: Historical. Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to the selector crash/starvation bug on operation reconfiguration and restart, resolved by `MF-STAB-003`.
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, `MF-LEARN-006`, `MF-UX-008`, and `MF-TELEM-001` and no longer represents current repository source.
- **Step 52 Tester Build**: Built from `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f` (`com.tachiguro.mathfirst.tester`, version `1.0 / versionCode 1`, SHA-256 `40e2b5e3f23e90a2dbe695db4d724e4375c5f2ef51709a801fa81cf6e06f4039`), installed and manually validated on Samsung Galaxy S26 Ultra in Steps 53–54 (`STEP_54_MANUAL_VALIDATION_PASS`).
- **Future Production Candidate**:
  - Any future production candidate packaging after pre-Step55 refinement will have `versionCode >= 4`.
  - Build 4 does **not** exist yet (not packaged, not signed, not tested).
  - Roadmap Step 55 production packaging is **NOT AUTHORIZED**.

### Authorized Downstream Project Sequence:
1. P0 repository integration proceeds through PR #60 on branch `diag/p0-zero-answer-runtime-trace`.
2. Live integration lifecycle phase (commit, push, re-review, merge decision, merge) must be verified dynamically from Git and GitHub state.
3. Merging PR #60 requires a successful re-review verdict (`PR_REVIEW_PASS`), `MERGE_DECISION`, and explicit affirmative user merge authorization.
4. P1 must not begin until repository truth confirms P0 integration is complete on `main` and a separate user dispatch authorizes P1.
5. Downstream P-item sequence ($\text{P0} \to \text{P1} \to \text{P1b} \to \text{P2} \to \text{P3} \to \text{P4} \to \text{P5} \to \text{P6} \to \text{P8}$) must be completed and merged before Step 55 may be proposed.
6. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. Build 4 does not exist yet (not packaged, not signed, not tested). No production packaging, release signing, ADB, or release action is authorized without explicit user dispatch.
