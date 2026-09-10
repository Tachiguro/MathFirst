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
`main` / `origin/main`: `30580ce7788466e6524668a4a7f479eb76274b5c`

The active task is MF-STAB-001, **Practice Progression and HUD Stabilization**, on `feat/mf-stab-001-practice-stabilization`. Its current local implementation candidate is `eed6e66481fc40f60ffdf4fdb98f6c6ba55bb264`, six commits ahead of `main` and zero behind before documentation reconciliation:

1. `243008d5134600f93066c637dd7014cbbc8edd26` — multiplication bootstrap advancement
2. `29cf40b9764ecdb57a28aaa91f1c719c9a0b253b` — fixed answer deadline
3. `8b75d701026ef5b7a45fd6844c13eef735167460` — practice-header progress HUD
4. `3fc1605174843638a2d03efeef31e22ec4bc3f25` — review remediation
5. `cac7f1f5d03336ba8e84c65dc8ced33f6e4ee292` — documentation reconciliation (docs commit between checkpoints 4 and 5)
6. `eed6e66481fc40f60ffdf4fdb98f6c6ba55bb264` — validation test remediation

Implementation and test remediation are complete and `REVIEW_APPROVED` with no findings. The first `FULL_VALIDATION` attempt on `cac7f1f5d03336ba8e84c65dc8ced33f6e4ee292` failed solely due to a stale legacy integration expectation; post-remediation Core tests passed 546/546. Narrow documentation reconciliation is the current `DOCUMENT_ONLY` lifecycle step and remains local and uncommitted. After documentation is committed, `FULL_VALIDATION` must target the newly created documentation-inclusive SHA; no push, Pull Request, or merge has occurred. MF-UX-003 is complete and merged through Pull Request #12 at `30580ce7788466e6524668a4a7f479eb76274b5c`. MF-REL-001 remains not started.

After successful documentation reconciliation with changes, the exact next lifecycle is `COMMIT_ONLY — MF-STAB-001 — Validation Remediation Documentation Commit`.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
