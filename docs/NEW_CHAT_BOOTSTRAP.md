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

## 6. Reference Merged Baseline (Recorded 2026-09-27)

> [!IMPORTANT]
> **Reference & History Only**: The baseline recorded below reflects repository history as of 2026-09-27. It is strictly non-authoritative for current checkout or task state and MUST NOT be used to determine:
> - Current branch
> - Current HEAD SHA
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

### Historical Merged Baseline (as of 2026-09-27)
- Synchronized `main` commit SHA: `01472b05ef83f586144414a3cb3a0c7abbc45189` (PR #51 merge `Merge pull request #51 from Tachiguro/docs/adaptive-learning-policy-design`)
- Latest merged Pull Request on `main`: PR #51 — `docs: record adaptive learning policy design` (`01472b05ef83f586144414a3cb3a0c7abbc45189`)
- Preceding recently merged PRs:
  - PR #50 (`fa99f5c162f8cbce3d55ca7a3cac9d1625249a1b`): `feat: add Cyber Defense training MVP`
  - PR #49 (`a1a02716b0e0b0acb020c21f9d4c13045bf803c5`): `Progression Coherence Audit — deterministic regression coverage`
  - PR #48 (`1a6b306a0346f4899247f9ea3dc05107c1afa03d`): `MF-DOC-008 — Post-MF-UX-007 Merge State Reconciliation`
  - PR #47 (`d37fbe3347679220bf847b06c83f7f9366738d03`): `MF-UX-007 — Progress Presentation Cleanup`
- Schema: V6 (preserved without migration)
- Build 2 status: `REJECTED` (`RELEASE_CANDIDATE_REJECTED_PENDING_REMEDIATION`).
- Build 3 status: Historical only. Step 30 technical smoke passed and Step 31 physical-device verification passed, but source predates MF-LEARN-004, MF-LEARN-005, MF-UX-007, Cyber Defense MVP, and Adaptive Learning policy design, and no longer represents current repository source.
- Future candidate status: Any future production candidate requires `versionCode >= 4`. Build 4 does **not** exist yet (not packaged, not signed, not tested).

### Session Discovery & Candidate Resolution Protocol
When initializing a new session:
1. **Inspect live Git and GitHub first**: Live local Git and GitHub repository state always takes precedence over documentation snapshots or remembered context. Execute the discovery sequence in Section 2 to discover live branch, HEAD SHA, working tree, and open PR status.
2. **Verify synchronized `main`**: Ensure local `main` and `origin/main` resolve to `01472b05ef83f586144414a3cb3a0c7abbc45189` unless newer live commits exist.
3. **Recognize Historical Build 3 Status and Pending Candidate**: Any subsequent production candidate requires `versionCode >= 4`, repetition of Step 30 and Step 31 verification (agent-executable when authorized), and separate user Google Play Console upload and publishing.
4. **Resolve active work from live state**: Check for open PRs, local task branches, uncommitted working tree modifications, and `docs/CURRENT_WORK.md`. Active work and lifecycle state are established solely through live evidence. When no PR is open, no task branch is active, and working tree on `main` is clean, there is no in-flight work.
5. **Await explicit dispatch**: When no active package is established by live evidence, do not autonomously select a downstream task.

### Durable Merged Baseline Summary
- **MF-LEARN-006 Adaptive Learning Policy, 482 Benchmark, and Durable Calibration** (PR #54, merge `bc7471b098e2f79262ff6e71302820bd281a14d5`, candidate `576836db96d4d16e3be2d701c66c5adea3ecd1fb`): Delivered Option-B Evidence-Adaptive Discovery, absolute no-immediate-fact-repetition invariant ($\text{FactId}(t+1) \ne \text{FactId}(t)$), tiered remediation (4 / 2 cooldown spacing; broad weakness threshold 2), Guided Gate G3 soft decoupling at `BandIndex >= 3`, durable pace calibration at $\ge 24$ positioned Correct attempts, calibrated downstream Cyber Defense Critical Hits, exact 482 strong-learner benchmark, restart determinism, and exact-candidate `FULL_VALIDATION_PASS` (1,772 Core tests passed in Debug/Release, 0 warnings/errors Windows & Android Release builds, 0 NuGet vulnerabilities, Schema V6 preserved without migration).
- **Post-PR #51/#52 Documentation Reconciliation** (PR #53, merge `50aed4714937777f98058cc64afb75a75d749709`): Reconciled baseline documentation following PR #51 and PR #52.
- **Agent Prompt Governance Hardening** (PR #52, merge `f6a842b71df39b2d6facafae41874c86d6f3f611`): Hardened repository governance with strict fail-closed project/task identity verification, single-canonical-checkout enforcement, forbidden Git operations, strict staging rules, and multi-mode operation boundaries across `AGENTS.md`, `docs/PROMPT_AND_TASK_ROUTING.md`, and `docs/NEW_CHAT_BOOTSTRAP.md`.
- **Adaptive Learning Policy Design** (PR #51, merge `01472b05ef83f586144414a3cb3a0c7abbc45189`): Formalized evidence-adaptive discovery, absolute no-immediate-fact-repetition invariant, tiered weakness remediation, guided gate soft decoupling, and pace calibration benchmark in canonical specification `docs/superpowers/specs/2026-09-26-adaptive-learning-policy-design.md` and ADR-0010.
- **Cyber Defense Training MVP** (PR #50, merge `fa99f5c162f8cbce3d55ca7a3cac9d1625249a1b`): Delivered Cyber Defense mini-game training mode MVP.
- **Progression Coherence Audit** (PR #49, merge `a1a02716b0e0b0acb020c21f9d4c13045bf803c5`): Added deterministic regression coverage for curriculum progression coherence.
- **MF-DOC-008 Post-MF-UX-007 Merge State Reconciliation** (PR #48, merge `1a6b306a0346f4899247f9ea3dc05107c1afa03d`): Reconciled repository baseline documentation following PR #47 merge.
- **MF-UX-007 Progress Presentation Cleanup** (PR #47, merge `d37fbe3347679220bf847b06c83f7f9366738d03`, candidate `5f489bae56cfd3bb9ea0b895baaf6778382ef867`): Delivered concise learner-facing Stage terminology across English, German, and Russian, structured Ready Gate overview for returning learners with completed practice history, active practice HUD accessibility and tooltip descriptions (`Training_OperationProgressGroupAriaLabel`), responsive layout preservation across viewports, surface elevation design tokens, and exact-candidate `FULL_VALIDATION_PASS` (1,605 Core tests passed, 0 warnings/errors Windows & Android Release builds, 0 NuGet vulnerabilities, tree identity `6761a9eb217bcea11f0f5bc7e14cc594100efd95`, Schema V6 preserved without migration).
- **MF-LEARN-005 Adaptive Practice Balance and Foundational Coverage** (PR #46, merge `c7fea74554abe01181b7a0e3d3c4e554c48f7d1a`, candidate `dd4b1b48f67cbb333f913eb2ec6aa37e895a8ebf`): Protected requested-New foundational material acquisition from remediation preemption, preserved remediation authority on non-New roles, enforced same-operation diversity inside selected semantic pools, validated sustained-failure and long-run simulations, preserved Schema V6 without migration, and exact-candidate `FULL_VALIDATION_PASS` (1,604 Core tests passed, 0 warnings/errors Windows & Android Release builds, 0 NuGet vulnerabilities, tree identity `385ae4edcc6acb1a6817294b72e068c51b5a36de`).
- **MF-DOC-007 Post-MF-LEARN-004 Documentation Reconciliation** (PR #45, merge `ef03dc09464ef169228e9bc1e00bba5a22e4a387`): Reconciled repository baseline documentation following PR #44 merge.
- **MF-LEARN-004 Guided Four-Operation Number-Space Gate** (PR #44, merge `8f4ae59110abf6ea9d365733297a0c15d4c296ea`, candidate `51a2bd9907ebdcf738a348bc90de29d31d6b68b6`): Delivered Addition-governed multiplicative number-space gating in Guided Mode (active iff all four operations are enabled) ([ADR-0009](decisions/ADR-0009-guided-four-operation-number-space-gate.md)), unrestricted Custom Mode subsets, presentation eligibility decoupling (`PERSISTED != CURRENTLY PRESENTABLE`), lossless Schema V6 preservation, candidate window anti-poisoning in SQLite streaming (`ReadCandidateRowsAsync`), semantic `GateIdentity` evidence caching, zero-mutation Settings/current-fact reconciliation, and exact-candidate `FULL_VALIDATION_PASS` (1,590 Core tests passed, 0 warnings/errors Windows & Android Release builds, 0 NuGet vulnerabilities, tree identity `053311fed6a6827af690a6138fd83cc2c63bb7de`).
- **MF-DOC-006 Post-MF-STAB-003 Documentation Reconciliation** (PR #43, merge `a9d232f78e2f9ebcbc431bd18109a0aeb08a9303`): Reconciled repository baseline documentation following PR #42 merge.
- **MF-STAB-003 Enabled-Subset Scheduling and Current-Fact Reconciliation** (PR #42, merge `caffe0e883f83249bee2c9a1f2122543e88c9ab0`, candidate `766d8ea7692d139425e2301121f93af7901cf238`): Established independent per-operation role ordinals ([ADR-0008](decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)), durable Schema V6 count reconstruction, deterministic zero-mutation Settings/current-fact reconciliation, and exact-candidate `FULL_VALIDATION_PASS` (1,516 Core tests passed, 0 warnings/errors Windows & Android Release builds, 0 NuGet vulnerabilities, tree identity `fcab56b3a886ed0c5018d4f8a16304ee83378b26`).
- **MF-DOC-005 Post-MF-UX-006 Documentation Reconciliation** (PR #41, merge `284d7cf2c50be6e2d4f219c00aa20d92387338f9`): Reconciled repository baseline documentation following PR #40 merge.
- **MF-UX-006 V1 Privacy, Copy, and Localization Hardening** (PR #40, merge `ae69f4ae27397fc6edf36a23bb671b0410680be1`, candidate `606158a233d7cface85fa0ef7bd03c2f9ef4f4cb`): Delivered PC numpad reset copy alignment, Russian localization formatting and progression terminology polish, offline in-app `/privacy` surface and Settings entry with system Back integration, language-neutral and multilingual static fatal host fallback in `index.html`, and exact-candidate `FULL_VALIDATION_PASS` (1476 Core tests passed, 0 warnings/errors Windows & Android Release builds, 0 NuGet vulnerabilities, tree identity).
- **MF-UX-005 Final Documentation Reconciliation** (PR #39, merge `60dba236aa38bca138ab583f610c9ff876994b04`): Established the post-MF-UX-005 synchronized documentation baseline on `main`.
- **Predecessors**: Slices and packages prior to PR #39 are documented in [docs/PROJECT_STATE.md](PROJECT_STATE.md).

### Downstream Roadmap Stages
- **Release Verification & Distribution**: Release preparation (final V1 gap audit, fresh Tester APK build, manual physical-device validation on Samsung Galaxy S26 Ultra, production packaging `versionCode >= 4`, Steps 30–32) remains deferred until explicitly authorized.

This historical reference baseline is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
