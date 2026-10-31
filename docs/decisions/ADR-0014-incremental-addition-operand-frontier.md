# ADR-0014: Incremental Addition Operand Frontier

## Status

Accepted (owner decision, Issue [#126](https://github.com/Tachiguro/MathFirst/issues/126)). Supersedes the *Addition structured bands* section of [ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md) and the Addition entry of its numeric-capacity terminal band.

## Date

2026-10-31

## Context

[ADR-0003](ADR-0003-independent-operation-progression-and-open-ended-fact-space.md) continued Addition after the dense `ADD-D01..ADD-D10` square with structured decimal families. `BandIndex 10` resolved to `ADD-P1-ANCHOR` (`10 + 20`, `20 + 10`, ..., `90 + 90`), followed by transfer, rounding, and decomposition families at tens and hundreds. A learner finishing `9 + 10` and `10 + 10` was therefore offered facts such as `20 + 80`, `60 + 60`, or `100 + 120` next. Structured advancement also required fluency (34 of 40 fluent attempts).

The owner rejected that jump as a curriculum progression ([decision](https://github.com/Tachiguro/MathFirst/issues/126#issuecomment-6535440644)) and approved incremental operand growth instead. The app is unreleased; the owner then chose in-place semantic adoption of saved Addition band indices over a versioned migration ([decision](https://github.com/Tachiguro/MathFirst/issues/126#issuecomment-6547945960)).

## Decision

### Curriculum

Addition `BandIndex = N - 1` has maximum operand `N`:

- `ADD-D01` (`N = 1`) owns `0+0`, `0+1`, `1+0`, `1+1`.
- Every later band `ADD-D{N:00}` owns exactly the ordered pairs `a + b` with `max(a, b) = N`: `2N + 1` facts, in the canonical order `0+N, 1+N, ..., (N-1)+N, N+0, N+1, ..., N+N`.
- `N = 11` introduces 23 facts, `N = 12` 25, `N = 13` 27, and so on; the cumulative eligible domain through band `B` is the complete square `0 <= a, b <= B + 1`.
- `ADD-D01..ADD-D10` keep their facts, identities, order, and the 121 D10 mastery facts used by [ADR-0013](ADR-0013-d10-proficiency-gated-operation-unlock.md). Canonical `FactId` values and FSRS card identities are unchanged.
- All incremental bands are `Dense`. Advancement uses the existing Dense rule ([ADR-0005](ADR-0005-acclimation-timing-and-rapid-dense-progression.md)): every owned fact needs an attempt after `BandStartedPracticePosition`, and latest-outcome correctness must satisfy `C * 10 >= N * 9`. Fluency and latency are not required.
- Subtraction, Multiplication, and Division curricula are unchanged, including the Subtraction structured families that invert the ADR-0003 additive core pairs.

### Analytic ownership and bounded generation

`IncrementalAdditionCurriculum` implements `IAnalyticCurriculumModel`. Because every band frontier is disjoint from all earlier bands, the owner of a canonical `add:{a}+{b}` is `max(1, max(a, b)) - 1`, computed from the parsed operands. Non-canonical spellings (leading zeros, signs, whitespace), foreign operations, and operands beyond the resource bound have no owner.

- `AcquisitionOwnershipResolver` uses the analytic owner for Addition and generates only the requested band (at most once per resolver); it never materializes the accumulated prefix.
- `GuidedNumberSpaceGate` uses the closed-form ceiling `2 * (B + 1)` (identical to the former prefix scan: 20 at D10, 22 at D11).
- `OperationCurriculum` validates that precomputed bands agree with the analytic model and allows procedural bands to be `Dense` only for an analytic curriculum.
- Per-question cost is proportional to the current frontier (`2N + 1`), not to the cumulative `(N + 1)^2` domain.

### Resource bound

`IncrementalAdditionCurriculum.MaximumSupportedOperand = 46,339` is an explicit resource-safety bound, not a pedagogical endpoint: it keeps the cumulative domain `(N + 1)^2` representable as an `Int32` count (46,340^2 = 2,147,395,600). The last band is `ADD-D46339` (`BandIndex 46338`, 92,679 facts). No band exists beyond it, so Addition stays in review and maintenance exactly as ADR-0003 prescribes for numerically unsafe successors; it never jumps to another magnitude. Sums stay far below `Int32.MaxValue`.

### Evidence capacity

- `DenseProgressionEvaluator.MaxDenseFrontierSize` changes from 25 (`MUL-D12`) to `IncrementalAdditionCurriculum.MaximumFrontierSize`.
- `SqliteLearnerStore.LoadLatestFrontierAttemptsAsync` and the current-band candidate read bind at most `FactIdParameterBatchSize = 200` fact IDs per statement and merge batches in ordinal `FactId` order. The current-band candidate read is bounded by the owned frontier instead of the 64-row review window, so no materialized current-band fact is mistaken for an unmaterialized `New` introduction when a frontier exceeds 64 facts (`N >= 32`). The same applies to the snapshot-based evidence path.

### Pre-release compatibility (owner decision)

The stored `operation_progression.band_index` is adopted in place:

- No migration, schema version change, compatibility map, clamping, or legacy anchor-band support is introduced.
- A saved Addition `BandIndex >= 10` now means `N = BandIndex + 1`, with its saved `BandStartedPracticePosition`.
- Physical attempts, item states, FSRS cards, backups, and earned `CurriculumStage` values are never deleted or rewritten by this change. Facts materialized from the former tens families (for example `add:20+80`) stay stored, are ineligible until `N` reaches their larger operand, and then rejoin review with their existing state.
- A learner saved at a former structured index `B > 10` resumes at `N = B + 1`; the incremental facts of bands `10..B-1` that were never materialized are not reintroduced as `New`. The owner accepted this pre-release consequence. Compatibility for released versions is a separate future decision.

## Consequences

### Benefits

- Addition progresses continuously; the first post-D10 facts include `11 + 11`, `0 + 11`, and `11 + 9` instead of `60 + 60`.
- Slow, consistently correct learners progress exactly like fast learners.
- Ownership and number-space checks are O(1) per fact and generation is O(frontier), keeping per-question cost bounded as `N` grows.
- D10 operation unlocks, earned stages, FSRS identity, remediation, cooldowns, and the other three curricula are unchanged.

### Costs and risks

- Exhaustive acquisition grows linearly per band: reaching `N` requires at least `(N + 1)^2` Addition presentations.
- The Guided ceiling grows by 2 per band, so Multiplication and Division facts gated by it (before their own soft decoupling at `BandIndex >= 3`) unlock more gradually than under the former 20 -> 180 jump.
- The documented 482-attempt strong-learner benchmark now ends at `ADD-D10 -> ADD-D11` (ceiling 20 -> 22).
- Pre-release learners saved at former structured Addition indices skip the unmaterialized intermediate incremental facts.

## Alternatives

### Keep the ADR-0003 structured Addition families

Rejected by the owner: the jump from the `0..10` square to tens anchors is pedagogically unacceptable.

### Versioned, lossless band-index migration

Considered (Issue #126 coordinator review, Option B). Not chosen: the app is unreleased and the owner explicitly preferred in-place semantic adoption without migration.

### A fixed endpoint such as `N = 100`

Rejected: the owner did not authorize a permanent learning endpoint. Only the explicit `Int32` resource bound above applies.

### Materialize the accumulated `0..N` square per question

Rejected: it grows quadratically with `N`; analytic ownership makes it unnecessary.
