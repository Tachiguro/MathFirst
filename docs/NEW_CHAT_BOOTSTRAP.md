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
Worktrees: Exactly one normal worktree

### Operational Baseline
- All planned pre-release packages through `MF-REL-002` are complete and merged into `main`.
- Historical baseline merge commit for `MF-REL-002` (PR #26): `79e0d48c058747e8112388d31d721a049ec2857a`.
- Active implementation package: None currently in flight.

### Session Discovery & Candidate Resolution Protocol
When initializing a new session:
1. **Inspect live Git and GitHub first**: Check `git rev-parse HEAD`, `git branch -vv`, `git status`, and `gh pr list`.
2. **Verify synchronized `main`**: Ensure local `main` is clean and synchronized with `origin/main`.
3. **Recognize completed pre-release packages**: Confirm `MF-DOC-004` (PR #24), `MF-UX-004` (PR #25), and `MF-REL-002` (PR #26) are merged.
4. **Recognize next technical roadmap stage**: Phase 6 Final Exact-Candidate Native V1 Validation.
5. **Resolve exact candidate commit**: Use the live, synchronized post-reconciliation `main` commit SHA as the authoritative validation candidate. Do not assume or hardcode a pre-reconciliation SHA as the final release candidate.

### Durable Merged Baseline Summary
- **MF-DOC-004** (PR #24 at `3e471e20e2d452ee1e383579ed3d0831e1825d3e`): Post-stabilization project-state documentation reconciliation.
- **MF-UX-004** (PR #25 at `46a7158d3c7fbdf6bc43fe120c35863ac55bb78b`): Keypad active press visual feedback, `:focus-visible` contract preservation, and responsive viewport validation.
- **MF-REL-002** (PR #26 at `79e0d48c058747e8112388d31d721a049ec2857a`): Release workflow hardening, generalized `SourceCandidate` packaging, validator-approved five-file promotion (`.aab`, `.provenance.json`, `.validation.json`, `TESTER_README.md`, `SHA256SUMS`), immutable provenance bytes, ValidationReceipt Schema v1, and release profile characterization.
- **Predecessors**: `MF-STAB-002` (PR #21, #22, #23), MathFirst Privacy Policy (PR #20), `MF-DOC-003` (PR #19), `MF-SET-001` (PR #18), `MF-REL-001` (PR #17), `MF-LEARN-003` (PR #16), and prior foundational packages.

### Downstream Roadmap Stages
- **Phase 6 - Final Exact-Candidate Native V1 Validation**: Pending execution on the synchronized post-reconciliation `main` commit.
- **Phase 6 - Production Packaging & Signing**: Separately authorized downstream work (`Distributable` profile with external production keystore).
- **Phase 6 - Final Real-Device Verification**: Separately authorized physical target hardware validation.
- **Phase 6 - Google Play Publication**: Separate subsequent release decision.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
