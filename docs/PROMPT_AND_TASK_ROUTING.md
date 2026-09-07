# Operation Modes, Prompt Anatomy, and Task Routing

This document defines the authoritative rules for agent Operation Modes, prompt structure, orchestration delegation, model selection, and prompt formatting for MathFirst.

---

## 1. Operation Modes

Every coding agent prompt must specify exactly one primary **Operation Mode**. Each mode has a strictly bounded responsibility and ends with a hard stop. Agents must never silently cascade or transition across mode boundaries.

| Operation Mode | Responsibility | Permitted Actions | Prohibited Actions |
|---|---|---|---|
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
| `PUSH_ONLY` | Pushing local branch/commit to remote origin. | `git push -u origin <branch>`. | Force-pushing, merging, deleting branches. |
| `PR_ONLY` | Opening a GitHub Pull Request using PR template. | `gh pr create` with filled template. | Merging PR, enabling auto-merge. |
| `FULL_VALIDATION` | Exact-candidate pre-push comprehensive verification. | Execute full verification suite on exact candidate HEAD against verified base. | Auto-repairing failures, modifying repository, pushing. |
| `POST_MERGE_SYNC_ONLY` | Synchronizing local default branch after manual user PR merge. | `git checkout main`, `git pull --ff-only origin main`. | Modifying source code, committing to main, deleting branches without explicit user authorization. |

> [!IMPORTANT]
> In `POST_MERGE_SYNC_ONLY`, synchronization must be **fast-forward only** (`git pull --ff-only origin main`). If fast-forward is not possible, the agent must **STOP** and report divergence immediately without attempting merge, rebase, reset, or auto-repair. Branch deletion is never implied and requires explicit affirmative user approval.

---

## 2. Standing Orchestration Delegation

Routine lifecycle steps within an approved package may be prepared and dispatched sequentially without requiring user confirmation for every mechanical step, provided:
1. A concrete approved package plan exists.
2. The package scope is unambiguous.
3. Pre-conditions and previous lifecycle steps are verified successful.
4. No material product, architecture, security, or data integrity decisions remain open.

Each lifecycle phase must still run in its own isolated agent task with its distinct Operation Mode and prompt.

---

## 3. Non-Delegable Operations Matrix

The following operations always require **explicit affirmative user authorization** before execution:

- Merging a Pull Request
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

---

## 4. Product & Architecture Governance

- **Product Decisions**: Product behavior, requirements, and user workflows must be defined and approved before coding. Coding agents must not silently make material product decisions.
- **Architecture Choices**: When serious architecture alternatives exist, coding agents must not silently choose an approach. Material architectural choices require explicit user approval and, when appropriate, an Architecture Decision Record (ADR).

---

## 5. Model & Agent Selection Principles

- **Right-Sizing**: Always choose the smallest, most efficient model that can reliably accomplish the task.
- **Correctness First**: Accuracy and compliance with repository invariants take precedence over speed or resource consumption.
- **Durable Neutrality**: Transient runtime properties (e.g. temporary API quotas, beta models, momentary provider outages) are operational details and must not be documented as permanent repository rules.

---

## 6. Prompt Formatting & Anatomy Standards

Every generated coding-agent prompt must adhere to this exact structure:

1. **User-Facing Introduction**: Written in German.
2. **Model Recommendation Table**: Exactly one Markdown table specifying the recommended model, role, and effort level.
3. **Prompt Code Block**: Exactly one copyable code block starting with `PROMPT START` and ending with `PROMPT ENDE`. Nothing must follow the code block.

### Standard Prompt Anatomy
Inside the prompt code block:
- **Header**: Project (`MathFirst`), Repository path (`C:\Dev\MathFirst`), `Operation Mode`.
- **Task & Objective**: High-level goal and specific deliverables.
- **Repository Truth**: Mandatory live verification clause.
- **Preconditions**: Explicit checks on branch, HEAD, working tree, and remotes.
- **Scope**: Explicit list of authorized actions and target files.
- **Non-Goals**: Explicit list of unauthorized actions.
- **Safety**: Hard constraints on forbidden Git commands and staging rules.
- **Verification**: Exact checks to run before concluding.
- **Reporting**: Specified technical report format (in English).
- **Hard Stop**: Explicit instruction terminating the agent invocation.
