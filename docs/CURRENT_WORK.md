# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-SET-001`
- **Title**: Practice Configuration: Operation Selection and Adjustable Practice Time
- **Active Task Branch**: `feat/mf-set-001-practice-configuration`
- **Base Branch / Commit**: `main` at `e1a0ae5a4a557f434f29ae04b7f8bcd32f3d2d88`
- **Operation Mode**: `IMPLEMENT_SLICE`
- **Slice**: `1 — Answer Entry Reliability, Operation Selection, and Basic Extra-Time Settings`
- **Status**: Slice 1 implementation complete and verified.
- **Implementation & Verification State**:
  - Bug 1 & Bug 2 answer entry regressions resolved and proven with dedicated tests (`AnswerEntryReliabilityTests`).
  - Arithmetic operation selection (Addition, Subtraction, Multiplication, Division) implemented in Domain, Preferences, Practice Scheduling, and UI.
  - Practice-time options (Standard, 30s, 45s, 60s) implemented with deadline floor clamping (`Max(adaptiveDeadline, explicitFloor)`).
  - All 954 Core tests passing. App builds cleanly for Windows and Android.

---

## 2. Operational Rules

1. **Subordinate Status**: If this file differs from the current branch, working tree, or GitHub state, live repository evidence is authoritative.
2. **Deterministic Scheduling**: Active operations rotate deterministically over the enabled subset in canonical order.
3. **Minimum Active Operations**: At least one operation must remain enabled at all times.
4. **Learning Continuity**: Disabling an operation excludes it from practice scheduling while preserving all underlying learning progress, band positions, FSRS card states, and attempt histories.
5. **Pace Measurement Integrity**: Explicit practice-time floor expands opportunity only and never inflates raw response latency measurement or alters adaptive fluency thresholds.

---

## 3. Current Lifecycle Position

- **Current Lifecycle Step**: Checkpoint B completion for Slice 1.
- **Next Lifecycle Step**: Code review (`REVIEW_ONLY`) and documentation synchronization.
