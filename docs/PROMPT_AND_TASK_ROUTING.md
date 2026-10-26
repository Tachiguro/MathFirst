# Operation Modes, Prompt Anatomy, and Task Routing

This document owns MathFirst task scope, local modes and agent prompt requirements. Universal coordinator/model/usage policy has one owner-controlled external source; it is not duplicated in this public repository. Local safety remains self-contained in [AGENTS.md](../AGENTS.md). Verify publication, adoption and rollout status from live GitHub Issues/PRs and owner-installed rendered Project Instructions; uncommitted edits grant no authority. The recorded coordinator decision, reviewed-adoption prerequisites, candidate smoke testing and formal activation are distinguished in [workflow §10](AGENT_WORKFLOW.md#10-rule_conflict-resolutions-and-rollout-boundary). Use the isolated modes below until applicable central adoption prerequisites are verified.

---

## 1. Operation Modes

Every coding agent prompt must specify exactly one primary **Operation Mode**. Each mode has a strictly bounded responsibility and ends with a hard stop. Agents must never silently cascade or transition across mode boundaries.

**`WORK_PACKAGE` policy selected for reviewed adoption:** the [owner-delegated coordinator decision](https://github.com/Tachiguro/MathFirst/issues/86#issuecomment-6402430454) selects one agent pass for planning, implementation, required checks and documentation within an approved, issue-linked scope. Decision-free planning in that scope needs no repeat approval. Each Git action needs a separate explicit field: `COMMIT_AUTHORIZED`, `PUSH_AUTHORIZED`, `PR_AUTHORIZED` (`YES` or `NO`; absent means `NO`). All review, validation, Git safety and coordinator-only merge gates remain binding; the package ends before merge. Until reviewed adoption under workflow §10, use the isolated modes below. Narrow modes remain available afterward; `DOCUMENT_ONLY` tasks acquire no Git integration authority.

| Operation Mode | Responsibility | Permitted Actions | Prohibited Actions |
|---|---|---|---|
| `WORK_PACKAGE` (after reviewed adoption) | One bounded, approved, issue-linked package. | Plan, implement, required checks and docs; commit/push/PR only with each matching explicit authorization field. | Merge, auto-merge, implicit release/device/data access, scope expansion or bypassing safety/review/validation gates. |
| `CHECKPOINT_ONLY` | Strictly read-only state capture. | Identity, host, cached refs (labelled possibly stale), dirty paths, hashes, stash/worktree list. | Fetch/pull, file/index/ref/FETCH_HEAD writes, staging or cleanup. |
| `PLAN_ONLY` | Read-only analysis and implementation planning. | Inspect repository, analyze code, draft implementation plan. | Creating branches, modifying files, staging, committing, pushing. |
| `IMPLEMENT_SLICE` | Focused implementation of one small TDD slice. | Write focused RED test, implement minimal GREEN code, run slice tests, create checkpoint commit. | Modifying out-of-slice files, package-wide refactoring, pushing, PR creation. |
| `CHECKPOINT_COMMIT_ONLY` | Staging and committing a completed implementation slice. | Explicit `git add <paths>`, commit with `MathFirst-Checkpoint` trailer. | Pushing, merging, staging unrelated files, `git add .`. |
| `IMPLEMENT` | Full implementation for bounded packages where slicing is not required. | Author code/tests, run focused tests. | Staging, committing, pushing, PR creation without explicit mode transition. |
| `TEST_ONLY` | Read-only test execution and evidence collection. | Run test suites, gather output. | Modifying code, auto-repairing failures, committing, pushing. |
| `REVIEW_ONLY` | Read-only code, architecture, and safety review of full package diff. | Inspect diff, review contracts/tests/docs, report findings (BLOCKER/MAJOR/MINOR/NIT). | Modifying files, committing, pushing, approving PRs. |
| `DOCUMENT_ONLY` | Authoring or reconciling documentation. | Create/edit documentation files. | Staging, committing, pushing, writing app source code. |
| `COMMIT_ONLY` | Explicit staging and committing of reviewed package changes. | Explicit `git add <paths>`, create package commit. | Modifying files, pushing, `git add .`, history rewriting. |
| `BUILD_ONLY` | Compilation and build artifact generation. | Run build commands, verify raw build output. | Running test suites, packaging, deploying, signing, pushing. |
| `PACKAGE_ONLY` | Assembling distribution packages from built artifacts. | Run packaging tools, verify archive contents. | Uploading, signing without approval, deploying. |
| `RELEASE_PREP_ONLY` | Separately authorized local release preparation. | Only named build/package/validation steps; signing requires separate affirmative approval. | Store actions, deployment, implicit device access or signing. |
| `PUSH_ONLY` | Pushing local branch/commit to remote origin. | `git push -u origin <branch>`. | Force-pushing, merging, deleting branches. |
| `PR_ONLY` | Opening a GitHub Pull Request using PR template. | `gh pr create` with filled template. | Merging PR, enabling auto-merge. |
| `FULL_VALIDATION` | Exact-candidate pre-push comprehensive verification. | Execute full verification suite on exact candidate HEAD against verified base. | Auto-repairing failures, modifying repository, pushing. |
| `POST_MERGE_SYNC_ONLY` | Synchronizing local default branch after verified coordinator-gated or owner PR merge. | `git checkout main`, `git pull --ff-only origin main`. | Modifying source code, committing to main, deleting branches without explicit user authorization. |

> [!IMPORTANT]
> In `POST_MERGE_SYNC_ONLY`, synchronization must be **fast-forward only** (`git pull --ff-only origin main`). If fast-forward is not possible, the agent must **STOP** and report divergence immediately without attempting merge, rebase, reset, or auto-repair. Branch deletion is never implied and requires explicit affirmative user approval.

---

## 2. Standing Orchestration Delegation

Routine lifecycle steps within an approved package may be prepared and dispatched sequentially without requiring user confirmation for every mechanical step, provided:
1. A concrete approved package plan exists.
2. The package scope is unambiguous.
3. Pre-conditions and previous lifecycle steps are verified successful.
4. No material product, architecture, security, privacy, data, cost, device or release decisions remain open.

Until reviewed `WORK_PACKAGE` adoption under workflow §10, each lifecycle phase must run in its own isolated agent task with its distinct Operation Mode and prompt.

After reviewed adoption, `WORK_PACKAGE` permits combined routine phases within approved scope; material decisions still require `PLAN_DECISION_REQUIRED`. It never silently changes an existing task's mode or permissions.

### ChatGPT-first coordination and Issue maintenance (#84)

The ChatGPT coordinator reads verified GitHub issues, source, commits and PR diffs, performs bounded analysis/review and prepares the next step within approved scope. Delegate only work that needs local edits, the native host or real execution. GitHub access does not verify local dirty/unpushed work or native build, GUI or device results; mark unseen evidence `UNVERIFIED`. Local coding agents execute their dispatched scope and report; they do not select follow-up tasks or agents.

Within an already approved MathFirst package or roadmap plan, routine Issue administration belongs to the coordinator through the connected GitHub integration: deduplicate, create, annotate, reconcile, update acceptance/status and close after verified acceptance. Read back every mutation. Record exact PR/commit/check references, limitations and provenance. Do not accept brainstorming as an approved goal, close on an unverified report, or close #84/#86 before the reviewed documentation change is merged. Read-only triage changes nothing; local `DOCUMENT_ONLY` tasks authorize no GitHub mutations. No coding-agent `ISSUE_ONLY` mode is introduced.

Use targeted queries and meaningful milestone updates; avoid duplicate Issues, polling and permission requests per checkbox. New product goals/priorities, material architecture/security/data decisions, scope/cost increases and other non-delegable operations still require the owner. The gated coordinator merge exception below is preserved separately.

**MathFirst coordinator-only PR merge delegation:** The owner explicitly authorizes the coordinating ChatGPT to merge PRs within previously approved MathFirst scope **without per-PR consent**, provided the [AGENTS.md](../AGENTS.md) §6 fail-closed merge gate confirms current base/head, candidate validation, review/CI, branch protections, data/security boundaries and no open material blockers. Record verified merge SHA in the implementation Issue and dispatch `POST_MERGE_SYNC_ONLY` as a separate local agent task. A coding agent must never autonomously merge, and auto-merge remains separately prohibited. This standing delegation does not authorize releases, branch deletion, destructive Git, new scope, device automation or extra costs.

---

## 3. Non-Delegable Operations Matrix

The following operations always require **explicit affirmative user authorization** before execution; the owner has already supplied a **MathFirst-only standing authorization** for coordinator-gated routine PR merges that satisfy [AGENTS.md](../AGENTS.md) §6:

- Merging a Pull Request (local coding agents: forbidden; coordinating ChatGPT: only under the verified standing-authority gate)
- Enabling GitHub auto-merge
- Deleting local or remote branches
- Deleting worktrees
- Destructive Git operations (`reset`, `clean`, `stash`, `rebase`, `amend`, force push, history rewrite)
- Creating Git tags or GitHub releases
- Application deployment or publishing
- App store binary uploads or release signing
- Package creation outside explicit scope
- Executing emulator, simulator, physical device, or ADB operations
- Manual device testing
- Operations touching real private user data
- Launching parallel writing agents or subagents
- Material scope expansion beyond the approved package

**Stronger store boundary:** all Play Console or other store uploads, metadata, releases, tracks, rollouts and publication are `OWNER_ONLY` / `NEVER_AUTOMATE`. The owner performs them manually; affirmative approval is not agent authorization. No publishing API, CI job, service account, Fastlane or UI automation. Release signing still needs separate affirmative owner approval. Auto-merge is never part of coordinator standing delegation. See [release governance](BUILD_AND_RELEASE.md).

---

## 4. Product & Architecture Governance

- **Product Decisions**: Product behavior, requirements, and user workflows must be defined and approved before coding. Coding agents must not silently make material product decisions.
- **Architecture Choices**: When serious architecture alternatives exist, coding agents must not silently choose an approach. Material architectural choices require explicit user approval and, when appropriate, an Architecture Decision Record (ADR).

---

## 5. Coordinator Policy Boundary

Agent/model selection, availability, effort, usage and universal coordinator presentation are maintained only in the owner-controlled external governance source. No mandatory multi-agent comparison table or local model catalog applies here. Do not copy private policy text, links, model allowances or quota information into this public repository. Routine local tasks follow `AGENTS.md` and their complete prompt without loading the private contract; reconciliation requiring an unavailable verified source stops with `GOVERNANCE_SOURCE_UNVERIFIED`.

---

## 6. Prompt Formatting & Anatomy Standards

The local agent contract requires one complete, copyable English prompt with the actual task and project identity at its top. Owner-facing coordination is German. Universal presentation policy belongs to the external source (§5); no comparison table is required by MathFirst. Generic `PROMPT START` / `PROMPT ENDE` boilerplate remains forbidden.

### Visible Prompt Header Contract
Every MathFirst `PROJECT_TASK` or `REVIEW_TASK` coding-agent prompt MUST start visibly with the canonical header in this exact order. A separately dispatched `GOVERNANCE_TASK` uses the verified governance identity and checkout under the exception in §7, never the MathFirst header:

```text
TASK: <short human-recognizable current task>
PROJECT: MathFirst
REPOSITORY: Tachiguro/MathFirst
CANONICAL CHECKOUT: C:\Dev\MathFirst
OPERATION MODE: <mode>
TASK_CLASS: PROJECT_TASK | REVIEW_TASK
```

The `TASK` line must be immediately visible at the very top of the prompt code block without any preceding prose, markdown headers, or boilerplate, so that the user can identify the intended work at a glance on small displays.

### Standard Prompt Anatomy
Inside the prompt code block:
- **Header**: Canonical lines (`TASK`, `PROJECT`, `REPOSITORY`, `CANONICAL CHECKOUT`, `OPERATION MODE`).
- **Objective & Deliverables**: High-level goal and specific bounded deliverables.
- **Project & Task Identity Gate**: Mandatory fail-closed verification of project, repository, and task scope before execution; instructions for `PROJECT_MISMATCH` or `TASK_SCOPE_MISMATCH` hard stops.
- **Repository Truth**: Mandatory live verification clause overriding prompt text, chat history, or handoff documents.
- **Preconditions**: Explicit checks on branch, HEAD, working tree, and remotes.
- **Scope**: Explicit `SCOPE` and `AUTHORIZED` actions/target files; issue/PR reference, risk gate and acceptance criteria.
- **Git authorizations (`WORK_PACKAGE` after reviewed adoption)**: Separate `COMMIT_AUTHORIZED`, `PUSH_AUTHORIZED` and `PR_AUTHORIZED` fields; only explicit `YES` authorizes that individual action, subject to all gates.
- **Non-Goals**: Explicit `NOT_AUTHORIZED` actions. Mode names and broad wording such as "finish" grant no implicit Git or release authority.
- **Safety**: Hard constraints on forbidden Git commands and strict staging rules.
- **Verification**: Exact checks to run before concluding.
- **Reporting**: Specified technical report format (in English).
- **Hard Stop**: Explicit report-and-stop instruction, with `HARD STOP` on the final line. This stricter MathFirst marker stays required even where external formatting permits its omission.

Before acting, check every essential field and the complete report-and-stop instruction. Missing fields or visible truncation produce `PROMPT_INCOMPLETE`, zero mutations and a stop. Ordinary owner questions to ChatGPT do not need coding-agent task fields. A source link alone never loads its content or authorizes another repository.

---

## 7. Project & Task Identity Gate (Fail-Closed)

Every dispatched MathFirst agent must enforce the Project & Task Identity Gate. Once the reviewed identity rules take effect under workflow §10, the sole central governance exception is a separately authorized new task explicitly declaring `TASK_CLASS: GOVERNANCE_TASK` and `REPOSITORY: Tachiguro/project-governance`. Verify the governance checkout's repository, root, origin and single-writer state before action; execute only its authorized scope there, with zero MathFirst writes. A link, mention or pasted text grants no cross-repository access. MathFirst documentation authorization grants no `GOVERNANCE_TASK` authority. Without verified adoption of the exception, foreign inspection and action are prohibited.

1. **Verification Before Action**: Before answering the substantive task or making ANY repository mutation, the agent must verify:
   - `PROJECT` matches `MathFirst`.
   - `REPOSITORY` matches `Tachiguro/MathFirst`.
   - Canonical checkout resolves to `C:\Dev\MathFirst`.
   - Requested work belongs to MathFirst and conforms to the explicitly assigned `Operation Mode` and authorized scope.
2. **Fail-Closed on Foreign Project (`PROJECT_MISMATCH`)**: Except for the separately dispatched governance exception once effective, if the prompt or follow-up targets another project or repository:
   - Perform ZERO repository mutations.
   - Do NOT reinterpret the foreign request as MathFirst work.
   - Do NOT answer the foreign task substantively.
   - Do NOT execute commands for the foreign project.
   - Do NOT inspect or mutate another project or directory.
   - Do NOT silently switch projects.
   - Do NOT attempt to be helpful by performing adjacent work.
   - Output only the applicable `PROJECT_MISMATCH` response from [AGENTS.md](../AGENTS.md) §2, then stop immediately.
3. **Fail-Closed on Out-of-Scope Task (`TASK_SCOPE_MISMATCH`)**: If the task belongs to MathFirst but is outside the authorized package scope or Operation Mode:
   - Perform ZERO repository mutations.
   - Output only the standard `TASK_SCOPE_MISMATCH` report and HARD STOP.
4. **Follow-Up Protection**: Later conversation messages do NOT implicitly override project identity, repository identity, operation mode, or authorized scope. A deliberate project or scope switch requires an explicitly dispatched new task.
