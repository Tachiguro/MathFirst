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
3. **Explicitly authorized next package**: If the user or active orchestration supplies a named package/task and lifecycle step (e.g. P0 PLAN_ONLY), first verify live repository state, then proceed only with that authorized task under repository governance.

Agents must NOT autonomously begin P0 implementation, Roadmap Step 55, or any other downstream activity merely because it is listed as planned; explicit user/orchestrator dispatch is required.
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

### Historical Reference Delivery Baseline (Snapshot as of 2026-10-07)
- **Historical Testing Verification (Roadmap Steps 51–54)**:
  - Step 51: Final V1 Gap Audit passed (`STEP_51_READY_FOR_STEP_52`).
  - Step 52: Fresh Tester APK packaged and validated offline (`STEP_52_TESTER_APK_PASS`, APK SHA-256 `40e2b5e3f23e90a2dbe695db4d724e4375c5f2ef51709a801fa81cf6e06f4039`).
  - Step 53: Installed on physical Samsung Galaxy S26 Ultra (`STEP_53_INSTALL_PASS`).
  - Step 54: Manual physical-device tester validation completed (`STEP_54_MANUAL_VALIDATION_PASS`).
- **Roadmap Step 55 Status**: **NOT EXECUTED / NOT AUTHORIZED** (explicitly declined by user after Step 54).
- **Pre-Step55 V1 Refinement Program**: Formalized in [docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md) covering P0 through P8:
  - P0 (Zero-Answer / `0 + 0` Core-Flow Freeze): Delivered & Merged (PR #60).
  - P1 (Normal Practice Without Deadline Failure): Delivered & Merged (PR #61).
  - P1b (Active Thinking Time / Interruption Safety): Delivered & Merged (PR #62, `a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`).
  - P2 (Direct-to-Practice Start / Remove Onboarding): Delivered & Merged (PR #63 at `1b485091755294221b6f242e174d99c168fc8e9d`, validated candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`).
  - P2b (Gameplay and Startup Refinements): Delivered & Merged (PR #64 at `f580a7154a4043a5097ffd852b5cf454be2cc397`, candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`).
  - P3 (Cumulative Operation Unlock Progression): Delivered & Merged (PR #65 at `759389650778f5d7b6a334b15556c5f31f6de5d0`, post-merge tests: 2,196 passed, Schema V9 live, ADR-0012 authoritative).
  - P4 (Settings Simplification): Delivered & Merged (PR #66 at `8400151ff080caecf024a418a9b6b8ada4873c2d`, validated candidate `edcc150039f369b8f809982499a5e1b2714e064c`, `FULL_VALIDATION_PASS`, 2,204 Core tests passed, Schema V9 preserved).
  - P5 (Cyber Defense Visual Consistency): Delivered & Merged (PR #67 at `aeb7bc46e8b425d9da95493a367f99f7ed330871`, validated candidate `80f08e4ad2eb33c5e884e869766bb765bbf1277a`, `FULL_VALIDATION_PASS`, 2,222 Core tests passed, 18 contract tests across 3 suites).
  - P6 (Tester Diagnostics / Telemetry Release Boundary): Delivered & Merged (PR #68 at `049ec1d5d3859a139f8d5493d6dae7607d321b02`, validated candidate `bceede18dd5bc2007f4bdc211f721979a50f2c35`, `P6_FULL_VALIDATION_PASSED`, 117 permanent focused P6 tests across 11 suites, 2,228 Core Debug and Release tests in formal validation; post-P6 merge state reconciled via PR #69 at `4ba870ad0bd6c516e74d2000a8f5c0878fb609a5`).
  - P8: MF-AUDIT-002 (Test-Coverage Audit & Targeted Hardening — implementation complete across 3 slices at `3558f8cee7b3aad031459990276ff99d73312379`, review verdict `MF_AUDIT_002_REVIEW_APPROVED`, documentation reconciled; fresh sessions must inspect live Git/GitHub to determine downstream integration status).
  - P7: Deferred / Post-Core.
  - Step 55: Unauthorized.
- **Delivered Pre-Step55 Packages & Merged PRs**:
  - PR #69 (`4ba870ad0bd6c516e74d2000a8f5c0878fb609a5`): `docs: reconcile post-P6 merge state`
  - PR #68 (`049ec1d5d3859a139f8d5493d6dae7607d321b02`): `P6: isolate tester diagnostics from production builds`
  - PR #67 (`aeb7bc46e8b425d9da95493a367f99f7ed330871`): `P5: cyber defense visual consistency`
  - PR #66 (`8400151ff080caecf024a418a9b6b8ada4873c2d`): `P4: simplify practice settings`
  - PR #65 (`759389650778f5d7b6a334b15556c5f31f6de5d0`): `P3: cumulative operation unlock progression`
  - PR #64 (`f580a7154a4043a5097ffd852b5cf454be2cc397`): `P2b: gameplay and startup refinements`
  - PR #63 (`1b485091755294221b6f242e174d99c168fc8e9d`): `P2: start fresh learners directly in practice`
  - PR #62 (`a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`): `P1b: active thinking time and interruption-safe learning evidence`
  - PR #61 (`4e3ca4943c5809cbe470a4b0ac4f192b24b66795`): `P1: normal practice without deadline failure`
  - PR #60 (`10c01c05fa9b50b5278c775d78a87ca9a7ef2060`): `P0: resolve zero-answer core-flow freeze`
  - PR #57 (`975fb134636f33fba4a54b399aedae80edf95235`): `docs: reconcile post-mf-telem-001 project state`
  - PR #56 (`bbdf62652927efa26475a9f2d83778de6465f5e1`): `MF-TELEM-001: Tester Telemetry Export and Share`
- **Preceding Historical Reference PRs**:
  - PR #55 (`76116d11b8563b0407188ba53ccefd998eda958d`): `MF-UX-008: stabilize Cyber Defense combat layout and opponent presentation`
  - PR #54 (`bc7471b098e2f79262ff6e71302820bd281a14d5`): `MF-LEARN-006: adaptive learning policy, 482 benchmark, and durable calibration`
  - PR #53 (`50aed4714937777f98058cc64afb75a75d749709`): `docs: reconcile post-pr51 pr52 project state`
  - PR #52 (`f6a842b71df39b2d6facafae41874c86d6f3f611`): `docs: harden agent prompt governance`
  - PR #51 (`01472b05ef83f586144414a3cb3a0c7abbc45189`): `docs: record adaptive learning policy design`
  - PR #50 (`fa99f5c162f8cbce3d55ca7a3cac9d1625249a1b`): `feat: add Cyber Defense training MVP`
- Schema: V9 (delivered via P3; adds `curriculum_stage` to `learner_progression`)
- Telemetry: `telemetry_export_schema_v2` (16 properties including boolean `is_interrupted`)
- Build 2 status: `REJECTED` (`RELEASE_CANDIDATE_REJECTED_PENDING_REMEDIATION`).
- Build 3 status: Historical only. Step 30 technical smoke passed and Step 31 physical-device verification passed, but source predates MF-LEARN-004, MF-LEARN-005, MF-UX-007, MF-LEARN-006, MF-UX-008, MF-TELEM-001, P0, P1, P1b, P2, and P2b, and no longer represents current repository source.
- Future candidate status: Any future production candidate requires `versionCode >= 4`. Build 4 does **not** exist yet (not packaged, not signed, not tested).

### Session Discovery & Candidate Resolution Protocol
When initializing a new session:
1. **Inspect live Git and GitHub first**: Live local Git and GitHub repository state always takes precedence over documentation snapshots or remembered context. Execute the discovery sequence in Section 2 to discover live branch, HEAD SHA, working tree, tracking status, and open PR status.
2. **Verify live repository synchronization**: Fetch `origin` (`git fetch origin`), inspect live `origin/main` (`git rev-parse origin/main`), and verify that local `main` is synchronized with `origin/main` (`git status`, `git log origin/main..HEAD`, `git log HEAD..origin/main`). Never require equality to a documentation-embedded SHA; live synchronization is determined by Git tracking state.
3. **Recognize Historical Build 3 Status and Pending Candidate**: Any subsequent production candidate requires `versionCode >= 4`, repetition of Step 30 and Step 31 verification (agent-executable when authorized), and separate user Google Play Console upload and publishing. Step 55 remains unauthorized.
4. **Resolve active work from live state**: Check for open PRs (`gh pr list --state open`), divergent local task branches with unmerged work, uncommitted working tree modifications, and `docs/CURRENT_WORK.md`. Active work and lifecycle state are established solely through live evidence. When no PR is open, no task branch has unmerged work, and working tree on `main` is clean, there is no in-flight work.
5. **Await explicit dispatch**: When no active package is established by live evidence, do not autonomously select a downstream task (such as P8 implementation or Step 55). State that there is currently no active work item and await explicit dispatch.

### Historical Delivered Baseline Summary
- **Post-P6 Merge State Reconciliation** (PR #69, merge `4ba870ad0bd6c516e74d2000a8f5c0878fb609a5`): Reconciled baseline documentation following P6 integration.
- **P6 Tester Diagnostics / Telemetry Release Boundary** (PR #68, merge `049ec1d5d3859a139f8d5493d6dae7607d321b02`, candidate `bceede18dd5bc2007f4bdc211f721979a50f2c35`): Enforced compile/profile isolation for tester diagnostics, dedicated `TesterDiagnosticsSection` component, conditional DI registrations, ReleaseTool build metadata propagation, and profile-wide Full Local Reset cache cleanup (`P6_FULL_VALIDATION_PASSED`, 2,228 Core tests passing in Debug and Release, 117 permanent P6 contract/regression tests across 11 suites, merge tree `817ee7b250c5bed555f4c4bce8852dce5d8dbf68`).
- **P5 Cyber Defense Visual Consistency** (PR #67, merge `aeb7bc46e8b425d9da95493a367f99f7ed330871`, candidate `80f08e4ad2eb33c5e884e869766bb765bbf1277a`): Aligned Settings, Privacy, secondary dialogs, overlays, and Not Found with Option A Scoped Cyber Defense visual language while preserving Light/Dark/System theme fidelity, keyboard focus rings, reduced-motion suppression, and MF-UX-008 gameplay stability (`FULL_VALIDATION_PASS`, 2,222 Core tests passing, 18 permanent visual and accessibility contract tests across three suites).
- **P4 Settings Simplification** (PR #66, merge `8400151ff080caecf024a418a9b6b8ada4873c2d`, candidate `edcc150039f369b8f809982499a5e1b2714e064c`): Streamlined Settings by removing user-facing Practice Time selection (Standard, No Time Pressure, 30s, 45s, 60s) while retaining lower-level plumbing and read-only curriculum operation status (`FULL_VALIDATION_PASS`, 2,204 Core tests passing, Schema V9 preserved).
- **P3 Cumulative Operation Unlock Progression** (PR #65, merge `759389650778f5d7b6a334b15556c5f31f6de5d0`): Delivered four-stage cumulative progression ($+ \to + - \to + - \times \to + - \times \div$), monotonic `CurriculumStage` in Schema V9, tolerant prerequisite D01 frontier unlock predicates, aggregate broad weakness gating, conservative V8 $\to$ V9 migration, semantically Guided `CurriculumManaged` practice, and read-only Settings unlock status (`FULL_VALIDATION_PASS`, 2,196 Core tests passing).
- **P2b Gameplay and Startup Refinements** (PR #64, merge `f580a7154a4043a5097ffd852b5cf454be2cc397`, candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`): Implemented digit-scaled Cyber Defense critical hit timing ($T_{\text{crit}} = T_{\text{easy}} \times \text{DigitCount}$), shared radar/damage authority (`Session.CurrentFactCriticalHitThresholdMs`), and fresh startup `InitialReadyGate` orientation without active timing before explicit Start (`FULL_VALIDATION_PASS`, 2,027 Core tests passing).
- **P2 Direct-to-Practice Start / Remove Onboarding** (PR #63, merge `1b485091755294221b6f242e174d99c168fc8e9d`, candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd`): Delivered direct start in active practice for fresh learners, initial preference default to Addition only, returning-learner progress overview, and complete removal of obsolete onboarding components, routes, CSS, and preference APIs (2,014 Core tests passing).
- **P1b Active Thinking Time / Interruption Safety** (PR #62, merge `a7b579b4ef2ebdb5f1fe7e059b6bbfb33fb6a912`): Delivered SQLite Schema V8 (`is_interrupted` column), Telemetry Schema V2 (16 properties), Dual-Window Structured Band progression (Window A: 40 math attempts; Window B: 40 timing-eligible attempts), timing-evidence eligibility (`TimingEvidenceEligible = !IsInterrupted`), pace calibration gating, and active interaction latency tracking across interruptions (2,010 Core tests passing).
- **P1 Normal Practice Without Deadline Failure** (PR #61, merge `4e3ca4943c5809cbe470a4b0ac4f192b24b66795`): Removed automatic timeout failure from normal practice while preserving active interaction latency measurement, adaptive pace estimation, and FSRS rating semantics.
- **P0 Zero-Answer / `0 + 0` Core-Flow Freeze** (PR #60, merge `10c01c05fa9b50b5278c775d78a87ca9a7ef2060`): Resolved physical hardware freeze when entering 0 for `0 + 0` from fresh/reset state; reinforced state release across submit $\to$ commit $\to$ next fact pipeline.
- **Post-MF-TELEM-001 Documentation Reconciliation** (PR #57, merge `975fb134636f33fba4a54b399aedae80edf95235`): Reconciled repository baseline documentation following the merge of PR #56.
- **MF-TELEM-001 Tester Telemetry Export and Share** (PR #56, merge `bbdf62652927efa26475a9f2d83778de6465f5e1`): Delivered Schema V7 persistence with five nullable presentation-context columns, pseudonymous random installation UUID, canonical JSON export contract (`telemetry_export_schema_v1`), sandboxed native platform sharing (`telemetry-share`), Full Local Reset cleanup, localized Settings export and reset UI, and non-interference regression coverage (`REVIEW_PASS`, 1,906 Core tests passed, 0 warnings/errors Windows & Android builds).
- **MF-UX-008 Static Combat Layout and Boss Presentation** (PR #55, merge `76116d11b8563b0407188ba53ccefd998eda958d`, feature HEAD `43949fdc7513714d9e4cbb755d0c5c8da5ba4a8b`): Delivered static combat layout positional stability (keypad and arithmetic typography stationary across combat transitions), layout-isolated boss presentation (`clamp(90px, 16vh, 140px)`), visual layering (`z-index: 4` + scrim protection), progressive opponent scaling (0.65 to 1.50), tier-preserving feedback scale composition (`SCALE_PRESERVED_ACROSS_ALL_STATES`), and scoped active-gameplay scroll suppression (`.training-host.active-gameplay`) (`REVIEW_PASS`, `USER_PHYSICAL_DEVICE_ACCEPTANCE_PASS`, 1,786 Core tests passed, Schema V6 preserved).
- **MF-LEARN-006 Adaptive Learning Policy, 482 Benchmark, and Durable Calibration** (PR #54, merge `bc7471b098e2f79262ff6e71302820bd281a14d5`, candidate `576836db96d4d16e3be2d701c66c5adea3ecd1fb`): Delivered Option-B Evidence-Adaptive Discovery, absolute no-immediate-fact-repetition invariant ($\text{FactId}(t+1) \ne \text{FactId}(t)$), tiered remediation (4 / 2 cooldown spacing; broad weakness threshold 2), Guided Gate G3 soft decoupling at `BandIndex >= 3`, durable pace calibration at $\ge 24$ positioned Correct attempts, calibrated downstream Cyber Defense Critical Hits, exact 482 strong-learner benchmark, restart determinism, and exact-candidate `FULL_VALIDATION_PASS` (1,772 Core tests passed in Debug/Release, 0 warnings/errors Windows & Android Release builds, 0 NuGet vulnerabilities, Schema V6 preserved without migration).
- **Predecessors**: Slices and packages prior to PR #54 are documented in [docs/PROJECT_STATE.md](PROJECT_STATE.md).

### Downstream Roadmap Stages
- **Pre-Step55 V1 Refinement Program**: The active planned path follows workstreams P0 through P8 documented in [docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md).
- **Release Verification & Distribution**: Production packaging (Step 55, `versionCode >= 4`, `Distributable` profile), Step 56 smoke, Step 57 device verification, and Step 58 Google Play publication remain explicitly deferred and unauthorized until pre-Step55 refinement is complete and separate user authorization is granted.

This historical reference baseline is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
