# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Current Program Position**: Following completion, formal validation (`MF_CYBER_002_FULL_VALIDATION_PASSED`), merge of `MF-CYBER-002` (Core Domain State Machine & Sector/Run Engine) via PR #74 (`110290308366176ef75a5e27660985a627153f02`), and post-merge synchronization (`MF_CYBER_002_POST_MERGE_SYNC_COMPLETED`), the delivered baseline on `main` includes the pure domain combat state machine with 2,833 Core tests passing. No new gameplay implementation package is actively in flight. The current repository-writing task is post-merge project state reconciliation (`DOCUMENT_ONLY`). The next approved roadmap package is `MF-CYBER-003` (Persistent Gameplay Store and Idempotent Submission Consumer), pending separate explicit authorization.
- **Active Package**: None in active implementation. `MF-CYBER-002` is delivered and merged. Next approved roadmap package is `MF-CYBER-003` (Persistent Gameplay Store & Idempotent Submission Consumer), awaiting explicit user authorization. Current repository activity is post-merge project state reconciliation (`DOCUMENT_ONLY`).
- **Official Package Objective**: Implement the pure, immutable core domain state machine, exact integer sector scaling formulas, invariant-safe run and opponent state models, defeat healing, incorrect-answer counter-damage, game-over reboot with terminal snapshot preservation, and bounded property-tested simulation for Cyber Defense in `MathFirst.Domain.CyberDefense`.
- **Repository State & Synchronization Anchor**:
  - Live local Git and GitHub repository state always takes precedence over documentation baselines.
  - Verified base `main`: `110290308366176ef75a5e27660985a627153f02` (PR #74 MF-CYBER-002 merge commit, tree `9887d68a8fad23b0f5942b151890b000a64fde7e`).
  - Active task branch: `docs/mf-cyber-002-postmerge-reconciliation`.
  - Delivered candidate: `919ee606b34212b0619e72cd094f76458caac0ca`.
  - Feature branch status: Delivered branch `feat/mf-cyber-002-domain-state-machine` merged via PR #74; local `main` synchronized with `origin/main`; 0 open PRs.
  - Delivered checkpoint commit sequence (10 commits: 8 implementation checkpoints + 1 separate Non-Interference remediation + 1 documentation reconciliation commit):
    1. `830bcc81c695b58d0e42be22d0cfa78d029c6b8f` (`feat(cyber-defense): implement opponent classification and exact sector scaling (MF-CYBER-002 slice 1/8)` — `MathFirst-Checkpoint: MF-CYBER-002 1/8 sector-scaling-and-opponent-classification`).
    2. `effc7f3d8be2d9b94311707535d65389a79721cc` (`feat(cyber-defense): implement combat tuning policy and damage scaling (MF-CYBER-002 slice 2/8)` — `MathFirst-Checkpoint: MF-CYBER-002 2/8 combat-policy-and-damage-scaling`).
    3. `9cc2ec546d023cc6b3a27db050e44b5ed6bf9eb4` (`feat(cyber-defense): implement invariant-safe run and opponent state (MF-CYBER-002 slice 3/8)` — `MathFirst-Checkpoint: MF-CYBER-002 3/8 invariant-safe-run-and-opponent-state`).
    4. `168e2607ecd0e39ad8758977f36ba8e408db2568` (`test(cyber): reconcile domain non-interference contracts (MF-CYBER-002)` — test-only Non-Interference contract remediation).
    5. `13538b30feb7e88935fcfb04210c6d1e9d410423` (`feat(cyber-defense): implement normal opponent combat and progression (MF-CYBER-002 slice 4/8)` — `MathFirst-Checkpoint: MF-CYBER-002 4/8 normal-opponent-combat-and-advancement`).
    6. `8c1664d6cc94367ae4ed980ab596c85114278fcd` (`feat(cyber-defense): implement boss combat and sector advancement (MF-CYBER-002 slice 5/8)` — `MathFirst-Checkpoint: MF-CYBER-002 5/8 boss-encounter-and-sector-progression`).
    7. `fb707b341eba8ce93d452067f57140742dee1733` (`feat(cyber-defense): implement incorrect answer counter-damage and nonlethal transitions (MF-CYBER-002 slice 6/8)` — `MathFirst-Checkpoint: MF-CYBER-002 6/8 incorrect-answer-counter-damage`).
    8. `970733d53662dd9895e6ee8e7932fd656f908a4e` (`feat(cyber-defense): implement fatal counter-damage, game over, and reboot (MF-CYBER-002 slice 7/8)` — `MathFirst-Checkpoint: MF-CYBER-002 7/8 lethal-damage-game-over-and-reboot`).
    9. `48c9aef5fa60c9c3b1b33f4ce1ceeb41818a0a48` (`feat(cyber-defense): bounded deterministic simulation and domain invariant verification (MF-CYBER-002 slice 8/8)` — `MathFirst-Checkpoint: MF-CYBER-002 8/8 bounded-domain-simulation-and-invariants`).
    10. `919ee606b34212b0619e72cd094f76458caac0ca` (`docs(cyber): reconcile project documentation for MF-CYBER-002 domain state machine` — documentation reconciliation).
- **Completed MF-CYBER-002 Milestones**:
  - Slice 1 Implementation: Established `OpponentKind` (`Normal`, `Boss`) and exact integer sector scaling in `CyberDefenseScalingPolicy` (normal count $G(s) = 5 + \lfloor \log_2(s) \rfloor$, boss index $G(s)$, total opponents $G(s) + 1$, normal HP $H_{\text{normal}}(s) = \text{isqrt}(s + 15) - 2$, boss HP $H_{\text{boss}}(s) = \text{isqrt}(36 \cdot (s + 15)) - 12$, using checked integer square root arithmetic).
  - Slice 2 Implementation: Established `CyberDefenseCombatPolicy` defining player maximum HP (100), normal enemy base damage (4), boss enemy base damage (6), sector damage bonuses ($\lfloor s / 50 \rfloor$), defeat healing (Normal = 2 HP, Boss = 6 HP), and attack damage validation ($\ge 1$).
  - Slice 3 Implementation: Implemented immutable domain entities `OpponentState` and `CyberDefenseRunState` with strict constructor validation (checked HP bounds, positive sector, valid opponent index, expected kind/max HP matching policy), value equality (`IEquatable<T>`), initial run factory (`CyberDefenseRunState.InitialRun()`), active run factory, and persistent rehydration support.
  - Non-Interference Test Remediation: Reconciled architectural regression suite (`NonInterferenceRegressionTests.cs`) replacing broad name-based DLL restrictions with three modular contracts (Contract A: forbidden domain assembly references; Contract B: mathematical learning core isolation from Cyber Defense; Contract C: Cyber Defense domain purity) with synthetic negative fixtures, unblocking pure domain namespace `MathFirst.Domain.CyberDefense`.
  - Slice 4 Implementation: Implemented normal opponent combat and advancement transitions in pure static `CyberDefenseStateMachine.ApplyAttempt` returning immutable `CyberDefenseCombatTransitionResult`, damage clamping, non-carryover of excess damage, and next opponent setup.
  - Slice 5 Implementation: Implemented boss combat and sector advancement in `CyberDefenseStateMachine.ApplyAttempt`, advancing sector ($s \to s + 1$), resetting opponent index to 0, clamping defeat healing (6 HP up to 100 max HP), and guarding against arithmetic overflow with checked operations.
  - Slice 6 Implementation: Implemented incorrect answer counter-damage, non-lethal player HP deduction, zero opponent damage, zero encounter advancement, and zero defeat healing.
  - Slice 7 Implementation: Implemented fatal counter-damage handling when player HP reaches zero, generating immutable `CyberDefenseTerminalRunSnapshot` (preserving original defeated encounter with `PlayerCurrentHp = 0`) and rebooting active state to `CyberDefenseRunState.InitialRun()`.
  - Slice 8 Implementation: Implemented comprehensive property-based testing and multi-sector bounded simulations (`CyberDefenseStateMachinePropertyTests`, `SectorScalingFormulaPropertyTests`) verifying monotone HP progression, zero excess damage leak, defeat healing bounds, and mathematical non-interference.
  - Consolidated Package Review: Consolidated package review completed with verdict `MF_CYBER_002_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 1 non-blocking NIT; 0 production defects; 0 unresolved P0/P1 gaps).
  - Documentation Reconciliation Checkpoint: Reconciled candidate documentation (`919ee606b34212b0619e72cd094f76458caac0ca`).
  - Exact-Candidate Validation: Formal validation passed (`MF_CYBER_002_FULL_VALIDATION_PASSED` on `919ee606b34212b0619e72cd094f76458caac0ca`).
  - GitHub PR #74 & Merge: Created and merged PR #74 (`110290308366176ef75a5e27660985a627153f02`), followed by clean post-merge sync (`MF_CYBER_002_POST_MERGE_SYNC_COMPLETED`).
- **Live State Discovery**: Live local Git and GitHub repository state always takes precedence over documentation. The live branch HEAD commit SHA, divergence from `origin/main`, working tree status, review state, mergeability, CI/checks, and merge state must be discovered dynamically from:
  - `git status`
  - `git rev-parse HEAD`
  - `git fetch origin`
  - `git log origin/main..HEAD`
  - `gh repo view Tachiguro/MathFirst`
  - `gh pr list --state open`
- **Durable MF-CYBER-002 Technical Contracts & Invariants**:
  1. **Pure Domain Isolation**:
     - `MathFirst.Domain.CyberDefense` contains zero dependencies on presentation, MAUI, SQLite, or application services.
     - Pure static state machine `CyberDefenseStateMachine.ApplyAttempt` operates on immutable states with zero side effects.
  2. **Mathematical Non-Interference**:
     - Math Engine retains absolute authority for arithmetic evaluation, correctness, FSRS-6 spaced repetition, curriculum scheduling, adaptive difficulty, and learning progression.
     - Cyber Defense consumes already confirmed attempt correctness and effective attack damage.
  3. **Immutable State Models & Value Equality**:
     - `CyberDefenseRunState`, `OpponentState`, `CyberDefenseTerminalRunSnapshot`, and `CyberDefenseCombatTransitionResult` are immutable sealed classes implementing `IEquatable<T>` and value equality operators.
  4. **Exact Sector Scaling & Opponent Counts**:
     - Normal Opponent Count: $G(s) = 5 + \lfloor \log_2(s) \rfloor$.
     - Boss Opponent Index: $G(s)$. Exactly one boss per sector.
     - Total Opponents per Sector: $G(s) + 1$.
     - Normal Opponent Max HP: $H_{\text{normal}}(s) = \text{isqrt}(s + 15) - 2$.
     - Boss Opponent Max HP: $H_{\text{boss}}(s) = \text{isqrt}(36 \cdot (s + 15)) - 12$.
  5. **Combat Tuning & Healing Invariants**:
     - Player Maximum HP: 100. Initial run starts at Sector 1, OpponentIndex 0, Player HP 100, Normal Opponent 2/2 HP.
     - Enemy Counter-Damage: $\text{NormalDamage}(s) = 4 + \lfloor s / 50 \rfloor$; $\text{BossDamage}(s) = 6 + \lfloor s / 50 \rfloor$.
     - Defeat Healing: Normal = 2 HP, Boss = 6 HP. Healing is clamped and never exceeds the 100 HP maximum.
  6. **Combat Transition Semantics**:
     - Correct answers damage the current opponent, trigger no enemy counterattack, never carry over excess damage, and require at least one correct attack per opponent defeat.
     - Incorrect answers trigger enemy counter-damage, do not damage the opponent, do not advance encounter/sector, and award no healing.
  7. **Fatal Damage & Reboot Lifecycle**:
     - Fatal damage reduces player HP to zero, captures an immutable `CyberDefenseTerminalRunSnapshot` preserving the original defeated encounter, and returns `CyberDefenseRunState.InitialRun()` as `NextState`. No invalid active run is constructed.
  8. **Known Non-Blocking Review Observation (NIT)**:
     - The public constructor `CyberDefenseCombatTransitionResult` accepts argument combinations without full cross-validation when invoked directly; `CyberDefenseStateMachine` generates exclusively valid transitions.
  9. **Critical Mathematical Boundaries**:
     - Math Engine retains absolute authority: combat never selects facts, alters difficulty, modifies correctness evaluation, alters FSRS-6 scheduling, or penalizes learning progress.
     - Learner Store Schema V9 remains completely protected.
- **Verification Evidence on MF-CYBER-002 Delivery & Quality State**:
  - Core automated test suite: **2,833 passed** in Debug and Release configurations (0 failed, 0 skipped; **+443 net automated test cases** over pre-MF-CYBER-002 baseline 2,390).
  - Test Breakdown: `CyberDefenseStateMachinePropertyTests` (49 passed), `SectorScalingFormulaPropertyTests` (60 passed), `CyberDefenseStateMachineTests` (137 passed), Predecessor policy/state invariant tests (`CyberDefenseCombatPolicyTests`, `CyberDefenseRunStateInvariantTests`, `CyberDefenseScalingPolicyTests` — 188 passed), `NonInterferenceRegressionTests` (23 passed), Broad Cyber Defense regression suite (561 passed).
  - Android & Windows Compilation: Clean Windows Debug & Release (0 warnings, 0 errors), clean Android Debug & Release compile-only (Target `Compile`, 0 warnings, 0 errors).
  - Security & Markdown Audit: 0 vulnerable NuGet packages (6 projects audited), 48 Markdown documents / 308 relative links / 0 broken links / 0 broken anchors.
  - Review & Validation Verdicts: `MF_CYBER_002_REVIEW_APPROVED`, `MF_CYBER_002_FULL_VALIDATION_PASSED`, `MF_CYBER_002_PUSH_COMPLETED`, `MF_CYBER_002_PR_CREATED` (PR #74), `MF_CYBER_002_POST_MERGE_SYNC_COMPLETED`.
  - Candidate diff check: `git diff --check` PASS; merge tree `9887d68a8fad23b0f5942b151890b000a64fde7e` identical to candidate tree.
- **Prior Merged Work**:
  - **PR #74 (MF-CYBER-002: Core Domain State Machine & Sector/Run Engine)**: Merged via PR #74 at merge commit `110290308366176ef75a5e27660985a627153f02` on 2026-10-09 (`feat(cyber): MF-CYBER-002 pure domain combat state machine and sector engine`, candidate `919ee606b34212b0619e72cd094f76458caac0ca`, tree `9887d68a8fad23b0f5942b151890b000a64fde7e`). Delivered pure static combat state machine `CyberDefenseStateMachine.ApplyAttempt`, exact integer sector scaling `CyberDefenseScalingPolicy`, combat tuning policies `CyberDefenseCombatPolicy`, invariant-safe immutable models `CyberDefenseRunState`, `OpponentState`, `CyberDefenseTerminalRunSnapshot`, `CyberDefenseCombatTransitionResult`, defeat healing, counter-damage, game-over reboot with terminal snapshot preservation, modular Non-Interference regression contracts, and bounded deterministic simulation suites with 2,833 Core tests on `main`.
  - **PR #73 (MF-CYBER-001: Architectural Boundary, Calm Mode & Math Decoupling)**: Merged via PR #73 at merge commit `ef120433a06df9244066677441f22d82649fa6b7` on 2026-10-08 (`feat(cyber): MF-CYBER-001 Calm Mode and math decoupling`). Delivered user-selectable Calm Mode, layout isolation (`.calm-mode`), confirmed-attempt combat dispatch with `SubmissionId` deduplication, `PendingCombatContext` navigation safety, and mathematical non-interference regressions with 2,390 Core tests on `main`.
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
  - **Active Sequence**: MF-CYBER-002 delivery lifecycle is complete and merged into `main`. The current repository task is dedicated post-merge documentation reconciliation (`DOCUMENT_ONLY`), while MF-CYBER-003 remains the next approved roadmap package awaiting separate explicit authorization.
  - **Cyber Defense Roadmap Sequence**: MF-CYBER-001 (Boundary & Calm Mode, Merged PR #73) $\to$ MF-CYBER-002 (Domain State Machine, Merged PR #74) $\to$ MF-CYBER-003 (Gameplay Store & Receipts, Next Approved Roadmap Package) $\to$ MF-CYBER-004 (Headless Simulator) $\to$ MF-CYBER-005 (XP Economy & Attack Tree) $\to$ MF-CYBER-006 (Firewall & Beginner Assistance) $\to$ MF-CYBER-007 (Critical Strike & Overdrive) $\to$ MF-CYBER-008 (Combat HUD & Layout Invariant) $\to$ MF-CYBER-009 (Narrative Tutorial & Copy) $\to$ MF-CYBER-010 (Simulation Matrix & Calibration).
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
11. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*; GDD & Roadmap merged via PR #72; delivered in MF-CYBER-001 [Merged PR #73] and MF-CYBER-002 [Merged PR #74]; MF-CYBER-003 next)

### 1.2 Current Verified Quality State

#### Merged Main Baseline (`main@110290308366176ef75a5e27660985a627153f02` / PR #74 Baseline)
- **Merge Commit**: `110290308366176ef75a5e27660985a627153f02` (PR #74 `Merge pull request #74 from Tachiguro/feat/mf-cyber-002-domain-state-machine`).
- **Post-Merge Baseline**: Complete P0–P6, P8, Cyber Defense GDD/Roadmap (PR #72), MF-CYBER-001 (PR #73), and MF-CYBER-002 (PR #74) delivered baseline on `main`: 2,833 Core tests passing in Debug and Release on `main` (+443 net automated test cases over pre-MF-CYBER-002 baseline 2,390), pure static domain state machine `CyberDefenseStateMachine.ApplyAttempt`, exact integer sector scaling in `CyberDefenseScalingPolicy`, combat tuning and counter-damage in `CyberDefenseCombatPolicy`, invariant-safe immutable models (`CyberDefenseRunState`, `OpponentState`, `CyberDefenseTerminalRunSnapshot`, `CyberDefenseCombatTransitionResult`), defeat healing, fatal counter-damage game-over reboot with terminal snapshot capture, modular Non-Interference contracts, user-selectable Calm Mode with layout isolation (`.calm-mode`), confirmed-attempt combat dispatch with `SubmissionId` deduplication, `PendingCombatContext` navigation safety, normalized Cobertura coverage 93.93% lines / 82.43% branches, hardened domain invariants, SQLite rollback and corruption fail-closed contracts, ReleaseTool CLI fail-closed parsing, isolated ReleaseCli console test infrastructure (`[Collection("ReleaseCli process console")]`), P6 compile/profile isolation (`MATHFIRST_TESTER_DIAGNOSTICS`), Cyber Defense secondary surface visual alignment, Settings simplification, four-stage cumulative progression ($+ \to + - \to + - \times \to + - \times \div$), monotonic `CurriculumStage` in `learner_progression`, Schema V9 persistence, Schema V8 `is_interrupted`, Telemetry Schema V2, tolerant D01 unlock predicates, aggregate broad weakness gating, Guided G3 soft decoupling, and direct-to-practice startup.

#### Verified MF-CYBER-002 Quality Evidence (`feat/mf-cyber-002-domain-state-machine` / Merged PR #74)
- **Implementation State**:
  - Slice 1 (`830bcc81c695b58d0e42be22d0cfa78d029c6b8f`): Opponent classification (`OpponentKind.Normal`, `OpponentKind.Boss`) and exact integer sector scaling in `CyberDefenseScalingPolicy` ($G(s) = 5 + \lfloor \log_2(s) \rfloor$, $H_{\text{normal}}(s) = \text{isqrt}(s + 15) - 2$, $H_{\text{boss}}(s) = \text{isqrt}(36 \cdot (s + 15)) - 12$, boss index $G(s)$, total count $G(s) + 1$).
  - Slice 2 (`effc7f3d8be2d9b94311707535d65389a79721cc`): Combat tuning policy and damage scaling in `CyberDefenseCombatPolicy` ($\text{PlayerMaxHp} = 100$, enemy damage $\text{NormalDamage}(s) = 4 + \lfloor s / 50 \rfloor$, $\text{BossDamage}(s) = 6 + \lfloor s / 50 \rfloor$, defeat healing $\text{Normal} = 2$, $\text{Boss} = 6$, positive attack validation $\ge 1$).
  - Slice 3 (`9cc2ec546d023cc6b3a27db050e44b5ed6bf9eb4`): Invariant-safe immutable run state (`CyberDefenseRunState`) and opponent state (`OpponentState`) with checked bounds, value equality, and factory/rehydration methods.
  - Non-Interference Remediation (`168e2607ecd0e39ad8758977f36ba8e408db2568`): Reconciled architectural regression suite (`NonInterferenceRegressionTests.cs`) replacing broad name-based DLL restrictions with three modular contracts (Contract A: forbidden domain assembly references; Contract B: mathematical learning core isolation from Cyber Defense; Contract C: Cyber Defense domain purity) with synthetic negative fixtures, unblocking pure domain namespace `MathFirst.Domain.CyberDefense`.
  - Slice 4 (`13538b30feb7e88935fcfb04210c6d1e9d410423`): Normal opponent combat and advancement transitions in pure static `CyberDefenseStateMachine.ApplyAttempt` with `CyberDefenseCombatTransitionResult`, damage clamping, non-carryover of excess damage, and next opponent setup.
  - Slice 5 (`8c1664d6cc94367ae4ed980ab596c85114278fcd`): Boss combat and sector advancement in `CyberDefenseStateMachine.ApplyAttempt`, advancing sector ($s \to s + 1$), resetting opponent index to 0, clamping defeat healing (6 HP up to 100 max HP), and guarding against arithmetic overflow with checked operations.
  - Slice 6 (`fb707b341eba8ce93d452067f57140742dee1733`): Incorrect answer counter-damage, non-lethal player HP deduction, zero opponent damage, zero encounter advancement, and zero defeat healing.
  - Slice 7 (`970733d53662dd9895e6ee8e7932fd656f908a4e`): Fatal counter-damage handling when player HP reaches zero, generating immutable `CyberDefenseTerminalRunSnapshot` (preserving original defeated encounter with `PlayerCurrentHp = 0`) and rebooting active state to `CyberDefenseRunState.InitialRun()`.
  - Slice 8 (`48c9aef5fa60c9c3b1b33f4ce1ceeb41818a0a48`): Bounded deterministic property tests and multi-sector simulations (`CyberDefenseStateMachinePropertyTests`, `SectorScalingFormulaPropertyTests`) verifying monotone HP progression, zero excess damage leak, defeat healing bounds, and mathematical non-interference.
  - Documentation Reconciliation (`919ee606b34212b0619e72cd094f76458caac0ca`): Reconciled candidate documentation baseline.
- **Review & Validation Status**:
  - Consolidated package review completed: `MF_CYBER_002_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 1 non-blocking NIT on public constructor parameter combination cross-validation; 0 confirmed production defects; 0 unresolved P0/P1 gaps).
  - Formal full validation passed: `MF_CYBER_002_FULL_VALIDATION_PASSED` on candidate `919ee606b34212b0619e72cd094f76458caac0ca` (2,833 Core Debug / 2,833 Core Release passed, 561 focused Cyber Defense passed, 23 Non-Interference passed, 0 warnings / 0 errors Windows/Android, 0 NuGet vulnerabilities, 48 Markdown docs / 308 links / 0 broken, `git diff --check` PASS).
  - Remote push and PR creation completed: `MF_CYBER_002_PUSH_COMPLETED`, `MF_CYBER_002_PR_CREATED` (PR #74).
  - Merged via PR #74 at merge commit `110290308366176ef75a5e27660985a627153f02` (tree `9887d68a8fad23b0f5942b151890b000a64fde7e`).
  - Post-merge synchronization completed: `MF_CYBER_002_POST_MERGE_SYNC_COMPLETED`.
  - Scope: 15 files changed across `MathFirst.Domain` and `MathFirst.Core.Tests` (0 schema migrations, Schema V9 preserved, 0 tooling/script changes).

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
12. MF-CYBER-001 (Architectural Boundary, Calm Mode & Math Decoupling) is integrated and merged into `main` via PR #73 (`ef120433a06df9244066677441f22d82649fa6b7`).
13. MF-CYBER-002 (Core Domain State Machine & Sector/Run Engine) is complete, fully validated (`MF_CYBER_002_FULL_VALIDATION_PASSED`), and merged into `main` via PR #74 (`110290308366176ef75a5e27660985a627153f02`); post-merge synchronization completed (`MF_CYBER_002_POST_MERGE_SYNC_COMPLETED`).
14. MF-CYBER-003 (Persistent Gameplay Store and Idempotent Submission Consumer) is the next approved roadmap package, awaiting explicit user authorization. Subsequent Cyber Defense roadmap packages (MF-CYBER-004 through MF-CYBER-010) follow sequentially.
15. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. Build 4 does not exist yet (not packaged, not signed, not tested). No production packaging, release signing, ADB, or release action is authorized without explicit user dispatch.
