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
- Current Candidate HEAD: `0c896e6eac827bf3dddca244bc507306997fc2f9`
- Operation Mode: `DOCUMENT_ONLY`
- Behavioral Implementation & Test Suite: Complete across 3 slices and remediated under consolidated re-review (`REVIEW_PASS`). Targeted packaging, script, and validation test suites passed during implementation; final review remediation verified 141 directly relevant tests; full Core suite validation remains pending for formal `FULL_VALIDATION`. Clean Release builds on Windows and Android. Local AAB packaging and validation verified.
- Unpushed Commits: 4 forward-only corrective commits ahead of `origin/feat/mf-rel-001-android-aab-packaging`.
- Remote PR #17: Open on GitHub (base `main`, remote head `eccfa823e365dd728b3c63b5065601f561969f9c`), intentionally stale, will be updated during `PR_ONLY`.

### Commit Chain (MF-REL-001)
- Base: `9a9e5c43d1f1685cf21e766e0890b790ab09040c`
- Initial Slices (Pushed to origin): `bc7e738` (manifest hygiene), `f890cbd` (packaging provenance), `8e53be1` (validation harness), `7684a04` (validator fix), `09f88ff` (initial doc reconciliation), `eccfa82` (line ending normalization).
- Forward-Only Corrective Commits (Local):
  - `199dbd7`: Slice 1/3 `android-release-policy` — Android release policy, multi-generation backup rules, net10.0-android36.0 TFM, and AndroidPackagingContractTests.
  - `179273e`: Slice 2/3 `safe-packaging-provenance` — ReleaseTool packaging architecture, strict packaging profiles (`SourceCandidate` vs `Distributable`), companion schema-v1 provenance JSON, and AabPackagingScriptValidationTests.
  - `c227ab6`: Slice 3/3 `authoritative-aab-validation` — Authoritative AndroidAabValidator, JarSignatureInspector, validate-android-aab wrapper, and AndroidAabValidationTests.
  - `0c896e6`: Review remediation `fix(release): correct AAB signature and signer validation` resolving R01 (jarsigner self-signed/trust/timestamp warnings non-fatal while requiring cryptographic verification), R02 (parse keytool Signer #N blocks, extract Certificate #1 leaf SHA-256 fingerprint, ignore chain certs as separate signers), and R03 (remove `--no-build` from `scripts/validate-android-aab.ps1` so tool builds in fresh checkout). Candidate HEAD `0c896e6eac827bf3dddca244bc507306997fc2f9`.

### Implemented Architecture Summary (MF-REL-001)
1. **Release Automation Architecture**: Dedicated .NET 10 console release tool (`tools/MathFirst.ReleaseTool`) invoked via thin PowerShell wrappers (`scripts/package-android-aab.ps1`, `scripts/validate-android-aab.ps1`).
2. **Packaging Profiles**:
   - `SourceCandidate`: branch `feat/mf-rel-001-android-aab-packaging`, exact SHA candidate provenance, debug-signed, non-distributable.
   - `Distributable`: branch `main` only, HEAD == local main == origin/main, Release build, external signing credentials with file-indirect passwords `file:<path>`, approved SHA-256 fingerprint validation.
3. **Android Manifest & Security Hygiene**: Stripped `INTERNET` and `ACCESS_NETWORK_STATE` permissions (zero network permissions contract), retained `android:allowBackup="true"` in manifest root and configured multi-generation data extraction rules (`data_extraction_rules.xml` API 31+ deny cloud backup, allow device-to-device transfer only for learner SQLite databases; `backup_rules.xml` API 28-30 device-to-device only; API 24-27 deny-all backup). Added `.gitignore` patterns preventing keystore and secret leakage.
4. **Exact Candidate Provenance**: Companion schema-v1 provenance JSON (`<ArtifactId>.provenance.json`) recording metadata (commit SHA, branch, timestamp UTC, target framework, package name, version, signing profile, bundle SHA-256).
5. **Local Artifact Validation**: Offline ZIP integrity and bundle structure validation (`BundleConfig.pb`, `base/manifest/AndroidManifest.xml`, DEX bytecode, resources, signature blocks, certificate SHA-256 leaf extraction, SHA-256 hash match).

### Immediate Next Lifecycle Steps
- Complete `DOCUMENT_ONLY` reconciliation (uncommitted documentation changes locally).
- `COMMIT_ONLY` → `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`.
- Release Boundary Invariant: AAB generation and validation do not imply Google Play upload, distribution, or production release.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
