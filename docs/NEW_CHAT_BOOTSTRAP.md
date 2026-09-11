# New Chat Session Bootstrap Protocol

This document defines the mandatory discovery and re-anchoring procedure for newly launched chat sessions or agents interacting with the MathFirst repository.

---

## 1. Capability Gate

Before making any claims about repository status, determine whether tools providing direct local repository access (e.g. terminal execution, file inspection) and GitHub access (e.g. `gh` CLI) are available in the current runtime environment:
- **If repository access tools are available**: Execute the live discovery commands below. Never assume or rely on remembered chat history.
- **If repository access tools are NOT available**: State clearly that live repository state cannot be verified directly. Never fabricate or extrapolate repository facts without tool access.

---

## 2. Live Discovery & Re-Anchoring Sequence

When starting a new session with repository access, execute the following inspection steps in order:

### Step 1: Verify Local Git State
Inspect the canonical checkout at `C:\Dev\MathFirst`:
```powershell
git rev-parse --show-toplevel
git status
git branch -a -vv
git log -1 --format=fuller
git worktree list
```
Verify:
- Repository root is `C:\Dev\MathFirst`.
- Current checked-out branch.
- Current HEAD commit SHA.
- Working tree and index status (clean vs. uncommitted modifications).
- Untracked files.
- Active worktrees.

### Step 2: Verify Remote State & Upstream Synchronization
```powershell
git remote -v
gh repo view Tachiguro/MathFirst --json defaultBranchRef,isPrivate,visibility,url
gh pr list --state open
gh pr list --state merged --limit 10
```
Verify:
- Remote `origin` points to `git@github.com:Tachiguro/MathFirst.git` (or HTTPS equivalent).
- Remote default branch (`main`).
- Remote tracking status and unpushed commits (`git log origin/main..HEAD` or upstream).
- Open Pull Requests on GitHub.
- Relevant recently merged Pull Requests to understand recent baseline integration.

### Step 3: Read Foundational Governance
Read the essential governance documents:
1. [AGENTS.md](../AGENTS.md)
2. [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)
3. [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md)
4. [docs/INDEX.md](INDEX.md)

---

## 3. Active Package Resolution Priority

To determine what package is currently in flight or what should happen next, evaluate sources in strict priority order:

1. **Open Active Pull Request**: An open PR on GitHub indicates a package in the candidate/review/merge phase.
2. **Local Task Branch with Checkpoint Commits**: A dedicated task branch diverging from `main` with unpushed checkpoint commits indicates active in-flight implementation.
3. **`docs/CURRENT_WORK.md`**: Operational evidence of active package context and lifecycle phase.
4. **`docs/BACKLOG.md`**: Authoritative registry of accepted inactive work.

> [!NOTE]
> The absence of an open Pull Request does NOT prove that there is no active work. An in-flight task branch or local slice implementation may be actively progressing.

---

## 4. Determining the Next Work Item

- If live state matches an active package (open PR, active task branch, or `docs/CURRENT_WORK.md`), report the exact verified package and its current lifecycle phase.
- If all branches are merged, working tree is clean on `main`, and no package is active in `docs/CURRENT_WORK.md`, state:
  > **"There is currently no clearly determined next work item."**
- **Strict Prohibition**: Agents must NEVER autonomously pick an item from [docs/BACKLOG.md](BACKLOG.md) or [docs/ROADMAP.md](ROADMAP.md) without explicit user instruction and prompt dispatch.

---

## 5. Explicitly Authorized Next Package Context

Use the live-state discovery rules above to distinguish the following cases:

1. **Verified active package**: An open Pull Request, active task branch with checkpoint work, or `docs/CURRENT_WORK.md` consistent with live state identifies a package in flight. Report that package and continue only within its verified lifecycle.
2. **No active package**: If no package is active and no explicit authorization is supplied, do not autonomously select a BACKLOG or ROADMAP item.
3. **Explicitly authorized next package**: If the user or active orchestration supplies a named package and lifecycle step, first verify live repository state, then proceed only with that authorized package under the repository governance.

Explicit authorization does not override GitHub or live Git evidence, create an active branch by implication, or permit autonomous selection of unrelated work.

---

## 6. Current Verified Candidate Snapshot

Repository: `Tachiguro/MathFirst`
Canonical path: `C:\Dev\MathFirst`
`main` / `origin/main`: `b2c5a43707167d5f6720d66834ebbbd3c6ede1d1` (MF-LEARN-002 merged via PR #15)
Worktrees: Exactly one normal worktree

### Active Feature Candidate
- Active Task Branch: `feat/mf-learn-003-acclimation-rapid-dense`
- HEAD Commit: `d2179ffaf3074ab0c1d32f5d018722557c37e78f` (Corrective `editable-multidigit-input`)
- Package: `MF-LEARN-003` — **Acclimation Timing, Rapid Dense Expansion, and Keypad Defaults**
- Operation Mode: `DOCUMENT_ONLY` (reconciling repository documentation for corrective editable multi-digit input)
- Behavioral Implementation & Test Suite: Complete across 5 checkpoint slices and corrective editable multi-digit input with 790 passing Core tests (790/790 passed, 0 failed, 0 skipped) in `MathFirst.Core.Tests`. Corrective independent review approved (`REVIEW_PASS`). Feature branch is local only / not pushed. Open PRs: 0. `FULL_VALIDATION` remains pending.

### Checkpoint Commit Chain (MF-LEARN-003)
- Base: `b2c5a43707167d5f6720d66834ebbbd3c6ede1d1`
- Slice 1: `b1a466bc01d5a5ef968eb711fa88f1a792864b51` (`answer-length-deadlines-proof-policy`)
- Slice 2: `9953308f079b968731bc68b13a993c97b3292308` (`numpad-default-order`)
- Slice 3: `ae4051b7d1d6c883be19ba977972358b29dd86a6` (`coverage-first-dense-selection`)
- Slice 4: `269636fab2cae0974cbec9e6085ec5420c6aa66a` (`bounded-frontier-evidence`)
- Slice 5: `9e07ec34e42a688847e23e53e47adb728fbec428` (`rapid-dense-progression`)
- Prior Docs: `a06535c0f5c46d0d3ec655540778d2d666aebfc9` (`acclimation-rapid-dense-reconciliation`)
- Corrective Checkpoint: `d2179ffaf3074ab0c1d32f5d018722557c37e78f` (`editable-multidigit-input`)

### Implemented Architecture Summary (MF-LEARN-003)
1. **Answer-Length Acclimation Deadlines & Proof**: Unproven facts (`CorrectAttempts == 0`) receive digit-aware novelty floors (15s/20s/25s/30s for 1/2/3/4+ digits) and entry allowances (+1000ms per extra digit) while maintaining strict decoupling from response latency measurement, adaptive fluency thresholds, `IsFluent`, and FSRS ratings.
2. **Keypad Defaults & Ordering**: Numpad layout is the default/fallback across Onboarding, Settings, and default UI state with Numpad presented first/left and Phone second/right in visual options.
3. **Coverage-First Dense Selection**: Scheduled turns for Dense bands select unmaterialized owned-frontier facts as `PracticeSelectionRole.New` across nominal Due/Maintenance/Frontier turns until first-pass coverage is complete, subordinate only to eligible Remediation ($\ge 4$ distance).
4. **Authoritative Latest-per-Frontier Persistence**: `LoadLatestFrontierAttemptsAsync` queries latest positioned attempts per frontier fact (`PracticePosition > BandStartedPracticePosition`), supported by Schema V6 partial index `ix_attempt_history_operation_fact_position`.
5. **Correctness-Driven Dense Progression**: Dense bands advance when complete frontier coverage is met and latest votes satisfy $C \cdot 10 \ge N \cdot 9$ using in-memory candidate overlay and recoverable error/timeout votes. Fast Acquisition and the `MUL-D01` special bootstrap are retired; Structured bands retain standard 40-attempt rolling-window progression.
6. **Editable Incomplete Multi-Digit Input**: Incomplete multi-digit answers remain editable until the expected canonical digit count is reached, allowing mistyped partial answers to be corrected with Backspace/Delete before auto-submission without mutating learning state; single-digit immediate auto-submission is preserved.

### Immediate Next Lifecycle Steps
- Complete `DOCUMENT_ONLY` reconciliation.
- `COMMIT_ONLY` → `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`.
- Deferred Work: `MF-REL-001` (Android Internal AAB Packaging and Release Automation) remains deferred and not started.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
