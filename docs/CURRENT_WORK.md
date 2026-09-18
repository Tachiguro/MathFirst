# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Work Package**: `MF-UX-005` — Native UX, Responsiveness, and Interaction Polish
- **Active Task**: Slice 1 — Practice Performance and Keypad Press-State Reliability
- **Current Operation Mode**: `IMPLEMENT_SLICE`
- **Active Task Branch**: `feat/mf-ux-005-practice-performance-keypad-reliability`
- **Baseline Commit**: `156d5afd32299ba19d8ca2a8f2f56a7babfe31d8` (merged PR #29)
- **Slice 1 Progress**:
  1. Practice timer render isolation: Extracted countdown presentation into `PracticeCountdownTimer.razor` updating at 10 Hz (100ms interval), eliminating ~20 full `Home.razor` component re-renders per second while maintaining authoritative monotonic elapsed time and timeout accuracy.
  2. Preferences hot-path removal: Removed repeated `PreferenceStore.GetEnabledOperations()` reads from `OperationProgress` during hot timer/render evaluation; enabled operations are cached at explicit initialization and lifecycle boundaries.
  3. Keypad visual reliability: Guarded `.numeric-keypad-button:not(:disabled):hover` in `app.css` with `@media (hover: hover) and (pointer: fine)` to eliminate sticky touch hover in Android WebView, and keyed `.numeric-keypad` container with `@key="_lastPreparedFactInstanceRevision"` so buttons start completely neutral on every new arithmetic fact without lingering visual focus or press artifacts.
- **Next Technical Lifecycle**: `REVIEW_ONLY` — MF-UX-005 Slice 1

---

## 2. Pending Later Slices for MF-UX-005

The following accepted slices under `MF-UX-005` remain pending and NOT yet implemented in Slice 1:

1. **KnownFirst-style Onboarding Layout & Navigation**: Five-step required onboarding flow refinement without "Start with defaults / Skip setup" shortcut.
2. **Android Back in Onboarding**: Consistent hardware/gesture Back navigation throughout onboarding steps.
3. **Android Back from Settings -> Practice**: Navigating directly back to Practice on Android Back rather than exiting the application.
4. **Delayed Repeated-Error Teaching Acknowledgement**: Visible 3-second lockout on the teaching intervention modal before the learner can proceed.
5. **Haptic Touch Feedback**: Platform-specific haptic vibration on keypad taps and interaction milestones.
6. **Pause Overlay Information**: Extended session statistics and information on the manual pause dialog.
7. **Native Startup White-Flash / Root-Theme Correction**: Elimination of native window/activity white flash during initial splash/theme launch.
8. **Installed-Size / App-Data Investigation**: APK/AAB package size analysis and runtime app data profiling.
9. **Tester Ergonomics**: Streamlined tester diagnostics and feedback mechanisms.

---

## 3. Explicit Downstream Roadmap Boundaries

The following stages are strictly sequential and require separate authorization:

1. **Comprehensive Review (`REVIEW_ONLY`)**: Package-wide review of Slice 1 code, tests, and documentation.
2. **Subsequent MF-UX-005 Slices**: Iterative implementation of remaining UX polish slices under separate authorized dispatches.
3. **Full Validation (`FULL_VALIDATION`)**: Execution of automated regression and verification suites.
4. **Pull Request & Explicit Merge Authorization**: Dedicated PR to `main` with affirmative user approval.
5. **Post-Merge Synchronization & Exact Candidate Establishment**: Establishing the new exact candidate SHA on synchronized `main`.
6. **Production Packaging & Signing**: Creation of a production `Distributable` AAB package signed with release credentials.
7. **Physical Android Technical Smoke Verification**: Verification on clean physical hardware.
8. **Manual Physical-Device Functional Verification**: Verifying curriculum boundaries and UX polish in manual practice.
9. **Google Play Publication Gate**: Separate decision on store upload.
