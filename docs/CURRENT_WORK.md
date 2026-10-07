# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: `P3-DOCUMENTATION-RECONCILIATION` (P3 Cumulative Operation Unlock Progression Documentation Reconciliation).
- **Active Package**: `P3`: Cumulative Operation Unlock Progression.
- **Branch / Upstream State**: Task branch `feat/p3-cumulative-operation-unlock-progression` based on `main` (`f580a7154a4043a5097ffd852b5cf454be2cc397`), `origin/main` at `f580a7154a4043a5097ffd852b5cf454be2cc397`.
- **Implementation & Integration Milestones**:
  - Slice 1: `3465d1e83d287122096a0f4fce1c55224ea178d4` (`feat(domain): add cumulative curriculum unlock policies`) [MathFirst-Checkpoint: P3 slice-1-cumulative-curriculum-unlock-policies]
  - Slice 2: `7a129762a030a88c378c23fdd801262df5fe0dc0` (`feat(learning): persist cumulative curriculum stages`) [MathFirst-Checkpoint: P3 slice-2-persist-cumulative-curriculum-stages]
  - Slice 3: `b7966219bb0719af8172cfd8b2790a9234a7716c` (`feat(settings): show curriculum operation unlock status`) [MathFirst-Checkpoint: P3 slice-3-show-curriculum-operation-unlock-status]
  - Slice 4: `d1e794cf529598e1d57ae39dbe790bc51ede81d5` (`test(p3): harden cumulative progression regressions`) [MathFirst-Checkpoint: P3 slice-4-harden-cumulative-progression-regressions]
- **Live State Discovery**: Live local Git and GitHub repository state always takes precedence over documentation. The live branch HEAD commit SHA, divergence from `origin/main`, working tree status, review state, mergeability, CI/checks, and merge state must be discovered dynamically from:
  - `git status`
  - `git rev-parse HEAD`
  - `git fetch origin`
  - `git log origin/main..HEAD`
  - `gh repo view Tachiguro/MathFirst`
  - `gh pr list --state open`
- **Status of Active Work**:
  - **P3 Implementation & Review Status**: Implementation complete across four feature checkpoint commits. Consolidated package review returned `P3_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 3 Note). Candidate HEAD is `d1e794cf529598e1d57ae39dbe790bc51ede81d5`.
  - **P3 Integration & Procedural State**: Task branch `feat/p3-cumulative-operation-unlock-progression` is local-only (4 commits ahead of `main@f580a7154a4043a5097ffd852b5cf454be2cc397`, unpushed, no open PR). Documentation reconciliation is CURRENT. Formal `FULL_VALIDATION` remains **PENDING**.
  - **Durable P3 Behavioral Contract**:
    1. **Four-Stage Cumulative Progression**:
       - Normal product practice operates under `PracticeMode.CurriculumManaged`.
       - Stage 1: Addition ($+$)
       - Stage 2: Addition ($+$) and Subtraction ($-$)
       - Stage 3: Addition ($+$), Subtraction ($-$), and Multiplication ($\times$)
       - Stage 4: Addition ($+$), Subtraction ($-$), Multiplication ($\times$), and Division ($\div$)
       - Available operations derive from persisted `CurriculumStage`. Legacy operation preferences do NOT control `CurriculumManaged` scheduling.
       - `PracticeMode.Custom` remains available for explicit custom/testing use and continues to use operation preferences.
    2. **Unlock Predicates & Single-Fact Deadlock Tolerance**:
       - Stage 1 $\to$ Stage 2: `ADD-D01` prerequisite frontier fully introduced AND $\le 1$ prerequisite `NeedsRemediation` fact AND aggregate broad weakness false.
       - Stage 2 $\to$ Stage 3: Stage 2 earned AND `SUB-D01` prerequisite frontier fully introduced AND $\le 1$ prerequisite `NeedsRemediation` fact AND aggregate broad weakness false.
       - Stage 3 $\to$ Stage 4: Stage 3 earned AND `MUL-D01` prerequisite frontier fully introduced AND $\le 1$ prerequisite `NeedsRemediation` fact AND aggregate broad weakness false.
       - Unlock does NOT depend on age, grade, onboarding, arbitrary attempt count, response latency, pace calibration, Critical Hit, or fluency by itself.
    3. **Monotonic Curriculum Stage**:
       - `CurriculumStage` never regresses during normal learning. Previously unlocked operations remain unlocked.
       - Aggregate broad weakness ($\ge 2$ eligible `NeedsRemediation` facts across active operations) may block only the *next* stage; it never relocks the current earned stage.
       - Resets to Stage 1 occur only through explicit learning-destructive actions (*Reset Learning Progress*, *Full Local Reset*). *Reset UI Preferences* does NOT alter `CurriculumStage`.
    4. **Schema V9 & Atomic Persistence**:
       - SQLite Schema V9 persists `curriculum_stage` (`INTEGER NOT NULL DEFAULT 1 CHECK (curriculum_stage BETWEEN 1 AND 4)`) in `learner_progression`.
       - Fresh databases initialize at Stage 1.
       - `CurriculumStage` is committed atomically with the accepted submission state (attempt record, item learning state, operation progression, FSRS card state, `PracticePosition`, `StoreRevision`). Failed persistence never publishes an unlock.
    5. **Conservative V8 $\to$ V9 Migration**:
       - Migration stage evidence is cumulative and preference-independent:
         - ADD ready (`BandIndex >= 1` or D01 introduced with $\le 1$ error) $\implies$ Stage 2
         - ADD + SUB ready $\implies$ Stage 3
         - ADD + SUB + MUL ready $\implies$ Stage 4
       - Division history alone, Custom-mode history alone, and preference booleans have zero migration stage authority.
       - Advanced historical data for locked operations is preserved losslessly; dormant learning resumes upon later unlock.
    6. **Guided Number-Space Gating Alignment**:
       - `CurriculumManaged` Stage 3 is semantically Guided: Multiplication is constrained by `AdditionCeiling` until soft decoupling at `MUL BandIndex >= 3` ([ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md)).
       - `CurriculumManaged` Stage 4 follows standard Guided Gate G3 rules.
       - `PracticeMode.Custom` subsets remain Unrestricted under [ADR-0009](decisions/ADR-0009-guided-four-operation-number-space-gate.md); Custom all-four retains Guided behavior.
    7. **Read-Only Settings Curriculum Status**:
       - Settings operation controls display read-only status ("Unlocked" / "Locked") derived from `CurriculumStage`.
       - Settings controls do not mutate operation preference toggles, cannot bypass locked operations, and cannot disable unlocked operations.
    8. **Scheduler Invariants & 482 Normative Benchmark**:
       - Global `PracticePosition` remains authoritative for attempt sequencing.
       - Per-operation role ordinal remains $\text{NextOperationAttemptOrdinal}(O) = \text{AcceptedAttemptCount}(O) + 1$.
       - Stage unlock does not advance `PracticePosition`; new operations apply prospectively.
       - Historical dormant accepted-attempt counts are preserved and govern role ordinals upon unlock.
       - Canonical strong learner 482 benchmark ([ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md)) executes in `PracticeMode.Custom` with all four operations enabled from start, strictly isolating MF-LEARN-006 behavior without modification.
  - **Verification Evidence on P3 Candidate (`d1e794cf529598e1d57ae39dbe790bc51ede81d5`)**:
    - Consolidated review test evidence:
      - P3 focused review: 169 passed, 0 failed, 0 skipped.
      - High-risk adjacent review: 190 passed, 0 failed, 0 skipped.
      - Full Core review: 2,196 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
    - Permanent regression suites: `CurriculumUnlockPolicyTests`, `SchemaV9MigrationTests`, `TrainingSessionUnlockIntegrationTests`, `SettingsUnlockContractTests`, `P3PersonaSimulationRegressionTests`, `P3MonotonicityRegressionTests`, `P3SchedulerTransitionRegressionTests`, `P3LegacyDormantEvidenceRegressionTests`.
    - Key scenario families: Personas A/B/C, M1–M7 monotonicity, scheduler transitions, dormant evidence preservation, Guided G3, persistence atomicity, reset semantics.
    - **Evidence Boundary**: This evidence is implementation/review evidence. Formal `FULL_VALIDATION` remains **PENDING**. No Windows Release build, Android Release build, NuGet vulnerability audit, Markdown link audit, or exact-candidate formal validation is claimed for candidate `d1e794cf529598e1d57ae39dbe790bc51ede81d5`.
- **Prior Merged Work**:
  - **P2b (Gameplay and Startup Refinements)**: Merged via PR #64 at merge commit `f580a7154a4043a5097ffd852b5cf454be2cc397` (validated candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`, `FULL_VALIDATION_PASS` with 2,027 Core tests). Delivered digit-scaled Critical Hit timing ($T_{\text{crit}} = T_{\text{easy}} \times \text{DigitCount}$), shared radar/damage authority (`Session.CurrentFactCriticalHitThresholdMs`), and fresh startup `InitialReadyGate` orientation without active timing before explicit Start.
  - **P2 (Direct-to-Practice Start / Remove Onboarding)**: Merged via PR #63 at merge commit `1b485091755294221b6f242e174d99c168fc8e9d` (validated candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`, 2,014 Core tests).
  - **P1b (Active Thinking Time / Interruption Safety)**: Merged via PR #62 (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`, 2,010 Core tests).
  - **P1 (Normal Practice Without Deadline Failure)**: Merged via PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`, 1,936 Core tests).
  - **P0 (Zero-Answer / `0 + 0` Core-Flow Freeze Blocker)**: Merged via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`, 1,917 Core tests).
- **Downstream Scope**:
  - **P4 (Settings Simplification)**: Downstream. Streamlined settings, remove practice time selection, prevent unlock bypass. P4 does NOT start until P3 is fully validated, reviewed, and merged.
  - **P5** (Cyber Defense Visual Consistency), **P6** (Tester Diagnostics / Telemetry Release Boundary), **P8** (Test Coverage Audit & Targeted Hardening), **P7** (Deferred Game Polish).
  - Roadmap Step 55 is **NOT AUTHORIZED**. Build 4 does **NOT EXIST**.

### 1.1 Pre-Step55 Refinement Program Sequence

The canonical pre-production program ([docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md)) governs downstream work:
1. **P0**: Zero-Answer / `0 + 0` Core-Flow Freeze (`Merged` — PR #60 at `4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`)
2. **P1**: Normal Practice Without Deadline Failure (`Merged` — PR #61 at `4e3ca4943c5809cbe470a4b0ac4f192b24b66795`)
3. **P1b**: Active Thinking Time / Interruption Safety (`Merged` — PR #62 at `a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`)
4. **P2**: Direct-to-Practice Start / Remove Onboarding (`Merged` — PR #63 at `1b485091755294221b6f242e174d99c168fc8e9d`, candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`)
5. **P2b**: Gameplay and Startup Refinements (`Merged` — PR #64 at `f580a7154a4043a5097ffd852b5cf454be2cc397`, candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`)
6. **P3**: Cumulative Operation Unlock Progression (`Implementation Complete & Review Approved` — candidate `d1e794cf529598e1d57ae39dbe790bc51ede81d5`, review `P3_REVIEW_APPROVED`, docs reconciliation current, full validation pending)
7. **P4**: Settings Simplification (streamlined settings, remove practice time selection, prevent unlock bypass — *Downstream*)
8. **P5**: Cyber Defense Visual Consistency (dark technical surfaces, restrained neon/cyber accents — *Downstream*)
9. **P6**: Tester Diagnostics / Telemetry Release Boundary (hard compile/profile boundary isolating Tester diagnostic controls from Distributable UI — *Downstream*)
10. **P8**: Test-Coverage Audit & Targeted Hardening (factual baseline, critical invariant coverage — *Downstream*)
11. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*)

### 1.2 Current Verified Quality State

#### Merged Main Baseline (`main@f580a71` / PR #64 `P2b`)
- **Merge Commit**: `f580a7154a4043a5097ffd852b5cf454be2cc397` (PR #64 `Merge pull request #64 from Tachiguro/feat/p2b-gameplay-startup-refinements`).
- **Merged Candidate**: `11181fe0d3e4b7c752e15815432f66b6c862a61b`.
- **Post-Merge Baseline**: Digit-scaled Critical Hit timing, shared radar/damage authority, fresh startup `InitialReadyGate` pre-attempt orientation without active timing, Schema V8 persistence, Telemetry Schema V2 export; 2,027 Core tests passing in Debug and Release.

#### Verified P3 Quality Evidence (`feat/p3-cumulative-operation-unlock-progression`)
- **Package Commit Chain**:
  - Slice 1 (`3465d1e83d287122096a0f4fce1c55224ea178d4`): `feat(domain): add cumulative curriculum unlock policies`.
  - Slice 2 (`7a129762a030a88c378c23fdd801262df5fe0dc0`): `feat(learning): persist cumulative curriculum stages`.
  - Slice 3 (`b7966219bb0719af8172cfd8b2790a9234a7716c`): `feat(settings): show curriculum operation unlock status`.
  - Slice 4 (`d1e794cf529598e1d57ae39dbe790bc51ede81d5`): `test(p3): harden cumulative progression regressions`.
- **Review Status & Evidence**:
  - Consolidated package review: `P3_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 3 Note).
  - Implementation review test evidence on candidate: 2,196 Core tests passed (0 failed, 0 skipped).
  - Formal `FULL_VALIDATION` remains pending on candidate `d1e794cf529598e1d57ae39dbe790bc51ede81d5`.
  - Packaging boundaries strictly preserved: zero APK/AAB packaging, zero signing, zero store actions.

---

## 2. Historical Merged Implementation Packages

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
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, `MF-LEARN-006`, `MF-UX-008`, `MF-TELEM-001`, `P0`, `P1`, `P1b`, `P2`, and `P2b` and no longer represents current repository source.
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
6. P3 implementation and review are complete on task branch `feat/p3-cumulative-operation-unlock-progression` (candidate `d1e794cf529598e1d57ae39dbe790bc51ede81d5`, review `P3_REVIEW_APPROVED`). Documentation reconciliation and independent review precede full validation rerun, commit, push, and PR.
7. Downstream P-item sequence ($\text{P4} \to \text{P5} \to \text{P6} \to \text{P8}$) must be completed and merged before Step 55 may be proposed.
8. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. Build 4 does not exist yet (not packaged, not signed, not tested). No production packaging, release signing, ADB, or release action is authorized without explicit user dispatch.
