# LEVEL_AUTHORING.md — the six composition rules

**Read this before authoring or editing a level.** These are the constraints that actually govern
whether a level can be framed and read; they are derived, not taste, and each one was paid for.

Moved here from the retired Android repo's `LevelDefinition.kt` on 2026-08-06, when game data
authoring moved into Unity. Dozens of comments in that Kotlin still point at "the composition
rules at the top of the campaign block" — they mean this file now.

`LevelDefinitionInspector` checks rules 1, 2, 3, 5 and 6 live in the inspector. Rule 4 is rule 6's
measure. A warning there is a warning about the level, not about the tool.

---

## The six rules

**1. Aiming zoom is set by the PLAYER LINE alone.**
`camZ = (playerHalfWidth + 1.2) / 0.45`, and the visible half-width at that distance is exactly
`playerHalfWidth + 1.2`. During the phase the player spends most of their time in, they see their
own line and 1.2 units of margin — **the enemy is not in frame and cannot be.** Keep the player
line **~6 wide**: that puts the camera near 9 and renders a soldier around 130px. A wide player
line is a zoomed-out level, and nothing else about the layout can compensate.

**2. Scout / resolving zoom is set by the ENEMY CLUSTER, structure edges included.**
Keep the whole enemy side inside **~11 world units** so that framing stays near `camZ` 15. This is
the rule the old levels broke hardest — two fortresses 20 units apart forced the camera to its
clamp and shrank everything.

**3. One dominant structure per level, at most two small supports.**
At 2.5x a fortress tier is 6.0 wide and the stack is 6.0 tall; a garrison post is 3.75 wide. Three
or four of those in a row is a level that cannot be framed. This is also the Archery Bastions
read — one commanding keep, not a village.

**4. Engagement separation 14–18 units.**
Well inside the ~49-unit max range, and it preserves the arc that made the original L1 feel right.
Objectives further out read as "shots pass through" — **range is not the constraint, legibility
is.**

**5. Garrison the MAJORITY of the enemy roster.**
L3 was won in three volleys with its structures at 238/340 — the unit-kill win condition resolved
before the structures mattered at all, so their HP was irrelevant to the outcome. Structures only
read as objectives when killing them is the efficient way to kill units, which means most of the
enemy roster stands **on** them. Raising `STRUCTURE_HP_SCALE` does not fix this; it just leaves
more HP standing at the same victory. (The scale itself measured correct: the L6 keep took ~5–6
full-roster volleys at 637 HP.)

**6. Separation is measured TANK → DOMINANT STRUCTURE**, which is what rule 4's 14–18 means.
Shipped L1/L4/L6 are 16.5/14.3/17.0 by that measure; their forward ground groups sit far closer,
and an advancing group closer still by design.

---

## Reference numbers

**Camera frustum** at `GAMEPLAY_Z = 10`, HFOV ≈ 49°: half-width ≈ 4.5 units from camera centre.
With `CAMERA_MIDFIELD_X = -0.5`: left edge ≈ −5.0, right edge ≈ +4.0.

**Garrison capacities**, from `standWidth` after scaling:

| Structure | standWidth | ~soldiers |
|---|---|---|
| Outpost | 1.5 | 3 |
| Bunker | 2.125 | 4 |
| Barracks | 2.25 | 4 |
| Garrison post | 3.125 | 6 |
| Watch tower | 1.5 | 3 |
| Comms tower | 0.75 | 2 |
| Tower platform | 1.5 | 3 |
| Fortress wide / mid / small | 4.5 / 3.0 / 2.0 | 9 / 6 / 4 |

**Roster scale is LOCKED at 7–30 units per side** (`GAME_DESIGN_LOCKS.md` → Army & Scale), and
that bound includes garrisoned units. The loadout picker's ground-squad range is therefore
`7 - garrisonedPlayerCount` to `30 - garrisonedPlayerCount`, clamped so the minimum never exceeds
the maximum. A level that breaks under a *legal* loadout is a product bug, not a player error.

---

## Where this sits against the other docs

- `GAME_DESIGN_LOCKS.md` and `CAMERA_ARCHITECTURE.md` **win.** These rules are derived from the
  camera model; if one ever contradicts it, the camera is right.
- `PRODUCT_DIRECTION.md` says what a level is FOR — one beat, one thing taught. This file says
  whether the level can be seen. Both apply.
- **Test rigs are exempt.** They exist to break a rule on purpose and measure what happens; set
  `isTestLevel` and the inspector stops complaining.

## History worth not repeating

The campaign was rebuilt from first principles on 2026-07-29 because the previous 25 levels were
authored against 1x structures and every one of them overflowed the frame once `STRUCTURE_SCALE`
went to 2.5. Rules 1–4 are what came out of that; 5 and 6 were added by the playtest immediately
after. The lesson underneath is that a level's composition is a function of the camera, and the
camera is locked — so composition is checkable, and now it is checked.
