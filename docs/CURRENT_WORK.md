# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: `P2b-DOCUMENTATION-SYNC` (P2b Gameplay and Startup Refinements Documentation Synchronization).
- **Active Package**: `P2b`: Gameplay and Startup Refinements.
- **Branch / Upstream State**: Task branch `feat/p2b-gameplay-startup-refinements` based on `main` (`1b485091755294221b6f242e174d99c168fc8e9d`), `origin/main` at `1b485091755294221b6f242e174d99c168fc8e9d`.
- **Implementation & Integration Milestones**:
  - Slice 1: `33d3552fa619d24a759022096695ac069faa0260` (`feat(combat): digit-scale critical hit window based on answer length`) [MathFirst-Checkpoint: P2b slice-1-digit-scale-critical-hit-window]
  - Slice 2: `cb07353507755712223db58539b48276302410e0` (`feat(combat): align radar and scoring to digit-scaled critical window`) [MathFirst-Checkpoint: P2b slice-2-align-critical-hit-consumers]
  - Slice 3: `11181fe0d3e4b7c752e15815432f66b6c862a61b` (`feat(practice): start fresh practice behind ready gate`) [MathFirst-Checkpoint: P2b slice-3-fresh-start-ready-gate]
- **Live State Discovery**: Live local Git and GitHub repository state always takes precedence over documentation. The live branch HEAD commit SHA, divergence from `origin/main`, working tree status, review state, mergeability, CI/checks, and merge state must be discovered dynamically from:
  - `git status`
  - `git rev-parse HEAD`
  - `git fetch origin`
  - `git log origin/main..HEAD`
  - `gh repo view Tachiguro/MathFirst`
  - `gh pr list --state open`
- **Status of Active Work**:
  - **P2b Implementation & Review Status**: Implementation complete across three feature checkpoint commits. Consolidated package review returned `P2B_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 1 Note). Candidate HEAD is `11181fe0d3e4b7c752e15815432f66b6c862a61b`.
  - **P2b Integration & Procedural State**: Task branch `feat/p2b-gameplay-startup-refinements` is local-only (3 commits ahead of `main@1b485091755294221b6f242e174d99c168fc8e9d`, unpushed, no open PR). Formal `FULL_VALIDATION` remains **PENDING**.
  - **Durable P2b Behavioral Contract**:
    1. **Digit-Scaled Critical Hit Timing**:
       - Product rule: $\text{CriticalHitThresholdMs} = \text{CurrentFactEasyThresholdMs} \times \text{DigitCount}(\text{CurrentFact.CorrectResult})$
       - Canonical answer digit-count boundaries:
         - 0 $\to 1\times$
         - 9 $\to 1\times$
         - 10 $\to 2\times$
         - 99 $\to 2\times$
         - 100 $\to 3\times$
         - 999 $\to 3\times$
         - 1000 $\to 4\times$
       - Gameplay/Presentation Only: Digit-scaled Critical Hit timing is strictly a Cyber Defense gameplay and presentation threshold. It does NOT alter the underlying adaptive learning Easy threshold ($\text{EasyThresholdMs}$), fluency threshold ($\text{FluencyThresholdMs}$), expected pace ($P_{\text{fact}}$), attempt classification (`AdaptiveAttemptClassifier`), FSRS ratings (`FsrsRatingMapper`), FSRS item state, pace shrinkage, curriculum progression, remediation, `PracticePosition`, telemetry export, or persistence.
       - A multi-digit answer may exceed the normal learning Easy threshold, still fall within its expanded Critical Hit gameplay window, score a Cyber Defense Critical Hit, and remain non-Easy/non-fluent in learning telemetry. This distinction is intentional.
    2. **Radar and Scoring Alignment**:
       - Both Cyber Defense radar countdown/arc and Critical Hit damage/scoring decisions consume the same authoritative `Session.CurrentFactCriticalHitThresholdMs`.
       - Critical Hits remain available only after pace calibration readiness ($\ge 24$ timing-eligible positioned Correct attempts).
       - Correct answers outside the Critical Hit window deal standard 1 HP damage.
    3. **Fresh Startup Ready Gate**:
       - Fresh learner: $\text{App} \longrightarrow \text{Practice experience} \longrightarrow \text{InitialReadyGate} \longrightarrow \text{no progress overview} \longrightarrow \text{no active timing} \longrightarrow \text{Start} \longrightarrow \text{Running practice}$
       - Returning learner: $\text{App} \longrightarrow \text{InitialReadyGate} \longrightarrow \text{progress overview} \longrightarrow \text{Start / Resume} \longrightarrow \text{Running practice}$
       - Direct-to-Practice continues to mean: no onboarding/configuration wizard. It no longer means active response timing begins immediately upon application launch.
       - InitialReadyGate represents pre-attempt orientation, not an interruption ($\text{IsTimingActive} = \text{false}$, $\text{ActiveElapsed} = 0$, $\text{IsInterrupted} = \text{false}$). After Start, $\text{PracticeGate} = \text{Running}$ and active timing begins from zero.
       - Manual pause after active timing has begun retains existing P1b interruption semantics; background interruption semantics remain unchanged.
       - Schema remains V8; telemetry export remains Schema V2.
       - No onboarding code was restored; no tutorial system has been implemented.
  - **Verification Evidence on P2b Candidate (`11181fe0d3e4b7c752e15815432f66b6c862a61b`)**:
    - Latest complete Core test suite execution: 2,027 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
    - Slice 1: `CyberDefenseCalibrationGateTests` 20 passed after GREEN; targeted Cyber Defense and adaptive regressions 319 passed.
    - Slice 2: focused Cyber Defense UI + calibration 58 passed; CyberDefense 79 passed; adaptive rating/calibration/non-interference 49 passed; full Core 2,026 passed.
    - Slice 3: `DirectToPracticeStartupTests` 8 passed; adjacent interruption/onboarding/reset/responsive group 92 passed; full Core 2,027 passed.
    - **Evidence Boundary**: This evidence is implementation/review evidence. Formal `FULL_VALIDATION` remains **PENDING**. No Windows Release build, Android Release build, NuGet vulnerability audit, Markdown link audit, or exact-candidate formal validation is claimed for candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`.
  - **Physical Device Test Context**:
    - Pre-P2b physical observation on Samsung SM-S948B / Galaxy S26 Ultra (Android 16 / API 36) from installed source `main@1b485091755294221b6f242e174d99c168fc8e9d`: application launched successfully and general practice operated, but multi-digit Critical Hit window felt unfairly short and fresh startup immediately activated timing. These physical observations motivated P2b.
    - **Physical Acceptance Boundary**: P2b candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b` has NOT yet been packaged or manually validated on the physical device. Zero post-P2b physical device acceptance is claimed.
- **Prior Merged Work**:
  - **P2 (Direct-to-Practice Start / Remove Onboarding)**: Merged via PR #63 at merge commit `1b485091755294221b6f242e174d99c168fc8e9d` (validated candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`). Eliminated 5-step onboarding wizard; fresh learners start directly in practice; system defaults; Addition-only default; domain fallback boundary restored.
  - **P1b (Active Thinking Time / Interruption Safety)**: Merged via PR #62 (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`).
  - **P1 (Normal Practice Without Deadline Failure)**: Merged via PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`).
  - **P0 (Zero-Answer / `0 + 0` Core-Flow Freeze Blocker)**: Merged via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`).
- **Downstream Scope**:
  - **P3 (Cumulative Operation Unlock Progression)**: PLANNED / NEXT. Canonical progression: Stage 1 ($+$), Stage 2 ($+, -$), Stage 3 ($+, -, \times$), Stage 4 ($+, -, \times, \div$).
    - *Known Planning Issue & Requirement*: The earlier proposal suggesting `PreferenceEnabledOperations INTERSECT UnlockedOperations` is flawed because fresh P2 installs start Addition-only; an intersection model would prevent newly unlocked operations from entering practice. The unlock-authority vs learner-filter architecture must be corrected in a dedicated plan refinement before implementation.
  - **P4** (Settings Simplification), **P5** (Cyber Defense Visual Consistency), **P6** (Tester Diagnostics / Telemetry Release Boundary), **P8** (Test Coverage Audit & Targeted Hardening), **P7** (Deferred Game Polish).
  - Roadmap Step 55 is **NOT AUTHORIZED**. Build 4 does **NOT EXIST**.

### 1.1 Pre-Step55 Refinement Program Sequence

The canonical pre-production program ([docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md)) governs downstream work:
1. **P0**: Zero-Answer / `0 + 0` Core-Flow Freeze (`Merged` — PR #60 at `4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`)
2. **P1**: Normal Practice Without Deadline Failure (`Merged` — PR #61 at `4e3ca4943c5809cbe470a4b0ac4f192b24b66795`)
3. **P1b**: Active Thinking Time / Interruption Safety (`Merged` — PR #62 at `a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`)
4. **P2**: Direct-to-Practice Start / Remove Onboarding (`Merged` — PR #63 at `1b485091755294221b6f242e174d99c168fc8e9d`, candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`)
5. **P2b**: Gameplay and Startup Refinements (`Implemented & Review Approved` — candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`, review `P2B_REVIEW_APPROVED`, full validation pending)
6. **P3**: Cumulative Operation Unlock Progression ($+ \to + - \to + - \times \to + - \times \div$; plan refinement required before implementation — *Downstream*)
7. **P4**: Settings Simplification (streamlined settings, remove practice time selection, prevent unlock bypass — *Downstream*)
8. **P5**: Cyber Defense Visual Consistency (dark technical surfaces, restrained neon/cyber accents — *Downstream*)
9. **P6**: Tester Diagnostics / Telemetry Release Boundary (hard compile/profile boundary isolating Tester diagnostic controls from Distributable UI — *Downstream*)
10. **P8**: Test-Coverage Audit & Targeted Hardening (factual baseline, critical invariant coverage — *Downstream*)
11. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*)

### 1.2 Current Verified Quality State

#### Merged Main Baseline (`main@1b48509` / PR #63 `P2`)
- **Merge Commit**: `1b485091755294221b6f242e174d99c168fc8e9d` (PR #63 `Merge pull request #63 from Tachiguro/feat/p2-direct-to-practice`).
- **Merged Candidate**: `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`.
- **Post-Merge Baseline**: Onboarding wizard removed, fresh installs default to Addition only, Numpad keypad, System theme/language, haptics enabled; Schema V8 persistence, Telemetry Schema V2 export; domain fallback decoupled from application defaults.

#### Verified P2b Quality Evidence (`feat/p2b-gameplay-startup-refinements`)
- **Package Commit Chain**:
  - Slice 1 (`33d3552fa619d24a759022096695ac069faa0260`): `feat(combat): digit-scale critical hit window based on answer length`.
  - Slice 2 (`cb07353507755712223db58539b48276302410e0`): `feat(combat): align radar and scoring to digit-scaled critical window`.
  - Slice 3 (`11181fe0d3e4b7c752e15815432f66b6c862a61b`): `feat(practice): start fresh practice behind ready gate`.
- **Review Status & Evidence**:
  - Consolidated package review: `P2B_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 1 Note).
  - Implementation test evidence on candidate: 2,027 Core tests passed (0 failed, 0 skipped).
  - Formal `FULL_VALIDATION` remains pending on candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`.
  - Packaging boundaries strictly preserved: zero APK/AAB packaging, zero signing, zero store actions.

---

## 2. Historical Merged Implementation Packages

- **P2 (Direct-to-Practice Start / Remove Onboarding)**: Merged via PR #63 (`1b485091755294221b6f242e174d99c168fc8e9d`, validated candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`). Eliminated 5-step onboarding wizard; fresh learners launch directly into practice; default preferences set to System language/theme, Numpad, haptics enabled, Addition only; restored domain fallback boundary (`PracticeOperationPreferencePolicy.NormalizeEnabledOperations` $\to$ `AllOperations`); historical failed candidate `42d8c0884ad3350cad5774ecd5b0098d13ed3e74` (hang on domain fallback conflation) diagnosed and fixed via `085b929f058eb1ba4477f2bdc1412a09c218d648`; final candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd` validated and merged.
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
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, `MF-LEARN-006`, `MF-UX-008`, `MF-TELEM-001`, `P0`, `P1`, `P1b`, and `P2` and no longer represents current repository source.
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
5. P2b implementation and review are complete on task branch `feat/p2b-gameplay-startup-refinements` (candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`, review `P2B_REVIEW_APPROVED`). Documentation reconciliation and independent review precede full validation rerun, commit, push, and PR.
6. Downstream P-item sequence ($\text{P3} \to \text{P4} \to \text{P5} \to \text{P6} \to \text{P8}$) must be completed and merged before Step 55 may be proposed.
7. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. Build 4 does not exist yet (not packaged, not signed, not tested). No production packaging, release signing, ADB, or release action is authorized without explicit user dispatch.
