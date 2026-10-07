# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Current Program Position**: MF-AUDIT-002 / P8 (Test-Coverage Audit & Targeted Hardening) is the active package. Implementation is complete across three slices (`76e718006c2adc5953995d99bbfc1451dc8e63c1`, `56880b3ff3da2c6e06840226ac236dafedba34bd`, `3558f8cee7b3aad031459990276ff99d73312379`), consolidated package review is approved (`MF_AUDIT_002_REVIEW_APPROVED`), corrected apples-to-apples Cobertura coverage evidence is recorded (`PRIOR COVERAGE DELTA CORRECTED`), and documentation reconciliation is complete. Current integration state must be discovered dynamically from live Git and GitHub repository state; this document does not encode transient commit, validation, push, PR, merge, or synchronization status.
- **Active Package**: `MF-AUDIT-002` — Test-Coverage Audit & Targeted Hardening.
- **Repository State & Synchronization Anchor**:
  - Live local Git and GitHub repository state always takes precedence over documentation baselines.
  - Verified base main: `4ba870ad0bd6c516e74d2000a8f5c0878fb609a5` (PR #69 Post-P6 Merge State Reconciliation; P6 merged via PR #68 at `049ec1d5d3859a139f8d5493d6dae7607d321b02`).
  - Active task branch: `feat/mf-audit-002-test-coverage-hardening`.
  - Historical implementation checkpoints:
    - Slice 1: `76e718006c2adc5953995d99bbfc1451dc8e63c1` (`test: harden critical domain invariants`).
    - Slice 2: `56880b3ff3da2c6e06840226ac236dafedba34bd` (`test: harden persistence and long-run invariants`).
    - Slice 3: `3558f8cee7b3aad031459990276ff99d73312379` (`test: harden release and security boundaries`).
- **Completed P8 Milestones**:
  - Slice 1 Implementation: Hardened domain invariants across `BroadWeaknessPolicyTests`, `CurriculumUnlockPolicyTests`, `CurriculumInvariantPropertyTests`, and `DeterministicOperationSchedulerTests` (`MathFirst-Checkpoint: MF-AUDIT-002 1/3 critical-domain-invariants`).
  - Slice 2 Implementation: Hardened persistence, recovery, and long-run invariants across `SqlitePersistenceConformanceTests`, `PersistenceRecoveryAndLifecycleTests`, and `LongRunIndependentProgressionTests` (`MathFirst-Checkpoint: MF-AUDIT-002 2/3 persistence-recovery-long-run`).
  - Slice 3 Implementation: Hardened ReleaseTool CLI fail-closed boundaries in `ReleaseCliFailClosedContractTests` (`MathFirst-Checkpoint: MF-AUDIT-002 3/3 release-security-reset`).
  - Consolidated Package Review: Review verdict returned `MF_AUDIT_002_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit; 0 confirmed production defects; 0 unresolved P0/P1 gaps).
  - Coverage Remediation Decision: Adopted normalized apples-to-apples Cobertura delta (`PRIOR COVERAGE DELTA CORRECTED`).
- **Live State Discovery**: Live local Git and GitHub repository state always takes precedence over documentation. The live branch HEAD commit SHA, divergence from `origin/main`, working tree status, review state, mergeability, CI/checks, and merge state must be discovered dynamically from:
  - `git status`
  - `git rev-parse HEAD`
  - `git fetch origin`
  - `git log origin/main..HEAD`
  - `gh repo view Tachiguro/MathFirst`
  - `gh pr list --state open`
- **Durable MF-AUDIT-002 Technical Contracts & Invariants**:
  1. **Domain Invariant Hardening (Slice 1)**:
     - `BroadWeaknessPolicy`: Evaluates NeedsRemediation eligibility, active-operation filtering, missing-progression exclusion, acquisition ownership/band gating, Guided number-space gate filtering, multi-operation aggregation, and exact uncapped counts.
     - `CurriculumUnlockPolicy`: Direct boundary and error coverage for public helpers, invalid-stage fail-closed handling, and terminal Stage 4 behavior.
     - Canonical arithmetic property tests: Validated addition ($a + b$), non-negative exact subtraction, exact multiplication with zero-safe inverse checks, and exact division with positive divisors, exact divisibility, and multiplication inverse.
     - Scheduler rank & tie-break: Primary rank key, secondary deterministic bytes, operation enum fallback, and digest-length validation.
  2. **Persistence, Recovery & Long-Run Hardening (Slice 2)**:
     - Real SQLite mid-transaction rollback: Verified via deterministic SQLite trigger that write failures after meaningful row writes roll back completely, preserving durable state and allowing subsequent valid commits to succeed.
     - Corrupted `CurriculumStage` fail-closed: Verified transactional commit read path fails closed upon encountering out-of-range persisted stage values without partial persistence.
     - Repeated transient persistence recovery: Verified Unavailable $\to$ Unavailable $\to$ Success lifecycle retains in-flight evaluation and applies state exactly once.
     - Multi-stage `CurriculumManaged` long-run: Verified monotonic Stage 1 $\to$ Stage 2 $\to$ Stage 3 $\to$ Stage 4 progression, cumulative unlock behavior, `PracticePosition` continuity, anti-repetition invariants, selector liveness, and durable restart across stage boundaries (distinguished from pre-existing 2,000-turn Custom mode long-run).
  3. **ReleaseTool CLI Fail-Closed Protection (Slice 3)**:
     - Hardened CLI parsing in `MathFirst.ReleaseTool` against missing command, unknown commands, missing option values, duplicate options, unknown options, missing required options across packaging and validation commands, and positive integer validation on build number overrides.
     - Accurate required-option rules: `android-package` requires `--profile` and `--expected-commit-sha`; `android-validate` requires `--aab-path`, `--provenance-path`, `--expected-commit-sha`, and `--profile`; `android-validate-apk` requires `--apk-path`, `--provenance-path`, and `--expected-commit-sha`.
  4. **Intentional No-New-Test Decisions (`NO_NEW_TEST_REQUIRED`)**:
     - Judged already sufficiently protected without percentage padding: archive validation, Tester / SourceCandidate / Production profile boundaries, reset exception semantics, telemetry/privacy/network boundaries, Guided gate coverage, and acquisition ownership coverage.
  5. **Zero Production & Runtime Code Changes**:
     - Exactly 8 test files changed (3 added, 5 modified); 0 production source changes, 0 tooling source changes, 0 script changes, 0 configuration changes, 0 schema changes, 0 migrations, 0 runtime behavioral changes.
- **Verification Evidence on MF-AUDIT-002 Implementation HEAD (`3558f8cee7b3aad031459990276ff99d73312379`)**:
  - Core automated test suite: 2,299 passed in Release, 0 failed, 0 skipped (+71 automated test cases over BASE 2,228).
  - Consolidated focused P8 test suite: 155 passed, 0 failed, 0 skipped.
  - Normalized Cobertura Coverage Baseline (`coverlet.collector 6.0.4`, Release configuration across 4 assemblies):
    - `MathFirst.Application`: Lines 94.31% (4080/4326) $\to$ 94.48% (4087/4326), +7 lines (+0.16 pp); Branches 83.86% (1289/1537) $\to$ 84.32% (1296/1537), +7 branches (+0.46 pp).
    - `MathFirst.Domain`: Lines 89.11% (777/872) $\to$ 93.00% (811/872), +34 lines (+3.90 pp); Branches 83.70% (385/460) $\to$ 89.78% (413/460), +28 branches (+6.09 pp).
    - `MathFirst.Infrastructure.Sqlite`: Lines 95.17% (1695/1781) $\to$ 95.51% (1701/1781), +6 lines (+0.34 pp); Branches 79.44% (429/540) $\to$ 80.56% (435/540), +6 branches (+1.11 pp).
    - `MathFirst.ReleaseTool`: Lines 90.52% (1710/1889) $\to$ 91.64% (1731/1889), +21 lines (+1.11 pp); Branches 75.32% (815/1082) $\to$ 77.54% (839/1082), +24 branches (+2.22 pp).
    - Total: Lines 93.17% (8262/8868) $\to$ 93.93% (8330/8868), +68 lines (+0.77 pp); Branches 80.63% (2918/3619) $\to$ 82.43% (2983/3619), +65 branches (+1.80 pp).
  - **Evidence Boundary**: Records implementation and consolidated-review evidence. Formal candidate validation and integration status must be established dynamically from live repository state.
- **Prior Merged Work**:
  - **Post-P6 Merge State Reconciliation**: Merged via PR #69 at merge commit `4ba870ad0bd6c516e74d2000a8f5c0878fb609a5`.
  - **P6 (Tester Diagnostics / Telemetry Release Boundary)**: Merged via PR #68 at merge commit `049ec1d5d3859a139f8d5493d6dae7607d321b02` (validated candidate `bceede18dd5bc2007f4bdc211f721979a50f2c35`, 2,228 Core tests).
  - **P5 (Cyber Defense Visual Consistency)**: Merged via PR #67 at merge commit `aeb7bc46e8b425d9da95493a367f99f7ed330871` (validated candidate `33dd87b646c0a0c94519fa76b7100346c4f30c6a`, 2,222 Core tests).
  - **P4 (Settings Simplification)**: Merged via PR #66 at merge commit `8400151ff080caecf024a418a9b6b8ada4873c2d` (validated candidate `edcc150039f369b8f809982499a5e1b2714e064c`, 2,204 Core tests).
  - **P3 (Cumulative Operation Unlock Progression)**: Merged via PR #65 at merge commit `759389650778f5d7b6a334b15556c5f31f6de5d0` (validated candidate `d1e794cf529598e1d57ae39dbe790bc51ede81d5`, 2,196 Core tests).
  - **P2b (Gameplay and Startup Refinements)**: Merged via PR #64 at merge commit `f580a7154a4043a5097ffd852b5cf454be2cc397` (validated candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`, 2,027 Core tests).
  - **P2 (Direct-to-Practice Start / Remove Onboarding)**: Merged via PR #63 at merge commit `1b485091755294221b6f242e174d99c168fc8e9d` (validated candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`, 2,014 Core tests).
  - **P1b (Active Thinking Time / Interruption Safety)**: Merged via PR #62 (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`, 2,010 Core tests).
  - **P1 (Normal Practice Without Deadline Failure)**: Merged via PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`, 1,936 Core tests).
  - **P0 (Zero-Answer / `0 + 0` Core-Flow Freeze Blocker)**: Merged via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`, 1,917 Core tests).
- **Downstream Scope**:
  - **P8 (MF-AUDIT-002)**: Implementation complete, review approved, documentation reconciled. Downstream integration status is determined from live repository state.
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
9. **P6**: Tester Diagnostics / Telemetry Release Boundary (`Merged` — PR #68 at `049ec1d5d3859a139f8d5493d6dae7607d321b02`, validated candidate `bceede18dd5bc2007f4bdc211f721979a50f2c35`, `P6_FULL_VALIDATION_PASSED`; post-merge docs reconciled via PR #69 at `4ba870ad0bd6c516e74d2000a8f5c0878fb609a5`)
10. **P8**: Test-Coverage Audit & Targeted Hardening (`MF-AUDIT-002` — *Active package; implementation complete across 3 slices at `3558f8cee7b3aad031459990276ff99d73312379`, review approved, documentation reconciled; live repository state is authoritative for integration status*)
11. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*)

### 1.2 Current Verified Quality State

#### Merged Main Baseline (`main@4ba870a` / PR #69 `Post-P6 Merge State Reconciliation`)
- **Merge Commit**: `4ba870ad0bd6c516e74d2000a8f5c0878fb609a5` (PR #69 `Merge pull request #69 from Tachiguro/docs/post-p6-merge-reconciliation`; P6 feature merge PR #68 at `049ec1d5d3859a139f8d5493d6dae7607d321b02`).
- **Post-Merge Baseline**: Complete P0–P6 delivered baseline on `main`: Tester diagnostics compile/profile boundary (`MATHFIRST_TESTER_DIAGNOSTICS`), dedicated `TesterDiagnosticsSection` component isolation, conditional DI composition, ReleaseTool build metadata propagation (`MathFirstBuildClassification`, `MathFirstSourceCommit`), profile-wide Full Local Reset cache cleanup, Cyber Defense secondary surface visual alignment across Settings, Privacy, dialogs, overlays, and Not Found; full Light/Dark/System theme fidelity; keyboard focus rings on `.keypad-choice-card:focus-visible`; reduced-motion suppression; MF-UX-008 gameplay stability; Settings simplification (removal of Practice Time UI, retention of read-only curriculum status, lower-level practice-time plumbing preserved, durable Settings cards retained); four-stage cumulative progression ($+ \to + - \to + - \times \to + - \times \div$); monotonic `CurriculumStage` in `learner_progression`; Schema V9 persistence; tolerant D01 unlock predicates; aggregate broad weakness gating; Guided G3 soft decoupling; 2,228 Core tests passing in Debug and Release.

#### Verified MF-AUDIT-002 Quality Evidence (`feat/mf-audit-002-test-coverage-hardening`)
- **Implementation State**:
  - Slice 1 (`76e718006c2adc5953995d99bbfc1451dc8e63c1`): Critical domain invariants (`BroadWeaknessPolicy`, `CurriculumUnlockPolicy`, property tests, scheduler rank).
  - Slice 2 (`56880b3ff3da2c6e06840226ac236dafedba34bd`): Persistence, recovery, and long-run invariants (SQLite trigger rollback, corrupted stage fail-closed, transient recovery, 4-stage monotonic long-run).
  - Slice 3 (`3558f8cee7b3aad031459990276ff99d73312379`): ReleaseTool CLI fail-closed boundaries (argument parsing, missing/unknown commands and options, option requirements, positive integer build number validation).
- **Review & Validation Status**:
  - Consolidated package review: `MF_AUDIT_002_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit; 0 confirmed defects; 0 unresolved P0/P1 gaps).
  - Coverage decision: `PRIOR COVERAGE DELTA CORRECTED` (normalized apples-to-apples Cobertura delta adopted).
  - Core automated test suite: 2,299 passed in Release (0 failed, 0 skipped; +71 automated test cases).
  - Focused P8 test suite: 155 passed, 0 failed, 0 skipped.
  - Normalized Cobertura Coverage (coverlet.collector 6.0.4, Release configuration across 4 assemblies):
    - Lines: 8262/8868 (93.17%) $\to$ 8330/8868 (93.93%), +68 covered (+0.77 percentage points).
    - Branches: 2918/3619 (80.63%) $\to$ 2983/3619 (82.43%), +65 covered (+1.80 percentage points).
  - Code scope: 3 added test files, 5 modified test files, 0 production source changes, 0 tooling source changes, 0 script changes, 0 configuration changes, 0 schema changes, 0 migrations, 0 runtime behavioral changes.
  - Candidate whitespace and diff check: `git diff --check` PASS.
  - Evidence boundary: Implementation and review evidence only. Formal exact-candidate validation and integration status are determined dynamically from live Git/GitHub state.

---

## 2. Historical Merged Implementation Packages

- **Post-P6 Merge State Reconciliation**: Merged via PR #69 at merge commit `4ba870ad0bd6c516e74d2000a8f5c0878fb609a5`.
- **P6 (Tester Diagnostics / Telemetry Release Boundary)**: Merged via PR #68 at merge commit `049ec1d5d3859a139f8d5493d6dae7607d321b02` (validated candidate `bceede18dd5bc2007f4bdc211f721979a50f2c35`, `P6_COMPLETE_REVIEW_APPROVED`, `P6_FULL_VALIDATION_PASSED` with 2,228 Core Debug / 2,228 Core Release tests, 117 focused tests across 11 suites, clean Windows and Android compile matrices, merge tree `817ee7b250c5bed555f4c4bce8852dce5d8dbf68`). Enforces strict compile/profile boundary isolating Tester diagnostic controls and conditional DI services from non-Tester builds, propagates ReleaseTool build classification and source commit metadata, and maintains profile-wide Full Local Reset cache cleanup.
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
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, `MF-LEARN-006`, `MF-UX-008`, `MF-TELEM-001`, `P0`, `P1`, `P1b`, `P2`, `P2b`, `P3`, `P4`, `P5`, and `P6` and no longer represents current repository source.
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
9. P6 is integrated and merged into `main` via PR #68 (`049ec1d5d3859a139f8d5493d6dae7607d321b02`, validated candidate `bceede18dd5bc2007f4bdc211f721979a50f2c35`, `P6_FULL_VALIDATION_PASSED`; post-P6 docs reconciled via PR #69 at `4ba870ad0bd6c516e74d2000a8f5c0878fb609a5`).
10. Active package P8 (`MF-AUDIT-002`, Test-Coverage Audit & Targeted Hardening) implementation and review are complete, and documentation has been reconciled. Downstream integration status is determined dynamically from live Git/GitHub state.
11. Downstream P-item sequence ($\text{P8}$) must be completed and merged before Step 55 may be proposed.
12. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. Build 4 does not exist yet (not packaged, not signed, not tested). No production packaging, release signing, ADB, or release action is authorized without explicit user dispatch.
