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
`main` / `origin/main`: `8983bee7d4a3cc0a4201ab2b2d208e7692de6a5f`

### Active Feature Candidate
- Active Task Branch: `feat/mf-learn-002-adaptive-pace`
- HEAD Commit: `b9a886290e8e5497b96af4a4b6a1eb8961b2a34f` (Checkpoint 6/6 `take-break-zero-timing-fix`)
- Package: `MF-LEARN-002` — **Adaptive Pace, Fast Acquisition, and Practice Interventions**
- Operation Mode: `DOCUMENT_ONLY` (reconciling repository documentation to implementation and `REVIEW_PASS` state)
- Behavioral Implementation & Test Suite: Complete with 708 passing Core tests (0 failed, 0 skipped) in `MathFirst.Core.Tests`. Independent post-correction review approved (`REVIEW_PASS`). `FULL_VALIDATION` remains pending.

### Implemented Architecture Summary (MF-LEARN-002)
1. **Adaptive Pace & Deadlines**: Multi-level hierarchical shrinkage ($P_0=4500$, learner, operation, band, fact), instability allowance (+1000ms Incorrect, +1500ms Timeout), adaptive deadline clamped to 3000..30000ms (cold baseline 9000ms).
2. **Adaptive Fluency & Ratings**: Ratings mapped to fact pace (Easy $\le 0.85 P_{\text{fact}}$, Good $\le 1.25 P_{\text{fact}}$, Hard $> \text{FluencyThreshold}$, Again on error/timeout). Persisted `is_fluent` in Schema V6.
3. **Fast Acquisition**: Dense bands ($N \in [1, 12]$) advance upon 100% correct first-encounter latency $\le 2000\text{ ms}$ with clean prefix within phase-aware requested-New horizon.
4. **Selector Model**: Role-specific selector chains (`Requested New`, `Requested Due`, `Requested Maintenance`, `Requested Frontier`), removal of `AnyMaterialized`, early review liveness bridge, strict in-pool cooldown relaxation, remediation priority override.
5. **Teaching Interventions**: Session-local 2nd consecutive error on same fact triggers non-mutating canonical equation teaching overlay.
6. **Session Check-ins**: Checkpoint every 20 accepted attempts (correct count + median latency of correct attempts only) with Keep Going vs Take a Break zero-timing flow.
7. **Clean Practice HUD**: Distraction-free practice screen with transient session score and progress indicators removed; danger-styled Pause button.

### Immediate Next Lifecycle Steps
- Complete `DOCUMENT_ONLY` reconciliation.
- `COMMIT_ONLY` → `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`.
- Deferred Work: `MF-REL-001` (Android Internal AAB Packaging and Release Automation) remains deferred and not started.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
