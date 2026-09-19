# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Work Package**: `MF-UX-005` — Native UX, Responsiveness, and Interaction Polish
- **Active Task**: `MF-UX-005 — Repeatable Tester Artifact / APK Workflow` Post-Implementation Documentation Reconciliation
- **Current Operation Mode**: `DOCUMENT_ONLY` (documentation reconciliation for completed and reviewed 4-slice Tester APK workflow)
- **Active Task Branch**: `feat/mf-ux-005-tester-apk-workflow` (candidate HEAD `22665bc06199b6f8c8fc7f2d11975d3440cac292` during review; documentation unstaged in working tree)
- **Baseline Commit**: `336322b5386a872ebb726c7bdcf34bb207592650` (synchronized `main` post-PR #35)
- **Completed & Reviewed MF-UX-005 Deliverables**:
  1. **Slices 1–6 (Native UX Polish Baseline — PR #30, Merge `bde91a7578753f5c468d0534282edd9fd13f32a0`)**:
     - *Slice 1 (Practice performance & keypad reliability)*: Isolated timer presentation into child component at ~10 Hz; removed preference reads from timer render hot path; eliminated sticky touch hover in Android WebView; keyed keypad container per fact revision for clean button reset.
     - *Slice 2 (Onboarding layout & Android Back coordinator)*: Adopted KnownFirst-style vertically stacked full-width onboarding action layout preserving 5-step flow and draft selections; implemented application-owned `IAppBackNavigationCoordinator` handling Android system Back across Onboarding (steps 2–5 back, step 1 background pass-through), Settings (return to `/` preserving question/input/timing), and Root Practice (background pass-through with timing freeze).
     - *Slice 3 (Teaching dwell lock & typography)*: Implemented 3-second visible lockout with localized countdown on repeated-error teaching dialog; transitioned Pause button to non-destructive amber/yellow palette (`.button-pause`); cleaned up timer typography with translucent pill backing.
     - *Slice 4 (Configurable haptic feedback)*: Added `IHapticFeedbackService` abstraction and `MauiHapticDriver` utilizing normal `VIBRATE` permission (`KeyTap`, `Correct`, `Incorrect`, `Timeout` cues); added persisted preference in Settings and Onboarding Step 2; preserved on reset workflows.
     - *Slice 5 (No Time Pressure mode)*: Extended `PracticeTimeSetting` with `NoTimePressure = -1`, measuring active response latency uncapped for adaptive pace/FSRS while disabling deadlines and timeouts, displaying count-up elapsed time, with full EN/DE/RU localization.
     - *Slice 6 (Streak feedback & Pause summary)*: Added restrained textual streak indicator ($\ge 3$) and truthful transient Pause session summary (Completed, Correct, Streak, Median correct latency) without SQLite schema changes.
  2. **Slice 7 (Startup White-Flash Elimination — PR #31, Merge `15d39ac73ecdc276db2d3f1a9b6ea3ac0f8849b6`)**:
     - Configured Android BlazorWebView platform view startup background color as `#176B4D`;
     - Added synchronous inline first-paint background styling in `index.html`;
     - Removed raw unstyled `<div id="app">MathFirst</div>` placeholder;
     - Preserved single source of truth in `ThemeService` without `localStorage` theme duplication.
  3. **Blocker Remediation (Android Release Startup Resource Order Fix — PR #32, Merge `cf1d2a3f4779c16f1c6104fe8736f405eb14d94e`)**:
     - Deferred `MainPage` resolution until `CreateWindow()`, ensuring `App.InitializeComponent()` loads `Application.Resources` before `MainPage` static resource resolution, eliminating startup `XamlParseException`.
  4. **Tester Ergonomics — Slice A: Build Identity Metadata (PR #33, Merge `83b767c2265c1baba560abdeaa9fedad365d70da`)**:
     - Extended runtime build metadata model and parser with `ApplicationId`, `SourceCommit`, and explicit fail-closed `BuildClassification` (`Local`, `Tester`, `SourceCandidate`, `Production`);
     - Added deterministic short SHA formatting (`GetShortSourceCommit`);
     - Projected build metadata via MSBuild in `MathFirst.App.csproj` and integrated into `AppBuildInfo`.
  5. **Tester Ergonomics — Slice B: Diagnostics & Settings UX (PR #34, Merge `82c117912f72d6efe16055f8be754c9c577ae15a`)**:
     - Implemented deterministic support-safe `AppDiagnosticFormatter` (excluding learner data, attempt history, FSRS state, database paths, and secrets);
     - Defined `IAppPlatformInfo` and `IClipboardService` application abstractions with MAUI implementations;
     - Extended `Settings.razor` footer with compact build classification, short source commit, and localized "Copy diagnostic info" action with async execution and feedback status;
     - Full English, German, and Russian localization parity.
  6. **Repeatable Tester Artifact / APK Workflow (Slices 1–4 — Implemented & Reviewed on `feat/mf-ux-005-tester-apk-workflow`)**:
     - *Slice 1 (`248fc1c`)*: Tester APK profile foundation (`ReleaseProfile.Tester`, `ArtifactWorkspace` routing, deterministic naming `MathFirst-Tester-v{DisplayVersion}-b{BuildNumber}-{ShortCommit}-tester.apk`, MSBuild property matrix `ApplicationId=com.tachiguro.mathfirst.tester`, `BuildClassification=Tester`, `MathFirstSourceCommit=<SHA>`, `AndroidKeyStore=false`, debug signing, clean branch/working tree validation).
     - *Slice 2 (`a79738c`)*: Authoritative offline APK validation (`AndroidApkValidator` using `apksigner`, `aapt2 dump xmltree`, `dexdump -f`, manifest security rules, zero network permissions, `debuggable != true`, backup rules, leaf development-debug certificate policy).
     - *Slice 3 (`97edb4f`)*: Validation receipt schema v1 with `Tester` profile, deterministic `TESTER_README.md`, profile-aware `SHA256SUMS`, and atomic promotion of 5-file evidence bundle.
     - *Slice 4 (`22665bc`)*: `AndroidPackageCommand` wiring for `--profile Tester`, CLI surface enablement, and PowerShell entrypoints `scripts/package-android-tester-apk.ps1` and `scripts/validate-android-apk.ps1`.
     - *Review Baseline*: `REVIEW_PASS` (Findings: `NONE`), 1462 automated tests passed in `MathFirst.Core.Tests`, ReleaseTool build clean.
- **Next Technical Lifecycle**: `COMMIT_ONLY` (committing documentation reconciliation changes for Tester APK workflow).

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
10. **Haptic Feedback**: IMPLEMENTED IN SLICE 4. Provided distinguishable tactile feedback for keypad tap (`Click`), correct answer (`40ms pulse`), and incorrect/timeout (`120ms pulse`); subtle, respects platform capabilities with only normal `VIBRATE` manifest permission, no-ops safely on unsupported platforms, configurable in Settings and Onboarding Step 2, persisted locally, default enabled. Physical tactile-quality verification remains pending on Android hardware.
11. **Streak Feedback**: IMPLEMENTED IN SLICE 6. Positive, age-neutral consecutive correct streak feedback without manipulative pressure, fake praise, or learning mutations; session presentation only (visible in HUD when streak $\ge 3$).
12. **Confirmation / Learning Mode**: REJECTED. Do NOT add answer confirmation buttons, checkmark submit buttons, or separate Learning/Sprint modes. Smart auto-submit remains authoritative.
13. **Pause Information**: IMPLEMENTED IN SLICE 6. Lightweight current-session stats on Pause overlay (Completed, Correct, Current streak, Median correct latency) without invented percentages.
14. **Startup White Flash**: IMPLEMENTED IN SLICE 7. Neutral brand-continuity startup handoff (#176B4D native splash -> #176B4D Android WebView canvas -> #176B4D static HTML surface -> first rendered Light/Dark Blazor UI), empty app root container, no localStorage theme duplication, physical verification pending.
15. **Installed Size / App Data**: ACCEPTED INVESTIGATION — PENDING LATER SLICE. Separate analysis of debug vs release APK/AAB payloads, native libraries, WebView runtime, SQLite storage, and cache. Debug APK size is not production evidence.
16. **Tester Ergonomics & Workflow**: FULLY IMPLEMENTED & REVIEWED. Completed: runtime build identity metadata (PR #33), safe copyable diagnostics / Settings UX (PR #34), and repeatable tester artifact / APK packaging workflow (Slices 1–4 on `feat/mf-ux-005-tester-apk-workflow`).
17. **KnownFirst-Style Onboarding Action Layout**: IMPLEMENTED IN SLICE 2. Vertically stacked full-width actions with primary forward action on top and Back below across all 5 steps, with in-session draft selection preservation and no Skip shortcut.
18. **Android Back Navigation**: IMPLEMENTED IN SLICE 2. Application-owned `IAppBackNavigationCoordinator` handling Onboarding steps 2–5 back navigation, Onboarding step 1 safe background pass-through, Settings return to `/` with question/input/timing preservation, and Root Practice safe background pass-through with timing freeze.

---

## 3. Pending Later Slices for MF-UX-005

The following accepted items under `MF-UX-005` remain pending for subsequent implementation dispatches:

1. **Installed-Size / App-Data Investigation**: APK/AAB package size analysis, native library overhead, Blazor/WebView runtime footprints, and runtime app data profiling.
2. **Physical Android Device Verification**: Physical confirmation of Startup White-Flash elimination, Android system-Back navigation, haptic tactile feel, native clipboard diagnostic copying, and cumulative UX behaviors on hardware.

---

## 4. Explicit Downstream Roadmap Boundaries

The following stages are strictly sequential and require separate authorization:

1. **Commitment of Tester APK Documentation (`COMMIT_ONLY`)**: Commit the reconciled documentation changes on `feat/mf-ux-005-tester-apk-workflow`.
2. **Full Validation (`FULL_VALIDATION`)**: Execution of automated regression and verification suites on exact candidate HEAD.
3. **Pull Request & Explicit Merge Authorization**: Dedicated PR to `main` with affirmative user approval.
4. **Subsequent MF-UX-005 Slices**: Iterative implementation of remaining UX polish/investigation slices (Installed Size analysis, Physical Android verification) under separate dispatches.
5. **Comprehensive Review (`REVIEW_ONLY`)**: Package-wide review of completed MF-UX-005 scope.
6. **Production Packaging & Signing**: Creation of a production `Distributable` AAB package signed with release credentials.
7. **Physical Android Technical Smoke Verification**: Verification on clean physical hardware.
8. **Manual Physical-Device Functional Verification**: Verifying curriculum boundaries and UX polish in manual practice.
9. **Google Play Publication Gate**: Separate decision on store upload.
