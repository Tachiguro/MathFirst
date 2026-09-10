# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-UX-003`
- **Title**: Native Identity, Version, Visual Reconciliation
- **Active Task Branch**: `feat/mf-ux-003-native-identity`
- **Base Branch**: `main`
- **Base Commit**: `194b6d6a5a9f11c989bcaaf1468758ff82386bba`
- **Review-Approved Implementation Commit**: `9a67f387a57261a4d76afa70510fc6e8b1d23447`
- **Documentation Reconciliation**: Applied locally in the current working tree; documentation changes are not yet committed.
- **Implementation Status**: Complete and consolidated re-review `REVIEW_APPROVED`; all review findings are resolved.
- **Delivery Status**: The branch is unpushed, no Pull Request exists, and `main` does not yet contain MF-UX-003.
- **Previous Delivery Status**: `MF-UX-002` is complete and merged; `MF-DOC-001` and `MF-LEARN-001` are also complete and merged.
- **Current Product Direction**: Native V1 Release Readiness.
- **Platform Status**: Windows and Android remain the active native priorities. Web remains deferred.

---

## 2. Operational Rules

1. **Subordinate Status**: If this file differs from the current branch, working tree, or GitHub state, live repository evidence is authoritative.
2. **Lifecycle Isolation**: Formal full validation, push, Pull Request, and merge have not occurred for `MF-UX-003`.

---

## 3. Current Lifecycle Position

- **Documentation Commit Boundary**: The review-approved implementation HEAD is `9a67f387a57261a4d76afa70510fc6e8b1d23447`. A final documentation-inclusive candidate HEAD does not yet exist; `COMMIT_ONLY — MF-UX-003 — Documentation Reconciliation Commit` creates it.
- **Remaining Delivery Sequence**: `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`.
- **Next Package Gate**: MF-REL-001 is not started and remains blocked until MF-UX-003 completes its lifecycle.
