# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Work Package**: `MF-UX-005` — Native UX, Responsiveness, and Interaction Polish
- **Active Task**: Tester Ergonomics — Diagnostics & Settings UX
- **Current Operation Mode**: `IMPLEMENT_SLICE`
- **Active Task Branch**: `feat/mf-ux-005-tester-diagnostics-settings`
- **Baseline Commit**: `83b767c2265c1baba560abdeaa9fedad365d70da`
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
- **Slice 4 Progress (Completed & Validated)**:
  1. Haptic Feedback Application Abstraction & Failure Isolation: Defined `IHapticFeedbackService`, `IHapticDriver`, `HapticFeedbackService`, `NoOpHapticDriver`, and `HapticFeedbackCue` (`KeyTap`, `Correct`, `Incorrect`, `Timeout`) in `MathFirst.Application` with complete failure isolation protecting learning and submission flow from hardware/driver exceptions.
  2. Platform Driver & Android Manifest Normal Permission: Implemented `MauiHapticDriver` in `MathFirst.App.Services` utilizing `Microsoft.Maui.Devices.HapticFeedback` (`Click` for `KeyTap`) and `Vibration` (40ms pulse for `Correct`, 120ms pulse for `Incorrect`/`Timeout`), safe no-op on Windows/unsupported platforms, and declared only the normal `android.permission.VIBRATE` permission in `AndroidManifest.xml`.
  3. Preference Persistence & Reset Workflows: Added persisted boolean `Haptic Feedback Enabled` (default `true`) to `IPreferenceStore` and `MauiPreferenceStore` (`mathfirst.haptic_feedback_enabled`), restored to enabled on Restore Defaults and Full Local Reset, preserved on Reset Learning Progress.
  4. Onboarding & Settings UI Integration: Integrated haptic toggle in Settings with immediate persistence and preview cue on enable, and integrated haptic selection in Onboarding Step 2 (Keypad Layout) preserving draft choice across step transitions and maintaining the exact five-step onboarding flow.
  5. Practice Interaction Integration: Wired `KeyTap` to numeric keypad taps and backspace, `Correct` to accepted correct answer evaluation, `Incorrect` to accepted incorrect answer evaluation, and `Timeout` to session timeout, with auto-submit double feedback handling and no re-render duplicate cues.
- **Slice 5 Progress (Completed & Validated)**:
  1. No Time Pressure Domain & Policy Model: Extended `PracticeTimeSetting` with `NoTimePressure = -1`, `PracticeTimePreferencePolicy.HasEnforcedDeadline`, `IsNoTimePressure`, safe normalization of unknown/corrupt values to `Standard`, zero deadline floor, and backward compatibility with `Standard`, `30s`, `45s`, and `60s`.
  2. Deadline Enforcement & Timeout Hardening: Explicitly modeled `HasEnforcedDeadline` in `TrainingSession`. In No Time Pressure mode, automatic timeout is impossible, `IsCurrentItemTimedOut()` returns `false`, `SubmitAnswer` evaluates answers after arbitrary latency (e.g. 47s, 120s) as `Correct`/`Incorrect` normally, `RecordTimeout()` throws `InvalidOperationException`, and `HandleTimeoutAsync` in `Home.razor` is guarded by `HasEnforcedDeadline`. Timed modes retain standard deadline enforcement and timeout semantics.
  3. Response Latency & Learning Integrity: Active elapsed response time continues to be measured monotonically, excluding pauses, Settings, background/suspend, and feedback gates. Actual measured latency is persisted uncapped into attempt records, FSRS ratings, item states, and adaptive pace history without synthetic "unlimited = fluent" bias.
  4. Count-Up Elapsed Presentation & Accessibility: `PracticeCountdownTimer.razor` conditionally renders count-up elapsed time (`LearningPolicy.FormatElapsedTimerDisplay`) with neutral timer container (`role="timer"`, localized elapsed aria-label), eliminating countdown bars, color-draining transitions, and fake progressbar maximums in No Time Pressure mode, while preserving 10 Hz render isolation and Slice 3 modern pill typography.
  5. Settings UI & Localization Parity: Added "No Time Pressure" (`PracticeTime_NoTimePressure`) option to Practice Time settings with accessible selection state and responsive grid layout (`.choice-grid-practice-time`), complete localization parity in English, German (`Ohne Zeitdruck`), and Russian (`Без спешки`), preserved across Reset Learning Progress, and restored to `Standard` on Restore Defaults and Full Local Reset.
- **Slice 6 Progress (Completed & Validated)**:
  1. Restrained Current Correct Streak Feedback: Implemented transient `TrainingSession.CurrentCorrectStreak` (+1 on Correct, 0 on Incorrect/Timeout, 0 on Reset Learning Progress and cold start). During active practice, a subtle `.practice-streak-badge` appears in `.training-meta-row` only when streak $\ge 3$ (e.g. `Streak: 3`, `Streak: 7`) with localized accessible labels (`Training_Streak`, `Training_StreakAriaLabel`), disappearing instantly upon error/timeout without celebratory fanfare, XP, or gamification.
  2. Truthful Pause Session Summary: Implemented immutable `PracticeSessionSummary` and `TrainingSession.GetSessionSummary()` displaying Completed count, Correct count, Current streak, and Median correct response time (`LearningPolicy.FormatLatencySeconds` / `AdaptivePacePolicy.Median`) in `.pause-session-summary` within the Manual Pause overlay. Neutral placeholder `—` when 0 correct attempts.
  3. Strict Non-Mutation and Zero Schema Changes: Streak and pause summary state is purely transient in-memory presentation state. Zero SQLite schema changes, zero database writes/reads during render loops, zero impact on FSRS-6 spaced repetition, adaptive pace calculation, novel fact progression, or band advancement.
  4. Full Localization Parity: Added keys for English, German (`Serie`, `Pause-Übersicht`, `Abgeschlossen`, `Richtig`, `Aktuelle Serie`, `Mittlere Zeit`), and Russian (`Серия`, `Итоги паузы`, `Завершено`, `Правильно`, `Текущая серия`, `Среднее время`).
  5. Comprehensive Verification: 25 targeted unit tests in `SessionStreakAndSummaryTests.cs` (1269 total passing tests across the test suite).
- **Slice 7 Progress (Completed & Validated)**:
  1. Root-Cause Analysis: Confirmed primary white-flash sources are native Android WebView default white canvas and unstyled first-paint of `index.html` with raw `<div id="app">MathFirst</div>`. The brand splash itself (`#176B4D`) is verified healthy and retained as the continuous neutral handoff surface.
  2. Native Android BlazorWebView Handshake: Added Android-specific platform mapping via `BlazorWebViewHandler.Mapper.AppendToMapping("StartupSurfaceBackground", ...)` in `MauiProgram.cs` setting native WebView background color to `#176B4D`, eliminating the white native canvas before HTML first paint without adding permissions or custom WebViewClient.
  3. Static HTML First-Paint Synchronous Styling: Added synchronous inline `<style>` block in `<head>` of `index.html` setting `html, body, #app` to `#176B4D` (width/height 100%), beating browser defaults and external Bootstrap before runtime stylesheets load.
  4. Raw Placeholder Removal: Replaced `<div id="app">MathFirst</div>` with empty container `<div id="app"></div>`, eliminating unstyled plain-text flashes while preserving clean Blazor mounting.
  5. Authoritative Theme Preservation: Kept single source of truth in `ThemeService` / `IPreferenceStore`. Zero localStorage theme duplication, zero JS-bridge races, zero SQLite changes.
  6. Visual Handoff & Physical Verification Boundary: Established continuous visual pipeline: Native Splash (`#176B4D`) -> Native Android WebView (`#176B4D`) -> Static HTML Surface (`#176B4D`) -> Rendered MathFirst UI (Light `#F4F7F5` / Dark `#121916`). Physical hardware verification retained as `STARTUP_WHITE_FLASH_PHYSICAL_VALIDATION_PENDING`.
- **Release Startup Resource Order Fix (Blocker Remediation - Completed & Validated)**:
  1. Root-Cause Analysis: Android Release startup threw `XamlParseException` (`StaticResource not found for key NativeHostBackgroundLight`) because `App` constructor directly depended on `MainPage`, forcing DI to instantiate `MainPage` and execute `MainPage.InitializeComponent()` before `App.InitializeComponent()` populated `Application.Resources`.
  2. Delayed Resolution Architecture: Removed `MainPage` from `App` constructor dependencies and injected `IServiceProvider` instead. `App` constructor executes `InitializeComponent()` first and then `ThemeService.Initialize(this)`. `MainPage` is resolved transiently inside `CreateWindow()` via `_services.GetRequiredService<MainPage>()`.
  3. Transient Lifetime & Invariant Preservation: Preserved `AddTransient<MainPage>()` registration without instance caching; preserved theme initialization sequence; preserved all `#176B4D` startup backgrounds, light (`#F4F7F5`) and dark (`#121916`) host backgrounds, `AppThemeBinding`, and zero localStorage duplication.
  4. Verification: Added 6 architectural contract regression tests in `AppConstructorDependencyContractTests.cs` (1280 total passing Core tests, 0 failed, 0 skipped); Windows Release build 0 warnings/errors; Android Release build 0 warnings/errors.
  5. Physical Verification Boundary: Physical Android validation pending (`PHYSICAL_ANDROID_RELEASE_STARTUP_VALIDATION_PENDING`, `STARTUP_WHITE_FLASH_PHYSICAL_VALIDATION_PENDING`, `PHYSICAL_APP_WEBVIEW_BASELINE_MEASURED: NO`, `PHYSICAL_30_MB_APP_DATA_EXPLAINED: NO`).
- **Tester Ergonomics — Build Identity Metadata Slice (Completed & Validated)**:
  1. Runtime Build-Identity Metadata Model & Parser: Extended `AppBuildMetadata` and `AppBuildInfoMetadataParser` in `MathFirst.Application` to parse `ApplicationId`, `SourceCommit`, and `BuildClassification` alongside canonical `ApplicationTitle`, `DisplayVersion`, and `BuildNumber`.
  2. Fail-Closed Validation & Safety Invariants: Missing or blank `ApplicationId` fails closed (`MISSING_APPLICATION_ID_DOES_NOT_SILENTLY_ASSUME_PRODUCTION_ID: YES`). Missing or blank build classification safely defaults to `Local` and cannot report `Production` (`MISSING_BUILD_CLASSIFICATION_CANNOT_REPORT_PRODUCTION: YES`). Controlled classifications strictly allow `Local`, `Tester`, `SourceCandidate`, and `Production`; unknown non-empty values fail closed.
  3. Source Commit & Short SHA Formatting: Added `MathFirst.SourceCommit` supporting real Git SHAs or deliberate `local` default. `GetShortSourceCommit` produces deterministic 8-character short SHA or preserves `local` without exceptions on short strings.
  4. MSBuild Projection & AppBuildInfo Service: Projected `MathFirst.ApplicationId`, `MathFirst.SourceCommit`, and `MathFirst.BuildClassification` in `MathFirst.App.csproj`. `AppBuildInfo` exposes `ApplicationId`, `SourceCommit`, `ShortSourceCommit`, `BuildClassification`, and `IsTesterBuild` (derived strictly from `BuildClassification == "Tester"`).
  5. Verification: 59 targeted unit tests in `AppBuildInfoMetadataParserTests` and `NativeIdentityContractTests` (1318 total passing Core tests, 0 failed, 0 skipped); Windows Release build 0 warnings/errors; Android Release build 0 warnings/errors.
- **Tester Ergonomics — Diagnostics & Settings UX Slice (Completed & Validated)**:
  1. Safe Deterministic Diagnostic Formatter: Implemented `AppDiagnosticFormatter` in `MathFirst.Application` formatting canonical application title, display version, build number, build classification, short source commit, platform name/version, and application ID with strict exclusion of learner progress, attempt history, FSRS data, database paths, and secrets.
  2. Platform Information & Clipboard Boundaries: Defined `IAppPlatformInfo` and `IClipboardService` in `MathFirst.Application`, implemented `MauiAppPlatformInfo` (normalizing WinUI to Windows and Android to Android) and `MauiClipboardService` in `MathFirst.App.Services`, and registered them via standard singleton DI in `MauiProgram.cs`.
  3. Settings UX & Build Identity: Extended `Settings.razor` footer to display compact build classification and short source commit alongside version/build, added localized "Copy diagnostic info" button with safe async clipboard execution, and localized success/failure feedback status.
  4. EN/DE/RU Localization: Added keys `Settings_Build`, `Settings_Source`, `Settings_CopyDiagnostics`, `Settings_CopyDiagnostics_Success`, `Settings_CopyDiagnostics_Failure` with complete parity across English, German, and Russian dictionaries in `LocalizationService.cs`.
  5. Verification: 114 targeted unit and contract tests in `TesterDiagnosticsContractTests`, `NativeIdentityContractTests`, `PolicyAndLocalizationTests`, and `AppBuildInfoMetadataParserTests`; Windows build 0 warnings/errors; Android build 0 warnings/errors.
- **Next Technical Lifecycle**: `REVIEW_ONLY` — MF-UX-005 Tester Ergonomics — Diagnostics & Settings UX

---

## 2. Authoritative Decisions Registry for MF-UX-005

The following 18 product and UX decisions are authoritative across all subsequent slices and chat transitions:

1. **Answer Submission (Auto-Submit)**: KEEP CURRENT BEHAVIOR. Single-digit canonical answers continue to auto-submit immediately upon entering the digit (e.g. if answer is 4 and user taps 9, it is immediately evaluated as incorrect). Do NOT introduce confirmation buttons, Enter-only submission, grace periods, correction delays, or separate Learning/Sprint input modes. Backspace remains available for partial multi-digit editing only.
2. **`(Enter)` Button Copy**: KEEP CURRENT BEHAVIOR. Retain `(Enter)` on Continue / Keep Going actions to preserve visible external/physical keyboard affordance.
3. **Pause Button Color**: IMPLEMENTED IN SLICE 3. Transitioned Pause away from destructive red / `button-danger` styling to non-destructive amber/yellow styling (`.button-pause`) with accessible contrast in both Light and Dark modes, clearly distinguishing Pause from destructive Reset/Delete actions.
4. **No Time Pressure Mode**: IMPLEMENTED IN SLICE 5. Added Practice Time setting "No Time Pressure" (`NoTimePressure = -1`) where active response latency is measured and persisted uncapped for adaptive fluency and pacing, elapsed time visibly counts up, but there is NO countdown, NO answer deadline, and NO automatic Timeout. Standard adaptive timing (Standard) and fixed floor modes (30s, 45s, 60s) remain fully functional.
5. **Timer Typography**: IMPLEMENTED IN SLICE 3. Removed heavy WordArt-like stroke (`-webkit-text-stroke`) in favor of cleaner bold sans-serif typography with subtle translucent pill backing ensuring accessible contrast across themes and fill color transitions.
6. **Practice Vertical Layout**: NO FORCED COMPRESSION REQUIRED. Numeric keypad remains anchored toward the bottom of the usable Practice area. Normal vertical whitespace between equation and keypad is acceptable. Only genuine clipping/overflow defects should be corrected.
7. **Didactic Tips / Visual Math Explanations**: REJECTED FOR CURRENT PRODUCT SCOPE. Do NOT add zero-rule hints, multiplication mnemonic tips, ten-frame graphics, dots/counters, or per-fact explanations. Canonical equation/result is sufficient.
8. **Error Remediation Spacing**: KEEP CURRENT BEHAVIOR. Retain existing spaced in-session remediation (`LearningPolicy.RemediationInterveningCount = 3`); incorrect facts do not immediately repeat on the consecutive turn.
9. **Repeated-Error Teaching Lock**: IMPLEMENTED IN SLICE 3. On second consecutive error for the exact FactId, TeachingIntervention displays canonical equation with Continue initially disabled for a 3-second visible lockout with localized countdown feedback, acknowledging with zero scoring or learning mutations.
10. **Haptic Feedback**: IMPLEMENTED IN SLICE 4. Provided distinguishable tactile feedback for keypad tap (`Click`), correct answer (`40ms pulse`), and incorrect/timeout (`120ms pulse`); subtle, respects platform capabilities with only normal `VIBRATE` manifest permission, no-ops safely on unsupported platforms, configurable in Settings and Onboarding Step 2, persisted locally, default enabled.
11. **Streak Feedback**: IMPLEMENTED IN SLICE 6. Positive, age-neutral consecutive correct streak feedback without manipulative pressure, fake praise, or learning mutations; session presentation only (visible in HUD when streak $\ge 3$).
12. **Confirmation / Learning Mode**: REJECTED. Do NOT add answer confirmation buttons, checkmark submit buttons, or separate Learning/Sprint modes. Smart auto-submit remains authoritative.
13. **Pause Information**: IMPLEMENTED IN SLICE 6. Lightweight current-session stats on Pause overlay (Completed, Correct, Current streak, Median correct latency) without invented percentages.
14. **Startup White Flash**: IMPLEMENTED IN SLICE 7. Neutral brand-continuity startup handoff (#176B4D native splash -> #176B4D Android WebView canvas -> #176B4D static HTML surface -> first rendered Light/Dark Blazor UI), empty app root container, no localStorage theme duplication, physical verification pending.
15. **Installed Size / App Data**: ACCEPTED INVESTIGATION — PENDING LATER SLICE. Separate analysis of debug vs release APK/AAB payloads, native libraries, WebView runtime, SQLite storage, and cache. Debug APK size is not production evidence.
16. **Tester Ergonomics**: PENDING MF-UX-005 SCOPE. Source/build identity display, easy diagnostic copy, repeatable tester artifacts without release build leaks.
17. **KnownFirst-Style Onboarding Action Layout**: IMPLEMENTED IN SLICE 2. Vertically stacked full-width actions with primary forward action on top and Back below across all 5 steps, with in-session draft selection preservation and no Skip shortcut.
18. **Android Back Navigation**: IMPLEMENTED IN SLICE 2. Application-owned `IAppBackNavigationCoordinator` handling Onboarding steps 2–5 back navigation, Onboarding step 1 safe background pass-through, Settings return to `/` with question/input/timing preservation, and Root Practice safe background pass-through with timing freeze.

---

## 3. Pending Later Slices for MF-UX-005

The following accepted items under `MF-UX-005` remain pending for subsequent implementation slices:

1. **Installed-Size / App-Data Investigation**: APK/AAB package size analysis and runtime app data profiling.
2. **Tester Ergonomics**: Streamlined tester diagnostics and feedback mechanisms.
3. **Physical Android Device Verification**: Physical confirmation of Startup White-Flash elimination, Android system-Back navigation, haptic feel, and cumulative UX behaviors on hardware.

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
