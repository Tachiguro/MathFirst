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
`main` / `origin/main`: `a051518420db3b45f8ca1074bac27e9b4d1b799d` (MF-SET-001 merged via PR #18)
Worktrees: Exactly one normal worktree

### Active Package Context
- Active Task Branch: `docs/mf-doc-003-v1-baseline-reconciliation`
- Package: `MF-DOC-003` — **Post-Merge Project State and V1 Baseline Reconciliation**
- Base Branch / Commit: `main` at `a051518420db3b45f8ca1074bac27e9b4d1b799d`
- Package Type: `Documentation`
- Current Lifecycle: `DOCUMENT_ONLY`
- Next Lifecycle: `REVIEW_ONLY`
- Remote State: Branch unpushed; no Pull Request opened yet

### Durable Merged Baseline (MF-SET-001 Summary)
- **Delivery Status**: Merged into `main` via PR #18 at `a051518420db3b45f8ca1074bac27e9b4d1b799d`.
- **Enabled Operations & Onboarding Choice**: Any non-empty subset of Addition, Subtraction, Multiplication, and Division may be selected in Settings or during fresh-install Onboarding (all four enabled by default, using shared preferences). Enabled operations filter newly generated practice and the progress HUD; disabled operations retain all learning progress for later reactivation.
- **Returning Learner Progress & Session Check-In Summary**: Returning learners see a compact progress overview of enabled operations before starting practice without invented mastery percentages. Every 20 attempts, a check-in summary presents completed attempts, correct count, median correct latency, and actual Stage changes across process-local segments.
- **Current-Fact and HUD Semantics**: Settings changes preserve the displayed question. On return, the HUD immediately shows the enabled set; the next generated question follows the updated configuration.
- **Scheduling and Evidence**: `AdaptivePracticeSelector` alone schedules via $(P - 1) \bmod k$ and ordinal $\lfloor (P - 1) / k \rfloor + 1$. `PracticeSelectionEvidence` is operation/prospective-position scoped; cache state never changes the scheduled operation; required evidence is loaded on demand without wrong-operation fallback.
- **Persistence and Recovery**: `SqliteLearnerStore` remains schedule-policy agnostic and atomically validates submitted state transitions. Preferences remain outside learner SQLite; no schema change was required. Post-write evidence recovery never duplicates a durable submission.
- **Practice Time and Runtime Timer**: Standard, 30 s, 45 s, and 60 s preserve active-only raw latency semantics. Pause, Settings, and same-process background time are excluded; a true cold restart resets only the in-flight timer, not preferences or learner progress.
- **Settings Cleanup and Localization**: The developer Statistics / Diagnostics section is removed without deleting learner data. The enabled-only operation HUD remains. 20 unique Diagnostics keys (60 locale entries) were removed; `Diagnostics_Group_Learning` remains for the HUD accessible label.
- **Answer Entry & Input Lifecycle Invariants**: Unsubmitted partial input is preserved on the same exercise across pause, settings, and background transitions. Every new exercise instance always starts with an empty input buffer. Partial multi-digit input remains editable with Backspace before complete submission; single-digit correct answers auto-submit. Reset Learning Progress preserves operation/time preferences; Restore Default Settings preserves learning; Full Local Reset performs both resets.

### Immediate Next Lifecycle Steps
- Complete `DOCUMENT_ONLY` reconciliation on `docs/mf-doc-003-v1-baseline-reconciliation`.
- Next lifecycle step: `REVIEW_ONLY`.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
