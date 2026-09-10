# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-UX-002`
- **Title**: Deterministic Practice Personality and Contextual Copy
- **Active Task Branch**: `feat/mf-ux-002-contextual-copy`
- **Base Branch**: `main`
- **Base Commit**: `97bd5476eff12858068119a7e84880c9c92be3dd`
- **Candidate Implementation Commit**: `27e8fb6db511bc5875c8ca16feb6e800b84bc8b0`
- **Documentation Reconciliation**: Complete and `REVIEW_APPROVED` as part of the MF-UX-002 candidate delivery.
- **Implementation Status**: Complete and `REVIEW_APPROVED` on the candidate branch; all implementation review findings are resolved.
- **Delivery Status**: The branch is unpushed, no Pull Request exists, and `main` does not yet contain MF-UX-002.
- **Previous Delivery Status**: `MF-DOC-001` is complete and merged; `MF-LEARN-001` is completed and merged through Pull Request #9.
- **Current Product Direction**: Native V1 Release Readiness.
- **Platform Status**: Windows and Android remain the active native priorities. Web remains deferred.

---

## 2. Operational Rules

1. **Subordinate Status**: If this file differs from the current branch, working tree, or GitHub state, live repository evidence is authoritative.
2. **Lifecycle Isolation**: Formal full validation, push, Pull Request, and merge have not occurred for `MF-UX-002`.

---

## 3. Current Lifecycle Position

- **Documentation Commit Boundary**: A `COMMIT_ONLY` lifecycle step records the review-approved reconciliation before formal candidate validation.
- **Remaining Delivery Sequence**: `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`.
- **Next Package Gate**: MF-UX-003 remains blocked until MF-UX-002 is merged and post-merge synchronization completes.
