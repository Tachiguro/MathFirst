# Cyber Defense MVP Constraints Addendum

**Status:** User-directed constraints for the approved Cyber Defense MVP direction  
**Date:** 2026-09-26  
**Parent spec:** `docs/superpowers/specs/2026-09-26-cyber-defense-gamification-design.md`

## 1. Android Orientation

For the Cyber Defense MVP, Android training is **portrait-only**.

Allowed:

- normal portrait (0°);
- reverse portrait (180°), so the device can be used upside down where the platform/device supports sensor portrait.

Not allowed:

- landscape at 90°;
- reverse landscape at 270°;
- any responsive redesign whose purpose is to turn the battle screen into a landscape layout.

Implementation direction: use Android `ScreenOrientation.SensorPortrait` on `MainActivity` so portrait and reverse portrait are allowed while landscape orientations are excluded.

This orientation rule is scoped to the Cyber Defense MVP branch and does not authorize unrelated platform redesign.

## 2. Responsive Meaning

The training screen must still be responsive across supported portrait devices:

- narrow and wide phones;
- different Android aspect ratios;
- system safe areas / gesture insets;
- supported font scaling where practical;
- Light and Dark Mode.

Responsive means **fit the same portrait composition to different device sizes**. It does not mean creating a separate landscape composition.

## 3. Arithmetic Expression Capacity

The arithmetic task remains the primary visual element.

The expression area must be designed so representative larger content fits without clipping or colliding with the keypad or battle HUD.

The MVP must explicitly validate at least:

- `99 × 99`;
- `999 + 999`;
- an entered four-digit answer such as `1998`.

The typography may scale down as expressions become longer, but the expression must remain large, high-contrast, and visually dominant. Small expressions should use the available space generously rather than always rendering at the smallest size.

The implementation must not change arithmetic semantics, answer parsing, auto-submission, task selection, FSRS behavior, or curriculum logic merely to satisfy layout.

## 4. Cyber Defense Visual Direction

The selected shell is Cyber Defense in both Light and Dark Mode.

Enemy artwork may borrow the stronger creature/monster appeal of the Neo Creatures concept while preserving the Cyber Defense UI language.

For the ASAP MVP:

- use a small reusable opponent set;
- avoid a large asset pipeline;
- preserve the existing Settings access pattern instead of adding a new bottom navigation solely because it appeared in concept art;
- do not treat concept-art level/XP values as durable product semantics;
- keep the implementation fast enough for near-term Android tester distribution.

## 5. Deferred Product Logic

The following remain documented product direction but are not part of this visual MVP unless an existing implementation can be reused without changing learning behavior:

- adaptive placement/discovery redesign;
- Guided Journey prerequisite redesign;
- FSRS thinning changes;
- persistent XP / Player Level economy;
- full achievements;
- Daily Goal / Streak;
- notifications;
- Google Play Games integration;
- final balancing for enemy HP, shield counts, boss cadence, or encounter length.

Prototype encounter values may be local/transient and must be clearly treated as replaceable validation defaults.
