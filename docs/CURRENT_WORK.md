# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-LEARN-001`
- **Title**: Independent Operation Progression and Open-Ended Fact Space
- **Active Task Branch**: `feat/mf-learn-001-independent-progression`
- **Base Branch**: `main`
- **Base Commit**: `223a75f76adeb81983f50162c0941f344848260a`
- **Current Lifecycle Mode**: `FULL_VALIDATION` (current delivery gate; validation has not yet passed).
- **Planning Status**: Complete; the refined Hybrid curriculum architecture is approved.
- **Documentation Status**: Documentation reconciliation is complete for the approved implementation and recorded for the pending final validation gate.
- **Implementation Status**: Complete on the local feature branch. Learner Schema V5, independent progression, structured generators, ownership-aware frontiers, deterministic bounded selection, and the target selector policy are implemented and reviewed.
- **Review Status**: `REVIEW_APPROVED`; persistence integrity, publish-after-commit state, bounded runtime selection, and long-run/restart/migration coverage findings are resolved.
- **Delivery Status**: Local-only. No push or Pull Request exists; the branch remains 11 commits ahead of `main`.
- **Platform Status**: No Web work is active. Windows and Android remain the active native product priorities.
- **Package Scope**:
  - Document independent Addition, Subtraction, Multiplication, and Division progression;
  - Define the dense 567-fact foundation and deterministic structured arithmetic bands;
  - Define unique acquisition ownership, representative structured acquisition, exact advancement gates, and deterministic queue roles;
  - Define lazy materialization, stable FactIds, FSRS boundaries, and atomic V4-to-V5 migration without learner reset;
  - Reconcile product, project-state, roadmap, ADR registry, and changelog documentation without changing application source or tests.

---

## 2. Operational Rules

1. **Subordinate Status**: If this file differs from the current branch, working tree, or GitHub state, live repository evidence is authoritative.
2. **Lifecycle Isolation**: The candidate is at the `FULL_VALIDATION` delivery gate; validation must complete before `PUSH_ONLY`, and no push, PR, or merge is part of the current invocation.

---

## 3. Current Lifecycle Position

- **Current Activity**: Candidate at the `FULL_VALIDATION` delivery gate; validation has not yet passed.
- **Next Governed Activity**: `PUSH_ONLY` after successful `FULL_VALIDATION`.
- **Later Lifecycle**: `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`.
