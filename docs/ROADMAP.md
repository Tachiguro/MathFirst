# MathFirst Strategic Development Roadmap

This document defines the high-level development sequence and milestone phases for MathFirst.

---

## 1. Roadmap Principles

1. **Phased Progression**: Each phase establishes the prerequisites for subsequent phases.
2. **Product-Driven Architecture**: Technical architecture is decided only after product requirements, target platforms, and user workflows are explicitly defined.
3. **Multi-Platform Design Objective**: MathFirst should maximize shared domain and application logic across all supported platforms (Android, Web, Windows) where technically sensible, while allowing platform-specific UI or integration code where justified.
4. **Thin Vertical Slices**: Implementation focuses on delivering end-to-end user workflows before expanding into broad infrastructure foundations.
5. **No Speculative Package Allocation**: Specific canonical `MF-*` package identifiers are created only when work packages are formally defined.

---

## 2. Sequence of Development Phases

### Phase 1: Governance & Repository Foundation
- Establish repository governance, invariant rules, safety protocols, and multi-mode agent lifecycle (`MF-GOV-001`).

### Phase 2: Product Definition
- Define the core purpose of MathFirst.
- Identify target users and primary mathematical/educational problems solved.
- Define the core user workflow and Minimum Viable Product (MVP) scope.
- Establish platform-specific UX and operational requirements for the confirmed target platforms:
  - **Android**
  - **Web**
  - **Windows**
- Establish offline/online operational constraints, storage needs, and data privacy requirements.
- Evaluate monetization only if relevant to the product model.

### Phase 3: Architecture Decisions
- Formulate and approve Architecture Decision Records (ADRs) required to support the approved product definition.
- Evaluate technology stack options (languages, runtimes, UI frameworks, storage, build tools) against the mandatory constraint to support **Android**, **Web**, and **Windows**.
- Select the architecture baseline that best satisfies cross-platform domain sharing and platform-tailored user experience.

### Phase 4: Thin Vertical Slice
- Implement one complete, minimal end-to-end user workflow from UI/input through mathematical evaluation to result presentation.
- Validate end-to-end developer experience, testing harnesses, and user experience across target platforms before scaling.

### Phase 5: Iterative Packages
- Deliver modular features and capabilities through bounded, TDD-driven `MF-*` packages under the governed lifecycle.
