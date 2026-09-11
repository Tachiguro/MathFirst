# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-DOC-002`
- **Title**: Adaptive Learning System and Next Lifecycle Handoff Documentation
- **Active Task Branch**: `docs/mf-doc-002-adaptive-learning-handoff`
- **Base Branch / Commit**: `main` at `45ef623f44df87c0da97460d38dbb797c2aa18bf`
- **Operation Mode**: `DOCUMENT_ONLY`
- **Status**: Authoring unstaged documentation changes to capture the newly approved product direction for `MF-LEARN-002` (Adaptive Pace, Fast Acquisition, and Practice Interventions) into [docs/BACKLOG.md](BACKLOG.md), align roadmap sequencing, and reconcile documentation with the completed `MF-STAB-001` baseline.
- **Prior Completed Package**: `MF-STAB-001` (Practice Progression and HUD Stabilization) is complete and merged into `main` via Pull Request #13 at `45ef623f44df87c0da97460d38dbb797c2aa18bf` (final validated candidate: `f54d0b1c5c8884ff1d4871abca667582759d5be1`; Core: 546 passed, 0 failed, 0 skipped; Windows/Android Release: 0 warnings, 0 errors).

---

## 2. Operational Rules

1. **Subordinate Status**: If this file differs from the current branch, working tree, or GitHub state, live repository evidence is authoritative.
2. **Lifecycle Isolation**: `DOCUMENT_ONLY` modifies documentation files without staging, committing, or pushing.
3. **No Behavioral Implementation**: No code or tests under `src/` or `tests/` are modified during this package.

---

## 3. Current Lifecycle Position

- **Immediate Next Lifecycle Step**: `REVIEW_ONLY — MF-DOC-002 — Adaptive Learning Documentation Handoff Review`.
- **Subsequent Documentation Delivery**: `COMMIT_ONLY` → `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`.
- **Next Behavioral Package**: Upon completion and sync of `MF-DOC-002`, the next behavioral package begins with `PLAN_ONLY — MF-LEARN-002 — Adaptive Pace, Fast Acquisition, and Practice Interventions`.
- **Deferred Work**: `MF-REL-001` (Android Internal AAB Packaging and Release Automation) remains not started and is intentionally deferred until `MF-LEARN-002` completes its entire lifecycle.
