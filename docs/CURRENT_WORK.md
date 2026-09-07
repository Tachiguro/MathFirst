# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-PROD-001`
- **Title**: MathFirst Product Definition
- **Active Task Branch**: `docs/mf-prod-001-product-definition`
- **Base Branch**: `main`
- **Base Commit**: `a3e0bec9da7352421931e30cc2130fee2217871f`
- **Current Lifecycle Mode**: `DOCUMENT_ONLY`
- **Next Lifecycle Mode**: `REVIEW_ONLY`
- **Known Open Decisions**: Confirmed product definition requirements established in `docs/PRODUCT.md`; technical architecture and implementation details deferred to Phase 3.

---

## 2. Operational Rules

1. **Subordinate Status**: If `CURRENT_WORK.md` indicates an active package but the local branch is `main` with zero unpushed commits and no open PR, live repository state overrides this document.
2. **No Cleanup Commit Treadmill**: A dedicated post-merge commit is not required merely to reset this file to idle; new chat sessions detect resolved work by examining live Git/GitHub history.
