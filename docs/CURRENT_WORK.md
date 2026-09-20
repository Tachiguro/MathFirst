# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Package**: `MF-DOC-006` — Post-MF-STAB-003 Merge State Reconciliation
- **Current Lifecycle**: `DOCUMENT_ONLY`
- **Task Branch**: `docs/mf-doc-006-post-mf-stab-003-merge-reconciliation`
- **Base Baseline**: `main` / `origin/main` at `caffe0e883f83249bee2c9a1f2122543e88c9ab0`
- **Most Recently Merged Implementation Package on `main`**: `MF-STAB-003` — Enabled-Subset Scheduling and Current-Fact Reconciliation (PR #42 merge at `caffe0e883f83249bee2c9a1f2122543e88c9ab0`, validated pre-merge candidate `766d8ea7692d139425e2301121f93af7901cf238`, tree identity `fcab56b3a886ed0c5018d4f8a16304ee83378b26`, `REVIEW_APPROVED`, `FULL_VALIDATION_PASS`, Schema V6 preserved)
- **Active Implementation Package**: None (no implementation package is active; downstream packages must not be autonomously selected)
- **Next Lifecycle for MF-DOC-006**: `REVIEW_ONLY` $\to$ `COMMIT_ONLY` $\to$ `FULL_VALIDATION` $\to$ `PUSH_ONLY` $\to$ `PR_ONLY`

---

## 2. MF-STAB-003 Implementation Checkpoints

The package implementation was completed across four structured checkpoint commits on the task branch:

1. **Slice 1: Role Ordinal Authority & Restart Regression**
   - Commit SHA: `4e697b4f525240d6fbb604f76a677d866227ba23`
   - Trailer: `MathFirst-Checkpoint: MF-STAB-003 1/2 role-ordinal-authority-and-restart-regression`
   - Scope:
     - Established independent per-operation attempt count authority for role schedules (`NextOperationAttemptOrdinal(O) = AcceptedAttemptCount(O) + 1`) across the 10-slot cycle: 1 New, 2 Due, 3 New, 4 Maintenance, 5 Frontier, 6 New, 7 Due, 8 New, 9 Due, 10 Frontier (remainder sets $\{0, 2, 5, 7\} \to \text{New}$, $\{1, 6, 8\} \to \text{Due}$, $\{3\} \to \text{Maintenance}$, $\{4, 9\} \to \text{Frontier}$ for $i = (\text{ordinal} - 1) \bmod 10$);
     - Decoupled `AdaptivePracticeSelector.GetRequestedRole(ordinal)` from global `PracticePosition`;
     - Preserved global `PracticePosition` for attempt sequencing, permutation bag scheduling, FSRS virtual time elapsed, and persistence transaction boundaries;
     - Fixed the physical-device release blocker where disabling an operation after substantial practice and restarting crashed or starved the selector in `PracticeSelectionRole.Due`;
     - Preserved Schema V6 without migration, dynamically reconstructing per-operation attempt counts from persisted `item_learning_state.total_attempts`;
     - Added targeted regression coverage in `tests/MathFirst.Core.Tests/SqliteEnabledSubsetPersistenceTests.cs` and `tests/MathFirst.Core.Tests/IndependentSelectorTests.cs` (1,507 passing Core tests).

2. **Slice 1B: Selection Context and Recovery Invariants Hardening**
   - Commit SHA: `8a0b17d09ec7f2114d1c7efe6c43be6ef75b4421`
   - Trailer: `MathFirst-Checkpoint: MF-STAB-003 1B/2 authority-hardening-and-recovery-invariants`
   - Scope:
     - Hardened selection context passing and role-ordinal consistency across selector boundaries;
     - Reinforced fallback recovery invariants when operational subsets are dynamically reconfigured;
     - Added `AuthorityHardeningAndRecoveryInvariantTests`.

3. **Slice 1C: Recovery and Authority Verification Closure**
   - Commit SHA: `fd70fe6b856a9beacb38db8a4df3d2cc1f13a9c3`
   - Trailer: `MathFirst-Checkpoint: MF-STAB-003 1C/2 recovery-verification-closure`
   - Scope:
     - Closed authority verification contracts and ensured complete coverage of edge cases during single-operation and multi-operation restarts;
     - Validated cold-restart and warm-resume persistence fidelity.

4. **Slice 2: Current Fact Configuration Reconciliation**
   - Commit SHA: `cb2d984916ff080509713ae3b73b04a1fd8aa4bc`
   - Trailer: `MathFirst-Checkpoint: MF-STAB-003 2/2 current-fact-configuration-reconciliation`
   - Scope:
     - Implemented deterministic Settings/current-fact reconciliation in `TrainingSession.ReconcilePracticeConfigurationAsync`:
       - **Case A & C (Invalid unsubmitted fact)**: Immediately discarded and replaced with a valid fact for the same prospective `PracticePosition`. Strictly zero learning mutations created (no attempt record, no timeout, no score mutation, no FSRS mutation, no progression mutation, no attempt count increment);
       - **Case B (Valid unsubmitted fact)**: Retained with exact identity, `FactInstanceRevision`, partial input, and remaining timer state preserved;
       - **Accepted Feedback Deferral**: If the active exercise has already accepted submission feedback, reconciliation is deferred until next question preparation, ensuring learner feedback is never erased;
     - Updated `Settings.razor` to await reconciliation before navigating back to practice;
     - Added comprehensive reconciliation test suite in `tests/MathFirst.Core.Tests/PracticeConfigurationReconciliationTests.cs` (1,516 passing Core tests).

---

## 3. Package Summary & Authoritative Invariants

MF-STAB-003 delivers the following durable invariants:

1. **Independent Per-Operation Role Authority**: Practice selection roles (`New`, `Due`, `Maintenance`, `Frontier`) are derived strictly from per-operation attempt counts (`NextOperationAttemptOrdinal(O) = AcceptedAttemptCount(O) + 1`) mapped across the 10-slot cycle (1 New, 2 Due, 3 New, 4 Maintenance, 5 Frontier, 6 New, 7 Due, 8 New, 9 Due, 10 Frontier). Per-operation count is authoritative **only** for role-cycle ordinals; global `PracticePosition` governs attempt ordering, permutation bag scheduling, FSRS virtual time, and persistence validation. Existing coverage, pace, fluency, progression, and item-state authorities remain unchanged.
2. **Global Practice Position Authority**: Global `PracticePosition` retains authority for attempt sequencing, bounded permutation bag operation scheduling, FSRS virtual time distance, and database persistence boundaries.
3. **Durable Count Reconstruction (Schema V6)**: Operation attempt counts are reconstructed from `SUM(item_learning_state.total_attempts WHERE operation = O)` upon initialization. Schema remains V6 with zero schema changes.
4. **Settings Reconciliation & Zero-Mutation Replacement**: When Settings disables the operation of an unsubmitted active exercise, it is replaced immediately with a valid exercise for the same prospective `PracticePosition` without generating an attempt or mutating learning state.
5. **Accepted-Feedback Deferral**: Learner feedback on already-submitted exercises is never discarded by Settings changes.
6. **Awaited Settings Navigation**: `Settings.razor` awaits session reconciliation before navigating back to practice, preventing race conditions.

---

## 4. Review Status & Findings

- **Verdict**: `REVIEW_APPROVED`
- **Blocker Findings**: 0
- **Major Findings**: 0
- **Minor Findings**: 1
  - *Location*: `tests/MathFirst.Core.Tests/BoundedSelectionIntegrationTests.cs`
  - *Detail*: A test helper passes global prospective position to `AdaptivePracticeSelector.GetRequestedRole(...)` instead of per-operation attempt count. This is isolated to test helper semantics and does not affect production code.
  - *Action*: Tracked in Backlog for follow-up maintenance in `MF-LEARN-004` or test suite cleanup.
- **Test Evidence**: 1,516 Core unit, integration, and regression tests passing with 0 failures and 0 skips; clean Windows and Android Release builds.

---

## 5. Downstream Release Context & Planned Packages

### Release Context:
- **Build 2 Rejection**: Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to the selector crash/starvation bug on operation reconfiguration and restart.
- **Remediation**: `MF-STAB-003` resolved the root cause in merged code (PR #42 at `caffe0e883f83249bee2c9a1f2122543e88c9ab0`).
- **Release Sequence**:
  1. Complete documentation reconciliation `MF-DOC-006` (`DOCUMENT_ONLY` $\to$ `REVIEW_ONLY` $\to$ `COMMIT_ONLY` $\to$ `FULL_VALIDATION` $\to$ `PUSH_ONLY` $\to$ `PR_ONLY` $\to$ Merge).
  2. Authorize new production candidate build with `versionCode` $\ge 3$.
  3. Re-run Step 30 (smoke verification) and Step 31 (physical device verification on Samsung SM-S948B, Android 16).
  4. Google Play gate remains blocked until Step 31 passes.

### Future Planned Packages:
- `MF-LEARN-004`: Guided Four-Operation Number-Space Gate (onboarding/progression gating for multi-operation arithmetic). Inactive / not authorized.
- `MF-UX-007`: Progress Presentation Cleanup (HUD and readiness progress presentation polish). Inactive / not authorized.

> [!IMPORTANT]
> There is currently no automatically selected implementation package. Following completion of MF-DOC-006, new sessions must not autonomously activate MF-LEARN-004, MF-UX-007, or any other package without explicit user dispatch.
