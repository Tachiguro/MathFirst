# ADR-0003: Independent Operation Progression and Open-Ended Fact Space

## Status

Accepted

## Date

2026-09-08

## Context

MathFirst currently implements learner Schema V4, a finite 418-fact catalog, a global Addition-to-Subtraction-to-Multiplication-to-Division introduction sequence, and a 12-attempt mixed checkpoint after each shared operand level. Exposure advances that sequence even when answers are incorrect or slow, and completion of the level-10 checkpoint ends acquisition and enters mixed practice over the fixed catalog.

That implementation proved the end-to-end native learning loop, but it does not express the intended product model. Arithmetic operations develop at different rates; correctness and recall latency must both influence readiness; elementary facts benefit from exhaustive acquisition, while a Cartesian catalog becomes unsuitable as operand magnitudes grow. The architecture must also preserve existing learner evidence, exact-fact FSRS scheduling, deterministic offline behavior, and the atomic persistence boundary established by [ADR-0002](ADR-0002-offline-execution-and-local-persistence-boundary.md).

This decision defines the approved target architecture for implementation planning. It does **not** describe the current implementation: Schema V5, independent band progression, procedural structured families, lazy materialization, and the selector policy below remain to be implemented.

## Decision

### Hybrid curriculum model

MathFirst adopts a hybrid curriculum:

- Elementary arithmetic facts remain exhaustively learnable where exact-fact memorization is pedagogically useful.
- Beyond that dense foundation, each operation progresses through bounded, deterministic arithmetic skill families rather than treating every pair in a growing Cartesian operand range as mandatory acquisition material.
- Addition, Subtraction, Multiplication, and Division advance independently. Deterministic operation interleaving is a scheduling policy, not a progression dependency.
- Correctness and latency both gate advancement. The target recent correctness rate is at least 95%, while isolated mistakes remain recoverable through later qualifying attempts, remediation, and FSRS review.
- There is no global checkpoint gate, no global progression lockstep, no permanent 418-fact or level-10 ceiling, and no terminal "all introductions complete" state.

### Fact-space terminology

The target model distinguishes four sets:

1. **Legacy/materialized learned facts** have persisted item or FSRS state. They remain reviewable regardless of the learner's current acquisition band.
2. **Curriculum-eligible facts** belong to completed bands or the current band. Eligibility is not a claim of mastery.
3. **Current acquisition-frontier facts** are the facts whose single acquisition owner is the operation's current band. Dense bands require exhaustive acquisition; structured bands require a deterministic representative sample.
4. **Due review facts** are any materialized exact facts whose FSRS due Practice Position has arrived. Due-review eligibility survives operation advancement.

Unseen, non-sampled structured candidates from completed bands are not permanent mastery or acquisition debt.

### Unique acquisition ownership

Every exact arithmetic fact has exactly one acquisition-owner band per operation. Its owner is the earliest band in the operation's canonical curriculum order whose generated candidate family contains that canonical `FactId`.

Consequently:

- the dense foundation owns every fact it already contains;
- a structured band's acquisition frontier contains only facts owned by that band;
- New selection, structured 16-fact coverage, and current-band frontier mastery evidence use only owned-frontier facts;
- a fact owned by an earlier band remains reviewable through FSRS and remediation;
- membership in a later mathematical family may support diagnostics but cannot grant duplicate acquisition credit.

Ownership is determined from canonical `FactId` values after generation, not from formula labels or unordered mathematical equivalence.

### Dense basic-fact foundation

| Operation | Exhaustive dense definition | Count |
|---|---|---:|
| Addition | Every ordered `a + b`, where `0 <= a <= 10` and `0 <= b <= 10` | 121 |
| Subtraction | The complete non-negative inverse family of dense Addition: `m - b`, where `m = a + b` for `a,b` in `0..10` | 121 |
| Multiplication | Every ordered `a * b`, where `0 <= a <= 12` and `0 <= b <= 12` | 169 |
| Division | Every `(d * q) / d = q`, where `1 <= d <= 12` and `0 <= q <= 12` | 156 |
| **Total** |  | **567** |

Presentation-direction mirrors remain distinct exact facts. Division by zero is impossible. Addition retains dense bands `ADD-D01` through `ADD-D10`. Subtraction retains the existing triangular `SUB-D01` through `SUB-D10`, followed by `SUB-I11` through `SUB-I20`, whose 55 additional facts complete the 121-fact inverse region. Multiplication and Division continue through their dense factor/divisor bands to 12.

All valid V4 facts remain valid with unchanged canonical identity.

### Addition structured bands

After `ADD-D10`, Addition progresses by decimal magnitude. For magnitude `k >= 1`, let `P = 10^k`. For each lower place, let `q = 10^j`, with `j` descending from `k - 1` through `0`, and let `u,v` be digits in `1..9`.

The canonical band order at one magnitude is:

1. `ANCHOR`
2. `T{k-1}`, `R{k-1}`, `D{k-1}`
3. `T{k-2}`, `R{k-2}`, `D{k-2}`
4. continuing through `T0`, `R0`, `D0`.

#### `ADD-P{k}-ANCHOR`

For `1 <= u <= v <= 9`, generate the unordered core pair `{uP, vP}` and emit both presentation directions when the operands differ. The mathematical candidate count is 81. The owned frontier is 80 for `k = 1`, because `add:10+10` is dense-owned, and 81 for later magnitudes.

#### `ADD-P{k}-T{j}`

Define:

```text
c = 1 + ((3u + 5v + k + j) mod 9)
x = cP + uq
y = vq
```

For every ordered `u,v` in `1..9`, emit `x + y` and `y + x`. This transfer family has 162 mathematical candidates.

#### `ADD-P{k}-R{j}`

Using the same `c`, define:

```text
x = cP + uq
y = vP
```

Emit both directions. This round-magnitude-plus-non-round family has 162 mathematical candidates.

#### `ADD-P{k}-D{j}`

For `1 <= u <= v <= 9`, define:

```text
a  = 1 + ((2u + 3v + k + j) mod 9)
b0 = 1 + ((5u + 7v + k + j) mod 9)

if u == v and a == b0:
    b = 1 + (b0 mod 9)
else:
    b = b0

x = aP + uq
y = bP + vq
```

Emit both directions when distinct. This decomposition family has 90 mathematical candidates.

The `T`, `R`, and `D` formulas are disjoint under their canonical ordering. Arithmetic verification gives 495 raw generated FactIds and 494 owned candidates at tens after excluding dense-owned `add:10+10`; hundreds have 909 raw and owned candidates. Only 16 distinct owned-frontier facts per structured band are required for acquisition, limiting normal introductions to at most 64 at tens and 112 at hundreds.

### Subtraction structured bands

Subtraction remains non-negative. Its dense inverse extension generates `m - b` for `m = 11..20` and `m - 10 <= b <= 10`, adding `10 + 9 + ... + 1 = 55` facts.

For every structured Addition core pair `{x,y}`, with `z = x + y`, the corresponding Subtraction family generates:

```text
z - x = y
z - y = x
```

Mathematical duplicates are removed. Acquisition ownership follows the corresponding Addition band ownership, but Subtraction progression and its advancement evidence remain independent.

### Multiplication structured bands

Dense acquisition ends at factor 12.

#### `MUL-2D`

For `u = 1..9` and `v = 2..9`, define:

```text
t = 2 + ((3u + 5v) mod 8)
x = 10t + u
y = v
```

Emit `x * y` and `y * x`. The mathematical and owned-frontier counts are 144; the acquisition target is 16.

For scale `k >= 1`, let `P = 10^k`.

#### `MUL-P{k}-ROUND`

Generate the core `{uP, v}` for `u = 1..9` and `v = 2..9`, emitting both directions. The raw count is 144. At `k = 1`, 16 presentation facts (`10 * v` and `v * 10`) are dense-owned, leaving an owned frontier of 128. At `k >= 2`, the owned frontier is 144. The acquisition target is 16.

#### `MUL-P{k}-SHIFT`

Generate `{n, P}` and emit both directions. At `k = 1`, use `n = 13..99`, producing 174 raw and owned facts. At `k >= 2`, use `n = 1..99`, producing 198 raw facts; 16 presentation facts overlap the earlier `ROUND` band at the same scale, leaving an owned frontier of 182. The acquisition target is 16.

#### `MUL-P{k}-SCALED`

Use the `MUL-2D` significand `x` and generate `{xP, v}`, emitting both directions. Arithmetic ownership verification gives raw and owned-frontier counts of 144 for every complete `Int32`-safe scaled band. The acquisition target is 16.

This structured progression is intentionally more conservative than Addition and Subtraction.

### Division structured bands

For every structured Multiplication owned core pair `{x,y}`, with `p = x * y`, the corresponding Division family generates the exact inverses:

```text
p / x = y
p / y = x
```

Division remains exact-integer only, with a positive divisor, a non-negative dividend, and no remainder. Acquisition ownership follows the corresponding Multiplication family, while Division advances independently.

### Advancement semantics and gate

Dense advancement means every exact owned-frontier fact has been encountered at least once and recent operation-specific correctness and fluency show readiness for the next band.

Structured advancement means the learner has demonstrated sufficient fluency on a deterministic representative owned-frontier sample to begin the next arithmetic family. It is neither exhaustive mastery of all mathematical candidates nor proof of arbitrary arithmetic mastery at that magnitude.

An operation advances from its current band only when all of the following are true:

1. At least 40 accepted attempts for that operation occurred after its `BandStartedPracticePosition`.
2. Within the latest 40 accepted attempts for that operation after `BandStartedPracticePosition`:
   - at least 38 are correct;
   - at least 34 are fluent;
   - at least 20 are current acquisition-frontier attempts;
   - at least `min(16, owned-frontier-size)` distinct current acquisition-frontier facts appear.
3. Coverage is complete for the band type:
   - dense: every owned-frontier fact has at least one lifetime accepted attempt;
   - structured: at least 16 distinct owned-frontier facts were introduced during the current band.
4. Advancement occurs only as part of the atomic accepted submission that completes the gate.

`Fluent` means Correct with `ResponseLatencyMs <= 2500`. Incorrect and Timeout attempts are incorrect and non-fluent. Correct attempts above 2500 ms are correct but non-fluent. There is no automatic band regression. Earlier weak facts remain scheduled through remediation and FSRS.

### Operation and role scheduling

For the next accepted global Practice Position `p`, the scheduled operation is `(p - 1) mod 4` in the canonical order Addition, Subtraction, Multiplication, Division. This deterministic interleaving does not couple advancement.

Each operation follows this repeating ten-attempt role cycle:

| Position | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| Role | New | Due | New | Maintenance | Frontier | New | Due | New | Due | Frontier |

The normal proportions are 40% New, 30% Due, 20% explicit Frontier reinforcement, and 10% Maintenance.

The semantic candidate pools are:

- **New**: an unmaterialized exact fact whose acquisition owner is the scheduled operation's current band;
- **Frontier**: a materialized exact fact whose acquisition owner is the scheduled operation's current band;
- **Due**: a materialized exact fact for the scheduled operation that is due under FSRS;
- **Maintenance**: a materialized exact fact for the scheduled operation that is eligible for non-due maintenance under the existing practice policy;
- **AnyMaterialized**: any eligible materialized exact fact for the scheduled operation.

A due same-session remediation for the scheduled operation overrides its normal role, retaining current `ItemLearningState` remediation semantics. It does not replace the globally scheduled operation, so a weak operation cannot starve stronger operations.

After the remediation override is considered, the scheduled role resolves through the first non-empty semantic pool in exactly this order:

```text
New role:        New -> Frontier -> Due -> Maintenance -> AnyMaterialized
Due role:        Due -> Frontier -> Maintenance -> AnyMaterialized
Maintenance role: Maintenance -> Frontier -> Due -> AnyMaterialized
Frontier role:   Frontier -> Due -> Maintenance -> AnyMaterialized
```

The selector never switches operations because a preferred pool is empty. Only a scheduled New role may select an unmaterialized fact. Due, Maintenance, Frontier, remediation, and AnyMaterialized paths never materialize a new exact fact. Earlier-owned facts may appear through those review paths, but they receive no new acquisition or current-band coverage credit. Normal introduction therefore remains capped at four opportunities per ten accepted attempts for an operation.

In dense bands, New continues selecting unmaterialized owned-frontier facts until the dense frontier is fully materialized, then falls back to Frontier. In structured bands, New falls back to Frontier after 16 distinct owned-frontier facts have been introduced. These fallbacks remain current-frontier attempts without granting duplicate acquisition credit.

The fallback system is total. A fresh operation starts with New, and every initial band has a non-empty acquisition frontier, so its first accepted operation attempt can materialize a fact. At least one eligible materialized fact then exists for that operation. Migrated learners retain their materialized facts and resolve current-band New or Frontier candidates from their migration state. The selector must not persist a candidate cursor or use `Random.Shared`.

Within the selected semantic pool, normal exact-fact and commutative-mirror cooldowns apply first. If no candidate survives, the selector relaxes the commutative-mirror cooldown, then the exact-fact cooldown, and finally selects deterministically from the semantic pool. Soft cooldowns cannot make selection non-total.

Without remediation overrides, every normal ten-attempt operation cycle contains four New roles and two explicit Frontier roles. A successful New selection is a current-frontier attempt, and an exhausted New pool falls back to Frontier. The cycle therefore provides at least six current-frontier opportunities, or at least 24 in a 40-operation-attempt window. Due or Maintenance fallback to Frontier can only increase frontier exposure. This makes the 20-frontier and 16-distinct structured gates mechanically reachable.

Forty operation attempts are the minimum evidence-window size, not a guarantee of advancement at attempt 40. Same-session remediation and weak-fact obligations may replace normal opportunities and delay the point at which all gates hold simultaneously. Once those obligations clear, a stable learner can satisfy the gate; empty role pools cannot permanently block an operation.

### FSRS boundary

The target architecture preserves:

- `FSRS.Core` 1.0.7;
- `DesiredRetention = 0.95` and the existing 21 parameters;
- disabled fuzzing;
- Practice Position virtual time;
- the current deterministic Again/Hard/Good/Easy correctness-and-latency mapping;
- deterministic per-`FactId` card identity and per-fact scheduling.

Progression consumes raw attempt correctness and latency; it does not depend directly on FSRS stability, difficulty, or interval values. Advancement never deletes or suspends existing FSRS state.

### Fact identity and lazy materialization

The canonical presentation-direction-sensitive identifiers remain exactly:

```text
add:{left}+{right}
sub:{left}-{right}
mul:{left}*{right}
div:{dividend}/{divisor}
```

They remain independent of database row IDs and stable across migration. Existing valid stored IDs are copied verbatim; no alias-normalization migration is permitted.

Before durable materialization, a fact is conceptual and generated deterministically from operation, band, candidate rank, operands, and canonical `FactId`. Presentation alone creates no learner row. The first accepted attempt atomically creates or updates attempt history, item learning state, FSRS state as applicable, and progression state. Restart before an accepted submission creates no durable progression.

### Schema V5 target state

Schema V5 must retain the existing durable metadata required by the persistence architecture and make the following progression state authoritative:

- global `PracticePosition`, `StoreRevision`, and `SchemaVersion`;
- per operation, `BandIndex` and `BandStartedPracticePosition`.

`CoverageCursor` and `FrontierCursor` are not authoritative and must not be persisted. V5 removes logical ownership of the V4 global progression concepts `CurrentOperation`, `CurrentIntroductionTurn`, `CurrentMaxOperand`, `OperationMaxOperands`, `CompletedCheckpointLevel`, `ActiveCheckpointLevel`, `CheckpointAttemptCount`, and `CheckpointCorrectCount`.

`AttemptRecord` gains a nullable `PracticePosition`. Every new V5 accepted submission stores its exact positive Practice Position. This provides policy-neutral, deterministic ordering and rolling-window evidence without wall-clock ordering. Historical V4 positions must not be reconstructed from timestamps or SQLite `rowid`.

### V4-to-V5 migration

Migration is atomic and preserves valid attempt history, item learning states, FSRS states, global Practice Position, store revision, canonical `FactId` values, and legitimate learning evidence. Historical V4 attempts receive `NULL` `PracticePosition`.

Existing V4 maximum operand values `1..10` map directly to dense band `D1..D10` for each operation. A migrated Multiplication or Division learner at 10 enters `D10` and may later acquire `D11` and `D12`. A migrated Subtraction learner at `D10` next reaches `I11`; a migrated Addition learner at `D10` next reaches its first structured decimal band.

Every migrated `BandStartedPracticePosition` is initialized to the preserved current global Practice Position. Preserved item and attempt evidence may satisfy dense lifetime exposure, and FSRS reviews continue, but the rolling 40-attempt advancement window starts after migration. Obsolete checkpoint and global-turn counters are discarded.

An unsupported newer schema fails without writes. Any migration failure rolls back completely. Automatic learner reset is forbidden.

### Reset semantics

Reset Learning Progress clears attempts, item state, and FSRS state; resets Practice Position; and creates fresh per-operation initial-band progression. It preserves UI preferences and onboarding state. Full Local Reset performs the same learning reset and also restores applicable UI and onboarding preferences to defaults. Transient Ready, Pause, and Background-Resume gates remain non-persisted learner-schema state.

### Numeric capacity and open-endedness

MF-LEARN-001 retains `Int32`. Open-ended means there is no artificial fixed curriculum ceiling and future bands are procedurally available while the entire next defined band is safe in `Int32`.

Future implementation must use checked arithmetic and validate:

- Addition: checked result;
- Subtraction: `left >= right`;
- Multiplication: checked product;
- Division: positive divisor, non-negative dividend, and exact divisibility.

When the next complete band would exceed numeric safety, that operation remains in maintenance. This decision does not add `long`, `BigInteger`, decimal-result arithmetic, negative subtraction, or division with remainder.

### Determinism

The implementation must preserve these invariants:

- the same durable learner state produces the same scheduled operation;
- the same operation, band, role, and learner state produce the same selected candidate;
- candidate ordering never depends on dictionary or hash iteration order;
- production selection does not depend on `Random.Shared`;
- presentation without accepted submission mutates no durable progression state;
- band generation is pure and deterministic;
- acquisition ownership is deterministic and unique;
- `FactId` serialization is deterministic;
- cooldown relaxation follows a fixed terminating order;
- FSRS fuzzing remains disabled;
- advancement is atomic with its triggering submission;
- wall-clock time does not determine advancement.

A later implementation may use a stable cross-platform deterministic permutation or hash, but it must specify the algorithm portably and must not require persisted candidate cursors.

### Mixed checkpoint removal

The current 12-attempt Mixed Checkpoint is superseded in the target product architecture. Continuous independently progressing operation practice replaces the global checkpoint, its shared counters, and the terminal "all introductions complete" state. Existing checkpoint attempts remain valid learner history after migration.

## Architectural Invariants

1. Every exact fact has one acquisition-owner band per operation.
2. Dense foundations are exhaustive; structured bands use representative owned-frontier acquisition.
3. All four operations advance independently.
4. Recent correctness, fluency, current-frontier evidence, and band-specific coverage jointly gate advancement.
5. Exact-fact remediation and FSRS review survive operation advancement.
6. Facts and selection are generated deterministically and materialized only by accepted submissions.
7. V4 evidence migrates atomically without fabricated historical Practice Positions or automatic reset.
8. The target remains exact-integer and `Int32`-bounded while removing artificial curriculum ceilings.

## Consequences

### Benefits

- Learners can progress in one operation without being blocked by another.
- Elementary facts receive exhaustive acquisition while later arithmetic expands linearly by useful place-value structure rather than quadratically by operand ceiling.
- Advancement represents sustained correct, fluent performance rather than exposure alone.
- Unique acquisition ownership prevents overlapping formulas from double-counting evidence.
- Lazy materialization avoids a large permanent catalog while retaining granular exact-fact scheduling.
- Existing V4 learner evidence and FSRS cards remain valuable after migration.
- Deterministic generation and Practice Position ordering support offline replay and cross-platform conformance.

### Costs and risks

- Schema V5 migration and per-operation rolling-window evaluation require careful atomicity and conformance testing.
- Generator, ownership, sampling, and cooldown algorithms become part of the durable deterministic contract.
- Structured-band advancement proves representative fluency, not exhaustive mastery of all possible arithmetic at a magnitude; product language and diagnostics must remain precise about that boundary.
- `Int32` eventually limits complete procedural bands, at which point an operation must remain in maintenance until a separately approved numeric expansion.

## Alternatives

### Retain the global lockstep and Mixed Checkpoint model

Rejected because a shared Addition-to-Division staircase couples unrelated learning rates, the 12-attempt checkpoint does not measure operation-specific readiness, and level 10 creates an artificial acquisition endpoint.

### Dense Cartesian expansion

Rejected because exhaustive `0..N` operand pairs grow quadratically and turn large volumes of mathematically redundant facts into permanent acquisition debt.

### Structured families only

Rejected because the elementary foundation benefits from exhaustive exact-fact memorization and granular review; representative sampling alone would leave avoidable basic-fact gaps.

### Fixed finite catalog extension

Rejected because choosing another hard upper bound merely postpones the same terminal-ceiling problem and does not express place-value progression.

### Pre-generate a huge catalog

Rejected because unseen candidates would consume storage and imply persistent learner items before meaningful interaction. Pure generation plus lazy materialization is smaller and semantically clearer.

### FSRS-only operation advancement

Rejected because FSRS optimizes exact-card review scheduling; its stability, difficulty, and interval values are not an operation-level curriculum-readiness contract.

### Accuracy-only progression

Rejected because a slowly calculated correct response is not equivalent to automated recall. Fluency is a core MathFirst outcome.

### Reset V4 learner data instead of migrating it

Rejected because valid attempt, item, and FSRS evidence is user-owned learning history. Atomic lossless migration is required, and fabricated ordering evidence would be equally unacceptable.
