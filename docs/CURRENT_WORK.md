# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-IMPL-006`
- **Title**: Windows V1 Hardening & End-to-End Simulation
- **Active Task Branch**: `feat/mf-impl-001-windows-addition-skeleton`
- **Base Branch**: `main`
- **Base Commit**: `a0b8d816ca1d2b8b2a4fbc958e4fb9e82badd273`
- **Current Lifecycle Mode**: `IMPLEMENT_ONLY_CONTINUE`
- **Delivery Priority**: Windows-first
- **Package Scope**:
  - Performed comprehensive audit and hardening of the complete uncommitted Windows V1 candidate across domain, application, persistence, FSRS-6 scheduler, UI/UX, and localization;
  - Exhaustively validated the entire 418-fact arithmetic catalog (121 Addition, 66 Subtraction, 121 Multiplication, 110 Division) verifying unique stable FactIds, non-negative subtraction, non-zero divisor, integer division, proper range bounds, and display symbols;
  - Created and executed deterministic end-to-end progression simulation proving that fresh learner state reaches Level 10 checkpoint and transitions into open-ended adaptive mixed practice with all 418 facts exposed and zero level 11 expansion;
  - Conducted high-volume synthetic learner simulations (5,000 attempts per profile: Strong, Mixed, and Struggling) verifying FSRS numeric safety (non-NaN, finite Stability/Difficulty, non-overflowing DuePracticePosition), proper prioritization of weak items, and larger spacing for mastered items;
  - Verified FSRS task-time determinism across arbitrary calendar dates and timezones;
  - Executed high-volume SQLite stress testing (5,000 atomic commits in isolated temp DB) with clean snapshot reload, revision verification, and zero corruption;
  - Validated three-way reset matrix (Reset Learning Progress vs Reset UI Preferences vs Full Local Reset) and restart persistence;
  - Audited package references and confirmed zero vulnerabilities across all dependencies;
  - Verified localization dictionary parity across English, German, and Russian with zero missing keys;
  - Executed clean Windows native smoke launches and restart validations;
  - Expanded automated test suite to 174 passing unit and simulation tests with zero warnings and zero build errors;
  - Implemented adaptive answer-deadline ladder based on consecutive-correct streak (30s / 20s / 15s / 10s) with streak failure reset safely restoring the longest 30s deadline;
  - Enlarged the full-card-width countdown progress bar to 40-48px height (`clamp(40px, 4.5vh, 48px)`) with prominent centered bold millisecond display (`clamp(1.25rem, 2.8vh, 1.65rem)`, `XX.XXX s`) rendered with a direct high-contrast black glyph outline (`-webkit-text-stroke: 2px #000`, `paint-order: stroke fill`) directly on the countdown text;
  - Implemented monotonic pause/resume timing in `TrainingSession` across Settings navigation, cleanly preserving remaining deadline, excluding time spent in Settings from response latency and timeout evaluations, and persisting completed feedback interaction states (`TimeoutFeedback`, `IncorrectFeedback`, `CorrectFeedback`) across view navigation;
  - Re-aligned training page composition to top-justify MathFirst branding and Settings button, lowered primary action button placement, and added responsive portrait rules with resilient arithmetic typography (`clamp(2.5rem, 6.5vw + 1.5vh, 4.75rem)`);
  - Added Current Answer Deadline telemetry to Settings Developer Diagnostics in English, German, and Russian;
  - Expanded automated test suite to 178 passing unit and simulation tests with zero warnings and zero build errors.
- **Explicitly Deferred Features**:
  - Web and Android implementations deferred.

---

## 2. Operational Rules

1. **Subordinate Status**: If `CURRENT_WORK.md` indicates an active package but the local branch is `main` with zero unpushed commits and no open PR, live repository state overrides this document.
2. **No Cleanup Commit Treadmill**: A dedicated post-merge commit is not required merely to reset this file to idle; new chat sessions detect resolved work by examining live Git/GitHub history.
