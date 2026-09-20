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
- Synchronized `main` commit SHA: `caffe0e883f83249bee2c9a1f2122543e88c9ab0` (PR #42 merge)
- Most recently completed merged implementation package on `main`: `MF-STAB-003` — Enabled-Subset Scheduling and Current-Fact Reconciliation (PR #42 at `caffe0e883f83249bee2c9a1f2122543e88c9ab0`, validated candidate `766d8ea7692d139425e2301121f93af7901cf238`, tree identity `fcab56b3a886ed0c5018d4f8a16304ee83378b26`)
- Active package: `MF-DOC-006` — Post-MF-STAB-003 Merge State Reconciliation
- Active task branch: `docs/mf-doc-006-post-mf-stab-003-merge-reconciliation`
- Active lifecycle: `DOCUMENT_ONLY`
- Active implementation package: None (no implementation package is active; downstream packages must not be autonomously selected)
- Base baseline: `caffe0e883f83249bee2c9a1f2122543e88c9ab0` (PR #42 merge)
- Schema: V6 (preserved without migration)
- Build 2 status: `REJECTED` (`RELEASE_CANDIDATE_REJECTED_PENDING_REMEDIATION`)
- Next expected mode for MF-DOC-006: `REVIEW_ONLY` $\to$ `COMMIT_ONLY` $\to$ `FULL_VALIDATION` $\to$ `PUSH_ONLY` $\to$ `PR_ONLY`

### Session Discovery & Candidate Resolution Protocol
When initializing a new session:
1. **Inspect live Git and GitHub first**: Check `git rev-parse HEAD`, `git branch -vv`, `git status`, and `gh pr list`.
2. **Verify synchronized `main`**: Ensure local `main` and `origin/main` resolve to `caffe0e883f83249bee2c9a1f2122543e88c9ab0` unless newer live evidence exists.
3. **Recognize Build 2 Rejection and Merged MF-STAB-003 Remediation**: Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to selector crash/starvation when operations were disabled after practice and the session restarted. The defect is resolved in merged source on `main` via `MF-STAB-003` (PR #42, [ADR-0008](decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)). However:
   - no replacement production candidate exists yet;
   - no new AAB has been produced or signed (next candidate `versionCode` must be $\ge 3$);
   - Step 30 has not been rerun;
   - Step 31 has not been rerun;
   - Step 32 / Google Play is not authorized.
4. **Recognize completed MF-STAB-003, MF-DOC-005, and MF-UX-006 history**: PR #42 merged MF-STAB-003 to `main` at `caffe0e883f83249bee2c9a1f2122543e88c9ab0`. PR #41 merged MF-DOC-005 at `284d7cf2c50be6e2d4f219c00aa20d92387338f9`. PR #40 merged MF-UX-006 at `ae69f4ae27397fc6edf36a23bb671b0410680be1`.
5. **Resolve active work from live state**: Documentation may lag a newer branch or PR. Live Git and GitHub remain authoritative (Live Git/GitHub > documentation > chat history).
6. **Await explicit dispatch**: When no active package is established by live evidence, do not autonomously select a downstream task. After MF-DOC-006 completes, new sessions must not autonomously activate MF-LEARN-004, MF-UX-007, or any other package without explicit user dispatch.

### Durable Merged Baseline Summary
- **MF-STAB-003 Enabled-Subset Scheduling and Current-Fact Reconciliation** (PR #42, merge `caffe0e883f83249bee2c9a1f2122543e88c9ab0`, candidate `766d8ea7692d139425e2301121f93af7901cf238`): Established independent per-operation role ordinals ([ADR-0008](decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)), durable Schema V6 count reconstruction, deterministic zero-mutation Settings/current-fact reconciliation, and exact-candidate `FULL_VALIDATION_PASS` (1,516 Core tests passed, 0 warnings/errors Windows & Android Release builds, 0 NuGet vulnerabilities, tree identity `fcab56b3a886ed0c5018d4f8a16304ee83378b26`).
- **MF-DOC-005 Post-MF-UX-006 Documentation Reconciliation** (PR #41, merge `284d7cf2c50be6e2d4f219c00aa20d92387338f9`): Reconciled repository baseline documentation following PR #40 merge.
- **MF-UX-006 V1 Privacy, Copy, and Localization Hardening** (PR #40, merge `ae69f4ae27397fc6edf36a23bb671b0410680be1`, candidate `606158a233d7cface85fa0ef7bd03c2f9ef4f4cb`): Delivered PC numpad reset copy alignment, Russian localization formatting and progression terminology polish, offline in-app `/privacy` surface and Settings entry with system Back integration, language-neutral and multilingual static fatal host fallback in `index.html`, and exact-candidate `FULL_VALIDATION_PASS` (1476 Core tests passed, 0 warnings/errors Windows & Android Release builds, 0 NuGet vulnerabilities, tree identity).
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
- **Phase 6 - Native V1 Remediation (`MF-STAB-003`)**: Complete and merged to `main` through PR #42 at `caffe0e883f83249bee2c9a1f2122543e88c9ab0`.
- **Phase 6 - Post-MF-STAB-003 Documentation Reconciliation (`MF-DOC-006`)**: In progress on `docs/mf-doc-006-post-mf-stab-003-merge-reconciliation` (`DOCUMENT_ONLY`).
- **Phase 6 - Production Packaging & Signing (Candidate `versionCode` $\ge 3$)**: Separately authorized downstream work (`Distributable` profile with external production keystore).
- **Phase 6 - Final Release-Grade Physical Validation (Steps 30 & 31)**: Pending separately authorized repetition on physical hardware (Samsung SM-S948B, Android 16) following Build 2 rejection.
- **Phase 6 - Google Play Gate**: Pending and separately authorized.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
