# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Package**: `MF-LEARN-005` — Adaptive Practice Balance and Foundational Coverage
- **Current Lifecycle**: `DOCUMENT_ONLY`
- **Task Branch**: `codex/mf-learn-005-adaptive-practice-balance`
- **Candidate HEAD**: `8624173e93ef305786d2f087919295ea17b86f04` (implementation candidate HEAD before documentation commit)
- **Authoritative Baseline (`main` / `origin/main`)**: `ef03dc09464ef169228e9bc1e00bba5a22e4a387`
- **Most Recently Merged Documentation Package on `main`**: `MF-DOC-007` — Post-MF-LEARN-004 Merge State Reconciliation (PR #45 merge commit at `ef03dc09464ef169228e9bc1e00bba5a22e4a387`)
- **Most Recently Merged Implementation Package on `main`**: `MF-LEARN-004` — Guided Four-Operation Number-Space Gate (PR #44 merge commit at `8f4ae59110abf6ea9d365733297a0c15d4c296ea`, validated candidate `51a2bd9907ebdcf738a348bc90de29d31d6b68b6`, tree identity `053311fed6a6827af690a6138fd83cc2c63bb7de`, `REVIEW_APPROVED`, `FULL_VALIDATION_PASS`, Schema V6 preserved)
- **Status of MF-LEARN-005**: Implemented, review-approved, documentation reconciliation in progress; **NOT MERGED YET** (not yet `FULL_VALIDATED`, not pushed, no open PR, not merged)
- **Next Lifecycle for MF-LEARN-005**: `COMMIT_ONLY` $\to$ `FULL_VALIDATION` $\to$ `PUSH_ONLY` $\to$ `PR_ONLY` $\to$ Manual User Merge $\to$ `POST_MERGE_SYNC_ONLY`

---

## 2. MF-LEARN-005 Implementation Checkpoints

The package implementation was completed across three structured checkpoint commits on task branch `codex/mf-learn-005-adaptive-practice-balance`:

1. **Slice 1: Selector Implementation & Same-Operation Diversity Guard**
   - Commit SHA: `c1f484e83a8da6d3a4d68d98934cd41b3f50aa75`
   - Trailer: `MathFirst-Checkpoint: MF-LEARN-005 1/3 protect-new-and-same-op-balance`
   - Scope:
     - Selector implementation:
       - Protected requested-New introduction: when `requestedRole == PracticeSelectionRole.New` and the current operation has at least one eligible unmaterialized candidate in its active introduction frontier, remediation does not preempt that requested New opportunity;
       - Remediation exhaustion fallback: when New frontier is exhausted (all owned material has been materialized or no eligible unseen candidate exists), eligible remediation may preempt requested New;
       - Same-operation strict-tier repeat guard: inside the already-selected semantic pool, strict candidate selection avoids immediately repeating the previous same-operation `FactId` when another viable `FactId` exists;
       - Preserved cooldown relaxation to guarantee liveness when only repeating candidates exist;
       - Focused selector regression coverage in `tests/MathFirst.Core.Tests/AdaptiveReviewStabilizationTests.cs`, `tests/MathFirst.Core.Tests/IndependentSelectorTests.cs`, and `tests/MathFirst.Core.Tests/PracticeSequenceDiversityTests.cs`.

2. **Slice 2: TrainingSession & Persistence Integration Coverage**
   - Commit SHA: `31d760d36fb0509f481952fc7e509e2a5b00ba8b`
   - Trailer: `MathFirst-Checkpoint: MF-LEARN-005 2/3 sustained-failure-integration`
   - Scope:
     - TrainingSession / persistence integration:
       - Sustained-failure integration in `tests/MathFirst.Core.Tests/PracticeBalanceIntegrationTests.cs`;
       - Guided all-four Addition failure (0%): all four initial Addition owned facts introduced without false progression;
       - Guided all-four Multiplication failure (0%): all four initial Multiplication owned facts introduced while preserving Guided gate;
       - Custom Addition failure (0%): all foundational facts introduced and liveness preserved;
       - SQLite restart under struggle: PracticePosition, materialization, attempt counts, and FSRS state preserved with remaining unseen foundational facts reachable.

3. **Slice 3: Long-Run Balance Regressions & Deterministic Replay**
   - Commit SHA: `8624173e93ef305786d2f087919295ea17b86f04`
   - Trailer: `MathFirst-Checkpoint: MF-LEARN-005 3/3 long-run-balance-regression`
   - Scope:
     - Long-run regression:
       - Partial-failure profiles: Addition 50% (500 attempts), Addition 25% (500 attempts), alternating 50% (500 attempts);
       - Total-failure profile: all operations 0% (1,000 attempts; all 13 initial eligible foundational facts across the four operations were introduced [Addition: 4, Subtraction: 3, Multiplication: 4, Division: 2] with zero correct answers and no starvation);
       - Strong learner profile: all operations 100% (500 attempts);
       - Custom MUL+DIV asymmetric failure (500 attempts);
       - Deterministic replay over 500 positions with exact sequence identity for `PracticePosition`, `Operation`, `FactId`, and `RequestedRole`.

---

## 3. Package Summary & Authoritative Invariants

MF-LEARN-005 resolves verified selector defects and delivers the following durable invariants:

1. **Protected New Introduction**:
   - If `requestedRole == PracticeSelectionRole.New` and the current operation has at least one eligible unmaterialized candidate in its active introduction frontier, remediation does **not** preempt that requested New opportunity.
   - The New opportunity proceeds through the existing New fallback chain, guaranteeing foundational acquisition coverage.
   - When no eligible New candidate remains in the active frontier, eligible remediation may preempt requested New.
2. **Preserved Remediation Authority**:
   - Eligible remediation continues to preempt `Due`, `Maintenance`, and `Frontier` turns, subject to existing remediation spacing ($\ge 4$ positions from previous error/timeout).
3. **Same-Operation Diversity**:
   - Inside the already-selected semantic pool, strict candidate selection avoids immediately repeating the previous same-operation `FactId` when another viable `FactId` exists.
   - Does not alter operation selection, change requested role, switch semantic pools, or alter global cooldown constants (`ExactFactCooldownDistance = 3`, `MirrorFactCooldownDistance = 3`).
   - Liveness relaxation preserves repetition when strictly necessary.
4. **Strict Non-Changes**:
   - Does not modify `DeterministicOperationScheduler`, bounded permutation bags, or operation frequency (no operation weighting, no weak-operation weighting, no dynamic weighting).
   - Does not modify the 10-slot per-operation role cycle or $\text{AcceptedAttemptCount}(O) + 1$ role authority.
   - Does not alter global `PracticePosition` authority, normal fallback chains, remediation spacing, FSRS parameters or scheduling, `AdaptivePacePolicy`, mastery/progression thresholds, `GuidedNumberSpaceGate`, Custom Mode independence, curriculum ownership, lazy materialization, SQLite candidate anti-poisoning, persistence, or Schema V6.

---

## 4. MF-LEARN-005 Review Status & Findings

- **Verdict**: `REVIEW_APPROVED`
- **Candidate HEAD**: `8624173e93ef305786d2f087919295ea17b86f04` (implementation candidate HEAD before documentation commit)
- **Package Base**: `ef03dc09464ef169228e9bc1e00bba5a22e4a387`
- **Blocker Findings**: 0
- **Major Findings**: 0
- **Minor Findings**: 0
- **Core Review Evidence**: 1,604 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
  - Package-focused selector regression: 77 passed (across `AdaptiveReviewStabilizationTests`, `PracticeSequenceDiversityTests`, and `IndependentSelectorTests`)
  - `PracticeBalanceIntegrationTests`: 11 passed
  - Package-relevant review regression: 201 passed
- **Schema**: V6 unchanged (no migration, no table or column additions).
- **Merge Status**: **NOT MERGED YET**. MF-LEARN-005 is implemented and review-approved, but has not yet undergone formal `FULL_VALIDATION`, is not pushed, has no open PR, and is not merged.

---

## 5. Downstream Release Context & Planned Sequence

### Release Context:
- **Build 2 Rejection**: Historical. Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to the selector crash/starvation bug on operation reconfiguration and restart, resolved by `MF-STAB-003`.
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004` and `MF-LEARN-005` and no longer represents current repository source.
- **Future Production Candidate**:
  - Any future production candidate packaging after MF-LEARN-004/005 merge will have `versionCode >= 4`.
  - Build 4 does **not** exist yet (not packaged, not signed, not tested).

### Authorized Downstream Project Sequence:
1. Complete MF-LEARN-005 lifecycle (`DOCUMENT_ONLY` $\to$ `COMMIT_ONLY` $\to$ `FULL_VALIDATION` $\to$ `PUSH_ONLY` $\to$ `PR_ONLY` $\to$ Manual User Merge $\to$ `POST_MERGE_SYNC_ONLY`).
2. `MF-UX-007` — Progress Presentation Cleanup (accepted in Backlog, inactive; must not be activated without explicit dispatch).
3. Final V1 gap audit (no new package identifier may be invented here).
4. Build a fresh Tester APK from the then-current synchronized `main`.
5. Install the Tester APK on the user's current physical test device: Samsung Galaxy S26 Ultra.
6. Manual physical-device tester validation.
7. Only after successful tester validation: create the next production candidate with `versionCode >= 4`.
8. Step 30 technical smoke verification.
9. Step 31 production physical-device verification.
10. Google Play Step 32 publication gate (user responsibility in Google Play Console).

> [!IMPORTANT]
> MF-LEARN-005 is active candidate work in `DOCUMENT_ONLY`. Downstream packages must not be autonomously activated without explicit user dispatch.
