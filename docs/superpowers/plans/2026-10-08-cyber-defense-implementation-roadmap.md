# MathFirst — Cyber Defense Roguelite/RPG
## Implementation Roadmap & Slicing Strategy

**Date:** 2026-10-08  
**Status:** Approved Implementation Roadmap — Binding Execution Plan for Downstream Packages (No Implementation in This Phase)  
**Parent Specification:** `docs/superpowers/specs/2026-10-08-cyber-defense-roguelite-gdd.md`  
**Repository:** `Tachiguro/MathFirst`  
**Base Commit:** `main@b9ee8a940ea33d893c406071cc966e1f792e01c8`  
**Target Branch Family:** `feat/mf-cyber-*`  
**Package Family:** P7 (*Deferred / Post-Core*)  
**Release Boundary:** Roadmap Step 55 is **NOT AUTHORIZED**; Build 4 does **NOT EXIST**.

---

## 1. Executive Summary & Program Strategy

This document establishes the binding implementation plan for transitioning MathFirst Cyber Defense from its transient prototype (`CyberDefenseEncounterState.cs`) into an authoritative, persistent roguelite/RPG gamification layer.

### 1.1 Core Strategic Decisions
1. **Full Feature Implementation Precedes Balancing**: All confirmed product systems (unbounded sectors, 100 HP model, 4 skill families, beginner assistance, spotlight tutorial, local achievements, and headless simulator) will be fully implemented in small, governed code packages before fine-tuning balance numbers.
2. **Headless Simulation First**: Exact balance parameters (enemy damage, boss HP, level curves, shield recharge thresholds, and assistance triggers) will be calibrated empirically using the Headless Simulator across millions of simulated turns, rather than guessed during UI development.
3. **Multi-Device Physical Validation**: Once the simulator confirms system stability, presentation and ergonomics will be validated on physical Android hardware (Samsung Galaxy S26 Ultra) and Windows.
4. **Governed Lifecycle Slices**: Each package is bounded, independently verifiable, developed on a dedicated task branch, reviewed via `REVIEW_ONLY`, validated via `FULL_VALIDATION`, and merged manually by the user via Pull Request.

---

## 2. Architecture Dependencies & Boundary Isolation

```
                               ┌──────────────────────────────────────────────┐
                               │             MathFirst.Domain                 │
                               │  - Authoritative Mental Arithmetic Engine   │
                               │  - FSRS-6, Fact Scheduling, Curriculum      │
                               │  - Schema V9 Persistence (Protected)        │
                               └──────────────────────┬───────────────────────┘
                                                      │
                                                      │ Emits ConfirmedAttemptEvent
                                                      │ (SubmissionId, FactId, LatencyMs, IsCorrect)
                                                      ▼
┌───────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                MathFirst.Domain.CyberDefense                                      │
│  - Idempotent Submission Consumer (Receipt Deduplication)                                         │
│  - Sector & Opponent Scaling Engine: G(s) = 5 + floor(log2(s))                                     │
│  - 100 HP State Machine, Combat Damage, Shield Recharge                                           │
│  - Exactly 4 Skill Families: Attack, Firewall, Critical Strike, Overdrive                          │
│  - Fair Emergency Beginner Assistance Policy                                                      │
└──────────────────────────────────────────────┬────────────────────────────────────────────────────┘
                                               │
                       ┌───────────────────────┴───────────────────────┐
                       ▼                                               ▼
┌─────────────────────────────────────────────┐ ┌───────────────────────────────────────────────────┐
│     MathFirst.Infrastructure.Sqlite         │ │             Headless Combat Simulator             │
│  - Versioned Gameplay Store Schema V1       │ │  - Direct In-Process Headless Consumer            │
│  - Transactional XP, Level, SP Ledgers      │ │  - 5 Synthetic Player Profiles (Perfect, Expert,  │
│  - Active Run State Persistence & Recovery  │ │    Average, Learner, Beginner)                    │
│  - Local Offline Achievements Table         │ │  - Virtual Time, Seed Reproducibility, Batch Logs │
└──────────────────────┬──────────────────────┘ └───────────────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────┐
│              MathFirst.App                  │
│  - CyberDefenseHud.razor Component Polish   │
│  - Calm Mode Toggle & Decoupled Layout      │
│  - UpgradeTerminal.razor Modal Component    │
│  - Spotlight Tutorial & Localized Copy      │
│  - Rigid Lower Math Area Geometry Guard     │
└─────────────────────────────────────────────┘
```

---

## 3. Package Slicing Sequence (Ten Bounded Packages)

Work is structured into ten sequential, single-responsibility development packages:

```
[MF-CYBER-001: Boundary & Calm Mode]
       │
       ▼
[MF-CYBER-002: Domain State Machine]
       │
       ▼
[MF-CYBER-003: Persistent Store & Receipts]
       │
       ▼
[MF-CYBER-004: Headless Simulator Engine]
       │
       ▼
[MF-CYBER-005: XP Economy & Attack Tree]
       │
       ▼
[MF-CYBER-006: Firewall & Beginner Assistance]
       │
       ▼
[MF-CYBER-007: Critical Strike & Overdrive]
       │
       ▼
[MF-CYBER-008: Combat HUD & Layout Invariant]
       │
       ▼
[MF-CYBER-009: Narrative Tutorial & Copy]
       │
       ▼
[MF-CYBER-010: Simulation Matrix & Calibration]
```

---

### Package 1: Architectural Boundary, Calm Mode & Math Decoupling (`MF-CYBER-001`)
- **Objective**: Establish the strict architectural boundary between the Math Engine and Cyber Defense, introduce user-facing Calm Mode, and ensure arithmetic practice functions 100% autonomously without gameplay state.
- **Scope & Deliverables**:
  - Add explicit `IsCyberDefenseEnabled` configuration preference.
  - Implement Calm Mode UI toggle in Settings and Practice views.
  - Verify that when Calm Mode is active, all combat components are cleanly omitted from rendering and layout expands generously.
  - Ensure switching to Calm Mode freezes active combat state without background simulation or stealth changes.
  - Author comprehensive non-interference regression tests proving zero alteration to FSRS-6, `AttemptOutcome`, response latency, or Schema V9 persistence.
- **Test Taxonomy**:
  - `CalmModeDecouplingContractTests`: Verifies full arithmetic practice session execution with null/disabled combat state.
  - `NonInterferenceRegressionTests`: Verifies that learning telemetry and progression remain bit-for-bit identical with gameplay enabled vs. disabled.

---

### Package 2: Core Domain State Machine & Sector/Run Engine (`MF-CYBER-002`)
- **Objective**: Implement the pure domain combat state machine, sector scaling, 100 HP player model, and game-over rules without UI or persistence dependencies.
- **Scope & Deliverables**:
  - New domain assembly/namespace: `MathFirst.Domain.CyberDefense`.
  - `CyberDefenseRunState`: 100 starting HP, Sector index $s$, Opponent index, remaining enemy HP.
  - Opponent scaling formulas:
    $$G(s) = 5 + \lfloor \log_2(s) \rfloor$$
    $$H_{\text{normal}}(s) = 2 + \lfloor \sqrt{s+15} - 4 \rfloor$$
    $$H_{\text{boss}}(s) = 12 + \lfloor 6(\sqrt{s+15} - 4) \rfloor$$
  - Exactly one boss per sector, encountered after all $G(s)$ normal enemies are defeated.
  - Pure domain state transition: `ApplyAttempt(bool isCorrect, int attackDamage, ...)` returning combat mutations.
  - Game-over rule: Player HP reaches 0 $\to$ Run resets to Sector 1; no loss of meta-progression.
  - One-hit defeat rule: High attack damage defeats early enemies in 1 hit; minimum 1 correct answer per defeated enemy; no splash damage to next opponent.
- **Test Taxonomy**:
  - `CyberDefenseRunStateMachineTests`: Tests state transitions across normal hits, enemy counter-attacks, boss spawns, and HP depletion.
  - `SectorScalingFormulaPropertyTests`: Fuzzes $s \in [1, 10000]$ ensuring monotonic logarithmic enemy growth and overflow-safe arithmetic.

---

### Package 3: Persistent Gameplay Store & Idempotent Submission Consumer (`MF-CYBER-003`)
- **Objective**: Implement the versioned SQLite Gameplay Store and the idempotent event consumption pipeline, ensuring deduplicated rewards and seamless crash recovery.
- **Scope & Deliverables**:
  - New SQLite tables in `MathFirst.Infrastructure.Sqlite`: `gameplay_progression`, `gameplay_run_state`, `gameplay_receipt_ledger`.
  - Zero modification to Learner Store Schema V9.
  - `CyberDefenseSubmissionConsumer`: Consumes confirmed learning attempts stamped with unique `SubmissionId`.
  - Exactly-once receipt validation: Reprocessing a `SubmissionId` returns cached outcome without duplicate XP, damage, or state mutation.
  - Transactional atomic commit: Run state, receipt, and XP ledger commit in a single SQLite transaction.
  - Cold start recovery: Unfinished runs restore accurately upon app launch.
- **Test Taxonomy**:
  - `SqliteGameplayPersistenceConformanceTests`: Validates schema creation, atomic transactions, and mid-transaction rollback.
  - `IdempotentSubmissionConsumerTests`: Validates rejection of duplicate submission IDs and idempotency under retry loops.

---

### Package 4: Authoritative Headless Combat Simulator & Profile Harness (`MF-CYBER-004`)
- **Objective**: Build the dedicated headless combat simulation engine utilizing the exact production state transitions, enabling automated large-scale balance verification.
- **Scope & Deliverables**:
  - `HeadlessCombatSimulator` in tooling/test infrastructure: 100% headless, zero UI, zero thread sleeps, deterministic PRNG seeds.
  - Five synthetic profiles:
    - *Profile A (Perfect)*: 100% accuracy, 0 upgrades $\to$ Simulates up to Sector 1000 without defeat.
    - *Profile B (Expert)*: ~98% accuracy, attack-focused upgrades.
    - *Profile C (Average)*: ~85% accuracy, balanced upgrades.
    - *Profile D (Learner)*: ~70% accuracy, defensive upgrades.
    - *Profile E (Beginner)*: ~50% accuracy, unupgraded baseline.
  - Telemetry output: Seed, profile, questions, accuracy, sector reached, game-overs, XP, level, damage dealt/taken, virtual time.
  - Batch execution runner capable of simulating 100,000+ turns in seconds.
- **Test Taxonomy**:
  - `HeadlessSimulatorEngineContractTests`: Validates simulator equivalence with production domain logic.
  - `ProfileAPerfectSurvivalTests`: Asserts that Profile A never experiences an accidental defeat up to Sector 1000.

---

### Package 5: XP Economy, Player Level Curve & Attack Upgrade Path (`MF-CYBER-005`)
- **Objective**: Deliver the permanent player level progression curve, XP allocation rules, unbounded Attack skill tree, and the pause-safe Upgrade Terminal data model.
- **Scope & Deliverables**:
  - Experience point awarding: $+1$ XP for incorrect, $+6$ XP for correct, $+10$ XP flat boss milestone bonus. Opponent defeat bonuses ($+1$ XP normal, $+2$ XP boss) are awarded strictly per actually executed confirmed successful attack on that opponent (not based on max HP, overkill, or simulated hits).
  - Two-phase level curve:
    $$\Delta \text{XP}(L) = \begin{cases} \lceil 80 + 24(L-1)^{0.75} \rceil & L \le 100 \\ \lceil 834 + 160\log_2(L/100) \rceil & L > 100 \end{cases}$$
  - Level-up awards $+1$ Skill Point (SP). Level-up never forces a modal interruption.
  - Attack Skill: Base damage 1, $+1$ damage per rank, unbounded ranks, cost $C(r) = 1 + \lfloor \log_2(r) \rfloor$.
  - `UpgradeTerminalViewModel`: Read-only snapshot of current skills, prices, available SP; safe purchase execution; zero timing impact.
- **Test Taxonomy**:
  - `PlayerLevelCurveTests`: Validates monotonic XP thresholds and overflow safety up to Level 1000.
  - `AttackUpgradeContractTests`: Validates SP deductions, damage increases, and voluntary spend semantics.

---

### Package 6: Firewall Shield Mechanics & Fair Beginner Assistance (`MF-CYBER-006`)
- **Objective**: Implement the Firewall skill family (capacity, absorption, dual-outcome recharge) and the fair, non-patronizing emergency assistance for struggling learners.
- **Scope & Deliverables**:
  - Firewall unlocked at Player Level 5; initially 0 shields; max 3 slots (unlocked at Lvl 5 / 20 / 50).
  - Incoming enemy damage on error is absorbed completely by 1 shield.
  - Dynamic recharge: Correct answers award $+2$ charge units, incorrect answers award $+1$ charge unit; 30 units restores 1 shield.
  - Fair Beginner Assistance: Detects 3 consecutive early game-overs before reaching Sector 1 Boss with 0 Firewall unlocked.
  - Deploys temporary emergency protection (e.g. emergency shield / damage mitigation) without condescending text or arithmetic tampering.
  - Headless simulator verification of assistance trigger rates across Profile D and Profile E.
- **Test Taxonomy**:
  - `FirewallRechargeAndAbsorptionTests`: Validates correct/incorrect charge accumulation, capacity clamping, and damage absorption.
  - `BeginnerAssistancePolicyTests`: Validates assistance activation on repeated defeats, deactivation upon clearing Sector 1, and anti-farming guards.

---

### Package 7: Critical Strike & Overdrive Combat Systems (`MF-CYBER-007`)
- **Objective**: Implement Critical Strike (accuracy-based burst damage) and Overdrive (combat streak reward), fully reconciling and replacing the old prototype speed-crit.
- **Scope & Deliverables**:
  - Reconcile prototype: Formally retire `PrototypeCriticalHit` timing dependencies; preserve radar widget for visual pacing without speed penalty.
  - Critical Strike: Unlocks at Player Level 12 (Rank 1: every 5th correct hit $+50\%$; Rank 2: every 4th correct hit $+100\%$).
  - Overdrive: Unlocks at Player Level 20. Requires genuine consecutive correct answers. Any incorrect answer resets combo to 0. Rank 1: 5-streak awards $+50\%$ damage for next 2 hits.
  - Damage Rounding & Additive Bonus Stacking: Percentage-boosted combat damage is rounded up once ($\lceil \dots \rceil$) after all bonuses are summed ($D_{\text{effective}} = \lceil D \cdot (1 + \sum \text{bonus}) \rceil$; e.g. base damage 1 with $+50\%$ deals 2 damage); combined bonuses add together smoothly without intermediate multi-rounding.
  - Absolute non-mutation of math response latency or FSRS ratings.
- **Test Taxonomy**:
  - `CriticalStrikeContractTests`: Validates $N$-th hit trigger intervals, ceiling rounding (base 1 + 50% = 2 dmg), damage calculations, and zero timing leakage.
  - `OverdriveCombatStreakTests`: Validates combo accumulation, error reset (including when absorbed by Firewall), and bonus damage windows.

---

### Package 8: Combat HUD Polish & Rigid Geometric Layout Enforcement (`MF-CYBER-008`)
- **Objective**: Refine `CyberDefenseHud.razor` to present 100 HP, dynamic shields, opponent health, and boss ceremonies while strictly protecting the lower mathematical training area.
- **Scope & Deliverables**:
  - Upper HUD layout: 100/100 HP health bar, opponent status, sector indicator, compact tactical radar.
  - Dynamic display: Shields appear only when Firewall is unlocked (no empty slots); subtle glowing upgrade arrow when $\text{SP} > 0$.
  - Boss ceremony: 1–3s victory fanfare, ~2s compact sector recap, automatic advance to next sector.
  - System Overload sequence: Respectful reboot screen resetting to Sector 1.
  - Rigid Geometric Invariant: Verify that `expression-row`, `answer-input`, `solve-to-attack-panel`, `keypad-grid`, and `progress-hud` retain their exact pixel/touch dimensions across Light and Dark modes.
  - Responsive audit: Samsung Galaxy S26 Ultra portrait, compact Android, Windows wide/narrow.
- **Test Taxonomy**:
  - `CombatHudComponentRenderTests`: BUnit tests asserting markup structure, progress bar accessibility attributes, and dynamic shield count.
  - `LowerMathAreaLayoutContractTests`: CSS and markup regression tests ensuring zero DOM height reduction in the lower arithmetic area.

---

### Package 9: Narrative Spotlight Tutorial, Copy Hygiene & Local Achievements (`MF-CYBER-009`)
- **Objective**: Implement the lightweight spotlight tutorial, eliminate copy drift across launch states, and introduce offline local achievements.
- **Scope & Deliverables**:
  - Short Cyber Defense story card (1–2 sentences).
  - 3-step contextual spotlight tutorial (Enemy $\to$ Problem & Keypad $\to$ System Integrity). Completely skippable; replayable from Settings.
  - Zero mathematical thinking time during active tutorial steps.
  - Copy State Hygiene: Strict audit distinguishing fresh learners, returning learners, new runs, and game-overs.
  - Local Achievements in SQLite: Offline-only milestones (Sector 10, Boss Slayer, First Upgrade, etc.). Zero Google Play Games SDK.
- **Test Taxonomy**:
  - `SpotlightTutorialLifecycleTests`: Validates skip behavior, persistent completion flag, and timer pause guarantee.
  - `CopyHygieneLocalizationTests`: Validates copy isolation across fresh launch vs. returning learner across EN, DE, and RU.

---

### Package 10: Comprehensive Simulation Matrix, Physical Device Validation & Parameter Calibration (`MF-CYBER-010`)
- **Objective**: Execute large-scale simulation matrices across millions of turns, calibrate preliminary balance values, and conduct physical-device validation on Android and Windows.
- **Scope & Deliverables**:
  - Run 10,000+ seeds per synthetic profile on the Headless Simulator.
  - Evaluate progression curves: XP per minute, time to Sector 10/50/100, death frequency, and beginner assistance efficacy.
  - Fine-tune preliminary balance values (enemy damage, boss HP, recharge rates) within `MathFirst.Domain.CyberDefense`.
  - Install Tester build on physical Samsung Galaxy S26 Ultra (SM-S948B, Android 16); validate touch ergonomics, visual hierarchy, Light/Dark theme contrast, and keyboard focus.
  - Document verified balancing baseline in project documentation.
- **Test Taxonomy**:
  - `SimulationMatrixBalanceRegressionTests`: Asserts progression envelope bounds across the five profiles.
  - Recorded physical-device test receipts.

---

## 4. Testing Taxonomy & Evidence Boundaries

Every package must provide falsifiable technical evidence:

1. **Unit Tests (`MathFirst.Core.Tests`)**:
   - 100% branch coverage on domain mathematical formulas, scaling algorithms, and state transitions.
   - Deterministic property tests verifying that $G(s)$, $H_{\text{normal}}(s)$, and $H_{\text{boss}}(s)$ never overflow or produce non-positive values.
2. **Persistence & Conformance Tests**:
   - Real SQLite execution verifying schema creation, atomic commits, rollback on mid-transaction failure, and idempotency.
3. **Non-Interference Regression Tests**:
   - Verification that `AttemptRecord`, `FsrsCardState`, `ItemLearningState`, and `CurriculumStage` in Schema V9 remain completely unaffected by game actions.
4. **Component & UI Tests**:
   - BUnit tests for `CyberDefenseHud.razor`, `UpgradeTerminal.razor`, and spotlight overlays.
   - Accessibility validation (`aria-valuenow`, `aria-label`, focus rings on `.keypad-choice-card:focus-visible`).
5. **Headless Simulator Runs**:
   - Output logs from Profile A–E runs attached as technical evidence.

---

## 5. Non-Delegable Operations & Safety Gates

Autonomous agents and contributors must strictly respect the following governance constraints throughout the implementation:

- **No Step 55 Execution**: Roadmap Step 55 (Production Packaging, Signing, Store Publication) is **STRICTLY PROHIBITED**.
- **No Build 4 Creation**: Any package generation must remain within Tester profile boundaries.
- **No Git Invariant Violations**: Never commit to `main`, never perform `git reset`, `git clean`, `git rebase`, `git stash`, force-push, or branch deletion.
- **Single Writer Rule**: Exactly one repository-writing agent operates at a time.
- **Manual PR Merge**: All pull requests must be merged manually by the repository owner on GitHub.
