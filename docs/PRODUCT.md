# MathFirst Product Definition

This document defines the authoritative, implementation-independent product contract for **MathFirst**. It captures all confirmed product requirements, the core learning model, progression rules, platform expectations, and Minimum Viable Product (MVP) boundaries required prior to Phase 3 Architecture Decisions.

---

## 1. Purpose and Value Proposition

MathFirst is a mathematics learning application dedicated to helping learners automate fundamental arithmetic facts through repeated, adaptive practice.

- **Primary Goal**: Facilitate the transition from conscious, effortful calculation of elementary arithmetic facts to rapid, reliable, and effortless mental recall.
- **Problem Solved**: Many learners struggle with higher-level mathematical concepts because basic arithmetic facts (e.g. `7 + 8`, `6 × 7`) are not automated, consuming working memory during complex problem-solving.
- **Value Proposition**: A distraction-free, highly responsive, and intelligent practice environment that continuously identifies and targets an individual's specific arithmetic weaknesses until complete fluency is achieved.

---

## 2. Target Users

MathFirst is designed to be **age-neutral**. It serves any learner seeking to build or restore arithmetic fluency:

- **Children**: Elementary learners acquiring fundamental arithmetic operations for the first time.
- **Teenagers**: Students addressing foundational calculation gaps to succeed in secondary mathematics.
- **Adults**: Self-directed learners and adults rebuilding arithmetic confidence or mental agility.

### User Experience Tone
- Clear, focused, and respectful.
- Serious and non-patronizing across all age groups.
- Avoids presentation or gamification that makes the experience unnecessarily juvenile, patronizing, or distracting.
- Designed to treat the learner's time with dignity.

---

## 3. Product Principles

1. **Fluency Over Mere Correctness**: An answer is truly mastered only when it can be recalled reliably and without hesitation.
2. **Adaptive Focus**: Practice must prioritize what the learner finds difficult, slow, or unfamiliar, while allowing mastered facts to gradually recede into maintenance.
3. **Progressive Scaffolding**: The problem space expands incrementally as mastery is proven, avoiding early cognitive overwhelm.
4. **Frictionless Interaction**: Fast, low-latency input tailored to the ergonomics of each supported platform.
5. **Offline Core and Account Independence**: Core learning must function without an active network connection, the MVP does not require a user account, and learning progress must persist locally across sessions.

---

## 4. Core Learning Model

### Learning Items
- Individual arithmetic expressions/problems represent distinct **learnable items** (e.g. `3 + 4` and `4 + 3`).
- **Commutative Expressions**: Expressions with reversed operands (e.g. `3 + 4` versus `4 + 3`) may be tracked as distinct items, reflecting the reality that human cognitive recall speed and confidence often differ by presentation direction.
- The learning engine maintains state and history at the individual item level to ensure granular tracking of recall strength.

---

## 5. Arithmetic Progression

### Number-Range Progression
MathFirst does not expose the full arithmetic problem space immediately. Learners begin with a very small working number range and expand progressively:

- **Starting Range**: Practice begins in an initial working range approximately around `0..1`.
- **Progression Cycle**:
  ```
  Small Number Range ──► Demonstrated Mastery ──► Incremental Range Expansion ──► Continued Adaptive Practice
  ```
- Active number ranges expand incrementally as the learner proves sufficient mastery over current facts.

### Arithmetic Operations
The intended broad progression sequence across operations is:
```
Addition ──► Subtraction ──► Multiplication ──► Division
```
- Progression across operations normally follows increasing foundational mastery.
- Operations build logically upon preceding capabilities.
- Not all four operations are required to be active or present in the initial MVP.

### Initial MVP Arithmetic Boundary
The initial MVP does not require negative subtraction results or division with remainders. Exact later scope for these arithmetic cases remains outside the current MVP definition.

---

## 6. Adaptive Training Loop

The practice selection engine automatically composes training queues based on the learner's live learning needs:

1. **New / Unknown Facts**: Gradually introduced into the active practice pool as working ranges expand.
2. **Weak / Insecure Facts**: Items with inconsistent accuracy or borderline recall speed receive frequent exposure.
3. **Previously Incorrect Facts**: Items answered incorrectly are prioritized for rapid reinforcement.
4. **Slow Facts**: Correctly answered items that required conscious calculation remain active until recall becomes automated.
5. **Mastered Maintenance Facts**: Strong, automated facts appear at gradually lengthening intervals to maintain long-term retention.

---

## 7. Response Evaluation and Fluency

True arithmetic fluency requires evaluating two distinct dimensions:

1. **Correctness**: Whether the submitted numeric answer is mathematically correct.
2. **Response Latency**: The elapsed time between item presentation and answer submission.

### Behavioral Requirement
- The system must distinguish between:
  - **Automated Recall**: Fast, accurate responses indicating memorized mastery.
  - **Conscious Calculation**: Correct responses that required noticeable calculation time.
- A slowly calculated correct answer must not be treated as equivalent to an automated recall; it must remain active in practice until retrieval is fluid.

---

## 8. Session Behavior

### Queue Composition
Training sessions are automatically generated by blending items across learning categories (new items, weak items, due reviews, and items requiring remediation).

### In-Session Mistake Remediation
When a learner provides an incorrect answer during a session:
1. The correct result must be clearly shown and reinforced.
2. The missed fact must be presented again later within the **same training session** to solidify short-term memory before the session concludes.
3. The learner must not be subjected to immediate, repetitive lock-step retries on the same screen before moving forward.

---

## 9. Input and Interaction Requirements

Input ergonomics are critical to measuring true arithmetic recall rather than motor typing friction:

- **Android**:
  - Touch-first user interface.
  - Efficient numeric answer entry.
- **Windows & Web**:
  - Effective physical keyboard support.
  - Physical numeric keypad (numpad) support where available.

---

## 10. Offline, Accounts, and Local Progress

### Offline Baseline
- The core learning loop, practice sessions, evaluation, and progression must function **100% offline**.
- An active network connection must not be required to practice or maintain progress across Android, Web, or Windows.

### User Accounts & Identity
- An account is **NOT required** for the MVP.
- All core learning functionality must be completely usable without registration, login, profile setup, or authentication.

### Local Persistence
- Learning state, item histories, and progression milestones must persist reliably in local device storage.
- Local persistence must survive application restarts, browser refreshes, and device reboots.

### Privacy & Data Integrity
- The MVP does not require personal user identity information for the core learning loop.
- Detailed policies regarding telemetry, analytics, crash diagnostics, and potential future online services remain unresolved.

---

## 11. Platform Requirements

MathFirst must support three mandatory target platforms:

| Platform | Key Product Requirements |
|---|---|
| **Android** | Touch-first interaction, efficient numeric answer entry, 100% offline core training loop. |
| **Web** | Effective physical keyboard support, numpad support where available, 100% offline core training loop. |
| **Windows** | Effective physical keyboard support, numpad support where available, 100% offline core training loop. |

### Cross-Platform Design Objective
MathFirst aims to maximize shared domain and application logic across Android, Web, and Windows where technically sensible, while permitting platform-specific UI or integration adaptations where justified.

---

## 12. MVP Product Boundary

The primary objective of the initial MVP is to prove the complete end-to-end MathFirst learning loop and progression model.

### Mandatory Capabilities (MUST for MVP)
- Present arithmetic learning items clearly.
- Capture numeric input efficiently across touch and keyboard environments.
- Evaluate answer correctness.
- Measure response latency / speed.
- Maintain a local learning state per item.
- Prioritize weak, slow, incorrect, and new items adaptively.
- Reinforce incorrect answers and reintroduce them later within the same session.
- Progressively expand the active number range based on demonstrated mastery.
- Function 100% offline without network connectivity.
- Operate completely without user accounts or authentication.
- Implement at least one core arithmetic operation (e.g. Addition) to validate the end-to-end learning loop.

---

## 13. Later Product Capabilities

The following capabilities are recognized as potential future extensions beyond the initial MVP:

- **Multiplication and Division Progression**: Broadening arithmetic operations as learner progression expands.
- **Extended Arithmetic Scope**: Negative subtraction results and division with remainders.
- **Optional Cross-Device Cloud Synchronization**: Optional multi-device synchronization and optional cloud accounts.
- **Data Portability**: Manual progress export and import functionality.
- **Richer Progress Presentation**: Additional learning statistics, visualization widgets, and practice customization.

*(Note: Specific dashboard implementations, visualization formats, and playlist features are non-binding illustrative possibilities and not approved roadmap commitments.)*

---

## 14. Current Scope Boundaries and Non-Goals

For the current product definition and MVP scope, the following boundaries apply:

1. **Focus on Fundamental Arithmetic Fact Automation**: Current product scope is strictly dedicated to automating elementary arithmetic facts. Narrative word problems, algebraic equations, geometry, and higher-level mathematics are not part of the current product definition.
2. **No Mandatory Accounts or Cloud Dependence for MVP**: The core learning experience must not require user registration, mandatory logins, or remote server dependencies.
3. **Low-Distraction Learning Environment**: In line with the age-neutral, respectful tone, the current product avoids unnecessary or distracting gamification (such as cartoon avatars, loot mechanics, or intrusive animations) that would detract from arithmetic focus.
4. **Unresolved Commercial and Telemetry Scope**: Monetization models, advertising policies, and telemetry/diagnostic frameworks are explicitly unresolved (see Section 15) and are not established as permanent product prohibitions.

---

## 15. Unresolved Product Decisions

The following decisions remain intentionally open and must not be treated as finalized product requirements until formally resolved:

| Decision Area | Description | Status |
|---|---|---|
| **Scheduler Mathematics** | Exact mathematical scheduling formulation, interval growth curves, and penalty weights. | `UNRESOLVED` |
| **Fact Catalog Strategy** | Finite pre-populated catalog of canonical items versus deterministic procedural generation with stable identities. | `UNRESOLVED` |
| **Telemetry, Analytics, and Crash Diagnostics** | Whether and how telemetry, analytics, and crash diagnostics should operate. | `UNRESOLVED` |
| **Exact Fluency Thresholds** | Specific millisecond or second boundaries defining automated recall versus calculated responses. | `UNRESOLVED` |
| **Exact Range Expansion Increments** | Specific numerical step increments and mastery percentages required to unlock range expansions. | `UNRESOLVED` |
| **Commutative Cross-Seeding** | Whether and how mastery of `3 + 4` influences initial recall expectations for `4 + 3`. | `UNRESOLVED` |
| **Operation Unlock Criteria** | Exact mastery thresholds required before subsequent operations are unlocked. | `UNRESOLVED` |
| **Manual Operation Control** | Whether users should be able to manually enable, disable, or override operation progression. | `UNRESOLVED` |
| **Session Length & Bounding** | Exact rules determining when a training session concludes (e.g. dynamic item count, queue exhaustion, or time limits). | `UNRESOLVED` |
| **Answer Submission Trigger** | Immediate auto-submit upon length match versus explicit confirmation (e.g. Enter key / submit button). | `UNRESOLVED` |
| **Progress Visualization Details** | Specific dashboard widgets, charts, and mastery visual indicators. | `UNRESOLVED` |
| **Launch Languages & Localization** | Target launch languages and string resource management structure. | `UNRESOLVED` |
| **Accessibility Acceptance Details** | Exact WCAG conformance levels and specialized motor/cognitive accommodation settings. | `UNRESOLVED` |
| **Monetization Model** | Long-term project funding structure (e.g. completely free open source, donations, or optional support). | `UNRESOLVED` |
| **Cloud Account & Sync Architecture** | Optional cloud synchronization design and account backend protocols. | `UNRESOLVED` |
| **Export/Import Specification** | Exact schema, file format, and migration rules for manual data transfer. | `UNRESOLVED` |

---

## 16. Architecture Inputs for Phase 3

This Product Definition establishes the following functional constraints for **Phase 3 Architecture Decisions**:

1. **Target Platforms**: Architecture must support **Android**, **Web**, and **Windows**.
2. **Shared Logic Objective**: Architecture should maximize reuse of core domain and application logic across supported platforms where technically sensible.
3. **Offline Core Training**: Architecture must support the complete core learning and practice loop without requiring an active network connection.
4. **Local Progress Persistence**: Architecture must support persistent learning state, item histories, and progression across sessions on each target platform.
5. **Response-Time Measurement**: Architecture must allow the application to capture answer response latency accurately enough to distinguish conscious calculation from automated recall.
6. **Account Independence**: The MVP core learning loop must operate without mandatory authentication or account infrastructure.
