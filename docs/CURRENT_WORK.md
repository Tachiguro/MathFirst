# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Work Package**: `MF-UX-005` — Native UX, Responsiveness, and Interaction Polish
- **Active Task**: Slice 4 — Configurable Haptic Feedback
- **Current Operation Mode**: `IMPLEMENT_SLICE`
- **Active Task Branch**: `feat/mf-ux-005-practice-performance-keypad-reliability`
- **Baseline Commit**: `156d5afd32299ba19d8ca2a8f2f56a7babfe31d8` (merged PR #29)
- **Slice 1 Progress (Completed & Validated)**:
  1. Practice timer render isolation: Extracted countdown presentation into `PracticeCountdownTimer.razor` updating at 10 Hz (100ms interval), eliminating ~20 full `Home.razor` component re-renders per second while maintaining authoritative monotonic elapsed time and timeout accuracy.
  2. Preferences hot-path removal: Removed repeated `PreferenceStore.GetEnabledOperations()` reads from `OperationProgress` during hot timer/render evaluation; enabled operations are cached at explicit initialization and lifecycle boundaries.
  3. Keypad visual reliability: Guarded `.numeric-keypad-button:not(:disabled):hover` in `app.css` with `@media (hover: hover) and (pointer: fine)` to eliminate sticky touch hover in Android WebView, and keyed `.numeric-keypad` container with `@key="_lastPreparedFactInstanceRevision"` so buttons start completely neutral on every new arithmetic fact without lingering visual focus or press artifacts.
- **Slice 2 Progress (Completed & Validated)**:
  1. Authoritative UX & Product Decision Reconciliation: Reconciled and durably recorded all 18 accepted, rejected, and deferred user decisions in repository documentation.
  2. KnownFirst-style Onboarding Action Layout: Restructured onboarding actions across all five steps to use vertically stacked full-width button presentation (`.onboarding-actions` column layout) with primary forward action on top and Back action below, maintaining the required five-step flow without skip shortcuts and preserving draft selections (language, theme, keypad layout, operation selection) across backwards step transitions.
  3. Deterministic Android Back Navigation Bridge: Implemented `IAppBackNavigationCoordinator` and thread-safe `AppBackNavigationCoordinator` in `MathFirst.Application.Navigation`, wired into `MainPage.xaml.cs` to intercept Android system Back button and predictive gestures:
     - Onboarding steps 2–5 navigate back to previous step; step 1 safely passes through to platform default backgrounding without losing draft state or completing onboarding.
     - Settings page navigates directly back to `/` (Home/Practice), preserving the active question, partial answer input, and remaining answer deadline semantics (Settings time excluded from answer latency).
     - Practice/Home root surface passes through to normal Android task backgrounding with active timer frozen behind the resume gate.
- **Slice 3 Progress (Completed & Validated)**:
  1. Minimum 3-Second Visible Lock for Repeated-Error Teaching Intervention: Implemented `TeachingLockTracker` with monotonic clock time accumulation and `TeachingInterventionDialog.razor` with 10 Hz countdown feedback. Continue button is initially disabled with localized countdown, Enter key bypass is blocked while locked, backgrounding freezes required visible dwell time, and acknowledging upon unlock preserves zero scoring/progression mutations.
  2. Non-Destructive Amber/Yellow Pause Styling: Replaced `.button-danger` on `.pause-practice-btn` with dedicated `.button-pause` / `.button-warning` with accessible high-contrast amber palette in Light (`#b26a00`) and Dark (`#e09f3e`) modes, preserving destructive danger styling on reset controls.
  3. Modernized Timer Typography: Completely removed `-webkit-text-stroke` and heavy text shadows across all breakpoints; introduced clean, bold sans-serif timer typography with subtle dark translucent pill backing providing high contrast in Light and Dark themes across all timer fill colors.
- **Slice 4 Progress (Current Slice)**:
  1. Haptic Feedback Application Abstraction & Failure Isolation: Defined `IHapticFeedbackService`, `IHapticDriver`, `HapticFeedbackService`, `NoOpHapticDriver`, and `HapticFeedbackCue` (`KeyTap`, `Correct`, `Incorrect`, `Timeout`) in `MathFirst.Application` with complete failure isolation protecting learning and submission flow from hardware/driver exceptions.
  2. Platform Driver & Android Manifest Normal Permission: Implemented `MauiHapticDriver` in `MathFirst.App.Services` utilizing `Microsoft.Maui.Devices.HapticFeedback` (`Click` for `KeyTap`) and `Vibration` (40ms pulse for `Correct`, 120ms pulse for `Incorrect`/`Timeout`), safe no-op on Windows/unsupported platforms, and declared only the normal `android.permission.VIBRATE` permission in `AndroidManifest.xml`.
  3. Preference Persistence & Reset Workflows: Added persisted boolean `Haptic Feedback Enabled` (default `true`) to `IPreferenceStore` and `MauiPreferenceStore` (`mathfirst.haptic_feedback_enabled`), restored to enabled on Restore Defaults and Full Local Reset, preserved on Reset Learning Progress.
  4. Onboarding & Settings UI Integration: Integrated haptic toggle in Settings with immediate persistence and preview cue on enable, and integrated haptic selection in Onboarding Step 2 (Keypad Layout) preserving draft choice across step transitions and maintaining the exact five-step onboarding flow.
  5. Practice Interaction Integration: Wired `KeyTap` to numeric keypad taps and backspace, `Correct` to accepted correct answer evaluation, `Incorrect` to accepted incorrect answer evaluation, and `Timeout` to session timeout, with auto-submit double feedback handling and no re-render duplicate cues.
- **Next Technical Lifecycle**: `REVIEW_ONLY` — MF-UX-005 Slice 4

---

## 2. Authoritative Decisions Registry for MF-UX-005

The following 18 product and UX decisions are authoritative across all subsequent slices and chat transitions:

1. **Answer Submission (Auto-Submit)**: KEEP CURRENT BEHAVIOR. Single-digit canonical answers continue to auto-submit immediately upon entering the digit (e.g. if answer is 4 and user taps 9, it is immediately evaluated as incorrect). Do NOT introduce confirmation buttons, Enter-only submission, grace periods, correction delays, or separate Learning/Sprint input modes. Backspace remains available for partial multi-digit editing only.
2. **`(Enter)` Button Copy**: KEEP CURRENT BEHAVIOR. Retain `(Enter)` on Continue / Keep Going actions to preserve visible external/physical keyboard affordance.
3. **Pause Button Color**: IMPLEMENTED IN SLICE 3. Transitioned Pause away from destructive red / `button-danger` styling to non-destructive amber/yellow styling (`.button-pause`) with accessible contrast in both Light and Dark modes, clearly distinguishing Pause from destructive Reset/Delete actions.
4. **No Time Pressure Mode**: ACCEPTED CHANGE — PENDING LATER SLICE. Add a future Practice Time setting equivalent to "No Time Pressure" where response latency is measured and persisted for adaptive fluency and pacing, elapsed time remains visible, but there is NO countdown, NO answer deadline, and NO automatic Timeout. ("MEASURE TIME WITHOUT ENFORCING A DEADLINE"). Standard adaptive timing remains available.
5. **Timer Typography**: IMPLEMENTED IN SLICE 3. Removed heavy WordArt-like stroke (`-webkit-text-stroke`) in favor of cleaner bold sans-serif typography with subtle translucent pill backing ensuring accessible contrast across themes and fill color transitions.
6. **Practice Vertical Layout**: NO FORCED COMPRESSION REQUIRED. Numeric keypad remains anchored toward the bottom of the usable Practice area. Normal vertical whitespace between equation and keypad is acceptable. Only genuine clipping/overflow defects should be corrected.
7. **Didactic Tips / Visual Math Explanations**: REJECTED FOR CURRENT PRODUCT SCOPE. Do NOT add zero-rule hints, multiplication mnemonic tips, ten-frame graphics, dots/counters, or per-fact explanations. Canonical equation/result is sufficient.
8. **Error Remediation Spacing**: KEEP CURRENT BEHAVIOR. Retain existing spaced in-session remediation (`LearningPolicy.RemediationInterveningCount = 3`); incorrect facts do not immediately repeat on the consecutive turn.
9. **Repeated-Error Teaching Lock**: IMPLEMENTED IN SLICE 3. On second consecutive error for the exact FactId, TeachingIntervention displays canonical equation with Continue initially disabled for a 3-second visible lockout with localized countdown feedback, acknowledging with zero scoring or learning mutations.
10. **Haptic Feedback**: IMPLEMENTED IN SLICE 4. Provided distinguishable tactile feedback for keypad tap (`Click`), correct answer (`40ms pulse`), and incorrect/timeout (`120ms pulse`); subtle, respects platform capabilities with only normal `VIBRATE` manifest permission, no-ops safely on unsupported platforms, configurable in Settings and Onboarding Step 2, persisted locally, default enabled.
11. **Streak Feedback**: ACCEPTED CHANGE — PENDING LATER SLICE. Positive, age-neutral consecutive correct streak feedback without manipulative pressure, fake praise, or learning mutations; session presentation only.
12. **Confirmation / Learning Mode**: REJECTED. Do NOT add answer confirmation buttons, checkmark submit buttons, or separate Learning/Sprint modes. Smart auto-submit remains authoritative.
13. **Pause Information**: ACCEPTED — PENDING LATER SLICE. Lightweight current-session stats on Pause overlay (completed/correct attempts, streak, valid progression changes, median correct latency) without invented percentages.
14. **Startup White Flash**: ACCEPTED DEFECT INVESTIGATION — PENDING LATER SLICE. Investigate native MAUI/Android launch theme, splash background, and BlazorWebView initialization to eliminate white flash before dark UI renders, including portable guidance for KnownFirst and other MAUI Blazor apps.
15. **Installed Size / App Data**: ACCEPTED INVESTIGATION — PENDING LATER SLICE. Separate analysis of debug vs release APK/AAB payloads, native libraries, WebView runtime, SQLite storage, and cache. Debug APK size is not production evidence.
16. **Tester Ergonomics**: PENDING MF-UX-005 SCOPE. Source/build identity display, easy diagnostic copy, repeatable tester artifacts without release build leaks.
17. **KnownFirst-Style Onboarding Action Layout**: IMPLEMENTED IN SLICE 2. Vertically stacked full-width actions with primary forward action on top and Back below across all 5 steps, with in-session draft selection preservation and no Skip shortcut.
18. **Android Back Navigation**: IMPLEMENTED IN SLICE 2. Application-owned `IAppBackNavigationCoordinator` handling Onboarding steps 2–5 back navigation, Onboarding step 1 safe background pass-through, Settings return to `/` with question/input/timing preservation, and Root Practice safe background pass-through with timing freeze.

---

## 3. Pending Later Slices for MF-UX-005

The following accepted items under `MF-UX-005` remain pending for subsequent implementation slices:

1. **No Time Pressure Mode**: Practice time option measuring latency without deadlines/timeouts.
2. **Restrained Streak Feedback**: Positive consecutive correct streak presentation.
3. **Pause Overlay Information**: Extended session statistics on the manual pause dialog.
4. **Native Startup White-Flash / Root-Theme Correction**: Elimination of native window/activity white flash during initial splash/theme launch with KnownFirst portability guidance.
5. **Installed-Size / App-Data Investigation**: APK/AAB package size analysis and runtime app data profiling.
6. **Tester Ergonomics**: Streamlined tester diagnostics and feedback mechanisms.
7. **Physical Android Device Verification**: Physical confirmation of Android system-Back navigation, haptic feel, and cumulative UX behaviors on hardware.

---

## 4. Explicit Downstream Roadmap Boundaries

The following stages are strictly sequential and require separate authorization:

1. **Comprehensive Review (`REVIEW_ONLY`)**: Package-wide review of Slice 3 code, tests, and documentation.
2. **Subsequent MF-UX-005 Slices**: Iterative implementation of remaining UX polish slices under separate authorized dispatches.
3. **Full Validation (`FULL_VALIDATION`)**: Execution of automated regression and verification suites.
4. **Pull Request & Explicit Merge Authorization**: Dedicated PR to `main` with affirmative user approval.
5. **Post-Merge Synchronization & Exact Candidate Establishment**: Establishing the new exact candidate SHA on synchronized `main`.
6. **Production Packaging & Signing**: Creation of a production `Distributable` AAB package signed with release credentials.
7. **Physical Android Technical Smoke Verification**: Verification on clean physical hardware.
8. **Manual Physical-Device Functional Verification**: Verifying curriculum boundaries and UX polish in manual practice.
9. **Google Play Publication Gate**: Separate decision on store upload.
