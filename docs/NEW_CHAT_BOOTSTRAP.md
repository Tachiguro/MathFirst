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
- Synchronized `main` commit SHA: `336322b5386a872ebb726c7bdcf34bb207592650` (PR #35 merge)
- Active work package context: `MF-UX-005` (Native UX, Responsiveness, and Interaction Polish)
- Active implementation branch: `feat/mf-ux-005-tester-apk-workflow` (candidate HEAD `22665bc06199b6f8c8fc7f2d11975d3440cac292` during review; documentation unstaged in working tree)

### Session Discovery & Candidate Resolution Protocol
When initializing a new session:
1. **Inspect live Git and GitHub first**: Check `git rev-parse HEAD`, `git branch -vv`, `git status`, and `gh pr list`.
2. **Verify synchronized `main`**: Ensure local `main` is clean and synchronized with `origin/main` at `336322b5386a872ebb726c7bdcf34bb207592650`.
3. **Recognize completed baseline packages & PRs**: Confirm pre-release packages through `MF-REL-002` (PR #26), post-release reconciliation (PR #27), forensic remediation (PR #28, PR #29), and `MF-UX-005` deliverables: Slices 1–6 (PR #30), Slice 7 startup white flash (PR #31), Release startup resource order fix (PR #32), Tester Ergonomics metadata (PR #33), Tester Ergonomics diagnostics & settings UX (PR #34), and post-PR #34 reconciliation (PR #35) are merged.
4. **Recognize completed in-flight deliverables**: Repeatable Tester Artifact / APK Workflow (Slices 1–4) implemented and reviewed with `REVIEW_PASS` across 1462 passing Core tests on `feat/mf-ux-005-tester-apk-workflow`.
5. **Recognize remaining accepted `MF-UX-005` boundaries**: Installed Size / App Data investigation and physical Android device verification.
6. **Await explicit dispatch**: Do not autonomously select or start the next slice without explicit user orchestration.

### Durable Merged Baseline Summary
- **MF-UX-005 Tester APK Packaging Workflow** (Slices 1–4 on `feat/mf-ux-005-tester-apk-workflow`): Dedicated `ReleaseProfile.Tester`, deterministic APK naming and workspace routing (`artifacts/android/tester/<ArtifactId>/`), authoritative offline APK validation (`AndroidApkValidator` with `apksigner`, `aapt2 dump xmltree`, `dexdump -f`, manifest security rules, development-debug certificate policy), ValidationReceipt Schema v1 `Tester` receipts, deterministic `TESTER_README.md`, profile-aware `SHA256SUMS`, `AndroidPackageCommand` integration, and PowerShell entrypoints `scripts/package-android-tester-apk.ps1` and `scripts/validate-android-apk.ps1` (1462 Core tests passing).
- **MF-UX-005 Post-PR #34 Documentation Reconciliation** (PR #35 at `336322b5386a872ebb726c7bdcf34bb207592650`): Reconciled baseline documentation on `main` following merge of PR #34.
- **MF-UX-005 Tester Diagnostics & Settings UX** (PR #34 at `82c117912f72d6efe16055f8be754c9c577ae15a`): Support-safe deterministic diagnostics formatter, minimal platform info and clipboard abstractions (`IAppPlatformInfo`, `IClipboardService`) with MAUI implementations, Settings footer build identity display and localized "Copy diagnostic info" action with async execution and status feedback, complete EN/DE/RU localization keys.
- **MF-UX-005 Build Identity Metadata** (PR #33 at `83b767c2265c1baba560abdeaa9fedad365d70da`): Extended runtime build metadata model/parser with `ApplicationId`, `SourceCommit`, and explicit fail-closed `BuildClassification` (`Local`, `Tester`, `SourceCandidate`, `Production`), deterministic short SHA formatting, MSBuild projection in `MathFirst.App.csproj`, and `AppBuildInfo` integration.
- **MF-UX-005 Release Startup Order Fix** (PR #32 at `cf1d2a3f4779c16f1c6104fe8736f405eb14d94e`): Deferred `MainPage` resolution until `CreateWindow()`, ensuring `App.InitializeComponent()` loads `Application.Resources` before `MainPage` static resource resolution, eliminating startup `XamlParseException`.
- **MF-UX-005 Startup White Flash Elimination** (PR #31 at `15d39ac73ecdc276db2d3f1a9b6ea3ac0f8849b6`): Continuous `#176B4D` background across splash, Android WebView canvas, and HTML first paint, removing raw unstyled placeholder.
- **MF-UX-005 Native UX Polish Slices 1–6** (PR #30 at `bde91a7578753f5c468d0534282edd9fd13f32a0`): Timer render isolation (~10 Hz in child component), keypad visual reset per fact, KnownFirst onboarding vertically stacked full-width action layout, application-owned `IAppBackNavigationCoordinator` handling Android system Back across Onboarding/Settings/Practice, 3s teaching dwell lock, amber Pause styling (`.button-pause`), clean bold sans-serif timer typography with translucent pill backing, configurable haptic feedback with normal `VIBRATE` permission, No Time Pressure practice time mode with count-up elapsed display, restrained textual streak indicator ($\ge 3$), and transient Pause session summary.
- **Native V1 Forensic Remediation** (PR #28 plan at `4e997f35b4a7884b4b0592beab682a36319ea358`, PR #29 fix at `156d5afd32299ba19d8ca2a8f2f56a7babfe31d8`): Practice Fact Eligibility Invariant ($\text{owner}_O(F) \le B$) and session startup error boundary / recovery ([ADR-0007](decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md)).
- **Predecessors**: `MF-REL-002` (PR #26), `MF-UX-004` (PR #25), `MF-DOC-004` (PR #24), `MF-STAB-002` (PR #21, #22, #23), MathFirst Privacy Policy (PR #20), `MF-DOC-003` (PR #19), `MF-SET-001` (PR #18), `MF-REL-001` (PR #17), `MF-LEARN-003` (PR #16), and prior foundational packages.

### Downstream Roadmap Stages
- **Remaining MF-UX-005 Slices**: Installed Size / App Data investigation, physical Android device verification.
- **Phase 6 - Final Exact-Candidate Native V1 Validation**: Pending execution on synchronized `main` post-MF-UX-005.
- **Phase 6 - Production Packaging & Signing**: Separately authorized downstream work (`Distributable` profile with external production keystore).
- **Phase 6 - Final Real-Device Technical and Functional Verification**: Separately authorized physical target hardware validation.
- **Phase 6 - Google Play Publication**: Separate subsequent release decision.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
