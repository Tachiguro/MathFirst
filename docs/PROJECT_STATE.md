# MathFirst Verified Project State

This document records stable, verified facts about MathFirst. It excludes transient package workflow state and distinguishes current implementation from accepted target architecture.

---

## 1. Project Identification

- **Project Name**: MathFirst
- **Repository URL**: https://github.com/Tachiguro/MathFirst
- **Default Branch**: `main`
- **License**: Apache License 2.0 (see [LICENSE.txt](../LICENSE.txt))
- **Current Status**: MF-LEARN-001 and MF-UX-002 are complete and merged into `main`. MF-UX-003 is implementation-complete and review-approved on its candidate branch; its delivery lifecycle remains outstanding.

---

## 2. Supported Architecture and Platform State

- **Supported target architecture**: Android, Web, and Windows remain the confirmed targets. C#/.NET 10, a shared Domain/Application core, MAUI Blazor Hybrid native hosts, and a future standalone Blazor WebAssembly PWA are established by [ADR-0001](decisions/ADR-0001-cross-platform-application-topology-and-stack-baseline.md).
- **Current native implementation**: The MAUI application currently targets and runs on Windows and Android. Shared presentation, learning, timing, input, and Application/Domain logic are present.
- **Web status**: Web remains a supported future target but has no active runtime implementation and is deferred until native Windows/Android V1 work and release readiness are complete.
- **Inactive platforms**: iOS and Mac Catalyst are not active targets.
- **Current priority**: Windows and Android are the active native product priorities; Android is increasingly prioritized for eventual Google Play publication while Windows remains primary.

---

## 3. Current Implemented Learning and Persistence Baseline

- The current runtime implements independent progression for Addition, Subtraction, Multiplication, and Division. The dense foundation contains 121 Addition, 121 Subtraction, 169 Multiplication, and 156 Division facts (567 total), followed by structured open-ended bands; the curriculum has no finite total size or permanent level-10 ceiling.
- Operation scheduling and role selection are deterministic, and selection uses bounded evidence windows with lazy fact materialization. The global Mixed Checkpoint and global introduction lockstep are not active runtime concepts.
- Exact facts use stable, presentation-direction-sensitive canonical IDs.
- `FSRS.Core` 1.0.7 is integrated with 95% desired retention, 21 parameters, disabled fuzzing, deterministic per-FactId card identity, and Practice Position virtual time.
- The current learner persistence format is **Schema V5**. It stores global Practice Position and StoreRevision, per-operation progression rows, positioned V5 attempts, item learning state, FSRS card state, and durable history. Valid migrated V4 attempts retain NULL Practice Position.
- `MathFirst.Infrastructure.Sqlite` owns the concrete native `SqliteLearnerStore` and `Microsoft.Data.Sqlite`; the Application layer owns persistence contracts. Accepted submissions commit attempt, item, FSRS, and progression changes atomically with revision checks, idempotent SubmissionId replay, and publish-after-successful-persistence session semantics.
- Reset Learning Progress clears learner attempts, item state, FSRS state, and progression while preserving UI/onboarding preferences. Full Local Reset additionally restores applicable UI and onboarding preferences. Ready, Pause, and Background-Resume gates are transient and are not stored in the learner schema.

---

## 4. Accepted Target Learning Architecture

[ADR-0003](decisions/ADR-0003-independent-operation-progression-and-open-ended-fact-space.md) records the accepted and implemented MF-LEARN-001 architecture:

- independent per-operation band progression;
- a 567-fact exhaustive dense foundation followed by deterministic structured arithmetic families;
- one acquisition-owner band per exact fact;
- representative 16-fact acquisition for structured bands;
- correctness, latency, frontier evidence, and coverage as advancement gates;
- deterministic operation/role scheduling, procedural generation, and lazy materialization;
- Schema V5 progression and nullable per-attempt Practice Position;
- atomic preservation of valid V4 learner evidence without automatic reset;
- removal of the global Mixed Checkpoint and fixed level-10/418-fact acquisition ceiling.

MF-LEARN-001 implementation and review completed, its full validation passed, and the feature branch was merged into `main` through Pull Request #9. Post-merge synchronization completed successfully; Native V1 release-readiness work remains separate.

---

## 5. Native Identity and Version Baseline

- MF-UX-002, **Deterministic Practice Personality and Contextual Copy**, is complete and merged. Its deterministic contextual copy remains presentation-only and does not affect Schema V5, learning-policy semantics, or accepted-attempt behavior.
- MF-UX-003 establishes the canonical V1 identity: application title `MathFirst`, application identifier `com.tachiguro.mathfirst`, display version `1.0`, build `1`, and company `Tachiguro`. Product and assembly titles are projected from the canonical title; Android and unpackaged Windows identity remain unchanged.
- Settings displays localized version/build information. `AppBuildInfo` reads generated application metadata through the shared platform-neutral parser, which requires complete, non-blank, valid numeric display-version metadata and a positive invariantly parsed build number.
- Native branding uses a white geometric MF mark, primary `#176B4D`, companion `#0F523A`, light host background `#F4F7F5`, dark host background `#121916`, and adaptive icon foreground scale `0.65`. The MAUI single-project `MauiIcon`/`MauiSplashScreen` architecture generates native identity assets; manually maintained Android or Windows icon sets are not used.
- Native Not Found content is localized in English, German, and Russian; the pre-Blazor host placeholder is the language-neutral `MathFirst`. The former unused template image/raw payloads were removed.
- Contract coverage verifies canonical project properties and MSBuild projection, metadata parsing including fail-closed invalid inputs, localization and version display, SVG and native-host XAML structure, Android palette/identity, Windows unpackaged identity, removed template payloads, Not Found localization, and the startup identity.

## 6. Candidate Delivery State

- MF-UX-003 is implementation-complete and consolidated-review `REVIEW_APPROVED` at `9a67f387a57261a4d76afa70510fc6e8b1d23447`, based on `main` commit `194b6d6a5a9f11c989bcaaf1468758ff82386bba`. Its documentation has been reconciled locally but not committed; `FULL_VALIDATION`, push, Pull Request, and merge remain pending.
- Focused implementation evidence includes slice results of 50 passed and 64 passed tests, targeted Windows/Android builds with 0 warnings and 0 errors, and final remediation coverage of 26 passed tests with targeted Windows/Android builds at 0 warnings and 0 errors. This is not formal documentation-inclusive `FULL_VALIDATION` evidence.

## 7. Durable Delivery Evidence

- MF-AND-001 was merged to `main` through GitHub Pull Request #7 on 2026-09-08, adding the Android V1 runtime and native SQLite layer while preserving Windows behavior.
- Repository history records 178 passing automated unit and simulation tests before the Android V1 package; the merged Android package adds deterministic lifecycle, input, responsive-layout, localization, and SQLite architecture coverage.
- Repository release notes record a completed real-device Android spot-check for responsive layout, keypad/native IME behavior, Ready/Pause/Resume, smart auto-submit, and Incorrect/Timeout feedback context.
- MF-LEARN-001 completed with `REVIEW_APPROVED`, a full Core suite of 409 passed, 0 failed, and 0 skipped, Windows and Android Release builds with 0 warnings and 0 errors, clean vulnerability and static-regression audits, Pull Request #9 merge, and successful post-merge synchronization.

---

## 8. Durable Product Boundaries

- Core practice is offline-first and requires no account.
- Correctness and response latency are separate learning evidence.
- Negative subtraction, division with remainder, cloud synchronization/accounts, export/import, and larger-than-`Int32` arithmetic remain deferred.
- Packaging, signing, store publication, and deployment require separate authorized lifecycle work.
