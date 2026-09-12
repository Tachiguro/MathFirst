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
`main` / `origin/main`: `e1a0ae5a4a557f434f29ae04b7f8bcd32f3d2d88` (MF-REL-001 merged via PR #17)
Worktrees: Exactly one normal worktree

### Active Feature Candidate
- Active Task Branch: `feat/mf-set-001-practice-configuration`
- Package: `MF-SET-001` — **Practice Configuration: Operation Selection and Adjustable Practice Time**
- Final Implementation HEAD: `f453501412b7c9fce39356a0837a5b862d6221fd`
- Final Implementation Parent: `694b2e6b677b0f327f79f58ce5966871cd2b69ce`
- Current Lifecycle: `DOCUMENT_ONLY`
- Final Review: `REVIEW_PASS` (Implementation Readiness: YES)
- Final Core Baseline: 1037 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`)
- Release Builds: Windows 0 warnings / 0 errors; Android 0 warnings / 0 errors
- Remote State: Feature branch unpushed; no MF-SET-001 Pull Request; not merged
- Android Validation Artifact: `C:\Dev\MathFirstArtifacts\MF-SET-001\MathFirst-MF-SET-001-f453501-debug.apk` (SHA-256 `97f85e448d078e46d813aa1244e49b95933e51e1c9d64732faf3a614ae31db12`), provenance `f453501412b7c9fce39356a0837a5b862d6221fd`. Successfully validated on physical Android device with zero remaining defects.

### Commit Chain (MF-SET-001)
- Base: `e1a0ae5a4a557f434f29ae04b7f8bcd32f3d2d88`
- Forward-Only Commits (Local):
  - `e18dae5`: `fix(practice): stabilize multi-digit and next-fact input` — resolves editable multi-digit input and empty next-fact buffer.
  - `dc402c3`: `feat(settings): add practice operation and time controls` — adds domain models, preferences, UI controls for operation enablement and practice-time options.
  - `8772d56`: `fix(settings): persist single-operation practice selection` — hardens preference saving against single/empty operation edge cases.
  - `9cd173c`: `fix(persistence): support configurable operation scheduling` — removes obsolete fixed-four modulo-4 validation from `SqliteLearnerStore`, establishing schedule-agnostic persistence.
  - `98a6468`: `fix(practice): refresh selection evidence after configuration changes` — scopes `PracticeSelectionEvidence` to operation/position and rejects stale cached evidence.
  - `3b5caac`: `fix(practice): prevent addition-only persistence stalls` — remediates enabled-subset persistence and evidence-loading failures.
  - `4e18eee`: `fix(practice): preserve deterministic scheduling on evidence refresh` — keeps the selector authoritative, makes disabled-operation prefetch best effort, and adds exactly-once post-write recovery.
  - `a658d95`: `feat(practice): finalize practice visibility and timer lifecycle` — filters the practice HUD to enabled operations, removes the Settings Statistics / Diagnostics section, and finalizes process-local active timer semantics.
  - `694b2e6`: `feat(onboarding): add operation choice and progress feedback` — adds operation selection to fresh-install onboarding, returning learner progress presentation, and session check-in 20-attempt summaries.
  - `f453501`: `fix(practice): clear answer input for every new fact` — introduces process-local `FactInstanceRevision` and `CurrentAnswerInput` on `TrainingSession` with input re-keying, resolving the Android input retention defect on new exercises after session break. Final HEAD `f453501412b7c9fce39356a0837a5b862d6221fd`.

### Implemented Architecture Summary (MF-SET-001)
1. **Enabled Operations & Onboarding Choice**: Any non-empty subset of Addition, Subtraction, Multiplication, and Division may be selected in Settings or during fresh-install Onboarding (all four enabled by default, using shared preferences). Enabled operations filter newly generated practice and the progress HUD; disabled operations retain all learning progress for later reactivation.
2. **Returning Learner Progress & Session Check-In Summary**: Returning learners see a compact progress overview of enabled operations before starting practice without invented mastery percentages. Every 20 attempts, a check-in summary presents completed attempts, correct count, median correct latency, and actual Stage changes across process-local segments.
3. **Current-Fact and HUD Semantics**: Settings changes preserve the displayed question. On return, the HUD immediately shows the enabled set; the next generated question follows the updated configuration.
4. **Scheduling and Evidence**: `AdaptivePracticeSelector` alone schedules via $(P - 1) \bmod k$ and ordinal $\lfloor (P - 1) / k \rfloor + 1$. `PracticeSelectionEvidence` is operation/prospective-position scoped; cache state never changes the scheduled operation; required evidence is loaded on demand without wrong-operation fallback.
5. **Persistence and Recovery**: `SqliteLearnerStore` remains schedule-policy agnostic and atomically validates submitted state transitions. Preferences remain outside learner SQLite; no schema change was required. Post-write evidence recovery never duplicates a durable submission.
6. **Practice Time and Runtime Timer**: Standard, 30 s, 45 s, and 60 s preserve active-only raw latency semantics. Pause, Settings, and same-process background time are excluded; a true cold restart resets only the in-flight timer, not preferences or learner progress.
7. **Settings Cleanup and Localization**: The developer Statistics / Diagnostics section is removed without deleting learner data. The enabled-only operation HUD remains. 20 unique Diagnostics keys (60 locale entries) were removed; `Diagnostics_Group_Learning` remains for the HUD accessible label.
8. **Answer Entry & Input Lifecycle Invariants**: Unsubmitted partial input is preserved on the same exercise across pause, settings, and background transitions. Every new exercise instance always starts with an empty input buffer. Partial multi-digit input remains editable with Backspace before complete submission; single-digit correct answers auto-submit. Reset Learning Progress preserves operation/time preferences; Restore Default Settings preserves learning; Full Local Reset performs both resets.

### Immediate Next Lifecycle Steps
- Complete `DOCUMENT_ONLY` reconciliation (uncommitted documentation changes locally).
- Next after successful documentation: `COMMIT_ONLY`.

This snapshot is operational evidence only. Live local Git and GitHub state always override it; a new session must re-verify every fact before acting.
