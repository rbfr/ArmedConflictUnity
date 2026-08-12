# UNIT_VARIETY_DESIGN.md — Army Presentation Redesign

> **ROSTER CUT 2026-08-05 — read the rest of this document as HISTORY.** The roster went from
> fifteen unit definitions to eight, and from ten classes carrying art to seven. **Marksman,
> Mortar Team and Engineer no longer exist**, nor do four of the five "commander" stat variants.
> The Marksman's numbers survive under the SNIPER's name; the Grenadier keeps the lobbed-explosive
> role for all three of the cut arcing units.
>
> Everything below still applies — the measurement method, the band profile, the seven attempts
> and why each fell short — but wherever it names a deleted class, that class is gone. The problem
> it describes is also now materially easier: **six silhouettes to separate, not ten.**

> **PORTED TO THE RIGGED HIERARCHY 2026-08-06, for the Unity build.**
> `tools/blender/build_units_rigged.py` rebuilds all seven classes on the animatable limb
> hierarchy, carrying every prop below with its measurements. Two things this document should be
> read with in mind:
>
> - **POSE is no longer an axis.** The lean, the hunch and the fore/aft stagger belong to the idle
>   clip now, and re-authoring them into the mesh would fight it. That costs less than it sounds:
>   by this doc's own record, every pose-only pass was reported as "the same soldier" at gameplay
>   scale. STANCE — how far apart the leg pivots sit — is still ours, because a joint's position is
>   free under rotation-only clips.
> - **The band profile is now a TOOL**, `python3 tools/measure_units.py`, with `--legacy` for the
>   v6 set. The rigged port reproduces the legacy spread almost exactly (hero 37/32/27 px against
>   38/33/29), which is the evidence that it carried the seven attempts across rather than
>   restarting them.
>
> Verified on device: the L9 parade reads as six distinct outlines, a 24-level sweep logs no
> missing render slots, and L18 (26 v 26) holds 60 fps through four volleys. Still NOT verified by
> the only arbiter that has ever settled this — the user's judgment in moving play.


**Status:** Attempt 5 applied 2026-07-26 — bespoke hero body (`unit_hero.glb`), the last
"not started" item on this doc's list. Verified on-device on L7 and L11; measured 1.34x the
crowd's height and ~2.6x its width. Read the "projection fact" section under Attempt 5 before
touching any unit geometry: X-Z is the screen plane and Y is depth, which retroactively
explains why Attempt 1's stance work and Attempt 5's own first-pass chest sash were invisible.
Still awaiting the user's judgment in actual moving play — that has been the only reliable
arbiter on this problem, and every attempt that skipped it was wrong.

Earlier history: three passes this session each looked correct by one signal (a Blender
render, a zoomed-in screenshot) and then didn't hold up under a real, un-zoomed, moving
on-device check. The user paused the work, asked for a written record and a fresh read, and
that review is the "Verification pass" section below. Read this whole doc before touching unit
rendering, `Formation.kt`, or `UnitDefinition` — it exists so the next round doesn't
re-discover the same dead ends.

**Camera and aim are a SEPARATE, already-closed effort — do not touch.** See
`CAMERA_ARCHITECTURE.md` and `GAME_DESIGN_LOCKS.md`. Both were extensively iterated and
verified on-device earlier in this session; the user has explicitly asked to lock them down.
This doc is scoped to unit visual variety and formation layout only.

## Goal (user's words, lightly edited)

Emulate Archery Bastion's presentation: a crowd of small, simple, visually-interchangeable
units massed in dense uniform rows on structure tiers, plus a small number of large,
individually-detailed "hero" characters standing apart, at the base/front, not gridded in
with the crowd. Every unit — hero and crowd alike — fires a volley each turn; see below,
this part is already true and must stay true.

Reference: a live screenshot of the actual Archery Bastion app was reviewed in-session
(not saved to the repo). Key read: the crowd (identical small archers) stacks in tight rows
on the tower's tiers; two hero characters (one per side) stand individually at the gate/base,
clearly larger, holding a weapon with real presence, not part of any grid.

## What's ALREADY true — do not re-implement

- **Every unit fires regardless of placement.** `GameViewModel.kt`'s enemy fire block builds
  `enemyAimVelocities` from `enemyUnits` (all of them) and the enemy fire loop only excludes
  `meleeDamage > 0` units — nothing in the fire path branches on ground vs. clustered vs.
  hero vs. structure-mounted. (Was `fireTeams()` when this was written; fire-team batching
  was removed 2026-07-25 — one round per unit now — which makes this MORE true, not less:
  every unit's round is individually simulated and drawn.)
- **Crowd-on-structure-tiers already exists.** `EnemyGroup.standingOnStructureId` +
  `Formation.mounted()` (`Formation.kt`) already stacks a garrison in compressed rows on a
  structure's deck — used across roughly 15 of the 25 levels today (tier1/tier2/tier3,
  bunker, barracks garrisons, etc.). The "crowd on tiers" half of the reference is a level-
  composition/selection question, not new engine work.

## What's been tried on unit visual differentiation, and why each attempt fell short

### Attempt 1 — pose/stance differentiation within the shared low-poly body
`build_units_v6.py`: gave Heavy/MachineGunner a wider stance + bent bracing arms, Sniper a
narrower staggered stance + forward lean, all still built from the same shared torso/leg/head
primitives as every other class. Looked clearly different in Blender renders and in zoomed-in
on-device screenshots. **User feedback: "if I zoom (pinch) in... it's just that from where we
show the action you wouldn't be able to tell."** Root cause: a bent elbow or a slightly wider
stance is a small fraction of a unit's total silhouette area at actual gameplay pixel size: a
few dozen pixels tall in an establishing shot.

### Attempt 2 — bigger bolted-on shapes + per-class weapon models
Two real, useful findings survived this attempt:
1. **Color/material choice matters more than shape at distance.** Heavy's first big
   back-mounted shape was on the `accent` material (near-black "dark gear" tone, shared with
   boots/vest) — camouflaged against the unit's own dark parts. Moving it to `trim` (which
   gets `unitTrimColor`'s per-class signature color, `SceneHost.kt`) made it read as a
   distinct light-colored blob even in a tiny (~50px) render.

   **Rule REVISED 2026-07-26** — the original wording here was "default to `trim`, not
   `accent`, unless there's a specific reason not to", and Attempts 4c and 5 both found
   specific reasons, so the rule is restated by shape AREA rather than as a blanket default:
   - The thing that actually matters is contrast against **whatever the shape directly abuts**.
     Attempt 2's failure was an `accent` shape touching `accent` boots/vest; it wasn't about
     `accent` being a bad material.
   - **Small, high-contrast shapes** (badges, sashes, visor slits, per-class props): use
     `trim`. They need maximum contrast to register at all, and their area is far too small
     to dominate the frame.
   - **Large shapes** (mantles, capes, coat panels, back armor): do NOT use `trim`. Attempt 4c
     found that once weapons stopped being near-white, Heavy's large trim slab became the
     brightest, largest thing on screen and read as a floating panel. Use `accent` when the
     shape should band and break up the figure (Attempt 5's shoulder mantle — on `primary` the
     hero was a smooth, featureless bell with less internal contrast than a grunt), or
     `primary` when it should simply add mass in the side's own colour (Attempt 5's cape).
2. **Shared weapon models undercut body differentiation.** `Rifleman`, `Sniper`, and
   `Marksman` all pointed `gunModelAsset` at the literal same `models/placeholder_gun.glb`.
   Guns are long, elongated, high-contrast shapes that dominate a unit's profile silhouette
   (the camera views units from the side — "faces +X, camera sees the -Y side" per the
   Blender builder scripts) — two classes holding an identical gun read as near-identical
   regardless of body differences. Gave Sniper its own `gun_sniper.glb` (long thin barrel +
   raised scope) this session; Marksman still shares the placeholder and would benefit from
   the same treatment if picked back up.

Despite both fixes, the user's read after seeing it in real play: **"still pretty rough."**
Not fully torn down to first cause before the session pivoted to Attempt 3 — worth deciding
whether Attempt 2's approach (bigger shapes on the same-size grunt body) is worth more tuning
at all, or whether Attempt 3's direction (some units are just bigger) supersedes it.

### Attempt 3 (current, UNVERIFIED) — hero-scale rendering + formation clustering + heroes stand apart
In progress when the session paused. Implemented and compiling, NOT confirmed to look right
on-device — every verification attempt was confounded (only one class in frame, or clusters
at different z-depths making size comparison unreliable in a static screenshot). See "Current
code state" below for exactly what exists.

## Key technical facts (read before writing more unit-rendering code)

- **Every unit is force-normalized to the same bounding cube.** `SceneHost.kt` calls
  `scaleToUnitCube(UNIT_SCALE_UNITS)` (0.77) on every unit body, unconditionally — this is
  WHY Attempt 1/2's bigger source geometry never made a unit bigger on screen, it just got
  scaled back down to match everyone else. This is the mechanism Attempt 3 changes (see
  `renderScale` below).
- **Precedent for opting out of that normalization already exists on structures**:
  `StructureDefinition.modelAbsoluteScale` (`StructureDefinition.kt`) lets a structure skip
  `scaleToUnitCube` entirely. Units need a *multiplier*, not an opt-out — grunts should still
  normalize to the standard size, only hero classes scale up from it.
- **Guns use a fixed world-unit offset from the body**, not one derived from the body's own
  bounding box: `GUN_OFFSET_X`/`GUN_OFFSET_Y`/`GUN_SCALE_UNITS` (`SceneHost.kt`). If the body
  scale changes and these don't, the weapon floats at the wrong height — this was handled in
  Attempt 3 (see below) but re-verify if anyone touches unit scale again.
- **Formation.grid is a perfectly uniform, unrotated grid**, zero jitter — a real contributor
  to the "spreadsheet" look, independent of body/color work.
- **Existing level data packs multiple `EnemyGroup` entries within a tight `anchorX` span** —
  e.g. L18 fits 5 separate groups within ~3 world units. This directly constrains how much
  visual "spread" any per-group formation change (clustering, hero spacing) can have before
  it visually bleeds into a neighboring group's territory. A first-pass hero spacing value
  (2.2× column spacing) did exactly this on L18 and had to be tightened to 1.3×. **Any
  further spacing work should be checked against L18 specifically, the densest case found.**
- **`CollisionSystem.UNIT_HIT_RADIUS`** (`CollisionSystem.kt`, 0.5) is a single global
  constant, deliberately left untouched — this whole effort is visual-only scope. Rebalancing
  hitboxes for a bigger unit is a separate decision nobody has asked for yet.
- **Knockback/ragdoll math is pure animation** (age → sine-arc offset/rotation), not tied to
  model size — confirmed safe, no changes needed regardless of unit scale.

## Current code state

- `UnitDefinition.renderScale: Float = 1f` (`UnitDefinition.kt`) — hero-scale multiplier,
  default preserves existing behavior. Currently only `HeavyRifleman`/`EnemyHeavyRifleman`
  set it, to `1.35f`.
- `SceneHost.kt`: unit body `scaleToUnits`/`scaleToUnitCube` calls and the gun's
  `GUN_OFFSET_X`/`GUN_OFFSET_Y`/`GUN_SCALE_UNITS` all multiply by `definition.renderScale`.
- `Formation.kt`: added `clustered()` (breaks a ground group into 2-3 loose sub-clusters with
  jitter instead of one rigid grid) and `heroes()` (spreads a hero-scale group into its own
  individually-spaced line, not gridded with anyone). Original `grid()`/`mounted()` untouched
  and still used directly by mortar squad slots and reinforcement march-in — those keep their
  exact prior behavior.
- `GameViewModel.kt`'s `buildUnits`/`formationFor`: picks `mounted()` (on a structure) >
  `heroes()` (`renderScale != 1f`) > `clustered()` (everyone else), and widens grid/cluster
  column spacing proportionally for hero-scale groups so they don't overlap neighbors.
- `build_units_v6.py` / `build_gun_sniper.py`: Attempt 1/2's pose changes and the new sniper
  rifle, still in place (not reverted — Heavy/MG/Sniper's bodies still carry them regardless
  of what happens with Attempt 3).
- Not started: a genuinely bespoke, non-shared-`core()` detailed Heavy hero model (this was
  gated on confirming the scale mechanism itself looks right first — never reached).

## Verification pass — 2026-07-24, L7 "Twin Towers"

Ran the doc's own suggested next step: clean build, fresh install, seeded stars, L7 opening
establishing shot (player line, camera at its real gameplay distance — no pinch-zoom).
L7 was chosen because its player groups put `Rifleman ×12` at `anchorX = -6.0` and
`HeavyRifleman ×4` at `anchorX = -4.5`, both on the ground at `anchorZ = 0` — same depth,
adjacent, so the comparison isn't confounded the way every prior attempt was.

**Open question 1 is RESOLVED: yes, `renderScale = 1.35` reads.** Measured off the frame,
Heavy silhouettes are ~1.29× rifleman height in screen pixels (315px vs 245px on a 2×
upscale of the un-zoomed frame), and the size step is obvious at a glance without zooming.
The scale mechanism works and should be kept — Attempt 3's core premise is sound.

**But scale alone does not deliver the reference read, for two reasons the frame makes
plain:**

1. **Weapons dominate every silhouette, and they're the wrong color.** `GUN_SCALE_UNITS`
   (0.48) is 62% of `UNIT_SCALE_UNITS` (0.77), and the gun is held at a diagonal so it spans
   even more of the profile than that ratio suggests. Worse: **guns get no runtime material
   override at all.** Unit bodies run the three-tone `body`/`accent`/`trim*` override loop
   (`SceneHost.kt` ~504-541); the gun's `ModelNode` block (~613+) sets position/rotation/scale
   and nothing else, so guns fall back to GLB-embedded materials — which per CLAUDE.md don't
   render on the target device — and come out near-white. The result across a 12-unit line is
   a picket fence of high-contrast pale diagonal bars that is by far the loudest thing in the
   frame. The crowd reads as a rack of rifles, not a crowd of soldiers. **This is the most
   likely single cause of "still pretty rough," it affects every unit on screen (not just the
   differentiated classes), and it is cheap to fix (gun material override + scale trim).**
   Note this also *inverts* Attempt 2's finding #2: shared gun models weren't the problem so
   much as guns being oversized and maximally bright.
2. **4 heavies among 12 riflemen is a chunkier subgroup, not a hero.** Confirms open question
   2 below visually. The player line is also a single flat rank on flat ground — the player
   side has no structure tiers at all, so the reference's "crowd massed on tiers, heroes at
   the base" composition can't happen there regardless of rendering work.

Also observed: Heavy's big back shape (Attempt 2, moved to `trim` = `armor plate steel`
`0.46/0.48/0.52`) is visible as intended, but it reads as a flat floating panel with a gap —
closer to a riot shield than to armor, and confusingly similar to `shield_bearer`'s trim tone.
Visible ≠ characterful; it's the loudest thing about the class and the least expressive.

Formation `clustered()` spread is not readable at gameplay distance either — the z component
of the spread barely separates units on screen, so a "cluster" mostly still reads as a line.

## Open questions for the next pass

1. ~~**Does `renderScale = 1.35` actually read as "bigger" in real, moving gameplay?**~~
   RESOLVED 2026-07-24 — yes, see the verification pass above. Keep the mechanism.
2. **Is folding a hero class into existing level `EnemyGroup` data even compatible with the
   reference vision?** Existing levels often place 3-8 `HeavyRifleman` per level, packed among
   4-5 other groups. The reference shows 1-2 individual heroes with room to breathe. Making
   Heavy bigger and spread-apart doesn't, by itself, turn "8 mid-sized guys" into "2 unmissable
   heroes" — that may need a roster/level-composition rethink (fewer heroes, more room),
   not just rendering changes. Worth deciding explicitly rather than discovering it level by
   level.
3. **Is Attempt 2 (bigger shapes on same-size grunts) still worth pursuing in parallel**, or
   does Attempt 3's "some units are just bigger" direction replace it? They're not mutually
   exclusive (a hero-scale unit can ALSO have bigger/bolder shapes) but nobody has decided if
   grunt-level shape work (MachineGunner's ammo drum, further weapon-sharing fixes for
   Marksman) is still worth finishing on its own.
4. **Formation.clustered's spread parameters are first-guess, not tuned.** Worth a real
   pass once the hero question above is settled, since hero spacing and crowd clustering
   interact on shared, tightly-packed level data.

## Attempt 4 (applied 2026-07-24) — weapons de-emphasized, hero counts cut

Two changes, in that order, each verified on-device before the next.

### 4a. Weapons stop dominating (`SceneHost.kt`)
- New shared `gunMaterial` (dark gunmetal `0.21/0.22/0.24`), applied to the gun `ModelNode`'s
  renderables in `apply` and re-applied from `onFrame` — same staleness guard as the body
  materials. Guns are a single joined mesh with one material, so a flat override is correct;
  no trim/accent split needed. One material for every class and both sides on purpose: weapon
  color carries neither class identity (that's `trim`) nor faction identity (that's the
  uniform), and a bright weapon out-shouts both.
- `GUN_SCALE_UNITS` 0.48 → 0.40. `GUN_OFFSET_X`/`GUN_OFFSET_Y` deliberately unchanged — they
  place the weapon at the HANDS, so they track body size, not weapon size. The old comment
  claiming all three "track UNIT_SCALE_UNITS proportionally" was the reason the gun kept
  growing alongside the body; that coupling is now explicitly broken and documented.

Result on L7's establishing shot: the picket-fence read is gone, soldiers read before their
rifles, and per-class props (rocket tips, ammo drums) become the brightest per-class cue
instead of competing with a wall of white bars.

### 4b. Hero counts cut, crowd grown (`LevelDefinition.kt`)
User decision: aim at the reference's actual framing, not "heavies are a bigger sub-rank."
Every `HeavyRifleman`/`EnemyHeavyRifleman` group is now **count 2** (boss capstones were
already 1). The removed units were folded into the same side's nearest plain rifleman group,
so **total unit counts are unchanged on every player side and on all but four enemy sides**
(L9's reinforcement wave, L23, L24 and L25 each lose 1-2, having no crowd group to absorb
them). L4 had a 6-strong heavy block and no plain infantry at all — split into a
4-rifleman crowd plus a hero pair at `anchorX = 5.8`.

**Known balance drift, deliberate and easy to tune:** a heavy is 64 HP and a rifleman 32, so
every swap removes 32 HP from that side while keeping its shot count identical. Both sides
are affected, so it's roughly symmetric, but every level is now somewhat lower-HP/faster.
Deploy cost moves the same direction (heavy 2 pts → rifleman 1 pt), so the default player
composition got *cheaper* on every level and can never violate `deployBudget` — no loadout
validation can break as a result of this pass.

`Formation.mounted()` now takes `columnSpacing` and `GameViewModel.formationFor` passes the
renderScale-multiplied value, so the hero pairs still garrisoning decks (L8/L10/L16/L17/L18/
L23/L24/L25 bunkers and tiers) get proportional room. The deck-`width` clamp still wins, so a
hero pair on a narrow ledge packs tighter rather than standing off the edge.

### 4c. Heavy's back slab reshaped (`build_units_v6.py`, `unit_heavy.glb` rebuilt)
Once 4a landed, the slab was the brightest and largest thing on screen. It was
`(0.30, 0.56, 0.66)` at `z=1.08` — spanning up to helmet height and nearly as wide as the
0.60 torso, which read as a flat floating panel. Now `(0.34, 0.44, 0.40)` at `z=1.00`:
stops at shoulder height, no longer halos the torso, and is pushed deeper in x, which is the
axis that actually shows in the -Y profile view where the "bulky" read has to come from.
`unitTrimColor("heavy_rifleman")` darkened `0.46/0.48/0.52` → `0.34/0.36/0.40` — the old value
was chosen while it had to compete with near-white weapons, and it also collided with
`shield_bearer`'s gunmetal.

Verified on L11 ("Fortress Duel", the one level with tiers on BOTH sides): garrison crowd on
the fortress tiers + rifleman crowd on the ground + a clearly larger hero pair standing apart
is, for the first time, the reference composition.

## Attempt 5 (applied 2026-07-26) — bespoke hero body, and the projection fact behind three dead ends

The doc's last "not started" item: a hero model that shares no geometry with the crowd's
`core*()` bodies. `tools/blender/build_unit_hero.py` → `unit_hero.glb`, wired to
`HeavyRifleman` (so `EnemyHeavyRifleman` and the `FirebaseCommander`/`FrostlineCommander`/
`CitadelSovereign` capstones inherit it through their existing `.copy()`).

### The fact that should have been written down four attempts ago

**Units face +X and the camera sees the -Y side, so the silhouette the player reads is the X-Z
plane. Y is depth INTO the screen.** Two consequences, both of which explain earlier failures
that were previously attributed to "too subtle":

- Anything separated only in **Y overlaps into one shape on screen**. Attempt 1's wider stance
  (`core_bulky`'s `stance_y`) spreads the boots along the depth axis — it changes the outline by
  almost nothing, no matter how far it's pushed. Same for `build_heavy`'s pauldrons at
  y = ±0.375, while that same function's ±X chest plate and back pack are the parts that do
  read — which its own comment had noticed empirically without naming the cause.
- A detail shape **centred in Y is inside the body and invisible**. This bit this very attempt:
  v1's chest sash was a 0.42-deep box centred in a 0.40–0.50-deep torso and did not appear on
  screen at all. Surface detail must sit ON the -Y face (y just outside the local half-width)
  or protrude past the outline in X/Z.

So: spend the shape budget in X and Z. Y widths exist only to keep the mesh solid.

### The four silhouette moves

1. **Greatcoat** flaring in X from a 0.30 waist to a 0.50 hem — below the belt the hero is a
   solid wedge where every crowd class is a ~0.17-wide leg column. Biggest outline difference
   available. (Note the on-screen "leg gap" people imagine closing doesn't exist: the crowd's
   two legs are separated in Y and already overlap into one column.)
2. **Shoulder mantle spread in X** (0.48 across), not as ±Y pauldrons — inverted-triangle upper
   body against the crowd's straight box.
3. **Cape trailing back in -X**, leaned 12°, on `primary` not `trim`: Attempt 4c's lesson is
   that a large trim shape becomes the brightest thing on screen and out-shouts the figure. On
   the uniform colour it instead makes the hero a bigger mass of its own side's colour.
4. **Peaked cap with a forward brim** to x=+0.27 — distinct head profile where every crowd
   class has a round helmet blob.

Height is exactly 1.55 like every other unit (`scaleToUnits` normalizes by the LARGEST
dimension, so the height must match or `renderScale` stops meaning what it says — verified:
z span 1.550 is the max, x 0.663, y 0.660). Arms sit at z=0.90, `core()`'s hand height, because
`GUN_OFFSET_X/Y` place the weapon at the HANDS and assume that relative height.

### Verified on-device 2026-07-26 (L7 establishing shot, then L11)

**Measured off the un-zoomed L7 frame:** heroes are 207px tall vs the crowd's 155px (1.34×,
matching `renderScale = 1.35`) and **62–80px wide vs 24–32px — about 2.6× the width**, so
roughly **3.5× the crowd's screen area**. That is what delivers the reference's "large
individual character" read; the user's remembered target of 2.5–3× turns out to be satisfied by
area without touching `renderScale` at all. Left at 1.35 deliberately: hero pairs still garrison
narrow decks on eight levels and `CollisionSystem.UNIT_HIT_RADIUS` is a single global 0.5, so a
scale jump is a balance/layout decision, not a rendering tweak.

**One real defect found and fixed between passes.** v1 put the mantle on `primary`, and the hero
came out a smooth, featureless green bell — distinct in outline but with *less* internal contrast
than a crowd grunt, which at least has a dark helmet and a dark rifle breaking it up. Moving the
mantle to `accent` and adding a coat placket on the -Y face gives three readable bands (dark cap
/ dark shoulders / green coat). Re-verified after the change.

### 5b. Faces (user request, 2026-07-26)

User's read of Attempt 5: "more like characters but would be better if they had faces." Because
the camera sees the -Y side, **a face here is a PROFILE, not a front view**, which reorders what's
worth modelling: the nose breaks the head's round outline and therefore reads at any size that
resolves the head at all, while anything painted flat on the head competes with a ~30px target.
Final: nose protruding 0.04 past the sphere, plus a horizontal eye SLIT on the -Y face (~7x4px on
a 207px hero). A first pass used a 0.03 eye cube and a jaw box — the eye disappeared into the cap
brim and the jaw merged with the collar (z 1.18–1.29) into one dark mass, leaving the head a green
disc between two dark bars.

Honest limit: zoomed in, it clearly reads as a face. At real un-zoomed framing you get a strong
"this figure is facing right, and has a face" read from the brim/nose profile, but individual
features are 2-3px and not separately resolvable. Going further means skin tone, and the runtime
override loop only supports primary/accent/trim (`startsWith("accent")`/`startsWith("trim")` in
SceneHost) — a fourth face material would mean extending that naming convention across the unit
material path, which is a bigger change than it looks.

**L11 ("Fortress Duel", tiers on both sides) is now unambiguously the reference composition:**
dense uniform crowd rows on the fortress tiers, two visibly larger hero figures standing apart
on the ground at the base. Enemy side confirmed too — the pale Frost Legion hero reads the same
way against its own crowd.

## Remaining next steps

1. **User judgment in real moving play** — the only signal that has ever been reliable here.
   Attempt 5's heroes have been checked on L7 and L11 by the model, not yet by the user.
2. ~~**`Formation.clustered()` is still not readable at gameplay distance.**~~ FIXED
   2026-07-27, and **the z-component was not the cause** — this doc's own diagnosis was wrong.
   The defect was a ratio in X: clusters sat 1.7 column-spacings apart while each was a full
   column-spacing wide, so the gap BETWEEN clumps (0.343) came out smaller than the spacing
   WITHIN one (0.490) — ratio 0.70 — and the ±0.147 cluster-anchor jitter could shrink it to
   0.05. Grouping by proximity needs the inverse, so three "clusters" of three were
   geometrically one uneven line of nine. Widening the offsets was not available as a fix:
   L18 packs neighbouring group anchors 0.7-1.2 apart and the old spread already reached 1.31
   either side of the anchor. The width therefore came out of the CLUMPS instead —
   `CLUSTER_PACK_FACTOR` packs each to 0.304 (shoulder distance, the `MOUNTED_COLUMN_SPACING`
   reasoning: a body is only ~0.21 wide, so 0.49 is an open-field value) and `CLUSTER_GAP_FACTOR`
   spends all of it on the gaps, expressed in units of the packed spacing so the ratio cannot
   silently invert again under a future spacing tweak. Ratio 0.70 -> 2.20 (1.41 at worst-case
   jitter) while the 9-unit half-span SHRINKS 1.311 -> 1.278. Anchor jitter now scales to the
   gap rather than the spacing, so it can move a clump visibly but never merge two.
   Verified on L18's establishing shot: measured ~39px intra-clump spacing against a ~105px
   step between clumps (2.7x), and the clumps read as clumps un-zoomed.

   **General lesson, worth applying beyond this function:** "not readable at distance" was
   assumed to mean "the effect is too small" for three attempts running. Here the effect was
   large enough and pointed the wrong way — a spacing relationship, not a magnitude. Measure
   the ratio before reaching for a bigger number.

2b. **NEW, found during that verification: L18's hero pair is swallowed by the crowd.** With the
   new footprint the rightmost rifleman clump centres at -4.63, inside the hero pair's
   -4.83..-3.97 span, and the frame shows a plain rifleman standing BETWEEN the two heroes
   with a third occluded behind one. This is level data, not a `Formation` bug: `Rifleman x9
   @ -5.6` and `HeavyRifleman x2 @ -4.4` are only 1.2 apart, tighter than the crowd group is
   wide at any sane clustering. Attempt 4b cut hero counts to 2 specifically so they would
   stand apart, and on L18 they do not. ~~Fix is to spread L18's player anchors; left for the
   user's call since it is content, not rendering.~~ **FIXED 2026-07-28.**

   Two things made it fixable without rebalancing the level. First, the room came from the
   EMPTY GROUND BEHIND the line, not from crowding the enemy: the crowd shifted back to -6.7
   (rear edge -8.08, just clear of the tank's -8.10) so the front only moved 0.35 closer.
   Measured spans, jitter and body width included, now leave 0.14 of daylight between crowd and
   heroes and 0.07 on the far side. The other crowd groups still overlap each other on purpose —
   a crowd SHOULD mass together; only the heroes need clearance.

   Second, and reusable: **the heroes stand 0.55 FORWARD in z.** That is the reference
   composition (heroes at the front, crowd behind) and it costs no lateral room, which a line
   like this has none of to spare. Safe because `CollisionSystem` tests x/y only — unit z is
   cosmetic for hit detection, so a forward hero is exactly as hittable. It also buys a free
   size cue: closer to the camera renders slightly larger, on top of `renderScale`.

   Any other level whose heroes read as swallowed can take the same treatment; L18 was the
   densest case found, so it is the worst one.
3. Heavy's back pack is better but still the lightest element in frame. If it's still too
   loud in play, darken `unitTrimColor` further before reshaping again — but note Attempt 2's
   lesson that `accent`-dark (0.13) made it vanish entirely.
4. Marksman still shares `models/placeholder_gun.glb` with Rifleman. Lower priority than it
   looked before 4a — with weapons dark and smaller, a shared gun costs much less legibility
   than it did — but it's still the last shared-weapon case.
5. Not started: a bespoke, non-shared-`core()` Heavy hero model. Now that hero counts are 2
   per side, a genuinely detailed hero model has far fewer instances to justify its cost.

## Attempt 6 (applied 2026-07-28) — the body is no longer one rigid mesh

The one-mesh-per-material convention is broken, deliberately and only at the waist. Units
split into an upper and a lower half (`build_units_v6.py` `finish()`, `build_unit_hero.py`),
and the upper half pivots at the waist so a dying body FOLDS instead of toppling as a plank.
Before this, death could only be a whole-body rotation plus a squash that `SceneHost` itself
described as a stand-in for articulation the shape could not do.

**Chosen over two bigger options, on purpose.** A full rigid limb puppet (~6 parented pieces)
would multiply draw calls 2-6x with 30-40 units on screen; skeletal animation adds an armature,
weights and a baked clip to every builder and is untried in this codebase. The waist buckle is
one joint, at most doubles renderables (measured: 4-6 per unit, up from 3), and was cheap
enough to judge before committing to either. Both bigger options remain open.

### What the pipeline looks like now

- The seam is real geometry, not a guess: `core()`'s legs top out at z=0.67 and its torso
  starts at 0.67. Parts are assigned by CENTRE against a threshold in the empty band above it,
  so no class builder had to change.
- **The hero has its OWN seam at 0.86** — its greatcoat is a 0.72-tall taper topping out at the
  belt. Splitting it at the crowd's 0.67 would have cut the coat in half. Any future bespoke
  body needs its own seam checked, not the shared constant.
- Naming carries two contracts at once: the runtime picks the fold group with
  `contains("upper")` and materials with `startsWith("trim")`/`startsWith("accent")`, so upper
  pieces are `accent_upper_X`/`trim_upper_X`. Lower pieces keep their exact original names.
- A model with no `upper_*` node simply doesn't fold. That is what makes partial rollout safe —
  and `unit_shield.glb` is deliberately still unconverted, because its shield panel spans
  z 0.125-1.075 and whether a HELD shield folds with the torso is a judgement call.

### Two defects, neither visible in Blender

1. **Never re-assert a child transform captured in `apply`.** A first pass captured each upper
   node's glTF translation (the pivot) and rewrote it every frame, reasoning that child
   transforms are as corruptible as the root and `enforceTransform` only checks the root. That
   was actively harmful: `apply` runs at first composition — the same moment this codebase's
   other comments warn the asset may not be populated — so the capture read (0,0,0) and pinned
   the upper body AT THE FEET. Corpses rendered as slabs radiating from the boots. The glTF
   translation is already the pivot; rotating about the node's own origin needs no help.
2. **A monotonic fold is wrong once the body is prone.** The buckle ramped to 46 degrees and
   HELD it. The topple ends near 90, so a permanent same-sign 46 put the torso at ~136 — folded
   past flat, driven into the ground. User's words: "contorted, and it appears to go below the
   ground." A collapse buckles WHILE FALLING and then lies roughly flat, so the fold is now a
   transient peaking ~49 degrees early in the fall (a `k^0.7` exponent biases the peak) decaying
   to a 9-degree resting bend. `DEATH_BUCKLE_REST_DEG = 0` is the guaranteed-safe setting if any
   contortion remains.

### Honest limit — the sixth time this doc has recorded the same lesson

At 4x zoom the fold is obvious. At real un-zoomed framing the dead are a small heap at the base
of the line, corpses pile and occlude each other, and the articulation is NOT what you notice.
The risk was predicted before building (at ~155px a joint is a few pixels) and the design aimed
at the outline rather than the joints — 46 degrees of fold still didn't change the silhouette
enough against overlapping bodies.

Kept because it is strictly more correct than a rigid plank and because it compounds with
anything that later makes corpses more visible or less clustered. It has NOT been shown to
change the read in play. If it doesn't earn its keep, commit `a2371d5` reverts the GLBs, the
builders and the runtime together.

**The transferable lesson, now demonstrated twice in two days:** before spending a pass on unit
detail, ask what fraction of the SILHOUETTE it changes at ~155px. Attempt 1 (stance), Attempt 5
(faces) and Attempt 6 (limb fold) all cleared the "is it correct" bar and failed the "does it
survive the frame" bar. Contrast the two changes that DID read: hero scale (Attempt 3, 1.34x
height and 2.6x width) and the clump gap ratio (Formation, a spacing RELATIONSHIP) — both
changed large-scale layout, not local detail.

## Fold rest angle zeroed, 2026-07-29

`DEATH_BUCKLE_REST_DEG` 9 -> 0. User, looking at a settled corpse: *"the dead player still appears
to be folded at the waist."* Attempt 6 already recorded 0 as the guaranteed-safe value if any
contortion remained, so this takes it. The transient buckle during the fall
(`DEATH_BUCKLE_DEG` 46) is unchanged — that is the part that reads as a body giving way; the
resting bend was the part that read as a broken pose. Changed but NOT yet caught on camera in a
settled state, so treat as unverified until someone watches a corpse come to rest.

## Attempt 7 (applied 2026-07-28) — the band profile, and a method that catches its own mistakes

Six attempts were judged by eye and five of them failed. This one was judged by a
**measurement taken before anything was built**, and that measurement caught two errors that
would otherwise have shipped. The measurement is the durable part of this entry; the specific
shapes are not.

### CORRECTION 2026-07-29 — the 155px figure is ~1.7x too large

Everything below is stated at "155px unit height at real gameplay framing." That number is wrong,
and it is not a constant. Screen scale is a pure function of camera distance:

    px_per_world_unit = 1080 / (2 * CAMERA_Z_HALF_FOV_TAN * camZ) = 1200 / camZ

155px for a 0.77-tall unit implies 201 px/world-unit → **camZ 6.0**, essentially `CAMERA_Z_MIN`
(5.5) — the bullet-cam / tight-follow floor, not the Aiming distance where a player actually
studies the field. L1's Aiming camera sits near 10.4, giving 115 px/world-unit and an **89px**
unit. Measured on device at L28 with riflemen in frame: **~80-89px**. Camera arithmetic and a real
frame agree.

**This does not reverse anything in this doc.** The band profile is a set of ratios and its
ranking is scale-invariant; and every "at 155px this detail dies" conclusion was measured against
a figure nearly twice as generous as reality, so those findings get *stronger*, not weaker. What
it does mean: any future pass that sets a px-denominated target must halve it, and the absolute
px numbers in the tables below should be read as ~1.7x inflated.

Found while building the structure equivalent of this measurement; see
`STRUCTURE_VARIETY_DESIGN.md` and `tools/measure_structures.py`, which takes `--camz=`.

### Measure this, not "does it look different"

Parse each unit GLB, normalise the way `scaleToUnits` does (largest dimension → 0.77), and
report the **width of the silhouette in three height bands — legs / torso / head — in
screen pixels at real gameplay framing** (155px unit height). Two numbers matter:

- **which band is widest** — the class's signature; and
- **the spread across the whole roster** — not any one class's number.

Height is not available as an axis at all: every unit normalises to exactly 155px, so a class
cannot be made taller. Width must be spent in **X**, because the camera sees the -Y face and
anything spread in Y overlaps into the same outline (the projection fact, Attempt 5).

The survey that started this pass:

    mortar 89px  mg 81  heavy 72  hero 66  engineer 57  shield 52
    marksman 48  sniper 46  rocket 43  grenadier 41  rifleman 31

Four classes inside 7px is one shape wearing four hats. Note *why* mortar and mg read: props
that protrude past the body outline. That is the whole mechanism.

### Error 1 — optimising each class in isolation destroys the spread

A first pass gave all four look-alikes a protruding shape and hit every individual target. The
result was grenadier 75 / sniper 75 / heavy 72 / marksman 70 / rocket 68 / hero 66 — **six**
classes inside 9px, worse than the cluster it replaced. **Eleven classes cannot all widen.**
Some must stay narrow for the wide ones to mean anything, which is why Marksman's body was
reverted to plain and its separation bought entirely through its weapon instead.

### Error 2 — total width is the wrong descriptor, and it hides the dangerous failure

Measuring in bands showed the sniper's low-draped ghillie had produced 74/67/46 — which is the
**hero's** profile (66/51/46, a greatcoat flaring to a wide hem). A crowd class had begun
imitating the one silhouette that has to stay unique, and single-number width called them 9px
apart and fine. Moving the ghillie up to the shoulders (61/66/73) fixed it.

**Generalise this: the roster has a small number of reserved silhouettes** — the hero's wide
hem above all — and any new shape must be checked against them by band, not by total width.

### Final profile (legs / torso / head)

    mortar     61/89/37     grenadier  75/75/29     engineer  24/57/52
    mg         26/81/70     rocket     23/52/68     shield    47/52/48
    heavy      31/72/72     hero       66/51/46     marksman  23/48/31
    sniper     61/66/73                             rifleman  23/31/28

### Weapons are measured the same way, on a different axis

Every gun normalises to the same ~80px length, so length cannot separate them either — what
does is **drop relative to length**. Marksman's new rifle (the last class still holding
`placeholder_gun.glb`, now used only by Rifleman) is built around a deep angled magazine for
exactly this reason. A first version was an anatomically sensible battle rifle with a longer
barrel and measured 35px of drop against the placeholder's 37 — no difference at all, because
under normalisation a longer barrel just shrinks everything else. It had to be exaggerated well
past real proportions to register:

    marksman 47px  placeholder 37  launcher 34  sniper 30  mg 22  rocket 21

### Still the honest limit

The band profile predicts legibility; it does not prove it. All of the above is verified by
measurement and by an un-zoomed on-device frame, NOT by the user's judgment in moving play —
which remains the only arbiter that has ever settled this question. What is genuinely new is
that the method caught its own two errors before reaching the user, which no previous attempt
did.

## Garrison presentation, 2026-08-02 — density and rank depth

Not another silhouette attempt. This is the OTHER half of "small and compact": how a garrison
reads as a mass, which turns out to be worth more than any per-class geometry change measured so
far, and it was settled by measuring the reference directly rather than by argument.

### The reference, measured

Archery Bastions is installed on the test device (`com.bastion.archers`); its own castle tiers are
visible on the main menu, so a `screencap` is all this took. What its tiers actually do:

- **TWO ranks**, packed until the bodies overlap, the back rank's helmets sitting just above the
  front rank's. It is emphatically not a single row.
- **Horizontal pitch ~0.49x the unit height left visible above the parapet.** This ratio is the
  useful form: it is camera-independent, so it compares across the two games directly, which raw
  pixels do not.
- The occluder is a low **solid, unbroken** band cutting the row at ~40-45% of body height. No
  rhythm in the parapet competing with the rhythm of the men.

### What ours was doing

`MOUNTED_COLUMN_SPACING` (0.30) and `MOUNTED_MIN_SPACING` (0.22) were left at their literals when
the body shrank 0.77 → 0.48, so the gap between defenders went from 0.09 — 43% of a 0.21-wide body
— to 0.17, more than a full body of daylight between much smaller men. Exactly the "loose picket"
the constant's own comment exists to prevent, and the same oversight as the constants
`DEFAULT_COLUMN_SPACING` and `UNIT_HIT_RADIUS` were saved from in that pass. Measured on the ratio
above: ours ran 0.85 against the reference's 0.49.

Both now derive from `UnitGeometry.LEGACY_SCALE_RATIO`, which lands the ratio at 0.53. Two
independent derivations agreeing — the body-width argument and the reference measurement — is why
that is the number and not a tuned one.

### Rank depth flipped back to two

2026-07-25 had moved `Formation.mounted` to ONE rank unless the deck could not seat everyone, on
the reading that the reference is "a single unbroken row shoulder-to-shoulder". That reading was
wrong; see above. The original two-rank version genuinely did read as a clump on a wide tier, but
**the cause was the spacing, not the second rank** — at 0.30 a 3x2 group was as wide as it was
deep, so it measured as a square blob. At the derived spacing a rank is half as wide and the same
group reads as a formation with depth. Two ranks now at `count >= 5`; below that a garrison is a
picket line and splitting three men 2+1 reads as an accident.

### What this does NOT fix

A small garrison on a wide deck is still a small clump on a wide deck — packing tightly is correct
and cannot fill a wall that was authored with eight men on it. **Filling a tier is a roster-size
question, not a spacing one.** The reference runs ~15 per rank. Do not "fix" it by spreading the
row, which is the 2026-07-25 mistake in the opposite direction.

## Tier 2.2, part one — the heroes were never composed, 2026-08-11

**The engine half of crowd-vs-hero was finished months ago and the LEVEL half was never done.**
Every one of the seven attempts above crossed into Unity intact — `Formation.Clustered` carries
the 2.2 gap ratio, `Mounted` carries the reference-derived density and two ranks at count >= 5,
and the port's own `renderScale` bug (it spread heroes apart but never made them bigger) was
fixed at `BattleRunner.cs`. None of that was the problem.

**All four hero groups in the campaign were authored ONTO a structure, in counts of four and
five.** L6 x4 on the keep, L7 x5 on the barracks, L11 x4 on the beach post, L12 x5 on the citadel.
Attempt 4b cut hero counts to 2 per side specifically so they would stand apart; the port went
back to 4-5 and gridded them into a deck row.

**`FormationFor` dispatches on the garrison branch FIRST**, so `Formation.Heroes` — the whole
"stands apart, individually" path this document spent three attempts arguing for — was reached by
exactly ONE thing in the entire game: L10's turn-4 reinforcement wave. It was not a bug in the
function. Nothing ever called it.

**Nothing else could see this.** `LevelComposition` passed all twelve levels while four of them
packed five 1.9x bodies onto a roof, because spans and reach and garrison-majority were all
satisfied — a hero is a legal garrison member. The rules measure geometry, not casting.

### What changed

Heroes moved to the GROUND, cut to 1-2, at z 0.4 (forward of the crowd line, and free:
`SweptCollision` is x/y only, so a forward hero is exactly as hittable). **The first version of
this placed them "in front of THEIR structure" and that rule was wrong — see part three.**
Surplus heavies were swapped 1:1 for enemy riflemen IN the garrison they left, which is why every
level's roster total is unchanged and enemy DAMAGE OUTPUT is identical — `EnemyHeavyRifleman` is
`EnemyRifleman` with 2x HP and 1.9x scale and the same damage 8. The only balance movement is
enemy HP: **L6 -64, L7 -96, L11 -96, L12 -96.** Four levels are slightly faster to clear.

L11 fields ONE hero, not two, and that is deliberate: at 10 units, two heroes on the ground pushed
the garrison to exactly half the roster and broke rule 5's majority. A lone champion on the beach
is a better composition than a rule bent for symmetry.

### The measurement that killed the third assertion

The intended check was three properties: heroes on the ground, heroes rare, heroes FORWARD IN Z.
The third is false and only measuring found it — **L12's deck garrison sits at z 0.80 against the
hero's 0.34**, because `deckStandZOffset` and a staging offset are different things that happen to
share an axis. Asserting it would have asserted a belief. It is not in the check.

### The check, and its negative run

`PortSelfTest.CheckHeroStaging` builds every campaign level and measures the units a player would
see: no hero at deck height, at most 2 per level, and every hero's nearest CROWD body at least
2.5x the crowd's own column spacing away. The hero COUNT is inside the condition rather than
beside it — with no heroes authored anywhere the whole function is vacuously true, which is the
empty-purse trap this repo has now paid for three times.

Against the old level data, per the standing rule that a check never seen to fail is not evidence:

```
[FAIL] heroes stand APART on the ground, never gridded into the crowd — 18 across the campaign,
       biggest group 5 (max 2), 18 on a deck, 13 inside the 0.76 clearance floor
       (tightest 0.00 on L12, crowd spacing 0.31)
```

**Tightest 0.00 is a hero standing at the same x as a crowd body** — interleaved in the citadel's
rifle row, which is the defect in one number.

### Device, with the control shot

Confirmed on L6 at real framing, and the CONTROL was taken — the same drag, same frame, on a build
with the old level data. Before: four oversized red figures crowding the keep's roofline as one
lump, taller than its own crenellations, nothing on the ground. After: two greatcoated heroes
standing alone at the base in front, small crowd on the roof. **This is the first change in this
document's history to be confirmed against its own control shot rather than against a bare
observation.**

Still NOT verified by the only arbiter that has ever settled anything here — Rob's judgment in
moving play. Every attempt in this document that skipped that was wrong.

### What Tier 2.2 still owes

The crowd half. This pass fixed which units are staged as heroes and where they stand; it did
nothing to the CROWD's own readability, which is what 2.2's entry in `PRODUCT_DIRECTION.md`
actually names. Before spending a pass on that, re-read "the honest limit" above twice: three
attempts (stance, faces, limb fold) each cleared "is it correct" and failed "does it survive the
frame", and the two changes that DID read were large-scale layout, not local detail.

## Tier 2.2, part two — a deck is ONE formation, 2026-08-11

**Two groups garrisoned on the same structure stood inside one another.** `FormationFor` called
`Formation.Mounted` once per authored GROUP, and every call centred its row on the same deck — so
L11's three riflemen and three machine gunners occupied an identical `5.81..6.19`, **dx 0.000
dz 0.000**, three men in three spots twice over. L6 and L12 were partial versions of the same
thing. L12's was the `tightest 0.00` that part one's hero check reported.

**Nothing could see it, and the reason generalises.** `LevelComposition` reads span and reach,
both of which a doubled-up garrison satisfies perfectly: a row of six that is really three men
twice over measures exactly like a row of three. Every unit is individually correct too — right
deck, right height, right rank. **A rule-checker is evidence about the rules it has**, and none of
the seven has an opinion about how many bodies are in a spot.

`LevelBuilder.DeckSpots` now lays out each deck ONCE across every group standing on it and serves
the groups in author order, so the first group fills the front rank and the next takes the rank
behind it — ordered ranks rather than a mixture, which is what the reference's tiers look like.
Spacing takes the LARGEST `renderScale` on the deck, because pitch is a property of the row and
not of one man in it.

**A reinforcement wave still builds its arrivals in isolation** and cannot see who is already up
there, so a wave that garrisons onto an occupied deck would reintroduce this. No campaign wave
garrisons today — L10's is ground — and the fallback path is kept and commented for that case.

### The detector was wrong before the code was right

The first version compared each group's x-RANGE and reported L11 as still broken after the fix had
landed. It had not: a back rank legitimately spans the same x as the front one, which is what a
second rank IS. The check is **Chebyshev** now — apart on EITHER axis is apart — and the standing
lesson is the mirror of the usual one: a detector that reports a failure has to be checked for
what it is actually finding, exactly as one that reports nothing has to be shown finding the thing
when it is there.

`PortSelfTest.CheckNobodyOverlaps` asserts it over all 1511 same-side pairs in the campaign, with
the pair COUNT inside the condition so an empty campaign cannot pass it vacuously. Negative run,
against the old builder and the current level data:

```
[FAIL] no two units on a side stand in the same place — 3 co-located pairs over 1511 same-side
       pairs, tightest 0.000 on L11 (floor 0.065, body 0.131)
```

### Deck FILL is measured, and it is the open question

Garrisons occupy **15-69% of their deck, most of them 16-34%** — L2, L8, L10, L11's GarrisonPost
and L12's wide tier are the worst, at 15-16%. This is the "small clump on a wide deck" this
document already names, and it is NOT a spacing bug: pitch was derived against the reference in
the 2026-08-02 pass and measures correctly. The body shrank 0.77 -> 0.48 while `standWidth` is
real structure geometry and did not, so the same deck now holds a smaller clump.

**Do not fix it by spreading the row** — that is the 2026-07-25 mistake, and this document already
records it. The reference runs ~15 per rank; ours run 3-8. Filling a tier is a ROSTER-SIZE
question and therefore a balance question, which is why it is written down here rather than
applied: it needs Rob's call, not a constant.

*(The percentages above were re-measured on 2026-08-12 with `DeckFillReport` and are a little
different from the 12-56% first written here by hand. The instrument is the number now.)*

### Shrinking the STRUCTURES cannot fix it — measured and closed, 2026-08-12

The obvious mirror fix — the body shrank and the buildings did not, so shrink the buildings —
was measured before it was applied, and **the arithmetic kills it.** `DeckFillReport` sweeps a
hypothetical scale factor over every garrisoned deck in the campaign.

**A single global factor cannot work, because the decks are already 4.6x inconsistent with their
own garrisons**: L6's MountainBunker is at 69% while L12's FortressTierWide is at 15%. Any one
factor overflows the tight decks before it fills the loose ones — at x0.62 the campaign spans
25% to 111%, and past 100% the garrison no longer fits and `Formation.Mounted` starts compressing
it.

**And per-structure factors are worse, because of how small they have to be.** What each deck
would have to shrink to for its CURRENT garrison to fill it to 75%:

| | deck now | needs | factor |
|---|---|---|---|
| L2/L8/L10/L11 GarrisonPost | 3.13 | 0.67 | **x0.22** |
| L12 FortressTierWide | 4.50 | 0.92 | **x0.21** |
| L4/L7/L9 BarracksBlock | 2.25 | 0.67 | x0.30 |
| L6 FortressTier | 3.00 | 0.92 | x0.31 |
| L3/L5 CommandBunker | 2.13 | 0.92 | x0.43 |

**The scale is uniform, so that is a height cut too.** Every enemy structure is drawn
`Vector3.one * worldScale` in `LevelScenery` — there is no per-axis squash — so GarrisonPost at
x0.22 is 0.55 units TALL, against a soldier's 0.48. The building would be the height and the width
of the men standing on it. That is not a dominant structure, and rule 3 is built on there being
one.

**The reason it fails is worth keeping, because it inverts the framing.** Our decks are already
sized like the reference's: a 3.13 deck seats **17 per rank** at the derived pitch, and the
reference runs ~15. **The geometry is right and the roster is a third of the size.** Shrinking
the building would make it un-reference-like in order to hide a roster gap, which is the
2026-07-25 mistake pointing the other way.

So the option list is back to two, and both are roster calls: more bodies at full strength (a real
difficulty change on twelve signed-off levels), or more bodies at split strength (constant damage
and HP, ~250 entities instead of 99, and a crowd stat variant per garrisoned class).

`DeckFillReport.Run` is the instrument and it only measures — it never edits an asset:

```bash
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod DeckFillReport.Run -logFile -
```


## Tier 2.2, part four — the crowd split, 2026-08-12

**Every garrisoned group became MORE, WEAKER bodies, and nothing else moved.** A rifleman's 32 hp /
8 damage becomes two crowd bodies at 16/4; a machine gunner's 40/4 becomes two at 20/2; a
grenadier's 24/6 becomes two at 12/3. 155 garrisoned bodies became 248. `CrowdSplit.Apply` is the
authoring step, idempotent and re-runnable.

**The invariant is CONSTANT OUTPUT and it was proved, not asserted.** Every level was built twice —
once on the original data and once on the split — and all three totals came back identical on all
twelve levels:

```
              BEFORE                          AFTER
L1   units= 9 hp=288 dmg= 72 struct=18   units=14 hp=288 dmg= 72 struct=18
L12  units=18 hp=680 dmg=164 struct=41   units=26 hp=680 dmg=164 struct=41
```

`BalanceAudit.Report` is **byte-identical across all 61 findings** — every race ratio, every siege
verdict, every reach rule. That is the control shot for "no level Rob has signed off gets harder".

### What the split actually bought

The wide decks — the ones this document has complained about since 2026-08-02 — roughly doubled:

| | before | after |
|---|---|---|
| L2/L8/L10/L11 GarrisonPost | 16% | **34%** |
| L12 FortressTierWide | 15% | **32%** |
| L4/L9 BarracksBlock | 22% | **47%** |
| L6 FortressTier | 23% | **42%** |
| L1 Outpost | 34% | **59%** |

**On the NARROW decks the split buys a second RANK instead of width, and the fill number cannot
see it.** A 3-man row and a 6-man double row have the same span, so L3's WatchTower still reads
34% while now standing two deep. That is the reference's tier shape, not a failure — but it is why
the percentages understate the change, and why the device is the arbiter.

### Three constraints picked the factors, and each cost a run to find

- **The factor must divide HP and damage EXACTLY.** A remainder silently retunes the level the
  split was supposed to leave alone.
- **No crowd body may fall to the incendiary burn's 8 damage.** The first table split the sniper
  x2 and the grenadier x3, landing both on exactly 8 hp — and `PortSelfTest`'s roster-frailty
  check went red immediately, because the burn stopped CHIPPING and started one-shotting. **The
  sniper is therefore not split at all** (16 only halves to 8) and loses nothing: both its
  garrisons sit on 1.50 decks where the split bought a rank, not width. The grenadier took x2.
- **The 7-30 roster scale is a LOCK.** Splitting both of L12's garrisons took it to 31.
  `CrowdSplit` now takes groups worst-clump-first and skips any split that would breach the lock,
  so L12 splits its citadel — the dominant structure and the campaign's worst fill — and leaves
  its gate alone.

### The one thing that is NOT neutral, measured

Aggregate HP is preserved exactly, but **kills happen per BODY, and the last round into each body
wastes its overkill.** A 40 hp machine gunner takes exactly 5 rifle rounds; two 20 hp ones take
3 + 3 = 6. Riflemen are unaffected (16 and 32 are both multiples of 8), which is why the bulk of
the campaign does not move:

```
rounds to clear the roster, at 8 damage a round
L1 36->36   L2 44->44   L3 38->38   L4 77->81   L5 47->50   L6 77->82
L7 49->52   L8 50->50   L9 67->70   L10 56->60  L11 44->47  L12 85->85
campaign 670 -> 695  (+3.7%)
```

So seven levels want **3-5 more rounds**, a third to a half of one player volley, and five are
untouched. It is one-directional and small, and there is no factor that removes it: 40 has no
divisor that is both burn-safe and a multiple of 8. **`BalanceAudit` models HP in aggregate and
cannot see this at all** — it is recorded here because a measured 3.7% is a fact and an unmeasured
one is a surprise.

### Two traps this did not fall into, both because the shapes were already here

- **The crowd variants share their parent's `modelAsset`**, which is what `BattleRunner
  .UnitClassKey` keys on — so they reuse the same prefab and the same data-sized slot pool, and
  **no scene rebuild is needed.** A new class key would have fallen through to the generic enemy
  prefab and quietly re-skinned every garrison.
- **They are separate definitions rather than a per-entity stat override.** `UnitEntity` reads its
  stats off `Definition` in eight places, and "grep for EVERY READER" is a trap this repo has paid
  for twice already (`flagMount.scale`, `standingYFor`).

**The projectile pool had to grow, and its overflow is SILENT.** The draw loop skips any round past
the end of the pool while it still flies and still damages. L12's enemy volley went from ~23
bullets to 51 against a pool of 64, so `ProjectilePoolSize` is **96** and `PortSelfTest` now
measures the real campaign peak against the real constant. Negative run at 48:
`worst is L12 Bullet at 51 rounds against a pool of 48`.

**Rob has not seen this on a device.** Deck fill is a presentation change and this document's own
"honest limit" applies to it exactly as it does to the seven silhouette attempts: measurement
predicts legibility, it does not prove it.

## Tier 2.2, part three — a structure's ANCHOR is not its EDGE, 2026-08-11

Rob, on the L6 build: *"heroes are behind the structure which makes them really tough to hit
without firing at a steep angle."* He was right, and the cause is geometric rather than perceptual.

**A structure blocks as a box `hitWidth` wide, and `hitWidth` is not the width of the building you
see.** L6's keep is drawn around x 6 and blocks from **x 3.88**. Part one placed the heroes at 4.3
— "in front of the keep", measured off its ANCHOR — and put them *inside* it. Measured against the
formula `CollisionSystem` actually uses:

| | hero span | box that swallowed them | depth |
|---|---|---|---|
| L6 | 3.90..4.71 | keep blocks 3.88..8.13, top 2.0 | inside |
| L11 | 4.18 | post blocks 4.13..7.88, top 2.5 | inside |
| L12 | 3.90..4.71 | citadel blocks 3.00..9.00, top 2.0 | **1.71 deep** |
| L7 | 2.87..3.56 | barracks blocks 3.55..6.05 | 0.01 inside |

To hit a unit standing in a box you must clear the box top and reach the ground within the same
fraction of a unit — L6 needed a 2.0 drop in 0.02 of travel. That is the near-vertical plunge Rob
describes, and it is why the hero pass made them HARDER to hit than when they stood on the roof:
a garrison on a deck is above every box and takes an ordinary arc.

**The placement rule is now: clear of every enemy structure's box, with nothing between the hero
and the player.** Only a box at LOWER x than the target can shadow it, since the shot travels
left to right. Heroes sit at L6 -1.0, L7 2.7, L11 3.2, L12 0.6 (with L12's ground riflemen moved
to -1.0 to leave them the room).

### Rule 7 cannot see this, and that is the general point

`LevelComposition` passed all twelve levels throughout. Rule 7 measures the distance and height to
a unit and asks whether the roster has the POWER to get there — there is nothing in its model
about what is IN THE WAY. **Reach and a clear line are different questions**, and the seven rules
only ask the first.

### The check indicted shipped content, and this time the content was wrong

`PortSelfTest.CheckNobodyStandsInAWall` asserts it over every static ground unit. Written to
defend the hero fix, it immediately found **four riflemen the campaign already shipped**: two on
L9 inside the mountain bunker (0.46 and 0.74 deep) and two on L10 inside the outpost (0.13 and
0.45). Every one a unit the player could not hit without the same plunge. They were moved out
with the heroes.

This document's standing warning is to be suspicious when a brand-new check indicts long-standing
content — the "ASCII only" glyph check flagged 23 strings that rendered perfectly. The warning is
not "assume the check is wrong"; it is **go and measure which of the two is wrong.** Here it was
the content, and the measurement said so unambiguously.

**ADVANCING units are exempt, semantically rather than as a tolerance.** L9's shield bearers start
0.01 inside the bunker on formation jitter and walk out on their first move, so starting in a box
costs them nothing. A static unit gets no such reprieve.

Negative run against the committed state:

```
[FAIL] no ground unit stands inside a structure's collision box — 10 of 43 ground units
       embedded, tightest clearance -1.71 on L12
       (EnemyHeavyRifleman at x 4.71 vs FortressTierWide edge 3.00)
```

### Device

L6, real drag, deliberately SHALLOW — ~241px per axis against L1's 331, the flat arc that could
not have reached the old position at all. It landed among the heroes and killed three (16 -> 13),
and the enemy-turn camera then framed the pair standing alone in open ground ahead of the bunker,
plainly larger than the crowd on its roof. **609 checks green, 12 levels still pass all seven
composition rules.**
