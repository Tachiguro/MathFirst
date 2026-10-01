# New Chat Session Bootstrap Protocol

This document defines the mandatory discovery and re-anchoring procedure for newly launched chat sessions or agents interacting with the MathFirst repository.

---

## 1. Capability & Identity Gate

### Capability Check
Before making any claims about repository status, determine whether tools providing direct local repository access (e.g. terminal execution, file inspection) and GitHub access (e.g. `gh` CLI) are available in the current runtime environment:
- **If repository access tools are available**: Execute the live discovery commands below. Never assume or rely on remembered chat history.
- **If repository access tools are NOT available**: State clearly that live repository state cannot be verified directly. Never fabricate or extrapolate repository facts without tool access.

### Project & Task Identity Check (Fail-Closed)
Before answering the substantive task or making ANY repository mutation, verify:
1. `PROJECT` matches `MathFirst`.
2. `REPOSITORY` matches `Tachiguro/MathFirst`.
3. Canonical checkout path resolves to `C:\Dev\MathFirst`.
4. Requested task belongs to `MathFirst` and conforms to the explicitly declared `Operation Mode` and authorized package scope.

If a foreign project/repository is detected:
- Perform ZERO repository mutations;
- Do NOT answer the foreign task substantively;
- Output the standard `PROJECT_MISMATCH` report and HARD STOP.

If an out-of-scope task is detected:
- Perform ZERO repository mutations;
- Output the standard `TASK_SCOPE_MISMATCH` report and HARD STOP.

Follow-up conversation messages do NOT implicitly override project identity, repository identity, operation mode, or authorized scope.

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
2. **Genuinely Divergent Local Task Branch / Checkpoint Work**: A dedicated local task branch with unmerged commits or active checkpoint work (where `git log main..<branch>` contains unmerged commits not part of an already-merged/closed PR) indicates active in-flight implementation.
3. **`docs/CURRENT_WORK.md`**: Operational evidence of active package context and lifecycle phase, consistent with live Git state.
4. **`docs/BACKLOG.md`**: Authoritative registry of accepted inactive work.

> [!IMPORTANT]
> **Active Branch vs. Historical Branch Discrimination**:
> - The mere existence of a local or remote task branch does **NOT** prove that active work is in flight. A branch that has already been fully merged into `main` and associated with a merged/closed Pull Request is historical.
> - An active task branch requires actual unmerged, divergent, or checkpoint commits relative to `main` (`git log main..<branch>` or unpushed work). Live PR state and commit ancestry determine whether a branch represents in-flight work.
> - The absence of an open Pull Request does NOT prove that there is no active work, because local slice implementation or unpushed task branches may be actively progressing.

---

## 4. Determining the Next Work Item

- If live state matches an active package (open PR, active task branch, or `docs/CURRENT_WORK.md`), report the exact verified package and its current lifecycle phase.
- If all branches are merged, working tree is clean on `main`, and no package is active in `docs/CURRENT_WORK.md`, state:
  > **"There is currently no clearly determined next work item."**
- **Strict Prohibition**: Agents must NEVER autonomously pick an item from [docs/BACKLOG.md](BACKLOG.md) or [docs/ROADMAP.md](ROADMAP.md) without explicit user instruction and prompt dispatch.

---

## 5. Explicitly Authorized Next Package Context

Use the live-state discovery rules above to distinguish the following cases:

1. **Verified active package**: An open Pull Request, a genuinely divergent local task branch with unmerged checkpoint work, or `docs/CURRENT_WORK.md` consistent with live state identifies a package in flight. Report that package and continue only within its verified lifecycle.
2. **No active package**: If no package is active and no explicit authorization is supplied, do not autonomously select a BACKLOG or ROADMAP item. State clearly that there is currently no active work in flight.
3. **Explicitly authorized next package**: If the user or active orchestration supplies a named package/task and lifecycle step (e.g. Roadmap Step 51 Final V1 Gap Audit), first verify live repository state, then proceed only with that authorized task under repository governance.

Agents must NOT autonomously begin Roadmap Step 51 or any other downstream activity merely because it is listed as next; explicit user/orchestrator dispatch is required.
Explicit authorization does not override GitHub or live Git evidence, create an active branch by implication, or permit autonomous selection of unrelated work.

---

## 6. Historical Reference Snapshot (Recorded 2026-10-01)

> [!IMPORTANT]
> **Reference & History Only**: The snapshot recorded below reflects historical reference points as of 2026-10-01. It is strictly non-authoritative for current checkout or task state and MUST NOT be used to determine:
> - Current branch
> - Current HEAD commit SHA
> - Current package in flight
> - Current lifecycle phase
> - Working-tree cleanliness or modification state
> - Staged or untracked file state
> - Open Pull Request state
>
> All current operational facts must be derived exclusively from live discovery commands (Section 2) and live GitHub queries at session startup. Live local Git and GitHub repository state always take precedence over documentation baselines.

Repository: `Tachiguro/MathFirst`
Canonical path: `C:\Dev\MathFirst`
Worktrees: Exactly one normal worktree by default

### Historical Reference Delivery Baseline (Snapshot as of 2026-10-01)
- **Historical Documentation Reconciliation Reference**: PR #57 (`975fb134636f33fba4a54b399aedae80edf95235`): `docs: reconcile post-mf-telem-001 project state`
- **Delivered Implementation Package Reference (MF-TELEM-001)**: PR #56 (`bbdf62652927efa26475a9f2d83778de6465f5e1`): `MF-TELEM-001: Tester Telemetry Export and Share`
- **Preceding Historical Reference PRs**:
  - PR #55 (`76116d11b8563b0407188ba53ccefd998eda958d`): `MF-UX-008: stabilize Cyber Defense combat layout and opponent presentation`
  - PR #54 (`bc7471b098e2f79262ff6e71302820bd281a14d5`): `MF-LEARN-006: adaptive learning policy, 482 benchmark, and durable calibration`
  - PR #53 (`50aed4714937777f98058cc64afb75a75d749709`): `docs: reconcile post-pr51 pr52 project state`
  - PR #52 (`f6a842b71df39b2d6facafae41874c86d6f3f611`): `docs: harden agent prompt governance`
  - PR #51 (`01472b05ef83f586144414a3cb3a0c7abbc45189`): `docs: record adaptive learning policy design`
  - PR #50 (`fa99f5c162f8cbce3d55ca7a3cac9d1625249a1b`): `feat: add Cyber Defense training MVP`
- Schema: V7 (delivered via PR #56)
- Build 2 status: `REJECTED` (`RELEASE_CANDIDATE_REJECTED_PENDING_REMEDIATION`).
- Build 3 status: Historical only. Step 30 technical smoke passed and Step 31 physical-device verification passed, but source predates MF-LEARN-004, MF-LEARN-005, MF-UX-007, MF-LEARN-006, MF-UX-008, and MF-TELEM-001, and no longer represents current repository source.
- Future candidate status: Any future production candidate requires `versionCode >= 4`. Build 4 does **not** exist yet (not packaged, not signed, not tested).

### Session Discovery & Candidate Resolution Protocol
When initializing a new session:
1. **Inspect live Git and GitHub first**: Live local Git and GitHub repository state always takes precedence over documentation snapshots or remembered context. Execute the discovery sequence in Section 2 to discover live branch, HEAD SHA, working tree, tracking status, and open PR status.
2. **Verify live repository synchronization**: Fetch `origin` (`git fetch origin`), inspect live `origin/main` (`git rev-parse origin/main`), and verify that local `main` is synchronized with `origin/main` (`git status`, `git log origin/main..HEAD`, `git log HEAD..origin/main`). Never require equality to a documentation-embedded SHA; live synchronization is determined by Git tracking state.
3. **Recognize Historical Build 3 Status and Pending Candidate**: Any subsequent production candidate requires `versionCode >= 4`, repetition of Step 30 and Step 31 verification (agent-executable when authorized), and separate user Google Play Console upload and publishing.
4. **Resolve active work from live state**: Check for open PRs (`gh pr list --state open`), divergent local task branches with unmerged work, uncommitted working tree modifications, and `docs/CURRENT_WORK.md`. Active work and lifecycle state are established solely through live evidence. When no PR is open, no task branch has unmerged work, and working tree on `main` is clean, there is no in-flight work.
5. **Await explicit dispatch**: When no active package is established by live evidence, do not autonomously select a downstream task (such as Roadmap Step 51 Final V1 Gap Audit). State that there is currently no active work item and await explicit dispatch.

### Historical Delivered Baseline Summary
- **Post-MF-TELEM-001 Documentation Reconciliation** (PR #57, merge `975fb134636f33fba4a54b399aedae80edf95235`): Reconciled repository baseline documentation following the merge of PR #56.
- **MF-TELEM-001 Tester Telemetry Export and Share** (PR #56, merge `bbdf62652927efa26475a9f2d83778de6465f5e1`): Delivered Schema V7 persistence with five nullable presentation-context columns, pseudonymous random installation UUID, canonical JSON export contract (`telemetry_export_schema_v1`), sandboxed native platform sharing (`telemetry-share`), Full Local Reset cleanup, localized Settings export and reset UI, and non-interference regression coverage (`REVIEW_PASS`, 1,906 Core tests passed, 0 warnings/errors Windows & Android builds).
- **MF-UX-008 Static Combat Layout and Boss Presentation** (PR #55, merge `76116d11b8563b0407188ba53ccefd998eda958d`, feature HEAD `43949fdc7513714d9e4cbb755d0c5c8da5ba4a8b`): Delivered static combat layout positional stability (keypad and arithmetic typography stationary across combat transitions), layout-isolated boss presentation (`clamp(90px, 16vh, 140px)`), visual layering (`z-index: 4` + scrim protection), progressive opponent scaling (0.65 to 1.50), tier-preserving feedback scale composition (`SCALE_PRESERVED_ACROSS_ALL_STATES`), and scoped active-gameplay scroll suppression (`.training-host.active-gameplay`) (`REVIEW_PASS`, `USER_PHYSICAL_DEVICE_ACCEPTANCE_PASS`, 1,786 Core tests passed, Schema V6 preserved).
- **MF-LEARN-006 Adaptive Learning Policy, 482 Benchmark, and Durable Calibration** (PR #54, merge `bc7471b098e2f79262ff6e71302820bd281a14d5`, candidate `576836db96d4d16e3be2d701c66c5adea3ecd1fb`): Delivered Option-B Evidence-Adaptive Discovery, absolute no-immediate-fact-repetition invariant ($\text{FactId}(t+1) \ne \text{FactId}(t)$), tiered remediation (4 / 2 cooldown spacing; broad weakness threshold 2), Guided Gate G3 soft decoupling at `BandIndex >= 3`, durable pace calibration at $\ge 24$ positioned Correct attempts, calibrated downstream Cyber Defense Critical Hits, exact 482 strong-learner benchmark, restart determinism, and exact-candidate `FULL_VALIDATION_PASS` (1,772 Core tests passed in Debug/Release, 0 warnings/errors Windows & Android Release builds, 0 NuGet vulnerabilities, Schema V6 preserved without migration).
- **Post-PR #51/#52 Documentation Reconciliation** (PR #53, merge `50aed4714937777f98058cc64afb75a75d749709`): Reconciled baseline documentation following PR #51 and PR #52.
- **Agent Prompt Governance Hardening** (PR #52, merge `f6a842b71df39b2d6facafae41874c86d6f3f611`): Hardened repository governance with strict fail-closed project/task identity verification, single-canonical-checkout enforcement, forbidden Git operations, strict staging rules, and multi-mode operation boundaries across `AGENTS.md`, `docs/PROMPT_AND_TASK_ROUTING.md`, and `docs/NEW_CHAT_BOOTSTRAP.md`.
- **Adaptive Learning Policy Design** (PR #51, merge `01472b05ef83f586144414a3cb3a0c7abbc45189`): Formalized evidence-adaptive discovery, absolute no-immediate-fact-repetition invariant, tiered weakness remediation, guided gate soft decoupling, and pace calibration benchmark in canonical specification `docs/superpowers/specs/2026-09-26-adaptive-learning-policy-design.md` and ADR-0010.
- **Cyber Defense Training MVP** (PR #50, merge `fa99f5c162f8cbce3d55ca7a3cac9d1625249a1b`): Delivered Cyber Defense mini-game training mode MVP.
- **Progression Coherence Audit** (PR #49, merge `a1a02716b0e0b0acb020c21f9d4c13045bf803c5`): Added deterministic regression coverage for curriculum progression coherence.
- **MF-DOC-008 Post-MF-UX-007 Merge State Reconciliation** (PR #48, merge `1a6b306a0346f4899247f9ea3dc05107c1afa03d`): Reconciled repository baseline documentation following PR #47 merge.
- **MF-UX-007 Progress Presentation Cleanup** (PR #47, merge `d37fbe3347679220bf847b06c83f7f9366738d03`, candidate `5f489bae56cfd3bb9ea0b895baaf6778382ef867`): Delivered concise learner-facing Stage terminology across English, German, and Russian, structured Ready Gate overview for returning learners with completed practice history, active practice HUD accessibility and tooltip descriptions (`Training_OperationProgressGroupAriaLabel`), responsive layout preservation across viewports, surface elevation design tokens, and exact-candidate `FULL_VALIDATION_PASS` (1,605 Core tests passed, 0 warnings/errors Windows & Android Release builds, 0 NuGet vulnerabilities, tree identity `6761a9eb217bcea11f0f5bc7e14cc594100efd95`, Schema V6 preserved without migration).
- **Predecessors**: Slices and packages prior to PR #47 are documented in [docs/PROJECT_STATE.md](PROJECT_STATE.md).

### Downstream Roadmap Stages
- **Release Verification & Distribution**: Release preparation (final V1 gap audit, fresh Tester APK build, manual physical-device validation on Samsung Galaxy S26 Ultra, production packaging `versionCode >= 4`, Steps 52–58) remains deferred until explicitly authorized.

This historical reference baseline is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
