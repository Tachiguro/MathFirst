# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-ARCH-001`
- **Title**: Cross-Platform Application Stack Baseline
- **Active Task Branch**: `docs/mf-arch-001-cross-platform-stack`
- **Base Branch**: `main`
- **Base Commit**: `5a1bda2259a55b60d6693dac0296a564681dc2be`
- **Current Lifecycle Mode**: `DOCUMENT_ONLY`
- **Next Lifecycle Mode**: `REVIEW_ONLY`
- **Known Open Decisions**: Architecture baseline accepted for topology and concrete stack. Persistence technology, learning-engine boundaries, scheduler details, and later architecture decisions remain unresolved.

---

## 2. Operational Rules

1. **Subordinate Status**: If `CURRENT_WORK.md` indicates an active package but the local branch is `main` with zero unpushed commits and no open PR, live repository state overrides this document.
2. **No Cleanup Commit Treadmill**: A dedicated post-merge commit is not required merely to reset this file to idle; new chat sessions detect resolved work by examining live Git/GitHub history.
