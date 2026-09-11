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
`main` / `origin/main`: `9a9e5c43d1f1685cf21e766e0890b790ab09040c` (MF-LEARN-003 merged via PR #16)
Worktrees: Exactly one normal worktree

### Active Feature Candidate
- Active Task Branch: `feat/mf-rel-001-android-aab-packaging`
- Package: `MF-REL-001` — **Android Internal AAB Packaging and Release Automation**
- Operation Mode: `DOCUMENT_ONLY`
- Behavioral Implementation & Test Suite: Complete across 3 checkpoint slices with 808 passing Core tests (808/808 passed, 0 failed, 0 skipped) in `MathFirst.Core.Tests`. Clean Release builds on Windows and Android. Local AAB packaging and validation verified.

### Checkpoint Commit Chain (MF-REL-001)
- Base: `9a9e5c43d1f1685cf21e766e0890b790ab09040c`
- Slice 1: `bc7e738` (`android-manifest-hygiene-hardening`)
- Slice 2: `f890cbd` (`aab-packaging-provenance-automation`)
- Slice 3: `8e53be1` (`local-aab-validation-harness`)
- Validator fix: `7684a04` (`correct boolean operator in validate-android-aab.ps1`)

### Implemented Architecture Summary (MF-REL-001)
1. **Manifest Offline-First Hygiene**: Removal of `INTERNET` and `ACCESS_NETWORK_STATE` permissions from `AndroidManifest.xml` enforcing ADR-0002 offline privacy at the manifest boundary while preserving `android:allowBackup="true"`.
2. **Repository Secrets Hygiene**: Strict ignore rules in `.gitignore` blocking keystores (`*.keystore`, `*.jks`, `*.p12`, `*.pfx`), secret configurations (`signing.properties`, `.env`), and release artifacts (`artifacts/`, `*.aab`, `*.apk`).
3. **Repeatable AAB Packaging (`scripts/package-android-aab.ps1`)**: Deterministic Release packaging targeting `net10.0-android`, supporting fail-closed clean working tree checks, externalized signing credentials, semantic version overrides, and deterministic artifact naming.
4. **Exact Candidate Provenance**: Companion `.provenance.json` recording Git commit SHA, branch, timestamp (UTC), target framework, signing state, and SHA-256 bundle checksum.
5. **Local Artifact Validation (`scripts/validate-android-aab.ps1`)**: Offline ZIP integrity and bundle structure validation (`BundleConfig.pb`, `base/manifest/AndroidManifest.xml`, DEX bytecode, resources, signature blocks, SHA-256 hash match).

### Immediate Next Lifecycle Steps
- Complete `DOCUMENT_ONLY` reconciliation.
- `COMMIT_ONLY` → `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`.
- Release Boundary Invariant: AAB generation and validation do not imply Google Play upload, distribution, or production release.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
