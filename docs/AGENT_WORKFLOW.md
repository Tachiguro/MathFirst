# MathFirst Agent Workflow & Lifecycle

This document defines the authoritative development lifecycle, slicing protocols, TDD practices, review standards, and branch synchronization procedures for MathFirst.

---

## 1. Standard Package Lifecycles

All work in MathFirst is organized into small, bounded packages identified by a canonical `MF-<CATEGORY>-<NUMBER>` identifier (e.g. `MF-GOV-001`).

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
                                                            └── [Manual User Merge]
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
                                                    └── [Manual User Merge]
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

Every completed package must pass a documentation reconciliation check in `DOCUMENT_ONLY` mode:
- **`DOCUMENTATION_RECONCILED_WITH_CHANGES`**: Documentation updated (e.g. `CHANGELOG.md`, `docs/PROJECT_STATE.md`, `docs/CURRENT_WORK.md`). Requires a subsequent `COMMIT_ONLY`.
- **`DOCUMENTATION_CURRENT_NO_CHANGES`**: Documentation is already completely accurate; no changes or commits created.

---

## 6. Exact-Candidate Full Validation (`FULL_VALIDATION`)

- Runs against the **exact final candidate HEAD commit**.
- Any modification to the repository after validation makes prior validation immediately stale.
- **Fail-Closed**: Validation tools must not auto-repair code; failures must be reported as-is.

---

## 7. Push, Pull Request, and Manual Merge

1. **`PUSH_ONLY`**: Push the validated task branch to `origin` using `git push -u origin <branch>`.
2. **`PR_ONLY`**: Open a GitHub PR targeting `main` using the standard [.github/pull_request_template.md](../.github/pull_request_template.md).
3. **Manual User Merge**: The repository owner manually reviews and merges the PR on GitHub. Agents do not merge PRs or enable auto-merge.

---

## 8. Post-Merge Synchronization (`POST_MERGE_SYNC_ONLY`)

After the user manually merges the PR on GitHub:
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
3. Compare working tree against the latest checkpoint commit.
4. If the working tree is dirty or state is ambiguous, execute `REVIEW_ONLY` to establish the verified baseline before resuming implementation.
