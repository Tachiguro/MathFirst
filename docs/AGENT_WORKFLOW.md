# MathFirst Agent Workflow & Lifecycle

This document defines the authoritative development lifecycle, slicing protocols, TDD practices, review standards, and branch synchronization procedures for MathFirst.

Verify publication, adoption and rollout status from live GitHub Issues/PRs and owner-installed rendered Project Instructions. The owner-delegated coordinator decision is recorded in [§10](#10-rule_conflict-resolutions-and-rollout-boundary); independent Gate C review, authorized merge and owner-installed rendered Project Instructions are prerequisites under the central contract. Candidate loader/smoke evaluation precedes acceptance and formal V4-active designation. Use the isolated modes below until applicable central adoption prerequisites are verified. Uncommitted edits grant no authority or activation.

---

## 1. Standard Package Lifecycles

All work in MathFirst is organized into small, bounded packages identified by a canonical `MF-<CATEGORY>-<NUMBER>` identifier (e.g. `MF-GOV-001`).

**`WORK_PACKAGE` policy selected for reviewed adoption:** one agent executes an approved, issue-linked scope through preflight → plan → implementation → focused and required checks → documentation. Decision-free plans inside that scope need no repeat approval; new material product, architecture, security, privacy, data, cost, device, release or scope decisions require `PLAN_DECISION_REQUIRED`. Explicit-path commit, task-branch push and PR creation each need their own `COMMIT_AUTHORIZED`, `PUSH_AUTHORIZED` or `PR_AUTHORIZED` field set to `YES` (absent means `NO`). Review and evidence boundaries stay distinct, with all Git safety, required validation and coordinator-only merge gates preserved. Adoption remains subject to §10; narrow modes continue to bind restricted tasks such as `DOCUMENT_ONLY`. No coding agent merges, enables auto-merge, starts unauthorized release/device work or dispatches follow-up agents.

The isolated lifecycles below bind until reviewed adoption under §10 and remain available afterward for restricted tasks.

### A. Normal Code & Behavioral Changes
```
[PLAN_ONLY]
    └── [IMPLEMENT_SLICE] (repeated for each slice)
            └── [REVIEW_ONLY] (consolidated package review)
                    └── [DOCUMENT_ONLY] (documentation reconciliation gate)
                            └── [COMMIT_ONLY] (commit doc changes if needed)
                                    └── [FULL_VALIDATION] (exact candidate HEAD)
                                            └── [PUSH_ONLY]
                                                    └── [PR_ONLY]
                                                            └── [Coordinator-Gated / Owner Merge]
                                                                    └── [POST_MERGE_SYNC_ONLY]
```

### B. Documentation-Only Packages
```
[PLAN_ONLY]
    └── [DOCUMENT_ONLY]
            └── [REVIEW_ONLY]
                    └── [COMMIT_ONLY]
                            └── [FULL_VALIDATION]
                                    └── [PUSH_ONLY]
                                            └── [PR_ONLY]
                                                    └── [Coordinator-Gated / Owner Merge]
                                                            └── [POST_MERGE_SYNC_ONLY]
```

---

## 2. Slicing & Test-Driven Development (TDD)

For all behavioral changes:
1. **Genuine RED -> GREEN Evidence**:
   - **RED**: Write a focused test demonstrating that the desired behavior is absent or failing. Compilation errors, broken fixtures, path errors, or environment issues do NOT qualify as valid RED.
   - **GREEN**: Implement minimal production code to satisfy the test, then re-run the exact same focused test suite to prove success.
2. **`IMPLEMENT_SLICE` Composite**:
   - Focuses strictly on a single slice.
   - Contains: focused RED -> minimal implementation -> focused GREEN -> targeted verification -> checkpoint commit.

---

## 3. Checkpoint Commits

Checkpoint commits record durable progress after completing an implementation slice:
- They represent focused GREEN evidence for that slice only.
- They do **not** imply package completion, full review, or readiness to merge.
- Commit messages must include the standard trailer:
  ```text
  MathFirst-Checkpoint: MF-<ID> <slice>/<total_slices> <short-description>
  ```

---

## 4. Consolidated Review (`REVIEW_ONLY`)

Before candidate validation, a comprehensive read-only review of the entire package diff must be conducted. The review inspects:
- Scope adherence and non-goals.
- Product contract and API compatibility.
- Architecture and design consistency.
- Data integrity, persistence, and migrations.
- Error handling and edge cases.
- Privacy and security boundaries.
- Absence of regressions and test adequacy.
- Documentation currency.

### Finding Severity Levels
- **BLOCKER**: Critical defect, invariant violation, or severe data/security issue. Must be resolved before proceeding.
- **MAJOR**: Significant gap or design issue requiring remediation.
- **MINOR**: Small improvement or non-blocking cleanup.
- **NIT**: Cosmetic formatting or trivial wording suggestion.

---

## 5. Documentation Reconciliation Gate

Every completed package must pass a documentation reconciliation check in `DOCUMENT_ONLY` mode, or within its approved `WORK_PACKAGE` after reviewed adoption:
- **`DOCUMENTATION_RECONCILED_WITH_CHANGES`**: Documentation updated (e.g. `CHANGELOG.md`, `docs/PROJECT_STATE.md`, `docs/CURRENT_WORK.md`). Requires a subsequent `COMMIT_ONLY`, or explicit commit authorization within an adopted `WORK_PACKAGE`.
- **`DOCUMENTATION_CURRENT_NO_CHANGES`**: Documentation is already completely accurate; no changes or commits created.

---

## 6. Exact-Candidate Full Validation (`FULL_VALIDATION`)

- Runs against the **exact final candidate HEAD commit**.
- Any modification to the repository after validation makes prior validation immediately stale.
- **Fail-Closed**: Validation tools must not auto-repair code; failures must be reported as-is.
- Mandatory before push, PR and merge, including documentation-only packages. Gate A does not replace that requirement unless its reviewed adoption satisfies §10. A local documentation draft can stop after scoped checks; it is not a validated integration candidate.

### Native command map and evidence

Use Local Windows / PowerShell in `C:\Dev\MathFirst`, the project's .NET 10 toolchain and the verified exact candidate/base. Existing [TESTING.md](TESTING.md) contracts remain binding; this map records current commands without running or relaxing them:

| Check | Command |
|---|---|
| Core Debug | `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj -c Debug` |
| Core Release | `dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj -c Release` |
| Windows Release | `dotnet build src/MathFirst.App/MathFirst.App.csproj -c Release -f net10.0-windows10.0.19041.0` |
| Android Release compile | `dotnet build src/MathFirst.App/MathFirst.App.csproj -c Release -f net10.0-android36.0 -t:Compile` |
| Dependency security audit | `dotnet list MathFirst.slnx package --vulnerable --include-transitive` |
| Markdown references | Read-only relative-file/anchor audit against the candidate docs |
| Whitespace | `git diff --check <verified-base>...HEAD`; for an uncommitted draft also `git diff --check` |
| Candidate hygiene | Verify branch, HEAD, index/worktree including untracked files and exact scope |

Retain package-specific focused/regression, coverage, zero-warning and security gates wherever required. Native compile evidence does not prove Android runtime/device behavior. Tests use synthetic fixtures, fake clocks, mocked networks and isolated temporary stores; never real learner data. Report command/check, `PASSED` / `FAILED` / `SKIPPED` / `NOT_RUN`, exit code (or N/A), candidate SHA and limitations. Checks on dirty documentation at a base SHA are draft evidence only.

### Documentation Gate A after reviewed adoption

The [owner-delegated coordinator decision](https://github.com/Tachiguro/MathFirst/issues/86#issuecomment-6402430454) selects focused Gate A for genuinely non-critical documentation after reviewed project-policy adoption: exact scope/source-diff review, `git diff --check`, Markdown relative-file/anchor and formatting checks, protection/invariant/permission checks and coordinator source-level diff review. Changes to the meaning of governance authority, identity, permissions, privacy, security, data profile, validation policy or release boundaries are excluded regardless of file extension: they remain Gate C with independent review outside the implementing session and full project gates on the exact candidate. Product changes retain Gate B or C as applicable and all mandatory regression/native build/test gates.

**Gate A requires reviewed adoption under §10.** Verify independent Gate C review, authorized merge and applicable central adoption prerequisites from live evidence. The #86/#84 governance migration remains Gate C and requires the existing mandatory exact-candidate `FULL_VALIDATION` before push, PR and merge. No retroactive waiver is permitted; unrun checks are `NOT_RUN`, never `PASSED`. Documentation checks prove no native build, test or full-validation result.

---

## 7. Push, Pull Request, and Coordinator-Gated Merge

1. **`PUSH_ONLY`**: Push the validated task branch to `origin` using `git push -u origin <branch>`.
2. **`PR_ONLY`**: Open a GitHub PR targeting `main` using the standard [.github/pull_request_template.md](../.github/pull_request_template.md).
3. **Coordinator-gated MathFirst merge:** The coordinating ChatGPT may merge an ordinary PR within previously approved MathFirst scope under the owner's project-specific standing approval, **only after** the fail-closed gate in [AGENTS.md](../AGENTS.md) §6 passes (exact candidate, reviews/tests/CI, current head/base, protection, privacy/security and unresolved findings). No auto-merge, agent-controlled merge, or scope expansion is implied.
4. **Owner merge remains possible:** The repository owner may instead review and merge manually. Local coding agents must not merge PRs or enable auto-merge.

---

## 8. Post-Merge Synchronization (`POST_MERGE_SYNC_ONLY`)

After an independently verified merge by the coordinator or repository owner:
1. Switch to `main`: `git checkout main`
2. Fast-forward only synchronization: `git pull --ff-only origin main`
   - *Fail-Closed Guard*: If fast-forward is not possible, the agent must **STOP** and report divergence. Never merge, rebase, reset, or auto-repair.
3. Local branch deletion: Deleting the merged local task branch (`git branch -d <branch>`) is a **non-delegable operation** requiring explicit user authorization.
4. Confirm clean local status and synchronization with `origin/main`.

---

## 9. Interruption and Resume Protocol

If an agent session is interrupted or resumed:
1. Inspect the local task branch and commit log for `MathFirst-Checkpoint` trailers.
2. Read [docs/CURRENT_WORK.md](CURRENT_WORK.md) for recorded context.
   Reconcile that context with the verified implementation Issue/PR; historical snapshots cannot override live state or choose unrelated work.
3. Compare working tree against the latest checkpoint commit.
4. If the working tree is dirty or state is ambiguous, execute `REVIEW_ONLY` to establish the verified baseline before resuming implementation.

## 10. RULE_CONFLICT Resolutions and Rollout Boundary

These resolutions for [#86](https://github.com/Tachiguro/MathFirst/issues/86) and [#84](https://github.com/Tachiguro/MathFirst/issues/84) incorporate the [owner-delegated coordinator policy decision of 2026-10-25](https://github.com/Tachiguro/MathFirst/issues/86#issuecomment-6402430454). `WORK_PACKAGE` and focused documentation Gate A are selected for reviewed adoption; verify independent Gate C review, authorized merge and central-contract adoption prerequisites from live GitHub Issues/PRs and owner-installed rendered Project Instructions. Uncommitted edits grant no authority or activation.

| RULE_CONFLICT | Resolution / remaining gate |
|---|---|
| Mandatory dual-agent recommendation and local universal policy vs one external coordinator-policy source | Remove the table duty and local model policy; retain self-contained MathFirst safety and local prompt fields. Copy no private policy text, catalogs or quotas. |
| Isolated lifecycle and every non-trivial plan approved vs coherent `WORK_PACKAGE` and no repeat approval inside approved scope | Recorded decision: adopt bounded, approved, issue-linked packages after reviewed adoption; one agent may plan, implement, check and update docs. Each commit/push/PR action requires its own explicit authorization field. Material decisions still escalate; narrow modes remain binding for restricted tasks. `DOCUMENT_ONLY` tasks acquire no prospective `WORK_PACKAGE` authority. |
| Routine Issue maintenance vs approval for every mechanical checkbox | Coordinator handles evidence-backed upkeep inside approved scope, reads back mutations and closes only after verified acceptance. Read-only and local `DOCUMENT_ONLY` tasks mutate no Issues. No new product/data/release authority. |
| Documentation Gate A vs mandatory exact-candidate `FULL_VALIDATION` | Recorded decision: adopt focused Gate A after reviewed policy adoption only for genuinely non-critical documentation. Semantic authority/identity/permission/privacy/security/data-profile/validation/release changes remain Gate C. The #86/#84 migration retains independent Gate C review and mandatory exact-candidate full validation before push/PR/merge, without retroactive waiver. |
| Older #84/#86 manual-merge wording vs current coordinator-only delegation | Preserve the `AGENTS.md` §6 gate: coordinator may merge approved work only after exact-head review/validation/CI/protection checks; coding agents never merge. Require atomic expected-head guard, readback and Issue evidence, then separate fast-forward sync. `DOCUMENT_ONLY` tasks never merge. |
| Separate approval for store uploads vs owner-only store execution | Strengthen every active release rule to `OWNER_ONLY` / `NEVER_AUTOMATE`; signing retains separate affirmative approval and external-secret isolation. |
| External optional end marker vs MathFirst prompt contract | Retain first-line `TASK:` and last-line `HARD STOP`; check essential fields as well. |
| Local status/backlog snapshots vs Issue-first truth | Issues own accepted work/status; live Git/GitHub own source and integration facts, actual worktree owns unpublished edits. Preserve stable IDs and historical evidence; reconcile gradually without deleting open work. |
| Offline assumptions vs actual data flows | Keep `UNCLASSIFIED`; future online features undecided. Offline operation does not establish absence of personal data. Owner classification precedes data-flow changes. |

Document publication, policy adoption and formal V4-active acceptance are distinct. Verify their actual status from live GitHub Issues/PRs and owner-installed rendered Project Instructions; uncommitted edits to AGENTS.md grant no authority. An authorized, independently reviewed and merged rule update and owner-installed rendered Project Instructions are prerequisites for applying candidate instructions under the central contract. Central governance determines adoption prerequisites and rollout order. The BINGO pilot is required for formal MathFirst rollout/activation; never declare it passed without verified evidence.

At the prescribed rollout stage, loader checks and prospective smoke tests exercise the installed candidate instructions. Their purpose is to obtain acceptance: prior smoke acceptance is not a prerequisite for testing those candidate rules. This bounded rollout evaluation is distinct from ordinary task authority; ordinary tasks use the isolated modes until applicable central adoption prerequisites are verified. `DOCUMENT_ONLY` tasks grant no loader/smoke execution or permission expansion. Published repository rules must define an unambiguous applicable mode and mismatch response; do not refer agents to undefined older rules.

Formal **V4-active** designation requires the verified BINGO pilot, reviewed/merged project rules, owner-installed rendered Project Instructions, passing loader checks for each used agent and required smoke-test acceptance. V4.1 also requires the refreshed instructions, a fresh project chat and affected coordinator smoke tests. Record evidence in the rollout Issue; neither a baseline label nor this recorded decision proves independent review, validation, installation, smoke success or activation. After reviewed adoption under these conditions, the selected `WORK_PACKAGE` and Gate A policies govern future eligible tasks; all stricter safety and separately authorized-action boundaries remain binding.
