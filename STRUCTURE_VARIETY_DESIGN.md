# STRUCTURE_VARIETY_DESIGN.md — Building Legibility Redesign

Started 2026-07-29. User's report: *"more detailed buildings, towers, etc. right now they are
difficult to decipher what they are."*

This doc is the structure counterpart to `UNIT_VARIETY_DESIGN.md`, and it deliberately starts
where that one ended up after seven attempts: **with a measurement taken before any geometry is
authored.** Six unit attempts were judged by eye and five of them failed on device. The seventh
was judged by a number and caught two of its own errors before shipping. That method is the only
part of the unit work that transferred, so it is the first thing built here.

## The measurement

`tools/measure_structures.py` — pure Python, no Blender, re-runnable from a normal shell:

    python3 tools/measure_structures.py            # whole set
    python3 tools/measure_structures.py --ascii bunker

It parses each GLB, rasterises the **real triangles** of the X-Y silhouette (glTF Y-up; GLB Z is
depth into the screen and is discarded — the projection fact from Attempt 5), and scales to
gameplay pixels.

### The scale constant was wrong, and it was not a constant

`UNIT_VARIETY_DESIGN.md` states a unit is **155px at real gameplay framing**. That is the figure
every unit-legibility conclusion is calibrated against, and it is roughly **1.7x too large.**

Screen scale is a pure function of camera distance:

    px_per_world_unit = 1080 / (2 * CAMERA_Z_HALF_FOV_TAN * camZ) = 1200 / camZ

155px for a 0.77-tall unit implies 201 px/world-unit, which solves to **camZ = 6.0** — essentially
`CAMERA_Z_MIN` (5.5), the bullet-cam / tight-follow floor. It is not the Aiming distance, which is
where a player actually studies the field: L1 sits near camZ 10.4, giving 115 px/world-unit and a
**89px** unit.

Verified on device 2026-07-29 by putting riflemen in L28's frame and measuring them: **~80-89px**,
against the 155px the doc predicts. Two independent routes, camera arithmetic and a real frame,
agree.

What this does and does not change:

- **Relative metrics are scale-invariant** — `fill`, the band *ratios*, and `proud` are unaffected,
  and the ranking of the set is identical at either scale.
- **Absolute px figures and any px-denominated target shrink by 1.74x.** The targets below are
  stated at the corrected scale.
- For the unit work it makes the existing conclusions *stronger*, not weaker: every "at 155px this
  detail dies" finding was measured against a figure nearly twice as generous as reality. Nothing
  in that doc needs reversing — but a seventh attempt calibrated on 155px would have aimed at the
  wrong target.

The tool defaults to camZ 10.4 and takes `--camz=` to check any other framing.

| metric | meaning |
|---|---|
| `fill` | occupied fraction of the bounding rect. **A joined stack of axis-aligned boxes lands near 0.9.** This is the numeric statement of "it's just a block." |
| `base/mid/top` | occupied width in each horizontal third, px. Which band is widest is the structure's signature — the direct analogue of a unit's legs/torso/head profile. |
| `roofline` | std-dev of the silhouette's top edge across columns, px. A flat lid is ~0. Roofline is the strongest real-world building-identity cue, so this measures the diagnosis directly. |
| `levels` | distinct top-edge heights, quantised to 4px. A box has 1. |
| `proud` | fraction of area outside the base footprint's column span — the "props protruding past the outline" mechanism, which is what made the mortar and MG the two most readable units. |

## Baseline, 2026-07-29 (before any change, at the corrected camZ 10.4 scale)

    structure                  w x h   fill  base  mid  top   roof  lvl  proud
    fortress_tier_wide     286 x  98   0.96   286  274  280   12.2    2   0.00
    fortress_tier          205 x  98   0.94   205  193  199   14.3    2   0.00
    barracks               121 x  83   0.94   120  120  121    3.8    2   0.00
    garrison_post          182 x 123   0.91   182  165  174   21.9    2   0.00
    fortress_tier_small    125 x  98   0.91   125  113  119   18.2    2   0.00
    tower_base              74 x  92   0.90    73   73   66   11.7    2   0.00
    mountain_bunker         92 x  80   0.82    92   92   82   14.2    4   0.00
    bunker                 135 x  69   0.79   119  133  116   10.2    8   0.02
    cliff_outcrop          127 x 140   0.72   127  118  120   26.7    4   0.00
    tower_platform          76 x  74   0.70    61   61   76    2.6    2   0.07
    outpost                150 x  92   0.66   150  142  123   13.8    5   0.00
    ridge_watchtower       138 x 208   0.64   138  104   80   71.6    4   0.00
    watch_tower             83 x 182   0.51    54   75   83    0.0    1   0.19
    placeholder_tank       173 x 100   0.47   164  163   19   10.8    6   0.01
    comms_tower             44 x 258   0.30    43   37   43   15.1    4   0.01

Four findings, all of which the eyeball had already reached but could not state:

1. **Six structures at fill >= 0.90.** On screen they are solid rectangles.
2. **The band profile is flat.** Barracks is 120/120/121 — identical to within 1px. Fortress wide
   286/274/280, garrison post 182/165/174. Attempt 7's cited failure was four *different unit
   classes* within 7px of each other; this is *within a single structure*, to within 2%.
3. **`proud` is 0.00 for eleven of fifteen.** The one mechanism proven to make a silhouette
   readable is essentially unused across the whole set.
4. **`watch_tower` roofline = 0.0, levels = 1** — a dead-flat lid. This is the standable-deck
   constraint (below) showing up exactly where predicted.

### Confirmed on device, L28, 2026-07-29

The parade frame settles it. Left to right the level shows mountain bunker, command bunker, tower
base + platform, barracks, outpost, garrison post — and:

- **The barracks and the outpost are the same building in two colours.** Box, flat dark roof slab,
  a door and two windows painted on the front face. Their `fill` differs (0.94 vs 0.66) but the
  *read* does not, because the outpost's extra shapes sit where they do not change the outline.
  This one pairing is the user's complaint in miniature.
- **Every structure is a box with a lid and a flagpole.** At this size the pole is doing more
  identification work than the geometry is.
- **The flags actively hurt.** Each structure flies an identical banner in the stage's faction
  colour, so the most eye-catching element on every building is the element they all share. Worth
  revisiting once silhouettes carry their own weight — flags are a faction cue competing with the
  type cue, and right now they are winning.

The metric earns trust by ranking the set the same way a person does at both ends: the structures
anyone would call readable — comms tower 0.30, watch tower 0.51, ridge watchtower 0.64 — are
precisely the low-fill, high-roofline ones. That agreement is the reason to believe it about the
ambiguous middle.

## Why this is NOT the unit problem

The unit doc's most-repeated lesson is that local detail dies at 155px. **That lesson does not
transfer**, because structures have far more pixels to spend:

| | on-screen (w x h) | vs a unit's area |
|---|---|---|
| Command Bunker | 135 x 69 | ~1.5x |
| Barracks | 121 x 83 | ~2x |
| Outpost | 150 x 92 | ~3.5x |
| Tower base + platform | ~76 x 150 | ~3.5x |
| Fortress tier (wide) | 286 x 98 | ~8x |

Detail at these sizes can survive. What *does* transfer is the **ordering**: silhouette first,
then tone, then surface. And the reserved-silhouette rule — the roster has a small number of
shapes that must stay unique, and every new shape is checked against them by band, not by total
width.

## The constraint that shapes everything

Standable structures need a **flat deck at exactly Z = size**. That is why every building tops
out in a lid, and roofline is the strongest identity cue there is.

But `standWidth` is consistently narrower than `hitWidth` — barracks 0.9 of 1.0, bunker 0.85 of
1.0, garrison post 1.25 of 1.5. **The margin outside `standWidth` is free.** Pitched end bays,
stair heads, vent stacks, chimneys and masts can live there without breaking the standing
contract. That margin is the entire budget for roofline, and it is the main unlock.

Decision 2026-07-29: **roof clutter only, inside the current footprint.** `size` / `hitWidth` /
`standWidth` / `deckStandZOffset` stay untouched, so no collision, formation or level-data
retuning is needed — these are not re-derived from the model at runtime. (This constraint is
softer than it looks, since the campaign is slated for redesign and placements will be
re-authored anyway; revisit only if a specific structure proves impossible within it.)

## Plan

**Phase 0 — measurement.** DONE. `tools/measure_structures.py`, baseline above.

**Phase 1 — third material tone.** Add a `trim*` prefix to the structure colour path in
SceneHost, matching the convention units already use (`trim` → `accent` → primary). Structures
get two tones today where units get four, and `structureColors()` clusters everything in
desaturated tan/concrete/olive at similar luminance. Roof / wall / detail cannot separate on two
tones. Compatible with the damage-chunk system as `trim_chunk_N`, since chunks match on
`contains("chunk")` plus trailing digits.

**Phase 2 — form-language taxonomy.** Today one vocabulary — `box body + slab deck + square_rim
parapet` — literally builds the barracks, bunker, tower base, tower platform and garrison post.
They differ by proportion, not by type. Each structure gets an archetype no other may imitate,
checked by band profile:

| structure | reserved silhouette |
|---|---|
| Command Bunker | low sloped earth-hugging wedge, zero verticals |
| Barracks | long horizontal, pitched end bays + roof clutter |
| Outpost | hut + lean-to + crate stack, deliberately irregular |
| Garrison Post | concrete pier with an external stair run |
| Watch Tower | splayed timber legs, overhanging cabin |
| Tower Base + Platform | concrete shaft, **cantilevered** cab (today this is the watch tower's idea a second time — the pair most worth separating) |
| Comms Tower | lattice mast + dish array (already the strongest; use as reference) |
| Fortress tiers | masonry: crenellations, buttresses, batter |
| Cliff / Mountain / Ridge | natural rock, no right angles |

**Phase 3 — rebuild.** Ordered by **archetype coverage**, not by current level usage: the
campaign is being redesigned, so ordering by "appears in 18 levels" optimises for levels that
will not exist. Build so the form language is complete and the reserved silhouettes are all
distinct, which is what a redesign wants to draw from. Measure against targets before export.

**Phase 4 — verify at real framing.** L28 (below), then the standing screenrecord +
contact-sheet pass, un-zoomed.

## Targets

Per-structure numbers are not targets; the **spread across the set** is, exactly as it was for
units. Working goals:

- No structure above **fill 0.80** (six are above 0.90 today).
- Every structure's **widest band identified and distinct** from its neighbours in the parade.
- **roofline > 10px** on every standable structure, at the camZ 10.4 scale (barracks 3.8,
  tower_platform 2.6, watch_tower 0.0 today).
- **proud > 0.10** on at least half the set (two of fifteen today).
- **No two structures sharing a read** — the barracks/outpost pairing above is the acceptance
  test, not an abstract target.

The obvious failure mode is Attempt 7's Error 1: hitting every individual target destroys the
spread. Eleven classes could not all widen; fifteen structures cannot all become spiky. Some must
stay plain — a bunker *should* be a low solid slab — for the silhouettes that do break out to
mean anything.

## Test levels

**L28 `TEST — Structure Parade`** (blocks) and **L29 `TEST — Structure Parade II`** (towers,
natural cover, assembled fortress). Both `isTestLevel = true`, in no stage, excluded from star
totals.

Staging: two rows, short in front (z +1.8), tall behind (z -1.8), back row offset a **half step
in X** so each structure sits in a front-row gap.

Two things were learned building these, both on device:

- **Height sorting alone is not enough.** The first version relied on it and the rows still
  collided — these are opaque volumes up to 2.4 tall, not the thin figures the unit parade
  stages. Sorted by height AND offset in X; both are needed.
- **All fourteen in one frame does not work.** It needs a player line spanning ~±5.4, which puts
  the camera near 15 and packs the rows tightly enough that structures occlude each other. An
  occluded parade is not a conservative test, it is a broken one. Seven per level keeps real gaps
  between subjects.

Framing: during Aiming the camera is `(playerHalfWidth + FramePad) / 0.45` (`FramePad` 0.6)
and reads the **player line
only** — enemy structures do not affect it at all. The player line is spread to match the
structure rows. Erring wide is the safe direction (legibility fails as things get smaller), but
not so wide that subjects overlap. The spectator riflemen sit at z +2.8 so they stay in frame as
a live scale reference — which is what caught the 155px error above.

Nothing shoots: one immortal enemy group sits far right, out of frame (Victory fires on
`enemyUnits.isEmpty()` regardless of structures), and the buildings stay intact as long as the
player never releases a drag.

## Pilot — barracks, 2026-07-29

One structure taken end to end (measure → rebuild → export → device) before committing a pass to
all fifteen, because "spent a pass on geometry that didn't move the read" is the failure mode
five of seven unit attempts hit.

    barracks   before   120 x  83   fill 0.94   120/120/121   roof  3.8   lvl 2
    barracks   after    120 x 138   fill 0.69   120/116/ 78   roof 25.2   lvl 4
    outpost    (unchanged)  150 x 92  fill 0.66  150/142/123  roof 13.8   lvl 5

Every target met, and confirmed on device: **the barracks and the outpost are no longer the same
building.** The profiles now diverge rather than differing by colour — the barracks tapers hard
(120→78, ratio 0.65) while the outpost stays wide (150→123, ratio 0.82).

Three things learned that change the plan:

1. **The roofline did essentially all of the work.** The stove pipes are the identifier you read
   first at real size; the plinth/recessed-wall step is visible but secondary. This is the same
   mechanism as the mortar and MG among units — geometry that breaks the outline — and it is the
   lever to spend first on every remaining structure.

2. **Band boundaries are a trap worth knowing about.** The first rebuild hit fill, roofline and
   levels but still measured 120/116/116 — as flat as the block it replaced. The bands are thirds
   of the silhouette HEIGHT, so a short stack put the top third's floor at 0.687, right where the
   full-width parapet sits, and the parapet filled the band. Carrying the pipes to 1.20 moved the
   boundary to 0.80 and the top band became the pipes alone. **Band width counts occupied
   COLUMNS, not solid area** — a wide-but-airy roof measures exactly as wide as a solid one.

3. **`proud` is the wrong metric for wide low buildings, and cannot be fixed by trying harder.**
   It measures area outside the base footprint's column span, so for any building whose base is
   its widest part it is structurally 0 — a chimney rising from within the footprint never scores.
   It remains the right metric for towers, where an overhanging cab or cabin does break the base
   span (watch_tower 0.19). **Do not chase `proud` on the blocks**; the "proud > 0.10 on half the
   set" target applies to the vertical archetypes only. Roofline and levels are the block metrics.

### Phase 1 earned less than expected

The third tone shipped with the pilot (`trim*` in SceneHost, olive monitor against tan walls) and
is the weakest part of the result. The monitor is small and the camera's pitch puts it partly
behind the front parapet, so at real size the tone reads as one more dark band. The plumbing is
correct and worth keeping — it costs nothing and rolls out per structure via `structureTrimColor`
returning null — but **Phase 1 should not be run globally on its own merits.** Add a third tone
where a specific structure's geometry needs one, not as a set-wide pass.

Also noted from the frame: the flagpole now competes with the stove pipes and reads as a fourth
pipe. Reinforces the flag observation above.

## Phase 3 results — 2026-07-29

    structure              before                          after
    fortress_tier_wide     0.96  286/274/280  roof 12.2 →  0.89  288/272/280  roof 18.2
    fortress_tier          0.94  205/193/199  roof 14.3 →  0.86  208/192/200  roof 20.5
    fortress_tier_small    0.91  125/113/119  roof 18.2 →  0.83  127/111/119  roof 25.0
    garrison_post          0.91  182/165/174  roof 21.9 →  0.67  182/174/ 74  roof 32.9
    tower_base             0.90   73/ 73/ 66  roof 11.7 →  0.79   78/ 80/ 70  roof 14.2
    barracks               0.94  120/120/121  roof  3.8 →  0.69  120/116/ 78  roof 25.2
    tower_platform         0.70   61/ 61/ 76  roof  5.4 →  0.41   49/ 90/ 15  roof 15.0  proud 0.24
    outpost                0.66  150/142/123  roof 13.8 →  0.45  150/123/ 21  roof 27.3
    watch_tower            0.51   54/ 75/ 83  roof  0.0 →  0.43   54/ 75/ 83  roof 15.2

Every band profile now tapers or steps; none is flat. Six structures sat above fill 0.90 before,
none does now. Verified on device at both parades: the six blocks on L28 are individually
identifiable, and the fortress on L29 reads as a fortress rather than stacked crates.

Two deliberate non-changes, both Error-1 discipline — not everything can break out, or the ones
that do stop meaning anything:

- **`command_bunker` stays a low slab** (fill 0.79, roofline 10.2). Its archetype IS "low sloped
  wedge, zero verticals." Giving it a mast would have made it a small barracks.
- **`tower_base` does not chase fill.** A squat structural pier is solid; opening it up would put
  it back in competition with the watch tower. Its job is the taper, and the pair's silhouette
  lives in the platform's cantilever above it (`proud` 0.07 → 0.24).

### The remaining gap — the three natural structures

`cliff_outcrop`, `ridge_watchtower` and `mountain_bunker` were NOT rebuilt, because **they have no
builder scripts**. Every other structure in the set is generated from a `tools/blender/build_*.py`
that can be edited and re-run; these three are two-node meshes with no reproducible source, so
changing them means authoring them from scratch rather than editing a recipe.

They are also the set's remaining look-alikes, and the same failure the barracks/outpost pair had:
on L29 the ridge watchtower and the cliff outcrop are two tan angular masses that blur together,
and on L28 the mountain bunker reads as a second command bunker. Their measurements are middling
rather than bad (0.72 / 0.64 / 0.82), which is exactly the trap — single numbers looked fine for
the barracks too, and it took the band profile and a device frame to see the problem.

Recommended next step: write `tools/blender/build_natural_structures.py` for all three, against
the "natural rock, no right angles" archetype, so they stop being tan boxes and stop needing a
one-off authoring pass every time.

## Scale pass — STRUCTURE_SCALE, 2026-07-29

User: *"structures should be much larger — we're going for an Archery-Bastion-like experience."*

Implemented as ONE constant, `STRUCTURE_SCALE = 2.5f` in `StructureDefinition.kt`, applied by
`StructureDefinition.scaled()` to every length-valued field: `size`, `hitWidth`, `standWidth`,
`deckStandZOffset`, `flagMount`, cannon muzzle offsets, `modelScaleUnits`, and a new `worldScale`
that SceneHost applies as node scale for `modelAbsoluteScale` models (whose GLB carries real world
size and therefore ignores `modelScaleUnits`). Placement Y is multiplied by the definition's own
`worldScale` at entity construction, because a stack's authored y (0.8, 1.6) is a multiple of tier
height and would otherwise leave tiers floating apart.

No GLB was rebuilt. Tuning the look is one number.

**Not scaled, deliberately:** the player tank (a vehicle already correctly sized against its own
crew) and `splashRadius` / `damage` (gameplay quantities calibrated against UNIT size).

### The thing to understand before tuning it

Scale is relative and the camera is a function of the field's WIDTH. Growing every structure by k
and then spreading the level out by k to fit pulls the camera back by k and renders everything
exactly the same size as before — **net zero**. What the constant buys is the structure-to-unit
RATIO, and it only reads as "bigger" if layouts stay roughly as wide. Which means fewer, larger
structures per level — which is also the Archery Bastions look: one commanding keep, not six huts.

### Verified on device

The garrison post went from 1.3x a rifleman's height to about 5x; the assembled fortress reads as
a crenellated keep towering ~8x over the soldiers, with the garrison nestled behind the
battlements. That last detail is the strongest vindication of the crenellation work in Phase 3 —
at 1x the merlons were texture, at 2.5x they are cover the garrison stands behind.

### Three consequences, all real

1. **Existing campaign levels are broken by this and need re-authoring.** Their x layouts were
   tuned for 1x structures. On L11 "Fortress Duel" the player's own fortress now fills the entire
   frame and the enemy is not visible at all. This is expected given the planned redesign, but it
   is the blocker for playing any existing level today. Nothing crashes; the geometry, stacking,
   garrison placement and collision are all internally consistent — the compositions are simply
   wrong now.
2. **The parades had to shrink to three structures each.** Two rows stopped working: the front row
   grew tall enough to bury the back one, and at z +/-2 the near row's perspective magnification
   pushed it past both screen edges. Six subjects do not fit at all — the camera clamps at
   `CAMERA_GAMEPLAY_Z` 22, and a row wide enough to hold them renders everything at half of L1's
   size. L28 is now bunker / barracks / garrison post; L29 is comms / watch tower / fortress.
   **Outpost, mountain bunker, cliff outcrop, ridge watchtower and the tower pair currently have
   no parade home** and need an L30 to stay inspectable.
3. **Structures are now much easier to hit.** Collision is `hitWidth x size`, so target area grew
   ~6x while `maxHp` did not move at all. Structure HP almost certainly needs a rebalance pass,
   and it is a gameplay change rather than a visual one.

## Natural structures rebuilt + HP rebalance, 2026-07-29

### maxHp now scales with the structure set

`STRUCTURE_HP_SCALE = STRUCTURE_SCALE` (2.5), applied in `scaled()`. **The factor is WIDTH, not
area.** Collision is a `hitWidth x size` box, so at 2.5x a structure presents 6.25x the area — but
hits do not scale with area. A volley's rounds spread across a beaten zone whose width tracks the
enemy formation span, and they arrive plunging, so what decides whether a round connects is
essentially the width presented to that spread: a 1.3-wide outpost catches ~22% of a 6-wide zone,
a 3.25-wide one ~54%. That ratio is 2.5.

Left un-scaled, everything was ~2.5x easier to kill than intended — a grenadier does 24 per hit
against an outpost's 36 HP, so it went from a two-hit objective to a single-volley formality.
**Estimated, not playtested**: if structures still fall too fast, raise the one constant rather
than editing fifteen values.

### The three natural structures

`tools/blender/build_natural_structures.py` — they were the only structures with no builder
script, and the set's last look-alikes. Reserved silhouettes, separated by aspect ratio as much as
by band profile:

    cliff_outcrop     132x144  (0.92, taller)  128/128/ 75  fill 0.75  all rock, no built element
    ridge_watchtower  147x261  (0.56, tall)    147/116/ 77  fill 0.51  rock footing carrying a
                                                                       BUILT timber shaft
    mountain_bunker   116x112  (1.04, squat)   113/111/ 94  fill 0.75  concrete set into a
                                                                       capped hillside
    command_bunker    135x 69  (1.96, flat)    119/133/116  fill 0.79  free-standing slab

The mountain bunker's hillside is deliberately CAPPED. A first pass took it to 1.05 and it
measured 116x133 against the cliff's 132x144 — two rock masses of near-identical size and profile,
the exact convergence the rebuild exists to prevent.

### Two defects the measurement could not see

Both were caught only by the device frame, and both are worth remembering:

1. **Parts must OVERLAP their neighbour, not merely touch.** The first pass left 0.10-0.17 gaps
   between accent pieces and the rock beneath them. At 2.5x that is 20-35 screen pixels, and
   against a contrasting tone it read as slabs floating in mid-air. Bounding box, fill and band
   profile are all *identical* whether parts touch or float — the metric is blind to it.
2. **These three had no `structureColors()` entry** and fell through to the generic else-branch,
   whose accent is near-black. Every accent part therefore read as a dark box bolted onto a rock
   rather than as part of it. Rock wants a SHADED ROCK accent; only genuinely built elements (the
   ridge's timber shaft, the bunker's concrete) should take a different material tone. Added
   per-type palettes for all three.

### Parade slots closed

L11 `TEST — Natural Parade` (cliff / ridge / mountain) and L12 `TEST — Outpost & Tower`. Every
structure in the set now has a parade home again.

## Status

- [x] Phase 0 — measurement tool + baseline
- [x] L28 / L29 structure parade test levels, verified on device
- [x] Scale constant corrected (155px → 89px) and propagated to UNIT_VARIETY_DESIGN.md
- [x] Phase 1 — `trim*` third-tone plumbing (kept, but demoted: see the pilot; apply per
      structure, not as a set-wide pass)
- [x] Pilot — barracks rebuilt and verified on device; method works
- [x] Phase 2 — taxonomy applied, roofline-first
- [x] Phase 3 — rebuilds: barracks, outpost, garrison_post, tower_base, tower_platform,
      fortress tiers (x3), watch_tower. command_bunker deliberately unchanged.
- [x] Phase 4 — on-device verification at L28 + L29
- [x] Scale pass — `STRUCTURE_SCALE = 2.5f`, verified on device
- [x] **Re-author campaign level layouts for 2.5x structures** — DONE 2026-07-29. The old 25 were
      scrapped and the campaign rebuilt from first principles at 12 levels; do not read this as
      open. NB the level numbers throughout this doc predate that rebuild — the structure parades
      referred to as L28/L29 are L15/L16 today, and `LevelDefinitions.all` now holds 29 entries.
- [x] Rebalance structure `maxHp` — scales with width (2.5x). ESTIMATED, needs a play pass.
- [x] L11 + L12 parades — every structure has a home again
- [x] The three natural structures rebuilt from a real builder script, verified on device
- [ ] Revisit flags: identical banner on every structure competes with the type cue

## Parapets vs. their own garrisons, 2026-08-02

Opened as "parapet occlusion" — the reference hides its crowd's legs behind waist-high parapets,
ours didn't. **The premise was inverted.** Ours were not under-occluding; two of the three fortress
tiers were erasing their garrisons outright, and one of the two causes was not a parapet at all.

### Cause 1 — the crenellation outgrew the men

`MERLON_HEIGHT` was authored 2026-07-29 at 0.20 model units. Against a then-0.77 unit that is
0.50 world vs 0.77 — a correct waist-to-chest crenellation. The 0.77 → 0.48 unit shrink
(2026-08-02) made it **taller than the men standing behind it**, so on the wide tier only the
defenders who happened to land in a crenel gap were visible at all. Merlon pitch and garrison
pitch are unrelated rhythms, so which men survived was arbitrary.

Now 0.09 — 0.225 world, 47% of a unit — so a defender is cut at mid-torso whether he stands behind
a merlon or in a crenel, matching the reference's ~40-45%. The crenellated crown survives as a
toothed course rather than a wall, which matters because it is this set's reserved silhouette and
the reason these tiers stopped reading as stacked crates. The deck front lip (0.05, cutting a unit
at 26%) is deliberately unchanged: that is what the crenel gaps should read as.

**Written into the builder as a fraction of a soldier, with the derivation.** This is the same
failure as `flagMount.scale`, `CRACK_Z_OFFSET` and `crackScale`: a constant authored against
geometry that later started scaling. It is the only defence that has ever worked.

### Cause 2 — the real bug: garrisons on stacked tiers were inside the masonry

`GameViewModel.standingYFor` returned `placement.y + definition.size` while the structure entity
is built at `placement.y * definition.worldScale + size/2`. `STRUCTURE_SCALE`'s pass updated the
writer and missed this reader.

Unstacked structures hid it perfectly — their `y` is 0, and `0 * k == 0` — so every ground-level
garrison was correct and nothing looked broken. Only STACKED tiers were wrong, and by a lot:

    L20 bas_mid   row at 0.8 + 2.0 = 2.8   real deck 4.0   (1.2 world units low)
    L20 top_l/r   row at 1.6 + 2.0 = 3.6   real deck 6.0   (2.4 world units low)

Both rows were embedded in the tier below and drew nothing. **Ten garrisoned elevated placements
were affected — campaign L5, L6, L9, L12 and test L13, L20 have been shipping half-empty
fortresses.** It presents exactly as the parapet swallowing the row, which is what it was mistaken
for through two device passes; GLB-bounds arithmetic settled it, not pixels. Consistent with the
standing method note: geometry and eyes, not colour metrics.

### Verified

On device, uninstall/reinstall: L20's bastion manned on all six platforms (was one and a half),
L6's keep on all three. `modelFrontZ` is unchanged on all three tier GLBs, so the decal anchoring
work is untouched.

### Open

The MID tier's row is still nearly submerged behind its own course — `deckStandZOffset` -0.075
sits it further back than the wide tier's +0.24, so it reads as helmets and an ammo pack. One
number per tier. Not done deliberately: per-tier `deckStandZOffset` tuning was the option
explicitly declined when this pass was scoped.
- [x] Parapet height derived from unit scale; `standingYFor` stacking bug fixed (2026-08-02)
- [ ] Fortress MID tier `deckStandZOffset` — row still submerged behind its own course
