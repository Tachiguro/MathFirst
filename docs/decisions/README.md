# Architecture Decision Records (ADRs)

This directory contains the immutable Architecture Decision Records for MathFirst. ADRs capture significant architectural, technological, and structural decisions along with their context, rationale, and consequences.

---

## 1. ADR Governance & Standards

1. **When to Create an ADR**: An ADR is required whenever a material architecture, framework, technology stack, or data storage decision with serious alternatives is evaluated.
2. **Permanent Numbering**: Decisions are numbered sequentially starting from `ADR-0001-<short-title>.md`.
3. **No Reuse of Numbers**: Once assigned, an ADR number is permanent. If a decision is superseded, a new ADR is authored with a new number and explicitly marks the prior record as superseded.

---

## 2. ADR Lifecycle Statuses

- **`Proposed`**: Under evaluation and discussion. Not yet approved.
- **`Accepted`**: Approved and active architecture baseline.
- **`Superseded`**: Replaced by a subsequent decision (must reference `Superseded by ADR-XXXX`).
- **`Deprecated`**: No longer applicable; decision retired without direct replacement.

---

## 3. Standard ADR Document Structure

Every ADR must contain the following sections:
- **Status**: Current lifecycle status (`Proposed`, `Accepted`, `Superseded`, `Deprecated`).
- **Date**: YYYY-MM-DD of authoring/decision.
- **Context**: Problem statement, background, constraints, and driving factors.
- **Decision**: The specific technical or architectural choice being adopted.
- **Consequences**: Positive, neutral, and negative impacts resulting from the decision.
- **Alternatives**: Other viable options considered and specific reasons why they were rejected.

---

## 4. ADR Registry

| ADR ID | Title | Status | Date |
|---|---|---|---|
| [ADR-0001](ADR-0001-cross-platform-application-topology-and-stack-baseline.md) | Cross-Platform Application Topology and Stack Baseline | Accepted | 2026-09-07 |
| [ADR-0002](ADR-0002-offline-execution-and-local-persistence-boundary.md) | Offline Execution and Local Persistence Boundary | Accepted | 2026-09-07 |
