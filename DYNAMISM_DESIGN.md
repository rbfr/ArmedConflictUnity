# DYNAMISM_DESIGN.md — ArmedConflict

Reference spec for the gameplay-dynamism layer. Planned 2026-07-19.
Companion to `GAME_DESIGN_LOCKS.md` and `PROGRESSION_DESIGN.md` — nothing here overrides a
lock. The core verb (drag → volley → watch) stays untouched; this plan attacks the three
repetition sources the progression layer didn't: **every turn has the same shape, there is
no in-battle decision beyond the drag, and it's always green-vs-red on the same field.**

**Build priority:** `PRODUCT_DIRECTION.md` places this layer in **Tier 1** (after the
12-level campaign spine and victory/meta juice). Do not start ammo/events/factions as the
main thread while the player-facing map is still 7 campaign levels + test rigs with a
silent victory screen.

## Design pillars (decided 2026-07-19, treat as soft locks)

- **Every turn contains a decision beyond the drag.** The drag stays sacred (no landing
  marker, no aim assists — locked); new layers change WHAT you fire and WHY, never HOW
  aiming works.
- **Unlock once, then free.** Special ammo types are bought once with coins and are then
  freely selectable every volley. Ammo is a skill/decision layer, not a rationed resource —
  no per-shot costs, no stock to run out of. (Consumables stay the rationed layer; ammo is
  the permanent one.) Preserves the skill-first pillar: no level may require special ammo.
- **Variety on three axes**: what you fire (ammo types), what the battle throws at you
  (mid-battle events), and what you see (factions, biomes, silhouettes, cosmetics).
- **Everything new is also an economy hook.** Ammo unlocks, cosmetics, and faction-stage
  content all feed the coin loop from `PROGRESSION_DESIGN.md` Phase 1.
- **Legible effects** (same principle as stars/upgrades): every ammo type and event must be
  describable in one sentence a player could say out loud.

## Phase 0 — Stabilize the L24 branch (before any new work)

The `progression-phase1` branch carries the L24 camera/zoom fixes (pan-back timing,
shooter-centered zoom margin, heli margin, `CAMERA_GAMEPLAY_Z` 10→22, decoupled windup
timer) plus `EnemyWindupCameraTest`. Before redesign work starts:

1. Run the remaining manual checklist from `L24_VOLLEY_HIT_CAMERA_PLAN.md` §4:
   L24 turn 2+, heli levels L4/L7/L10, one non-melee level (L1) — on-device recordings.
2. Spot-verify "shots land short but still register damage" is genuinely burst-radius
   tolerance and not a separate bug.
3. Update the L24 plan doc's status/DoD, commit, merge.

Known debt carried forward (acceptable, documented): `CAMERA_GAMEPLAY_Z = 22f` is a tuned
bound for current layouts, not a derived worst case — any future level with a wider enemy
spread than L24 must re-check zoom framing on-device. Camera framing/zoom math in
SceneHost remains untestable by unit tests; extract further pure functions (like
`enemyWindupCameraTargetX()`) opportunistically when touching that code.

---

## Phase A — Ammo / volley types (first slice: biggest repetition-killer per effort)

Before each drag the player may switch the volley's ammo type via a small HUD selector
(near the aim area; selection persists across turns; no mid-drag switching). One choice,
every turn, changing what the volley *does* — the drag itself is unchanged.

| Type       | One-sentence effect                                                    | Unlock (placeholder) |
|------------|------------------------------------------------------------------------|----------------------|
| Standard   | Today's volley, unchanged.                                             | always free          |
| Incendiary | Hit units catch fire and take burn damage at the start of the enemy's next windup. | 300c |
| AP         | Double damage to structures and armored units, reduced vs. soft targets. | 400c |
| Cluster    | Wider target zone (more enemies hit), each hit weaker.                 | 500c |

Design notes:

- **Incendiary** — burn is a single legible tick ("burning units take X when their turn
  starts"), not a per-second DoT: one damage event, applied in the EnemyWindup transition,
  visible as a flame flicker on the unit. No new per-tick damage pipeline.
- **AP** — a damage multiplier table on the payload (vs. structure / vs. armored HP / vs.
  unarmored), applied in `CollisionSystem`. Makes structure-heavy and heavy-unit levels
  play differently from rifle-line levels — target-reading becomes the skill.
- **Cluster** — raises the volley's effective `zoneHalfWidth` (still real-target
  convergent fire per the lock — never a blind fan), so the volley's rounds spread across
  more distinct targets. The wide-formation counter-pick. (Pre-2026-07-25 this was described
  as splitting "fire-team volume"; fire teams are gone — one round per unit — so the effect
  is simply that more aim points fall inside the wider zone.)
- **Enemy stays on Standard for now**; Phase D can give later factions a signature ammo
  as identity (e.g. a faction whose volleys burn) — flavor through the same pipeline.
- Tank shell / splash weapons: ammo type applies to the whole volley including the shell
  (AP shell = the bunker-buster fantasy). Heli sniping flat-shots inherit it too — no
  special cases.

Implementation notes:

- Payload enum on `ProjectileEntity` + damage/zone modifiers in a new
  `AmmoDefinition` (stats in Definition classes — locked convention).
- Burn/flame VFX and any new impact effects use BOUNDED round-robin slot pools in their
  own id bands (hard rule — see CLAUDE.md).
  **The flame SHIPPED in the Unity build 2026-08-09 and was confirmed on device 2026-08-10** —
  two flickering tongues per burning man, driven straight off `GameState.BurningEnemyIds` so it
  needs no new tick state, bounded pool, guttering out over half a second and following the
  ragdoll of anyone the burn kills. Full write-up in `HANDOVER.md`. Note the burn's window makes
  it a TELEGRAPH as well as a cue: the fire is up for the whole post-volley pause, saying these
  men are about to take damage.
- Unlock state + selected type persist in `ProgressStore`/`EconomyStore` like unit
  unlocks (Phase 1c pattern).
- Damage math (multiplier tables, cluster zone scaling, burn tick) lives in pure
  functions with unit tests — this layer must not join the untestable SceneHost class.

## Phase B — Mid-battle events (battles change shape while you play)

Two events, both telegraphed one full turn ahead — surprise comes from *anticipating* the
change, never from being blindsided:

- **Wind shifts**: on wind-enabled levels the wind level can change between turns
  (HUD banner + windsock/particle read during the enemy windup: "wind rising →").
  Never changes mid-flight. Reuses the shipped Phase-3 wind system; the new part is the
  schedule (per-level `windSchedule` or seeded random walk) and the telegraph.
- **Enemy reinforcement waves**: generalize the boss `BossPhaseTrigger` machinery with a
  turn-count trigger (`arrivesOnTurn`) so non-boss levels can field a telegraphed second
  wave ("armor column inbound — 2 turns"). Turns some levels from "grind the line down"
  into "race the clock / choose what to kill first." Waves are enemy-side only, so star
  math (player-roster-based) is untouched.

Both are per-level data (`LevelDefinition`), introduced as stage signature mechanics per
the locked stage structure — not sprinkled everywhere at once.

## Phase C — Tactical consumables expansion

The Phase-2 consumable system (cap 2/battle, HUD-triggered, coin sink) gains tactical
options that interact with the enemy's turn, not just your own:

- **Smoke Screen**: the next enemy volley fires through smoke — its accuracy jitter
  radius is doubled (reuses the `EnemyAI.aimAt` jitter knob directly). The defensive
  answer to a scary windup you can see coming.
- **Overwatch Flare** (candidate, needs a fun-check): the next enemy advance turn is
  halved (`advancePerTurn` budget cut) — the anti-melee-rush pick on Frostline-style
  levels.

Same rules as existing consumables: single-use, bought with coins, selected at loadout,
cap 2/battle, never required to clear a level.

## Phase D — Visual variety (kill green-vs-red)

All four selected; ordered by leverage-per-effort:

1. **Enemy factions per stage** — each stage gets a `FactionDefinition`: name, uniform/
   gear palette, structure accent colors, and (later) a signature ammo/behavior flavor.
   Rendering rides the existing runtime color-override pipeline (`structureColors()`,
   per-side uniform/gear materials) — data + palette work, minimal new code. Stage select
   and level previews surface the faction identity.
2. **Biome/environment per stage** — backdrop painting, ground palette, prop set
   (desert / snow / urban to match existing stage themes). The painted-2D-backdrop
   architecture makes this almost entirely art. Frostline already proves the prop path
   (`PropPlacement`).
3. **New unit silhouettes** — 2–3 visually distinct types via the Blender templates
   (`build_units_v5.py` conventions): e.g. mortar team (crewed weapon silhouette),
   marksman (prone/long rifle), engineer. They join the loadout/unlock economy as new
   "saving up for" targets (keep 1–2 locked per stage — Phase 1c principle). Ashfall's
   "nothing left to gate" gap gets filled by one of these.
4. **Player cosmetics** — camo/palette sets for your own army, bought with coins.
   Pure vanity coin sink through the same per-side material override; no gameplay effect
   ever. Persist in `ProgressStore`.

---

## COVERAGE REGRESSION — 2026-07-29, CLOSED same day

The campaign rebuild on 2026-07-29 scrapped the previous 25 levels (authored against 1x
structures, they overflowed the frame at STRUCTURE_SCALE 2.5) and the six first-principles
replacements taught the structure vocabulary only. Every phase below stayed shipped and working,
but for a few hours **no level exercised any of it**: heliChance / windAccelZ / bossPhases /
reinforcementWaves / staticCamera were each used by zero levels, and seven unit definitions were
orphaned. It was purely level DATA that no longer existed — nothing needed re-implementing.

**Closed by the campaign expansion to 12 levels (L7-L12, stages Ashfall City and Frostline).**
Each new level reintroduces one dead mechanic rather than being authored for its own sake:

    heliChance          L11 (DATA ONLY — see below)     windAccelZ          L8
    bossPhases          L9, L12                         reinforcementWaves  L10
    staticCamera        L12
    EnemyShieldBearer   L7, L9, L11, L12    EnemyHeavyRifleman  L9, L10, L12
    EnemyRocketTrooper  L8              CityHallGuardian    L9 (boss phase)
    NestCommander       L12             FrostlineCommander  L12

The two new stages also restore the unreachable faction palettes: `factionPaletteFor()` keys off
stage id, so `stage_city` (Ashfall Militia) and `stage_frostline` (Frost Legion) had been dead
code while the campaign was six levels long. The natural structure set (cliff / ridge / mountain
bunker) also entered the campaign for the first time, on L10 and L12.

**The helicopter is the one mechanic still dark**, and NOT because of level data. `HELI_ENABLED`
(GameViewModel) is `false`, and every heli path is gated on it, so `heliChance` is inert on any
level. Verified on device 2026-07-29: six enemy turns on L11 at `heliChance = 0.35` produced no
gunship. The field comment there reads as temporary sequencing ("while that camera work is
tuned"); the actual reason, per the user, is that **the camera was having to do too much work**
with a gunship in the mix. That is a load/complexity problem, not a scheduling one, so it does not
expire when the camera rearchitecture locks — do not flip the flag on the assumption the blocker
is stale. L11 keeps its `heliChance` so it is ready the day the flag flips, but it currently plays
as a conventional level.

**Still orphaned, deliberately:** `FirebaseCommander` and `CitadelSovereign`. The first is the
L6 stage-finale boss retrofit — L6 was left alone because it had just been playtested and
measured — and the second belongs to the unbuilt stage 5 (`stage_citadel`, Sovereign Guard).

Check this table again after any future campaign teardown — it is exactly the kind of thing
that survives a rewrite silently.

## Status: build order complete (2026-07-21)

Every slice in the Build order table below is shipped — A, B1, B2, C (Smoke Screen +
Overwatch Flare), D1–D4 — all verified on-device, per each phase's "shipped notes" section
above. `progression-phase1` no longer exists as a branch (merged and deleted); all work
happens directly on `main`.

**Camera/unit-size rearchitecture — investigated 2026-07-21, both leads ruled out.**
Chasing D3/D4 legibility surfaced a structural problem, not a bug: `SceneHost`'s camera
uses a fixed 90° vertical FOV (to widen the horizontal FOV for portrait army-width fit —
see `CAMERA_FOCAL_LENGTH`), which wastes most of the screen on sky/mountains above and
dirt below regardless of zoom distance (measured `gameplayCamZ`=13.8 on L24, well under
the 22 cap). A `UNIT_SCALE_UNITS` bump (0.68→0.77) bought a modest, diminishing-returns
improvement on 2026-07-20. Two further leads were prototyped this session and both turned
out not to help:

1. **Letterboxing the 3D viewport** (rendering into a shorter strip, raising focal length
   to preserve horizontal FOV) was implemented, verified correct on-device (ground
   alignment held at every zoom), then reverted. Reason: this game's 3D scene has no
   skybox/ground-plane/environment (`isOpaque = false`, nothing set up in `SceneHost.kt`)
   — the "wasted" sky/dirt space was ALREADY just `BattleBackground`'s full-screen 2D
   painting showing through transparent pixels, not rendered 3D content. There was nothing
   to reclaim, so the change was a verified visual no-op. Don't re-attempt this without
   first adding actual 3D background geometry that would benefit from the crop.
2. **Tightening the front-line gap** (player vs. enemy `anchorX` starting positions) was
   prototyped on L1 (pulled ~2 units per side, gap 13.4→~9.4 world units). Measured
   `gameplayCamZ` dropped 13.83→10.5 (confirmed via temporary debug log) and units were
   visibly, meaningfully bigger on-device. BUT: on-device playtest found projectiles read
   as noticeably weaker/slower — confirmed via `TrajectoryPhysics.velocityFromDrag`
   (`TrajectoryPhysics.kt`) that launch velocity is pure drag-pixel math, completely
   independent of level geometry, so the same arc now only has to cross a much shorter gap
   to reach the enemy. The instantaneous physics didn't change; the *satisfying long lob*
   did. This is core to the game's identity (drag-and-release artillery), so the
   experiment was reverted rather than compensated around. **Any future attempt at this
   lever needs an answer for the arc-feel regression FIRST** (e.g. retuning
   `PROJECTILE_SPEED_SCALE`/gravity to preserve flight *time* at shorter range, not just
   preserving `gameplayCamZ` math) before touching per-level `anchorX` data again.

Also found and fixed independent of either lead: `BattleScreen.kt`'s ground-impact circles
used a hardcoded, fully independent `0.685f`/`30f` pair that had drifted ~4x out of sync
with the shared `HORIZON_FRACTION`/`CAMERA_LOOK_AT_Y` projection every other overlay uses —
now shares the same formula.

Net conclusion (superseded below): making units read as bigger is a harder problem than
either "crop the viewport" or "zoom the camera closer" — both are either no-ops or trade
away something else load-bearing.

**Resolved, same day (2026-07-21), via a reference screenshot.** The user pulled up an
Archery Bastions screenshot showing the actual lever this session's writeup had flagged as
untried: "accepting a narrower horizontal FOV/less generous army-width margin as a
deliberate scope trade." That reference camera never tries to fit both full army spans in
one frame at once — it crops tight to whichever side is on screen (the player's own line
isn't shown at all; only arrows entering from off-screen imply it). `SceneHost`'s
`gameplayCamZ` was rebuilt around the same idea: instead of one level-wide zoom sized to
the worst case across BOTH sides (`maxOf(playerSpan, enemySpan, shooterReach,
volleyReach)`), the half-width is now computed PER `TurnPhase` — `Aiming` frames only the
player span, `EnemyWindup`/`PlayerScout` frame only the enemy span (+ `shooterReach` for
off-center ranged shooters), and `Resolving` frames whichever side is RECEIVING the volley
(a one-time snapshot of `projectile.ownerIsPlayer` at phase entry, not reactive per-tick —
reactive would make the zoom crawl as the follow camera's tracked mean drifts mid-flight).
This let the old `volleyReach` term be deleted outright: it existed only to keep the
in-flight projectile's WORST-CASE position in frame at a single fixed zoom; now Resolving
gets its own tight zoom sized to the receiving side, so a shot's local neighborhood is all
that needs to fit. The X-pan choreography (`playerCamX`/`enemyCamX` swap per phase) already
worked this way — this just brought Z (zoom) in line with it. `Preview` phase is
untouched (still frames both armies — that's level-browsing context, not core gameplay).

Verified on-device across all four `TurnPhase` values on L1: units, guns, ammo-select
buttons, and even the small "ARMED" HUD badge are all now clearly legible at a glance,
comparable in scale to the reference. Trade-off, as anticipated: you no longer see both
full army lines in one frame during gameplay, only in Preview — this was the deliberate
scope trade the prior writeup called out as the last untried lever, and it's what the
reference itself does.

---

## Build order & status

| Slice | Contents | Status |
|-------|----------|--------|
| 0 | L24 checklist run, docs updated, branch merged | shipped (2026-07-19); `progression-phase1` merged to `main` and pushed 2026-07-20 |
| A | Ammo types: 3 payloads + selector HUD + unlocks + pure-function tests | shipped (2026-07-20) |
| B1 | Wind shifts (schedule + telegraph) | shipped (2026-07-20) |
| B2 | Reinforcement waves (`arrivesOnTurn` trigger + telegraph) | shipped (2026-07-20) |
| C | Smoke Screen + Overwatch Flare | both shipped (2026-07-20, 2026-07-21) — in UNITY, **Smoke only** (2026-08-10); Overwatch held, see below |
| D1 | Enemy factions per stage (palettes + identity surfacing) | shipped (2026-07-20); **ported to UNITY 2026-08-12** — TWO factions, one per stage, device-confirmed. See below |
| D2 | Biome art per stage | rescoped + shipped as prop-dressing pass (2026-07-20) — see below |
| D3 | New unit silhouettes (2–3 Blender builds + economy wiring) | shipped (2026-07-20) |
| D4 | Player cosmetics | shipped (2026-07-20); **ported to UNITY 2026-08-12** — four camo sets, device-confirmed. See below |

Each slice ships independently and is playable on its own. A→B→C is the gameplay track;
D1/D2 can run in parallel with any of it (art-heavy, low code risk). Update the Status
column as slices land; promote placeholder prices/values to real numbers here once tuned.

## Phase A — shipped notes (2026-07-20)

Implemented as spec'd: `AmmoDefinition.kt` (pure math + the 4 definitions), `ProgressStore`
unlock-once/selected-type persistence, `EconomyStore.purchaseAmmo`, HUD chip row in
`BattleScreen` (shown only once a 2nd type is owned), AMMUNITION section in
`LoadoutScreen`, and 7 unit tests (`AmmoTest.kt`) covering the multiplier math and the
Incendiary impact-marking in `CollisionSystem`.

Verified on-device (fresh install, seeded coins): bought Incendiary in the loadout screen
(balance deducted, flipped to "✓ Unlocked"), selected it in-battle via the HUD chip
(highlights orange), fired a volley on an 11-enemy level — 3 died outright, 5 marked
burning, "🔥 5 burning" appeared live in the HUD. At the next enemy windup the burn tick
applied (that pass wounded rather than killed — expected, burn=6 vs full-HP riflemen) and
the indicator cleared correctly. A separate small-roster run (L1, 6 enemies) had the burn
tick finish the whole enemy side before the enemy ever got a windup — burn-can-kill
confirmed working, not just spec'd.

Answered open questions from the original spec:
- **Burn can kill** — confirmed both in `AmmoTest` (burnDamage ≥ Sniper's 8 maxHp) and live
  (L1 battle ended before an enemy volley ever fired).
- **AP's soft-target malus**: kept as spec'd (0.75× vs units) — not separately playtested
  for balance; still a watch-item once more levels get played with it equipped.
- **Cluster's fire-team interaction**: implemented as "widen the zone, soften the round" —
  concentration/spill behavior inside `applyBurst` is untouched by ammo type.

**Follow-up pass (2026-07-20)**: bought AP (400c) and Cluster (500c) in the loadout screen
on a fresh level (both flipped to "✓ Unlocked", coins deducted), confirmed all 4 chips
(STD/INC/AP/CLU) render and highlight correctly in-battle, then fired one live volley with
each. AP: no crash, enemy count dropped, and the level's structure (36 HP) was destroyed
outright by the 2× multiplier — HP line vanished from the HUD, full turn resolved cleanly
afterward (heli appeared, "Your turn" returned normally). Cluster: fired cleanly, enemy
count ticked down, no crash. All four ammo types are now confirmed working end-to-end, not
just code-path-identical by inspection.

## Phase B in UNITY — the schedule and the telegraph (2026-08-07)

The notes below are the ANDROID build's. The Unity port inherited the mechanism intact and
nothing had ever driven it; Phase D wired arrival, and this is the other half.

**The telegraph counts down, and the count is composed, not authored.**
`ReinforcementWave.telegraphText` (a whole sentence with the number baked in) is now
`telegraphLabel` — what is coming, no number — plus `telegraphLeadTurns`.
`EventSystems.TelegraphLine` builds the line per turn, so a 2-turn lead reads
"Elite squad inbound - 2 turns" and then "- 1 turn". Authoring the number instead put it one
copy-paste from disagreeing with `arrivesOnTurn`, with nothing checking it, and held the same
figure for the whole warning — which tells the player the clock has stopped.
`ReinforcementWaveBeat` takes the lead as an argument; below 1 it is clamped, not honoured,
because pillar 7 is not something an individual wave gets to opt out of.

**The schedule is two levels, both in stage 2, both 2-turn leads.** L10 Rubble Yard is the
beat-chart's "reinforcement race" and its lead went 1 → 2 to match the chart's own words
("armor in 2 turns"). L11 Oceanfront was the only stage-2 level the audit called NO MECHANIC —
its beat offers a heli it cannot have — so its "else elite push" fallback is delivered as a
telegraphed wave rather than three more bodies in the opening formation. **L12 was deliberately
NOT given one**: it already combines the boss phase with the charge, and its designNotes record
a device-measured margin against the 288 siege capacity that adding enemies would quietly undo.

**One real bug found, and one false alarm worth keeping:**

- **A glyph-coverage check that is asked of the FONT.** `PortSelfTest` now runs every authored
  string that reaches TMP — level names and goals, announcements, telegraph labels, unit names —
  through `TMP_Settings.defaultFontAsset.HasCharacter`. **It was written as an "ASCII only" check
  first, on the strength of CLAUDE.md's note, and flagged 23 strings: every campaign `levelGoal`
  and all 17 test-rig names. A device screenshot then showed an em dash rendering perfectly in the
  loadout panel** — LiberationSans SDF covers Latin-1 and General Punctuation, and only lacks the
  symbols (`★`, `◆`, emoji, arrows) the HUD already draws as sprites. All 23 "fixes" were reverted.
  The lesson is the one this project keeps relearning in new costumes: **a check written against a
  note in a doc asserts the note, not the behaviour.** What it does catch for real is wind's
  announcement strings, which came over from the Kotlin with a wind emoji and two arrows.
- **Rule 7 stopped at the opening roster.** `BalanceAudit` measured reach against the units
  present at level build, so a wave could be authored past maximum range and the level would pass
  every rule-7 check while being unwinnable from turn 4 — the L7 bug, one turn later. The audit now
  STEPS the tick to the wave's arrival and re-runs the reach rule on the real spawned positions,
  rather than re-deriving them from `anchorX` and having two implementations to disagree. Proved
  by pushing L11's wave to x 22: `121% power ... UNWINNABLE`, 2 errors. At the authored x 8 both
  wave levels clear (L10 89%/97%, L11 89%/98%).

**Wind is still not shipped and the reason is not the schedule.** `windAccelZ` drifts the round in
Z while the collision test is X/Y only, so wind cannot change what a shot hits. A wind schedule
would telegraph a change the player cannot feel, which is worse than no wind at all. Its
announcement strings did get de-emoji'd (they carried a wind glyph and two arrows, none of which
LiberationSans SDF has — three blank boxes waiting for whoever wires it up).

## Phase B — shipped notes (2026-07-20)

**Wind shifts (B1)**: `GameState.windAccelZ` is now the live per-battle value (was a static
`LevelDefinition` read at two call sites — aim-preview arc and projectile physics — both
switched to the state field). Rolled once per Enemy→Player handover on wind levels only
(`base != 0f`); ±0.35 gust, clamped to [0.4×, 1.8×] of the level's base magnitude, sign
always matches base (a level's wind gusts stronger/weaker, never flips or dies). Pure math
extracted to `nextWindAccelZ()` (`WindShiftTest.kt`, 6 cases). Telegraph is peripheral HUD
text ("🌬️ Wind rising →" / "🌬️ Wind falling ←"), same spot/style as the Phase A burn
indicator — deliberately not the big boss-style banner, since a gust is a texture change,
not an event.

**Reinforcement waves (B2)**: new `ReinforcementWave` data class + `LevelDefinition
.reinforcementWaves`, generalizing `BossPhaseTrigger`'s spawn mechanism from a
structure-destruction trigger to a turn-count trigger (`arrivesOnTurn`, checked against a
new `GameState.turnNumber` incremented at the same handover). Reuses the existing
`buildUnits()` spawn path and the boss banner UI as-is — no new spawn code, no new HUD
component. Turn-arithmetic extracted to `reinforcementWaveBeat()` (`ReinforcementWaveTest
.kt`, 3 cases). Shipped example: L9 "Signal Intercept" gets a 4-unit armor column on turn 3
(telegraphed turn 2), anchored past the existing formation's max x — same off-edge-entry
convention as boss waves.

**Verified on-device** (fresh install, seeded stars/coins):
- L9: fired volley 1 → enemy handover → turn 2 showed "Armor column inbound — 1 turn" in
  the HUD banner exactly as authored. Fired volley 2 → turn 3 showed "ARMOR COLUMN
  ARRIVING!" and enemy count jumped 5→9 (the wave actually spawned).
- L7 (Twin Towers, windAccelZ=1.2): two full playthroughs. First 3★-cleared in 2 turns —
  too few handovers to reliably roll a 35%-chance event, a false alarm not a bug (confirmed
  by adding a temporary debug log: the RNG rolls were happening every handover, just
  landing above 0.35 those two times). Second run deliberately fired weak volleys to
  stretch the battle; by turn 5 the log showed roll=0.168 (below the 0.35 threshold),
  wind dropped 1.2→0.85, and "🌬️ Wind falling ←" appeared in the HUD on the same tick —
  confirmed end-to-end, debug log then removed.

## Phase C in UNITY — Smoke shipped, Overwatch HELD (2026-08-10)

**Smoke Screen shipped and was confirmed on device**: armed from the HUD (`Smoke / ARMED` — the
button stays visible while armed, which is the whole point of the arm/spend split below), and
spent at the enemy volley it blinds. `BattleTick.FireEnemyVolley` passes
`SmokeScreenJitterMultiplier` into the already-ported `EnemyAI.AimAt`. `PortSelfTest` asserts the
effect the way the player meets it — the standard deviation of where 40 volleys of rounds actually
LAND, which widens by well over a third — rather than asserting that a 2f was passed somewhere.

**OVERWATCH FLARE IS NOT BUILT, and this is a decision, not an omission.** It halves the enemy's
next advance budget. In this port **nothing ever advances**: `UnitEntity.AdvancePerTurn` is
imported and read only to count advancers for a threat line, `AdvanceRemaining` is written
nowhere, there is no march step for enemies, and `SkirmishEntity` — the melee an arrival resolves
into — is defined, counted, and never created. Advancing squads and melee are an EIGHTH dead
system in this port.

A 200-coin item that changes nothing the player can feel is worse than no item: it teaches that
coins are decorative. That is the same reasoning already applied to wind ("do not author a wind
level or a wind schedule until wind does something"). `PortSelfTest` asserts BOTH halves — that
Overwatch is not sold, AND that no enemy ever banks an advance — so the day advancing squads are
ported the check goes red and adding the catalog entry is the fix.

## Phase C — shipped notes (2026-07-20)

**Smoke Screen**: new `ConsumableType.SmokeScreen` (200c), same cap-2/battle inventory
system as the Phase-2 items. `EnemyAI.aimAt` gained a `jitterMultiplier` param (default
1f); Smoke Screen passes 2f for exactly one windup. The effective-radius math is extracted
to `EnemyAI.jitterRadius()` so it's testable without touching `Random` (`SmokeScreenTest
.kt`, 2 cases).

Arm/consume semantics follow **Airstrike's** pattern, not Trauma Kit's: armed via
`toggleSmokeScreenArmed()` (toggle only, no spend), and spent — `ProgressStore
.spendConsumable` + the `loadedConsumables` decrement — only at actual use, inside the
Player→Enemy handover where `EnemyAI.aimAt` is called. A first implementation spent
immediately on arm (Trauma Kit's instant-effect pattern) and broke: the HUD button's
visibility is gated on `loadedConsumables[type] > 0`, so spending at arm-time zeroed that
count instantly and the button vanished the moment it was tapped, with no way to see or
un-arm the "ARMED" state. Caught on-device, not in the test suite (a Compose visibility gate
isn't unit-testable without also standing up the UI layer) — fixed by moving the spend to
consumption time, same as Airstrike already does.

**Real bug found and fixed in the same pass**: adding Smoke Screen as a 4th consumable
type pushed `LoadoutEditorOverlay`'s total content past one screen's height. The overlay's
outer `Column` had no scroll of its own (only the roster sub-list did, via a fixed-height
`LazyColumn`), so `Confirm`/`Cancel` were laid out below the bottom of the screen and
**absent from the compose tree entirely** — not just clipped, unreachable by any input.
Fixed with `fillMaxHeight(0.9f)` + `verticalScroll` on the outer `Column` (nested cleanly
under the roster's own scroll, same axis, bounded inner height). This heals itself for any
future addition to the loadout screen (D3's new unit silhouettes, D4's cosmetics) — worth
remembering if a locked device session ever finds Confirm "missing" again.

**Verified on-device**: bought Smoke Screen, equipped it (post-scroll-fix, Confirm
reachable), began a battle, armed it from the HUD ("Smoke (1)" → "Smoke (ARMED)"), fired,
and the enemy's next turn passed with zero player casualties and the button correctly
gone (spent).

**Overwatch Flare — shipped 2026-07-21**: same instant-arm shape as Smoke Screen exactly
(`ConsumableType.OverwatchFlare`, 200c), consumed at the SAME Enemy-windup handover where
`advanceRemaining` gets banked for every advancing group (`advancePerTurn > 0f`) — halves
that one turn's budget via a new pure `EnemyAI.advanceBudget(base, halved)` (`OverwatchFlareTest.kt`,
3 cases). HUD button mirrors Smoke Screen's ("Overwatch (N)" → "Overwatch (ARMED)").
`Consumables.all` and the loadout screen's generic `CONSUMABLES` iteration needed zero
changes — same reason D3's units needed no `LoadoutScreen` changes.

**Verified on-device**: bought Overwatch Flare (200c) on L8 "Bunker Busters" (its assault
shield-bearer group has `advancePerTurn = 2.2f`), equipped it, armed it from the HUD
("Overwatch (1)" → "Overwatch (ARMED)"), fired a volley. A temporary debug log at the
banking site confirmed `flareConsumed=true banked=[1.1, 1.1]` — exactly half of 2.2 for
both shield bearers — before being removed; the enemy turn then resolved with no crash and
the button correctly gone (spent). This closes out Phase C in full.

## Phase D3 — shipped notes (2026-07-20)

Three new player-side units, all via `build_units_v5.py`'s `core()`/`finish()` conventions
(no new SceneHost code needed — the existing "trim*"/"accent*"/else material-by-node-name
convention and generic `Roster.entries` iteration in `LoadoutScreen` picked them up with
zero rendering changes):

- **Marksman** (`unit_marksman.glb`, 16 hp / 20 dmg / 0.5x structure, reuses
  `BulletVariant.Sniper`'s tracer): long-rifle precision, hits harder than the Sniper at
  the cost of being a hair tankier, not more fragile.
- **Engineer** (`unit_engineer.glb`, 48 hp / 6 dmg, `ProjectileType.Grenade` splash 0.5 /
  3x structure): demolition specialist — tankiest of the splash classes so it can survive
  getting into range.
- **Mortar Team** (`unit_mortar.glb`, 20 hp / 8 dmg, `ProjectileType.Shell` splash 1.2 /
  2x structure): crewed indirect fire, widest splash of any infantry class, fragile crew.

Unlock prices 300/350/450c (`Roster.kt`), point costs 3/3/4 — Mortar Team's 4pt cost is
deliberately above every other non-boss unit, keeping it the "saving up for" target Phase
1c principle asks for. Not retroactively wired into Ashfall's `unlockRewardId` gate (left
as the doc's own "nice-to-have, not part of D3's own scope").

**Verified on-device** (fresh install this session, seeded 5095 coins from a prior run):
bought all three in the loadout screen (each flipped from lock-icon "Buy" to a +/- stepper,
coins deducted 5095→4445 in order), freed 4 deploy points off Rifleman, fielded 1 Mortar
Team alongside 4 Rifleman/1 Marksman/1 Engineer (7/8 units), Confirmed (loadout overlay's
Phase-C scroll fix still reached Confirm fine with 3 more roster rows), began L1, fired one
live volley and let the enemy turn resolve — no crash, all three new silhouettes rendered
correctly in the mixed formation (screenshotted and visually confirmed), turn cycled back
to "Your turn" cleanly. `ProgressStore`'s `unlocked_units` set persisted all three ids
(`marksman`, `engineer`, `mortar_team`) correctly across the confirm.

Note: the level-select screen's coin readout showed a stale pre-purchase value (5095) after
returning from the loadout overlay, while the persisted prefs value was correct (3995) —
pre-existing display-staleness, unrelated to this unit-wiring change (coin balance itself
was correct; not investigated further, out of D3 scope).

## Phase D4 — shipped notes (2026-07-20)

Four cosmetic sets, unlocked once with coins and freely reselectable any time from the
loadout screen (not a per-volley HUD choice like ammo — the army's look doesn't change
mid-battle): Olive Drab (free, the pre-existing default, unchanged colors), Desert Tan
(300c), Urban Grey (350c), Arctic White (400c). `CosmeticDefinition.kt` stores colors as
plain `0xRRGGBB` Ints (data/ layer has no Compose dependency); `SceneHost.cosmeticColor()`
converts to `Color` where it's actually consumed. `ProgressStore`/`EconomyStore` mirror the
ammo-unlock shape exactly (`unlockedCosmetics`/`isCosmeticUnlocked`/`unlockCosmetic`,
`selectedCosmetic`/`setSelectedCosmetic` with the same validate-on-read fallback-to-default
pattern, `EconomyStore.purchaseCosmetic`). `CosmeticsTest.kt` (3 cases) covers Olive's
free/default status and that every set resolves to a distinct uniform color and back to
itself. Only unit uniform/gear recolor — player structures (the tank, etc.) stay their
fixed palette per `structureColors()`, matching D1's own choice to leave structures alone
for the enemy faction system; per the doc's own spec this is "pure vanity... no gameplay
effect ever."

**Two real bugs found and fixed on-device, both worth remembering for any future
loadout-screen toggle**:

1. **Olive Drab showed "🔒 Buy · 0c" instead of "✓ Selected"** — `LoadoutScreen`'s
   `isUnlocked` check only tested `def.set.name in unlockedCosmetics` (the persisted SET),
   but Olive is never actually stored there (same as Standard ammo) — `ProgressStore`'s own
   `isCosmeticUnlocked` has an explicit `set == CosmeticSet.Olive ||` special case that the
   loadout screen's local check didn't mirror. Fixed by adding the same `def.coinPrice == 0
   ||` short-circuit.
2. **Selecting and confirming Desert Tan did nothing in-battle** — units still rendered
   Olive green. Root cause: `val selectedCosmetic = remember { ... }` had no keys, so it
   only ever evaluated ONCE, at `SceneHost`'s first-ever composition. `SceneHost`/
   `BattleScreen` stay mounted continuously across the loadout-editor/level-select round
   trip (confirmed independently while chasing an unrelated camera-zoom bug this same
   session — see the `UNIT_SCALE_UNITS` comment) — a keyless `remember` there can never see
   a later change, no matter how many times you back out and re-enter a battle. Fixed by
   reading `ProgressStore.selectedCosmetic()` UNCACHED every recomposition, the same way
   `factionPalette` already does a few lines above it. The existing D1
   `bodyMaterialRef`/`gearMaterialRef` + `onFrame` reapply pipeline picked up the new
   `MaterialInstance` with no further changes — confirming that fix really does generalize
   to "any future per-node property that changes after first composition," as its own
   comment predicted.

**Verified on-device** (fresh install): bought Desert Tan (300c, "Buy" → "Select" → tapped
→ "✓ Selected", Olive correctly flipped off), Confirmed (Confirm/Cancel still reachable
with the loadout screen's 4th section added), began L1 — player riflemen and the tank crew
rendered in sandy Desert Tan uniform/gear while the player tank structure stayed its
original green, enemy formation stayed red, no crash. Fired one live volley through a full
turn (enemy volley resolved, camera panned back) — cosmetic held steady through combat,
no flicker, no reversion.

## Phase D4 in UNITY — 2026-08-12

**The NINTH dead system found in this port**: `CosmeticSet`, `ProgressStore`'s whole cosmetic
block and `EconomyStore.PurchaseCosmetic` were all ported on day one and a grep for callers
outside those three files returned nothing — no UI, no purchase, no repaint. The docs said
"shipped" because their status tables record the RETIRED ANDROID BUILD. Check the Unity callers,
not the status table.

**Four sets, the Kotlin's prices**: Olive Drab free, Desert Tan 300c, Urban Grey 350c, Arctic
White 400c. Bought and worn on the loadout screen, in a strip below the consumables — buying also
WEARS, unlike a consumable, because a camo has no cap to spend and no reason to be owned and not
worn. The selection persists immediately as a standing preference, like the ammo choice.

**It rides the faction repaint** (`Render.FactionPaint`), pointed at the player's army instead of
the enemy's. Uniform and gear; skin and per-class trim are shared across both armies and stay put,
and the player's STRUCTURES keep their fixed palette — confirmed on device, where the tank crew
changed camo and the tank did not.

**Olive is a real destination, not an absence.** Its catalog entry stores NO colour: selecting it
repaints back to the build-time material assets themselves. A default you can return to has to be
somewhere the repaint can go, which is the half a "paint it once at startup" implementation gets
wrong.

**Two palettes now compete, and the colours are boxed in by each other.** Urban Grey is the
squeezed one: cooler and it approaches Ironclad Legion's steel blue-grey (two grey armies on stage
2), darker and it approaches the player's own Olive (measured 0.159 — the 350 coins buy a set
nobody can see you wearing), warmer and it approaches Desert Tan. It is a LIGHT warm grey because
that is the gap. `PortSelfTest` measures every camo against every faction, against the enemy
default, and against every other camo.

**RIGS lends the whole wardrobe**, session-only, writing nothing — the same bargain the consumable
test supply strikes, and for the same reason: the release build is not debuggable, so confirming a
400-coin camo would otherwise cost a 400-coin re-earn on every build. Switching RIGS off takes the
borrowed camo back IN THE BATTLE YOU ARE STANDING IN, which is the same lesson the consumable
supply learned.

**Confirmed on device 2026-08-12**: Arctic White on the infantry and the tank crew with the tank
structure unchanged, the enemy still Redguard red, and the army back in Olive the moment RIGS was
switched off.

## Phase D1 in UNITY — 2026-08-12

**Two factions, not four, because this port has two stages**: Redguard (Valley Front) keeps the
existing enemy red UNCHANGED, and Ironclad Legion (Enemy Stronghold) is the Kotlin's steel
blue-grey. Stage 1 is where the player learns what "the enemy" looks like, so a faction system
whose first act is repainting the tutorial army teaches nothing — the identity is what is new
there, not the colour. Ashfall Militia and Frost Legion have no stage to live on here and were
not authored; the day a third stage exists, the two palettes are in this file's shipped notes below.

**The data lives where the campaign's data lives** — `FactionDefinitionSO` assets in
`Assets/GameData/Factions`, attached to `StageDefinitionSO.faction`, authored once by
`FactionAuthoring.Author` and edited directly afterwards. This is the one place the Unity port
disagrees with the Kotlin on purpose: there the palettes sat in the UI layer (`SceneHost.kt`),
because colour had always been a UI concern in that build. Here `Assets/GameData` is the source of
truth and a stage asset already exists to hang it on.

**What a faction may touch is deliberately narrow: the enemy's UNIFORM and GEAR.** Per-class TRIM
is shared across both armies — the uniform says which SIDE a soldier is on and the trim says which
CLASS he is, and letting a faction repaint the trim collapses the two readings into one. Skin,
structures and the player are untouched; the player's colour belongs to cosmetics (D4/Tier 2.4),
and two systems repainting one army is how the Kotlin build got a permanently stale uniform.

**The repaint is a POOL RESET, and that is the whole engineering risk.** Pools are built once and
survive a level switch, so `BattleRunner.ApplyFaction` runs on every `LoadLevel` beside the scorch
re-material and `TintShadows`. Renderers are classified ONCE against the two build-time materials
(`FactionPaint.Classify`) — matching on the material rather than on the `skin*`/`trim*`/`accent*`
mesh-name prefixes, because that convention belongs to the art pipeline and a second copy of it
can disagree with the first. `sharedMaterial`, not a `MaterialPropertyBlock`: every enemy on a
level wears the same uniform, so one material per faction keeps the army in one batch.

**Confirmed on device 2026-08-12, and the control is half the evidence**: L1 red → six ▶ steps to
L7 steel blue-grey → six ◀ steps back to L1 red again. A single paint proves nothing here; the
failure mode is a recycled slot keeping the previous stage's colours, and only the second and
third paint can show it.

**One check was wrong when written, in this file's favourite way.** The "these are visibly
different armies" check began as a LUMA-WEIGHTED rgb distance, which weights blue at 0.11 — it
scored steel blue-grey as 0.082 from the player's olive green and indicted a palette the Kotlin
shipped and played fine. Two tones of equal brightness and opposite HUE are trivially told apart,
and hue is the axis the whole feature works in. It is an opponent-colour distance now (brightness,
red-vs-green, blue-vs-yellow as three axes) and is only a coarse floor against someone authoring
two palettes that are genuinely the same colour. **A metric invented in the same hour as the thing
it judges is not the artefact — the device is.**

## Phase D1 — shipped notes (2026-07-20)

**Four factions, one per stage**: Redguard (Valley Front — the pre-existing red, unchanged
identity), Ironclad Legion (Enemy Stronghold — steel blue-grey), Ashfall Militia (Ashfall
City — charcoal/soot with an ember-orange banner), Frost Legion (Frostline — pale ice
blue-grey). `FactionPalette` + `factionPaletteFor(levelNumber)` live in `SceneHost.kt`
(shared with `BattleScreen.kt` — same package) rather than the data layer, matching where
`structureColors`/`unitTrimColor` already live: color palettes have always been a
UI-layer concern here, not baked into `UnitDefinition`/`StructureDefinition`. Only the
enemy's side-identity colors (uniform/gear/banner) vary — player colors and the per-class
trim colors stay constant, and structure palettes are untouched (they already carry more
information — building TYPE — than a flat faction tint would add). Level preview surfaces
the identity as "Enemy: <Faction>" under the level name, tinted with the faction's banner
color (brighter than the uniform tone, chosen for lit 3D materials, which read muddy as
flat HUD text). `FactionPaletteTest.kt` (3 cases) covers the stage→faction lookup as plain
JVM tests — `factionPaletteFor` resolved and ran under the unit-test classpath without
needing Robolectric, since it only touches `StageDefinitions` (plain Kotlin) and returns
Compose `Color` values, never touching an actual Android/Filament API.

**Real bug found and fixed in the same pass, twice**: `ModelNode`'s `apply` block — where
unit/flag materials get assigned via `rn.setMaterialInstances(...)` — runs ONCE, at first
composition of that node's id. Because unit and structure-flag ids are REUSED across
levels (the zero-disposal registry never resets them), an id first composed on an earlier
stage kept that earlier stage's faction color FOREVER, silently ignoring every later
`bodyMaterial`/`gearMaterial`/`trimMaterial`/banner param change — no crash, no log, just
a permanently stale uniform. Caught on-device: browsing from L1 to L7 in the preview
showed the correct "Enemy: Ironclad Legion" text but the background army still rendered
Redguard red. Found again independently for the flag-banner code path (same bug, separate
render function) once unit colors were fixed and Frostline's flag was still red. Fixed
both by tracking the material params in a `mutableStateOf` ref (updated every recomposition
via the existing `SideEffect` block, same pattern as position/visibility refs already used
here) and re-applying materials from `onFrame` — which DOES run every frame — whenever the
ref changes, rather than only at node creation. This is a structural gap that will bite
any FUTURE per-node property that's meant to change after a unit/flag's first appearance
(not just faction color) — worth remembering before adding another "recolor/reskin at
runtime" feature (D4's player cosmetics will hit this exact pattern).

**Verified on-device**: fresh install, browsed L1→L7→L13→L19 in the level-preview
carousel (the exact repro path — L1's units compose first, then browsing forward reuses
their ids). Confirmed all four "Enemy: <Faction>" HUD labels render with the right text
and color, and cropped close-up screenshots of the actual 3D army confirm each stage's
distinct uniform palette AND flag banner color, post-fix.

## Phase D2 — rescoped + shipped notes (2026-07-20)

Investigated before building anything: biome backdrops were already in good shape (7
distinct `BackgroundDefinition`s, each with genuinely different `SilhouetteStyle` drawing
code, not just a palette swap). The real, measurable gap was battlefield PROPS — Stage
3/4 dress every level, Stage 1 dressed only 1 of 6, Stage 2 only 2 of 6. Rescoped D2 to
close that gap instead of inventing new biomes (user confirmed). Dressed L1/L3/L4/L5/L6/
L7/L9/L11/L12 with the existing `PropPlacement` system — pure data, zero new code or
assets. See the commit for the level-by-level rationale; the one design-sensitive call
was keeping any new `barbed_wire` on L11 (has an active melee-advance mechanic)
cosmetic-only rather than accidentally adding a second slow zone.

**Follow-up, same session — three real bugs found investigating "why is the view zoomed
out" and its consequences**, none part of the original D2 scope but surfaced while
verifying it on-device:

1. **Formation row-depth was accidentally coupled to the unit-scale legibility bump** —
   fixed by splitting `Formation.grid`'s spacing into separate column/row terms (row
   depth reverted to its pre-session value). See `L24_VOLLEY_HIT_CAMERA_PLAN.md` §12.
2. **Enemy volleys were firing but invisible on-screen** — the zoom formula never
   accounted for how far a volley's projectiles actually fly (only each side's static
   formation footprint), previously masked on heli levels by an unconditional margin this
   session's earlier heli-zoom fix made conditional. Fixed with a `volleyReach` term. See
   `L24_VOLLEY_HIT_CAMERA_PLAN.md` §12 for the wide-zoom-forced verification that
   confirmed the hypothesis before shipping the real fix.
3. **Fire-team burst damage could visibly spill further than the tracer's landing
   point** — tightened `BURST_RADIUS` 0.9→0.62. A live follow-up report suggested it may
   still be occasionally visible at the new value; left as-is rather than tightening
   further without seeing the exact instance (next occurrence to be flagged live).

## Open questions (decide when the slice starts, not now)

- Incendiary burn magnitude and whether burn can kill (recommend: yes, kills — a burn
  that can't finish a 1-HP unit reads as broken).
- Whether AP's soft-target malus is needed at all, or AP is strictly-better vs. mixed
  comps (malus keeps Standard relevant; playtest it).
- Cluster's interaction with the fire-team concentration rule (spill earlier vs. more
  teams) — pick whichever reads better on-device.
- Whether wind shifts appear on non-wind stages as a rare event or stay a wind-stage
  signature only.
- Which new silhouette fills Ashfall's empty `unlockRewardId` slot.
