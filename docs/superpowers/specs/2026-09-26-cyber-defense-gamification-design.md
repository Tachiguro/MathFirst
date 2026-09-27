# MathFirst Cyber Defense Gamification Design

**Status:** Candidate Design Specification — pending explicit user review and approval  
**Date:** 2026-09-26  
**Branch:** `feature/cyber-defense-mvp`  
**Repository baseline:** `a1a02716b0e0b0acb020c21f9d4c13045bf803c5`

## 1. Product Decision

MathFirst will use **Cyber Defense** as the selected visual direction for the first gamification MVP.

The choice is based on direct product feedback from the user and a child tester:

- the child tester preferred the monster/creature appeal of the Neo Creatures concept;
- both preferred the visual language of Cyber Defense;
- Cyber Defense best matches the existing MathFirst green-oriented product identity;
- the MVP should therefore use the Cyber Defense interface language while allowing enemy designs to be more characterful, expressive, and creature-like than a purely technical simulator.

The product must support **both Light Mode and Dark Mode as first-class experiences**. Neither theme is secondary, and Cyber Defense must remain legible and attractive in both.

## 2. Core Product Principle

> **MathFirst rewards real ability, not merely time spent.**

Gamification must never become the authority for curriculum decisions.

The architecture preserves three separate meanings of progress:

1. **True Skill Progress** — what the learner can actually do mathematically.
2. **Guided Journey / Capability Unlocks** — learner-facing milestones derived from genuine competence.
3. **Meta Progression** — game-facing motivation such as player level, XP, achievements, streaks, encounters, and visual rewards.

Meta progression must never imply mathematical competence that the Learning Engine has not established.

## 3. Learning Authority Boundary

The existing Learning Engine remains authoritative.

It owns:

- task selection;
- curriculum progression;
- FSRS retention state;
- response evaluation;
- remediation;
- adaptive pace;
- operation progression;
- capability / number-space gating.

The Cyber Defense layer reacts to learner interaction but does **not** request mathematically inappropriate tasks.

Conceptual flow:

```text
Learning Engine
    -> selects mathematically appropriate fact
    -> learner answers
    -> learning state updates

Answer/result presentation
    -> Cyber Defense interprets the same result visually
    -> hit / critical / blocked / counterattack / shield feedback
```

The concrete implementation mechanism is intentionally not fixed by this document.

## 4. Zero Learning Regression

A combat loss must never remove legitimate learning progress.

Combat may reset temporary presentation state such as:

- current enemy HP;
- current shield/integrity;
- temporary combo;
- current encounter progress;
- transient visual effects.

Combat must never remove or downgrade merely because the player lost an encounter:

- learned evidence;
- accepted attempts;
- FSRS state;
- operation or capability unlocks;
- legitimate mathematical progression;
- earned achievements;
- long-term learner history.

A wrong answer remains useful learning evidence even if it also damages the player's temporary shield.

## 5. Selected Visual Direction: Cyber Defense

### 5.1 Tone

Cyber Defense should feel:

- modern;
- clean;
- green / technology-oriented;
- energetic;
- game-like without becoming visually noisy;
- suitable for a young child but not embarrassing for an adult;
- readable before decorative.

It must not become a literal cybersecurity simulator.

### 5.2 Enemy Style

Enemies can include a small reusable library of designs such as:

- glitch drones;
- virus-like creatures;
- corrupted bots;
- abstract cyber creatures;
- crystal / digital monsters;
- alien-tech entities;
- boss variants.

The child-tester preference for the Neo Creatures monster style should inform enemy art: opponents may be expressive and visually memorable even though the surrounding interface remains Cyber Defense.

Avoid requiring a large hand-authored asset catalogue.

### 5.3 Light / Dark Parity

Both themes are mandatory.

**Dark Mode** may use:

- deep charcoal / dark teal surfaces;
- restrained green glow;
- brighter enemy effects;
- high-contrast white arithmetic.

**Light Mode** may use:

- white / pale-gray surfaces;
- dark navy arithmetic;
- green accents;
- restrained cyber linework;
- bright enemy staging without washing out controls.

Existing user theme preference remains authoritative. Battle mode must never force Dark Mode.

## 6. Arithmetic Readability

The arithmetic task is always the primary visual element.

The UI must reserve enough width and vertical space for expressions larger than `7 + 8`.

The design must be able to accommodate examples such as:

- `12 × 12`;
- `199 + 199`;
- three-digit results;
- longer subtraction or division expressions supported by the curriculum.

Requirements:

- large, high-contrast operands and operators;
- no combat effects over the equation;
- no damage numbers over input controls;
- no enemy animation that obscures the task;
- no UI layout that only works for one-digit operands;
- keypad/input behavior remains efficient for rapid practice.

Damage numbers and hit effects belong visually at or near the opponent.

## 7. Cyber Defense Training Screen

The Cyber Defense MVP training screen should contain the following conceptual regions:

1. compact product/header area;
2. opponent presentation;
3. opponent HP;
4. player shield/integrity;
5. dominant arithmetic task;
6. existing answer-entry controls / keypad;
7. compact next-goal / progression hint where space permits.

The screen must not become so vertically dense that the answer controls are pushed into unsafe device areas or become uncomfortable on smaller displays.

Bottom navigation and system-safe areas must be treated as layout constraints rather than assumed fixed pixel positions.

## 8. Battle Feedback

### 8.1 Correct Answer

A correct answer produces a successful attack presentation.

Possible MVP feedback:

- opponent hit flash;
- HP reduction;
- small impact effect;
- optional compact damage indicator near opponent;
- subtle haptic feedback where supported.

### 8.2 Strong / Fast Correct Answer

A learner-relative strong response may later be presented as a **Critical Hit**.

Critical evaluation must never rely on one universal absolute speed threshold.

Future evaluation should use existing adaptive pace concepts and account for:

- learner-specific response pace;
- operation / fact pace;
- motor/input baseline;
- answer digit length.

For the visual MVP, exact Critical thresholds are not part of scope unless existing classification can be reused without altering learning behavior.

### 8.3 Incorrect Answer

A wrong answer may visually result in:

- attack blocked;
- opponent counterattack;
- shield/integrity loss;
- combo break;
- brief non-shaming visual feedback.

Do not use humiliating or punitive language.

### 8.4 Encounter Failure

If temporary shield/integrity reaches zero, the encounter may reset.

Suggested tone:

- `System breach`;
- `Defense failed`;
- `Reinitializing`;
- `Try again`.

Do not present mathematical regression.

## 9. Encounter Structure

The product is intended to remain effectively endless for as long as the learner wants to train and as far as the curriculum / review system supports useful work.

There is no conventional final campaign completion screen.

A small reusable enemy set is preferred over endless bespoke content.

Enemies may be reused through:

- different names;
- visual variants;
- palette / effect variants;
- difficulty presentation;
- boss treatment;
- context changes.

Exact encounter length, HP, damage, and shield values remain prototype decisions.

## 10. Bosses and Weakness Presentation

Bosses should feel like meaningful moments, not merely ordinary enemies with very large HP pools.

A future **Weakness Boss** may visually represent a period in which the legitimate Learning Engine schedule contains a meaningful concentration of difficult or remediation-relevant material.

Rules:

- the boss never directly chooses arbitrary facts;
- the Learning Engine remains authoritative;
- boss victory never directly marks facts mastered;
- actual learner evidence is what changes mathematical state;
- boss presentation may celebrate genuine stabilization only after the Learning Engine supports it.

Boss visuals should reuse the enemy asset system rather than require a new art pipeline for every milestone.

## 11. Adaptive Discovery Direction — Documented for Later Work

This is **not part of the immediate visual MVP implementation**, but the product direction is preserved here.

MathFirst should eventually use normal training itself as adaptive placement rather than forcing a separate placement exam.

For early Addition discovery:

- begin with very easy `0/1` material;
- do not present facts in predictable arithmetic order;
- every genuinely new fact should normally be encountered at least once;
- new facts dominate current discovery;
- older verification facts should preferentially come from weak, wrong, timed-out, or slow evidence;
- one isolated error should not automatically block exploration;
- repeated / patterned errors should slow escalation.

Product guidelines discussed so far:

- approximately 90% is a useful discovery-direction target once enough evidence exists;
- approximately 95% is the long-term retention / mastery target;
- these are guidelines, not yet hardcoded constants;
- for very small frontiers, naive percentages are misleading and should not be the only gate.

## 12. Spaced Repetition Direction — Documented for Later Work

FSRS remains a core MathFirst feature.

Long-term intent:

- stable trivial facts become extremely rare;
- one correct answer does not imply permanent mastery;
- wrong or slow answers increase future attention;
- confirmed stable facts receive increasingly distant review;
- very advanced learners should not constantly receive trivial facts such as `1 + 1`.

This design work must later reconcile selector-level maintenance / early-review behavior with the desired degree of long-term thinning.

The immediate Cyber Defense MVP must not rewrite this scheduling behavior.

## 13. Guided Journey Direction — Documented for Later Work

The default future experience should be a Guided Journey based on mathematical prerequisites / capabilities, not raw cross-operation stage equality.

Conceptually:

```text
Addition
├── Subtraction
└── Multiplication
      └── Division
```

This is not a rigid serial tree; operations can overlap.

Raw Presentation Stage values are not comparable across operations and must not be treated as a shared percentage or equivalent level.

The existing Addition-based number-space gate for Multiplication / Division is a useful architectural principle, but future learner-facing unlocks should be expressed as capabilities rather than raw stage-number comparisons.

This learning redesign is explicitly deferred from the first Cyber Defense visual MVP.

## 14. Meta Progression Direction — Documented for Later Work

Future gamification may include:

- Player Level;
- XP;
- Daily Goal;
- Streak;
- Achievements;
- hidden achievements;
- return summaries;
- optional notifications;
- large unlock celebrations.

These remain separate from True Skill Progress.

### 14.1 Achievements

The intended product behavior is progressive discovery:

- the achievement system is initially hidden;
- the first earned achievement reveals that achievements exist;
- hidden achievements do not reveal exact requirements before unlock;
- achievements never gate required mathematical progression.

A future achievement catalogue may contain roughly 40–50 achievements across journey, volume, competence, improvement, habit, and hidden categories.

### 14.2 Daily Goal and Streak

Future rules:

- meaningful mathematical activity counts, not merely opening the app;
- Daily Goal is soft and does not end a session;
- no aggressive FOMO;
- no purchased streak protection;
- Best Streak may remain visible after a break.

### 14.3 Notifications

Notification permission should not be requested immediately on first launch.

First demonstrate value, then ask contextually after a positive milestone.

Notifications should be optional, restrained, and non-threatening.

## 15. Immediate ASAP MVP Scope

The first implementation is intentionally a **visual / interaction validation MVP** intended for fast Android distribution and tester feedback.

### 15.1 Goals

The MVP must answer:

1. Does Cyber Defense make ordinary arithmetic practice feel more engaging?
2. Do both Dark and Light Mode remain attractive and readable?
3. Do expressive cyber/monster opponents improve the desire to continue?
4. Does the battle layer fit MathFirst without obscuring the arithmetic?
5. Is the layout comfortable on real Android devices?

### 15.2 In Scope

- Cyber Defense visual shell integrated into the existing training screen;
- first-class Light and Dark Mode;
- a small reusable set of opponent visuals / variants;
- opponent HP presentation;
- player shield/integrity presentation;
- simple correct-answer hit feedback;
- simple incorrect-answer counter/shield feedback;
- temporary encounter reset behavior if used;
- arithmetic remains driven by the existing Learning Engine;
- existing answer-entry behavior remains functional;
- responsive Android layout;
- retain current MathFirst accessibility/readability expectations;
- automated regression coverage for behavior changed by the MVP.

### 15.3 Explicitly Out of Scope for This MVP

- redesigning FSRS;
- implementing the new adaptive discovery algorithm;
- changing Guided Journey learning unlock rules;
- persistent XP economy;
- full Player Level system;
- full achievement catalogue;
- Daily Goal;
- Daily Streak;
- notifications;
- Google Play Games achievement sync;
- shops;
- currencies;
- equipment;
- loot;
- skill trees;
- crafting;
- leaderboards;
- large campaign story;
- hundreds of unique enemy assets;
- complex audio production.

The UI may reserve space or use clearly local prototype values where needed to demonstrate the concept, but it must not silently introduce durable progression semantics.

## 16. MVP Asset Strategy

Keep asset cost low.

Preferred strategy:

- a small set of base opponents;
- simple reusable hit / damage / shield effects;
- CSS / vector / composited presentation where practical;
- avoid bespoke full-screen background artwork per encounter;
- avoid any design that makes adding future content require continuous manual illustration work.

The exact art production method is an implementation-plan decision.

## 17. Theme and Device Requirements

The MVP must be checked on:

- Android portrait layouts;
- both Light and Dark Mode;
- small and large phone widths where supported by the existing app;
- system safe areas / bottom gesture regions;
- larger arithmetic expressions than the mockup's `7 + 8`;
- no overlap between bottom controls/navigation and device UI;
- responsive scaling without making keypad targets too small.

## 18. Validation Strategy

The MVP is explicitly meant for human feedback.

Tester feedback should focus on:

- which theme mode they prefer;
- whether the enemy area feels motivating or distracting;
- whether they want to continue after defeating an opponent;
- whether the arithmetic remains easy to read;
- whether the screen feels too busy;
- whether children and adults both understand what is happening;
- whether the encounter loss feedback feels frustrating;
- whether enemy designs are appealing;
- whether the interface remains fast and responsive.

Implementation should optimize for learning from this feedback rather than prematurely building the full game economy.

## 19. Decisions to Preserve

### Decided

- Cyber Defense is the selected MVP visual direction.
- Cyber Defense must support both Dark and Light Mode as first-class themes.
- Enemy art may incorporate the more expressive creature appeal preferred in Neo Creatures.
- The arithmetic task remains visually dominant.
- The Learning Engine remains authoritative.
- Battle presentation must not control curriculum/task selection.
- Zero Learning Regression on combat failure.
- Long-term product direction includes adaptive discovery instead of a separate placement exam.
- Spaced repetition remains a core feature.
- True Skill Progress, Guided Journey, and Meta Progression are semantically separate.
- Achievements are intended to be progressively revealed rather than shown as `0/N` on first launch.
- Notifications are contextual and optional, not an initial permission demand.
- No dark-pattern economy: no energy, loot boxes, artificial waits, casino mechanics, or pay-to-win.
- The immediate implementation is an ASAP visual/interactivity MVP for Android tester feedback.

### Proposed / Prototype Defaults

- exact enemy count in MVP;
- exact enemy HP;
- exact shield segment count;
- encounter length;
- hit damage;
- Critical Hit presentation;
- whether encounter failure is enabled in the very first prototype;
- exact next-goal presentation;
- exact animation timing / intensity;
- exact asset implementation strategy.

### Deferred

- adaptive discovery implementation;
- Guided Journey prerequisite redesign;
- FSRS/selector thinning redesign;
- persistent XP / Player Level;
- complete achievements;
- Daily Goal / Streak;
- notifications;
- Google Play Games synchronization;
- cosmetics / equipment / currency systems;
- social / leaderboard systems;
- large campaign content.

### Open Questions for Implementation Planning

1. Which existing training component boundaries should host the Cyber Defense presentation with the least production risk?
2. What is the smallest opponent asset set that gives testers enough variety?
3. Should first-MVP encounter failure be real (temporary reset) or should the prototype initially only show hit/counter feedback?
4. How should opponent HP scale without creating visual dependence on a fixed number of tasks?
5. Which UI elements from the concept mockups fit current MathFirst navigation, and which must be removed to avoid duplicating existing controls?
6. How should the layout reserve space for larger arithmetic expressions on small Android devices?
7. What minimum automated tests are needed to prove that the battle layer does not alter Learning Engine task selection/progression?

## 20. Approval Gate

This document is the written design specification required before implementation planning.

Implementation must not begin until:

1. the user reviews and explicitly approves this written spec;
2. an implementation plan is created from the approved spec;
3. the user reviews the implementation plan and selects the execution path.
