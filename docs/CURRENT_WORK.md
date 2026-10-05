# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: `P1B-DOCUMENTATION-SYNC` (P1b Active Thinking Time / Interruption Safety Documentation Synchronization).
- **Active Package**: `P1b`: Active Thinking Time / Interruption Safety.
- **Branch / Upstream State**: Task branch `feat/p1b-active-thinking-time` based on `main` (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`).
- **Implementation & Integration Milestones**:
  - Slice 1: `77dfd20b19497dfe208bbbfb1353e0a4c5586e9a` (`feat(practice): track interrupted attempts`)
  - Slice 2: `d604c49726034ca7976d5725de59772428a5f92d` (`feat(persistence): persist interrupted attempts`)
  - Slice 3: `a611e8e200e19ac9b61552a21e8b2fc31b63ba43` (`feat(learning): neutralize interrupted timing evidence`)
  - Slice 4: `702fd9164937daa130b2afe61f54255e0a4cbe02` (`feat(telemetry): export interruption metadata`)
  - Documentation Synchronization: `DOCUMENT_ONLY` in progress (uncommitted).
- **Live State Discovery**: Live local Git and GitHub repository state always takes precedence over documentation. The live status, review state, mergeability, CI/checks, and merge state must be discovered dynamically from:
  - `git status`
  - `git rev-parse HEAD`
  - `git fetch origin`
  - `gh repo view Tachiguro/MathFirst`
  - `gh pr list --state open`
- **Status of Active Work**:
  - **P1b Implementation Status**: Complete across all four implementation checkpoints (`P1B_IMPLEMENTATION: COMPLETE`).
  - **Durable P1b Behavioral Contract**:
    1. **Active Thinking Time**: `ResponseLatencyMs` represents accumulated active interaction time across lifecycle interruptions. Inactive interruption duration (manual pause, backgrounding, settings/dialog navigation) is excluded. Active timing is segmented across interruptions. An uninterrupted learner who thinks slowly remains genuinely slow. There is no naive wall-clock cutoff.
    2. **Interruption Fact**: `AttemptRecord.IsInterrupted` is a durable empirical boolean fact indicating at least one genuine lifecycle interruption occurred after active timing began and before submission. Normal submission does not mark interruption; the interruption flag resets cleanly for the next exercise.
    3. **Mathematical Outcome**: Interrupted Correct remains Correct. Interrupted Incorrect remains Incorrect. Interruption never creates a Timeout outcome. P1 normal-practice no-deadline semantics remain fully intact. Historical and explicit compatibility Timeout semantics remain supported.
    4. **Timing Eligibility**: `TimingEvidenceEligible` is derived as `!IsInterrupted`. It is NOT separately persisted as a redundant column and NOT exported as a separate telemetry field. `IsFluent` remains the active-latency classification. Thus, `IsFluent == true` and `IsInterrupted == true` is valid. Downstream consumers requiring continuous timing evidence evaluate `TimingEvidenceEligible`.
    5. **Item Learning State**:
       - *Interrupted Correct*: Mathematical state updates normally (increments `TotalAttempts`, `CorrectAttempts`, `ConsecutiveCorrectStreak`; clears active remediation). Timing and fluency state remains neutral (preserves `FluentStreak`, `IsProvisionallyMastered`, `LastLatencyMs`, `RollingLatencyMs`).
       - *Interrupted Incorrect*: Normal mathematical error semantics apply (increments `TotalAttempts`, `IncorrectAttempts`; resets `ConsecutiveCorrectStreak` and `FluentStreak`; revokes provisional mastery; sets remediation due), but contaminated latency is NOT sampled into `LastLatencyMs` or `RollingLatencyMs`.
    6. **Adaptive Pace**: Interrupted Correct attempts are excluded from latency shrinkage at learner, operation, band, and fact levels. Interrupted Incorrect and historical Timeout continue to contribute their existing outcome and instability evidence. Static priors, shrink weights, clamps, and sample limits are unchanged.
    7. **Pace Calibration**: Calibration threshold remains `AdaptivePacePolicy.PaceCalibrationCorrectAttemptThreshold == 24`. Only timing-eligible positioned Correct attempts count toward calibration readiness. The runtime and persisted property remains `PositionedCorrectAttemptCount`, reflecting audited calibration-specific usage.
    8. **Structured Band Dual Window**: Structured band advancement uses the Dual Window policy, retaining the canonical numerical gates (40 / 38 / 34 / 20 / 16):
       - *Window A*: Latest 40 qualifying mathematical attempts in the current band instance. Governs mathematical window sufficiency, Correct count ($\ge 38$), frontier attempts ($\ge 20$), distinct frontier coverage ($\ge \min(16, N)$), and representative sample introductions ($\ge 16$). Interrupted attempts remain in Window A.
       - *Window B*: Latest 40 timing-eligible qualifying attempts in the same band instance. Governs fluency sufficiency: requires a full 40 timing-eligible attempts with at least 34 fluent (`IsFluent == true`). Interrupted attempts neither help nor hurt Window B. Uninterrupted slow Correct and uninterrupted Incorrect count as eligible non-fluent evidence. No proportional formula exists.
    9. **Runtime History Expansion**: Runtime recent attempt history retains the union of latest 40 total positioned attempts and latest 40 timing-eligible positioned attempts per operation, deduplicated by `SubmissionId` and ordered deterministically by `PracticePosition` (theoretical bound: 4 operations $\times$ up to 80 unique attempts $+$ up to 3 global/cooldown records $=$ up to 323 records).
    10. **SQLite Schema V8**: Schema version is 8. `attempt_history` table contains `is_interrupted INTEGER NOT NULL DEFAULT 0 CHECK (is_interrupted IN (0, 1))`. Fresh databases initialize directly at V8; V7 $\to$ V8 migration preserves all existing rows with default `0` (`false`) and preserves historical Timeout semantics. All `AttemptRecord` read paths rehydrate `IsInterrupted`. No Schema V9 exists.
    11. **Telemetry Export V2**: Export payload uses `schema_version = 2`. Each attempt contains 16 properties (the 15 v1 properties in unchanged order, followed by boolean `is_interrupted` as the 16th property). No `is_timing_eligible` field is exported. Migrated legacy rows export `is_interrupted = false`; persisted interrupted attempts export `is_interrupted = true`.
    12. **FSRS**: FSRS behavior was deliberately not changed by P1b. Interrupted Correct continues to use the active-latency-derived rating; no fixed Good override exists.
    13. **Dense Progression**: Dense progression remains purely mathematical and correctness-driven ($C \cdot 10 \ge N \cdot 9$); it does not evaluate timing eligibility.
    14. **Cyber Defense**: Game mechanics remain unchanged presentation consumers; no interruption-specific anti-cheat or Critical Hit modification was added.
    15. **Settings / UX**: No additional Settings Resume action was added. Practice Time controls remain accepted transitional UX debt pending P4.
    16. **Step 55 / Build 4**: Roadmap Step 55 remains NOT EXECUTED / NOT AUTHORIZED. Build 4 does not exist.
  - **Verification Evidence on Task Branch**:
    - Complete Core test suite: 2,010 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
    - Focused test suites:
      - Telemetry JSON Serialization (`TelemetryJsonSerializerTests`): 32 passed.
      - Telemetry Export Coordination (`TelemetryExportCoordinatorTests`): 14 passed.
      - Complete Telemetry Suite: 64 passed.
      - Schema V8 Migration (`SchemaV8MigrationTests`): 16 passed.
      - Attempt Interruption Lifecycle (`AttemptInterruptionLifecycleTests`): 26 passed.
      - Runtime Persistence Regression (`RuntimePersistenceRegressionTests`): 9 passed.
      - Adaptive Pace, Calibration & Dual-Window Band Advancement: 124 passed.
    - Release builds: Windows and Android release builds were not re-executed during the documentation-only sync lifecycle.
  - **Integration & Procedural State**:
    - Branch `feat/p1b-active-thinking-time` is local-only (4 commits ahead of `origin/main`, 0 behind, unpushed, no open PR).
    - Next lifecycle step: `REVIEW_ONLY` (independent review of documentation synchronization before commit/push).
  - **Prior Work**:
    - **P0**: Zero-Answer / `0 + 0` Core-Flow Freeze resolved and merged via PR #60 (`4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`).
    - **P1**: Normal Practice Without Deadline Failure resolved and merged via PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`).
  - **Downstream Scope**: P2 (Direct-to-Practice Start), P3 (Cumulative Operation Unlock Progression), P4 (Settings Simplification), P5, P6, P8. Step 55 is **NOT AUTHORIZED**.

### 1.1 Pre-Step55 Refinement Program Sequence

The canonical pre-production program ([docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md)) governs downstream work:
1. **P0**: Zero-Answer / `0 + 0` Core-Flow Freeze (`Merged` — PR #60 at `4df7a5f4c8230700b427f0c1d9ebdabfcd98d823`)
2. **P1**: Normal Practice Without Deadline Failure (`Merged` — PR #61 at `4e3ca4943c5809cbe470a4b0ac4f192b24b66795`)
3. **P1b**: Active Thinking Time / Interruption Safety (`Implementation Complete` — 4 slices on task branch `feat/p1b-active-thinking-time`, HEAD `702fd9164937daa130b2afe61f54255e0a4cbe02`)
4. **P2**: Direct-to-Practice Start / Remove Onboarding (direct launch, system defaults, Addition only — *Downstream*)
5. **P3**: Cumulative Operation Unlock Progression ($+ \to + - \to + - \times \to + - \times \div$; demonstrated mathematical evidence, 3 simulation personas — *Downstream*)
6. **P4**: Settings Simplification (streamlined settings, remove practice time selection, prevent unlock bypass — *Downstream*)
7. **P5**: Cyber Defense Visual Consistency (dark technical surfaces, restrained neon/cyber accents — *Downstream*)
8. **P6**: Tester Diagnostics / Telemetry Release Boundary (hard compile/profile boundary isolating Tester diagnostic controls from Distributable UI — *Downstream*)
9. **P8**: Test-Coverage Audit & Targeted Hardening (factual baseline, critical invariant coverage — *Downstream*)
10. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*)

### 1.2 Current Verified Quality State

#### Merged Main Baseline (`main@4e3ca49` / PR #61 `P1`)
- **Merge Commit**: `4e3ca4943c5809cbe470a4b0ac4f192b24b66795` (PR #61 `Merge pull request #61 from Tachiguro/feat/p1-no-deadline-failure`).
- **Post-Merge Verification**: Normal practice without deadline failure integrated cleanly; 1,936 Core tests passing, clean Windows and Android builds.

#### Verified P1b Quality Evidence (`feat/p1b-active-thinking-time`)
- **Implementation Checkpoints**:
  - Slice 1 (`77dfd20b19497dfe208bbbfb1353e0a4c5586e9a`): `feat(practice): track interrupted attempts`.
  - Slice 2 (`d604c49726034ca7976d5725de59772428a5f92d`): `feat(persistence): persist interrupted attempts`.
  - Slice 3 (`a611e8e200e19ac9b61552a21e8b2fc31b63ba43`): `feat(learning): neutralize interrupted timing evidence`.
  - Slice 4 (`702fd9164937daa130b2afe61f54255e0a4cbe02`): `feat(telemetry): export interruption metadata`.
- **Validation Acceptance (`MathFirst.Core.Tests`)**:
  - Complete Core test suite: 2,010 passed, 0 failed, 0 skipped.
  - Focused interruption, persistence, pace, Dual-Window, and telemetry export suites passing.
  - Tracked working tree clean prior to documentation sync.
  - Packaging boundaries strictly preserved: zero APK/AAB packaging, zero signing, zero store actions.

---

## 2. Historical Merged Implementation Packages

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
3. P1b implementation is complete across four slices on branch `feat/p1b-active-thinking-time` (HEAD `702fd9164937daa130b2afe61f54255e0a4cbe02`, 2,010 Core tests passing). Documentation sync and review precede commit/push.
4. Downstream P-item sequence ($\text{P2} \to \text{P3} \to \text{P4} \to \text{P5} \to \text{P6} \to \text{P8}$) must be completed and merged before Step 55 may be proposed.
5. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. Build 4 does not exist yet (not packaged, not signed, not tested). No production packaging, release signing, ADB, or release action is authorized without explicit user dispatch.
