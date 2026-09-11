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

## 5. Explicitly Authorized Next Package Context

Use the live-state discovery rules above to distinguish the following cases:

1. **Verified active package**: An open Pull Request, active task branch with checkpoint work, or `docs/CURRENT_WORK.md` consistent with live state identifies a package in flight. Report that package and continue only within its verified lifecycle.
2. **No active package**: If no package is active and no explicit authorization is supplied, do not autonomously select a BACKLOG or ROADMAP item.
3. **Explicitly authorized next package**: If the user or active orchestration supplies a named package and lifecycle step, first verify live repository state, then proceed only with that authorized package under the repository governance.

Explicit authorization does not override GitHub or live Git evidence, create an active branch by implication, or permit autonomous selection of unrelated work.

## 6. Current Verified Candidate Snapshot

Repository: `Tachiguro/MathFirst`
Canonical path: `C:\Dev\MathFirst`
`main` / `origin/main`: `45ef623f44df87c0da97460d38dbb797c2aa18bf`

### Baseline Reconciliation
- `MF-STAB-001` (**Practice Progression and HUD Stabilization**) is **COMPLETE** and merged into `main` via Pull Request #13 at `45ef623f44df87c0da97460d38dbb797c2aa18bf`.
- Final validated feature candidate: `f54d0b1c5c8884ff1d4871abca667582759d5be1` (Core: 546 passed, 0 failed, 0 skipped; Windows/Android Release: 0 warnings, 0 errors).

### Active Documentation Task
- Active Task: `MF-DOC-002`, **Adaptive Learning System and Next Lifecycle Handoff Documentation**, on `docs/mf-doc-002-adaptive-learning-handoff`.
- Operation Mode: `DOCUMENT_ONLY`.
- Status: Documentation changes authored locally (unstaged/uncommitted), recording the accepted `MF-LEARN-002` product direction in [docs/BACKLOG.md](BACKLOG.md) and updating strategic sequencing in [docs/ROADMAP.md](ROADMAP.md).
- Immediate next documentation lifecycle: `REVIEW_ONLY — MF-DOC-002` → `COMMIT_ONLY` → `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`.

### Next Behavioral Package & Handoff Summary
- **Package**: `MF-LEARN-002` — **Adaptive Pace, Fast Acquisition, and Practice Interventions** (Status: `Accepted` in [docs/BACKLOG.md](BACKLOG.md)).
- **Intended Sequencing**: `MF-LEARN-002` is explicitly scheduled **before** `MF-REL-001` (Android Internal AAB Packaging and Release Automation). `MF-REL-001` remains not started.
- **Next Lifecycle Phase**: Begins with `PLAN_ONLY — MF-LEARN-002`.
- **Approved Product Direction to Preserve**:
  1. *Dual Goal*: Optimize both correctness and retrieval speed; push strong learners rapidly toward challenging material while weak facts recur more often.
  2. *Single Adaptive Timer*: One visible countdown bar serving as the actual answer deadline (no dual timers), adapting between a sensible minimum (~3s candidate) and 30s maximum (initial deadline in ~8–10s candidate region). The 5% fixed tightening step was considered too slow and is rejected.
  3. *Multi-Dimensional Pace*: Adaptation combines learner pace, operation, band difficulty, exact-fact history, latency, and stability.
  4. *FSRS Preservation*: Preserves `FSRS.Core` 1.0.7 with Practice Position virtual time, 0.95 desired retention, and deterministic per-FactId cards as the central spaced-repetition queue.
  5. *Adaptive Rating & Fluency*: Replace fixed latency thresholds (1000/2500ms) with ratings and fluency relative to adaptive expected pace.
  6. *Fast Acquisition*: Rapid advancement through small dense bands (e.g. `0 + 0 = 0`) upon confident demonstration without requiring 40 artificial repetitions.
  7. *Avoid Non-Due Repetition*: Prevent mastered non-due facts from filling empty selector pools.
  8. *Repeated-Error Intervention*: Repeated mistakes on a fact trigger a non-scored teaching pause / echo acknowledgement (`7 × 8 = 56`) before returning later via normal scheduling.
  9. *Minimalist Practice UI*: Keep practice distraction-free; evaluate transient session score vs. periodic check-ins/breaks in planning.
  10. *UI Corrections*: Investigate and fix the Pause button appearing blue in live rendering (must match intended red/danger style). The timer bar visual itself is approved and not a defect.
  11. *Safety & Hard Stop*: Checked arithmetic prevents `Int32` overflow at the end of curriculum. Endgame achievement ("Math God") is deferred gamification.
- **Open Items for Planning**: All exact numerical calibration values, timing formulas, scaling steps, time windows, and advancement gates remain unresolved and must be researched and justified during `PLAN_ONLY`.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
