# LEVEL_AUTHORING.md — the eight composition rules

**Read this before authoring or editing a level.** These are the constraints that actually govern
whether a level can be framed and read; they are derived, not taste, and each one was paid for.

Moved here from the retired Android repo's `LevelDefinition.kt` on 2026-08-06, when game data
authoring moved into Unity. Dozens of comments in that Kotlin still point at "the composition
rules at the top of the campaign block" — they mean this file now.

`LevelDefinitionInspector` checks rules 1, 2, 3, 5, 6, 7 and 8 live in the inspector. Rule 4 is
rule 6's measure. A warning there is a warning about the level, not about the tool.

**Rule 8 moved into `LevelComposition.CollisionBoxRule` on 2026-08-12**, so all eight rules now
report in the same place. It used to live only in `PortSelfTest.CheckNobodyStandsInAWall`, which
meant it failed the SUITE rather than showing up beside the level you were editing — an author saw
seven rules where there are eight. The suite still asserts it and now DELEGATES to the same
function, because one rule with two implementations is the second-source-of-truth failure this
project has already paid for.

**Rule 8 reports as an ERROR, and is the only non-roster error here.** Rules 1-6 are framing
judgements a level may bend for a reason it records in `designNotes`; rule 8 says a unit the
player is asked to kill cannot be hit, which is not a thing to bend. `LevelComposition.Report`
exits 1 on it, as the suite always did.

---

## The eight rules

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
It preserves the arc that made the original L1 feel right. Objectives further out read as "shots
pass through".

**The "~49-unit max range" this rule used to cite was WRONG, and it is what licensed a shipped
level to become unwinnable.** The real figure is `AimSystem.MaxRange45` = v²/g = 81/4 =
**20.25 units, on flat ground**, so 14–18 is not "well inside" anything — it spends 70–89% of the
entire envelope before a single unit is raised off the ground. Range IS a constraint, it is a
tight one, and rule 7 is what measures it.

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

**7. Every enemy UNIT must be REACHABLE, and height is what makes one unreachable.**
Added 2026-08-06, after the first six passed a level that could not be won. Victory is "every
enemy unit dead", so an enemy outside the ballistic envelope makes a level unwinnable at any skill
level, forever.

The envelope is **not** `MaxRange45`. The minimum speed that reaches a point at (dx, dy) is
`v² = g·(dy + √(dx² + dy²))`, so a target lifted onto a structure costs range twice — once for the
climb and once for the longer slant. **A garrison 4.5 units up at dx 15 needs 100% power; the same
garrison on the ground needs 85%.** Rules 1–6 measure framing and HORIZONTAL separation and cannot
see this at all.

Checked by `BalanceAudit.ReachRule`, which `LevelComposition` calls so the audit and the inspector
cannot disagree. Two thresholds, and both ranks are reported because a volley leaves EVERY player
unit at one velocity:

- **The FRONT rank over 100% is an ERROR** — nothing on the player line can reach the target.
- **The BACK rank over 100%, or the front rank over 92%, is a WARNING** — either part of every
  volley is wasted for the whole battle, or there is no headroom left and every miss is short,
  which is the shape that reads as "my shots pass through them".

L3 and L5 carry accepted warnings; both beats are explicitly about height, and the reason is in
their `designNotes`, which is where a bent rule belongs.

**8. Every GROUND unit must stand CLEAR of every enemy structure's collision box.**
Added 2026-08-11, after rules 1–7 passed four levels whose heroes had been placed inside a wall.

A structure blocks projectiles as an axis-aligned box **`hitWidth` wide** (falling back to `size`
when unset), rising from its base to `deckY`. **`hitWidth` is not the width of the building you
see, and a structure's ANCHOR is not its EDGE.** L6's keep is drawn around x 6 and blocks from
**x 3.88**; heroes placed at 4.3 — "in front of the keep" — were inside it. L12's were 1.71 deep.

A unit inside that box can only be hit by clearing the box top and then reaching the ground within
the same fraction of a unit. L6 wanted a **2.0 drop in 0.02 of travel**: a near-vertical plunge, at
a level whose whole geometry is built around a flat arc. Perversely, moving a defender OFF a roof
and onto the ground beside it can make it harder to hit, because a garrison on a deck stands above
every box and takes an ordinary shot.

**Rule 7 cannot see this and neither can any of 1–6.** Rule 7 asks whether the roster has the
POWER to reach a point; there is nothing in its model about what is IN THE WAY. **Reach and a clear
line are different questions**, and rules 1–7 only ask the first.
**Rule 7 also still measures TURN 0 ONLY**, which rule 8 no longer does — so a wave or boss spawn
placed out of the ballistic envelope is not caught by anything. That is not theoretical: the first
attempt at L11's fix moved its wave to anchorX 9 and put a heavy at dx 20.40 against a 20.25 flat
maximum, and `LevelComposition.Report` stayed green. It was caught by measuring the arrivals
directly. Extending rule 7 the same way is an open job. All twelve levels passed all
seven rules while three of them had heroes embedded in masonry.

**The rule covers TURN 0 AND EVERY MID-BATTLE ARRIVAL** — boss phases and reinforcement waves —
since 2026-08-12. It used to read the initial state alone, and that hole shipped four embedded
units across three levels plus a shadowed boss: **L6**'s heavy escort had two of three men inside
the Mountain Bunker, **L10** and **L11**'s turn-4 waves each landed a heavy inside a Garrison Post,
and **L12**'s Sovereign spawned in the gate's shadow, which is how Rob found it — a finale that
demands a near-vertical plunge after your tank shells are gone. Arrivals are placed through the
same `LevelBuilder.BuildUnits` call `BattleTick.Spawn` makes, so the check measures the positions
the game will actually produce.

**A boss phase's own trigger structures are exempt for that phase**, because they are provably
rubble by the time it fires. L12's Sovereign spawns dead centre of the citadel it bursts out of;
flagging that would assert a state the game can never be in. A reinforcement wave has no trigger,
so it is judged against every structure standing — the worst case, and the one a wave can land
into.

Two further exemptions, both semantic rather than tolerances:

- **Garrisoned units**, which stand on a deck above every box and are meant to.
- **ADVANCING units** (`advancePerTurn > 0`), which walk out of the box on their first move. L9's
  shield bearers start 0.01 inside the bunker purely on formation jitter and are hittable from
  turn one. A STATIC unit gets no such reprieve.
  **This exemption does not verify that the unit actually leaves promptly**, and it is worth
  knowing before leaning on it: L11's wave, had it been given an advance rather than moved, would
  have started 0.71 deep and needed THREE turns at 1.2 a turn to clear the box. "Advancing" is not
  the same claim as "hittable soon", and the check does not currently tell them apart.

Only a box at **lower x** than the target can shadow it, since the shot travels left to right — so
the practical rule when placing a ground group is: pick the leading EDGE of the foremost structure
and stay in front of it.

This was not only the hero pass's bug. The check found **four static riflemen the campaign had
already shipped** — two on L9 inside the mountain bunker (0.46 and 0.74 deep) and two on L10 inside
the outpost (0.13 and 0.45) — every one a unit the player could not hit without the same plunge.

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
