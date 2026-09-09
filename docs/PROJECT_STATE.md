# MathFirst Verified Project State

This document records stable, verified facts about MathFirst. It excludes transient package workflow state and distinguishes current implementation from accepted target architecture.

---

## 1. Project Identification

- **Project Name**: MathFirst
- **Repository URL**: https://github.com/Tachiguro/MathFirst
- **Default Branch**: `main`
- **License**: Apache License 2.0 (see [LICENSE.txt](../LICENSE.txt))
- **Current Status**: Native V1 implementation complete for MF-LEARN-001 on the local feature branch; delivery remains unpushed and unmerged.

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

The implementation and review are complete on the local feature branch. The branch remains local-only pending documentation commit, full validation, push, Pull Request, manual merge, and post-merge synchronization.

---

## 5. Durable Delivery Evidence

- MF-AND-001 was merged to `main` through GitHub Pull Request #7 on 2026-09-08, adding the Android V1 runtime and native SQLite layer while preserving Windows behavior.
- Repository history records 178 passing automated unit and simulation tests before the Android V1 package; the merged Android package adds deterministic lifecycle, input, responsive-layout, localization, and SQLite architecture coverage.
- Repository release notes record a completed real-device Android spot-check for responsive layout, keypad/native IME behavior, Ready/Pause/Resume, smart auto-submit, and Incorrect/Timeout feedback context.
- MF-LEARN-001 validation evidence includes a passing full Core suite, Windows and Android Release builds with zero warnings/errors, and final review approval. Exact checkpoint counts remain in the implementation evidence and task history rather than being repeated throughout this durable project-state summary.

---

## 6. Durable Product Boundaries

- Core practice is offline-first and requires no account.
- Correctness and response latency are separate learning evidence.
- Negative subtraction, division with remainder, cloud synchronization/accounts, export/import, and larger-than-`Int32` arithmetic remain deferred.
- Packaging, signing, store publication, and deployment require separate authorized lifecycle work.
