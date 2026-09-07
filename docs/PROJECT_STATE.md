# MathFirst Verified Project State

This document records the stable, verified factual baseline of the MathFirst project. It contains only durable project facts and excludes transient operational lifecycle noise.

---

## 1. Project Identification & Repository Baseline

- **Project Name**: MathFirst
- **Repository URL**: https://github.com/Tachiguro/MathFirst
- **Default Branch**: `main`
- **License**: Apache License 2.0 (see [LICENSE.txt](../LICENSE.txt))
- **Current Status**: Early Development

---

## 2. Target Platforms & Architecture Constraints

- **Confirmed Target Platforms (Requirement)**:
  - Android
  - Web
  - Windows
  *(Note: This is a durable product requirement. No platform implementations or binaries exist yet.)*
- **Cross-Platform Design Objective**: MathFirst should maximize shared domain/application logic across Android, Web, and Windows where technically sensible, while allowing platform-specific UI or integration code where justified.
- **Application Technology Stack**: Accepted baseline of C# / .NET 10, standalone Blazor WebAssembly PWA for Web, and .NET MAUI Blazor Hybrid for Android and Windows. See [ADR-0001](decisions/ADR-0001-cross-platform-application-topology-and-stack-baseline.md).
- **Product Architecture**: Accepted shared, platform-independent Domain/Application core with platform-specific hosts and adapters. Concrete persistence technology and other deferred architecture details are not selected.

---

## 3. Durable Product Requirements Baseline

- **Product Purpose**: MathFirst is dedicated to automating fundamental arithmetic facts through repeated, adaptive practice, transitioning learners from conscious calculation to fast recall.
- **Target Audience**: Age-neutral design suitable for children, teenagers, and adults, featuring a clean, focused, and non-patronizing user experience.
- **Core Fluency Model**: Practice evaluation considers both mathematical correctness and response latency to distinguish automated recall from conscious calculation.
- **Progression Model**: Learning begins in a very small number range (approximately `0..1`) and expands progressively as mastery is demonstrated.
- **Operation Sequence**: Broad intended progression order is `Addition` -> `Subtraction` -> `Multiplication` -> `Division`.
- **Offline Baseline**: Core learning and training loop must function 100% offline across Android, Web, and Windows.
- **Account Model**: No user account, registration, or login is required for the MVP.
- **Detailed Specification**: Authoritative product requirements, session mechanics, and MVP boundaries are defined in [docs/PRODUCT.md](PRODUCT.md).

---

## 4. Verified Repository Capabilities

- Clean public Git and GitHub repository initialized with default branch `main`.
