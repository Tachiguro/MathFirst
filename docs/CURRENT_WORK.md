# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-WIN-UX-001`
- **Title**: KnownFirst-Aligned Windows UX and Application Identity
- **Active Task Branch**: `feat/mf-win-ux-001-knownfirst-alignment`
- **Base Branch**: `main`
- **Base Commit**: `e7f95dfa14b767496b7bec4b5ef2d477da3fd2c6`
- **Current Lifecycle Mode**: `IMPLEMENT_ONLY_CONTINUE`
- **Delivery Priority**: Windows-first
- **Package Scope**:
  - Correct production identity to `com.tachiguro.mathfirst` and unpackaged Windows runtime publisher `Tachiguro`, while retaining the development signing identity and `FileSystem.AppDataDirectory`;
  - Align Settings and onboarding visual structure with the verified KnownFirst reference at commit `e18eaaed26b5f662798d827f9199c77a18dc39fa`, adapted to MathFirst functionality and vocabulary;
  - Combine language and appearance on the Welcome step, add a localized three-card arithmetic tutorial whose third item explains adaptive repetition and the non-pass/fail 12-task mixed round, and finish with a minimal Ready/Get Started step;
  - Insert a dedicated localized Phone keypad / PC numpad selection step before the tutorial, persist the UI-only preference at Get Started, expose the same preview choices in Settings, and restore Phone only for UI-default/full resets;
  - Enforce a shared canonical unsigned decimal grammar with comma/period equivalence, exact decimal parsing, rejection of redundant leading-zero forms, a 28-character safety limit, synchronous pre-DOM keyboard/paste filtering with C# fallback authority, and no semantic attempt for invalid or incomplete input;
  - Render the selected responsive clickable/touchable numeric keypad on Windows while preserving physical keyboard, Windows numpad, Backspace, decimal-separator, focus, and single-Enter behavior;
  - Enforce the monotonic timing lifecycle so only the active Practice/Home surface consumes answer time, including after learning resets, default restoration, full local reset, and direct Settings initialization;
  - Center the arithmetic expression and a substantially wider answer field in the training card's flexible middle region, wrapping the two regions on narrow layouts while preserving the accepted timer design and strong arithmetic minimum size;
  - Present the internal checkpoint phase to learners as localized `Mixed round` / `Mischrunde` / `Смешанный раунд` with concise `n/12` context, without changing the 12-accepted-attempt progression semantics;
  - Bring all three Settings inline reset/restore confirmations into view after render with a coherent programmatic focus target and reduced-motion-aware nearest-block scrolling;
  - Add deterministic timing, identity, numeric policy, keypad preference/order, reset, source-contract, and EN/DE/RU localization coverage;
  - Validate the continued package with 261 automated tests passing (0 failed, 0 skipped) and a Windows Debug build with 0 warnings and 0 errors;
  - Do not repeat native startup in the continuation run because a probe proved that overriding `LOCALAPPDATA` does not redirect the Windows special folder used for learner storage; preserving real learner data takes precedence. The earlier pre-continuation startup evidence remains historical only;
  - Complete a native startup smoke showing a responsive `MathFirst` window and the canonical Tachiguro data root; no learner database was created before the onboarding Get Started gate. Automated screenshot-based visual acceptance was unavailable and remains a user spot-check;
  - Keep all implementation uncommitted and unstaged for subsequent user spot-check and `REVIEW_ONLY`.
- **Explicitly Deferred Features**:
  - Web and Android implementations deferred;
  - Android native decimal IME behavior, dynamic viewport resizing, software-keyboard height/overlap, Submit visibility, and custom-keypad hide/adapt behavior require explicit Android-package validation.

---

## 2. Operational Rules

1. **Subordinate Status**: If `CURRENT_WORK.md` indicates an active package but the local branch is `main` with zero unpushed commits and no open PR, live repository state overrides this document.
2. **No Cleanup Commit Treadmill**: A dedicated post-merge commit is not required merely to reset this file to idle; new chat sessions detect resolved work by examining live Git/GitHub history.
