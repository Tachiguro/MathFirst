# MathFirst Agent Governance Rules

This document defines the universal repository invariants for all autonomous agents and human contributors working on **MathFirst**. All repository-writing agents MUST read this file before performing any repository modifications.

For task-specific routing, operation modes, testing, and workflow rules, consult [docs/INDEX.md](docs/INDEX.md).

---

## 1. Core Invariants & Repository Safety

1. **Repository Truth**: Live Git and GitHub repository state overrides chat history, handoffs, remembered context, assumptions, and previous agent reports.
2. **Canonical Checkout**: All work is performed in the single canonical local checkout at `C:\Dev\MathFirst` by default.
3. **Single Worktree**: Exactly one normal Git worktree is used by default unless the user explicitly authorizes additional worktrees.
4. **Single Writer**: Exactly one repository-writing agent may operate at a time. Parallel writers, writing subagents, and background repository writers are strictly forbidden without explicit user approval.
5. **Default Branch Protection**: Never author, implement, or commit changes directly on the default branch (`main`). All changes must be developed on a dedicated task branch and merged via Pull Request.
6. **Protect Local Work**: Never overwrite, discard, or silently reinterpret unexpected pre-existing local files, uncommitted edits, or unknown branches. Stop and report conflicts immediately.
7. **Live Verification Required**: Before any repository write, inspect and verify current branch, HEAD SHA, working tree, index, untracked files, remotes, and worktree state.
8. **Plan Before Write**: Every non-trivial change must be planned and approved before authoring code or documentation.

---

## 2. Forbidden Git Operations

The following Git operations are strictly forbidden without explicit user approval:
- `git reset` (soft, mixed, or hard)
- `git clean`
- `git stash`
- `git rebase`
- `git commit --amend`
- History rewriting of any kind
- Force-pushing (`git push --force`, `git push -f`, `--force-with-lease`)
- Branch deletion (local or remote)
- Worktree deletion

---

## 3. Strict Staging Policy

- **Never** use `git add .` or `git add -A`.
- **Never** use glob patterns that can accidentally stage unreviewed files.
- Stage only **explicit file paths** during `COMMIT_ONLY` or `CHECKPOINT_COMMIT_ONLY` operations.

---

## 4. Lifecycle Isolation & Operation Modes

Agents operate in isolated, single-responsibility **Operation Modes** (defined in [docs/PROMPT_AND_TASK_ROUTING.md](docs/PROMPT_AND_TASK_ROUTING.md)). Every agent task:
- Focuses on exactly one primary Operation Mode.
- Must not silently transition into another lifecycle phase.
- Ends with a definitive hard stop and technical report.

---

## 5. Non-Delegable Operations

The following actions require **explicit, affirmative user approval** and must never be executed autonomously by agents:
- Merging a Pull Request
- Enabling auto-merge on GitHub
- Branch or worktree deletion
- Destructive Git operations (`reset`, `clean`, `stash`, `rebase`, `amend`, force push, history rewrite)
- Git tag creation or GitHub release creation
- Application deployment or publishing
- App store uploads or binary signing
- Package creation outside explicit scope
- Emulator, simulator, physical device, or ADB operations
- Manual device testing
- Operations touching real private user data
- Launching parallel writing agents or subagents
- Material scope expansion

---

## 6. Privacy & Data Baseline

Automated tests and scripts must never open, copy, migrate, alter, delete, or test against real private user data. All testing must use synthetic fixtures, fake clocks, mocked networks, and temporary isolated directories (see [docs/TESTING.md](docs/TESTING.md)).

---

## 7. Language & Communication Baseline

- **User-Facing Orchestration**: German.
- **Coding-Agent Prompts**: English.
- **Coding-Agent Technical Reports**: English.
- **Code & Documentation**: English.

---

## 8. Documentation Router

Refer to [docs/INDEX.md](docs/INDEX.md) for the complete documentation index and task-based routing matrix.
