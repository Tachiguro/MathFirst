# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: `P4-DOCUMENTATION-RECONCILIATION` (P4 Settings Simplification Documentation Reconciliation).
- **Active Package**: `P4`: Settings Simplification.
- **Branch / Upstream State**: Task branch `feat/p4-settings-simplification` based on `main` (`759389650778f5d7b6a334b15556c5f31f6de5d0`), `origin/main` at `759389650778f5d7b6a334b15556c5f31f6de5d0` (0 ahead / 0 behind).
- **Implementation & Integration Milestones**:
  - Slice 1: Practice Time UI removal & localization cleanup (implemented & review-approved).
  - Slice 2: Permanent contract coverage `SettingsSimplificationContractTests.cs` (implemented & review-approved).
  - Complete Candidate Review: Consolidated package review returned `P4_COMPLETE_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 0 Note).
  - Working Tree State: Uncommitted review-approved candidate in working tree.
- **Live State Discovery**: Live local Git and GitHub repository state always takes precedence over documentation. The live branch HEAD commit SHA, divergence from `origin/main`, working tree status, review state, mergeability, CI/checks, and merge state must be discovered dynamically from:
  - `git status`
  - `git rev-parse HEAD`
  - `git fetch origin`
  - `git log origin/main..HEAD`
  - `gh repo view Tachiguro/MathFirst`
  - `gh pr list --state open`
- **Status of Active Work**:
  - **P4 Implementation & Review Status**: Implementation complete. Consolidated package review returned `P4_COMPLETE_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 0 Note). 2,204 Core review tests passing (0 failed, 0 skipped).
  - **P4 Integration & Procedural State**: Candidate remains uncommitted on task branch `feat/p4-settings-simplification` (0 ahead / 0 behind `main@759389650778f5d7b6a334b15556c5f31f6de5d0`, unpushed, no open PR). Documentation reconciliation is CURRENT. Next later lifecycle steps: documentation review (`REVIEW_ONLY`), `COMMIT_ONLY`, formal `FULL_VALIDATION`, `PUSH_ONLY`, `PR_ONLY`, manual merge, `POST_MERGE_SYNC_ONLY`. Formal `FULL_VALIDATION` remains **PENDING**.
  - **Durable P4 Behavioral Contract**:
    1. **Removal of Practice Time Settings UI**:
       - Normal Settings no longer exposes Practice Time selection.
       - Removed user-facing choices: Standard, No Time Pressure, 30 seconds, 45 seconds, 60 seconds.
    2. **Durable Settings Surface Retention**:
       - Normal Settings retains: Language, Theme / Appearance, arithmetic operation status, Keypad layout, Haptic feedback, Privacy Policy, Reset Learning Progress, Restore Default Settings, Full Local Reset, Version / Build information, current Tester diagnostics / telemetry controls.
    3. **Read-Only Arithmetic Operation Status**:
       - Settings displays Locked / Unlocked status derived strictly from `CurriculumUnlockPolicy` and `CurriculumStage`.
       - Cannot toggle operations; cannot bypass locked operations; cannot disable unlocked `CurriculumManaged` operations; contains no optional operation filter.
    4. **CurriculumStage Sole Authority**:
       - `CurriculumStage` remains the sole operation-unlock authority for `PracticeMode.CurriculumManaged`.
    5. **Custom Mode Non-Interference**:
       - `PracticeMode.Custom` remains explicit preference-driven behavior for tests/diagnostics.
    6. **Lower-Level Practice Time Plumbing Retained**:
       - Retains `PracticeTimeSetting`, `PracticeTimePreferencePolicy`, `IPreferenceStore` practice-time methods, `MauiPreferenceStore` persistence, and `TrainingSession` timing plumbing for compatibility, synthetic test fixtures, and future specialized modes.
    7. **Persisted Value Harmlessness**:
       - Existing persisted Practice Time preference values are harmless; no migration required.
    8. **Reset Behavior Unchanged**:
       - Reset Learning Progress, Restore Default Settings, and Full Local Reset preserve their established semantics.
    9. **P1/P1b Timing Behavior Unchanged**:
       - Normal practice remains deadline-free without forced timeouts (`HasEnforcedDeadline = false`).
    10. **Schema V9 Unchanged**:
        - Schema V9 persistence is unchanged.
    11. **No New ADR**:
        - No ADR was required for P4 because the architectural and product direction was already established by existing ADRs and the refinement plan.
  - **Verification Evidence on P4 Candidate**:
    - Consolidated review test evidence:
      - Dedicated P4 contract suite (`SettingsSimplificationContractTests.cs`): 8 passed, 0 failed.
      - Focused Settings / Reset / Config set: 220 passed, 0 failed.
      - P3 high-risk set: 169 passed, 0 failed.
      - P1/P1b adjacent set: 107 passed, 0 failed.
      - Full Core review: 2,204 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
      - `git diff --check`: PASS.
    - Permanent contract suite: `tests/MathFirst.Core.Tests/SettingsSimplificationContractTests.cs` (8 tests covering UI absence, plumbing retention, Settings surface retention, read-only operation status, CurriculumManaged authority, Custom mode non-interference, reset semantics, and pause/resume lifecycle).
    - **Evidence Boundary**: This evidence is implementation/review evidence. Formal `FULL_VALIDATION` remains **PENDING**. No Windows Release build, Android Release build, NuGet vulnerability audit, Markdown link audit, or exact-candidate formal validation is claimed for the uncommitted candidate.
- **Prior Merged Work**:
  - **P3 (Cumulative Operation Unlock Progression)**: Merged via PR #65 at merge commit `759389650778f5d7b6a334b15556c5f31f6de5d0` (validated candidate `d1e794cf529598e1d57ae39dbe790bc51ede81d5`, 2,196 Core tests). Delivered four-stage cumulative progression ($+ \to + - \to + - \times \to + - \times \div$), monotonic `CurriculumStage` in `learner_progression`, Schema V9 persistence, tolerant D01 unlock predicates, aggregate broad weakness gating, Guided G3 soft decoupling, and read-only Settings operation status in `CurriculumManaged`.
  - **P2b (Gameplay and Startup Refinements)**: Merged via PR #64 at merge commit `f580a7154a4043a5097ffd852b5cf454be2cc397` (validated candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`, `FULL_VALIDATION_PASS` with 2,027 Core tests). Delivered digit-scaled Critical Hit timing ($T_{\text{crit}} = T_{\text{easy}} \times \text{DigitCount}$), shared radar/damage authority (`Session.CurrentFactCriticalHitThresholdMs`), and fresh startup `InitialReadyGate` orientation without active timing before explicit Start.
  - **P2 (Direct-to-Practice Start / Remove Onboarding)**: Merged via PR #63 at merge commit `1b485091755294221b6f242e174d99c168fc8e9d` (validated candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`, 2,014 Core tests).
  - **P1b (Active Thinking Time / Interruption Safety)**: Merged via PR #62 (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`, 2,010 Core tests).
  - **P1 (Normal Practice Without Deadline Failure)**: Merged via PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`, 1,936 Core tests).
  - **P0 (Zero-Answer / `0 + 0` Core-Flow Freeze Blocker)**: Merged via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`, 1,917 Core tests).
- **Downstream Scope**:
  - **P5 (Cyber Defense Visual Consistency)**: Downstream. Dark technical surfaces, restrained neon/cyber accents. P5 does NOT start until P4 is fully validated, reviewed, committed, pushed, PR-merged, and synchronized.
  - **P6** (Tester Diagnostics / Telemetry Release Boundary), **P8** (Test Coverage Audit & Targeted Hardening), **P7** (Deferred Game Polish).
  - Roadmap Step 55 is **NOT AUTHORIZED**. Build 4 does **NOT EXIST**.

### 1.1 Pre-Step55 Refinement Program Sequence

The canonical pre-production program ([docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md)) governs downstream work:
1. **P0**: Zero-Answer / `0 + 0` Core-Flow Freeze (`Merged` — PR #60 at `4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`)
2. **P1**: Normal Practice Without Deadline Failure (`Merged` — PR #61 at `4e3ca4943c5809cbe470a4b0ac4f192b24b66795`)
3. **P1b**: Active Thinking Time / Interruption Safety (`Merged` — PR #62 at `a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`)
4. **P2**: Direct-to-Practice Start / Remove Onboarding (`Merged` — PR #63 at `1b485091755294221b6f242e174d99c168fc8e9d`, candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`)
5. **P2b**: Gameplay and Startup Refinements (`Merged` — PR #64 at `f580a7154a4043a5097ffd852b5cf454be2cc397`, candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`)
6. **P3**: Cumulative Operation Unlock Progression (`Merged` — PR #65 at `759389650778f5d7b6a334b15556c5f31f6de5d0`, candidate `d1e794cf529598e1d57ae39dbe790bc51ede81d5`)
7. **P4**: Settings Simplification (`Implementation Complete & Review Approved` — uncommitted candidate on `feat/p4-settings-simplification`, review `P4_COMPLETE_REVIEW_APPROVED`, docs reconciliation current, full validation pending)
8. **P5**: Cyber Defense Visual Consistency (dark technical surfaces, restrained neon/cyber accents — *Downstream*)
9. **P6**: Tester Diagnostics / Telemetry Release Boundary (hard compile/profile boundary isolating Tester diagnostic controls from Distributable UI — *Downstream*)
10. **P8**: Test-Coverage Audit & Targeted Hardening (factual baseline, critical invariant coverage — *Downstream*)
11. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*)

### 1.2 Current Verified Quality State

#### Merged Main Baseline (`main@7593896` / PR #65 `P3`)
- **Merge Commit**: `759389650778f5d7b6a334b15556c5f31f6de5d0` (PR #65 `Merge pull request #65 from Tachiguro/feat/p3-cumulative-operation-unlock-progression`).
- **Merged Candidate**: `d1e794cf529598e1d57ae39dbe790bc51ede81d5`.
- **Post-Merge Baseline**: Four-stage cumulative progression ($+ \to + - \to + - \times \to + - \times \div$), monotonic `CurriculumStage` in `learner_progression`, Schema V9 persistence, tolerant D01 unlock predicates, aggregate broad weakness gating, Guided G3 soft decoupling, read-only Settings operation status in `CurriculumManaged`, 2,196 Core tests passing in Debug and Release.

#### Verified P4 Quality Evidence (`feat/p4-settings-simplification`)
- **Candidate State**:
  - Slice 1: Practice Time UI removal & localization cleanup.
  - Slice 2: Permanent contract suite `SettingsSimplificationContractTests.cs`.
- **Review Status & Evidence**:
  - Consolidated package review: `P4_COMPLETE_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 0 Note).
  - Implementation review test evidence on candidate: 2,204 Core tests passed (0 failed, 0 skipped).
  - Formal `FULL_VALIDATION` remains pending on the uncommitted candidate.
  - Packaging boundaries strictly preserved: zero APK/AAB packaging, zero signing, zero store actions.

---

## 2. Historical Merged Implementation Packages

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
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, `MF-LEARN-006`, `MF-UX-008`, `MF-TELEM-001`, `P0`, `P1`, `P1b`, `P2`, `P2b`, and `P3` and no longer represents current repository source.
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
7. P4 implementation and review are complete on task branch `feat/p4-settings-simplification` (uncommitted candidate, review `P4_COMPLETE_REVIEW_APPROVED`). Documentation reconciliation and independent review precede full validation rerun, commit, push, and PR.
8. Downstream P-item sequence ($\text{P5} \to \text{P6} \to \text{P8}$) must be completed and merged before Step 55 may be proposed.
9. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. Build 4 does not exist yet (not packaged, not signed, not tested). No production packaging, release signing, ADB, or release action is authorized without explicit user dispatch.
