# ADR-0013: D10 Proficiency-Gated Operation Unlock

## Status

Accepted. The direction is owner-approved in [Issue #114](https://github.com/Tachiguro/MathFirst/issues/114). The decision takes effect when its implementation PR merges after independent Gate C review.

Supersedes [ADR-0012](ADR-0012-cumulative-operation-unlock-progression-and-monotonic-curriculum-stage.md) §3 "Tolerant Prerequisite Unlock Predicates" for live stage advancement. All other ADR-0012 decisions remain in force.

## Date

2026-10-30

## Context

[ADR-0012](ADR-0012-cumulative-operation-unlock-progression-and-monotonic-curriculum-stage.md) introduced the four cumulative curriculum stages (+, then −, then ×, then ÷) and a persisted, monotonic `CurriculumStage`. Its unlock predicate counted an operation as ready once its *introductory* band was exposed: every `ADD-D01`/`SUB-D01`/`MUL-D01` fact attempted (4, 3 and 4 facts) with at most one in remediation, or `BandIndex >= 1`. Deterministic headless personas recorded during [Issue #112](https://github.com/Tachiguro/MathFirst/issues/112) showed a 55%-accurate beginner reaching Division at attempt 39 and a fluent learner at attempt 19. On the Android tester build, the owner saw Multiplication and Division arrive long before Addition and Subtraction were fluent.

[Issue #114](https://github.com/Tachiguro/MathFirst/issues/114) records the owner-approved direction (2026-10-30):

- Fresh `CurriculumManaged` learners start with Addition only.
- Subtraction unlocks only after Addition has demonstrated mastery through Dense Band 10, including `10 + 10`.
- Multiplication unlocks only after Subtraction has demonstrated mastery through Dense Band 10, including `10 − 9` and `10 − 10`.
- Division unlocks only after Multiplication has demonstrated mastery through Dense Band 10, including `10 × 10`.
- Each new operation starts at its own introductory band. Earlier operations stay active, earned stages stay monotonic, and no learner data is deleted or rewritten.
- No age inference from latency, no penalty for slow keypad input, no unearned mastery for unseen facts, and no deadlocks.

### Why `BandIndex` alone is not mastery evidence

Dense band progression ([ADR-0005](ADR-0005-acclimation-timing-and-rapid-dense-progression.md)) advances on full frontier coverage and $C \cdot 10 \ge N \cdot 9$ latest-outcome correctness. For $N \le 9$ that means 100%. Larger frontiers tolerate unresolved facts: `ADD-D09` ($N = 19$) needs 18, `ADD-D10` ($N = 21$) needs 19, and `MUL-D10` ($N = 21$) needs 19. A learner can therefore reach `BandIndex = 10` while `10 + 7` or even `10 + 10` still has an incorrect latest outcome. The persona `Persona05_PersistentWeaknessInsideD10` reproduces this: Addition passes D10 while `10 + 7` is still wrong. Gating on `BandIndex >= 10` alone would silently count that fact as mastered.

### Why `ItemStates` in a session is not authoritative

`TrainingSession.ItemStates` is a lazily filled selection cache. The runtime snapshot loaded at startup deliberately contains no item states. After an app restart, the cache holds only facts touched by evidence loads since that start. During implementation, a restart persona evaluated against that cache stayed in Stage 1 for 4,000 attempts. A predicate over all 121 D10 facts must therefore read durable rows.

## Decision

### 1. Prerequisite mastery set

For each prerequisite operation, the **D10 mastery set** is the set of canonical facts owned (per `AcquisitionOwnershipResolver`) by Dense bands `D01..D10` (`BandIndex 0..9`). These existing canonical bands are reused unchanged.

| Stage left | Prerequisite | Mastery set | Size | Explicit boundary facts |
|---|---|---|---|---|
| 1 → 2 | Addition | `a + b`, `0 ≤ a, b ≤ 10` | 121 | `add:10+10` |
| 2 → 3 | Subtraction | `a − b`, `0 ≤ b ≤ a ≤ 10` | 66 | `sub:10-9`, `sub:10-10` |
| 3 → 4 | Multiplication | `a × b`, `0 ≤ a, b ≤ 10` | 121 | `mul:10*10` |

The boundary facts already belong to their mastery sets. `CurriculumUnlockPolicy.GetRequiredBoundaryFactIds` lists them separately so the owner-approved boundary stays pinned even if band construction ever changes.

### 2. Fact mastery evidence

`CurriculumUnlockPolicy.IsFactMasteryDemonstrated(state)` holds when, in the durable `ItemLearningState`:

- `TotalAttempts > 0` and `CorrectAttempts > 0` (actually attempted and answered correctly);
- `NeedsRemediation == false`; and
- `ConsecutiveCorrectStreak > 0` (the latest accepted outcome is Correct).

An incorrect or timed-out latest outcome, a pending remediation, and an unattempted fact are never classified as mastered. Latency, fluency, FSRS rating and `IsProvisionallyMastered` are deliberately ignored, so slow correct answers count fully.

A missed fact becomes resolved only through a *later* correct presentation. The remediation priority override re-presents a missed fact no sooner than 4 practice positions after its last review. The exact-fact cooldown also forbids immediate repetition. Recovery evidence is therefore always a separately spaced retrieval.

### 3. Unlock predicate

`CurriculumUnlockPolicy.EvaluatePrerequisiteMastery(stage, itemStates, progressions)` returns `PrerequisiteMasteryEvidence`. The stage transition `S → S+1` (`S ∈ {1, 2, 3}`) happens on an accepted submission exactly when:

1. the prerequisite operation's persisted `BandIndex >= 10` (D01..D10 completed by the unchanged Dense rule);
2. every fact in the prerequisite's D10 mastery set satisfies §2;
3. every explicit boundary fact satisfies §2; and
4. aggregate broad weakness ([ADR-0010](ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md), ≥ 2 eligible `NeedsRemediation` facts across active operations) is false.

Missing progressions fail closed. Stage 4 never advances. Stages advance by at most one per submission and never skip (`Stage 1` with all three prerequisites mastered moves to `Stage 2` only).

Unlocks still never depend on age, grade, player level, boss victories, Skill Points, Critical Hits, elapsed time, response latency, pace calibration or question quotas.

### 4. Authoritative evidence in the session

- `ILearnerStore.LoadItemStatesAsync(factIds)` is a read-only query for durable item states by fact ID. `SqliteLearnerStore` implements it as a parameterized `fact_id IN (...)` read. **No schema, index, migration or stored-data change is made.**
- **There is no `LoadSnapshotAsync` fallback.** The interface default throws `NotSupportedException`. An earlier draft of this section promised a snapshot-based default; it was never implemented and is withdrawn, so the contract below is the one that is implemented and tested.
- After each selection-evidence load, `TrainingSession` loads the current stage's prerequisite mastery set once per `(learner-state generation, stage)` into a private overlay. On submission, stage evaluation uses the overlay with the in-session candidate item states layered on top, so the latest uncommitted outcome always wins.
- The overlay is used **only** for stage evaluation. It does not feed selection, remediation, broad weakness, FSRS or any persisted value, so scheduling behavior is unchanged.
- Evaluation, persistence and publication keep the ADR-0012 atomic-commit order: the stage is computed before commit and published only after a successful commit.

**Store capability contract.** Both outcomes below can only delay an unlock. Neither can grant one, lower an earned stage, or turn an unattempted or missed fact into mastery.

| Adapter | Runtime snapshot | `LoadItemStatesAsync` | Reliable D10 unlock after restart |
|---|---|---|---|
| `SqliteLearnerStore` (the only production adapter; Android and Windows) | sparse (no item states) | implemented | **Supported** |
| Adapter using the default `LoadRuntimeSnapshotAsync` (complete `LoadSnapshotAsync`) | complete | default (unsupported) | Supported: the session already holds every durable item state |
| Adapter with a sparse runtime snapshot and no query | sparse | unsupported | **Not supported**: only in-session item states are evaluated, so the unlock is withheld indefinitely |

- **Unsupported (`NotSupportedException`).** This is a static capability. The overlay stays empty, the result is cached for the `(generation, stage)` key and not retried on every commit. Evaluation falls back to the in-session item states.
- **Failure (any other exception except cancellation).** The overlay stays empty and the key stays unset, so every following evidence load, which runs after each commit, retries the read. Practice itself is not interrupted. Once a read succeeds, the next accepted submission evaluates the full durable evidence and unlocks if it is complete. Cancellation propagates.
- Failures are never represented as mastery: an empty overlay contributes no facts, and the predicate in §3 requires every D01..D10 fact to be attempted and correct.
- Test evidence: `Issue114MasteryReadContractTests` (see [TESTING.md §38](../TESTING.md#38-issue-114-d10-proficiency-gated-operation-unlock-adr-0013)). Any future production `ILearnerStore` adapter with a sparse runtime snapshot must implement the query and extend that evidence.

### 5. Weak-fact handling and deadlock avoidance

- Weak facts stay presentation-eligible: they are owned by bands `≤ BandIndex` ([ADR-0007](ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md)) and are prioritized by the existing remediation override. Ungated Addition and Subtraction facts are never blocked by the Guided gate. Multiplication facts in `MUL-D01..D03` stay within the Addition ceiling (≥ 20 once Addition has passed D10), and decoupling at `MUL BandIndex >= 3` releases the rest.
- A learner who repeatedly misses `4 + 1` stays in `ADD-D04`, where $N = 9$ demands 100%. The learner keeps practicing accessible facts with targeted remediation, and no other operation appears.
- **Alternative evaluated and rejected (measured):** requiring two consecutive correct retrievals for any fact that was ever missed. Remediation clears after the first correct answer, and the scheduler does not prioritize old recovered facts. In the restart persona, `sub:7-2` (missed once, recovered once) was not presented again in about 2,000 attempts, a practical deadlock. A mixed learner needed 1,262 attempts for Stage 2 instead of 148.

### 6. Existing learners and compatibility

- `CurriculumStage` stays persisted and monotonic. Learners already at Stage 2, 3 or 4 keep every earned operation. Nothing relocks, and no learner state, attempt history, FSRS card or progression row is rewritten or deleted.
- Learners still below Stage 4 continue under the new predicate on their next accepted submission, using their full durable history. Existing history that already satisfies D10 mastery unlocks immediately. Otherwise the learner stays at the current stage until the evidence is earned.
- **Unattempted-fact gaps cannot deadlock a learner.** The selector introduces New facts only from an operation's current band, so a prerequisite with `BandIndex >= 10` and an unattempted D01..D10 fact would never unlock. No reachable state has such a gap:
  - Every Dense advance requires complete owned-frontier coverage ([ADR-0005](ADR-0005-acclimation-timing-and-rapid-dense-progression.md); earlier fast-acquisition and bootstrap rules also required coverage).
  - The only coverage-free band assignment, the Schema V4 → V5 migration, caps bands at 9 (`max operand − 1`, max ≤ 10).
  - Any migrated band ≥ 1 already made the V8 → V9 migration move the learner past that operation's prerequisite stage.
- The Schema V8 → V9 migration ([ADR-0012](ADR-0012-cumulative-operation-unlock-progression-and-monotonic-curriculum-stage.md) §6) is **unchanged**. It still evaluates the legacy introductory predicates, retained as `GetPrerequisiteFactIds`, `IsPrerequisiteFullyIntroduced` and `CountPrerequisiteWeakFacts` for that purpose only.
- Each operation's band progression stays independent. A new operation starts at its own persisted `BandIndex`, which is `0` (its `D01` introductory band) for learners without dormant history. Dormant legacy history resumes as described in ADR-0012. Nothing synchronizes bands across operations, so Subtraction never jumps to `18 − 18` because Addition is near `9 + 9`.
- `PracticeMode.Custom` keeps its preference-driven operation set, independent of `CurriculumStage`. The stage is still evaluated on custom submissions, under the same stricter predicate.
- The 482-attempt normative benchmark runs in `Custom` mode and is unaffected.

### 7. Measured deterministic personas

Synthetic personas use a real `TrainingSession`, temporary SQLite stores and a fake clock (`Issue114ProficiencyUnlockPersonaTests`). Answers are scripted per durable fact encounter. These are reproducible probes, not a study of real learners.

| Persona | Script | Stage 2 | Stage 3 | Stage 4 |
|---|---|---|---|---|
| Fast fluent adult | 100% correct, 800 ms | 121 | 254 | 618 |
| Slow but correct | 100% correct, 9,000 ms | 121 | 254 | 618 (identical facts presented) |
| Restart every 100 attempts | misses each `7 − x` once | 121 | 272 | 632 |
| Mixed learner | misses each fact with `(a + b) mod 7 = 0` once | 148 | 312 | 723 |
| Beginner recovers `4 + 1` | misses `4 + 1` six times | 134 | — | — |
| Weak fact inside D10 | misses `10 + 7` twenty times | 160 | — | — |
| Beginner never solves `4 + 1` | misses `4 + 1` always | not reached in 400 attempts (stays in `ADD-D04`, Addition only) | — | — |

Under ADR-0012, the Issue #112 seeded personas reached Stage 4 at attempts 39 (beginner), 30 (intermediate) and 19 (fluent). Stage 2 at 121 is the minimum possible: one correct answer for each of the 121 Addition mastery facts.

## Consequences

### Positive

- New operations appear only after the full `0..10` fact space of the previous operation is demonstrated, including the boundary facts.
- Weak facts are never silently classified as mastered, including those the Dense 90% rule tolerates.
- Fast accurate learners reach each unlock at the minimum number of attempts; slow accurate learners are not penalized.
- Restarts cannot hide or fake evidence, because stage evaluation reads durable item states.
- No schema, migration, FSRS or stored-data change.

### Neutral

- `ILearnerStore` gains one read-only query. Its default throws `NotSupportedException` and has no snapshot fallback; the capability contract is in §4.
- The legacy introductory helpers remain in `CurriculumUnlockPolicy` only for the V8 → V9 migration.

### Negative / Trade-offs

- Unlocks now take hundreds of attempts rather than tens. This is the owner-approved intent.
- A single late error on any prerequisite D10 fact (while the learner is still in that stage) blocks the next unlock until a later correct remediation retrieval, a short delay.
- A learner who persistently misses one fact receives that fact frequently through the existing remediation override (about half of the attempts in the `4 + 1` persona). Changing remediation cadence is outside this decision.
- Existing installs that earned Stage 2–4 under ADR-0012 keep those stages without D10 evidence. Monotonicity and no-data-loss take precedence.
- An adapter with a sparse runtime snapshot that does not implement `LoadItemStatesAsync` cannot reliably unlock operations after a restart (§4). No such production adapter exists, and a new one must implement the query.
- The celebratory unlock notification is out of scope ([Issue #116](https://github.com/Tachiguro/MathFirst/issues/116)).

## Alternatives

1. **Keep ADR-0012 introductory predicates.** Rejected: owner decision in Issue #114. Measured Division unlocks at attempts 19–39.
2. **`BandIndex >= 10` only.** Rejected: the Dense 90% rule can advance past D10 with unresolved facts (§Context), which would count incorrect facts as mastered.
3. **Two consecutive correct retrievals for previously missed facts.** Rejected after measurement: near-deadlock, because recovered old facts are rarely rescheduled (§5).
4. **FSRS stability/retrievability thresholds.** Rejected: couples unlock authority to FSRS model parameters and would need new calibrated thresholds; FSRS must remain unchanged.
5. **Latency or fluency thresholds.** Rejected: penalizes slow keypad entry and young learners, contrary to the product contract.
6. **Re-evaluating or relocking stages of existing learners.** Rejected: violates monotonic stages and no-progress-loss.
7. **Hydrating mastery rows into the shared `ItemStates` cache.** Rejected: that cache also feeds broad weakness and selection, so it would silently change scheduling after restarts.

## Relationship to Prior ADRs

- **[ADR-0012](ADR-0012-cumulative-operation-unlock-progression-and-monotonic-curriculum-stage.md)**: §3 superseded for live advancement. §1, §2, §4, §5, §7–§9 preserved. The §6 migration is preserved unchanged.
- **[ADR-0005](ADR-0005-acclimation-timing-and-rapid-dense-progression.md)**: Dense progression ($C \cdot 10 \ge N \cdot 9$) is unchanged. This ADR adds fact-level evidence on top of band completion.
- **[ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md), [ADR-0007](ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md), [ADR-0008](ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md), [ADR-0009](ADR-0009-guided-four-operation-number-space-gate.md), [ADR-0010](ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md)**: preserved.
