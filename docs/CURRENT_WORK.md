# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Package**: `MF-LEARN-004` — Guided Four-Operation Number-Space Gate
- **Current Lifecycle**: `DOCUMENT_ONLY`
- **Task Branch**: `codex/mf-learn-004-guided-number-space-gate`
- **Implementation Checkpoint HEAD**: `c962c33fe62edd633bcae003728391bec502e7eb`
- **Implementation Tree SHA**: `a855f249f9cd34fcca0e4a989a199ebdcb708f27`
- **Base Baseline (`main` / `origin/main`)**: `a9d232f78e2f9ebcbc431bd18109a0aeb08a9303` (PR #43 merge `docs/mf-doc-006-post-mf-stab-003-merge-reconciliation`)
- **Review Verdict**: `REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 2 Note)
- **Active Implementation Package**: `MF-LEARN-004` (implementation complete on task branch; pending documentation reconciliation, candidate validation, and merge)
- **Next Lifecycle for MF-LEARN-004**: `COMMIT_ONLY` (to commit reconciled documentation) $\to$ `FULL_VALIDATION` $\to$ `PUSH_ONLY` $\to$ `PR_ONLY` $\to$ Manual User Merge $\to$ `POST_MERGE_SYNC_ONLY`

---

## 2. MF-LEARN-004 Implementation Checkpoints

The package implementation was completed across four structured checkpoint commits on task branch `codex/mf-learn-004-guided-number-space-gate`:

1. **Slice 1: Guided Number-Space Gate Contract**
   - Commit SHA: `d3a0e2fc194e45655498c957c7b694c3fec931ee`
   - Trailer: `MathFirst-Checkpoint: MF-LEARN-004 1/4 add-guided-number-space-gate-contract`
   - Scope:
     - Implemented `GuidedNumberSpaceGate` domain entity in `src/MathFirst.Domain/Curriculum/GuidedNumberSpaceGate.cs`;
     - Defined Guided Mode condition (active iff exactly all four operations are enabled);
     - Implemented deterministic Addition ceiling derivation over the unlocked canonical Addition curriculum prefix ($0..\text{BandIndex}_{\text{ADD}}$);
     - Established multiplication ($\text{CorrectResult} \le \text{ceiling}$) and division ($\text{LeftOperand} \le \text{ceiling}$) eligibility checks;
     - Defined Addition and Subtraction invariance under cross-operation gating;
     - Added comprehensive unit tests in `tests/MathFirst.Core.Tests/GuidedNumberSpaceGateTests.cs`.

2. **Slice 2: Gated Practice Selection Evidence & Candidate Anti-Poisoning**
   - Commit SHA: `052b1bb82bf071a39b08d22468dbc5a5f0c80c92`
   - Trailer: `MathFirst-Checkpoint: MF-LEARN-004 2/4 gate-guided-practice-selection-evidence`
   - Scope:
     - Integrated `GuidedNumberSpaceGate` into `PracticeSelectionEvidenceRequest` and selection evidence construction;
     - Implemented streaming SQLite candidate filtering in `SqliteLearnerStore.ReadCandidateRowsAsync` before candidate accumulation and window truncation, guaranteeing gated historical facts do not consume any of the bounded 64 candidate window slots (candidate window anti-poisoning);
     - Reinforced defense-in-depth across selector candidate pools (`New`, `Useful Frontier`, `Due`, `Maintenance`, `Early Review`, `Remediation`) and final selection boundary assertions in `AdaptivePracticeSelector`;
     - Added targeted selection and persistence suites in `tests/MathFirst.Core.Tests/GuidedNumberSpaceSelectionTests.cs` and `tests/MathFirst.Core.Tests/GuidedNumberSpacePersistenceTests.cs`.

3. **Slice 3: Wire Guided Gate into TrainingSession & Settings Reconciliation**
   - Commit SHA: `2eef22ff047f2f94aee8210b20bad57e5ddc720b`
   - Trailer: `MathFirst-Checkpoint: MF-LEARN-004 3/4 wire-guided-number-space-gate-into-training-session`
   - Scope:
     - Added `DeriveEffectiveGuidedNumberSpaceGate` to `TrainingSession`, dynamically evaluating enabled operations and Addition progression;
     - Implemented `GateIdentity(bool IsActive, int? AdditionCeiling)` for selection evidence caching, ensuring cache invalidation on ceiling expansion while treating Addition and Subtraction as gate-invariant;
     - Integrated with MF-STAB-003 Settings reconciliation: unsubmitted active questions that become Guided-ineligible are discarded and replaced at the same prospective `PracticePosition` with zero learning mutations; eligible questions preserve partial input and timer state; accepted feedback is deferred;
     - Added comprehensive session and reconciliation integration tests in `tests/MathFirst.Core.Tests/GuidedNumberSpaceSessionTests.cs`.

4. **Slice 4: Regression Closure & Role Helper Alignment**
   - Commit SHA: `c962c33fe62edd633bcae003728391bec502e7eb`
   - Trailer: `MathFirst-Checkpoint: MF-LEARN-004 4/4 close-guided-number-space-regressions`
   - Scope:
     - Closed long-run regression suites: 100-attempt continuous Guided simulations (all-correct, Addition always wrong, Addition 50%, mixed errors), Custom mode independence, >64 SQLite candidate window anti-poisoning, and restart/reconciliation regressions;
     - Resolved historical MF-STAB-003 test-helper maintenance finding in `tests/MathFirst.Core.Tests/BoundedSelectionIntegrationTests.cs`, aligning test-helper role-ordinal derivation with per-operation attempt counts.

---

## 3. Package Summary & Authoritative Invariants

MF-LEARN-004 delivers the following durable invariants:

1. **Guided Mode vs. Custom Mode**:
   - **Guided Mode**: Active iff exactly all four operations (`Addition`, `Subtraction`, `Multiplication`, `Division`) are enabled. Enforces cross-operation multiplicative number-space gating governed by Addition ([ADR-0009](decisions/ADR-0009-guided-four-operation-number-space-gate.md)).
   - **Custom Mode**: Any other non-empty subset. Operates completely unrestricted (`GuidedNumberSpaceGate.Unrestricted`), allowing targeted practice without an Addition ceiling.
2. **Addition Ceiling Authority**:
   - In Guided Mode, the ceiling is the maximum represented number across the complete unlocked canonical Addition curriculum prefix ($0..\text{BandIndex}_{\text{ADD}}$).
   - Multiplication requires $\text{CorrectResult} \le \text{AdditionCeiling}$.
   - Division requires $\text{LeftOperand} \le \text{AdditionCeiling}$.
   - Addition and Subtraction are invariant under cross-operation gating.
3. **State & Schema Preservation**:
   - The gate restricts presentation eligibility only (`PERSISTED != CURRENTLY PRESENTABLE`).
   - Progression `BandIndex`, `ItemLearningState`, attempt history, FSRS cards, fluency evidence, remediation queues, accepted attempt counts, and global `PracticePosition` are preserved losslessly in Schema V6 without migration.
   - Dormant historical facts automatically regain presentation eligibility when the Addition ceiling expands or when switching to Custom Mode.
4. **Candidate-Window Anti-Poisoning**:
   - SQLite candidate streaming in `SqliteLearnerStore.ReadCandidateRowsAsync` filters out Guided-ineligible facts before candidate partitioning and window truncation, ensuring dormant facts do not consume any of the bounded 64 candidate window slots.
5. **Settings & Restart Reconciliation**:
   - Unsubmitted active questions that become Guided-ineligible upon configuration changes are discarded and replaced at the same prospective `PracticePosition` with zero learning mutations (no attempt record, no timeout, no score mutation, no FSRS mutation, no progression mutation, no count increment).
   - Eligible unsubmitted questions retain exact identity, partial input, and timer state.
   - Accepted feedback is preserved until deliberate dismissal.
   - Session restart deterministically reconstructs gate state from persisted Addition progression and enabled operations.
6. **Evidence Cache Semantic Identity**:
   - Cache identity includes semantic gate state (`GateIdentity(bool IsActive, int? AdditionCeiling)`). Equivalent semantic states compare by value equality of active status and ceiling. Addition and Subtraction remain gate-invariant (`GateIdentity(false, null)`). No gate identity is persisted.
7. **Scheduler & Role Invariants**:
   - `DeterministicOperationScheduler` bounded permutation bag turn scheduling remains unchanged (25% nominal turn share per operation in all-four mode).
   - Per-operation role progression remains derived strictly from $\text{AcceptedAttemptCount}(O) + 1$ across the 10-slot cycle.

---

## 4. Review Status & Findings

- **Verdict**: `REVIEW_APPROVED`
- **Blocker Findings**: 0
- **Major Findings**: 0
- **Minor Findings**: 0
- **Note Findings**: 2
  - *Note 1*: Informal review timing observations noted; ceiling derivation operates over a small canonical prefix without runtime caching dependencies.
  - *Note 2*: Extended diagnostic simulations (1000-attempt continuous runs with simulated complete Addition failure) observed that complete failure in an operation can cycle that operation through a very small set of repeated facts. MF-LEARN-004 intentionally does not alter operation allocation, remediation precedence, weak-frontier coverage, scheduler weights, or FSRS behavior; this finding is recognized as a deferred educational/architectural inquiry for a future separate `PLAN_ONLY` package.
- **Test Evidence**: 1,590 Core unit, integration, and regression tests passing with 0 failures and 0 skips; clean Windows and Android Release builds.

---

## 5. Downstream Release Context & Planned Packages

### Release Context:
- **Build 2 Rejection**: Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to the selector crash/starvation bug on operation reconfiguration and restart.
- **Remediation**: `MF-STAB-003` resolved the root cause in merged code (PR #42 at `caffe0e883f83249bee2c9a1f2122543e88c9ab0`).
- **Build 3 Status**:
  - Build 3 historically passed technical smoke (Step 30) and manual physical-device verification (Step 31).
  - However, Build 3 source predates `MF-LEARN-004` and therefore no longer represents current repository source.
- **Future Production Candidate**:
  - Any future production candidate packaging after `MF-LEARN-004` merge will have `versionCode >= 4`.
  - Build 4 does **not** exist yet; it has not been packaged, signed, or tested.
  - Production packaging and signing (`Distributable` AAB) and Step 30/31 verifications are agent-executable when explicitly authorized, but are not authorized in the current documentation lifecycle.
  - Must repeat technical smoke verification (Step 30) and manual physical-device functional verification (Step 31) on physical hardware (Samsung SM-S948B, Android 16).
  - Google Play publication gate (Step 32) remains user responsibility in Google Play Console (upload, rollout, and publishing) and is blocked until Step 31 passes.

### Future Planned Packages:
- `MF-UX-007`: Progress Presentation Cleanup (HUD and readiness progress presentation polish). Inactive / accepted in Backlog.
- *Weak-Frontier / Adaptive Practice Balance*: Deferred educational follow-up; requires separate `PLAN_ONLY` decision and is not an accepted package.

> [!IMPORTANT]
> There is currently no authorized implementation package beyond MF-LEARN-004. Downstream packages must not be autonomously activated without explicit user dispatch.
