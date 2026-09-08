# MathFirst Verified Project State

This document records stable, verified facts about MathFirst. It excludes transient package workflow state and distinguishes current implementation from accepted target architecture.

---

## 1. Project Identification

- **Project Name**: MathFirst
- **Repository URL**: https://github.com/Tachiguro/MathFirst
- **Default Branch**: `main`
- **License**: Apache License 2.0 (see [LICENSE.txt](../LICENSE.txt))
- **Current Status**: Native V1 development

---

## 2. Supported Architecture and Platform State

- **Supported target architecture**: Android, Web, and Windows remain the confirmed targets. C#/.NET 10, a shared Domain/Application core, MAUI Blazor Hybrid native hosts, and a future standalone Blazor WebAssembly PWA are established by [ADR-0001](decisions/ADR-0001-cross-platform-application-topology-and-stack-baseline.md).
- **Current native implementation**: The MAUI application currently targets and runs on Windows and Android. Shared presentation, learning, timing, input, and Application/Domain logic are present.
- **Web status**: Web remains a supported future target but has no active runtime implementation and is deferred until native Windows/Android V1 work and release readiness are complete.
- **Inactive platforms**: iOS and Mac Catalyst are not active targets.
- **Current priority**: Windows and Android are the active native product priorities; Android is increasingly prioritized for eventual Google Play publication while Windows remains primary.

---

## 3. Current Implemented Learning and Persistence Baseline

- The current runtime implements a finite 418-fact arithmetic catalog: 121 Addition, 66 non-negative triangular Subtraction, 121 Multiplication, and 110 exact Division facts through operand level 10.
- The current progression implementation uses global Addition-to-Subtraction-to-Multiplication-to-Division introduction lockstep, exposure-based turn advancement, a 12-attempt Mixed Checkpoint per level, and terminal mixed practice after level 10.
- Exact facts use stable, presentation-direction-sensitive canonical IDs.
- `FSRS.Core` 1.0.7 is integrated with 95% desired retention, 21 parameters, disabled fuzzing, deterministic per-FactId card identity, and Practice Position virtual time.
- The current learner persistence format is **Schema V4**. It stores attempt history, item learning state, FSRS card state, global progression/checkpoint state, Practice Position, schema version, and optimistic store revision.
- `MathFirst.Infrastructure.Sqlite` owns the concrete native `SqliteLearnerStore` and `Microsoft.Data.Sqlite`; the Application layer owns persistence contracts. Accepted submissions commit attempt, item, FSRS, and progression changes atomically with revision checks.
- Reset Learning Progress clears learner attempts, item state, FSRS state, and progression while preserving UI/onboarding preferences. Full Local Reset additionally restores applicable UI and onboarding preferences. Ready, Pause, and Background-Resume gates are transient and are not stored in the learner schema.

---

## 4. Accepted Target Learning Architecture

[ADR-0003](decisions/ADR-0003-independent-operation-progression-and-open-ended-fact-space.md) accepts the MF-LEARN-001 target architecture:

- independent per-operation band progression;
- a 567-fact exhaustive dense foundation followed by deterministic structured arithmetic families;
- one acquisition-owner band per exact fact;
- representative 16-fact acquisition for structured bands;
- correctness, latency, frontier evidence, and coverage as advancement gates;
- deterministic operation/role scheduling, procedural generation, and lazy materialization;
- Schema V5 progression and nullable per-attempt Practice Position;
- atomic preservation of valid V4 learner evidence without automatic reset;
- removal of the global Mixed Checkpoint and fixed level-10/418-fact acquisition ceiling.

This is an accepted target design, not current runtime behavior. MF-LEARN-001 implementation and Schema V5 migration remain pending.

---

## 5. Durable Delivery Evidence

- MF-AND-001 was merged to `main` through GitHub Pull Request #7 on 2026-09-08, adding the Android V1 runtime and native SQLite layer while preserving Windows behavior.
- Repository history records 178 passing automated unit and simulation tests before the Android V1 package; the merged Android package adds deterministic lifecycle, input, responsive-layout, localization, and SQLite architecture coverage.
- Repository release notes record a completed real-device Android spot-check for responsive layout, keypad/native IME behavior, Ready/Pause/Resume, smart auto-submit, and Incorrect/Timeout feedback context.
- These facts are historical delivery evidence, not a claim that MF-LEARN-001 or Schema V5 has been tested or implemented.

---

## 6. Durable Product Boundaries

- Core practice is offline-first and requires no account.
- Correctness and response latency are separate learning evidence.
- Negative subtraction, division with remainder, cloud synchronization/accounts, export/import, and larger-than-`Int32` arithmetic remain deferred.
- Packaging, signing, store publication, and deployment require separate authorized lifecycle work.
