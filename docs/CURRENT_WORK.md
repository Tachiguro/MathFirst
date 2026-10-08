# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Current Program Position**: Following completion, formal validation, and merge of `MF-AUDIT-002` / `P8` via PR #70 (`cc81242177dd75114934c3ad48b830c9ce87c70b`), post-merge baseline reconciliation via PR #71 (`4e259e863fa95d6f30441d8ffb2eb5e7d5cfdb61`), and finalization and merge of the Cyber Defense Roguelite GDD and Implementation Roadmap via PR #72 (`3d476dd2211e4bc00898bc8162ade4f22312effb`), development transitioned into the approved Cyber Defense roadmap. The active package is `MF-CYBER-001` (Architectural Boundary, Calm Mode & Math Decoupling). All four implementation slices and one targeted navigation-recovery remediation have been completed and approved in final review (`MF_CYBER_001_FINAL_REVIEW_APPROVED`). The current lifecycle mode is `DOCUMENT_ONLY`.
- **Active Package**: `MF-CYBER-001` — Architectural Boundary, Calm Mode & Math Decoupling.
- **Official Package Objective**: Establish the strict architectural boundary between the Math Engine and Cyber Defense, introduce user-selectable Calm Mode and persistent mode preferences, isolate layout and rendering, implement confirmed-attempt combat dispatch with `SubmissionId` deduplication and navigation-safe recovery context, and prove mathematical non-interference across 100% autonomous arithmetic practice.
- **Repository State & Synchronization Anchor**:
  - Live local Git and GitHub repository state always takes precedence over documentation baselines.
  - Verified base `main`: `3d476dd2211e4bc00898bc8162ade4f22312effb` (PR #72 Cyber Defense GDD and Implementation Roadmap merge commit).
  - Active task branch: `feat/mf-cyber-001-calm-mode-slice1`.
  - Current candidate HEAD: `c1ff1635f40bec62d1c41bf1bd64ece93e054824`.
  - Feature branch status: Active local task branch; unpushed, unmerged, 0 open PRs.
  - Local checkpoint commit sequence (5 commits):
    1. `506366d83c4f9686745c9cd535138b5006aa5a43` (`feat(cyber): implement calm mode preferences, encounter lifetime, and reset safety` — `MathFirst-Checkpoint: MF-CYBER-001 1/4 calm-mode-preferences-and-state-lifetime`).
    2. `e871fa4603a515e31adc0995a4f7e01dbce85a63` (`feat(cyber-defense): add Calm Mode UI, localization, and layout isolation` — `MathFirst-Checkpoint: MF-CYBER-001 2/4 calm-mode-ui-and-layout`).
    3. `c8d6e31f95886c2afd487d1412b16a2125b287b9` (`feat(practice): implement confirmed-attempt combat dispatch and deduplication (MF-CYBER-001 slice 3/4)` — `MathFirst-Checkpoint: MF-CYBER-001 3/4 committed-attempt-combat-dispatch`).
    4. `747c28ea66bdc1371c46c569280daa51bcd33eac` (`test(cyber-defense): add Calm Mode mathematical non-interference and layout regression tests (MF-CYBER-001 slice 4/4)` — `MathFirst-Checkpoint: MF-CYBER-001 4/4 non-interference-regression-hardening`).
    5. `c1ff1635f40bec62d1c41bf1bd64ece93e054824` (`fix(practice): remediate pending combat context loss across navigation (MF-CYBER-001)` — `MathFirst-Checkpoint: MF-CYBER-001 1/1 navigation-recovery-remediation`).
- **Completed MF-CYBER-001 Milestones**:
  - Slice 1 Implementation: Established persistent Cyber Defense preference under key `mathfirst.cyber_defense_enabled` via `ICyberDefenseModePreferences` and `MauiPreferenceStore` (defaulting to `true` for backward compatibility); implemented application-scoped `CyberDefenseSessionState` with lazy encounter initialization and frozen state preservation during Calm Mode; and integrated encounter state reset into `AppResetCoordinator` during Full Local Reset.
  - Slice 2 Implementation: Added accessible Cyber Defense vs. Calm Mode selection in Settings with immediate preference persistence; added Practice header quick toggle in `Home.razor` with race-condition guards against in-flight submission mutations; conditionally omitted `CyberDefenseHud`, top-region combat visuals, combo badges, and combat styling when Calm Mode is active; introduced neutral localized math practice presentation (`Practice_SolveHeading`) localized in EN, DE, and RU; and isolated practice layout via scoped `.calm-mode` CSS modifier preserving keypad geometry, touch targets, and viewport stability.
  - Slice 3 Implementation: Introduced `ConfirmedCombatAttempt` and `CyberDefenseCombatDispatcher` for post-commit combat bridging; deferred combat mutation until `Session.IsCurrentSubmissionCommitted` is confirmed; added session-lifetime deduplication in `CyberDefenseSessionState` by `SubmissionId`; safely captured pre-persistence eligibility and critical-hit classification; and handled persistence failure recovery and Calm Mode suppression without retroactive replay.
  - Slice 4 Implementation: Added `NonInterferenceRegressionTests` with 120-turn paired session simulations proving zero alteration to FSRS-6, `AttemptOutcome`, response latency, progression, or Schema V9 persistence; added `CalmModeDecouplingContractTests` verifying complete practice autonomy without encounter allocation and testing mode-toggle scenarios A–K; and added `LowerMathAreaLayoutContractTests` enforcing rigid geometry protection for the lower mathematical practice area across themes and reduced motion.
  - Consolidated Package Review: Initial consolidated review identified pending combat context loss when navigating from Home to Settings and back while in a post-submission feedback state due to component-local storage in `Home.razor`.
  - Targeted Navigation-Recovery Remediation: Introduced immutable `PendingCombatContext` to capture pre-persistence eligibility and critical-hit classification at submission time; moved pending context holder to application-scoped `CyberDefenseSessionState` keyed by `SubmissionId`; removed disposed component-local fields from `Home.razor`; and added targeted unit, behavioral, and UI contract tests.
  - Corrective Evidence Audit & Final Review Approval: Final review confirmed all remediation requirements, returning `MF_CYBER_001_FINAL_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 1 non-blocking NIT; 0 production code defects; 0 unresolved P0/P1 gaps).
- **Live State Discovery**: Live local Git and GitHub repository state always takes precedence over documentation. The live branch HEAD commit SHA, divergence from `origin/main`, working tree status, review state, mergeability, CI/checks, and merge state must be discovered dynamically from:
  - `git status`
  - `git rev-parse HEAD`
  - `git fetch origin`
  - `git log origin/main..HEAD`
  - `gh repo view Tachiguro/MathFirst`
  - `gh pr list --state open`
- **Durable MF-CYBER-001 Technical Contracts & Invariants**:
  1. **Persistent Mode Preference & Defaults**:
     - `ICyberDefenseModePreferences` defines persistent boolean property `IsCyberDefenseEnabled`.
     - `MauiPreferenceStore` persists preference under key `mathfirst.cyber_defense_enabled`.
     - Default value is `true` (Cyber Defense enabled) for seamless backward compatibility.
     - Settings and Home quick-toggle immediately update persistent storage without allocating encounter state or mutating learner progression.
  2. **Application-Scoped Session State & Lifetime**:
     - `CyberDefenseSessionState` is registered as an application singleton in `MauiProgram.cs`.
     - Encounter state (`CyberDefenseEncounterState`) is initialized lazily upon first access when Cyber Defense is enabled.
     - When Calm Mode is active, encounter allocation is bypassed entirely (`Encounter` remains null).
     - Switching to Calm Mode preserves existing in-memory encounter state in a frozen condition without background simulation or stealth changes.
     - Full Local Reset invokes `CyberDefenseSessionState.ClearEncounter()` in `AppResetCoordinator` and restores default preference; learning-only reset preserves encounter state while retiring uncommitted context on next practice fact.
  3. **Presentation & Layout Decoupling**:
     - `Home.razor` checks `CyberDefenseSessionState.IsCyberDefenseEnabled` and conditionally omits `CyberDefenseHud`, top-region combat visuals, combo badges, and combat styling when Calm Mode is active.
     - Introduces neutral localized solve heading (`Practice_SolveHeading`: "Solve the problem" / "Löse die Aufgabe" / "Решите задачу") when combat HUD is omitted.
     - `.calm-mode` CSS modifier isolates practice layout, preserving keypad coordinates, touch targets ($\ge 48\text{px}$), finger spacing, and viewport stability across Light/Dark themes and reduced motion.
  4. **Confirmed-Attempt Post-Commit Combat Dispatch**:
     - Introduces `ConfirmedCombatAttempt` value model capturing confirmed attempt attributes (`SubmissionId`, `IsCorrect`, `IsCritical`, `IsCommitted`, `WasEligibleAtSubmission`).
     - Combat mutation in `CyberDefenseSessionState.ProcessConfirmedAttempt` occurs strictly after `Session.IsCurrentSubmissionCommitted` is true.
     - If learner database commit fails (`PersistenceResult.StoreUnavailable` or `DatabaseError`), combat mutation is bypassed and pending context is preserved.
     - When next-exercise evidence loads after a successful commit, the confirmed attempt is dispatched to combat safely.
  5. **Session-Lifetime `SubmissionId` Deduplication**:
     - `CyberDefenseSessionState` maintains an in-memory set of processed `SubmissionId` values for the session lifetime.
     - Reprocessing an already-processed `SubmissionId` (e.g. during recovery or rapid re-dispatch) returns `CombatDispatchResult.DuplicateIgnored` with zero state mutation, zero extra damage, and zero duplicate score/XP.
  6. **Navigation-Safe Recovery Metadata (`PendingCombatContext`)**:
     - `PendingCombatContext` preserves the original `SubmissionId`, Cyber Defense eligibility at submission time (`WasEligibleAtSubmission`), and critical-hit classification (`IsCritical`). These values are captured before learner persistence and retained across Home component disposal and navigation.
     - Context is stored in application-scoped `CyberDefenseSessionState` keyed by `SubmissionId`.
     - Navigating from Home to Settings and back while in feedback states preserves pending combat metadata across Blazor component disposal and re-initialization.
  7. **Calm Mode Decoupling & Non-Retroactivity**:
     - Submissions committed while Calm Mode is active do not mutate combat state and are recorded as processed.
     - Re-enabling Cyber Defense resumes preserved encounter state without retroactively executing skipped battles.
  8. **Known Non-Blocking Review Observations**:
     - *Learning Reset Handler Lifecycle*: Learning-only reset does not immediately invoke `CyberDefenseSessionState.ClearPendingContext()`. The pending `TrainingSession` evaluation becomes unreachable after learning reset, and the stale context is subsequently retired during preparation of the next practice fact.
     - *Combat Dispatcher Architectural Role*: `CyberDefenseCombatDispatcher` exists as a thin pass-through wrapper while `Home.razor` dispatches through `CyberDefenseSessionState` directly; retained as a non-blocking future cleanup opportunity.
  9. **Critical Mathematical Boundaries**:
     - Math Engine retains absolute authority: combat never selects facts, alters difficulty, modifies correctness evaluation, alters FSRS-6 scheduling, or penalizes learning progress.
     - Active thinking time excludes gameplay presentation overhead.
     - Learner Store Schema V9 remains completely protected.
- **Verification Evidence on MF-CYBER-001 Implementation & Review State**:
  - Core automated test suite: **2,390 passed** in Debug and Release configurations (0 failed, 0 skipped; **+91 net automated test cases** over pre-MF-CYBER-001 baseline 2,299).
  - Windows compilation: 0 warnings, 0 errors in Debug and Release (`net10.0-windows10.0.19041.0`).
  - Android compilation: 0 warnings, 0 errors in Debug and Release (`net10.0-android36.0`).
  - Candidate diff check: `git diff --check` PASS.
  - Evidence boundary: Implementation, consolidated review, and documentation reconciliation evidence. Formal exact-candidate validation (`FULL_VALIDATION`), remote push, pull request creation, and user merge are pending separate lifecycle steps.
- **Prior Merged Work**:
  - **PR #72 (Cyber Defense Roguelite GDD and Implementation Roadmap)**: Merged via PR #72 at merge commit `3d476dd2211e4bc00898bc8162ade4f22312effb` on 2026-10-08 (`docs(p7): finalize Cyber Defense roguelite GDD and implementation roadmap`).
  - **PR #71 (Post-P8 Merge State Reconciliation)**: Merged via PR #71 at merge commit `4e259e863fa95d6f30441d8ffb2eb5e7d5cfdb61`.
  - **MF-AUDIT-002 / P8 (Test-Coverage Audit & Targeted Hardening)**: Merged via PR #70 at merge commit `cc81242177dd75114934c3ad48b830c9ce87c70b` (validated candidate `cbbbd31ec00a1a2b55a4dea5b827761157a67a89`, `MF_AUDIT_002_REVIEW_APPROVED`, `MF_AUDIT_002_VALIDATION_REMEDIATION_REVIEW_APPROVED`, `MF_AUDIT_002_FULL_VALIDATION_PASSED`, post-merge sync `MF_AUDIT_002_POST_MERGE_SYNC_COMPLETED`, 2,299 Core tests passing in Debug and Release [+71 automated test cases], normalized Cobertura coverage 93.93% lines / 82.43% branches, 11 test paths [3 added, 8 modified; 19 total package paths including 8 documentation paths], 0 production code changes).
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
  - **Active Sequence**: MF-CYBER-001 is undergoing documentation reconciliation (`DOCUMENT_ONLY`), to be followed by `COMMIT_ONLY`, exact-candidate `FULL_VALIDATION`, `PUSH_ONLY`, and `PR_ONLY`.
  - **Cyber Defense Roadmap Sequence**: MF-CYBER-001 (Boundary & Calm Mode) $\to$ MF-CYBER-002 (Domain State Machine) $\to$ MF-CYBER-003 (Gameplay Store & Receipts) $\to$ MF-CYBER-004 (Headless Simulator) $\to$ MF-CYBER-005 (XP Economy & Attack Tree) $\to$ MF-CYBER-006 (Firewall & Beginner Assistance) $\to$ MF-CYBER-007 (Critical Strike & Overdrive) $\to$ MF-CYBER-008 (Combat HUD & Layout Invariant) $\to$ MF-CYBER-009 (Narrative Tutorial & Copy) $\to$ MF-CYBER-010 (Simulation Matrix & Calibration).
  - Roadmap Step 55 remains **NOT AUTHORIZED**. Build 4 does **NOT EXIST**.

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
10. **P8**: Test-Coverage Audit & Targeted Hardening (`Merged` — PR #70 at `cc81242177dd75114934c3ad48b830c9ce87c70b`, validated candidate `cbbbd31ec00a1a2b55a4dea5b827761157a67a89`, `MF_AUDIT_002_FULL_VALIDATION_PASSED`, post-merge sync `MF_AUDIT_002_POST_MERGE_SYNC_COMPLETED`)
11. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*; GDD & Roadmap merged via PR #72; active implementation in MF-CYBER-001)

### 1.2 Current Verified Quality State

#### Merged Main Baseline (`main@3d476dd2211e4bc00898bc8162ade4f22312effb` / PR #72 Baseline)
- **Merge Commit**: `3d476dd2211e4bc00898bc8162ade4f22312effb` (PR #72 `Merge pull request #72 from Tachiguro/docs/p7-cyber-defense-gdd-20261008`).
- **Post-Merge Baseline**: Complete P0–P6 and P8 delivered baseline on `main` alongside approved Cyber Defense Roguelite GDD and Implementation Roadmap: 2,299 Core tests passing in Debug and Release on `main`, normalized Cobertura coverage 93.93% lines / 82.43% branches, hardened domain invariants, SQLite rollback and corruption fail-closed contracts, ReleaseTool CLI fail-closed parsing, isolated ReleaseCli console test infrastructure (`[Collection("ReleaseCli process console")]`), P6 compile/profile isolation (`MATHFIRST_TESTER_DIAGNOSTICS`), Cyber Defense secondary surface visual alignment, Settings simplification, four-stage cumulative progression ($+ \to + - \to + - \times \to + - \times \div$), monotonic `CurriculumStage` in `learner_progression`, Schema V9 persistence, Schema V8 `is_interrupted`, Telemetry Schema V2, tolerant D01 unlock predicates, aggregate broad weakness gating, Guided G3 soft decoupling, and direct-to-practice startup.

#### Verified MF-CYBER-001 Quality Evidence (`feat/mf-cyber-001-calm-mode-slice1`)
- **Implementation State**:
  - Slice 1 (`506366d83c4f9686745c9cd535138b5006aa5a43`): Calm mode preferences, encounter lifetime, and reset safety (`ICyberDefenseModePreferences`, `MauiPreferenceStore`, `CyberDefenseSessionState`, `AppResetCoordinator`).
  - Slice 2 (`e871fa4603a515e31adc0995a4f7e01dbce85a63`): Calm Mode UI, localization (`Practice_SolveHeading` in EN/DE/RU), and layout isolation (`.calm-mode` modifier).
  - Slice 3 (`c8d6e31f95886c2afd487d1412b16a2125b287b9`): Confirmed-attempt combat dispatch (`ConfirmedCombatAttempt`, `CyberDefenseCombatDispatcher`) deferred to post-commit, session `SubmissionId` deduplication, and recovery safety.
  - Slice 4 (`747c28ea66bdc1371c46c569280daa51bcd33eac`): Mathematical non-interference regressions (120-turn paired simulations in `NonInterferenceRegressionTests`), mode-toggle scenarios A–K (`CalmModeDecouplingContractTests`), and rigid lower math area layout protection (`LowerMathAreaLayoutContractTests`).
  - Targeted Remediation (`c1ff1635f40bec62d1c41bf1bd64ece93e054824`): Navigation-safe recovery metadata (`PendingCombatContext`) stored in application-scoped `CyberDefenseSessionState` keyed by `SubmissionId`, eliminating disposed component-local fields from `Home.razor`.
- **Review & Validation Status**:
  - Consolidated package review completed.
  - Targeted remediation review verdict: `MF_CYBER_001_FINAL_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 1 non-blocking NIT; 0 confirmed production defects; 0 unresolved P0/P1 gaps).
  - Core automated test suite: **2,390 passed** in Debug and Release (0 failed, 0 skipped; **+91 net automated test cases** over 2,299 baseline).
  - Code scope: 23 files changed across `MathFirst.App`, `MathFirst.Application`, and `MathFirst.Core.Tests` (0 schema migrations, Schema V9 preserved, 0 tooling/script changes).
  - Windows compilation: 0 warnings, 0 errors in Debug and Release (`net10.0-windows10.0.19041.0`).
  - Android compilation: 0 warnings, 0 errors in Debug and Release (`net10.0-android36.0`).
  - Candidate whitespace and diff check: `git diff --check` PASS.
  - Evidence boundary: Implementation, review, and documentation reconciliation evidence only. Formal exact-candidate validation, push, PR, and merge are determined dynamically from live Git/GitHub state in subsequent lifecycle steps.

---

## 2. Historical Merged Implementation Packages

- **PR #72 (Cyber Defense Roguelite GDD and Implementation Roadmap)**: Merged via PR #72 at merge commit `3d476dd2211e4bc00898bc8162ade4f22312effb` on 2026-10-08 (`docs(p7): finalize Cyber Defense roguelite GDD and implementation roadmap`). Established the authoritative target game design document (`docs/superpowers/specs/2026-10-08-cyber-defense-roguelite-gdd.md`) and implementation roadmap (`docs/superpowers/plans/2026-10-08-cyber-defense-implementation-roadmap.md`) covering the 10-package Cyber Defense execution sequence.
- **MF-AUDIT-002 / P8 (Test-Coverage Audit & Targeted Hardening)**: Merged via PR #70 at merge commit `cc81242177dd75114934c3ad48b830c9ce87c70b` (validated candidate `cbbbd31ec00a1a2b55a4dea5b827761157a67a89`, `MF_AUDIT_002_REVIEW_APPROVED`, `MF_AUDIT_002_VALIDATION_REMEDIATION_REVIEW_APPROVED`, `MF_AUDIT_002_FULL_VALIDATION_PASSED`, post-merge sync `MF_AUDIT_002_POST_MERGE_SYNC_COMPLETED`, 2,299 Core tests passing in Debug and Release [+71 automated test cases], normalized Cobertura coverage 93.93% lines / 82.43% branches, 11 test paths [3 added, 8 modified; 19 total package paths including 8 documentation paths], 0 production code changes). Hardened critical domain invariants across `BroadWeaknessPolicy`, `CurriculumUnlockPolicy`, property tests, and scheduler rank; hardened SQLite mid-transaction trigger rollback, corrupted `CurriculumStage` fail-closed rejection, repeated transient recovery, and 4-stage monotonic `CurriculumManaged` long-run simulations; hardened ReleaseTool CLI argument parsing and option validation; and stabilized ReleaseCli console test infrastructure under non-parallel collection isolation.
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
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, `MF-LEARN-006`, `MF-UX-008`, `MF-TELEM-001`, `P0`, `P1`, `P1b`, `P2`, `P2b`, `P3`, `P4`, `P5`, `P6`, and `P8` and no longer represents current repository source.
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
10. P8 (`MF-AUDIT-002`, Test-Coverage Audit & Targeted Hardening) is integrated and merged into `main` via PR #70 (`cc81242177dd75114934c3ad48b830c9ce87c70b`, validated candidate `cbbbd31ec00a1a2b55a4dea5b827761157a67a89`, `MF_AUDIT_002_FULL_VALIDATION_PASSED`, `MF_AUDIT_002_POST_MERGE_SYNC_COMPLETED`).
11. Cyber Defense Roguelite GDD and Implementation Roadmap are integrated and merged into `main` via PR #72 (`3d476dd2211e4bc00898bc8162ade4f22312effb`).
12. MF-CYBER-001 (Architectural Boundary, Calm Mode & Math Decoupling) is completed across 4 implementation slices and 1 targeted navigation-recovery remediation; final review approved (`MF_CYBER_001_FINAL_REVIEW_APPROVED`); in `DOCUMENT_ONLY` mode awaiting `COMMIT_ONLY` and `FULL_VALIDATION`.
13. Subsequent Cyber Defense roadmap packages (MF-CYBER-002 through MF-CYBER-010) follow sequentially.
14. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. Build 4 does not exist yet (not packaged, not signed, not tested). No production packaging, release signing, ADB, or release action is authorized without explicit user dispatch.
