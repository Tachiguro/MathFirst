# ADR-0002: Offline Execution and Local Persistence Boundary

## Status

Accepted

## Date

2026-09-07

## Context

[MathFirst's product definition](../PRODUCT.md) requires support for three mandatory target platforms: Android, Web, and Windows. The core learning loop, practice sessions, response evaluation, and progression must operate 100% offline. The Minimum Viable Product (MVP) requires no user account, registration, or remote server authentication; all learner progress must persist reliably in local device storage across application restarts, browser refreshes, and device reboots.

[ADR-0001](ADR-0001-cross-platform-application-topology-and-stack-baseline.md) established the application topology: a shared, platform-independent Domain and Application core with platform-specific hosts (Blazor WebAssembly PWA for Web, .NET MAUI Blazor Hybrid for Android and Windows) and explicit platform adapters.

The persistence architecture must address distinct runtime environments:
- **Web browsers**: Origin-scoped, asynchronous, quota-constrained, browser-managed lifecycle, with vulnerability to eviction or user data clearance.
- **Native OS platforms (Android, Windows)**: Direct filesystem and embedded database access with OS process lifecycles and background suspension.

In addition, learning evaluation in MathFirst depends on two dimensions: mathematical correctness and response latency. Persistence operations must never distort measured recall speed. The initial Thin Vertical Slice may focus on Addition, and the codebase must remain maintainable by a small or solo engineering team.

This architecture decision evaluates the persistence ownership boundary, logical state model, transactional invariants, concurrency control, versioning, failure semantics, and concrete technology candidate strategy specifically for MathFirst's requirements, independent of any preceding projects.

## Decision

### Persistence Ownership Boundary

MathFirst adopts a capability-oriented transactional persistence boundary with strict layer isolation:

1. **Domain Layer**:
   - Owns arithmetic models, learning entities, and mathematical invariants.
   - Remains completely persistence-unaware and storage-agnostic.
2. **Application Layer**:
   - Owns the capability-oriented persistence contract required by application use cases.
   - Orchestrates loading versioned learner state and committing atomic semantic submissions.
   - Understands and handles explicit persistence outcomes (e.g., success, revision conflict, storage unavailable, data corruption, unsupported version).
3. **Platform Adapters**:
   - Implement the Application layer's persistence contract using platform-appropriate storage mechanisms.
   - Own concrete browser, operating system, database, and filesystem integration.

Shared Domain and Application code must not depend directly on IndexedDB, JavaScript interop, Origin Private File System (OPFS), SQLite, filesystem APIs, .NET MAUI storage APIs, browser DOM APIs, Android APIs, Windows APIs, or database-specific libraries.

### Capability-Oriented Persistence Contract

The Application layer defines a use-case-oriented persistence capability rather than generic CRUD-style entity repositories. The boundary conceptually supports:

- Loading versioned learner state and history evidence;
- Committing a complete semantic-submission change set as one atomic unit;
- Enforcing expected-revision concurrency tokens;
- Returning explicit, strongly typed persistence outcomes.

Final C# interface signatures, DTOs, and serialization classes are not defined in this ADR and will be implemented during subsequent development phases.

### Logical Persisted State Model

The logical persisted data model encompasses:

- **Stable learning-item identity**: Unique and persistent reference to learnable arithmetic facts (e.g., presentation-direction-specific items).
- **Current learner/item state**: Derived recall strength, interval/review markers, and mastery indicators needed for active session queue composition.
- **Policy-neutral attempt evidence**: Historical record of individual practice attempts, preserving raw inputs, captured correctness outcomes, captured response latencies, and ordering/timestamp evidence required by future scheduling algorithms.
- **Progression state**: Active working number ranges, operation unlocks, and milestone markers affected by completed submissions.
- **Persistence-format version**: Explicit schema/data format integer for migration and compatibility detection.
- **Store revision / concurrency token**: Monotonic or token-based revision marker for optimistic concurrency control.
- **Stable submission/attempt identity**: Unique identifier for submission operations to support idempotency and retry deduplication.

This architecture persists both current derived state and raw historical attempt evidence, without adopting snapshot-only persistence or full event sourcing. Detailed physical tables, object stores, column types, indexes, and serialized formats are not defined here.

### Atomic Semantic-Submission Invariant

One accepted learner semantic submission corresponds strictly to **one atomic durable change set**.

A semantic-submission change set includes:
1. The new attempt/history evidence record;
2. Resulting updates to the specific item's learning state;
3. Any progression or range modifications triggered by the submission.

No partial authoritative state is permissible:
- If a persistence operation fails or is interrupted, no partial new submission state may be exposed or considered authoritative.
- The previous durable state remains authoritative until the entire change set commit succeeds.
- The Application layer must not treat a new submission state as committed until the platform adapter confirms durable persistence.

### Concurrency and Stale-Write Protection

Silent last-write-wins is prohibited.

- **Optimistic Concurrency**: The persistence contract enforces optimistic revision checking using an expected-revision token.
- **Stale Write Rejection**: If the store revision has advanced beyond the caller's expected revision (e.g., from concurrent tabs in Web or rapid overlapping operations), the update must fail explicitly with a revision conflict outcome.
- **Host-Level Serialization**: Host-level locks or browser origin locks (such as the Web Locks API) may be utilized by adapters as an operational optimization to minimize contention, but they do not replace the fundamental expected-revision contract.

### Format Versioning and Migration Boundary

Persisted state has an explicit format version from its first deployed representation:

- Adapters must inspect and detect the format version prior to loading or mutating data.
- If an unsupported or newer format version is encountered, the adapter must reject normal writable operations and must not silently mutate or downgrade the data.
- Upward migration capability is an architectural responsibility of the platform adapter layer.
- Concrete production migration routines are deferred until actual schema evolution is required.

### Cross-Platform Storage Policy

MathFirst establishes:
- One logical persistence contract;
- One unified set of architectural invariants (atomicity, revision checking, versioning, failure semantics);
- One reusable adapter-conformance test contract.

MathFirst does **not** mandate a single physical storage engine across Web, Android, and Windows. Adapters may utilize storage engines optimal for their respective host platforms.

### Offline Execution and Storage Separation

A strict distinction is maintained between application asset caching and learner data persistence:

- **Web (Blazor WebAssembly PWA)**: Offline application execution requires prior acquisition and caching of published application assets (HTML, WebAssembly binaries, JS, CSS) via the PWA service worker. The service worker is responsible solely for asset caching and does not manage learner data persistence.
- **Native (Android / Windows MAUI Blazor Hybrid)**: Application binaries are locally available upon installation.
- **Learner-Data Persistence**: Handled exclusively through the platform persistence adapter on local device storage, completely independent of service worker cache lifecycles.

### Browser Durability Policy

Browser-local storage constraints are recognized honestly:
- Browser persistence is origin-scoped and managed by the host browser.
- Data may be lost if the user explicitly clears site data/cookies or if the browser evicts origin storage under host device storage pressure.
- The Web adapter may request persistent storage permissions (`navigator.storage.persist()`) where available to mitigate eviction risk, but approval is not guaranteed.
- MathFirst cannot guarantee permanent browser-local durability against user-initiated clearing or browser eviction without a future export/import or synchronization mechanism.

### Timing Separation

Persistence I/O must never distort measured learner recall speed:
- Measured response latency is captured at the moment of learner input submission, before any persistence I/O, serialization, or adapter dispatch begins.
- Persistence write duration must not be added to or included in the learner's recorded recall latency.

### Learning and Scheduler Independence

The persistence model stores policy-neutral attempt evidence and generic state markers. It does not couple the storage architecture to:
- Specific scheduler mathematics or algorithms (such as FSRS or SM-2);
- Concrete mastery or fluency latency thresholds;
- Specific number-range expansion curves or unlock formulas.

## Architectural Invariants

1. **Complete Domain Independence**: The Domain model has zero dependencies on persistence mechanisms, DTOs, or storage frameworks.
2. **One Submission = One Atomic Change Set**: History evidence, item state updates, and progression adjustments commit together atomically or fail completely.
3. **No Silent Overwrites**: Optimistic revision checks prevent silent last-write-wins race conditions.
4. **Explicit Format Versioning**: Persisted state carries an explicit version token; newer/unsupported versions fail fast.
5. **Separation of Timing from I/O**: Recall latency measurement is isolated from persistence operations.
6. **Explicit Failure Outcomes**: Persistence failures return strongly typed failure results; the application never silently falls back to volatile state as authoritative.

## Failure Semantics

Platform persistence adapters and the Application layer must exhibit deterministic architectural behavior under error conditions:

| Failure Condition | Architectural Requirement |
|---|---|
| **Storage Unavailable** | Return an explicit unavailable outcome; never claim successful durable persistence. |
| **Quota / Disk Full** | Fail the entire submission transaction atomically; retain the prior committed state as authoritative. |
| **Transaction Failure** | Roll back all uncommitted changes; expose no partial submission state. |
| **Data Corruption** | Refuse normal operation; expose an explicit corrupted outcome; do not automatically or silently overwrite with a blank store without explicit user direction. |
| **Unsupported / Newer Version** | Refuse read/write operations; prevent silent data downgrades or corruption. |
| **Interrupted Write / Crash** | On restart/reopen, recover cleanly to the state before the interrupted transaction or the fully committed new state; never a partial/mixed record. |
| **Concurrent Stale Update** | Reject the operation with an explicit revision conflict outcome. |
| **Browser Eviction / Data Cleared** | Detect empty/reset origin storage cleanly; treat as a fresh store initialization without runtime crashes. |
| **Process Termination** | Only fully committed transactions are authoritative upon subsequent process launch. |
| **Ambiguous Submission Retry** | Use unique submission/attempt identity to ensure idempotent handling upon retry. |

## Technology Strategy and Evidence-Gated Candidates

Concrete persistence technologies are **not final accepted selections** in this ADR. Final selection is evidence-gated, pending bounded implementation spikes and conformance validation.

```
┌─────────────────────────────────────────────────────────────┐
│               Shared Application Persistence Contract       │
│               (Capability-oriented, Atomic, Versioned)     │
└───────────────┬─────────────────────────────┬───────────────┘
                │                             │
    ┌───────────▼───────────┐     ┌───────────▼───────────┐
    │  Web Adapter Candidate│     │Native Adapter Candidate│
    │      (IndexedDB)      │     │       (SQLite)        │
    └───────────────────────┘     └───────────────────────┘
          [Challenger:                  [Challenger:
          SQLite/WASM+OPFS]             Versioned Document/File]
```

### Primary Candidates

- **Web: IndexedDB**
  - *Rationale*: Browser-native, structured, asynchronous, transactional key-object storage; widespread cross-browser support; lower integration complexity and payload overhead than WASM database engines for MathFirst's data volume.
- **Native (Android / Windows): SQLite**
  - *Rationale*: Mature, embedded, ACID-compliant, structured relational engine; robust performance and durability on Android and Windows; strong ecosystem support in .NET.

### Candidate Challengers

- **Web Challenger: SQLite compiled to WebAssembly with OPFS (Origin Private File System)**
  - *Evaluation criteria*: Overhead, browser compatibility across mobile browsers, threading requirements (COOP/COEP headers), and bundle size impact.
- **Native Challenger: Versioned document / flat-file storage with atomic replacement**
  - *Evaluation criteria*: Durability guarantees, crash resilience, indexing efficiency, and implementation simplicity.

### Excluded Primary Technologies

- **`localStorage` / Synchronous Key-Value Storage**: Excluded as the primary learner-state store due to synchronous execution blocking UI threads, 5MB quota restrictions, lack of transactional capabilities, and string-only serialization constraints.

### Required Validation Evidence and Conformance Contract

Final engine acceptance requires passing a unified **Adapter Conformance Test Suite** covering:

1. Empty store initialization and schema creation;
2. Complete entity and history save/load roundtrips;
3. Atomic multi-entity commit during semantic submissions;
4. Zero partial state retention upon injected transaction failure;
5. Optimistic concurrency conflict rejection on stale revision tokens;
6. Format version detection and fast rejection of newer versions;
7. Deterministic handling of corrupt storage payloads;
8. Persistence verification across simulated close and reopen lifecycles;
9. Verification using deterministic synthetic fixtures.

Additional platform-specific evidence requirements:
- **Web**: Published Blazor WebAssembly PWA offline reload, storage quota exhaustion behavior, multi-tab conflict handling, and persistent storage request flow.
- **Native**: Android and Windows MAUI Blazor Hybrid integration, process lifecycle suspension/termination durability, and package build compatibility.

## Consequences

### Benefits

- Maximizes shared Domain and Application logic while respecting platform-specific storage realities.
- Enforces strict data consistency: learner history, item state, and progression cannot diverge due to partial writes.
- Prevents silent data corruption from concurrent tab usage or race conditions.
- Keeps domain and learning algorithms decoupled from storage representations, enabling independent evolution of schedulers and data models.
- Provides a clear migration and versioning path from day one.
- Defines clear evidence criteria before locking in concrete third-party dependencies or database engines.

### Costs and Risks

- Platform-specific adapters require separate implementations and integration test coverage for Web and Native hosts.
- Optimistic concurrency requires the Application layer to handle explicit conflict outcomes.
- Browser storage durability remains inherently vulnerable to user clearing and host eviction; mitigation requires clear user communication and future export capabilities.
- Maintaining an adapter conformance suite requires discipline across all supported platform adapters.

## Deferred Decisions

The following architectural and implementation details are explicitly deferred to subsequent packages:

- Final concrete persistence engine selection (IndexedDB, SQLite, or challengers);
- Third-party persistence libraries, NuGet packages, JavaScript wrappers, or ORMs;
- Physical database schemas, table definitions, column types, object store names, and index configurations;
- Storage keys, serialization formats (e.g., JSON, Protocol Buffers, MessagePack), and DTO layouts;
- Production schema migration scripts and transformation pipelines;
- Attempt history retention limits, archival rules, and compaction policies;
- Complete response-time architecture (`ITEM_READY`, `USER_SEMANTIC_SUBMISSION`, monotonic clock, input focus, rendering hooks, lifecycle suspension);
- Fact catalog representation (pre-populated catalog vs. procedural generation);
- Scheduler mathematics, interval formulas, FSRS, mastery equations, and fluency latency thresholds;
- Progression rules, number-range increments, and operation unlock criteria;
- Session bounding, queue composition, and mistake remediation workflows;
- User interface error dialogs and conflict reconciliation flows;
- Cloud accounts, cross-device synchronization, and remote backups;
- Telemetry, usage analytics, and crash reporting;
- Manual data export and import formats;
- Application packaging, code signing, deployment pipelines, and release automation.

## Alternatives

### Alternative A: Generic Repositories with Early Concrete Engine Commitment

- **Description**: Expose standard CRUD repository interfaces (e.g., `IRepository<T>`) from the Application layer, coordinate multi-entity updates via a Unit of Work abstraction, and immediately mandate IndexedDB for Web and SQLite for Native.
- **Why Not Selected**: CRUD repository abstractions fragment atomic use-case transactions, making it easy to accidentally violate the submission change-set invariant. Furthermore, committing to concrete libraries before gathering implementation evidence in MathFirst risks introducing unnecessary third-party complexity.

### Alternative C: Append-First / Event-Sourced Persistence

- **Description**: Store every attempt and interaction as an immutable sequence of domain events; reconstruct learner state and progression on demand via event replay and materialized views.
- **Why Not Selected**: Introduces substantial architectural overhead, including event schema versioning, projection management, periodic snapshotting, compaction, and replay performance tuning. MathFirst's current local learning requirements are fully satisfied by persisting current derived state alongside policy-neutral attempt evidence without full event sourcing.

### Alternative: Snapshot-Only State Persistence

- **Description**: Persist only the latest calculated state of each learning item and overall progression, discarding raw attempt history after session completion.
- **Why Not Selected**: Discarding raw attempt evidence (exact latency, timestamps, correctness) prevents retrospective optimization of future scheduling models, fluency algorithms, or analytics.

### Alternative: Enforced Single Storage Engine Across All Platforms

- **Description**: Force the identical storage engine (e.g., SQLite via WebAssembly/OPFS on Web, and SQLite on Android/Windows) across all targets for superficial uniformity.
- **Why Not Selected**: Mandating WASM SQLite on the Web introduces heavy bundle payloads, shared array buffer/threading constraints, and complex cross-browser OPFS compatibility issues that are unnecessary when IndexedDB provides excellent native asynchronous browser persistence. Platform fit takes precedence over artificial storage uniformity.
