# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: None (No active implementation package). The current operational lifecycle is the post-MF-TELEM-001 documentation reconciliation (`DOCUMENT_ONLY`), to be followed by independent `REVIEW_ONLY`. Preceding package `MF-TELEM-001` (Tester Telemetry Export and Share) is complete and merged into `main` via PR #56 at merge commit `bbdf62652927efa26475a9f2d83778de6465f5e1`.
- **Current Lifecycle**: `DOCUMENT_ONLY` (reconciling operational, architectural, persistence, roadmap, bootstrap, and changelog baseline after PR #56 merge).
- **Status of Active Work**: Implementation package `MF-TELEM-001` is complete, reviewed, verified, merged, and synchronized. There is currently no active feature or implementation branch in flight.
  - **Preceding Merged Package (MF-TELEM-001)**: Delivered Schema V7 persistence with five nullable attempt presentation-context columns, pseudonymous persistent random installation UUID, complete-history JSON telemetry export contract (`telemetry_export_schema_v1`), native platform sharing via MAUI `Share.Default.RequestAsync` and sandboxed Android FileProvider (`telemetry-share`), Full Local Reset cleanup of telemetry share cache and installation UUID regeneration, localized Settings export and full reset workflows, and explicit non-interference regression coverage. Post-merge validation: 1,906 Core tests passed (0 failed, 0 skipped), 0 compiler warnings/errors on Android and Windows builds.
  - **Preserved Unallocated Topics**: Adaptive Timing and Early Calibration Redesign (`DESIGN_REQUIRED` — separate future redesign informed by empirical telemetry evidence collected from MF-TELEM-001), and Light-Theme Cyber Defense Visual Reconciliation (`DEFERRED`).
- **Next Downstream Technical Lifecycle**: Final V1 Gap Audit (Roadmap Step 51) under `PLAN_ONLY` mode, to be executed in a separately authorized task after this documentation reconciliation is reviewed and integrated.

### 1.1 MF-TELEM-001 Merged Package Summary

The completed package merged on `main` via PR #56 (`bbdf62652927efa26475a9f2d83778de6465f5e1`) delivers:
1. **Schema V7 Persistence Enrichment**: Enriches SQLite `attempt_history` table with five nullable presentation-context columns (`attempt_context_version`, `presented_deadline_ms`, `expected_pace_ms`, `resolved_role`, `operation_band_before`), committed atomically with attempt evaluation while losslessly preserving historical attempts with NULL context;
2. **Pseudonymous Persistent Installation ID**: Random persistent UUID generated and stored in application preferences, supporting cross-session export correlation without user, account, device, hardware, or location identification;
3. **Canonical JSON Telemetry Export**: Implements `telemetry_export_schema_v1` serializing complete attempt history in deterministic ordering (`practice_position ASC` for positioned attempts; `timestamp ASC, submission_id ASC` for unpositioned attempts) with 15 privacy-filtered fields;
4. **Platform-Native Sharing**: Dispatches export file via MAUI `Share.Default.RequestAsync` and restricts Android `FileProvider` paths strictly to sandboxed cache (`telemetry-share`);
5. **Full Local Reset Cleanup**: Purges cached telemetry share files and regenerates the pseudonymous installation UUID upon Full Local Reset;
6. **Localized UI Integration**: Integrates export and reset workflows into Settings with English, German, and Russian localizations;
7. **Strict Learning Non-Interference**: Non-interference regression coverage confirms zero alteration to FSRS-6, item states, progression rules, pace calibration readiness, the 482 strong-learner benchmark, or Cyber Defense Critical Hit mechanics;
8. **Privacy Boundaries**: No server telemetry upload, no analytics SDK, no background transmission, no telemetry import/restore, and no combat telemetry exported.

### 1.2 Current Verified Quality State

- **Complete-Package Review**: `REVIEW_PASS` (`MF-TELEM-001` completed with zero Blocker, zero Major, zero Minor findings).
- **Merge Commit**: `bbdf62652927efa26475a9f2d83778de6465f5e1` (PR #56).
- **Post-Merge Verification**: `POST_MERGE_SYNC_COMPLETE` on canonical checkout `C:\Dev\MathFirst`; post-merge Core tests: `1,906 passed, 0 failed, 0 skipped`; Android and Windows builds: `0 warnings, 0 errors`.
- **Benchmark & Determinism**: Exact 482 strong-learner benchmark (`ADD-D10 -> ADD-P1-ANCHOR` at global accepted attempt 482; 121 Addition attempts), real-SQLite restart equivalence, and cold-restart next-selection determinism locked.
- **Persistence Contract**: Schema V7 live.

---

## 2. Historical Merged Implementation Packages

- **MF-UX-008 (Static Combat Layout & Boss Presentation)**: Merged via PR #55 (`76116d11b8563b0407188ba53ccefd998eda958d`). Delivered static combat layout positional stability (keypad and arithmetic typography stationary across combat transitions), layout-isolated boss presentation (`clamp(90px, 16vh, 140px)`), visual layering (`z-index: 4` + scrim protection), progressive opponent scaling (0.65 to 1.50), tier-preserving feedback scale composition (`SCALE_PRESERVED_ACROSS_ALL_STATES`), and scoped active-gameplay scroll suppression (`.training-host.active-gameplay`). Post-merge validation: 1,786 Core tests passed.
- **MF-LEARN-006 (Adaptive Learning Policy & 482 Benchmark)**: Merged via PR #54 (`bc7471b098e2f79262ff6e71302820bd281a14d5`). Delivered Option-B Evidence-Adaptive Discovery, universal no-immediate-fact-repetition invariant ($\text{FactId}(t+1) \ne \text{FactId}(t)$), tiered remediation (cooldown 4 / 2; broad weakness threshold 2), Guided Gate G3 soft decoupling at `BandIndex >= 3`, pace calibration readiness at $\ge 24$ positioned Correct attempts, calibrated Cyber Defense Critical Hits, and the exact 482 strong-learner benchmark. Post-merge validation: 1,772 Core tests passed.
- **MF-UX-007 (Progress Presentation Cleanup)**: Merged via PR #47 (`d37fbe3347679220bf847b06c83f7f9366738d03`). Delivered concise learner-facing Stage terminology, returning learner Ready Gate overview, active HUD accessibility (`Training_OperationProgressGroupAriaLabel`), and responsive layout preservation. Post-merge validation: 1,605 Core tests passed.

---

## 3. Downstream Release Context & Planned Sequence

### Release Context:
- **Build 2 Rejection**: Historical. Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to the selector crash/starvation bug on operation reconfiguration and restart, resolved by `MF-STAB-003`.
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, `MF-LEARN-006`, `MF-UX-008`, and `MF-TELEM-001` and no longer represents current repository source.
- **Future Production Candidate**:
  - Any future production candidate packaging after package merges will have `versionCode >= 4`.
  - Build 4 does **not** exist yet (not packaged, not signed, not tested).

### Authorized Downstream Project Sequence:
1. Documentation reconciliation for `MF-TELEM-001` is completed under `DOCUMENT_ONLY` mode and submitted for `REVIEW_ONLY`.
2. Following review and integration of documentation reconciliation, the next technical activity is **Roadmap Step 51: Final V1 Gap Audit** (`PLAN_ONLY`, separately authorized).
3. Downstream release preparation sequence (fresh Tester APK build, manual physical-device tester validation on Samsung Galaxy S26 Ultra, production packaging `versionCode >= 4`, Steps 52–58) remains deferred until explicitly authorized.

> [!IMPORTANT]
> Package `MF-TELEM-001` is complete and merged into `main`. Downstream release preparation, the Final V1 Gap Audit, or future design work must not be autonomously activated without explicit user dispatch.
