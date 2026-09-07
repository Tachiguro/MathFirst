# ADR-0001: Cross-Platform Application Topology and Stack Baseline

## Status

Accepted

## Date

2026-09-07

## Context

[MathFirst's product definition](../PRODUCT.md) requires Android, Web, and Windows support. Android interaction is touch-first, while Web and Windows require effective keyboard and numpad input. The complete core learning loop must work offline without an MVP account, and learning progress must persist locally. Both answer correctness and response latency affect learning evaluation.

The architecture should maximize shared Domain and Application logic where technically sensible while allowing platform-specific presentation and integration. Addition alone may form the initial Thin Vertical Slice. The solution must remain practical for a solo or small development team to maintain, test, and evolve.

The stack was evaluated specifically against these MathFirst requirements: three mandatory platforms, shared Domain/Application logic, offline capability, testability, input ergonomics, small-team maintainability, and reduced toolchain complexity. It was not selected because of another application's technology choices, including KnownFirst.

## Decision

### Architecture boundary

MathFirst will use shared, platform-independent Domain and Application layers. Platform-specific hosts own composition and integration. Explicit platform adapters may handle local persistence, monotonic timing, lifecycle, input and focus behavior where required, packaging, and OS or browser integration.

The shared Domain/Application core must not depend directly on:

- Blazor or Razor;
- .NET MAUI;
- DOM or other browser APIs;
- Android APIs;
- Windows APIs;
- WebView APIs;
- database-specific APIs;
- network or cloud infrastructure.

### Language and runtime

The shared language and runtime baseline is C# on .NET 10.

### Platform hosts

- **Web**: a standalone Blazor WebAssembly Progressive Web Application, published as an offline-capable application.
- **Android**: a .NET MAUI host with Blazor Hybrid presentation and platform-specific adapters where required.
- **Windows**: a .NET MAUI host with Blazor Hybrid presentation, using the WinUI/WebView2-backed host provided by MAUI and platform-specific adapters where required.

### Presentation sharing

Razor components may be shared selectively through a Razor Class Library when technically beneficial. Complete presentation sharing is not required. Host-specific presentation and integration adaptations are permitted, and platform quality takes precedence over forced UI reuse.

Native .NET MAUI presentation remains a controlled future fallback if MAUI Blazor Hybrid fails a mandatory MathFirst requirement during validation. That fallback is not currently selected.

## Consequences

### Benefits

- C# is the primary implementation language across all mandatory platforms.
- Domain and Application behavior can be shared extensively and tested with deterministic .NET unit tests.
- Presentation can be shared selectively without constraining platform-specific quality.
- One primary runtime and language reduce toolchain complexity relative to the evaluated alternatives.
- Explicit host and adapter boundaries isolate platform integration from core learning behavior.
- Standalone Blazor WebAssembly PWA provides a viable route to a published offline-capable Web application.

### Costs and risks

- Blazor WebAssembly has initial payload and startup characteristics that require representative measurement.
- MAUI Blazor Hybrid depends on a WebView-backed presentation host.
- MAUI's support lifecycle may require framework upgrades more frequently than the .NET 10 LTS lifecycle.
- Each host requires platform-specific adapters and corresponding integration tests.
- Browser persistence has durability and lifecycle limitations that must be handled explicitly.
- Native input, focus, lifecycle, and timing behavior cannot be inferred from component tests and requires real platform validation.
- Hybrid UI requires both component-level testing and host/integration testing.

These risks are not claimed to be resolved by this decision. Runtime, offline, persistence, and packaging behavior remain unproven until validated with implementation evidence.

### Required validation evidence

Later implementation and Phase 4 evidence must verify at minimum:

- Android touch input behavior;
- Android soft and hardware numeric input;
- Windows keyboard and numpad behavior;
- Enter, Backspace, and focus handling;
- the timing boundary from `ITEM_READY` to `USER_SEMANTIC_SUBMISSION`;
- published PWA offline reload and update behavior;
- browser persistence behavior;
- WebView and native lifecycle behavior;
- representative startup behavior;
- packaging feasibility.

These are validation obligations, not blockers to accepting this ADR.

### Deferred decisions

This ADR does not decide:

- a concrete local persistence engine, SQLite or any other database, or the final persistence schema;
- the final fact-catalog strategy;
- scheduler mathematics, including FSRS or another scheduler algorithm;
- fluency or mastery thresholds;
- number-range increments;
- commutative cross-seeding;
- operation unlock rules;
- session-length rules;
- final answer-submission behavior;
- telemetry, analytics, or crash diagnostics;
- cloud accounts or synchronization;
- export or import;
- monetization;
- signing, publishing, deployment, or release automation.

These remain later product or architecture decisions.

## Alternatives

### Kotlin Multiplatform

Share Kotlin Domain/Application logic, use Jetpack Compose for Android, Compose Multiplatform for Windows, and Kotlin/JS for Web. This was not selected because its total toolchain and presentation complexity is higher for the expected MathFirst team size, despite strong Android characteristics.

### TypeScript with React, React Native, and React Native Windows

Use TypeScript across React for Web and React Native hosts for Android and Windows. This was not selected because current Windows/native support and version-alignment risks increase maintenance uncertainty.

### Rust shared core with separate hosts

Implement the core in Rust and bind it into separate platform hosts. This was not selected because FFI, binding, and toolchain complexity is disproportionate to MathFirst's deterministic arithmetic domain.

### Blazor WebAssembly with native MAUI XAML

Use Blazor WebAssembly for Web and native MAUI XAML presentation for Android and Windows. This was not selected initially because it materially increases presentation duplication without a currently proven product requirement. Native MAUI presentation remains the controlled fallback if Hybrid validation exposes a mandatory limitation.

### Blazor WebAssembly with specialized Android and WinUI hosts

Use direct, specialized platform hosts rather than MAUI Blazor Hybrid. This was not selected initially because it materially increases platform plumbing and presentation duplication without a currently proven product requirement.
