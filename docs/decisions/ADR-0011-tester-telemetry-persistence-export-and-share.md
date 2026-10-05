# ADR-0011: Tester Telemetry Persistence, Export, and Share

## Status

Accepted

## Date

2026-09-28

## Context

Following the completion of the Adaptive Learning Policy in [ADR-0010](ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md) and the stabilization of presentation and combat layouts in MF-UX-008, MathFirst requires empirical, machine-analyzable learning evidence from real human testers across diverse age groups (e.g. young children aged four to six, fast adult learners) to inform the planned **Adaptive Timing and Early Calibration Redesign**.

However, three primary architectural and operational constraints exist:
1. **Schema V6 Lacks Presentation-Time Context**:
   The current SQLite database schema (Schema V6) records the evaluated outcome (`outcome`, `is_fluent`, `response_latency_ms`, `submitted_answer`) and global `practice_position` in `attempt_history`, but discards presentation-time context. Specifically, the presented countdown deadline, the expected pace estimate $P_{\text{fact}}$ at presentation time, the selector's resolved role (`New`, `Due`, `Maintenance`, `Frontier`, `Remediation`, `EarlyReview`), and the operation progression band index at presentation time are transiently computed in memory and lost upon attempt submission. A pure read-only export of Schema V6 is insufficient for analyzing whether deadlines were appropriately calibrated, whether the learner experienced time pressure, or why a specific fact was chosen.
2. **Offline-First & Strict Zero-Telemetry Invariant**:
   MathFirst adheres to a strict privacy invariant: the application operates locally without user accounts, contains no remote analytics backend or telemetry SDK, and requests zero network permissions (`android.permission.INTERNET`, `android.permission.ACCESS_NETWORK_STATE`). Telemetry transmission cannot happen automatically or in the background.
3. **Sandbox & Platform Share Security**:
   To export local data on modern mobile operating systems (specifically Android), data must be transferred securely through the platform share sheet using Android `FileProvider`. Sharing must not expose the internal SQLite database, the app data sandbox, or arbitrary cache folders to other applications.

---

## Decision

MathFirst adopts a unified, privacy-preserving persistence, export, and platform-sharing architecture for tester telemetry:

### 1. Atomic Persistence Enrichment via Schema V7
MathFirst rejects sidecar databases and dual-write event logs. The canonical `attempt_history` table in SQLite is enriched with five new nullable columns in **Schema V7**:
- `attempt_context_version INTEGER NULL`
- `presented_deadline_ms INTEGER NULL`
- `expected_pace_ms INTEGER NULL`
- `resolved_role TEXT NULL`
- `operation_band_before INTEGER NULL`

Presentation context is captured in memory at the exact moment an exercise is presented and committed atomically in the same SQLite transaction that records the evaluated attempt, updates `item_learning_state`, updates `fsrs_card_state`, and advances `operation_progression`.

### 2. Lossless Legacy Compatibility and Context Versioning
- **`attempt_context_version IS NULL`**: Represents legacy attempts where presentation context was not recorded. This applies to both pre-V5 unpositioned attempts and V5/V6 positioned attempts. Existing historical rows receive `NULL` and are **never** backfilled with synthetic context.
- **`attempt_context_version = 1`**: Represents Enriched Attempt Context v1 recorded under Schema V7.
- **No Time Pressure Representation**: When No Time Pressure is active, `attempt_context_version = 1` and `presented_deadline_ms IS NULL`.
- `practice_position` and `attempt_context_version` remain orthogonal concepts.

### 3. Canonical JSON Export Contract (`telemetry_export_schema_v1`)
The export file is a single UTF-8 JSON document containing metadata and a flat array of attempts.
- **Included Fields (15)**: `fact_id`, `operation`, `left_operand`, `right_operand`, `submitted_answer`, `outcome`, `is_fluent`, `response_latency_ms`, `timestamp`, `practice_position`, `context_version`, `presented_deadline_ms`, `expected_pace_ms`, `resolved_role`, `operation_band_before`.
- **Excluded Fields**: Internal UUID `submission_id`, `correct_answer`, `is_correct`, tester names, device/hardware serials, Android IDs, IP addresses, location data, and combat/gamification state.
- **Deterministic Ordering**: Positioned attempts are ordered strictly by `practice_position ASC`. Legacy unpositioned attempts are ordered by `timestamp ASC, submission_id ASC` (internal SQL tie-breaker).

### 4. Pseudonymous Installation Identifier
Each installation lazily generates a persistent random UUID (`installation_id`) stored in application preferences. This identifier allows downstream analysts to correlate sequential export files from the same installation across practice sessions. It is never derived from hardware or user identity. A Full Local Reset explicitly clears and regenerates this identifier.

### 5. Manual User-Initiated Native Share
Export is triggered exclusively by an explicit user tap on "Export & Share Telemetry" in Settings. The application generates the JSON export file into a sandboxed cache directory and dispatches it via .NET MAUI's cross-platform `Share.Default.RequestAsync`.

### 6. Sandboxed Android FileProvider Security
Android `FileProvider` paths are strictly restricted by overriding `Platforms/Android/Resources/xml/microsoft_maui_essentials_fileprovider_file_paths.xml` to expose only:
```xml
<cache-path name="telemetry_share" path="telemetry-share" />
```
The root cache directory, internal app data files, database files (`mathfirst.db`), and external storage are strictly excluded from FileProvider exposure.

### 7. Safe Temporary File Lifecycle & Truthful UI Semantics
- Temporary share files in `CacheDirectory/telemetry-share/` are uniquely named and are **not** immediately deleted upon `Share.RequestAsync` return, nor on app startup, allowing asynchronous target applications (email, messaging) sufficient time to read the file. The operating system reclaims cache files under storage pressure; Full Local Reset purges them.
- MathFirst UI confirms file preparation and system dispatch, but explicitly never claims successful network transmission, preserving truthful UI boundaries.

### 8. Strict Downstream Boundaries
- **Cyber Defense**: Combat mechanics remain downstream presentation consumers; zero combat state is persisted or exported.
- **Import / Restore**: Data import and restore are explicitly out of scope for this package and remain an unentangled future concern.

---

## Alternatives Considered and Rejected

1. **Pure Schema V6 Read-Only Export**:
   - *Rejected*: Schema V6 lacks presentation-time deadlines, expected pace, and resolved role. Exporting V6 data alone would fail the primary product objective of enabling empirical analysis for the Adaptive Timing and Early Calibration Redesign.
2. **Telemetry Sidecar Database or Dual-Write Event Log**:
   - *Rejected*: Maintaining a separate database file or appending to a flat event log introduces distributed transaction failure modes, synchronization overhead, corruption risk, and unnecessary complexity. Atomic storage in `attempt_history` guarantees 100% transactional consistency.
3. **Automatic / Background Telemetry Upload**:
   - *Rejected*: Violates core MathFirst privacy principles and the zero-network-permissions contract. MathFirst will not include network permissions or analytics SDKs.
4. **User-Entered Tester Identity or Demographics**:
   - *Rejected*: Subject to user error, privacy risks, and data-minimization violations. A locally generated random pseudonymous installation UUID provides sufficient correlation across sessions.
5. **Broad FileProvider Cache Access (`<cache-path path="." />`)**:
   - *Rejected*: Exposing the root cache directory violates the principle of least privilege and risks leaking transient application or webview cache data to receiving applications.

---

## Consequences

### Positive
- **Empirical Timing Redesign Foundation**: Provides authentic, machine-analyzable learning telemetry across diverse human learner demographics.
- **Architectural Simplicity**: Atomic SQLite transactions prevent data divergence between learning progress and telemetry evidence.
- **Privacy and Security Compliance**: Zero network permissions, strict data minimization, pseudonymous UUID correlation, and tightly sandboxed FileProvider sharing preserve learner trust.
- **Lossless Evolution**: Historical attempts are 100% preserved with explicit null context, ensuring existing learner databases upgrade smoothly without data loss.

### Neutral
- Schema migration from V6 to V7 requires migration logic and comprehensive test coverage.
- Downstream analysis tooling must handle both legacy rows (`context_version IS NULL`) and enriched rows (`context_version = 1`).

### Negative / Trade-Offs
- Temporary share files occupy a small amount of disk cache until reclaimed by the operating system or cleared by Full Local Reset.

---

## Amendment: Schema V8 Persistence & Telemetry Schema V2 (2026-10-05 / P1b)

### Status
Accepted / Amended

### Context
With the implementation of **P1b (Active Thinking Time / Interruption Safety)**, the learning engine requires durable persistence of empirical interruption facts (`AttemptRecord.IsInterrupted`) to distinguish continuous arithmetic cognitive effort from interrupted attempts without fabricating timeouts or contaminating adaptive pace and fluency models.

### Decision Amendments

1. **Schema V8 Persistence (`attempt_history.is_interrupted`)**:
   - SQLite learner database is upgraded to **Schema V8**.
   - Adds `is_interrupted INTEGER NOT NULL DEFAULT 0 CHECK (is_interrupted IN (0, 1))` to the `attempt_history` table.
   - Migration from V7 to V8 cleanly applies `ALTER TABLE attempt_history ADD COLUMN is_interrupted INTEGER NOT NULL DEFAULT 0 CHECK (is_interrupted IN (0, 1));` with schema version updated to 8.
   - Fresh databases initialize directly at Schema V8.
   - Historical rows default to `0` (`false`).

2. **Telemetry Export Schema V2 (`telemetry_export_schema_v2`)**:
   - The top-level export format increments to `schema_version = 2`.
   - The `attempts` array serialization is enriched from 15 to **16 properties**:
     1. `fact_id` (string)
     2. `operation` (string)
     3. `left_operand` (integer)
     4. `right_operand` (integer)
     5. `submitted_answer` (integer/string)
     6. `outcome` (string)
     7. `is_fluent` (boolean)
     8. `response_latency_ms` (integer)
     9. `timestamp` (ISO-8601 string)
     10. `practice_position` (integer or null)
     11. `context_version` (integer or null)
     12. `presented_deadline_ms` (integer or null)
     13. `expected_pace_ms` (integer or null)
     14. `resolved_role` (string or null)
     15. `operation_band_before` (integer or null)
     16. `is_interrupted` (boolean, defaults to `false` for legacy/uninterrupted rows)
   - `is_timing_eligible` is derived at runtime (`!IsInterrupted`), is **not** separately persisted in SQLite, and is **not** exported in telemetry.
