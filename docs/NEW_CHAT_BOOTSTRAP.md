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
- Synchronized `main` commit SHA: `60dba236aa38bca138ab583f610c9ff876994b04` (PR #39 merge)
- Most recently completed merged package: `MF-UX-005` — Native UX, Responsiveness, and Interaction Polish (completed through PR #39)
- Active package: `MF-UX-006` — V1 Privacy, Copy, and Localization Hardening
- Active task branch: `feat/mf-ux-006-v1-privacy-copy-localization`
- Active lifecycle: `DOCUMENT_ONLY`
- Pre-documentation candidate HEAD: `130cae92032ebdfbd6f430b5cf353192d5e39ba3`
- Base baseline: `60dba236aa38bca138ab583f610c9ff876994b04` (PR #39 merge)
- Package review status: `REVIEW_PASS`
- Next expected mode after documentation reconciliation: `COMMIT_ONLY`, followed by `FULL_VALIDATION`

### Session Discovery & Candidate Resolution Protocol
When initializing a new session:
1. **Inspect live Git and GitHub first**: Check `git rev-parse HEAD`, `git branch -vv`, `git status`, and `gh pr list`.
2. **Verify synchronized `main`**: Ensure local `main` and `origin/main` resolve to `60dba236aa38bca138ab583f610c9ff876994b04` unless newer live evidence exists.
3. **Recognize completed MF-UX-005 history**: PR #30 through PR #39 are merged. PR #36 delivered the repeatable Tester APK workflow, PR #37 delivered Release source-map exclusion, PR #38 delivered Timer visual remediation, and PR #39 delivered final documentation reconciliation.
4. **Recognize MF-UX-006 candidate state**: Implemented across three checkpoint commits on `feat/mf-ux-006-v1-privacy-copy-localization` (candidate pre-doc HEAD `130cae92032ebdfbd6f430b5cf353192d5e39ba3`), passing consolidated review (`REVIEW_PASS`), with `DOCUMENT_ONLY` reconciliation active and `FULL_VALIDATION` pending.
5. **Resolve active work from live state**: Documentation may lag a newer branch or PR. Live Git and GitHub remain authoritative.
6. **Await explicit dispatch**: When no active package is established by live evidence, do not autonomously select a downstream task.

### Durable Merged Baseline Summary
- **MF-UX-005 Final Documentation Reconciliation** (PR #39, merge `60dba236aa38bca138ab583f610c9ff876994b04`): Established the post-MF-UX-005 synchronized documentation baseline on `main`.
- **MF-UX-005 Timer Visual Remediation** (PR #38, merge `d1705bbdc0013372e44eadf7310ff2f313ecdcef`): Removed the Timer backing pill and retained bold white tabular numerals with a restrained dark local shadow/contour; 9/9 targeted visual-contract tests and 1463/1463 full Release Core tests passed.
- **MF-UX-005 Release Size Hygiene** (PR #37, merge `dbc6bf045454a78a1ba32793fa02245f83b50437`): Release-only exclusion of `bootstrap.min.css.map`, with 9/9 targeted packaging tests and 1463/1463 full Release Core tests passing.
- **MF-UX-005 Repeatable Tester APK Workflow** (PR #36, merge `e908bf2fba820f4f6adf03b278861b956e5dbbd5`): Dedicated `ReleaseProfile.Tester`, deterministic APK naming and workspace routing, authoritative offline APK validation in `MathFirst.ReleaseTool`, ValidationReceipt Schema v1 evidence, and PowerShell entrypoints `scripts/package-android-tester-apk.ps1` and `scripts/validate-android-apk.ps1`; historical automated evidence was 1462 passing Core tests.
- **MF-UX-005 Investigation and Physical Evidence**: Installed Size/App Data/RAM investigation completed with evidence limitations. A Timer-specific `TEST_ONLY` run on Samsung SM-S948B, Android 16, arm64-v8a was explicitly accepted by the user; final release-grade physical validation remains separate.
- **MF-UX-005 Post-PR #34 Documentation Reconciliation** (PR #35 at `336322b5386a872ebb726c7bdcf34bb207592650`): Reconciled baseline documentation on `main` following merge of PR #34.
- **MF-UX-005 Tester Diagnostics & Settings UX** (PR #34 at `82c117912f72d6efe16055f8be754c9c577ae15a`): Support-safe deterministic diagnostics formatter, minimal platform info and clipboard abstractions (`IAppPlatformInfo`, `IClipboardService`) with MAUI implementations, Settings footer build identity display and localized "Copy diagnostic info" action with async execution and status feedback, complete EN/DE/RU localization keys.
- **MF-UX-005 Build Identity Metadata** (PR #33 at `83b767c2265c1baba560abdeaa9fedad365d70da`): Extended runtime build metadata model/parser with `ApplicationId`, `SourceCommit`, and explicit fail-closed `BuildClassification` (`Local`, `Tester`, `SourceCandidate`, `Production`), deterministic short SHA formatting, MSBuild projection in `MathFirst.App.csproj`, and `AppBuildInfo` integration.
- **MF-UX-005 Release Startup Order Fix** (PR #32 at `cf1d2a3f4779c16f1c6104fe8736f405eb14d94e`): Deferred `MainPage` resolution until `CreateWindow()`, ensuring `App.InitializeComponent()` loads `Application.Resources` before `MainPage` static resource resolution, eliminating startup `XamlParseException`.
- **MF-UX-005 Startup White Flash Elimination** (PR #31 at `15d39ac73ecdc276db2d3f1a9b6ea3ac0f8849b6`): Continuous `#176B4D` background across splash, Android WebView canvas, and HTML first paint, removing raw unstyled placeholder.
- **MF-UX-005 Native UX Polish Slices 1–6** (PR #30 at `bde91a7578753f5c468d0534282edd9fd13f32a0`): Timer render isolation and keypad reliability; KnownFirst onboarding layout and Android Back coordinator; teaching dwell lock, amber Pause styling, and the historical Timer pill later superseded by PR #38; configurable haptics; No Time Pressure visible elapsed count-up; restrained textual streak and transient Pause summary.
- **Native V1 Forensic Remediation** (PR #28 plan at `4e997f35b4a7884b4b0592beab682a36319ea358`, PR #29 fix at `156d5afd32299ba19d8ca2a8f2f56a7babfe31d8`): Practice Fact Eligibility Invariant ($\text{owner}_O(F) \le B$) and session startup error boundary / recovery ([ADR-0007](decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md)).
- **Predecessors**: `MF-REL-002` (PR #26), `MF-UX-004` (PR #25), `MF-DOC-004` (PR #24), `MF-STAB-002` (PR #21, #22, #23), MathFirst Privacy Policy (PR #20), `MF-DOC-003` (PR #19), `MF-SET-001` (PR #18), `MF-REL-001` (PR #17), `MF-LEARN-003` (PR #16), and prior foundational packages.

### Downstream Roadmap Stages
- **Active Package**: `MF-UX-006` (under `DOCUMENT_ONLY` reconciliation on task branch).
- **Phase 6 - Final Exact-Candidate Native V1 Validation**: Pending after MF-UX-006 commit/merge and requires explicit authorization.
- **Phase 6 - Production Packaging & Signing**: Separately authorized downstream work (`Distributable` profile with external production keystore).
- **Phase 6 - Final Release-Grade Physical Validation**: Pending separately authorized validation of the exact production candidate.
- **Phase 6 - Google Play Gate**: Pending and separately authorized.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
