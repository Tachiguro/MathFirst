# Cyber Defense MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a fast Android-testable Cyber Defense training-shell MVP in both Light and Dark Mode, locked to portrait/reverse-portrait, while preserving all existing MathFirst learning semantics.

**Architecture:** The existing `TrainingSession` and Learning Engine remain authoritative. A small transient Cyber Defense presentation state reacts to already-accepted answer outcomes; it cannot choose facts, change curriculum, change FSRS state, or persist game progression. The existing training page keeps its answer flow and settings access while adding an opponent HUD, portrait-safe layout, and expression sizing that supports larger arithmetic.

**Tech Stack:** .NET 10, .NET MAUI Blazor Hybrid, Razor components, CSS isolation / existing `app.css`, Android `MainActivity`, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-26-cyber-defense-gamification-design.md` plus `docs/superpowers/specs/2026-09-26-cyber-defense-mvp-constraints-addendum.md`

## Global Constraints

- Work only on `feature/cyber-defense-mvp`; never write directly to `main`.
- Verify live Git state, branch, HEAD, worktree cleanliness, and relevant repository docs before every write phase.
- Android game/training orientation is portrait-only: allow 0° and 180°, reject 90° and 270°.
- Use Android `ScreenOrientation.SensorPortrait`; do not invent a landscape layout.
- Light Mode and Dark Mode are first-class and must both remain readable.
- The arithmetic task remains visually dominant.
- Explicitly validate `99 × 99`, `999 + 999`, and an entered result such as `1998` without clipping.
- Preserve existing `NumericAnswerInputPolicy`, `AnswerAutoSubmissionPolicy`, keypad preference, external-keyboard support, timer, persistence, FSRS, remediation, curriculum, and task-selection semantics.
- Cyber Defense state is transient prototype state only; no schema change and no durable XP/level economy.
- No new bottom navigation. Preserve the existing header Settings access pattern.
- Enemy artwork must be a small reusable set; no large content pipeline.
- No adaptive-discovery redesign, Guided Journey redesign, FSRS redesign, achievements, Dailies, streak redesign, notifications, Google Play Games, shop, currency, equipment, or leaderboards in this MVP.
- Use explicit staging only; never `git add .` or `git add -A`.

## Review Focus

1. **Android physical rotation:** 0° and 180° remain usable, while 90°/270° do not switch the training UI to landscape.
2. **Narrow portrait devices / gesture areas:** opponent HUD, expression, keypad, and Settings/Pause controls remain reachable with no bottom-system-UI overlap.
3. **Long arithmetic:** `99 × 99`, `999 + 999`, and the answer `1998` remain readable and do not force horizontal scrolling or overlap controls.
4. **Learning isolation:** correct/incorrect battle reactions must not alter which fact is selected, how it is rated, or how progress is persisted.
5. **Theme parity:** both Light and Dark Mode preserve sufficient contrast for equation, keypad, enemy HP, and shield/integrity.

---

### Task 1: Lock Android to sensor portrait

**Files:**
- Modify: `src/MathFirst.App/Platforms/Android/MainActivity.cs`
- Modify: `tests/MathFirst.Core.Tests/AndroidInputContractTests.cs`

**Interfaces:**
- Consumes: existing MAUI `MainActivity` activity attribute and Android `ScreenOrientation` enum.
- Produces: Android activity configuration that allows normal + reverse portrait and excludes landscape.

- [ ] **Step 1: Write the failing orientation contract test**

Add `AndroidInput_MainActivityUsesSensorPortraitOrientation()` to `AndroidInputContractTests` asserting that `MainActivity.cs` contains `ScreenOrientation = ScreenOrientation.SensorPortrait`, still contains `ConfigChanges.Orientation` and `ConfigChanges.ScreenSize`, and does not configure `ScreenOrientation.Landscape`, `ScreenOrientation.SensorLandscape`, or `ScreenOrientation.FullSensor`.

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```bash
dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AndroidInputContractTests"
```

Expected: the new sensor-portrait assertion fails before implementation.

- [ ] **Step 3: Set `MainActivity` orientation**

Modify the existing `[Activity(...)]` attribute in `MainActivity.cs` to include exactly:

```csharp
ScreenOrientation = ScreenOrientation.SensorPortrait
```

Keep the existing configuration-change flags unless a verified Android build proves one is incompatible.

- [ ] **Step 4: Run focused tests**

Run the same filtered test command. Expected: PASS.

- [ ] **Step 5: Commit**

Stage only `MainActivity.cs` and `AndroidInputContractTests.cs` and commit with a focused message such as `feat(android): lock training to sensor portrait`.

---

### Task 2: Add transient Cyber Defense encounter state

**Files:**
- Create: `src/MathFirst.Application/Practice/CyberDefenseEncounterState.cs`
- Create: `tests/MathFirst.Core.Tests/CyberDefenseEncounterStateTests.cs`

**Interfaces:**
- Produces: `CyberDefenseEncounterState` with transient prototype-only state.
- Required public members:
  - `const int PrototypeEnemyHitPoints = 5`
  - `const int PrototypeShieldSegments = 3`
  - `const int PrototypeEnemyCount = 3`
  - `int EnemyIndex { get; }`
  - `int EnemyHitPoints { get; }`
  - `int ShieldSegments { get; }`
  - `long Revision { get; }`
  - `void RecordCorrectAnswer()`
  - `void RecordIncorrectAnswer()`
  - `void Reset()`
- Semantics: presentation only; no dependency on curriculum, FSRS, persistence, or `TrainingSession` mutation.

- [ ] **Step 1: Write failing unit tests**

Cover:
- fresh state = enemy 0, 5 HP, 3 shields;
- correct answer reduces enemy HP by one and never below zero;
- defeating an enemy advances `EnemyIndex` modulo three and starts a fresh prototype encounter;
- incorrect answer removes one shield;
- losing the final shield resets only encounter HP/shields, not any learner state (the class has no learner-state dependency);
- every state-changing action increments `Revision`.

- [ ] **Step 2: Run the focused tests and verify they fail**

```bash
dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~CyberDefenseEncounterStateTests"
```

Expected: FAIL because the type does not yet exist.

- [ ] **Step 3: Implement the minimal transient state class**

Implement only the public surface above. Do not add persistence, XP, loot, timers, boss logic, or task-selection APIs.

- [ ] **Step 4: Run focused tests**

Expected: PASS.

- [ ] **Step 5: Commit**

Stage only the new state class and its test and commit, e.g. `feat(game): add transient Cyber Defense encounter state`.

---

### Task 3: Build the reusable Cyber Defense opponent HUD

**Files:**
- Create: `src/MathFirst.App/Components/Training/CyberDefenseHud.razor`
- Create: `src/MathFirst.App/Components/Training/CyberDefenseHud.razor.css`
- Create: `src/MathFirst.App/wwwroot/images/cyber-defense/glitch-drone.svg`
- Create: `src/MathFirst.App/wwwroot/images/cyber-defense/virus-core.svg`
- Create: `src/MathFirst.App/wwwroot/images/cyber-defense/crystal-malware.svg`
- Modify: `src/MathFirst.Application/LocalizationService.cs`
- Create: `tests/MathFirst.Core.Tests/CyberDefenseUiContractTests.cs`

**Interfaces:**
- Consumes: `CyberDefenseEncounterState` from Task 2 and current theme tokens from `app.css`.
- Produces: `CyberDefenseHud` parameters:
  - `[Parameter, EditorRequired] public CyberDefenseEncounterState State { get; set; }`
- Enemy slot mapping for MVP:
  - `0` → Glitch Drone → `/images/cyber-defense/glitch-drone.svg`
  - `1` → Virus Core → `/images/cyber-defense/virus-core.svg`
  - `2` → Crystal Malware → `/images/cyber-defense/crystal-malware.svg`

- [ ] **Step 1: Write failing source/UI contract tests**

`CyberDefenseUiContractTests` must verify:
- the component exists and accepts `CyberDefenseEncounterState`;
- exactly the three prototype asset paths above are referenced;
- component markup exposes enemy HP and shield/integrity with accessible labels/progress semantics;
- the component does not contain task-selection, FSRS, curriculum, persistence, XP economy, shop, or notification code;
- component CSS uses the existing theme variables (`--color-*`) rather than forcing a dark-only palette.

- [ ] **Step 2: Run the focused contract tests and verify failure**

```bash
dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~CyberDefenseUiContractTests"
```

- [ ] **Step 3: Create three lightweight reusable SVG opponents**

Create simple, original, repository-owned vector assets with no external runtime dependency. Keep file complexity small. The first should read as a technical drone; the second as a virus/core entity; the third should intentionally carry some of the stronger creature/monster appeal from the Neo Creatures concept while remaining inside the Cyber Defense visual language.

- [ ] **Step 4: Implement `CyberDefenseHud.razor` and isolated CSS**

Requirements:
- opponent image + name;
- HP bar based on `State.EnemyHitPoints / PrototypeEnemyHitPoints`;
- shield/integrity display based on `State.ShieldSegments / PrototypeShieldSegments`;
- responsive portrait layout;
- no bottom navigation;
- no permanent Level/XP semantics;
- Light and Dark Mode derive from existing MathFirst theme tokens;
- opponent visuals must never cover the equation/input region.

- [ ] **Step 5: Add minimal localized strings**

Add English, German, and Russian strings needed by the HUD (Cyber Defense label, shield/integrity labels, and enemy display names where localization is appropriate). Preserve English fallback behavior.

- [ ] **Step 6: Run focused tests and build the app project**

```bash
dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~CyberDefenseUiContractTests"
dotnet build src/MathFirst.App/MathFirst.App.csproj -c Debug
```

Expected: PASS / successful build.

- [ ] **Step 7: Commit**

Stage only the component, CSS, SVGs, localization changes, and contract tests; commit e.g. `feat(game): add Cyber Defense opponent HUD`.

---

### Task 4: Integrate the HUD into training without changing learning semantics

**Files:**
- Modify: `src/MathFirst.App/Components/Pages/Home.razor`
- Modify: `src/MathFirst.App/wwwroot/app.css`
- Modify: `tests/MathFirst.Core.Tests/ResponsiveAndCorrectAnswerFlowTests.cs`
- Modify: `tests/MathFirst.Core.Tests/CyberDefenseUiContractTests.cs`

**Interfaces:**
- Consumes: existing `TrainingSession` answer/evaluation flow, `CyberDefenseEncounterState`, and `CyberDefenseHud`.
- Produces: a Cyber Defense practice composition where accepted answer outcomes update only transient encounter state.

- [ ] **Step 1: Add failing contracts for learning isolation and layout**

Tests must pin these requirements:
- `Home.razor` creates/holds one transient `CyberDefenseEncounterState` instance for the active UI session;
- correct outcomes call only `RecordCorrectAnswer()` on battle state in addition to the existing learning flow;
- incorrect/timeout presentation may call `RecordIncorrectAnswer()` only after the existing session outcome is established;
- no battle state method appears in task-selection/curriculum APIs;
- existing `NumericAnswerInputPolicy`, `AnswerAutoSubmissionPolicy`, keypad enumeration, and existing Settings link remain present;
- no new bottom navigation is introduced;
- CSS contains a portrait composition that does not depend on `@media (orientation: landscape)`.

- [ ] **Step 2: Add large-expression regression expectations**

Extend `ResponsiveAndCorrectAnswerFlowTests` / UI contracts to require the expression area to remain single-row and shrink within bounds rather than overflow. The CSS must have explicit min/max sizing via `clamp(...)`, `min-width: 0`, and no horizontal scrolling for the expression region.

The manual verification matrix for this task must include:
- `7 + 8`;
- `99 × 99`;
- `999 + 999`;
- entered answer `1998`.

Do not change parsing/auto-submit semantics to satisfy these cases.

- [ ] **Step 3: Run the focused tests and verify failure**

```bash
dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~ResponsiveAndCorrectAnswerFlowTests|FullyQualifiedName~CyberDefenseUiContractTests"
```

- [ ] **Step 4: Integrate the HUD above the existing arithmetic interaction**

Render `CyberDefenseHud` only in the normal active-practice surface, not over startup/persistence/teaching/check-in overlays. Preserve the existing header Settings and Pause controls.

Wire transient encounter updates at the point where the existing answer outcome is already known. Do not reorder persistence, evaluation, advancement, timeout, or remediation behavior.

- [ ] **Step 5: Make the portrait expression/keypad layout fit real phones**

Adjust only the necessary training styles in `app.css`:
- keep equation and answer input on a stable portrait row;
- use bounded `clamp(...)` typography so short expressions remain large and long expressions shrink;
- maintain keypad tap-target usability;
- account for safe areas and smaller portrait heights;
- avoid any landscape-specific redesign.

- [ ] **Step 6: Run focused tests and both Debug builds**

```bash
dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~ResponsiveAndCorrectAnswerFlowTests|FullyQualifiedName~CyberDefenseUiContractTests|FullyQualifiedName~AndroidInputContractTests"
dotnet build src/MathFirst.App/MathFirst.App.csproj -c Debug -f net10.0-android
dotnet build src/MathFirst.App/MathFirst.App.csproj -c Debug -f net10.0-windows10.0.19041.0
```

Expected: all pass/build successfully.

- [ ] **Step 7: Commit**

Stage only Home, app.css, and relevant tests; commit e.g. `feat(game): integrate Cyber Defense training shell`.

---

### Task 5: Full regression and Android tester build readiness

**Files:**
- Modify only if verification reveals a defect in files already owned by Tasks 1–4.
- Update: `docs/CURRENT_WORK.md` / project-state docs only if repository governance requires lifecycle reconciliation for this package.

**Interfaces:**
- Consumes: complete Cyber Defense MVP implementation.
- Produces: branch that is buildable for Android tester distribution and has evidence that existing learning behavior remains intact.

- [ ] **Step 1: Run the full Core test suite in Debug**

```bash
dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj -c Debug
```

Expected: PASS.

- [ ] **Step 2: Run the full Core test suite in Release**

```bash
dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj -c Release
```

Expected: PASS.

- [ ] **Step 3: Build Android Release**

```bash
dotnet build src/MathFirst.App/MathFirst.App.csproj -c Release -f net10.0-android
```

Expected: successful Android Release build.

- [ ] **Step 4: Perform manual device smoke validation**

On a real Android device, verify:
- normal portrait works;
- 180° reverse portrait works when supported by device sensor settings;
- rotating 90°/270° does not enter landscape;
- Dark and Light Mode both render the opponent HUD and equation cleanly;
- Settings remains reachable from the existing header action;
- answer entry and auto-submit behavior match pre-MVP behavior;
- correct answers reduce prototype enemy HP;
- wrong answers reduce prototype shield only and retain mathematical progress;
- `99 × 99`, `999 + 999`, and `1998` fit the interaction region;
- bottom gesture/system area does not cover keypad controls.

- [ ] **Step 5: Inspect diff for scope creep**

Reject/remove accidental additions involving schema, persistent XP, achievements, notifications, Guided Journey changes, FSRS changes, or task-selection changes.

- [ ] **Step 6: Commit verification/documentation changes if any**

Use explicit staging and a focused commit.

- [ ] **Step 7: Stop before merge**

Report branch HEAD, changed files, test/build evidence, manual-smoke evidence, and remaining known MVP limitations. Do not merge, enable auto-merge, delete branches, or push directly to `main` without explicit user approval.
