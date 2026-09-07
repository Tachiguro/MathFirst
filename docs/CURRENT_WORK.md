# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-ARCH-002`
- **Title**: Offline Execution and Local Persistence Boundary
- **Active Task Branch**: `docs/mf-arch-002-persistence-boundary`
- **Base Branch**: `main`
- **Base Commit**: `dfd7492927780c1dd9f4644d3925448b88c8113e`
- **Current Lifecycle Mode**: `DOCUMENT_ONLY`
- **Next Lifecycle Mode**: `REVIEW_ONLY`
- **Accepted Package Architecture**:
  - Capability-oriented Application persistence boundary;
  - Atomic semantic-submission change sets (attempt evidence, item state, progression);
  - Optimistic revision checking to prevent stale overwrites;
  - Platform-specific persistence adapters;
  - Explicit persisted-format versioning;
  - Evidence-gated IndexedDB and SQLite primary candidates.
- **Known Open Decisions**: Final concrete persistence technology, response-time architecture, learning-engine and scheduler boundaries, physical database schema, and later Thin Vertical Slice implementation details.

---

## 2. Operational Rules

1. **Subordinate Status**: If `CURRENT_WORK.md` indicates an active package but the local branch is `main` with zero unpushed commits and no open PR, live repository state overrides this document.
2. **No Cleanup Commit Treadmill**: A dedicated post-merge commit is not required merely to reset this file to idle; new chat sessions detect resolved work by examining live Git/GitHub history.
