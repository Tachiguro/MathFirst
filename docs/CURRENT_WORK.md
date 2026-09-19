# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Most Recently Completed Package**: `MF-UX-005` — Native UX, Responsiveness, and Interaction Polish
- **Active Implementation Package**: None
- **Current Documentation Lifecycle**: `DOCUMENT_ONLY` — final MF-UX-005 reconciliation
- **Documentation Task Branch**: `codex/mf-ux-005-final-doc-reconciliation`
- **Current Merged Baseline**: `d1705bbdc0013372e44eadf7310ff2f313ecdcef` (PR #38 merge)
- **Next Lifecycle for This Documentation Change**: `REVIEW_ONLY`
- **After This Documentation Change Is Eventually Merged**: No active package is selected. Any downstream work requires explicit user authorization.

### MF-UX-005 Delivery History

1. **PR #30 — Slices 1–6** (merge `bde91a7578753f5c468d0534282edd9fd13f32a0`):
   - *Slice 1*: Practice timer rendering isolation/performance and keypad visual reliability.
   - *Slice 2*: KnownFirst-style onboarding action layout and Android Back coordinator.
   - *Slice 3*: 3-second repeated-error teaching dwell lock, amber Pause styling, and the then-current translucent Timer pill.
   - *Slice 4*: Configurable haptic feedback.
   - *Slice 5*: No Time Pressure mode with visible elapsed count-up and no automatic timeout.
   - *Slice 6*: Restrained textual correct streak and transient Pause summary.
2. **PR #31** (merge `15d39ac73ecdc276db2d3f1a9b6ea3ac0f8849b6`): Startup white-flash elimination.
3. **PR #32** (merge `cf1d2a3f4779c16f1c6104fe8736f405eb14d94e`): Android Release startup resource-order fix.
4. **PR #33** (merge `83b767c2265c1baba560abdeaa9fedad365d70da`): Tester build identity metadata.
5. **PR #34** (merge `82c117912f72d6efe16055f8be754c9c577ae15a`): Tester diagnostics and Settings UX.
6. **PR #35** (merge `336322b5386a872ebb726c7bdcf34bb207592650`): Post-PR #34 documentation reconciliation.
7. **PR #36** (merge `e908bf2fba820f4f6adf03b278861b956e5dbbd5`, head `2acfccabb81d814f966a197f0b2d7cb310b9e125`): Repeatable Tester APK workflow through `MathFirst.ReleaseTool` and the PowerShell package/validation entrypoints; historical automated evidence was 1462 passing Core tests.
8. **Investigation milestone**: Installed Size, App Data, and RAM evidence collection completed with the attribution limits recorded in [docs/PROJECT_STATE.md](PROJECT_STATE.md).
9. **PR #37** (merge `dbc6bf045454a78a1ba32793fa02245f83b50437`, head `a06d0b3c2eee184ac0b067d4536b9434c8da644a`): Release-only Bootstrap source-map exclusion; 9/9 targeted packaging tests and 1463/1463 full Release Core tests passed.
10. **PR #38** (merge `d1705bbdc0013372e44eadf7310ff2f313ecdcef`, head `2a7021977571e8c056a4ab2128ddc74e79b0f6b3`): Timer visual remediation superseding the Slice 3 pill treatment; 9/9 targeted visual-contract tests and 1463/1463 full Release Core tests passed.
11. **Physical Timer acceptance**: A Timer-specific `TEST_ONLY` run on Samsung SM-S948B, Android 16, arm64-v8a verified the replacement Tester APK update, preserved existing app/learner data, and the accepted Timer presentation described in [docs/TESTING.md](TESTING.md). This is not final production-candidate certification.

---

## 2. Authoritative Decisions Registry for MF-UX-005

The following product and UX decisions remain authoritative:

1. **Answer Submission (Auto-Submit)**: Keep current behavior. Single-digit canonical answers auto-submit immediately. Do not introduce confirmation buttons, Enter-only submission, grace periods, correction delays, or separate Learning/Sprint input modes. Backspace remains available for partial multi-digit editing only.
2. **`(Enter)` Button Copy**: Keep `(Enter)` on Continue / Keep Going actions for visible external-keyboard affordance.
3. **Pause Button Color**: Slice 3 introduced non-destructive amber/yellow styling (`.button-pause`) with accessible contrast in Light and Dark modes.
4. **No Time Pressure Mode**: Slice 5 added `NoTimePressure = -1`; active response latency is measured and persisted, elapsed time remains visibly counted up on a neutral track, and there is no deadline or automatic Timeout.
5. **Timer Typography**: The translucent backing pill introduced by PR #30 is historical and was superseded by PR #38. Current Timer text uses bold white tabular numerals and a restrained local dark shadow/contour, with no backing pill and no heavy text stroke. Countdown/progress semantics are unchanged.
6. **Practice Vertical Layout**: No forced compression; the keypad remains anchored toward the bottom, and ordinary vertical whitespace is acceptable.
7. **Didactic Tips / Visual Math Explanations**: Rejected for current scope. Do not add zero-rule hints, mnemonic tips, ten-frames, counters, or per-fact explanations.
8. **Error Remediation Spacing**: Keep `LearningPolicy.RemediationInterveningCount = 3`; an incorrect fact does not immediately repeat on the next turn.
9. **Repeated-Error Teaching Lock**: The second consecutive error for the exact `FactId` presents a canonical-equation teaching intervention with a 3-second visible lock before Continue enables and with no scoring or learning mutation on acknowledgement.
10. **Haptic Feedback**: The supported cue contract is exactly `KeyTap`, `Correct`, `Incorrect`, and `Timeout`. It is configurable, persisted locally, enabled by default, and safely no-ops on unsupported platforms.
11. **Streak Feedback**: Restrained textual session feedback only, visible from correct streak $\ge 3$; there is no flame icon or animation.
12. **Confirmation / Learning Mode**: Rejected. Smart auto-submit remains authoritative.
13. **Pause Information**: The Pause overlay presents Completed, Correct, Current streak, and Median correct latency without invented percentages.
14. **Startup White Flash**: PR #31 implemented the neutral brand-continuity startup handoff.
15. **Installed Size / App Data / RAM**: Investigation completed. Package, installed-size, App Data, cache, and RAM findings retain the evidence limitations in [docs/PROJECT_STATE.md](PROJECT_STATE.md); exact internal App Data attribution and current Play delivery size remain unknown.
16. **Tester Ergonomics & Workflow**: Completed through PR #33, PR #34, and PR #36. The repeatable workflow is implemented by `MathFirst.ReleaseTool` plus `scripts/package-android-tester-apk.ps1` and `scripts/validate-android-apk.ps1`.
17. **KnownFirst-Style Onboarding Action Layout**: Vertically stacked full-width actions with primary forward action above Back across all five steps, preserving draft selections and adding no Skip shortcut.
18. **Android Back Navigation**: `IAppBackNavigationCoordinator` handles Onboarding steps 2–5, Onboarding step 1 safe background pass-through, Settings return to `/` with question/input/timing preservation, and Root Practice safe background/pass-through with timing freeze.
19. **Release Startup Resource Order Fix**: PR #32 deferred `MainPage` resolution until `CreateWindow()`.
20. **Release Size Hygiene**: PR #37 excludes only `wwwroot\lib\bootstrap\dist\css\bootstrap.min.css.map` in Release configuration; runtime `bootstrap.min.css` remains available.

---

## 3. Package Completion Boundary

MF-UX-005 implementation and investigation scope is complete. Its physical UX evidence is intentionally narrower than final release certification. It did not create a replacement production AAB, perform production signing, execute the separate `FULL_VALIDATION` lifecycle, complete final release-grade physical validation, or authorize Google Play publication.

---

## 4. Explicit Downstream Roadmap Boundaries

The following stages remain pending and require separate authorization:

1. **Review of This Documentation Change (`REVIEW_ONLY`)**.
2. **Commit/Push/PR Lifecycle for This Documentation Change** under separately authorized operation modes.
3. **Full Validation (`FULL_VALIDATION`)** on an exact candidate HEAD.
4. **Production Packaging & Signing** of a `Distributable` AAB.
5. **Final Release-Grade Physical Validation** of the exact production candidate.
6. **Google Play Gate** and any publication action.

No downstream package is automatically activated by this reconciliation.
