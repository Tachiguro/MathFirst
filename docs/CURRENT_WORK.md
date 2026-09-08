# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-AND-001`
- **Title**: Android V1 Runtime and Cross-Platform Foundation
- **Active Task Branch**: `feat/mf-and-001-android-v1`
- **Base Branch**: `main`
- **Base Commit**: `66f3175b05837670e884acc43f41d2db88480bec`
- **Current Lifecycle Mode**: `FIX_ONLY`
- **Manual Acceptance Status**: `PASSED` on a real Android device, including responsive layout, custom keypad/native IME behavior, Ready/Pause/Resume, smart auto-submit, and Incorrect/Timeout feedback context.
- **Review Status**: `REVIEW_ONLY` completed with four focused findings (`MF-AND-R001` through `MF-AND-R004`); the package is in the resulting `FIX_ONLY` cycle and remains uncommitted and unstaged.
- **Delivery Priority**: Android V1 while preserving Windows V1
- **Package Scope**:
  - Activate the native MAUI app for `net10.0-android` while retaining `net10.0-windows10.0.19041.0`; do not activate Web, iOS, or Mac Catalyst;
  - Extract the concrete Schema V4 `SqliteLearnerStore` and `Microsoft.Data.Sqlite` ownership from `MathFirst.Application` into `MathFirst.Infrastructure.Sqlite`, retain Application persistence contracts, and register the adapter through app dependency injection;
  - Continue using private `FileSystem.AppDataDirectory\mathfirst_learner.db` storage on Android without shared-storage permissions, absolute Android paths, database migration, or changes to persistence semantics;
  - Use the shared custom MathFirst keypad as Android's primary answer UI and request native soft-keyboard suppression while preserving focus, Enter, and external/physical keyboard paths;
  - Derive timer activity from application foreground state, Practice/Home surface activity, a single transient Practice gate, and `AwaitingAnswer`, excluding background/device-lock time, Settings, onboarding, pause gates, and feedback states without generating semantic attempts or resetting the deadline;
  - Respect Android status/navigation bars and display cutouts through .NET 10 native safe-area handling; stack Appearance and keypad-selection choices on phone portrait and use a compact two-column Practice composition on short phone landscape with equation/answer left and the complete keypad right;
  - Remove the superseded selectable correct-answer confirmation preference and permanent Submit action; deterministically auto-submit complete integer answers, immediately advance correct answers after persistence, and require blocking Continue/Enter acknowledgement for Incorrect and Timeout results;
  - Gate every later cold Practice startup behind an opaque localized Ready overlay, treat onboarding Get Started as the first-session start gate, require explicit Resume after background return, and provide a Pause control beside Settings that freezes semantic time while completely hiding the fact and keypad;
  - Preserve the four onboarding steps, Settings workflows, Phone and PC Numpad orders, shared numeric policy, EN/DE/RU parity, app identity, and all learning-engine constants and semantics;
  - Add deterministic lifecycle, Ready/Pause gate, smart auto-submit, and feedback acknowledgement tests plus architecture, responsive-layout, persistence/reset, localization, and Android input/source contracts without duplicating the existing SQLite conformance suites;
  - Record the completed real-device Android spot-check covering cold-start readiness, one- and multi-digit auto-submit, blocking Incorrect/Timeout feedback, manual/background pause, hidden problem content, compact landscape keypad reachability, runtime IME suppression, system-bar clearance, and restart persistence as `PASSED`;
  - Complete only the four findings from the completed `REVIEW_ONLY`, keep the candidate uncommitted and unstaged, and return it to `REVIEW_ONLY` after focused and full validation.
- **Explicitly Deferred Features**:
  - Web, iOS, and Mac Catalyst runtime activation;
  - Release packaging, signing, publishing, emulator creation, and automated physical-device operations.

---

## 2. Operational Rules

1. **Subordinate Status**: If `CURRENT_WORK.md` indicates an active package but the local branch is `main` with zero unpushed commits and no open PR, live repository state overrides this document.
2. **No Cleanup Commit Treadmill**: A dedicated post-merge commit is not required merely to reset this file to idle; new chat sessions detect resolved work by examining live Git/GitHub history.
