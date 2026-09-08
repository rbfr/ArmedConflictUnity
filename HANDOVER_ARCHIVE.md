# HANDOVER_ARCHIVE.md — closed sections

Split out of `HANDOVER.md` in THREE passes:

| when | what moved |
|---|---|
| 2026-08-11 | the port's first two days, 08-05/06 |
| 2026-08-25 | the 08-07 → 08-11 build-and-fix entries |
| 2026-09-05 | 08-12 → 09-04 sitting logs, plus the resolved 08-07 siege write-up |

Each time the live file had grown past the point where the current state was findable.

**Read the per-pass heading before quoting anything**, and check for a STALE banner: one
section here has been overtaken by later work and carries one.

**Everything here is CLOSED.** These are how a thing was built, not what is true now. They
were kept in full rather than summarised because this project's record is that the reasoning
is the valuable part.

**What did NOT move, and where to look instead:**

- **`HANDOVER.md` is still the live file** — START HERE / Pick up here, standing traps,
  workflow, and what is still owed.
- **"Traps already paid for"** stayed live. Every trap in these archived entries that can
  still bite is distilled there or in `CLAUDE.md`.
- **"Things that will bite"** stayed live.

If you are reading an entry here to understand current behaviour, stop and check the live
file first: an archived entry is accurate about the day it was written and about nothing else.

---

## Level navigation — DONE, 2026-08-05

All levels are reachable (29 at the time; 24 after the biome cut below). `LevelScenery` (runtime) builds the ground, structures, props and
biome backdrop from the level asset and tears them down again; `BattleRunner.LoadLevel(index)` is
Clear + Build. RESTART / NEXT LEVEL appear on the victory/defeat screen, and a ◀ ▶ stepper with a
level readout is always on so the whole set can be swept from adb without a rebuild per level.

What that touched, and the parts worth knowing:

- **Nothing about a level is baked into `Battle.unity` any more.** Baking is what made a second
  level unreachable, and it also meant the one biome L1 happens to use was the only one anybody
  saw in the game. `Assets/Editor/BackdropBuilder.cs` is GONE, replaced by
  `Scripts/Render/BackdropRuntime.cs`; `BackdropPreview` now renders through that same code, so
  the preview and the game can no longer drift.
- **Runtime Materials/Textures/Meshes are not reclaimed when their GameObject dies.** Unity
  collects assets, not instances. Every one `LevelScenery` creates is tracked and destroyed on
  Clear — skip that and walking the campaign leaks a backdrop per level, which is the exact shape
  of the Android build's "a session gets progressively more expensive" defect.
- **Pools are still built ONCE and survive a level switch.** Minting render slots mid-session is
  the failure the Filament build paid for repeatedly. What has to be reset is everything that
  reads a slot's PREVIOUS occupant — a hidden slot still holds the last level's pose and position
  (`HideAll`), and the scorch pool needs re-materialling because its tint comes from the level's
  own ground colour.
- Model prefabs reach the runtime as a name→prefab table on `LevelScenery`, filled by
  `SpikeSceneBattle` from `Assets/Models`: there is no AssetDatabase in a player. Kenney's models
  are excluded and a duplicate bare name is logged, because the table would silently overwrite
  and one structure would quietly render as another.
- **31 more GLBs were imported.** Only outpost/sandbags/rifleman/projectiles had ever been
  brought over — enough for L1, and nothing else.

### The trap that only a device build can show: NO `CreatePrimitive` IN RUNTIME CODE

`GameObject.CreatePrimitive` always attaches a Collider, and IL2CPP MANAGED STRIPPING removes
collider classes from a build that never otherwise references them — this game has no physics at
all. On device the first call logged `Can't add component because class 'MeshCollider' doesn't
exist!` and then threw ArgumentNullException on the `Destroy` of the collider that was never
added, taking the whole level build down with it. The app launched to an empty scene.

It could not have shown up earlier: editor code strips nothing, so the same call is fine in
`SpikeSceneBattle` and `BackdropPreview`, and every primitive used to be baked at author time.
`PortSelfTest` and the headless scene build both passed clean immediately before it.

`Render/QuadMesh.cs` is the fix — a shared unit quad carrying Unity's own vertex layout (normal
-Z, so every caller's 180° face-the-camera turn and the scorch's 90° lie-flat stay correct), plus
`Create(name, parent, mat)`. It fixes the root rather than null-guarding the Destroy: a collider
on a backdrop quad was never wanted. **Use it for any new runtime geometry.**

### The other build-order trap: CreateAsset REPLACES, so references taken earlier dangle

`MakeScorchPrefab` calls `AssetDatabase.CreateAsset` on `Scorch.mat`, which does not overwrite in
place — it replaces the asset and mints a NEW guid. `WireScenery` ran first and loaded the old
one, so `scorchSource` serialised as `{fileID: 0}`: one null among dozens of correct references
in a scene file that otherwise looked perfect. On device it threw ArgumentNullException from
inside Material's copy constructor. The prefab is now built BEFORE the scenery is wired, and
`WireScenery` logs an error at build time for any null material — this class of failure should
never again reach a device to be diagnosed.

### Two silent data-loss bugs found on the way, both now fixed

Neither could show up while only L1 was reachable, and neither was visible in any count.

1. **`FortressTier` never imported at all.** `val FortressTier = FortressTierUnscaled.scaled()`
   has no `.copy` in it. The exporter's ident reader swallows dots, so it arrives as a ctor NAMED
   `FortressTierUnscaled.scaled`, and `extract_vals` accepted only the `.copy` form — so it was
   dropped. Worse, a bare identifier does not start with a wanted ctor name, so `looks_wanted`
   was false and it was not even recorded as unparsed. **Five levels place it** (L6, L9, and the
   bastion / structure-parade-B / tier-collapse rigs) and every one threw a
   NullReferenceException on load. Fixed in both `export_kotlin_data.py` and `DataImporter`:
   any DERIVING method counts, not just copy.
2. **`Capture` dropped every optional field**, so a `.copy()`/`.scaled()` that did not restate one
   silently lost it. It hid because the wide and small tiers restate all of theirs, and the one
   val that restates nothing was the one being dropped by (1). The three PLAYER fortress tiers
   were live victims: no `hitWidth` (so the collision box fell back to `size`) and NO damage
   chunks (so a player structure could never shed geometry). Now captures hitWidth, deckY,
   cannon, flagMount and damageChunks — hitWidth/deckY only when the base HAS them, since their
   presence is the signal and an unconditional -1 reads as "measured, and it is -1".

`PortSelfTest` now builds an initial state for EVERY level, checks `levelNumber == index + 1`
(the switcher indexes by position), and checks every structure and prop the campaign places has
an imported model. That check finds this class of bug in the same second as a typo; a device
sweep finds it at about a minute a level.

## Forest reworked, and the preview was lying — 2026-08-05

**`BackdropPreview` rendered EVERY biome as bare sky and ground, and reported success.**
`EditorSceneManager.NewScene` triggers an unused-asset unload, and a freshly emptied scene
references nothing — so a `BackgroundDefinitionSO` loaded BEFORE it has its native object freed
and becomes Unity's fake null: `bg == null` is true while `bg.style` and `bg.groundColor` still
read correctly off the managed wrapper. The old preview never noticed because it only ever read
fields; `BackdropRuntime` opens with a null guard, right for the game and silently true here.
Fixed by loading the background AFTER the scene, and the preview now logs an error if a biome
builds zero layers. **Do not trust a preview you have not sanity-checked against the device** —
this one passed the eye test for a whole session by producing plausible sky-and-ground images.

**Forest read as GREEN MOUNTAINS**, on the one campaign level that uses it (L2). Two causes:
- The hills were made TALLER than the treeline (15 units vs 11) to stop the ridge hiding behind
  the woods. That won the argument and lost the biome — the pale ridge owned the skyline.
- Nine crowns spanning the frame makes each one an eighth of the screen wide, and a triangle that
  wide is a hill however it is shaded.

Now ordered by ANGULAR height — hills 0.22 < mid trees 0.30 < near trees 0.42 — so the trees own
the skyline and the hills show through the gaps as a backdrop mass. `Treeline` gained two
parameters rather than having its constants fought: `crownScale` (a conifer at this distance is
about half as wide as it is tall; at 1.0 with a high count the spire comes out nearer a fifth,
and a row of those is REEDS) and `floor` (the solid canopy mass under the crowns — at 0.35 the
sky came down between every pair and the band read as a fringe). **The floor also WANDERS now**:
a constant one is a ruler laid across the full frame, the same failure a flat snowline has.

Both documented failure modes were re-hit while tuning this — 24 narrow trees gave the "reads as
GRASS" result exactly as the old comments predict, and 9 wide ones give hills. The window is
narrow; change count and crown width TOGETHER, and judge which band owns the skyline.

## Ocean ported and given a level — 2026-08-05

`BackgroundDefinitions.Ocean` was authored and referenced by NOTHING, in the Kotlin and the port
alike, so no build had ever displayed it. It now has **L30 `TEST — Oceanfront`** — authored in the
Kotlin and re-exported, because the pipeline is one way. The campaign+test total is 30; it was
APPENDED rather than filed with the other rigs, since the switcher indexes by position and
inserting mid-list would silently renumber everything after it.

The plan itself was one flat teal band. Ported from the Filament `drawOcean`: sea gradient, a sun
with a radial glow sitting ON the horizon, the scattered sun-glitter path, and the scalloped foam
surf line. **The ripple rows are NOT ported** — a ripple is a wavy LINE and the decal mechanism
draws rectangles, so away from the sun they read as debris floating on the water. That wants a
strip mesh like the silhouettes have. The drift does not need porting at all: the Filament version
scrolls each row by a hand-tuned fraction of pixels-per-unit, and here real depths parallax free.

Three traps, all of which cost a render:

- **`Mathf.SmoothStep` is NOT GLSL's `smoothstep`.** It is a smoothed LERP BETWEEN its first two
  arguments, so `Mathf.SmoothStep(0.26f, 0.34f, d)` returns a value in [0.26, 0.34] for every d
  and `1 - that` never falls below 0.66. That is a near-constant alpha across the whole quad,
  which drew the sun as a cream RECTANGLE with a brighter blob in it. `BackdropRuntime.Threshold`
  is the real thing. **Note `MakeScorchPrefab` uses the same call** and gets away with it only
  because its edges happen to be 0.45 and 1.
- **Anything shaped by alpha must clone a TRANSPARENT material ASSET** (`BackdropFadeSource.mat`).
  `unlitSource` is opaque and a copy ignores alpha entirely. Flipping `_Surface` and the blend
  modes on the copy at runtime is not a reliable substitute.
- **A layer sunk the way a RIDGE is sunk disappears behind the ground plane.** The surf was
  authored at BaseY -1.9 of a 2.8 band, so the ground occluded all but the tallest scallops and
  the foam came out as one straight white rule — the exact thing it exists to prevent.

And one that only the DEVICE could show, because `BackdropPreview` renders from x = 0:
**a fixed backdrop feature is offset from the WORLD ORIGIN, not from the frame.** The backdrop is
world-fixed and the camera is not — at Aiming it sits over the PLAYER LINE, around game x -9.5.
A sun placed at a frame-relative-looking -0.20 of the sea width landed 92% of a half-frame right
of that centre and was cut in half by the screen edge, while looking perfectly placed in the
preview. Judge any fixed feature at the camera position the PLAYER sees, and leave it room to
travel: the pan is real parallax, so the sun crosses the frame during a volley.

## Campaign cut to ONE LEVEL PER BIOME — 2026-08-05

Seven campaign levels, one per background: L1 Mountains, L2 Forest, L3 MountainsDusk, L4 Winter,
L5 Desert, L6 CityRuins, L7 Ocean (promoted from the test rig). Six levels whose biome was already
covered were DELETED from the Kotlin — they are in git. The 17 test rigs are kept for reference
and renumbered to L8-L24. Four stages over the seven, 2/2/2/1, gates at 0/3/6/9.

Total is now **24**, not 29. Two things in this repo carried the old count and both are fixed:

- **The importer never deleted ORPHANS.** It creates and updates, so a level removed from the
  Kotlin left its `.asset` behind — and `SpikeSceneBattle` collects EVERY `LevelDefinitionSO` it
  can find and orders them by `levelNumber`, so a deleted level rejoined the campaign silently at
  whatever number it used to hold. Six were stranded. `DataImporter` now sweeps any level asset
  the Kotlin no longer declares. The Kotlin is the source of truth in BOTH directions.
- **`BuildSandboxLevels` was a SECOND source of truth for level numbering.** The exporter cannot
  parse `rosterSandbox`, so the importer rebuilds those eight — with their numbers hardcoded at
  21-28. The Kotlin renumbered them to 16-23 and the importer silently did not, breaking
  `levelNumber == index + 1` and with it the level switcher. It now derives the number from the
  level's position in `levelOrder`. The composition is duplicated because it has to be; the
  ordering is not. **`PortSelfTest` caught this** — it is exactly what that check is for.

The Android repo's long-standing test failure is also gone. `FactionPaletteTest` hardcoded level
numbers 1/7/13/19 as one-per-stage, which were correct for the ORIGINAL 25-level campaign and
meaningless after it was rebuilt — by now two of the four were TEST levels, which sit in no stage
and deliberately fall back to the last one, so it asserted 4 distinct factions against 2. It now
derives its numbers from `StageDefinitions`. **50 tests, 0 failures.** A test that hardcodes level
numbers expires the next time the campaign is re-cut.

## Structures shed their own geometry — ported 2026-08-05

Reported as "just squares/bricks that fly" against the Filament build's real damage. The port had
the DATA (`damageChunks`, measured per structure), the entity field (`StructureEntity.ShedChunks`)
and the curve (`StructureDamage.ShedChunkCount`) — and nothing called any of them. Destruction
threw ten random cubes sized off `size`, so a hit building shed bricks that had never been part of
it, and only ever at the moment it died.

Now, as in the Kotlin: `chunk_N` groups vanish from the model in ascending N as HP drops, and the
tick spawns the SAME group as falling rubble from exactly where that geometry stood. The gap in
the silhouette plus the pile at the foot is the damage read, and it persists for the battle.

Both halves derive from `ShedChunkCount` — the renderer reads the tick's own `ShedChunks` rather
than recomputing, so they cannot disagree and drop a piece the building still has.

Carried across from the Kotlin, each of which was a visible failure there first:
- A group splits along its LONGEST axis, so a sandbag course scatters as bags instead of dropping
  as one long bar.
- A piece is sized from its VOLUME, cube-rooted and clamped — NOT the mean of its dimensions. The
  mean is dominated by the long axis of a flat plate: a wide tier's wall plate means out at 0.73,
  three times the largest destruction chunk, which read as slabs bigger than the wall they fell off.
- Barely thrown (vy 0.5, vx spread 0.9): it is coming loose under its own weight, so it reads as
  falling OFF the building rather than being launched.

Unity-side notes: chunk groups are collected ONCE at scenery build time, because grouping is a
string parse over every child node and doing it per frame per structure is the per-slot rescan the
Filament profile warns about. Grouping is by TRAILING NUMBER, not prefix — `chunk_3`,
`accent_chunk_3` and `trim_chunk_3` are one group, and matching the prefix would shed a wall's
stone and leave its trim hanging. Renderers are toggled rather than GameObjects, since a chunk
node may carry children.

Verified on device on the demolition rig: the garrison post's wall panels vanish one at a time as
HP falls 225 → 121, and shed rubble settles against its base.

## Per-class unit art — DONE, 2026-08-06

Every unit class used to render as the same rifleman. It now renders as itself: seven rigged
silhouettes (six crowd classes plus the hero), on the SAME skeleton, so one set of retargeted
Kenney clips still drives all of them. Verified on device — the L9 parade shows six readable
outlines, a 24-level sweep logs no missing slots, and a four-volley run on L18 (26 v 26) holds
60 fps with no exceptions.

Three things had to change together, and only the first is art:

**The models.** `tools/blender/build_units_rigged.py` in the Android repo supersedes
`build_unit_rigged.py` (which built the rifleman alone as the go/no-go test). It ports v6's
per-class props — ghillie, ammo drum, rocket tubes, shell bags, riot shield, greatcoat and cap —
onto the limb hierarchy, keeping v6's own measurements and comments, because those numbers are
the output of seven documented attempts in `UNIT_VARIETY_DESIGN.md`.

- **POSE is gone and that is fine.** v6 differentiated partly with a lean, a hunch and a
  fore/aft stagger; the idle clip owns those now. Per that doc every pose-only pass was reported
  as "the same soldier" at gameplay scale, so the loss is small. STANCE survives — a leg pivot's
  position is free, and the machine gunner still stands wider than the sniper.
- **Z is remapped through LANDMARKS, not scaled flat.** v6 puts a shoulder at 72% of height and
  the rig at 80%, and the hero has its own landmarks again (its waist is a belt at 0.86, not a leg
  seam at 0.67). A flat `z * K` floats a pauldron most of a shoulder off the body and the hero's
  cap a head above its neck.
- The port is checked by MEASUREMENT: `python3 tools/measure_units.py` reports the legs/torso/head
  band profile for the whole set, and `--legacy` measures the v6 originals for comparison. The
  rigged set reproduces the legacy spread almost exactly (hero 37/32/27 px against 38/33/29).
  **Judge the SPREAD ACROSS THE SET, never one class alone** — hitting every individual target is
  what destroyed the spread in that doc's Attempt 7. Note the projection plane differs from
  `measure_structures.py`: a unit is seen in PROFILE, and UP IS ALWAYS glTF Y (Blender's exporter
  converts Z-up on the way out — reading Z as up measures the model from above and every band
  comes back the same width, which is exactly what the first run of that tool did).

**The fourth tone.** `Tone()` implemented skin / accent / uniform and had no `trim`, so every
prop above fell through to the side's uniform colour — the ghillie, the ammo drum and the rocket
tips were all just more green. `RiggedUnits.TrimColor` carries SceneHost's per-class palette over
verbatim. Trim is held CONSTANT across both armies on purpose: the uniform says which side a
soldier is on and the trim says which class he is, and a faction palette touching the trim would
collapse the two readings into one.

**Per-class render slots.** `BattleRunner` pooled one prefab per side, which cannot work once the
classes have different geometry — swapping the model on a live slot is exactly the mid-session
mint the Filament build kept paying for. `UnitSlots` is a pool PER CLASS per side, and the sizes
come from the level data (`ClassCounts`), not a constant:

- Live units and RAGDOLLS share a pool, so a class is sized by everything a level ever SPAWNS
  rather than by everything alive at once — a corpse holds its slot while the live roster shrinks.
- Index arithmetic that assumed one flat pool had to go. `VolleyAnim` used to fire the first N
  slots; with per-class pools "the first N" is the first N of whichever class enumerates first,
  which would fire some soldiers twice and leave others standing. It reads `UnitSlots.Live`, which
  `SyncUnits` fills in roster order.
- `PortSelfTest` asserts every class the campaign FIELDS has both a rigged model and a per-side
  prefab. A class added to the Kotlin roster with no builder fails there in a second; without it,
  it is a soldier who never appears, found on a device.

**And `renderScale` reached the port as a formation number only** — it spread the heroes apart
and never made them bigger, so a hero authored at 1.9x rendered at exactly crowd size. Invisible
while every class shared one model, and the whole point of the hero the moment it has its own
greatcoat-and-cap body. `SyncUnits` now multiplies it onto the prefab's normalised scale.

## Health bars and the free camera — 2026-08-06

**A damaged unit now carries a health bar.** Before this, a wounded soldier was audible and
nothing else — the tick counted `TotalWoundedHits`, but a running total can say that SOMETHING was
hit and never WHICH, and with 32 HP against 8 damage most hits wound rather than kill, so the
common case was the unreported one.

- **Hidden until the unit has taken damage**, which needs NO new state: "has been hit" is
  `Hp < Definition.maxHp`. A whole line at the start of a turn carries nothing.
- **It FADES OUT a few seconds after the hit** (`CosmeticSystems.HealthBarSeconds`, 3s, with the
  last 0.7s spent fading), driven by `UnitEntity.LastHitAge` rather than by "is currently
  wounded". It first shipped persistent-while-damaged and that was rejected in play: the player
  has read the hit by then, and a bar over every damaged survivor turns a 26-strong line into a
  second HUD laid on top of the army. Re-armed from zero on every hit, so a unit under sustained
  fire keeps its bar rather than having it expire mid-bombardment.
- **BOTH quads fade, not just the fill.** Fading the coloured fill alone leaves the dark backing
  plate behind as a floating black tick over the soldier's head — a worse artefact than the bar it
  was retiring.
- **The material has to be a TRANSPARENT asset** (`HealthBarFadeSource.mat`, from the same
  `FadeSource` helper the ocean sun uses). An opaque URP/Unlit ignores alpha completely: the bar
  would hold full strength and then vanish on a single frame, which is the failure this repo
  already paid for once on the backdrop.
- Green above 0.6, amber above 0.3, red below. The fill is anchored to the LEFT edge, so damage
  eats it from one side; a centred fill shrinks toward the middle from both ends and reads as a
  charging meter rather than a wound.
- **The fill never drops below `BarMinFill` (22%) of the track**, and the empty track is DARK RATHER
  THAN BLACK. Reported as "I see a black health bar — shouldn't that mean they're dead?", and that
  was the cue failing exactly where it mattered most: the bar is ~30px wide, so a linear fill at
  25% health is SIX PIXELS of colour against a near-black track, which reads as a broken bar rather
  than as a dying soldier. The floor deliberately breaks the linear mapping at the bottom end,
  which is the right trade — down there the COLOUR carries the message and the exact fraction does
  not, and a message too small to see carries nothing. Note the COLOUR is still picked from the
  TRUE fraction; flooring both would make a dying unit read as merely wounded.
- **Both sides.** The tactically useful reading is which ENEMY is nearly dead, and the player's
  line has to answer the same question when it is being shot at.

Three things about how it is built, each of which is a rule this repo already paid for:

- **Sized against `UnitGeometry.UnitScaleUnits`**, like every body-relative thing here. The WIDTH
  is bounded by `Formation.MountedColumnSpacing` (0.187) rather than by the body: a garrison packs
  tighter than a ground line, so a bar sized to look right on open ground overlaps its neighbour's
  on a parapet — which is exactly where damaged units most need counting. It does NOT scale with
  `renderScale`; only its height offset does, so a hero's bar clears his cap without becoming a
  bigger, more important-looking bar.
- **Quads come from `QuadMesh`, never `GameObject.CreatePrimitive`** — IL2CPP strips the collider
  classes CreatePrimitive silently attaches, and on device that took the whole level build down.
  The bar is turned to face the camera with a 180° flip about **X, not Y**: turning about Y would
  also mirror local x, and the fill anchors to one end, so the bar would drain right-to-left.
- **Pre-warmed with every other pool**, sized from the level data. A bar minted the frame a unit is
  first wounded is a render slot created mid-gameplay, which is the failure the Filament build paid
  for repeatedly.

The bar REPLACED a hit flash (a near-white tint for 0.12s) built earlier the same day. The flash
worked and was rejected on the ask: it says a unit was hit and cannot say how badly, and "how
badly" is the part that changes what you aim at next. Its `HitFlashAge` field, tick step and
self-test checks were all removed rather than left dormant.

**And the free camera is back**, ported from Android's `ui/battle/DebugCamera.kt`: a CAM button
beside the level stepper, a six-button pad, and a live x/y/z readout. It HOLDS, through volleys and
the victory screen — that is the whole feature. It confirmed L1's bunker garrison stands correctly
on its deck in about ten seconds, which is the kind of question that otherwise costs a volley, a
screen recording and a frame hunt.

Two things it is worth knowing about:

- **Its x is GAME space, not Unity space.** `GameSpace.CameraX` negates, so a raw Unity x made the
  "→" button pan the view LEFT — it visibly did on the first device run. The readout matters as
  much as the button: it exists to be written down and compared against level data, which is
  authored in game x, and a tool that reports the mirror image of the coordinate you are hunting
  is worse than no readout at all.
- **It suppresses shake.** A tool for judging whether a thing is in the right PLACE cannot have
  the view jittering under it.
- **The pad is HELD, not tapped** (`GUI.RepeatButton` — a plain `Button` only fires on release,
  which is why the first version cost a tap per step). Movement is a RATE integrated against dt,
  not a per-frame step, and it ACCELERATES to 4x over 1.2s of holding: crossing a level is ~15
  units, nearly four seconds at a flat rate and about one and a half ramped, while the first
  moments stay slow enough to place the camera precisely. Measured on device: one 2s hold on OUT
  moved z 6.26 -> 31.16, which is 50 taps of the old pad; a 0.15s tap still moves 0.73 units.
- The held direction is recorded in OnGUI and CONSUMED IN UPDATE. OnGUI runs several times per
  frame — once per input event plus Layout and Repaint — so moving the camera inside it applies
  the movement an unpredictable number of times and the speed then depends on how much input the
  OS delivered.
- **A touch that starts on the pad is excluded from the aim drag.** With tap-to-step this never
  mattered, since `Release()` ignores a drag under a threshold and a tap barely moves; a finger
  resting on OUT for two seconds drifts on the glass, and on release that fired a volley and ended
  the turn. The camera tool must not be able to play the game.
- **From adb, `input tap` is now too brief.** Press-and-hold is `input swipe X Y X Y 600` — the
  same point twice, with a duration.

### Method note, because it cost an hour

The flash was diagnosed as "not rendering" from a screen recording twice before it turned out to
be working the whole time. Both times the detector was wrong, not the code: the first pass hunted
near-white pixels on WINTER ground, which is near-white, and the second sampled frames five
seconds after the volley instead of the one second where the rounds actually land. What settled it
was a temporary probe logging both ends — the tick arming the flash and the renderer applying it —
which printed `flash=True renderers=11 mat=Universal Render Pipeline/Lit` on the first run.

That is the same lesson this file already records four times over, in a new costume: **verify
CONTENT, and prefer positive evidence over a plausible cause.** A pixel search that finds nothing
is not evidence of absence until you have proved the search can find the thing when it IS there.

### Known, pre-existing: a unit's slot is not stable across frames

`UnitSlots.Take` hands slots out in roster order, so when a soldier dies everyone behind him
shifts down one slot. Per frame the assignment is still a bijection — every live unit gets a slot
of its own class at its own position — so the flash and the positions are correct. But anything
slot-STICKY drifts: `UnitAnim`'s clip time and its hidden→visible re-arm belong to the SLOT, not
to the unit, so a soldier can inherit a neighbour's animation phase when the rank in front of him
thins. The old flat pool indexed by order too, so this is not a regression from the per-class
change, and with full-roster volleys every unit is playing the same clip anyway. If per-unit
animation state ever matters, the fix is to key slots by unit id rather than by position.


## The tank shell, restored — 2026-08-06

**The player tank never fired.** `TankShellsRemaining` and `CannonArmed` were in `GameState`,
`LevelBuilder` totalled the ammo from every player structure with a cannon, `CannonSpec` imported
cleanly with its muzzle offsets and its `velocityBoost`, the Shell projectile type existed and its
prefab was pooled — and nothing ever built a shell. `FireVolley` spawned one bullet per unit and
stopped.

`BattleTick.CannonShells` is the missing piece: one heavy round per player-side structure that
mounts a cannon, added to the volley the infantry just threw. It is OFF-ROSTER — built from a
STRUCTURE, not a unit — so losing every soldier does not silence the tank, and the tank is not a
body the enemy can shoot at. Ammo is finite and `CannonArmed` gates it, so a level can field a
tank with a cold gun. No jitter: the infantry are spread on purpose, but a rifled gun puts its
round where it is pointed and a wandering shell reads as a bug.

**A test was passing over the hole the whole time.** `"the player tank contributes its cannon
shells"` asserted `TankShellsRemaining > 0` after the level was built — that the ammo had been
IMPORTED, never that anything fired it. Same family as the four failures this file already
records: it measured the input and called it the output. The checks now fire a volley and assert
a Shell comes out, that it carries its structure multiplier, that the ammo is SPENT, that it
stops at zero, and that `CannonArmed=false` fires nothing.

**And the same edit found a second hole.** The PLAYER's volley left `Type`, `SplashRadius` and
`StructureDamageMultiplier` at their defaults, so every round a human fired was a plain bullet
with no splash and a 1x structure multiplier. `AutoFire`, three methods down, set all three
correctly — so the rocket trooper's 6x against buildings and the grenadier's 2x existed only
under the debug driver, and a rocket rendered as a tracer. **Auto and the player firing through
different code is exactly how that survived**; anything that only Auto exercises is not tested.


## The corpse that came back — 2026-08-06

Restarting a level brought the whole enemy line back **lying on their backs**, playing a perfect
breathing loop on the ground.

`die` is the ONLY clip that drives the ROOT; every other clip is rotation on the joints below it.
Legacy `Animation` leaves a transform wherever the clip last sampled it when you stop, so
`anim.Stop()` + restart-the-idle brings back every joint EXCEPT the root — which stays face-down
on the floor. `UnitAnim.Stand()` now restores the root's authored rest transform explicitly,
captured in `Awake` before any clip has played (the only moment it is guaranteed to be at rest
rather than at the last frame of whatever ran on that slot).

**This was LATENT, and per-class pooling exposed it.** With one flat 48-slot pool per side, a
corpse took a high index that a fresh roster of ten never reached, so the death pose sat in a slot
nobody looked at. Per-class pools are sized to what the level actually fields, so a corpse takes
the slot immediately after the living — and a reload hands those exact slots straight to the new
roster. The pooling change did not break this; it stopped hiding it.

`PortSelfTest` asserts the SHAPE rather than the symptom: `die` drives the root and `idle` does
not. A future clip set that breaks that assumption says so in a second instead of on a device.

**The general rule, worth applying to anything else recycled: stopping an animation does not undo
it.** Ask what each clip WRITES, and make sure something restores every one of those channels —
not just the ones the next clip happens to drive.


## Contact shadows, and what this camera does to ground decals — 2026-08-06

Reported as: on the snow level the soldiers look like they are "standing on white space".

**The port had no unit shadows at all** — only a ported COMMENT in `BackgroundDefinitionSO`
mentioning that `groundNear` feeds the contact-shadow tone. The Filament build has them; the port
never got them. On the tan biomes that is nearly invisible, because the ground is far darker than
the sky and the horizon carries the ground read on its own. On WINTER the ground is near-white
under a pale sky, so with no shadow there is nothing at all saying where the surface is.

Two things had to be right, and only the first is obvious:

- **Tone comes from THIS level's ground**, scaled by 0.58 / 0.62 / 0.72 — the Filament build's
  numbers, and they are not uniform on purpose. A flat grey that works on snow is a black blob on
  CityRuins ash and invisible on Forest green, and BLUE is kept highest so the shade COOLS rather
  than muddies. Snow shadow goes blue, not grey-brown.
- **The ellipse is stretched 3.2x in DEPTH**, and that is forced by the camera rather than being a
  style choice. The battle camera sits ~1.2 up at ~10 back — about SIX DEGREES above the ground
  plane — so a decal lying flat is seen almost edge-on and its on-screen HEIGHT is its world depth
  times the sine of that angle, about a tenth. A round shadow 28px wide projects to a 3px smear,
  which is exactly what the first pass drew and why it read as nothing. Widening does not help
  (it just makes a wider smear, and it collides with the neighbour's); DEPTH is free, because the
  camera looks along it, and it is the only axis that buys screen height.

**This applies to every ground decal in the game, not just shadows** — scorch marks are subject to
the same projection and are why a burn reads as a smear. Anything new that lies flat on the ground
has to be sized in depth, not in width.

The falloff also needed a real solid core. The first version shouldered from 0.12 — nearly all
penumbra — which on snow is a smudge too faint to be anything. And note `Mathf.SmoothStep` is a
smoothed LERP BETWEEN its arguments rather than GLSL's `smoothstep`, so the useful knob is where
the ramp STARTS, not a threshold; the texture builder now ramps explicitly instead.

### Health bar: the track fades faster than the fill

Equal alpha is not equal legibility. The track is near-black and the fill is a saturated colour,
so against any of this game's grounds the dark track keeps far more contrast at the same alpha.
Faded together, the colour washes out first and the bar spends its last half-second as a DARK
HUSK over a soldier's head — which is very likely what "black means dead, right?" was actually
reporting, more than low health was. `HealthBarTrackAlpha` squares the fill's alpha, so a bar
always dissolves down to its COLOUR and never down to a black rectangle.


## Ragdolls: lean, and stopping at walls — 2026-08-06

Two reports: bodies flew backwards perfectly upright, and they flew THROUGH structures.

**The lean.** The tick has always spun a corpse at 220 deg/s, and the renderer was throwing that
away for animated units (`rotation = identity`) — correctly, at the time, because applying the
full spin on top of the `die` clip made a body fold AND cartwheel. Discarding it went too far the
other way: a statue on rails. `RagdollLeanDegrees` shows a FRACTION of the tumble (0.32) with a
CAP (38 deg), so the body pitches back as it is thrown and then holds that lean while the clip
does the folding. The cap is reached about a third of a second in, so it rises and settles rather
than winding up. Signed by side, because the two lines are thrown in opposite directions.

**The walls.** `StepRagdolls` had no notion of structures at all, so a body sailed through a
bunker — which is the one place a purely cosmetic system stops being cosmetic, because a body
passing through a building says the building is not there. `BlockOnStructures` stops it at the
face it arrived through and rests it on the ROOF if it cleared the wall.

It blocks on EVERY structure, not just the opposing side's. Projectiles deliberately pass through
FRIENDLY structures so a garrison can fire over its own fortress; a body has no such excuse, and
the most visible case is a player unit thrown backwards into the player's own tank.

`CollisionSystem.StructureBox` is now the one place that builds a structure's solid box — the
same box the projectile path uses, including the deck-vs-size distinction that once made a
garrison unkillable. Two hand-rolled copies of that arithmetic is exactly how the two would drift.

NOT yet judged in play: whether 0.32/38 is the right amount of lean. It is deliberately subtle.


## Data authoring, once Android is retired — CLOSED 2026-08-06 (kept for the reasoning)

**Decided and executed: authoring moved INTO UNITY.** The section below is the question as it
stood; what actually happened is two sections down, under "Data authoring moved into Unity".

The Android build stopped being the shipping build on 2026-08-06. One thing did not move with it:
**game DATA is still authored in Kotlin** and reaches Unity one way, through
`tools/export_kotlin_data.py` -> `data.json` -> `DataImporter` -> ScriptableObjects.

That was obviously right while Android was the product and Unity was the port. It is no longer
obviously right, and it is worth an explicit decision rather than drifting:

- **Keeping it** costs a second repo, a second toolchain and an export step on every level tweak,
  in a codebase nobody ships any more. It also keeps a real hazard alive: `DataImporter` REBUILDS
  the eight roster/grouping sandboxes itself because the exporter cannot parse their Kotlin
  generator, so the two halves of the level list already come from different places.
- **Moving authoring into Unity** means the ScriptableObjects become the source and can be edited
  directly — but it throws away a parser that has been debugged hard (`FortressTier` silently
  dropped, `Capture` losing optional fields, ARGB losing its low byte to a float mantissa), and
  the Kotlin files carry a great deal of design commentary that would need a home.

Nothing here is urgent — the pipeline works. But the reason it exists is gone, so the next person
to be annoyed by an export step should treat that annoyance as a real signal, not as friction to
be absorbed.

**Resolved the same day.** The annoyance was real and the move was smaller than feared: nothing
had to be migrated at all.

## Data authoring — DECIDED 2026-08-06: it moves into Unity

Rob closed the question above: **authoring moves into Unity.** The ScriptableObjects become the
source of truth. Not yet executed — it is Phase A of `_plans/archive/TIER0_PLAN.md`, and the work is
mostly DISARMING the importer rather than migrating anything, because the assets are already
correct and nothing gets re-parsed.

The one thing that must not be skipped: `DataImporter.Sweep` deletes any asset the Kotlin no
longer declares. That is correct while Kotlin is authoritative and is a data-destroying bug the
moment Unity is. It goes, `BuildSandboxLevels` comes out of the import path, and `Import` gets a
guard rather than the "never re-run this" comment it has carried for months.

## The victory screen and a live economy — 2026-08-06

`PRODUCT_DIRECTION.md` Tier 0.3/0.4a/0.5. **The port had a complete, tested, entirely DEAD
economy**: `EconomyStore`, `ProgressStore` and `TurnFlow.AwardVictory` were all ported and correct,
`AwardVictory` had ZERO callers, and no coin was ever earned or star ever recorded in a running
build. The whole of it came alive through one call site — `BattleRunner.ResolveBattleEnd`.

Keyed on `battleId`, NOT on a `Playing -> over` edge. An edge is one frame and the award has to
survive everything that keeps ticking after it (the free camera alone keeps a finished battle
running indefinitely); keying on the battle makes "pay once per battle" the literal invariant. A
replay pays again on purpose — the one-time parts are gated inside `GrantVictoryPayout` by
`previousBestStars`.

### The UI layer is BUILT IN CODE, and that is deliberate

`ArmedConflict.UI.BattleUI` constructs its whole hierarchy at runtime — no prefab, no serialized
references, therefore **no scene rebuild for any UI change**. The editor GUI runs over VNC on
llvmpipe where laying out a canvas by hand is genuinely painful, and there is no designer who
would edit it in the inspector. It is still real retained-mode uGUI, built once, allocating
nothing per frame.

`Build()` is called explicitly from `Create()` rather than from `Awake` — **Awake does not run in
edit mode** without `[ExecuteAlways]`, which left every widget null the first time the preview
harness built this canvas from an editor method.

### Traps this phase paid for

- **NOTHING OUTSIDE ASCII MAY APPEAR IN A TMP STRING.** The default `LiberationSans SDF` font
  asset is built over ASCII only, so `★` and `◆` render as missing-glyph boxes — silently, with no
  error. This was written into the code with a comment explaining it, and then `★` and `◆` were
  used in four strings anyway; only the rendered image caught it. The panel's stars and the coin
  icon are DRAWN SPRITES for this reason, and `TurnFlow.StarReason` says "3 stars" in ASCII with a
  self-test check asserting it contains no `★`. (The em-dash `—` does render — the asset covers
  Latin-1 punctuation. Verify anything else before using it.)
- **`AssetDatabase.ImportPackage` is ASYNCHRONOUS and imports NOTHING under `-quit`.** It is the
  documented way to install TMP's essential resources and it silently does nothing headless. They
  are unpacked directly instead by `tools/import_tmp_essentials.py` — a `.unitypackage` is a
  gzipped tar of one folder per asset holding `asset`, `asset.meta` (the GUID, which must come
  across) and `pathname`. One-time; the output is committed.
- **IMGUI always draws AFTER a ScreenSpaceOverlay canvas.** The old RESTART / NEXT buttons had to
  be REMOVED, not merely covered — they would have painted over the card and gone on eating its
  taps.
- **A ScreenSpaceOverlay canvas never appears in a camera's target texture.** An offscreen shot of
  one comes back empty; `BattleUIPreview` switches the canvas to `ScreenSpaceCamera` for the render.
- **Do not measure "did the text render" in pixels.** The first attempt counted pixels differing
  from the backdrop and reported 98.5% — meaningless, because the card's full-screen dim covers
  every pixel whether a glyph resolved or not. Ask TMP: `textInfo.characterCount` is non-zero only
  when a font asset resolved AND the string laid out. Count ACTIVE labels only; a hidden button's
  label never lays out and reads as a false failure.

### CONFIRMED ON DEVICE 2026-08-06

Pixel 10 Pro XL, release build. L1 driven to victory on AUTO:

```
[Battle] victory: 3★, +230 coins (Daily Bonus!), balance 230
```

Fired exactly once. The card rendered with every glyph, held a steady 60 fps, and the coin pill
carried 230 into L2. **NEXT was tapped and L2 loaded** — the EventSystem, touch and uGUI buttons
all work on hardware, which nothing in the editor could have shown. The card cleared on the level
switch. CAM hid the whole canvas and brought it back.

`Auto` is enough to confirm the card, the payout and the buttons. It says nothing about
difficulty, and the 3★ it produces is optimistic — measure balance with real drags.

**The dim looked broken and was not.** Eyeballing the screenshot said the full-screen dim had
failed to render; sampling the same pixels with the canvas hidden said otherwise — ratio 0.55,
which is exactly a 0.72-alpha black composited in LINEAR space and written out as sRGB
(0.28^(1/2.2) = 0.56). A URP overlay dim always reads far lighter than its alpha suggests. Do not
judge one by eye, and do not "fix" it by raising the alpha.

### Verify this again with

`DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod BattleUIPreview.Shots -logFile -`
writes the three cards to `Builds/ui/` and reports how many labels actually laid out glyphs.

## Data authoring moved into Unity — DONE 2026-08-06

Phase A of `_plans/archive/TIER0_PLAN.md`. **The ScriptableObjects in `Assets/GameData/` are now the
source of truth.** `CLAUDE.md`, `README.md` and `PRODUCT_DIRECTION.md` all say so; the section
above describing the one-way Kotlin pipeline is history, not instruction.

Nothing was migrated, because nothing needed to be. Re-running the exporter produced a `data.json`
byte-identical to the committed one, so the assets were already at the Kotlin's last word and the
exporter's hard-won parsing (FortressTier, Capture's optional fields, ARGB's low byte) is baked
into them. **The work was disarming the importer, not moving data.**

### What changed

- **`DataImporter` → `LegacyKotlinImport.ImportOnce`, and it REFUSES to run** without
  `-iAcceptDataLoss`. It still overwrites every asset in place with no undo. It carried a "never
  re-run this" comment for months while remaining one command away from destroying a day's
  authoring; a guard is cheaper than the incident. **Do not remove it.**
- **The orphan sweep is GONE.** It deleted any asset the Kotlin no longer declared — correct while
  the Kotlin was authoritative in both directions, and a shredder now, since a level authored in
  Unity is by definition one the Kotlin does not declare. The price, stated plainly: an asset
  deleted from the Kotlin now survives here, and `PortSelfTest`'s `levelNumber` contiguity check
  is the only thing left that catches a stranded level rejoining the campaign at its old number.
- **Sandbox generation is now `SandboxLevels.Generate`**, a command rather than a side effect of
  every import. That was the second source of truth for the level list. It reads the
  ScriptableObjects and **preserves each rig's existing `levelNumber` and `id`** rather than
  deriving them from `levelOrder`. Verified faithful: regenerating produced assets byte-identical
  to the committed ones.
- **`LEVEL_AUTHORING.md`** carries the six composition rules, moved out of `LevelDefinition.kt`
  before that file became unreachable. Dozens of Kotlin comments still point at "the composition
  rules at the top of the campaign block" — they mean that file now.
- **`LevelDefinitionSO.designNotes`** ([TextArea]) is where per-level reasoning goes. The Kotlin
  carried a great deal of it in comments and the migration would otherwise have stranded all of it.

### The rules are CHECKED now, not just written down

`LevelComposition.Report` (headless) and the level inspector run the same six checks. Both measure
by **building the level and reading the same half-widths the camera uses** — re-deriving spans
from anchors would create a second source of truth about framing, and would be wrong anyway
because a group's real width comes from Formation, not its anchor.

Warnings are advisory: a level may bend a rule for a reason, and that reason belongs in its
`designNotes`. An author who cannot ship a deliberate exception stops running the check at all.
Errors are the locked 7-30 roster scale.

### It immediately found real faults in shipped levels

```
L1 Patrol Encounter  warn  rule 5: 3/9 garrisoned (33%)
L3 Watchpost Ridge   warn  rules 4/6: separation 13.3 (14-18)
L5 Tower Assault     warn  rules 4/6: separation 11.3 (14-18)
L6 Ash Boulevard     warn  rules 4/6: separation 18.1; rule 5: 7/16 garrisoned (44%)
L7 Oceanfront        ERROR player roster 6, enemy roster 6 — the LOCK is 7-30 per side
```

**L7 violates a lock**, verified independently against the asset rather than taken from the tool.
None of these were fixed here: retuning levels is Phase D work, done against the beat chart, and
Phase A's deliverable is the tooling. They are the first real evidence that the campaign needs
that pass.

One limitation to know: "dominant structure" is resolved as the WIDEST enemy structure
(`hitWidth`, falling back to `size`). For a tall-narrow tower that is a weak proxy, and on L5 it
picked the CommandBunker over the tower the level is named for.

## Campaign split from the test rigs — DONE 2026-08-06

Phase B of `_plans/archive/TIER0_PLAN.md`. `PRODUCT_DIRECTION.md` pillar 10: "test rigs are not the
campaign."

Done with ONE array, not two. `SpikeSceneBattle` now orders **campaign-then-rigs** (`OrderBy
isTestLevel, ThenBy levelNumber`), so the campaign block leads and is contiguous, and the
player-facing path is simply `index < campaignCount`. A second serialized array would have meant
two indexing schemes and a conversion between them at every call site.

- **The ◀ ▶ stepper walks the campaign only.** A `RIGS` button unlocks the test block.
  Deliberately a runtime toggle and NOT `Debug.isDebugBuild`: the rigs have to stay reachable in a
  RELEASE build, because that is the only build performance may be measured on and sweeping them
  from adb is how missing geometry gets found. Locking them while standing on one snaps back to
  the last campaign level.
- **NEXT on the victory card is bounded by the campaign**, so winning the last campaign level no
  longer offers to walk the player into the unit parade.
- **The nav readout counts within the reachable block** — "L7 (7/7)", not 7 of 24 — and marks a
  rig with `RIG`.

### The renumbering chore is retired

`PortSelfTest` asserted `levelNumber == index + 1` across all 24, which is what forced every test
rig to be renumbered whenever the campaign changed size. It now asserts contiguity **within the
campaign only**; a rig's number indexes nothing. Phase D changes the campaign's size by five or
more levels, so this had to come first.

That half of the check matters MORE than it used to: the orphan sweep is gone, so a stale level
asset can no longer be deleted for us, and this is the only thing that catches one rejoining the
campaign at its old number. A duplicate-id check was added alongside it — ids key the saved star
results, so a duplicate silently makes two levels share a best-star record.

### Confirmed on device

Release build on the Pixel 10 Pro XL: ten ▶ taps from L1 stop at L7 and stay there; `RIGS` then
reaches L8 (TEST — Tier Collapse); locking again snaps L8 back to L7; the readout reads L7 (7/7).

## Campaign to twelve levels — DONE 2026-08-06

Phase D of `_plans/archive/TIER0_PLAN.md`. **12 campaign levels + 17 rigs = 29.** Every level owes one
beat from `PRODUCT_DIRECTION.md`'s chart and says which in its `designNotes`. Two stages of six,
bosses on 6 and 12. **`LevelComposition.Report`: 12 levels, 0 warnings, 0 errors** — the five that
were breaking their own rules are fixed.

| L | Level | Biome | Beat |
|---|---|---|---|
| 1 | Patrol Encounter | Mountains | teach the drag |
| 2 | Garrison Post | Forest | structures matter |
| 3 | Watchpost Ridge | MountainsDusk | prioritise threats |
| 4 | Ash Boulevard | CityRuins | the charge |
| 5 | Tower Assault | Desert | elevation |
| 6 | **Ridge Bastion** | Mountains | **stage boss A** |
| 7 | Barracks Line | Winter | toughness |
| 8 | **Timberline Crossing** | Forest | combine |
| 9 | **Dusk Redoubt** | MountainsDusk | outnumbered |
| 10 | **Rubble Yard** | CityRuins | reinforcement race |
| 11 | Oceanfront | Ocean | elite exam |
| 12 | **The Citadel** | Desert | finale |

Bold are new. Ash Boulevard moved from 6 to 4 (panic belongs early), Barracks Line from 4 to 7,
Oceanfront from 7 to 11.

**Campaign assets are named for their IDENTITY now** — `AshBoulevard.asset`, not `Level4.asset`.
The order moves as the funnel is tuned and a filename disagreeing with `levelNumber` is a trap.

### Two systems were dead and are now wired

This phase found the same shape of bug Phase C did, twice.

- **WIND IS COSMETIC.** `TrajectoryPhysics` applies `windAccelZ` to Z; the collision test is
  X/Y only (`SegmentDistanceSq(prevX, prevY, ...)`) and Z appears in `CollisionSystem` solely to
  place the detonation visual. Wind cannot change what a shot hits. It has also never been set on
  a level in either build. Beats 7 and 8 were built on wind and were re-cut onto real variables —
  toughness (HeavyRifleman at 64 hp, forcing concentration) and a combine of elevation + melee.
  **Do not author a wind level until wind does something.** Making it real is a PHYSICS change and
  needs an ask.
- **BOSS PHASES AND REINFORCEMENT WAVES WERE NEVER FIRED.** `EventSystems` has decided both
  correctly since the port and nothing ever called it: `bossPhases` and `reinforcementWaves` were
  read only by `BattleRunner`, and only to size the pools. Now wired into `BattleTick` step 7b,
  spawning through `LevelBuilder.BuildUnits` so an arrival is built exactly like the opening
  roster. Confirmed on device — L10 turn 4: `EVENT: Their heavies are here! (enemies 6 -> 10)`.

### What Auto still cannot test

`Auto` cannot trigger a BOSS PHASE. It targets the nearest enemy unit, so on Ridge Bastion it
clears everything else before the keep's garrison and the level resolves as a victory first. The
boss path is covered end-to-end by `PortSelfTest` instead — it razes the trigger structure, runs a
real `BattleTick.Step`, and asserts the phase fires once, spawns, announces, and does not re-fire.
**Seeing the Sovereign on a real device still needs an aimed drag at the keep.**

A trap that check paid for immediately: `LevelBuilder.BuildInitialState` does NOT set `Phase`
(`BattleRunner.LoadLevel` does, right after), so a state built for a test takes `Step`'s
cosmetic-only early return and no event fires. Set `Phase = Playing` on any hand-built state.

### One-off authoring script, deliberately deleted

The 12 levels were written by `CampaignAuthor.cs`, run once and then removed — creating five
levels' worth of GUID references by hand is not viable, but a script that can rewrite every level
wholesale is exactly the hazard `LegacyKotlinImport` was guarded against. The assets are the
artifact. `CampaignAudit.Dump` is kept: it is read-only and prints what each level actually is.

## Enemy turn juice — DONE 2026-08-06

Phase F of `_plans/archive/TIER0_PLAN.md`, `PRODUCT_DIRECTION.md` 0.6. Phase D made the events FIRE; this
makes them SAY something. `telegraphText` and `announcement` had been imported and displayed
nowhere since the port.

**Two channels, and the difference between them is the whole of pillar 7.**

- The **banner** is a flash — something just happened ("Their heavies are here!"), or the turn just
  changed.
- The **telegraph strip** is a standing condition — something is ABOUT to happen, and it stays up
  for the entire turn being warned about. `GameState.TelegraphText`, recomputed from scratch every
  tick rather than latched, so it clears itself the moment the wave lands. A warning with a fade
  timer has blindsided anyone who looked away, which is the thing the pillar exists to prevent.

**The turn handover names the threat, not the phase.** `ThreatLine` reports the ADVANCE first —
"3 closing on your line" — because a marching group reaching the line is the only thing that can
lose the level this turn, and counting rifles does not matter if it arrives. It falls back to
"Enemy turn". An event outranks it: both land on the same frame when a wave arrives on the
handover, and two competing banners tell the player nothing.

Confirmed on device, L10: the red strip reads "Heavy support inbound — 1 turn" through the whole
of turn 3, the wave lands on turn 4, the strip clears itself.

The strip started at y-104 and ran straight through the CAM / RIGS / stepper cluster. Harmless for
input (it takes no raycasts) but it read as a broken layout; it sits below the banner now.

## Loadout — DONE 2026-08-06

Phase E, `PRODUCT_DIRECTION.md` 0.4b: "something to buy that changes the next battle".

**SLOTS AND POINTS ARE SEPARATE, and that is the whole design.**

- **Slots** = the number of ground troops the level was AUTHORED with, read off the level. Fixed,
  because composition rule 1 measures the PLAYER LINE'S WIDTH and the aiming camera is framed on
  it. A loadout that could field more bodies than the level was drawn for would zoom the camera
  out, and nothing else in the layout can compensate.
- **Points** = `deployBudget`, and they buy QUALITY. Eight slots and eight points is eight
  riflemen; eight slots and sixteen points is four heavies and four riflemen, or two snipers and
  six riflemen.

So the squad never gets WIDER as the campaign goes on — it gets BETTER. Every authored level stays
framed exactly as it was measured, the locked 7-30 scale holds by construction, and the budgets
authored in Phase D turned out to need no change at all.

`Loadout.ToPlayerGroups` TILES the picks across the authored width, so a three-type squad is
exactly as wide as a one-type squad. Anchoring every pick at the same x would stack them; giving
each a fixed spacing would make rule 1 fail on the player's choices rather than on the level.

**The default is the old behaviour.** `Loadout.Default` fills every slot with the cheapest
unlocked unit, which reproduces what each level fielded before the picker existed — pillar 8,
"default paths cost nothing". BEGIN is live the moment the panel opens.

Garrisoned player groups are NEVER touched: the tank crew is level geometry standing on a
structure at a fixed anchor, not a squad pick.

### Checks that matter

`PortSelfTest` asserts, for EVERY campaign level: the default loadout is legal and fills every
slot; the default squad is no wider than the authored line, measured through the real
`LevelBuilder` on the same `PlayerCamHalfWidth` `LevelComposition` reads; an all-dearest-unit
squad also fits that frame; and `deployBudget` covers at least one cheap body per slot. Plus the
edges — an empty loadout is illegal, overfilling slots is illegal even when points allow it,
under-filling is legal, and a locked unit cannot be fielded.

### Two traps, both the same one

IMGUI draws AFTER the canvas. The loadout panel is modal, so `OnGUI` returns early while it is
open — otherwise the HUD and the ◀ ▶ stepper sit on top of the panel and stay TAPPABLE, and a
player could change level out from under the squad they were choosing. Identical to the
RESTART / NEXT problem in Phase C. The in-battle furniture (coin pill, banners) is also hidden
while the picker is up: it belongs to a battle that has not started, and it ghosted through the
panel's 97% fill.

### NOT DONE: the balance audit — SUPERSEDED, and the audit is now COMPLETE

**Both halves have since been built and run** (2026-08-06 arithmetic, 2026-08-07 device), and the
whole audit was CLOSED on 2026-08-07 by Rob playing the campaign and reporting the levels feel
fine. It found L7 unwinnable, made reach a checked rule, found the 288 siege ceiling and the tank
shell's overshoot. See the sections at the end of this file. The original text follows, and its
"has not been run" is no longer true.

`PRODUCT_DIRECTION.md` asks that every shipped level be clearable at stock tier by a competent
shooter, and calls a level that breaks under a LEGAL loadout a product bug. **That audit has not
been run** — it needs real drags per level, and `Auto` cannot measure difficulty (it never misses
and is structure-blind). The framing half is enforced by the checks above; the difficulty half is
still owed. It was deferred historically too; it is now the last open item in Tier 0.

## Ruins, instead of blocks everywhere — 2026-08-06

Rob: "I want to see better ruins when a structure is destroyed, not just the structure disappears
and then we have all of these blocks everywhere." Both halves of that were real, and they had
DIFFERENT causes.

**1. The building vanished.** Destruction removed the structure and threw TEN CUBES at random
angles with `Ttl = float.MaxValue`. Nothing marked where the building had stood.

Now a RUIN is PLACED rather than launched: 3-6 wide flat slabs lying inside the structure's own
footprint, already `Asleep`, persisting for the level. Sizes descend from the centre outward so it
reads as a collapsed mound rather than a row of equal lumps, and rotations are within ±11° —
masonry settles askew, it does not stand on end. `DebrisPiece.Squash` (0.3 for a slab, 1 for a
tumbling chunk) is what makes it lie FLAT: at this camera's ~6° the height of a lump is most of
what you can see of it, so a cube reads as a crate and a slab reads as fallen masonry. The
collapse still throws chunks, but they are transient now.

**2. "Blocks everywhere" was mostly NOT the destruction.** It was the SHED pieces — the chunks a
structure throws off as it takes damage, which also carried `DebrisRubbleTtl`. A structure sheds
up to a dozen chunk groups over its life, every one of them permanent, so they piled up across the
field as loose blocks with nothing to do with where the building stood. They are transient now.
The lasting record of DAMAGE is the structure's own missing geometry; the lasting record of
DESTRUCTION is the ruin.

**3. They were also nearly black.** The debris prefab used `structEnemyAccent` (0.30/0.24/0.18),
which at debris size on open ground reads as scorch rather than stone. It uses the structure BODY
tone now (0.52/0.44/0.34), so rubble reads as the building it came from.

### CONFIRMED ON DEVICE 2026-08-06

L1's outpost demolished with real aimed drags. Once its HP reached 0 the HUD's Structure line
cleared and the site holds a LOW, FLAT, CLUSTERED mound of slabs where the building stood — and
the field is otherwise clean, with none of the scattered blocks the original screenshot showed.

The diagnosis was confirmed first, and it is what made the fix the right one: that screenshot had
~14 near-black blocks strewn far wider than the structure's footprint, which identified the SHED
pieces rather than the destruction burst as the main culprit.

**A device-safety note.** Relaunching found the NOTIFICATION SHADE holding focus over the game
(`mCurrentFocus=NotificationShade` while `mFocusedApp` was still the game). Taps in that state are
exactly how earlier sessions ended up driving personal apps. `adb shell cmd statusbar collapse`
clears it cleanly — no synthesized input, and no KEYCODE_BACK, which is the thing to avoid.

To finish the check: L1, BEGIN, then repeat `input swipe 540 1150 204 1486 400`. That drag is
derived, not guessed — `ppu = 1080 * 0.0208 = 22.46 px` per drag-unit and `DragSpeedScale = 0.384`,
so L1's 16.5-unit tank→outpost separation needs `v = sqrt(16.5 * 4) = 8.12`, a 475 px drag, 336 px
on each axis at 45°, downward to launch upward. It lands on target: structure HP fell 90 → 50 → 28
over successive volleys. Budget ~10 volleys, since garrison units absorb hits first.

## The balance audit, arithmetic half — DONE 2026-08-06

`BalanceAudit.Report` (`Assets/Editor/BalanceAudit.cs`), the headless half of the last item Tier 0
owed. It cannot measure difficulty — that needs a human drag — but it settles the half that is
arithmetic and therefore needs no device at all, across BOTH ends of the legal loadout space
(stock, and the dearest legal squad), because the product rule is written over LEGAL loadouts.

**It found a shipped level that could not be won.** L7 Barracks Line garrisoned 3 grenadiers on
the CommsTower at x 8.6, 4.5 units above the muzzle: **100% power from the front rank, 108% from
the back**, and **101% — literally unwinnable — under a legal all-RocketTrooper squad**, which
tiles the line slightly further back. Verified by hand against the asset before anything was
changed: v = 8.96 against a 9.0 cap.

**All six composition rules passed it.** That is the finding under the finding. Rules 1-6 measure
FRAMING and HORIZONTAL separation; the power budget is spent on HEIGHT, and nothing measured it.

**And `LEVEL_AUTHORING.md` rule 4 was actively lying.** It described 14-18 separation as "well
inside the ~49-unit max range". The real figure is `AimSystem.MaxRange45` = v²/g = 81/4 =
**20.25 flat**, so the authored separation spends 70-89% of the whole envelope before a single
unit is lifted off the ground. That sentence is what licensed the level. It is corrected.

### What was changed

- **L7 fixed.** The grenadiers came off the mast onto a `TowerPlatform` at x 7.8 — reach 100% ->
  86%. The mast STAYS at 8.6 as the level's silhouette and identity, which keeps the enemy cluster
  depth; three enemy structures is still legal (one dominant + two supports). Moving them to the
  GROUND was tried first and rejected: it dropped the level to 45% garrisoned and broke rule 5.
  The beat is untouched — beat 7 is TOUGHNESS, carried by the 5 heavy riflemen on the barracks.
- **Reach is now RULE 7**, checked. Implemented once in `BalanceAudit.ReachRule` and CALLED by
  `LevelComposition`, so the audit and the level inspector cannot disagree about whether a level
  is playable. Front rank over 100% is an ERROR; back rank over 100%, or front over 92%, is a
  WARNING.
- **L3 and L5 carry accepted rule-7 warnings**, with the reason written into their `designNotes`,
  which is where a bent rule belongs. Both beats are explicitly about height ("fight upward", "the
  furthest target"), so their back rank — the tank crew — genuinely cannot reach and pulling the
  garrison in would pull the level's teeth.

### The three things it measures, and why each is honest

- **REACH.** Victory is every enemy UNIT dead, so an unreachable enemy is unwinnable at any skill
  level, forever. Uses the real envelope `v² = g(dy + √(dx²+dy²))`, NOT `MaxRange45` — height
  costs range twice, once for the climb and once for the longer slant, and using the flat figure
  would call a fortress-roof garrison reachable when it is not.
- **THE VOLLEY RACE at equal accuracy.** Both sides do fixed damage into a fixed HP pool, so the
  clean-volley count is exact and only accuracy is unknown; holding it EQUAL removes it. Warns
  past **2x**, not at break-even — the player also has the tank shell and per-turn attrition. At
  1.0 it warned on 21 of 24 squads, which is an instrument that discriminates nothing.
- **THE MELEE CLOCK.** `advancePerTurn` is authored, so turns-to-contact is known.

**Two ways to win, and the cheaper one is what the level costs.** A garrisoned unit dies with its
structure, so on a level that garrisons most of its roster — which rule 5 REQUIRES — razing can
clear the field for a fraction of the bodies' HP. Counting only the shoot route rated an
all-RocketTrooper squad at 20+ volleys and therefore hopeless, while that unit's entire design is
a 6x structure multiplier. With both routes, L12 The Citadel clears in 4.6 volleys by razing
against 21.6 by shooting: **the anti-structure unit is measurably the right pick on the fortress
level**, which is the roster working as designed.

### The device half — SINCE RUN, and CLOSED

Run the same day; the results are in the sections below. The ranking here is what chose which
levels to drag, and it was sound: L9 and L12, the two worst, were both unclearable at stock, and
L4, the least-flagged, was not. **Closed 2026-08-07 by Rob playing the campaign after the tank
shell was fixed and reporting the levels feel fine** — better evidence than the adb harness, which
has no aim preview and could never finish a mop-up phase.

**A systemic observation for whoever tunes difficulty next:** every campaign level needs 81-100%
power at its deepest enemy. The whole game lives in the top fifth of the aim range, so there is
almost no headroom anywhere and every level's aim demands roughly the same drag. Widening that
band means raising `AimSystem.MaxAimMagnitude`, which is a physics change touching all 29 levels
and needs an explicit ask — it was offered on 2026-08-06 and NOT taken.

---

# Closed sections from 2026-08-07 → 2026-08-11, split out 2026-08-25

Moved out of `HANDOVER.md` to stop it growing without bound — it had passed 4000 lines. These are
CLOSED: each one is a system that was built, or a bug that was fixed, and none of them is a
statement about current behaviour. **One of them has since been OVERTAKEN and says so in place**
— the device balance audit, which was measured when the tank carried three shells.

The live rules these sections paid for did not move with them: they live in `HANDOVER.md`'s
"Traps already paid for — do not rediscover these", which is the file's reference half and was
deliberately left behind.

## Enemy factions — Tier 2.1, built 2026-08-11

**Two stages, two armies**: Redguard (Valley Front) is the existing enemy red, UNCHANGED, and
Ironclad Legion (Enemy Stronghold) is steel blue-grey. The full reasoning — what a faction may
touch, why the data lives in `Assets/GameData/Factions` rather than in the UI layer as the Kotlin
had it, and why only two — is in `DYNAMISM_DESIGN.md`'s "Phase D1 in UNITY" section. What belongs
here is the traps.

**IT IS A POOL RESET, NOT A PAINT.** Pools are built once and survive a level switch, so the enemy
is repainted in `BattleRunner.ApplyFaction` on every `LoadLevel`, beside the scorch re-material and
`TintShadows`. The failure mode is the one this repo already paid for in scorch marks, shadows and
structure chunks: a recycled slot wearing the PREVIOUS level's colour. **A single paint cannot show
it — the device run is L1 red → L7 blue → L1 red again, and the third leg is the evidence.**

**Renderers are classified against the two build-time MATERIALS, not against the `skin*`/`trim*`/
`accent*` mesh-name prefixes.** That convention belongs to `RiggedUnits.Tone` in the art pipeline,
and a second copy of it at the render end is a copy that can disagree with the first. The
classification runs once with the pools; the per-switch cost is a list walk.

**`FactionPaint.Recolour` CLONES the material.** Tinting the source in place edits
`Assets/Materials/EnemyUniform.mat` ON DISK — the negative run proved it, leaving both .mat assets
modified in `git status` — and every faction then shares whichever colour was applied last, in the
editor and in every build after it.

**What is asserted and what is NOT.** The seven new checks cover the lookup (12/12 campaign levels
field a faction, 0/17 rigs do), the palettes being visibly different armies, and the repaint itself
on the SHIPPED rifleman prefab through three successive paints. **They do not cover the call
site** — `PortSelfTest` does not drive `MonoBehaviour` frame callbacks, so "ApplyFaction is called
on every LoadLevel" is device-verified only. Same considered gap as the loadout NRE guard.

**The distinctness check was WRONG when written, and it is the lesson of the session.** It began as
a luma-weighted rgb distance, which weights blue at 0.11; it scored steel blue-grey at 0.082 from
the player's olive green — under its own threshold — and indicted a palette the Kotlin build
shipped and played fine. **The metric, not the palette, was the thing that was three hours old.**
Equal-brightness opposite hues are trivially told apart, and hue is the axis the whole feature
works in. It is an opponent-colour distance now and is deliberately only a coarse floor. This is
the same family as the "ASCII only" glyph check that flagged 23 strings a device screenshot then
showed rendering perfectly: **be suspicious when a brand-new check indicts long-standing content.**

**All three negative runs are recorded**, per the standing rule:

```
[FAIL] the shared EnemyUniform/EnemyGear ASSETS come out of it unchanged
       (RGBA(0.270, 0.330, 0.420) was RGBA(0.520, 0.200, 0.180))   <- Recolour tinting in place
[FAIL] the rifleman splits into uniform / gear / neither (5 / 6 / 0 renderers)
                                                          <- skin+trim swept into the repaint
[FAIL] every campaign level fields a faction and no rig does (12/12 campaign, 17/17 rigs)
                                                          <- lookup ignoring stage membership
```

**A scene rebuild was required** — three new `[SerializeField]`s on `BattleRunner` (`stages`, and
the two enemy side-materials the classification keys on). The materials must be the SAME asset
references the enemy prefabs were toned with; reference equality is the whole mechanism.

## Player camo — Tier 2.4, built 2026-08-11

**The NINTH dead system** (factions, the other half of this session, were ABSENT rather than
dead — see "assume NOTHING is wired"). `CosmeticSet`, `ProgressStore`'s cosmetic block and
`EconomyStore.PurchaseCosmetic` were all ported and reached by nothing. Four sets now: Olive Drab
free, Desert Tan 300c, Urban Grey 350c, Arctic White 400c, bought and worn in a strip below the
consumables. Design detail is in `DYNAMISM_DESIGN.md`'s "Phase D4 in UNITY"; the traps are here.

**It rides the faction repaint, pointed at the other army.** Same `FactionPaint` classify-once /
apply-on-switch machinery, so the same pool-reset reasoning applies unchanged.

**Olive stores NO colour and that is deliberate**: selecting it repaints back to the build-time
material ASSETS. A default you can return to has to be a real destination — a "paint it once"
implementation has nowhere to go back to.

**RIGS lends the wardrobe** through `Cosmetics.TestOverride`, session-only, writing nothing. Switch
RIGS off and the borrowed camo is withdrawn IN THE BATTLE YOU ARE STANDING IN — the repaint is
otherwise only read by `LoadLevel`, which is the same round trip the consumable supply had to fix.

**THE VANITY CHECK WAS UNFALSIFIABLE TWICE OVER, and this is the entry worth reading.** It fires
the same seeded volley under two camo sets and demands identical damage. Against a deliberately
broken build where the camo really did buff damage 50%, it passed — twice, for two different
reasons:

1. **The volley never landed.** Both runs did zero damage, and zero equals zero. It now asserts
   the enemy's HP actually FELL as part of its own condition.
2. **The camo was never worn.** `SelectedCosmetic` validates on read, so selecting a set the
   player does not own silently returns Olive — the check was comparing Olive with Olive. It now
   unlocks the set first (and locks it again afterwards, via a new `ProgressStore.LockCosmetic`
   that exists for exactly this) and READS BACK what the store holds rather than trusting what it
   asked for.

Only after both fixes did it read `enemy 280/276` against the broken build. **Two independent
reasons a check could not fail, in one check, in one session** — ask what state the failure needs
and then verify you are actually in it.

**The UI's tap path needed its own spy.** `PortSelfTest` tests `Cosmetics.TestOverride`, not
`TapCamo`, so a test supply that quietly UNLOCKED the set for real passed every check.
`BattleUIPreview` now taps the tile and asks the STORE whether anything moved — it caught the
breakage immediately (`unlocked 0->1, worn Olive->Arctic`). That preview is the only harness that
drives real MonoBehaviour UI.

**Two palettes now compete for the same colour space.** Urban Grey is boxed in on three sides —
Ironclad's steel, the player's own Olive (measured 0.159, barely over the floor) and Desert Tan.
Before adding a fifth camo or a third faction, run the distinctness checks first and expect to
have to move something.

## Tier 1.3 — the consumables, built 2026-08-10

Found fully ported and reached by nothing (the SIXTH such system), and now live: **Airstrike 250c,
Early Reinforcements 200c, Trauma Kit 150c, Smoke Screen 200c** — bought and equipped on the
loadout screen at the locked cap of TWO, triggered from the battle HUD on the player's own Aiming
phase. `Consumables` is the catalog, `ConsumableActions` holds the effects, and each is confirmed
on a device by what it DID, not by the fact a button existed.

### Overwatch Flare is NOT built, and that is the most important line in this section

It halves the enemy's next advance budget. **Nothing in this port ever advances.**
`UnitEntity.AdvancePerTurn` is imported and read only to count advancers for a threat line;
`AdvanceRemaining` is written NOWHERE; there is no enemy march step; and `SkirmishEntity` — the
melee an arrival resolves into — is defined, counted in `IsVisuallyIdle`, and never created.
**Advancing squads and melee are an EIGHTH dead system**, and a large one: they are what
`PROGRESSION_DESIGN`'s whole survival/defend archetype is made of.

A 200-coin button that changes nothing teaches the player that coins are decorative, which is
worse than having no button. That is the same call already made about wind. `PortSelfTest` asserts
BOTH halves — Overwatch is not sold, AND no enemy ever banks an advance — so the day advancing
squads land, the check goes red and adding one catalog entry is the fix.

### Confirmed on device, each by its output

```
Trauma Kit    [Consumable] TraumaKit: hp 304 -> 320      (clamped; front rank only)
Airstrike     [Consumable] Airstrike armed=True -> Airstrike fired
              [Battle] volley: 12 rounds   <- the volley alone is 11
              Garrison Post 135 -> 87, round visibly falling NOSE-DOWN among the arcs
Reinforce     [Consumable] reinforcements: 10 -> 13 player units, formed up right of the line
Smoke         [Consumable] SmokeScreen armed=True (button reads `Smoke / ARMED`, still THERE)
              [Consumable] Smoke Screen spent on the enemy volley
```

Bought with coins earned in play, because the release build is not debuggable and `run-as` cannot
seed PlayerPrefs — which turned out to be a better test than seeding would have been: the purchase,
the balance, the affordability tint and the carry cap were all exercised for real.

### RESOLVED: the Airstrike now has an aircraft, and it flies BEFORE the volley

Rob asked, the day Tier 1.3 shipped, whether anything actually flies across the screen. It did not:
a single grenade popped into existence in mid-air and fell. **A 30 fps device capture then found
something worse than ugly — the bomb was detonating OFF-SCREEN**, ~0.85s before the volley-follow
camera finished panning to the target. Nobody had ever seen it.

**A control run corrected two earlier write-ups in this file.** The same drag with nothing armed
shows the identical "round falling nose-down among the arcs" that two sessions had taken for the
airstrike. **That is the TANK SHELL**, which fires on every volley for free. Take the control shot.

**The fix is a sequencing change, not an art change**, and Rob directed it: *"plane should fly first
before the player volley."* A straight-wing attack aircraft (`Assets/Models/attack_plane.glb`, built
by `build_attack_plane.py`) crosses from the player's side in a new `TurnPhase.AirstrikeRun`,
releases the bomb, and exits; the infantry volley launches the moment that bomb lands. With no
rounds in the air yet there is nothing for the camera to chase, so the pass owns the frame. The beat
costs **1.10s** and no damage number moved.

**THREE THINGS THIS COST, all of which a green test suite could not see:**

- **The model had to be BANKED ~45 degrees, and the SIGN matters as much as the angle.** The
  wingspan runs along DEPTH and `BattleCamera` looks UP ~14 degrees, so an unbanked aircraft
  projects its span vertically and reads as a cross-shaped blob. Rolled the WRONG way it shows the
  camera the bare top of the wing — the surface the builder deliberately leaves undetailed, because
  the player only ever sees the underside. It is `-45` with no yaw. Free: a runtime rotation.
- **IT SHIPPED FLYING BACKWARDS, and only Rob looking at it caught that.** The facing was reasoned
  out from the axis conventions — "the GLB is authored nose toward +X" plus "GameSpace negates X" —
  which produced a 180 degree yaw, a cannon trailing behind the tail, and a green test suite. The
  import chain in fact lands the authored nose pointing screen-right already. **Do not re-derive
  the facing from the conventions; ask `PlanePreview.Orientation`**, which renders all four
  yaw/bank combinations against a rank of soldiers. Same family as every other "assert the artefact,
  not the note about it" entry in this file, and the second time TODAY that reasoning about this
  aircraft's axes was wrong.
- **It flies at y=9.5 with a PASS-BY SOUND** (`Assets/Audio/plane_passby.wav`). The source is an
  8.3s recording; it is cut to 3.0s starting at 2.01s so its PEAK — which sits at 3.30s in the
  original — lands as the aircraft crosses the drop point, 1.29s into the run. Play the whole file
  and the loudest part arrives over empty sky, seconds after the plane has gone. Height does NOT
  move the release or the impact: the drop lead is `PlaneSpeed * BombFallTime`, and neither is a
  function of height.
- **`PlanePreview` was rendering a different aircraft than the game** and both halves are fixed: it
  had a hardcoded camZ **11** against the run's real **14**, and defaulted to a yaw the game had
  stopped using. It now derives camZ from `CameraDirector.AirstrikeRunHalfWidth` and reads
  `BattleRunner.PlaneBank` / `PlaneScale` directly, so it cannot drift again. **Delete
  `Builds/plane` before reading a sheet from it** — stale frames from an earlier run are glob-matched
  alongside the new ones and produced one thoroughly misleading comparison.
- ~~**STILL OPEN — Rob wants MORE ROUNDS coming from the plane**~~ **DONE** (2026-08-10) — 14
  rounds over the same walk at half the damage each, and then STRETCHED INTO STREAKS, which is the
  half that actually made a difference. See "The strafing burst" below.
- **THE BOMB IS A BULLET, not a grenade** (2026-08-10). The grenade prefab is olive-lime at 0.16
  scale and was, in Rob's words, hard to see; the bullet draws as the bright unlit TRACER. It is
  told apart from the aircraft's own cannon fire by `IsAirstrike`, which the renderer scales 2.4x —
  the flag had been set since Tier 1.3 and read NOWHERE. The scale is assigned on EVERY round
  because the slots are POOLED, and exactly one round may carry the flag; both are asserted.
  **AND IT IS MULTIPLIED ONTO THE PREFAB'S OWN SCALE, NEVER ONTO `Vector3.one`.** Every projectile
  prefab is authored at its own size — bullet 0.22, grenade 0.16, rocket 0.30, shell 0.34 — so the
  first version, which reset unflagged rounds to `Vector3.one`, drew EVERY ROUND IN THE GAME at
  raw GLB size, about 4.5x too big. Rob caught it in one look. **No test covers a per-frame
  transform write clobbering an authored value**, and the device is the only instrument that sees
  it: check an ORDINARY volley after touching this path, not just the case being added.
- **IT STRAFES, with REAL rounds** (added on Rob's ask, 2026-08-10). Seven cannon rounds at 4
  damage, walked along the ground into the bomb's own impact point. The earlier decision NOT to add
  gunfire is reversed and its reasoning survives: refusing a cue that does NOTHING was right, so
  these do something. **The Airstrike is stronger for it — 24 damage becomes 52** — a deliberate
  correction for the dearest item in the shop, but a balance change; `StrafeDamage` is the knob.
  **The first version was mechanically perfect and INVISIBLE**: rounds inheriting the aircraft's
  speed and dropping from 9.5 units arrive near-vertically at ~31 u/s and vanish in a few frames.
  Gunfire rakes FORWARD — each round is now solved onto its own point of the walk, giving it 10 u/s
  horizontally so it outruns the aircraft and draws a streak.
- **It was too big at 1.0 and is rendered at 0.85** (`BattleRunner.PlaneScale`), Rob's call after
  seeing it pass. Judged in the preview beside a rank of soldiers: 0.70 starts reading as distant
  scenery rather than as the thing you just paid 250 coins for.
- **The run inherited the AIMING framing and clipped the aircraft off the top of the frame.**
  `TurnPhase.AirstrikeRun` fell through `PhaseHalfWidth`'s `default:` to the tightest camera in the
  game (camZ 9.3). Fixed with an explicit case and a floor, `AirstrikeRunHalfWidth` -> camZ 14.
- **The aircraft FROZE at handover and hung in the sky for the rest of the battle.** Its motion sat
  inside the run's own step, which stops being called the instant the phase changes — and this
  aircraft deliberately OUTLIVES its phase, exiting over the top of the volley. It is the same rule
  as "anything that decays must decay on EVERY tick path". Motion moved to the physics section and
  the despawn point is carried on the entity, so it depends on nothing the phase owns.

### The strafing burst — doubled, and that ALONE CHANGED NOTHING. 2026-08-10

**"Still want to see more rounds coming from the plane."** The count went **`StrafeRounds` 7 -> 14
with `StrafeDamage` 4 -> 2** — twice the rounds over the same 4-unit walk, ~25 rounds/sec at
`PlaneSpeed 7`, with the burst's contribution held at 28 so the Airstrike's total stays 52.

**Rob then looked at the real thing and reported NO VISIBLE DIFFERENCE.** That verdict is the most
useful thing in this section, so it is recorded before the fix:

> "hmm i don't really see a difference — looks like only one."

**The count was never the bottleneck, and the device capture that "confirmed" it was measuring the
wrong thing.** The capture showed eight tracers in the air at once against seven with gaps, which is
true, and irrelevant: a **0.22-scale bullet travelling ~25 u/s covers a fifth of the gap it opens
between frames**, so seven of them and fourteen of them both draw a faint DOTTED CHAIN. A dot is the
same shape whether it is moving or not. This is "assert the OUTPUT, not the input" wearing a new
costume and it fooled a device screenshot: I asserted the round COUNT — an input — and read the
frames for confirmation of it rather than for what the burst LOOKED like.

**The fix is the round's SHAPE.** `ProjectileEntity.IsStrafe` marks the aircraft's cannon fire, and
the renderer stretches those rounds **4.5x along their own flight and 0.7x across it**
(`BattleRunner.StrafeRoundStretch` / `StrafeRoundWidth`), turning each into a tracer STREAK that
bridges most of the gap to the next frame. It costs nothing — the round is already rotated onto its
velocity, so local X is the direction of travel. **A bigger dot was the obvious change and is the
wrong one**: what fails to read is the shape, not the area, and the bomb already owns "big round
dot" (`IsAirstrike`, 2.4x). Three shapes now come out of one pooled prefab and the two flags are
the whole distinction, so `PortSelfTest` asserts they are MUTUALLY EXCLUSIVE.

**Density was still the only count lever with room.** The first round is fired
`StrafeLength + StrafeLead` = **8 units** behind the target and the aircraft spawns
`PlaneRunHalfLength` = **9** back — one unit of headroom — so a longer walk or lead needs the spawn
moved, which lengthens the beat. A shorter `StrafeFallTime` would raise the cadence and must NOT be
used: 0.40s is already short, and the first version proved short flights are what made these rounds
invisible.

**The damage budget was held deliberately.** More rounds at `StrafeDamage 4` would have been a
straight buff to an item that had already gone 24 -> 52 the same day, smuggled in under a
presentation ask. Count is presentation; the total is what the campaign feels, and `BalanceAudit`
does not know about consumables at all. The guarding check asserts ABSOLUTES (`>= 12` rounds, total
in `[24, 32]`) because the existing one asserted `Count == StrafeRounds` and `Damage ==
StrafeDamage` — self-consistency with its own constants, green on the tap Rob rejected and green on
a silent doubling. Run against both: `(7 rounds)` red, `(56, held at 28)` red.

### The rake had to SPREAD, not just fire more. 2026-08-11

> "the strafe should spread further horizontally. right now it seems to be directed at one or spots
> that are close together. it's a strafe — as plane moves to the right, the rounds should also move
> that way. it's more of a burst at the moment."

**Third verdict on this burst, and the third time the wrong dimension had been turned up.** Count
(7 -> 14) did nothing visible; SHAPE (dots -> streaks) made the rounds legible; neither moved the
one thing that makes gunfire read as strafing, which is the impacts WALKING across the shot. Four
units of walk inside a ~10.2-unit frame is a third of the screen — a cluster, whatever is in it.

**`StrafeLength` 4 -> 6, paid for with `PlaneRunHalfLength` 9 -> 11.** The two are locked together
by one inequality, and it is worth keeping in mind before touching either:

```
PlaneRunHalfLength >= StrafeLength + StrafeLead + 1
```

The first round is fired `StrafeLength + StrafeLead` behind the target, so the aircraft has to
exist that far back. **The spare unit is not slack**: the firing loop fires every round whose point
the plane has already passed, so a spawn at or beyond the first firing point dumps several rounds
from ONE position in a single tick — a literal burst, which is the thing being fixed.

**6 is the frame's limit, not a taste call.** The walk ends on the bomb, so it can only grow
leftward, and the run's frame reaches `PlaneCameraBias + AirstrikeRunHalfWidth` = 6.6 units left of
the target at the half-width FLOOR. Past that the opening rounds land off-screen: more spread, less
visible strafe. 6 leaves 0.6 units of margin on the tightest level.

**The cost is beat length** — the run is `(PlaneRunHalfLength - PlaneSpeed * BombFallTime) /
PlaneSpeed + BombFallTime`, so +2 units is +0.29s, taking the beat ~1.15s -> ~1.44s. That is the
price of a 50% wider rake and there is no cheaper lever: `StrafeLead` cannot shrink much (it is
`lead / fall` = 10 u/s of forward speed, and below `PlaneSpeed 7` the rounds stop outrunning the
aircraft, which is what made them invisible in the first place), and `StrafeFallTime` must not
shorten for the same reason.

**One thing to LISTEN to, not measurable from here: the pass-by sound.** Its peak is cut to land as
the aircraft crosses the drop point, and the drop now happens 0.72s into the run rather than 0.44s.
Nothing in the build can check that — `screenrecord` captures no audio — so it wants a human ear.

### The rake had to CROSS the target, not stop on it. 2026-08-11

> "the airstrike should continue to fire until the plane reaches the right side. it's not hitting
> the structure."

**A probe found the cause immediately, and it was not the walk's length.** The walk ENDED on the
aim point and approached it from the left, so every round but the last landed SHORT of whatever the
player aimed at:

```
[Geom] target=10.92   walk = [4.92, 10.92]
[Geom] structure 'Outpost'  span=[6.00, 8.00]
[Geom] enemyXs=4.0,4.3,4.7,4.9,6.8,7.0,7.2,6.9,7.1
```

Aim at a building and the burst rakes the dirt in front of it and stops at the near wall. **The fix
is `StrafeOvershoot = 3` — the walk now crosses the bomb's own impact point** and carries on past
it, so the rake goes over the target rather than up to it. Rounds land on BOTH sides.

**3 is the frame's right-hand limit, and it is smaller than the left one, because the camera LEADS
the aircraft.** `PlaneCameraBias` puts the frame 6.6 units left of the target and only 3.6 right of
it — so the overshoot spends the short side, and 3 keeps the same 0.6 of margin the left end has.

**Keeping the overshoot UNDER `StrafeLead` is what kept this a two-constant change.** The last
round is fired when the aircraft is `StrafeLead` short of it, at `target + 3 - 4`, which is still
before the bomb lands and the phase ends. Push it past the lead and rounds want to fire AFTER
handover — and the firing loop lives inside the run's own step, which stops being called the
instant the phase changes. That is the trap the aircraft's own motion already paid for. **The
negative run at `StrafeOvershoot 7` shows it exactly: 21 of 28 rounds ever fired**, the rest
silently dropped on the floor with no error anywhere.

**Density held: `StrafeRounds` 14 -> 28 and `StrafeDamage` 2 -> 1.** The count has now been raised
twice for the same reason — to hold the SPACING near 0.33 units as the walk grew 4 -> 6 -> 9 — and
the damage halved each time so the burst's contribution stays 28 and the item's total stays 52.

**One thing the budget arithmetic does NOT capture, and it is worth knowing before tuning:** a wider
rake spreads the same nominal damage over more empty ground, so its EFFECTIVE damage falls even
though the total is unchanged. The burst is presentation that happens to hurt. If it ever needs to
hurt a FIXED amount, that is a different design and wants a different mechanism than a walk of
independent rounds.

### The volley and the pass now land TOGETHER. 2026-08-11

> "i wonder if we can sync the player projectile volley with the plane. right now it's a little
> awkward."

**The two halves used to be ADDED.** The aircraft made its whole pass, its bomb landed, and only
then did the volley launch. Measured across the power range before changing anything:

```
power   plane run -> impact   volley flight   TOTAL NOW   if synced
 40%          1.57s               1.47s         3.05s       1.57s
 65%          1.57s               2.24s         3.81s       2.24s
 86%          1.62s               2.91s         4.53s       2.91s
100%          2.43s               3.36s         5.79s       3.36s
```

An ordinary shot cost **4.53 seconds** from release to impact, a third of it spent watching an
aircraft with none of the player's own rounds in the air. That is the awkwardness, and the table is
why it was not a matter of shaving a constant.

**Whichever half takes LONGER to reach the target now starts first, and the other is delayed by the
difference**, so both land together and the beat costs `max(flight, run)` instead of their sum.
`GameState.AirstrikeSpawnDelay` and `PendingVolleyDelay` are the two halves of that one alignment
and **at most one is ever non-zero**. At any usable power the volley is the slower half, so in
practice the volley goes at the moment of release — the game feels responsive to the drag again —
and the aircraft is held back to catch up.

**The phase stays `AirstrikeRun` even though the volley is away.** That is deliberate and it is
what keeps the earlier fixes intact: the run's camera cuts to the strike and HOLDS, so the aircraft
still enters across the left edge and the player's rounds arc into the same held frame rather than
dragging the camera off after them. Hits land either way — **collision runs on the always-run path,
not inside a phase** — which is the fact that made this possible at all.

**Everything the aircraft does moved to the always-run path.** Motion was already there; the guns
went there when the rake started outliving the bomb; and the BOMB RELEASE went there now, because
an aircraft held back is routinely still short of its drop point long after the phase has moved on.
`AirstrikePlaneEntity.BombTargetX` carries the target, because the aim it came from is cleared the
instant the volley launches — which is now usually BEFORE the aircraft is even released.

**A held aircraft must not be drawn.** The entity exists from the moment of release so nothing has
to be recomputed when it is let go, but a stationary aeroplane parked at its spawn for a second and
a half is a worse artefact than the one the delay fixes. The renderer gates on the same value the
tick does.

**TWO THINGS THE DEVICE FOUND THAT NO CHECK DID**, both caused by the aircraft now being HELD:

- **The pass-by sound fired at the moment of release**, over empty sky, a second before the plane
  existed. It used to be the same instant; it is not any more. It now plays on the true->false edge
  of `AirstrikeSpawnDelay` — the moment the aircraft is actually let go — because the clip is cut
  so its peak lands as the plane crosses its drop point, and that offset is measured from the START
  OF THE RUN. Anchor it anywhere else and the peak is silently thrown away, which is exactly what
  the original 8.3s clip did.
- **The release log said `volley held` when the volley was already away.** Third false reading from
  that one line — `volley: 0 rounds`, then strafe tracers counted as volley rounds, now this. Each
  time the beat changed under it. It reports the three real cases now: held, away with an airstrike
  inbound, or a plain volley.

**The check measures IMPACT TIMES out of a real stepped flight**, not the arithmetic that schedules
them:

```
[ok  ] bomb at 3.08s, volley at 3.17s from release (0.08s apart)
[FAIL] bomb at 1.92s, volley at 5.08s from release (3.17s apart)   <- the old added beat
```

That 3.17s gap in the negative run is the awkwardness, in numbers.

### The rake belongs to the ENEMY, not to the volley. 2026-08-11 — the design change

> "the strafe is independent of the player unit volley. it should start from the left, strafe
> should cover the whole enemy position and its structures."

**This is the change that made the previous three unnecessary.** The burst had always been defined
relative to the player's landing point — walk to it, then walk 3 past it — so its ground moved with
every drag: aim short and it raked open dirt, aim long and it raked past the line. Every fix before
this one was tuning an offset from the wrong origin.

**The rake is now derived from the enemy position and carried on the aircraft.**
`BattleTick.StrafeSpan` takes the enemy units and the enemy STRUCTURE EDGES — edges, because an
outpost is 2 units wide and raking to its centre leaves half the building unhit — plus
`StrafeMargin` at each end. `AirstrikePlaneEntity.StrafeFromX/ToX` carry it, fixed at the moment the
aircraft is committed. Carried rather than recomputed because the run outlives its own phase, and
because the enemy set SHRINKS as the rake kills, which would walk the far end backwards mid-burst.

**The bomb is the only part of an airstrike that cares where you aimed.** That split is the whole
design and it is what `PortSelfTest` now asserts, by firing the item at two different aims and
demanding the identical ground — the one property no arrangement of aim-relative constants can
fake.

**Three things had to move with it:**

- **The SPAWN is derived, not a fixed offset.** The aircraft must exist `StrafeLead` before the
  rake's first firing point AND still be short of the release when it drops, so it spawns at
  whichever is further back. `PlaneRunHalfLength` is now a FLOOR, not the spawn. Consequence worth
  knowing: **the beat is no longer one fixed length across the campaign** — a wider enemy line
  costs a longer pass.
- **The GUNS moved to the always-run physics path**, beside the aircraft's motion, for exactly the
  reason that motion moved there. See below.
- **The CAMERA frames the rake AND the bomb**, which are now different places. The anchor is the
  midpoint of everything the pass must show; the half-width covers all three points and is still
  floored by `AirstrikeRunHalfWidth`, so it can only ever pull back.

### The guns had to leave the phase, and the check that proved it was itself broken first

The rake reaches past the bomb's impact whenever the player aims short of the enemy's far edge —
the ordinary case — so the last rounds are fired AFTER the phase has handed over to the volley.
While the firing loop lived in the run's own step those rounds were **never fired at all**: no
error, no log, just a burst that stopped early. Same family as the aircraft freezing in mid-air,
and the second time this beat has paid for it.

**The check written for it PASSED against the broken code.** It used the same synthetic aim as
everything else in that block — one landing PAST the enemy's far edge — where the rake finishes
before the bomb and a phase-bound loop drops nothing. Re-pointed at an aim landing SHORT, which is
the only state where the failure is reachable, it reads:

```
[ok  ] a shot aimed SHORT ... fires the whole burst (28/28) — impact 3.16 vs rake end 10.13
[FAIL] a shot aimed SHORT ... fires the whole burst (17/28)      <- guns confined to the phase
```

**Eleven of twenty-eight rounds, dropped in silence.** The check now asserts the aim IS short as
part of its own condition, so it cannot quietly stop testing this if the geometry moves. This is
the third time in two days that a check had to be put into the state where its failure was
reachable before it was worth anything — see the empty purse and the null CameraFollowX.

### The check that caught the burst outliving its own phase

A pre-existing check went red on this change and was RIGHT to: "the volley that follows the run is
the volley the player aimed" counted `!IsAirstrike`, which had quietly meant "the volley" only
because the burst always finished before handover. It does not any more — the last rounds land
after the bomb — so they were being counted as volley rounds. It now excludes `IsStrafe` as well.
Worth recording because the check noticed a real behavioural change before any device did.

### The check that guards it asserts the FRAME, and the FIRING POSITIONS

Written against `StrafeLength` it would have been green through all three verdicts. It asks instead
for an impact span of `>= 5.5` units with the first landing inside the frame's left edge — the
frame being the thing the spread is actually competing with.

**And it asserts where the rounds were FIRED FROM, which the landings cannot see.** Every round is
solved onto its own point, so a clumped burst still produces a perfect walk of landing points. The
negative run proves it: spawning the aircraft too far forward left the impacts spanning a flawless
`6.00` while the firing positions collapsed from `5.95` to `3.97`.

```
[FAIL] ... impacts span 4.00 units ...          <- the old 4-unit walk
[FAIL] ... fired from 3.97 units ... not one    <- the clump, invisible in the impacts
```

### The aircraft did not FLY IN — the camera swept past it. 2026-08-10

> "the plane should come from the left side of the screen. it seems to just appear in the
> middle/left middle of the shot."

**It was never the spawn point, and no spawn distance could have fixed it.** A probe against L1:

```
target=10.92  spawn=1.92
AIMING frame  centre=-7.54  half=2.05  left=-9.59   spawnInside=False
RUN    frame  centre= 9.42  half=5.10  left= 4.32   spawnInside=False
```

The aircraft spawns off-frame under BOTH framings. But the run BEGINS with the camera still over
the player's own line at **-7.54**, and it then springs **17 units right** at
`MarchEscortSmoothTime` while the plane sits at 1.92 doing 7 u/s. **The camera overtakes the
aircraft and arrives to find it already mid-frame.** A camera travelling the same direction faster
than the plane always will.

**So the run CUTS to its anchor and holds, and it is the only phase that does.** Everything else
keeps the one continuous spring, deliberately — "every phase change TELEPORTED the camera" is a bug
this project already fixed once. `CAMERA_ARCHITECTURE.md` is LOCKED, so **this exception was asked
for and granted, not assumed**. It also delivers what the phase always claimed ("a hold, not a
chase"): the aircraft now crosses a frame that is already still, entering across the left edge
~0.34s into the run.

**The check that shipped this bug had the answer in its own failure message.** It read "aircraft
spawned off-frame" and asserted only `spawn == target - PlaneRunHalfLength` — the message named a
property about the FRAME that nothing in the check ever looked at. That is a doc asserting itself
one level down, and it is why the bug reached a device. The replacement asserts the camera is AT
the run anchor and the spawn is outside its left edge.

**And the first version of that replacement was worthless, for this file's favourite reason.**
`fresh` has never ticked, so its `CameraFollowX` is NULL — the spring then begins AT the anchor,
travels nothing, and sweeps past nothing. **The check passed against the sweeping code it was
written to catch.** Seeding the camera onto the player line first — where a real battle leaves it —
is what gave it teeth, and the negative run then read:

```
[FAIL] the run CUTS to its own framing and the aircraft enters across the LEFT EDGE —
       camera -7.44 (anchor 9.42, edge 4.32), spawn 1.92
```

That is Rob's bug, in numbers. Same family as the empty-purse check and the `ReferenceEquals`
refusal test: **ask what STATE the failure needs to be reachable in, then put the check in it.**

### Both confirmed on device, L1, 2026-08-10 — with the control in the same capture

Fresh install, RIGS test supply, armed from the HUD, a real drag
(`input swipe 300 900 631 1231 600`), recorded at 60 fps:

```
[Consumable] Airstrike armed=True -> Airstrike fired (TEST supply)
[Battle] airstrike run, volley held at 86% / 45.0deg
[Battle] volley: 11 rounds, after the airstrike          <- 1.15s later, beat unchanged
```

On the frames: the camera CUTS to the strike, holds on an empty frame for ~0.3s, and the aircraft's
nose crosses the LEFT EDGE — the frame at 2.02s catches it half in. The cannon fire is now a line
of distinct elongated TRACERS running from the aircraft to the ground, with impacts kicking up
across the enemy rank; outpost 90 -> 82 and the burst visible on every frame of the pass.

**The control is the same capture's ordinary volley**, which is where the pooled-scale regression
would show: the eleven infantry rounds arrive at normal size and normal shape, so the stretch does
not leak into a recycled slot. That is asserted nowhere and can only be seen — the same class of
bug as the `Vector3.one` scale regression, and the reason the volley is now checked on every device
run that touches this path.

**And the release log was lying — TWICE, for different reasons.** First it reported
`volley: 0 rounds`, because the volley had not been built yet. Then, once the burst began outliving
the run, a raw `Projectiles.Count` swept its tracers into the total and reported **18 rounds for an
11-round volley** on device. It now counts the volley and nothing else. A lying instrument is worse
than a missing one when it is the only instrument a release build has, and this line has now earned
that warning twice:

```
[Battle] airstrike run, volley held at 86% / 45.0deg
[Battle] volley: 11 rounds, after the airstrike        <- 1.10s later
```

### The arm/spend split, which is the one piece of this that cost something

**Airstrike and Smoke are ARMED and spent only when they FIRE. Trauma Kit and Reinforcements
resolve on the tap.** A first Kotlin implementation spent at arm time and the HUD button — whose
visibility is gated on the equipped count — vanished the instant it was tapped, with no ARMED state
to see and no way to change your mind. That was found on a device, not in a test suite. The device
shot above showing `Smoke / ARMED` still on screen is the evidence that this port did not repeat it,
and `PortSelfTest` asserts arming does not decrement.

**The permanent `ProgressStore` spend lives in `BattleRunner`, never in the tick.** The two armed
items are consumed inside pure tick functions, and a `PlayerPrefs` write in there would fire on
every `PortSelfTest` call to `FireVolley` and quietly drain the editor's own inventory. The runner
watches the armed flag's true→false transition — which is NOT the "inferred from a list-length
delta" trap this project has been bitten by, because the flag exists for exactly this and nothing
else clears it.

### Early Reinforcements dragged a second port in with it

The relief squad did not exist here either — no builder, no march. It enters a formation's width
BEHIND the player line and runs to its slots on `MarchTargetX`, so **without a march step the men
bought and paid for would stand off the framed edge for the rest of the battle.** `BattleTick
.StepMarch` is that step, and it runs on the battle-over tick path too: a jogging man frozen the
instant victory lands is on screen, because that path deliberately re-frames onto the survivors.

The squad is built from the player's OWN commonest ground unit rather than a hardcoded Rifleman as
the Kotlin does — `BattleTick` has no asset table, and giving it one means a serialized reference
and a scene rebuild for a squad the player already described at the loadout screen.

*A claim in the first draft of the plan was WRONG and the compiler caught it: I wrote that an
unwalked march would hang the turn via `GameState.Settled`. The property is `IsVisuallyIdle`, the
handover is `TurnFlow.EvaluateVolley` and does not consult it, and nothing in the port reads it yet.
The real cost is a permanent latch on a ported facility. Recorded because it is this file's own
standing rule — assert the artefact, not the name you remember — catching the person applying it.*

### The checks: 576, and every new one was seen to fail first

`PortSelfTest` went 559 → 576 (17 checks, deliberately consolidated — see below). Per the standing rule, the new block was run against **nine
deliberate breakages** and each one turned the intended check red: smoke wired to nothing
(`1.87 -> 1.97` spread, refused), the airstrike aimed 3 units off (`13.92 vs 10.92`), the march
removed, the trauma kit healing everyone, arming spending the carry, the cap truncating instead of
refusing, Overwatch sold, and — the ninth, added after the first pass — the airstrike reusing the
PLAYER's flight time, the real Kotlin bug, which a flat drag compresses to 0.63s and an arced one
stretches to 3.08s against its own fixed 1.4s.

**The block was then CONSOLIDATED, on Rob's instruction** — 50 assertions over 307 lines became
18 over 232, and the same nine breakages were re-run to prove nothing was lost. Coverage went UP:
a merged check caught the flight-time constant the split version had missed. Related facts belong
in ONE check whose message names them all; a failure naming three properties is as diagnostic as
three checks, and this file is read by people. Keep a check separate only when it can fail
independently for a reason worth naming. **Consolidating is not a licence to drop coverage —
re-run the breakages after merging.**

**One of the first-pass checks was worthless and was rewritten**: a refusal test written as
`ReferenceEquals(Use(hurt with {...}), hurt with {...})` allocates two different records, so it was
false whatever the code did — a check that could never fail, wearing the costume of a refusal test.
The same family as the phase-spread check deleted during the flame work.

**And one check was self-referential**: the airstrike's fall time was asserted against the same
constant that defines it, so setting that constant to 0.18s passed every check. It now carries an
absolute floor (`>= 0.8f`, "legible means SECONDS, not frames") alongside the flat-drag comparison
that does the real work.

### What the headless preview caught before the device

`BattleUIPreview.Shots` now renders the loadout panel in three states (nothing owned, owned, and
carrying) and **fails the run if any Button lays out off screen**. That is precisely the failure the
Kotlin hit when it added a consumables section: Confirm was pushed past the bottom of the screen,
not clipped but ABSENT from the tree and unreachable by any input, found on a locked device with no
way to start a battle. The strip here is positioned from the panel's own top rather than stacked
after the roster rows, so a longer roster cannot push it anywhere, and `PortSelfTest` pins the
arithmetic against the live roster's row count.

## The loadout screen's per-frame NullReferenceException — FIXED 2026-08-10

**The tick was running before there was a battle to tick.** `Start` calls `EnterLevel(0)`, which for
a campaign level opens the picker and RETURNS — `LoadLevel` does not run until the player presses
BEGIN. `GameState` is a `record`, so it is a CLASS and `state` is null for that whole screen, and
`Update` entered `BattleTick.Step` anyway. Its first line is `s.SelectedAmmo`. One thrown exception
and one stack capture per frame, on the one screen where the player is sitting still and reading.

The guard is `if (state == null) return;` at the top of `Update`, and it is on the STATE rather than
on `ui.LoadoutOpen` deliberately: a LATER picker (RETRY, NEXT LEVEL) opens over a state that exists
and ticks through it perfectly well. What must not run is a tick with nothing to tick.

**Why it hid.** Nothing looked wrong, because the picker is uGUI on its own canvas and both
`HandleInput` and `OnGUI` already stand down while it is open — so the screen drew correctly, BEGIN
worked, and the battle ran clean at 60 fps. The release build's IL2CPP trace carries no line
numbers, and `BattleRunner.Update` was the only frame in it.

**Measured both ways, same instrument, same 3-second window, same screen** — which is what makes the
numbers mean anything, per the standing rule:

```
OLD code   186 NullReferenceExceptions in 3s on the LOADOUT screen  (~62/s = one per frame)
FIXED        0 in 3s on the LOADOUT screen, 0 in 3s in battle, 0 across a real volley
```

The negative run cost one extra build and is the only thing proving the guard reaches the bug. The
instrument was proved too, because a silent logcat is not evidence: the same capture shows
`[Battle] L1 Patrol Encounter: 10 player, 9 enemy, 2 structures` on the BEGIN press, and a real drag
(`input swipe 300 900 631 1231 600`) took the enemy line 9 -> 4 at a steady 60 fps.

**No self-test covers this**, and that is a considered choice rather than an omission: `PortSelfTest`
does not drive `MonoBehaviour` frame callbacks, and a check on the guard's CONDITION would assert the
fix's own restatement of itself — the "assert the output, not the input" trap in its purest form. The
device count IS the assertion here, and both halves of it are recorded above.

**The diagnosis was READ, not probed** — the backlog entry recommended a probe and the code answered
faster. That is not a correction to the rule: the probe is right when a static read leaves a
hypothesis, and here the read produced a null field, a dereference of it, and an exact match to both
measured contexts (null only before the first `LoadLevel`; zero after). The negative run then did the
job the probe would have.

## The incendiary flame — 2026-08-09

The burn had dealt damage since Tier 1.1 with **nothing to see**. The only way to confirm from a
device that it had fired was the `[Burn]` log, which is why that log was kept and why it stays.

**It needs no new tick state.** The flame is drawn straight off `GameState.BurningEnemyIds` — a set
that is filled when the round lands and cleared when the burn resolves at the turn handover. That
window is the whole post-volley pause, which makes the fire a **telegraph** as well as a cue: it
says these men are about to take damage, and the health bars drop as it goes out.

**Two tongues per man, one quad each, flickering out of phase.** One tongue is a shape that changes
size; two are a fire. The flicker is `CosmeticSystems.FlameScale`, a **sine of absolute time** — dt
VARIES, so anything integrated per frame would run at a different rate on a stuttering one, and a
phase accumulated per slot would need clearing on recycle. Height and width swing in ANTIPHASE (a
flame narrows as it licks up); swung together the tongue just zooms and reads as a throbbing
sticker.

**`FlamePhase` is keyed on the UNIT ID, not the render slot.** Slots are handed out in roster order
and shift down as men die, so a slot-keyed phase would make every surviving flame jump the instant
a neighbour fell — the same reasoning as `UnitAnim.Desync`.

**The colour is in the TEXTURE, not in a tint.** Hot yellow core, deep orange tips: that gradient is
the whole difference between "fire" and "an orange triangle" at this size, and a per-instance tint
can only scale the lot. The property block is left for the guttering alpha, which is per-slot.

**A pooled flame, bounded and pre-warmed**, sized from the enemy roster including waves and boss
phases. Minting one the frame a volley lands — alongside the blast, scorch and debris pools — is
exactly the mid-session mint the Filament build kept paying for.

**It gutters out over half a second rather than stopping.** The burn resolves on ONE frame, and a
bright orange object vanishing in one frame is the artefact this repo has already paid for twice
(the health bar and a backdrop layer both held full strength and then blinked out). There is no
matching fade IN — fire catches instantly and dies slowly.

**And the flame follows a body the burn KILLS.** A man the fire finishes leaves `EnemyUnits` on the
frame he dies, so drawing only the living would snuff his flame at the exact moment it did the most
work. `DyingUnitEntity` carries the same `Id`, so the corpse keeps its own guttering half-second and
the fire falls with him.

### Two things the preview caught that no test could

`FlamePreview.Shots` renders the flame on a rank of soldiers at gameplay framing, in seconds,
**through `Render/FlameRig` and the shipped `Flame.prefab`** — the same placement and the same art
the game uses. It is deliberately NOT a second implementation: `BackdropPreview` once was, and spent
a whole session producing plausible, wrong pictures.

Its first render found both of these in one frame:

- **The flame was UPSIDE DOWN** — fat hot base licking down at the boots, tapering to a point above
  the head. The prefab copied the health bar's 180-degree turn about **X**, which mirrors the
  VERTICAL, and the texture is already generated the right way up. It is a turn about **Y** now,
  which mirrors the horizontal and costs only the direction of the tip's lean. *The health bar takes
  the opposite choice for the mirror-image reason: its fill anchors to one END, so it cannot afford
  a horizontal mirror and can afford a vertical one.* Both are 180-degree turns that "face the quad
  at the camera", and they are not interchangeable.
- **It read as a CANDLE, not a man alight** — a taper of `(1-t)^0.62` kept the tongue
  narrow-but-present all the way up and drew a needle, so six burning soldiers looked like six
  rocket exhausts. Steeper taper (0.85), a wider body (0.50), and a tip fade tripled to 0.34 so the
  fade ends the flame rather than the profile. Shorter and broader overall: 1.05x body height at
  0.76x width, from 1.15 and 0.60.

### Confirmed on device, L1, 2026-08-10

A real drag (`input swipe 300 900 631 1231 600`) into L1's bunker, incendiary selected:

```
[Probe] ammo=Incendiary unitsHit=5 incendiaryHits=5 survivorsMarked=5
[Burn] 4 burning took 8 (4 died)
```

And on the frames: **two garrison soldiers alight on the bunker DECK** — standing on the deck, not
the world floor, so the entity-relative y is right — each flame the correct way up, wide hot base at
the boots licking just past the helmet, and the two **visibly different in size and lean on the same
frame**, which is the per-unit phase doing its job. Fire-coloured and unmistakable against the red
enemy uniforms.

Then the whole death sequence, which is better than designed: the burn kills them, **the ragdoll is
thrown and the fire goes with it**, guttering out as the body tumbles. **60 fps throughout**, read
off the HUD on four consecutive samples during the burn.

**One artefact, UNRESOLVED and deliberately not chased** — tracked in `_plans/BACKLOG.md` as
"Flames outlive their bodies by a frame or two". For a frame or two at the moment of death the two
flames stand on the deck with NO BODIES under them, before the corpses appear in flight. Best
current reading is the already-documented "a unit's slot is not stable across frames"
corpse-handover timing, which the flame has made visible for the first time — but that is a
hypothesis from one contact sheet at 12 fps, not a diagnosis.

**What the preview could not show and the device did:** the guttering, the flame on a garrison
rather than on flat ground, the frame rate, and the death sequence.

### The trap that cost most of the session: AUTO IGNORES THE AMMO SELECTION

Six incendiary volleys were fired with the AUTO button and **not one man ever caught fire.** Nothing
was wrong with the flame, the burn, or the marking. `AutoFire` builds its own `ProjectileEntity` and
**never sets `Ammo`**, so every round it throws is Standard however loudly the HUD says Incendiary.

It is deliberate — `CannonShells` documents the identity default in as many words — and it is the
exact sibling of the long-standing "**Auto cannot test STRUCTURES**". Auto is a test harness, not
the player, and the list of what it cannot test is now two items long.

**What settled it was a PROBE, after two rounds of guessing had not.** One build, one line, both
ends of the path:

```
[Probe] ammo=Incendiary unitsHit=1 incendiaryHits=0 survivorsMarked=0    <- AUTO
[Probe] ammo=Incendiary unitsHit=5 incendiaryHits=5 survivorsMarked=5    <- a real DRAG
```

The state said Incendiary in BOTH. Only the rounds differed — which is precisely the "assert the
OUTPUT, not the input" rule wearing yet another costume, and the reason the probe printed the state
and the rounds side by side instead of either alone.

**Both facts are now `PortSelfTest` checks**, so nobody has to rediscover this against a phone:
Auto's rounds must be Standard while the state says Incendiary, and a real volley must carry the
selection. The second is not decoration — without it the first would still pass if ammo were broken
everywhere. The Auto limitation is also in `CLAUDE.md` beside its structures sibling, with the drag
that clears L1's 16 units.

### The checks, and the one that was deleted for failing its own negative test

`PortSelfTest` asserts the flicker arithmetic directly, and asks the TEXTURE about its shape —
because the failure being guarded is "the texture is wrong, so the game draws an orange RECTANGLE
over every burning soldier", which no test of the generator's inputs can see. The shape checks carry
their **own negative case in the same run**: a plain white square must fail every one of them.

Every check was then run against deliberately broken code, per the standing rule. That is how three
of them were confirmed (taper, neck, antiphase all went red) — and how one was found worthless:

- A check asserted the **spread between the largest and smallest neighbour phase gap**, claiming to
  catch "a wave marching along the rank". A ramp (`unitId * 0.1f`) sailed through it at 6.08 rad,
  because the wrap-around manufactures one enormous gap. **Deleted.** A check that names a failure it
  cannot detect is worse than no check: it reads as coverage.
- Its neighbour, "neighbouring units rarely flicker together", scored **394 of 400 pairs** on that
  same ramp. That is the one that works.
- An earlier version asserted a FLOOR on the closest pair, and failed on the honest implementation:
  among 40 random phases some pair is almost certainly within a few hundredths of a radian. That is
  what randomness looks like, and it is invisible in a crowd of thirty. **Assert the distribution,
  not the extreme.**

## Tier 1.2 — the telegraph and the schedule, 2026-08-07

The mechanism was already firing (Phase D wired arrival). This is the half that makes a wave
something the player can PLAY AGAINST rather than something that happens to them.

**The countdown is composed, not authored.** `ReinforcementWave.telegraphText` — one authored
sentence with the number baked into it — is now `telegraphLabel` (what is coming) plus
`telegraphLeadTurns`. `EventSystems.TelegraphLine` builds the line every tick from the live turn
gap. A number in the label held still for the whole warning, which tells the player the clock has
stopped, and sat one copy-paste from disagreeing with `arrivesOnTurn` with nothing checking it.
`ReinforcementWaveBeat` takes the lead; a lead below 1 is CLAMPED, not honoured — an individual
wave does not get to opt out of pillar 7. Where two leads overlap the strip shows the NEAREST
wave, because there is one strip and a flickering one reads as neither.

**Confirmed on device, whole cycle, L10 Rubble Yard:** turn 2 `Heavy support inbound - 2 turns`,
turn 3 `Heavy support inbound - 1 turn`, turn 4 the strip clears and enemy units go 8 -> 12. The
strip also sits correctly UNDER the "Enemy turn" banner when both are up — the two channels were
built to stack and this is the first build in which both have been on screen together.

**The schedule is two levels, both stage 2, both 2-turn leads.** L10 is the beat chart's
"reinforcement race" and went 1 -> 2 to match the chart's own words ("armor in 2 turns"). L11
Oceanfront was the only stage-2 level `CampaignAudit` called NO MECHANIC — its beat offers a heli
it cannot have — so its own "else elite push" fallback is delivered as a telegraphed wave (3
heavies, turn 4) instead of three more bodies in the opening formation. **L11's wave was NOT
device-tested**; it is the same code path and data shape as L10's and is driven by `PortSelfTest`.

**L12 was deliberately left alone.** It already combines the boss phase with the charge, and its
`designNotes` record a device-measured margin against the 288 siege capacity. Adding enemies to
the finale would have quietly undone a number someone paid a defeat to find.

**`BalanceAudit` now checks reach for units that are not on the field yet.** Rule 7 measured the
opening roster only, so a wave could be authored past maximum range and every rule-7 check would
pass while the level was unwinnable from turn 4 — the L7 bug, one turn later. It STEPS the tick to
the wave's arrival and re-runs the reach rule on the real spawned positions rather than
re-deriving them from `anchorX`, which would be a second implementation to disagree. Proved by
pushing L11's wave to x 22: `121% power ... UNWINNABLE`, 2 errors. At the authored x 8 both wave
levels clear (L10 89%/97%, L11 89%/98%).

### The check that was right about the mechanism and wrong about the rule

A glyph-coverage check over every authored string that reaches TMP was written as **"ASCII only"**,
because that is what `CLAUDE.md` said. It flagged **23 strings on its first run** — every campaign
`levelGoal` and all 17 test-rig names, all of which use an em dash — and all 23 were "fixed".

**A device screenshot then showed an em dash rendering perfectly in the loadout panel.**
LiberationSans SDF covers Latin-1 and General Punctuation; what it lacks is SYMBOLS — `★` U+2605,
`◆` U+25C6, emoji, arrows — which is why the star and coin are drawn as sprites. All 23 edits were
reverted, and the check now asks `TMP_Settings.defaultFontAsset.HasCharacter` instead of asserting
a range. It carries its own negative case in the same run (the star must be reported MISSING), and
it does catch one real thing: wind's announcement strings came from the Kotlin with a wind emoji
and two arrows.

**The lesson, and it is a new costume on the standing rule:** a check written against a NOTE IN A
DOC asserts the note. The doc was a compressed heuristic that had been true about the two symbols
it was written for. Ask the thing itself — the font, the engine, the device.

### The state of the checks, as of the handover

```
PortSelfTest.Run          592 checks, ALL PASS
LevelComposition.Report   12 campaign levels, 0 errors, 2 accepted warnings (L3, L5 rule 7 —
                          reasons in their designNotes; both beats are about height)
BalanceAudit.Report       0 errors, 19 warnings (race-ratio flags on the dearest-squad
                          extreme, which is informational)
```

**A scene rebuild is NOT pending** — one was run on 2026-08-10 after `BattleRunner` gained its
`planePrefab` field (and earlier the same day for `flamePrefab`), so `Assets/Scenes/Battle.unity` and several materials are dirty because of it.
**The APK on the device is current** — rebuilt on 2026-08-11 with the whole airstrike rework: tracer
streaks, the run's camera cut, the enemy-derived rake, the impact realignment, the pass-by sound's
new anchor, the honest release log, and test supply carrying all four consumables. Everything above
was confirmed on that build. **All of 2026-08-11 is CODE-ONLY — no scene rebuild is pending.**
The device is on L1 with RIGS ON and a fresh install's zero coins, which costs nothing because test
supply is free.

## The balance audit, DEVICE half — run 2026-08-07

> **STALE, 2026-08-25 — READ THIS BEFORE QUOTING ANY NUMBER BELOW.** Every result here was
> measured when the player tank carried **THREE** shells. It now carries **FIVE**
> (`PlayerTank.cannon.ammoPerBattle`, per-level `shellsOverride`), which moved siege capacity
> from 288 to **480** on every level with a tank. The clearability verdicts — including "L9 and
> L12 are NOT clearable at stock" — have **not been re-measured** since. The one finding that did
> survive is the L4 one, and it survived by being rediscovered the hard way on 08-25: the shells
> are the whole demolition budget and nothing in the game says so.

Real drags on the Pixel 10 Pro XL, stock squad (8 riflemen + 2 tank crew, 0 coins, nothing
unlocked — the level list steps straight into battle with the default loadout, so this is exactly
the baseline the arithmetic half modelled). Aim was DERIVED, not guessed: `BalanceAudit.Drags`
prints a 45-degree adb swipe per level from the level's own geometry.

**Result: L9 and L12 are NOT clearable at stock. L4 is, but only if the tank shells are spent
correctly, and nothing in the game says so.**

### The mechanism, and it is one number

**A rifleman's `structureDamageMultiplier` is 0.25.** His 8-damage round does **2** to a building,
so a ten-strong volley does **20 a volley if every round lands**. The tank shell is
`32 x 3 = 96`, and `ammoPerBattle` is **3**. So a stock squad's entire anti-structure capacity is
a FIXED **288**, spent in the first three volleys, after which a wall is effectively immune.

That collides head-on with composition rule 5, which REQUIRES the majority of the enemy roster to
be garrisoned. Where garrisoned structure HP exceeds 288, most of the enemy roster is standing
behind something the stock squad cannot break:

| Level | Garrisoned structure HP | vs 288 | Device result |
|---|---|---|---|
| L3 Watchpost Ridge | 340 | **deficit 52** | not run |
| L5 Tower Assault | 340 | **deficit 52** | not run |
| L6 Ridge Bastion | 392 | **deficit 104** | not run |
| L9 Dusk Redoubt | 330 | **deficit 42** | **3 runs, 3 total defeats** |
| L12 The Citadel | 425 | **deficit 137** | **defeat** |
| L4 Ash Boulevard | 240 | ok | every structure razed by shells alone |
| L1/L2/L7/L8/L10/L11 | 90-240 | ok | not run |

`BalanceAudit` now checks this directly (the SIEGE DEFICIT finding), and it is **predictive**: the
level with no deficit razed everything, the levels with one could not.

### What the runs actually showed

**L9, run 1** (fixed aim at the enemy mean): 22 -> 17 enemies, player 10 -> 0. Kills stopped DEAD
at 17 the moment the shells ran out, and structure damage fell to ~8 a volley.

**L9, run 2** (all fire on the bunker): bunker destroyed, but I kept firing at the empty site while
the barracks sat untouched. Defeat. Worth recording as an ERROR OF MINE, not a game fault — the
HUD's single "Structure HP" total cannot say WHICH structure still stands, and that is a genuine
readability gap.

**L9, run 3** (advancers first, then structures — the correct play): 22 -> 11 in three volleys with
only one loss, and the bunker's five machine gunners died with it, confirming the garrison-collapse
path works. Then the wall: ~6-10 structure damage a volley against 118 remaining, while losing ~1
unit a volley. Ended 1 unit vs 9 enemies.

**L12**: 18 -> 10 in three volleys (a roof garrison CAN be shot directly, as the self-tests claim),
then the same wall — ~12 a volley against 231 remaining. Player 10 -> 4 by volley 8.

**L4, run 1** (shells spent on the advancing shield bearers): 17 -> 7 with no losses, but the
structures were untouched and it stalled at the wall.
**L4, run 2** (all three shells into the structures): 17 -> 7 with **zero** losses, every enemy
structure destroyed by volley 10. It then stalled 7v7 because the survivors had closed to melee
range and my long derived drags flew over them — a limit of driving this from adb, since a real
player has the aim preview and would simply shorten the drag.

### Honest limits of this pass

- Drags were computed, not felt. In the endgame, when survivors close on the line, a computed
  45-degree drag overshoots and I had no preview to correct against. **A human is better than this
  harness at short range**, so L4's stall is not evidence that L4 is unwinnable.
- L3, L5, L6 were not run. Their deficits are known and L6's is large.
- Every run used the stock squad. Unlocking the rocket trooper (`structureDamageMultiplier` 6)
  changes the siege arithmetic completely, which is presumably the intent — but 0.4b says a level
  must be clearable at STOCK, and these are not.

### What this asks for — a decision, not a task

Three ways out, and this is Rob's call:

1. **Cut garrisoned structure HP under 288** on L3/L5/L6/L9/L12. Smallest change, keeps every beat,
   and the audit check enforces it from then on.
2. **Raise `ammoPerBattle`** from 3. One number, fixes all five at once, but it makes the tank the
   answer to every level and weakens the reason to ever buy a rocket trooper.
3. **Raise the rifleman's 0.25 structure multiplier.** Most invasive — it changes every level at
   once, including the seven that are currently fine.

My recommendation is **1**, plus a HUD change worth doing regardless: **"Structure HP" is a single
total across all enemy structures**, so it cannot tell the player which building is still standing.
That is what made run 2 waste four volleys on rubble, and a player has no better information than
I did.

## THE TANK SHELL DOES NOT LAND WHERE YOU AIM — found 2026-08-07

The most useful thing the whole balance audit turned up, and it was invisible until the HUD listed
structures separately.

**Measured on L12, one volley per drag, reading per-structure damage:**

| Drag (px per axis) | near tier | far tier | enemies |
|---|---|---|---|
| 272 | **-16** | 0 | 0 |
| **300** | -10 | **-96** | **-6** |
| 316 | -6 | -10 | -5 |

96 is exactly the shell (`cannon.damage 32 x structureDamageMultiplier 3`). So the shell lands on
the FAR tier when the infantry is aimed roughly at the NEAR one.

**The exact overshoot was pinned afterwards, in the harness rather than by eye: 2.5 to 3.9 units
depending on the aim** — at aim (6,6) the volley lands at 10.92 and the shell at 14.86. (The
device drag-deltas suggested ~1.3; that estimate was low, and the analytic figure supersedes it.)
`velocityBoost` is 1.12 and range goes as v², so the shell flies 1.2544x the infantry range. The
boost exists to stop the shell falling SHORT of the line's own volley (`BattleTick.CannonShells`
says so), and it overshoots instead.

**Why this matters more than any HP number.** The shell is the only weapon a stock squad has that
can break a structure — 96 against a rifleman's 2. The player aims ONE reticle and fires TWO
weapons that land in different places, and the one that matters is the one they cannot place. Every
failed run in this audit is at least partly this: shells thrown at a building and landing behind
it. My own first L12 run spent all three shells for ~58 total structure damage; the sweep above
put a single shell on target for 96.

**This is a candidate root cause for "levels are not clearable" that is INDEPENDENT of the HP
retune**, and it should be settled before any more level tuning. Three options, none taken:

1. **Aim the shell at the infantry's impact point** — solve the shell's velocity to match the
   volley's landing x rather than scaling the aim velocity by a constant. Most correct; it makes
   the single reticle honest. `TrajectoryPhysics.SolveVelocity` already solves speed to a target.
2. **Re-derive `velocityBoost` from the actual muzzle offset** rather than leaving it a hand-tuned
   1.12. Smallest change, still approximate, and it drifts the moment a tank moves on a level.
3. **Show the shell's own landing hint.** Rejected on sight — `CAMERA_ARCHITECTURE.md` and the
   HUD comment are explicit that a predicted landing marker was tried and reverted, because
   guessing angle and power IS the mechanic.

My recommendation is **1**.

### Structure HP is now listed PER STRUCTURE — done, confirmed on device

`BattleRunner.DrawHud` listed one summed "Structure HP" across all enemy structures, which cannot
say WHICH building still stands; it cost an earlier run four volleys fired into the site of an
already-destroyed bunker. It now lists each surviving enemy structure by `displayName`, nearest
first (which is also left-to-right on screen). Destroyed structures leave `state.Structures`, so
the list is automatically what is left to do.

**Duplicate names are real and would have rebuilt the exact ambiguity:** L12 places
`FortressTierSmall` and `FortressTierWide` and BOTH are called "Fortress Tier". A positional
qualifier is appended only when a name actually collides, so the common case stays clean.
Confirmed on device: `Fortress Tier (near): 115` / `Fortress Tier (far): 165`, and the qualifier
correctly disappears when one of them falls. Code-only, IMGUI — no scene rebuild.

### L9 roster cut 22 -> 15

L9 fielded 22 against the player's 10, the widest body ratio in the campaign, and the volley race
flagged it worst at 4.1x. Its garrisons were also over the decks they stand on:
`LEVEL_AUTHORING.md`'s capacity table rates a MountainBunker at ~2 and a BarracksBlock at ~4, and
this level had **5 and 8** on them. Now shields 6->4, forward rifles 3->2, bunker gunners 5->3,
barracks rifles 8->6. Garrison stays the majority at 9 of 15.

Effect on the audit: **race ratio 4.1x -> 1.9x**, under the warn threshold, with siege, melee clock
and all seven composition rules clean. Not re-run on device after the cut.

### Where L12 stands

Still not cleared, across two runs — but neither was an optimal line: the first mis-aimed all three
shells, the second spent them on the sweep above. Knowing that drag 300 puts a shell on the far
tier for 96, the untried optimal line is two volleys at 300 (far tier 165 dead, ten garrison with
it), then the near tier. **Try that before concluding anything about L12's tuning**, and ideally
after fixing the shell aim, which would change the answer for every level at once.

## The shell now lands where you aim — FIXED 2026-08-07

`BattleTick.FireVolley` no longer scales the aim velocity by `velocityBoost`. It takes the
**volley's own landing point** — `TrajectoryPhysics.LandingPoint` from the mean muzzle of the
firing line — and solves the gun onto it at the **same launch angle**, so the shell stays visually
part of the same volley.

`velocityBoost` survives with a real meaning: it is now the gun's speed **HEADROOM** over the drag
that ordered the shot — how much further back the tank may stand and still make it. A muzzle ~2
units behind the line needs about 1.07x, so the authored 1.12 covers it with room. It is a CAP on
the solved speed rather than a blind multiplier.

`InfantryMuzzleY` (0.35) is now a named constant used by the volley, the auto-fire path and the
shell's aim point. Two copies of that number would put the shell on a subtly different target.

### The size of the bug, pinned in the harness rather than by eye

The new self-tests were run against the OLD code before the fix was kept — because a check that
has never been seen to fail is not evidence. It failed exactly as it should:

| aim | volley lands | shell landed | overshoot |
|---|---|---|---|
| (5,5) | 5.41 | 7.95 | **+2.54** |
| (6,6) | 10.92 | 14.86 | **+3.94** |
| (7,5) | 10.60 | 14.50 | **+3.90** |

**The device drag-deltas had suggested ~1.3 units; that estimate was low and this supersedes it.**
After the fix all three agree to 0.01.

`PortSelfTest` now asserts the shell and the volley land together from their real (different)
origins across three aims, and that the shell never exceeds its boost headroom. Comparing LANDING
POINTS rather than velocities is the point — equal velocity from different origins is precisely
the bug.

### This did NOT make the siege retune unnecessary

Worth stating, because it was the open question when the fix was chosen. Shell capacity is
`3 x 96 = 288` either way; the fix lets the player RELIABLY LAND it rather than raising it. Every
pre-retune value (L3 340, L5 340, L6 392, L9 330, L12 425) still exceeds 288, so those levels were
unclearable on the arithmetic alone and the cuts stand. **Do not walk them back.**

What the fix does change is that the seven levels already under 288 got easier, because their tank
now reliably delivers 96s it used to throw past the target. None have been re-checked for being
too SOFT — that is the open risk from this change.

### On device: the structure phase now works, the ending is unconfirmed

L12, aiming directly at each structure (which is the whole point of the fix):

| volley | player | enemy | near tier | far tier |
|---|---|---|---|---|
| 0 | 10 | 18 | 115 | 165 |
| 1 | 10 | 18 | 109 | **59** |
| 2 | 9 | 13 | 107 | destroyed |
| 3 | 9 | 8 | destroyed | — |

**Both structures down by volley 3, the roster halved, nine of ten units alive.** Before the fix
the same level ate three shells for ~58 total structure damage. That is the fix working.

The ending is still unconfirmed, and honestly so: a second run with slightly looser drags left one
tier standing at 53 and sat at 6 v 13, and lost. Outcome is very sensitive to shell placement —
which is arguably RIGHT for a finale (three shells, 96 each, place them well) but means my adb
harness cannot reliably reproduce a win. **L12 is now plausibly winnable and has not been won.**

### Still owed — ALL SINCE RESOLVED

**Rob played the campaign after this fix and reported the levels feel fine (2026-08-07).** That
answered all three at once: a level clearing by hand, whether the seven levels already under 288
had gone too soft once the tank reliably landed, and L3/L5/L6 never having been played.

## Tier 1.1 — AMMO TYPES, built 2026-08-07

`PRODUCT_DIRECTION.md` Tier 1.1, spec `DYNAMISM_DESIGN.md` Phase A. **A fifth dead system**: the
`AmmoType` enum, `ProjectileEntity.Ammo`, `GameState.SelectedAmmo`, `GameState.BurningEnemyIds`,
the unlock/selection persistence in `ProgressStore`, `EconomyStore.PurchaseAmmo` and
`CollisionSystem.IncendiaryHitUnitIds` ALL existed since the port, and `FireVolley` never set
`Ammo`. Every round the game had ever fired was Standard, forever. This was wiring, not a build.

### What is there now

| | |
|---|---|
| `AmmoCatalogSO` + `Assets/GameData/AmmoCatalog.asset` | the four types, their prices and their numbers. Authored by `AmmoSetup.Build`, which is idempotent and safe to re-run |
| `AmmoModifiers` | the pure, testable projection the spec asks for — no ScriptableObject reaches the damage math |
| `BattleTick.FireVolley(.., ammoCatalog)` | stamps `Ammo` on every round INCLUDING the tank shell, and applies the scales |
| `BattleTick.Step(.., ammoCatalog)` | applies the incendiary burn on the handover edge |
| `BattleRunner.DrawAmmoSelector` | the in-battle selector, which also SELLS |

| Type | Effect | Price |
|---|---|---|
| Standard | the identity — cannot change a volley | free |
| Incendiary | 0.85x damage, and hit survivors take **8** at the enemy windup | 300c |
| AP | **2x to structures**, 0.6x to men | 400c |
| Cluster | **3.2x spread**, 0.65x damage — the wide-formation counter-pick | 500c |

**Standard is the IDENTITY and that is asserted**, which is what makes PRODUCT_DIRECTION's "no
ammo may be REQUIRED to clear a level" a checkable property rather than a promise: a level cleared
on Standard is a level cleared with every modifier at 1.

### The bug the DEVICE caught that the tests had passed over

Firing AP at L12's 165hp citadel took **128** off it where ~192 was intended.

The engine computes structure damage as `Damage * StructureDamageMultiplier`. The first version
scaled `Damage` down by AP's soft-target penalty, which then flowed through to masonry too — so
AP's real structure effect was `0.6 * 2 = 1.2x`, not 2x, and the type had almost no reason to
exist. **The test that passed was asserting the FACTOR (`StructureDamageScale == 2`) instead of
the PRODUCT.** `StructureMultiplier` now divides by `UnitDamageScale` so the two knobs are
independent and both read against the base round.

The replacement check asserts the NET per-round damage across three unit profiles, and was proven
to fail on the old form first: it reports 1.19x / 1.25x / 1.25x. Its tolerance is DERIVED from
integer rounding (`Damage` is an int, so an 8-damage round at 0.6 lands on 5, giving 2.08x) rather
than guessed, because a fixed epsilon would either fail that honestly-correct case or be too loose
to catch a 1.2x regression.

**The lesson is the one this file already records in four costumes: assert the OUTPUT, not the
input.** A multiplier being 2 is not the same claim as the damage being doubled.

### Decisions worth knowing

- **The selector SELLS.** Purchase lives in the in-battle selector rather than the loadout panel:
  the coin balance is already on that HUD and the panel is a fixed eight-row uGUI layout. Buying
  mid-battle is deliberately allowed — coins are earned, no ammo is required to clear anything,
  and "I want that one now" is the impulse a coin sink exists to catch. Buying also SELECTS, since
  buying then picking is a second step with no decision in it.
- **A tap on the selector can never start an aim drag.** `AmmoSelectorRect` is one definition read
  by both the drawing and the touch exclusion — the same trap the free-camera pad paid for, where
  a finger on a button also threw a volley and ended the turn.
- **No mid-drag switching, aiming phase only**, per the spec.
- The choice PERSISTS via `ProgressStore`, and is re-read on every level load, which also
  downgrades a selection the player no longer owns after a reset.
- **The burn is ONE tick, cleared as it is spent.** A unit that kept burning every turn off a
  single round would make the type a win button.
- `burnDamage` is **8**, re-derived against the CURRENT roster (it must chip, not one-shot, the
  frailest unit — now the 16hp Sniper). HANDOVER's old note about 6 being calibrated against an
  8hp Sniper is resolved; the check anchors to the live roster so it cannot expire silently again.

### Verified on device

- **The selector** renders and behaves. Purchase works end to end: 455 -> 55 buying AP, 325 -> 25
  buying Incendiary, 745 -> 245 buying Cluster, each button going gold and losing its price.
- **AP, after the correction.** One AP volley destroyed L12's 115hp gate outright and killed its
  five-man garrison with it. Standard could NOT have: its shell is 96, plus ~10 infantry, leaving
  the gate alive at 9. The 2x observed rather than derived.
- **The incendiary burn**, via the probe: `[Burn] 1 burning took 8 (0 died)` on L3. This is the
  one that could not be confirmed any other way — the burn has NO VISUAL, so unit counts cannot
  see an 8-point chip. The `[Burn]` log is KEPT for exactly that reason.
- The structure-HP retune is visible on device too: L3's Command Bunker now reads 125.

**Cluster's SPREAD was not isolated.** Four volleys — two Standard, two Cluster, same drag on a
fresh L4 — killed nothing either way, because the drag was aimed at the barracks rather than at
bodies. That measures MY AIM, not the ammo. The spread is one multiplier on the jitter the volley
already had, is covered by the tests, and shares the code path AP and Incendiary were confirmed
on. What is genuinely open is the BALANCE question — whether 3.2x is so wide that Cluster misses
everything — and that wants a human playing it rather than a scripted drag.

### Not done

- **Cluster's spread is unmeasured in play** — see above. Is 3.2x too wide to connect?
- **No flame VFX.** The burn is a damage event with no visual yet; the spec asks for a flicker on
  a burning unit, and `DYNAMISM_DESIGN` requires any new effect to use a BOUNDED slot pool.
- The spec mentions AP being strong against "armored units". **There is no armour concept in the
  roster** — no unit has such a field — so AP is implemented as structures-versus-men only.

## Corpses levitating onto roofs — FIXED 2026-08-07

Rob: "dead units can have physically impossible interactions with structures." Found by reading,
reproduced in the harness, fixed, and both halves covered by checks.

**`BlockOnStructures` rested a body on a structure's ROOF whenever it was horizontally inside the
footprint and at or above the box's BASE — and a ground structure's base IS the ground.** So a
corpse flung into a wall at chest height was snapped up the face and left standing on top of the
building. The condition's own comment already said *"a body that CLEARED THE WALL should land on
the roof"*; `y >= baseY` was never that test.

### Reproducing it took three attempts, and the first two passing is the interesting part

- A body resting ON the ground dips a hair BELOW `baseY` between ticks, so it escapes the branch.
- A single tick does not carry a thrown body far enough to enter the box at all.
- Only stepping until it actually penetrates shows it: **peak y 4.00 against a roof of 4.0, from a
  launch height of 1.50.**

**A check that never reaches the code it is testing is a green light that means nothing**, which
is the same lesson this file records for the hit flash, the backdrop and the AP multiplier.

### The fix is not simply `y >= topY`

That stops the levitation and then drops a body FALLING onto the roof straight through it, because
once it dips below the roof it no longer qualifies. Whether a body belongs on a roof is a question
about where it CAME FROM — exactly like the existing face test, which is why `fromX` was already
a parameter. `BlockOnStructures` now takes **`fromY`** and asks whether the body was above the
roof LAST tick. Both behaviours are asserted: thrown-at-a-wall never rises, fallen-from-above
still lands.

### Checked and NOT a bug, so nobody re-investigates it

`StepRagdolls` is handed the tick's OPENING structure list (`s.Structures`, before this tick's
destruction is applied), so a razed building keeps blocking ragdolls for at most one extra frame
at 1/60s. That was my first hypothesis and it is wrong.

### What the device pass did and did not show

The free camera was parked at the L3 bunker (x 6.84) and volleys fired into it. It confirmed the
garrison stands correctly ON the deck, and that the destroyed Watch Tower leaves flat ruin slabs —
no impossible placement visible. **It did not catch a corpse-against-a-wall moment**: ragdolls are
short-lived and the volleys that landed did not kill. The harness evidence is stronger and more
precise than a screenshot would have been here, so that is what this fix rests on.

**Caveat worth keeping:** this fixes ONE reproducible mechanism. Rob reported the symptom from his
own play without a screenshot, so if bodies still do something impossible, it is a DIFFERENT
mechanism and this section should not be taken as closing the report. The next things to suspect
are the "spawned inside: just stop, do not teleport" branch (a body whose x begins inside a
footprint stays inside it) and the fact that the collision box is an AABB while the models are not.


---

## Split 2026-09-05 — 08-12 → 09-04 closed sittings

Moved WHOLE from `HANDOVER.md`. Newest first (09-04 late sitting), then 09-02, 08-25,
08-18 → 08-12, then the old Open items list and the 08-07 siege retune (since resolved).

### THE 09-04 LATE SITTING — rule 10, and both bosses were INVISIBLE

**Read this before the 09-04 block below it; it changes one of that block's
conclusions.**

Took the owed item 1 — *"L12's boss phase has never been seen on device"* —
and it did not come back clean.

**Verified on device first**, on the 09-04 end-of-day APK: L12's phase FIRES,
the telegraph reads **"The citadel is breaking - something waits inside"** at
citadel 45/165 (L12's half of the telegraph work, previously only seen on L6),
and the escort's 5.5 -> 7.0 move is right — it stands at x ~2.5 by turn 7,
exactly 3 marches of 1.5.

**Then: nothing renders where the Sovereign stands.** Free camera parked at six
positions around the breach, wreck geometry every time, while the same model at
the same scale (the two heavy riflemen) renders perfectly 6 units away.

**The cause. A structure's WRECK has no collider and nothing in
`CollisionSystem`** — `LevelScenery` swaps the collapse model in at the
building's own position and scale, and it is pure geometry. L12's citadel wreck
spans **x 4.88-11.13, top y 1.23**; the Sovereign is a 0.91-tall body at x 8.92.
It is reachable, hittable, inside no box, and 150% covered by rubble.

**It was BOTH bosses — 8 of the campaign's 9 boss-phase bodies.** L6's entire
phase (Sovereign + 3 heavies) sits inside the keep's wreck, x 5.75-10.25. None
of them ever walks clear: a boss is authored `advancePerTurn: 0`.

**This probably rewrites the 09-04 reading of L6.** That block concludes rule 9
cleared the Sovereign so three lost boss phases at nine powers were *"aim, not a
bug"*. Rule 9 is right about what it measures — the shots do land — but it
measures REACHABILITY, NOT VISIBILITY. Nine powers and one kill is what firing
at an invisible target looks like. **Not proven**: L6's phase has not been
re-played since the fix.

**Built: RULE 10**, `LevelComposition.WreckOcclusionRule`, delegated from
`PortSelfTest.CheckNoBossArrivesInsideItsOwnRubble`. It reads the footprint the
RENDERER uses — the wreck's own bounds scaled by the LIVE BUILDING's scale, which
is what `LevelScenery` does — and models the camera's own elevation, because a
wreck 1.2 above the ground hides more than its height when the camera looks
nearly along the ground. **Red first: 4 offenders on L6, 5 on L12, suite exit 1**,
then green. Deliberately NOT judged: a unit near a structure the PLAYER might
destroy later — that wreck is not guaranteed and the net would indict half the
campaign on a maybe.

**Fixed in Z, not X.** L6 `anchorZ 0.9`, L12 `anchorZ 1.8`, both boss groups on
each level. The x-axis carries reach, separation and every collision box, and on
both levels the near side is blocked by another structure's box while the far
side is out past 20 units where rule 7 starts warning — **the two constraints
close the gap between them**, exactly as on L10. Z carries nothing but looks:
collision is 2D in x and height, melee compares `attacker.X` alone, advance moves
x, rule 7 reads dx/dy. A deliberate exception to "separate bodies in X, never in
Z", which is a rule about counting BODIES, not about clearing a wreck 2.5 deep.

**Also built: `LevelComposition.Arrivals`**, a probe, not a rule —
`-executeMethod LevelComposition.Arrivals -probeLevels 12` prints where the
simulation actually places every arrival, through the same `ArrivalSets` rules
8-10 use. It is what settled the disagreement: the rules said x 8.92 and the
screen said nothing. A rule reports a verdict; a probe reports what the verdict
was reached from.

**Campaign: 12 levels, 2 warnings, 0 errors** (the standing L3 rule 7 and L5
separation). `PortSelfTest` ALL PASS.

**DEVICE-VERIFIED, both bosses, on the APK now on the phone.**

- **L12.** Free camera parked at x 8.46 z 8.79 — the same neighbourhood that
  showed nothing but rubble on the previous build — and **the Sovereign is
  there**, in front of the wreck, rifle up, grounded with a shadow. From the
  PLAYER's own resolve camera the whole phase reads: 3 riflemen, 2 heavies, 5 on
  the gate deck, **all four shield bearers countable**, and the Sovereign the
  largest figure on the field. That is the finale's beat, seen for the first
  time.
- **L6.** Keep dropped in four volleys, phase fired, and **all four arrivals
  stand in a legible line in front of the keep's rubble.** This is the level that
  was played three times on 09-04 with nothing killable.
- **The z offset does NOT look wrong.** They read as standing in front of the
  ruin, which is the beat both design notes describe. No floating, no size jump
  worth noting.
- **Rounds still land.** A volley was thrown at L12's Sovereign at 91% and the
  tracers arrive on him in frame; collision ignores z, as the code says. Nothing
  was seen passing distractingly behind him — but no round was watched to a
  CONFIRMED impact on the boss (260 hp, nothing died), so that specific worry is
  eased rather than closed.

**CORRECTION to something this file said earlier today: the uninstall/reinstall
did NOT wipe the economy.** The picker came back at **4085 coins**, unchanged.
The standing note that a clean install resets the test supply is about RIGS,
which was indeed off.

**Still owed on L6: a real balance run.** Visibility is fixed; DIFFICULTY is
still unmeasured with a visible boss. My one volley there landed at 85% and short
of the group, which measures the drag, not the level. It wants a picker entry
with RIGS and ammo in play — not more of my mechanical drags.

### THEN — hero scale walked back, and the boss got its own colour

Rob, on seeing the newly-visible boss: *"he's too big. we want him to stand
out but he's like twice as tall as the regular units."*

| | was | now |
|---|---|---|
| `EnemyHeavyRifleman` / `HeavyRifleman` | 1.9 | **1.45** |
| `CitadelSovereign` | 1.9 | **1.65** |

**The boss was never uniquely big** — same `renderScale`, same `unit_hero.glb`,
same trim as `EnemyHeavyRifleman`, which ships in FIVE campaign levels (L6, L7,
L10, L11, L12) and stands beside it on L6. Shrinking only the boss would have
made it shorter than its own escort, so the band moved together and the boss
keeps a 1.14x edge to stay the largest figure on the field.

**KNOWN RISK, on the record: 1.35 was tried once and rejected** for reading as
"a slightly big soldier" — that is why the number was 1.9. 1.45 sits just above
that mark. The bet is that STAGING changed underneath it: Tier 2.2 moved heroes
off decks onto the ground, isolated, and isolation carries contrast that size
used to carry alone. **If it reads as a big rifleman, the bet was wrong and the
answer is colour, not height.**

**The boss now carries its own trim** — the first per-DEFINITION tone in the
project. `UnitDefinitionSO.hasTrimColor` / `trimColor`, applied per instance in
`BattleRunner.ApplyTrim` through a **MaterialPropertyBlock**, because the slots
of a class share one material. **It CLEARS on a recycled slot** — boss and heavy
are the same model and draw from the same pool, so a slot that held the boss
holds a mook next turn. Brass-gold `0.72, 0.58, 0.20`, picked against BOTH
faction palettes (red on L6, blue on L12); the mg's brass is darker.
`PortSelfTest.CheckATrimOverrideReachesARenderer` asserts the wiring and was run
red first.

**Full write-up in `UNIT_VARIETY_DESIGN.md`.**

**One observation, NOT acted on: the Sovereign is indistinguishable from a heavy
rifleman.** `CitadelSovereign` and `EnemyHeavyRifleman` both use
`models/unit_hero.glb` at `renderScale 1.9`, so on L6 four identical figures
stand in a row and one of them is the 260-hp boss. **ACTED ON later the same
sitting — see the block above; the boss now wears brass-gold trim.**


Last sitting **09-04**, a long one. **Everything is committed AND
PUSHED** — seven commits on `session/2026-08-25-shell-art-ragdoll`,
`c9b8d40..670c643`. The working tree is clean apart from the untracked
tracer leftovers below. **Ask git anyway** — this file does not track
commits, and Rob commits/pushes on an explicit ask.

`PortSelfTest.Run` after every change; it now carries rules 8, 9 AND 10.
**RIGS** is the test supply (consumables, camo, classes, ammo) — the
clean install RESETS it, so check the button rather than assuming.
**Do not use Auto** for structures, ammo, or consumables. Android repo
is RETIRED. `DISPLAY=:0` (not `:1`).

**The phone has the 09-04 END-OF-DAY APK** — all seven commits. Boots
to L1's picker, coins **4085**, RIGS off, DND/stay-awake/auto-rotate
restored. Smoke-checked on L1 and L9 only.

### What 09-04 shipped, in the order the blocks appear below

| | |
|---|---|
| Rule 8 tightened | no unit starts inside a building, at all. Found two the old silent exemption hid (L9, L12) |
| **Rule 9, new** | the ballistic-shadow checker. FIRES REAL SHOTS. Found a man on L10 nobody could hit |
| L6 tested properly | picker entry, RIGS, 14/14 points, ammo + consumables. Three defeats. The spike is entirely the boss phase |
| Boss telegraph | pillar 7 finally reaches the boss. Threshold 0.5, measured |
| Ragdoll flail | it was INTEGRATING — 179.9° of wind-up. Now an offset from rest |
| Snow pine | had no snow in it. Rebuilt |
| L6's last folded deck | bunker 8 -> 6, the two men moved to the keep. Campaign is single-rank throughout |

### Owed, in the order I would take it

1. **L12's boss phase has never been seen on device.** Its escort
   moved 5.5 -> 7.0 and its Sovereign 7.5 -> 9.0 this sitting, and
   `LevelComposition` is the only thing that has checked them.
   Reaching that phase means bringing the citadel down. **This is the
   one unverified change on the phone.**
2. **L6 has never been won**, before or after any of today's work.
   Rule 9 says the Sovereign IS reachable, so this is difficulty and
   aim, not a bug — see the block on what three losses did establish.
   It wants a player, not more of my drags.
3. **A campaign run, L1 → L6, through the picker.** `PRODUCT_DIRECTION`
   gates Tier 3 on *"after >= 5 fun sessions exist"* and nobody has
   ever played the funnel end to end. Tiers 0-2 are otherwise closed.
4. The two standing composition warnings — L3 rule 7, L5 separation
   11.6 — both long-standing, both documented, neither touched today.

**Do not open unprompted:** tracer look, Kenney particles, a new death
clip. Leftover and deliberately NOT wired:
`Assets/Models/Kenney/Particles/` and `Assets/Materials/TracerSprite.mat`.
Do not tidy unless he asks.

### Two lessons 09-04 kept re-teaching

**An instrument beats a story.** I was sure L6's boss was unreachable —
nine powers, nothing killed, and a documented hole in rule 7 to blame.
Rule 9 cleared L6 on its first run. The bug was my aim. It then found a
REAL unhittable man on L10, which no amount of playing had suggested.

**A silent exemption is where things hide.** Rule 8 had waved
clears-on-first-march through with a bare `continue` for as long as it
had existed. Removing it surfaced a shield bearer standing 0.07 inside
L9's bunker. Nothing would ever have reported that.

### Closed 09-04 — NO UNIT STANDS INSIDE A BUILDING, and the exemption that hid two

Rob: *"i dont think we should have enemy units within the buildings...
that doesn't make sense."* Rule 8 now ERRORS on an advancing unit
inside a collision box, however fast it marches clear.

**This overrides a deliberate split.** Until now, clears-on-first-march
was waved through SILENTLY, clears-eventually was a Warning, and only a
static embed was an Error. That split reasoned about HITTABILITY and on
that axis it was right — but it answers the wrong question. A man
standing inside masonry is not a pacing judgement, and no march he makes
next turn changes what the player sees on the turn he arrives.

**Nothing is lost: rule 9 carries the hittability half and carries it
better**, firing real shots and following an advancer march by march.
Rule 8 is now free to mean the simple thing its name says.

**The tightening found two cases the old split had hidden:**

- **L12's boss escort** spawned at anchorX 5.5, inside the GATE's box
  [3.25, 5.75]. The citadel does not count — it is the phase's own
  trigger and rubble by then — but the gate is a separate structure
  still standing. Moved to **7.0**, with the **Sovereign 7.5 -> 9.0** to
  keep the 2.0 gap the design notes asked for so the two groups do not
  stand on each other. Deeper into the citadel footprint is where the
  beat always wanted them: emerging from the breach.
- **L9's charge group** — the one worth remembering. A shield bearer
  **0.07 of a unit inside the mountain bunker**, exempt for as long as
  the rule has existed because it cleared on its first march. Moved
  anchorX **3 -> 2.8**. A silent exemption is exactly where that hides.

**Red before green: 2 offending levels, exit 1**, with both named. Green
after. **Campaign now 2 warnings, 0 errors** — the only two left are the
long-standing L3 rule 7 and L5 separation.

One bug of my own, caught by the report disagreeing with itself: the
first cut of the change dropped a `continue`, so an advancing unit was
counted in BOTH buckets and L9 was reported under the static-embed
message. The numbers not adding up is what showed it.

### Built 09-04 — RULE 9, the ballistic-shadow checker, and it found a shipped bug

Offered twice, declined twice as theoretical, then earned. **L6's boss
phase was played three times and nothing in it could be killed, at NINE
distinct powers spanning the whole envelope (45 to 88%).** Difficulty
does not look like that, so the standing suspicion was that the
Sovereign was unreachable — `CLAUDE.md` had already written the hole
down: *"rule 7 still reads turn 0 only; an arrival placed out of the
ballistic envelope is caught by nothing."*

**`LevelComposition.BallisticShadowRule` FIRES THE SHOT.** A sweep of
real trajectories through `TrajectoryPhysics.Step` at the tick's own dt,
against `CollisionSystem`'s own boxes and `SweptCollision.UnitHitRadius`,
counting how many land on the man. Zero is an ERROR, a handful is a
NEEDLE warning. Turn 0 and every arrival, via rule 8's `ArrivalSets` —
`DeadByTrigger` included, or every boss in the game would be condemned
for the rubble it bursts out of.

**IT CLEARED L6.** The Sovereign IS reachable. **My hypothesis was
wrong and the instrument said so** — three lost boss phases were aim,
not a bug, which is exactly what the checker was built to settle.

**It found a real one on L10.** The turn-4 heavy wave has **one man who
cannot be hit by any drag**. The depot sits at x 8 with a `hitWidth` of
3.75, so its box ends at **x 9.875** and stands 1.25 tall; the leftmost
heavy lands at **x 10.2** — a third of a unit past the far edge.
Clearing the box there and dropping to head height needs ~70° of
descent, and 70° carries only 14.5 units when he is 17.4 out. **The
other three, further out, ARE reachable**, which is the shadow behaving
exactly as the geometry says and why nothing saw it by eye. The wave is
`advancePerTurn: 0`, so he never walks clear.

**Corroboration worth trusting.** On L12 rule 9 reports the boss shield
escort *hittable after 2 marches*, from fired trajectories — and rule 8,
measuring box geometry by a completely different method, independently
says *2 turns to clear*. Two unrelated instruments on the same number.

**FIXED 09-04: the depot moved x 8 -> x 7.** One structure, one unit
left. The wave did not move, no count changed, separation is 16.5 and
still inside the 14-20 band, and **all nine rules pass**.

**The wave was never the thing to move, and the search proved it.**
Behind the depot there is no good spot at all: the shadow reaches to
about x 11.4 and rule 7's comfortable band ends right about there.
anchorX 12.1 is still shadowed; 12.4 clears rule 9 and immediately
raises rule 7's back-rank warning; 13.0 the same. **The two constraints
close the gap between them.**

**Giving it an advance does NOT work here, which is worth knowing.**
Advancing moves a unit TOWARD the player — L12's escort clears because
it starts INSIDE a box and walks out the near edge, but L10's heavy is
behind the FAR edge, so a march walks him INTO the depot for several
turns. The two cases look identical in a report and are opposites.

Shortening the depot was rejected: `GarrisonPost` is shared with L2 and
L8, so it would ripple. Moving the placement fixes the CAUSE — the
shadow — without touching anything shared. x 7.4 still leaves a needle;
7.0 is clean.

**Rule 9 is now wired into `PortSelfTest`**, delegating to
`BallisticShadowRule` exactly as rule 8 delegates to `CollisionBoxRule`
— one implementation, not two. Wired AFTER the fix: landing a red suite
on shipped content ahead of the fix teaches the next person to ignore
it. **Red before green confirmed** — the depot put back at x 8 fails the
suite with the L10 heavy named, exit 1.

**Campaign now reports 4 warnings, 0 errors.**
`LevelComposition.Report` now exits 1 on that error, which is the
checker doing its job and not a regression.

### L6 TESTED PROPERLY 09-04 — three defeats, and the spike has a name

The owed picker play, done: RETRY into L6's own picker, **RIGS test
supply so nothing came off Rob's inventory**, 8/8 troops and 14/14
points (3 Rifleman + 3 Grenadier + 1 Sniper + 1 Rocket), Airstrike
and Early Reinforcements carried, Incendiary on decks and AP on the
boss. **It still lost.** But the two halves of the level tell
completely different stories, and that is the finding.

**The garrison half is well tuned.** Four volleys — 77% on the
bunker, 88% on the keep deck — took 27 -> 9, destroyed both
structures, and cost two men. The grenadiers shred a packed deck
exactly as the picker text promises. Nothing to do here.

**The boss half is where the level is decided, and it is a cliff.**
From 8-9 units the trade was roughly **1.5 losses per turn for 0.25
kills**. Across two full boss phases I killed ONE thing.

The mechanical reason: **the boss phase is a MOVING-TARGET problem in
a game that has taught nothing but static ones.** Power had to walk
`88 -> 77 -> 64 -> 54 -> 50 -> 45` as they closed, there is no
distance readout, and each wrong guess is a wasted turn costing two
men. In run 2 the band was found by RECORDING a volley and reading
the impact off the screen — 54% landed on them — and by the next turn
the band had moved again.

And the trigger compounds it: **the keep falling spawns the Sovereign,
so the level ambushes the player for doing the thing it just taught
them to do.** That is the intended beat, but it lands when the army
is spent.

**Limits of this result, stated plainly.** One player, three
attempts, and two of them had a clear aiming error. The pre-boss half
is repeatable and easy; the post-boss half won even when aimed
correctly with a saturated loadout. **The spike is real and it is
entirely in the boss phase.** L6 has still never been won.

### Closed 09-04 — pillar 7 reaches the boss at last

Rob: *"it's worth fixing."*

`ReinforcementWaveBeat` refuses to let a four-man squad skip its
warning — *"a wave is not allowed to opt out of it"* — and L10/L11
both carry two-turn leads. **Meanwhile the Sovereign, 260 hp and a
heavy escort, arrived on L6 and L12 with nothing at all.** Not a
decision: a boss fires on a STRUCTURE FALLING rather than on a turn,
so it was never wired to the telegraph strip.

**A boss cannot borrow the waves' countdown.** A wave has
`arrivesOnTurn`, so "2 turns" is a fact; the player owns the boss's
clock and a number would be a lie. What can honestly be warned is
PROXIMITY TO THE TRIGGER, so it is a HEALTH THRESHOLD on the
structure gating the phase, and the line carries no number.

- `BossPhaseTrigger.telegraphLabel` + `telegraphAtHealthFraction`
- `EventSystems.ShouldTelegraphBossPhase` — 0 is EXCLUDED, not
  clamped: the gate is already down, the phase fires that tick, and a
  warning arriving with the thing it warns about is not a telegraph
- A wave keeps the strip when both want it. Its deadline is fixed and
  the player cannot move it; the boss's stays true as long as they
  leave the gate standing.
- Both campaign bosses authored, ASCII only (the em-dash
  missing-glyph trap is what bit the one shipped wave telegraph).

**Red before green: `2 of 2 campaign boss phase(s) have no
telegraphLabel: L6, L12`** — which is exactly the state the game
shipped in. Device-verified on L6: **`Something moves behind the
keep`** in the strip at Fortress Tier 33/139, phase not yet fired.

**The threshold is 0.5, and 0.35 was wrong — measured.** One volley
took the keep **137 -> 33, 75% of the structure in a single turn**. A
narrow band is not crossed slowly, it is JUMPED: the first run of the
day never showed the line at all because the keep passed straight
through it. Widening does not buy more turns against a heavy volley,
which can still clear the whole band at once — **it buys the warning a
chance to fire against the lighter ones.** The strip was
device-verified at 0.35; 0.5 only widens the band on the same code
path and render.

**NOT claimed: that this makes L6 winnable.** It removes the
blindside, which is a pillar violation worth fixing on its own terms.
Whether the boss phase is still too steep is unmeasured, and L6 has
never been won.

### Closed 09-04 — the flail INTEGRATED, and the snow pine had no snow

Two reports off L7, unrelated, both mislabelled at the source.

**"Their arms are spinning out of control."** Real, and worse than it
looked. `UnitAnim.Wave` composed onto `t.localRotation` — its own
previous value — every frame:

```
t.localRotation = Quaternion.Euler(...) * t.localRotation;   // WRONG
```

That reads as additive, and the aim lift does exactly the same thing.
**The difference is what is underneath.** The aim lift sits on a clip
that rewrites the joint every frame; a CORPSE HAS NO CLIP PLAYING —
`Set(Die)` stops all of them — and `LateUpdate` deliberately skips
`RestoreStance` for a flailing body. With nothing re-establishing a
base the multiply INTEGRATED, so the longer the fall the faster the
spin. L7 drops them off a building, which is why it showed there.
`ApplySlump` had the identical defect on torso and head, and its
easing gives the intent away: `rise = 1 - exp(-8·age)` converges on a
FIXED fold, which means nothing if each frame stacks on the last.

Both are offsets from a captured rest now (torso and head needed
their rest capturing; the limbs already had theirs).

**Red before green, with numbers: 179.9° on `arm-left` — fully
inverted — against the old code, 20.5° with the fix**, which is the
true composed amplitude of an 18° X / 9.9° Z flail.

**Why nothing caught it, and the gap it leaves.** Every other ragdoll
check in `PortSelfTest` tests `CosmeticSystems` — the ragdoll's
PHYSICS, engine-independent and easy to assert. Its POSE lives in a
MonoBehaviour and was covered by NOTHING. The new
`CheckAFallingBodyDoesNotWindUp` drives the real `LateUpdate` on a
real prefab through reflection, because **asserting the arithmetic in
isolation would have stayed green** — the offset was always bounded;
it was the COMPOSITION that diverged. Assume the same hole exists for
anything else `UnitAnim` writes.

**The green tree was not a colouring bug.** `keepColors: 1` was
working correctly and faithfully drawing the tree it was handed:
**`prop_snow_pine.glb` had no snow in it.** Three materials — `bark`,
`needle`, `needle_dark` — and its needles were merely a DARKER GREEN
than `prop_pine.glb`'s. A snow pine by filename only, and the
filename is what everyone had been reading.

Rebuilt: two green tiers with white cones sitting on them, a rim of
green showing beneath each. At mid-ground scale (L7 plants it at
z -8.5) snow has to be a COLOUR BLOCK, not a dusting. Blue-white
`0.90, 0.93, 0.97`, not paper-white — the timberline backdrop is
already pale and a 1.0 white flares against it. Device-checked on L7.

**The builder is `tools/blender/build_snow_pine.py` in the RETIRED
repo**, where CLAUDE.md says builders live — there was none for the
pines, they had been authored ad hoc, which is how one shipped
misnamed. It is left UNCOMMITTED there, alongside the other builders
already sitting uncommitted on `projectile-refinement`; that repo is
not maintained. **The GLB it produces is committed here.**

### Closed 09-04 — L6's last two-rank deck, moved rather than cut

The residual above, fixed the same sitting. **The bunker and the keep
are garrisoned by the SAME unit definition**, so the fix was never a
unit-count call at all — it is two men moving four units right:

| group | deck | seats | was | now |
|---|---|---|---|---|
| `bunker` (MountainBunker) | 1.00 | 6 | 8 | **6** — 100% fill |
| `keep` (FortressTier) `83aeca…` | 3.00 | 17 | 4 | **6** |
| `keep` `036d9a…` | | | 10 | 10 |

**Enemy count stays 27. No definition, stat or new group.** That is
strictly better than the 8 -> 6 first proposed, which would have made
a boss ~7% easier to buy a legibility fix.

**All 21 campaign decks now stand in ONE rank** — the staggered path
is gone from the shipping product. So the check's floor was raised
from `body * 0.3` (0.039) to a **full body width** (0.131): adjacent
men may not overlap at all. **Run against the old level first and it
went RED** — 7 bodies hidden, tightest gap 0.094, exit 1 — then green
with the level fixed, tightest gap now 0.152 on L1. Re-introduce a
staggered rank and it goes red again, deliberately.

`LevelComposition.Report`: L6 **all eight rules ok**, unchanged, and
the campaign's three standing warnings (L3 rule 7, L5 separation
11.6, L12 rule 8) are exactly as before. `DeckFillReport`:
MountainBunker 79% -> **100%**, FortressTier 85% -> **98%**.

**Device, 09-04 APK.** Both L6 decks read: six countable men on the
bunker, sixteen on the keep, every one distinct. The before/after on
the bunker is the whole story — four merged pairs, then six men.

**WHAT THIS PLAY DID NOT ESTABLISH, and it matters.** I threw ten
near-identical blind drags and lost at turn 9 with the **Mountain
Bunker untouched at 118** — I never once aimed at it. That defeat
measures the drag, not the level. The case for neutrality is
STRUCTURAL — same count, same definitions — with one honest caveat:
the two men moved from a shorter structure at x=4 to a taller one at
x=8, so they are marginally further out, and a shell on the keep now
catches 16 where it caught 14.

### L6's BALANCE IS UNSIGNED — two defeats that do not count, 09-04

**L6 has never been played to a win, before the deck move or after.**
Two attempts, both defeats, and NEITHER is evidence about the level.
Recorded here so the third attempt does not repeat them.

**Attempt 1 — ten blind drags at 87-88%, defeat T9.** Never aimed at
the bunker; it finished untouched at 118.

**Attempt 2 — a real line, defeat T12, one unit against nine.**

| turn | shot | result |
|---|---|---|
| 1-4 | 76%, at the bunker | bunker destroyed, 27 -> 21 |
| 5 | 76% again | WASTED — empty ground where the bunker had been |
| 6-8 | 88%, at the keep | 21 -> 12, keep to 5 hp |
| 9-12 | keep falls, boss phase | 12 -> 9, mine 5 -> 1 |

**The range arithmetic is exact and worth keeping**: `range =
22.56 x power^2` (v = 9.5·power, g = 4), tank at x -9.5. So the
**bunker (x 4, 13.5 out) wants 76-77%** and the **keep (x 8, 17.5
out) wants 88%** — and a drag of 331 px on each axis reads as ~87%,
so px ≈ 331 x (power / 0.875). Confirmed on the HUD's `Last:` line
both times.

**Why neither defeat counts.** Three things a real player has that
neither attempt used:

1. **Standard ammo on all twelve volleys.** Incendiary, AP and
   Cluster sat unused in the HUD the whole battle.
2. **No consumables** — RIGS was off, so spending would have cost
   Rob's real inventory.
3. **The stepper SKIPS the picker**, so both attempts fielded the
   AUTHORED DEFAULT squad against a `deployBudget` of 14. A player
   entering L6 through the picker fields a stronger army than either
   attempt did.

**Auto cannot close this** and was declined when offered: L6 is won
or lost on STRUCTURES, and Auto targets the nearest enemy UNIT and
builds its own volley — it would never put a round on the bunker,
exactly the failure attempt 1 made by hand. An Auto win here is a
green that cannot go red.

**What the play DID establish: the beat lands.** Dropping the keep
feels like winning, and then the Sovereign walks out of the breach.
It caught me twice — once not knowing it was coming, once knowing.
`PRODUCT_DIRECTION.md` asks beat 6 for a stage boss whose level does
not end when the structure does; it delivers.

**Owed: one picker entry with ammo in play.** Not another twelve
mechanical drags.

**Also learned, and it bit mid-session: the ◀ ▶ stepper is RELATIVE,
and the app RESUMES AT THE LAST-PLAYED LEVEL.** Five ▶ taps from a
fresh launch landed on **L11, not L6**, and a live volley went into
L11 before the level indicator was read. Nothing persisted — a
stepper reload discards it — but **read `L? (?/12)` before firing,
never count taps from an assumed start.**

### Played 09-04 — L6 on the 09-02 APK. The fix holds; ONE deck still does not read

The owed beat from the 09-02 list, and it closes it. **The one-rank
fix is signed by play**: L1's outpost shows ten countable men and
**L6's FortressTier shows fourteen in a single rank**, each one
distinct — a 3.00 deck at 85% fill and the best-reading garrison in
the game. Difficulty is unchanged as predicted (no count moved):
turn 1 took the enemy 27 -> 19, turn 4 sat at 15 with all ten player
units alive, and the Fortress collapsed to rubble and flame by turn 5.
Route: L1 picker -> BEGIN -> stepper to L6, stock loadout, four real
drags at 87% / 45°.

**What the play found, and no check can see.** L6's `MountainBunker`
— the campaign's ONLY two-rank deck, and the one case the new code
path exists for — **still reads as four men when it holds eight**.
At 1:1 on the phone: four. `DeckFillReport` says 8 in 2 ranks. The
other four only resolve at 10x zoom, where each "man" turns out to
be a PAIR sharing most of a torso, two heads out of one red mass.

The numbers say why. The stagger is half a pitch — `0.094`, which is
**72% of a body width** (`0.131`) — and the rear man is also 0.16
further back, so he is drawn slightly higher and smaller with his
feet behind his neighbour's shoulder.
`CheckEveryGarrisonBodyReadsOnScreen` passes it because its floor is
`body * 0.3` = `0.039`, **4.3x more permissive than the gap it is
measuring**.

That floor is not wrong about the bug it was written for — 0.000 vs
0.094 is exactly the red-then-green the fix was proven with, and 18
of the 19 decks genuinely do read. But it asserts NOT PERFECTLY
ECLIPSED where the name on the tin says CAN BE COUNTED. **It is the
house lesson in a new costume: it tests the input (is there a gap),
not the output (can a player count the men).**

**Not fixed, and deliberately not.** The cheap fix is not code.
`MountainBunker` is a 1.00 deck seating 6 per rank; **L9 puts exactly
6 on the same structure and gets 100% fill in one rank**, while L6
asks it to carry 8. Dropping L6's bunker 8 -> 6 makes the whole
campaign single-rank and deletes the staggered path from the shipping
product. That moves a unit count on a BOSS level, so it is Rob's call
and was left alone. If the 8 is kept instead, the check's floor wants
to mean countable (a full body width, not 0.3 of one) — but that
would go RED against L6 today, so it ships WITH the level edit, never
before it.


### Ask which beat — SUPERSEDED 09-04

The 08-28 and 09-02 lists are both spent; every item on them was taken
or closed. What is owed now is at the top of this file, under **Owed**.
Kept only so a reader following a back-reference lands somewhere true.

### Closed 09-02 — half of every garrison was INVISIBLE

Picked as the next beat and it was not the bug it was reported as.
L5's bunker deck showing 4 of 8 (and L6's, and L9's) is not an
undercount, a pool fault or a render fault. Every man was built,
placed and drawn. `Formation.Mounted` laid a garrison in two ranks
of equal size, which puts **every rear man at exactly his front
man's x**, 0.16 behind him — and at the camera's real height that
is 0.024 of screen rise, 5% of a 0.48 body, behind a shoulder
0.131 wide. Two ranks of four render as four men.

**It was the whole campaign, not three levels: 85 of 178
garrisoned bodies hidden across 19 decks, every one at a column
gap of exactly 0.000.** That number is the check going red against
the old code before the fix was trusted.

**The fix.** One rank until the deck runs out — which is what the
reference measurement always said (*castle tiers pack two ranks
UNTIL THE BODIES OVERLAP*); the `while` loop that grows the rank
count IS that sentence and the old code just never let it start at
one. A genuinely-needed second rank is now STAGGERED half a pitch
so it reads between shoulders. Unfolding the ranks then exposed a
second error: the deck clamp compared CENTRES against the full
roof, so a single rank of ten hung 109% of L1's outpost and 113%
of L6's bunker. `Formation.BodyWidth` is a real constant now and a
rank is laid into `width - BodyWidth`.

**This reverses a deliberate 2026-08-02 decision** ("rank depth
flipped back to two") and the argument is written up in
`UNIT_VARIETY_DESIGN.md` Tier 2.2 part five, not just applied.
Short form: that flip was right that the clump read was a SPACING
problem, and wrong to assume a camera that can see depth. Spacing
is untouched.

**It also closes the deck-FILL question** that document has
carried since 08-02, for free and without a roster call. Campaign
median fill 34% -> 70% against a 75% target; L1's outpost 59% ->
100%, L4/L9's barracks 47% -> 97%. The garrisons were never too
small for their decks — they were folded in half. **This is not
licence to spread a row**; nothing was widened, the row was
unsplit.

**Verified.** `PortSelfTest` ALL PASS, including a new
`CheckEveryGarrisonBodyReadsOnScreen` (0 hidden over 178 on 19
decks) whose depth premise is asserted rather than commented.
`LevelComposition.Report` byte-identical before and after — the
three standing warnings (L3 rule 7, L5 separation 11.6, L12 rule
8) are unchanged and pre-existing. `DeckFillReport` shows 20 of 21
decks in one rank; the exception is L6's MountainBunker, eight men
on a 1.00 roof, staggered and countable. Device: L1's outpost deck
at turn 1 shows **ten individually countable men** where it used
to show five.

No unit, stat, count or level asset moved. Code-only —
`Formation.cs` plus the checks — so **no scene rebuild was
needed** and none was run.

### Closed 09-02 — L5's angle claim retired, the level untouched

The 09-01 design question, answered by MEASUREMENT and signed by
Rob: **the notes move, the level does not.** No code changed, no
level data changed, no rebuild — `TowerAssault.asset` designNotes
only. `PortSelfTest` **ALL PASS**.

The 09-01 play was one line through the level, so it could not
prove the angle was free — only that one angle worked. So the
ballistics were rebuilt outside the engine (g=4, v=9.5×power,
semi-implicit Euler at dt=1/60, the real structure AABBs and the
0.38 unit hit radius) and **calibrated against the twelve recorded
throws before being trusted**. At a launch x of **−8.6** it
reproduces the whole line: 78% → bunker face, 82% → deck kills,
86% → tower base once the deck is empty, 88% → collapse. Only
then was it swept.

| angle | power band that kills the deck | band that hits the tower sniper |
|---|---|---|
| 25° | 93–100% | — |
| 30° | 87–95% | — |
| 35° | 84–91% | — |
| 40° | 82–88% | 97–99% |
| **45°** | **81–87%** | 95–97% |
| 50° | 81–88% | 95–96% |
| 55° | 83–89% | 96–98% |
| 60° | 87–93% | 99–100% |
| 65° | 92–99% | — |
| 70°+ | out of range | — |

**Every angle from 25° to 65° wins the deck.** A 40-degree-wide
legal window is not a skill axis; the ~7-point power band at each
angle is. The level teaches power precision an order of magnitude
more sharply than angle, so the old line — *"the first level where
the ANGLE matters more than the power"* — was backwards, not
merely unproven.

**The cause is geometric and it generalises.** A round clears a
structure's leading edge at `tan(angle) × half-hitWidth`. The
bunker is 2.5 wide and 1.125 tall, so its half-width (1.25)
already exceeds its deck height and **45° clears the face at any
power**. Forcing a loft anywhere needs deck height GREATER than
half-width. Do not reshape L5 to get it — the 2★ balance was
walked back twice to land, and `PRODUCT_DIRECTION.md`'s chart asks
L5 for *elevation / deck fight*, never for angle. It delivers the
beat it was assigned.

**Sniper footnote, same sweep.** The tower rider IS directly
hittable, but only in a 2–3 point needle (95–97% at 45°) — the
platform box shadows him. In practice he is a COLLAPSE target,
which is what the goal text already said. That explains why the
09-01 run killed him without ever hitting him.

**Offered and NOT taken:** extending `LevelComposition` with this
shadow rule, since rule 7 reads flat range at 45° and would pass a
garrison that is a needle or unreachable. Rob took the notes-only
option. Do not build the checker unasked.

### Played 09-01 — L5 through the picker, WON 2★ (the owed beat)

Beat 1 of the 08-28 list, and it closes it. Played on the 08-28
APK, no rebuild. Route: L1 picker → BEGIN → stepper ▶ to L5 →
throwaway defeat (short shots into the dirt, T14, +16) → **RETRY**,
which is the only path that opens a level's OWN picker. `EnterLevel`
raises the picker; the ◀ ▶ stepper calls `LoadLevel` and skips it.
Stock, Standard, **no Airstrike taken** — RIGS was off, so spending
it would have cost Rob's real inventory.

**The picker reads exactly as authored: 10/10 troops, 13/13 points,
7 Rifleman + 2 Grenadier + 1 Sniper.** Both caps saturate at the
default mix — there is no room to re-compose without dropping a
body or wasting a point. That is the walk-back landing coherently.

**The line as thrown** (45° throughout, power varied):

| Turn | Power | Result |
|---|---|---|
| T1 | 78% | **wall, not deck.** Bunker 137→113, no kills |
| T2 | 82% | **deck loft — enemy 12→6.** Tower base 135→101 |
| T3 | 83% | 6→4, **first casualty 10→9** (the sniper) |
| T4 | 83% | no kills — survivors outside the box. Bunker 89→63 |
| T5 | 67% | ground trio, 4→3 |
| T6–7 | 66–67% | 3→1, player 9→8 |
| T8–10 | 86% | tower base 67→19 |
| T11 | 86% | tower base 19→**1**, player 8→7 |
| T12 | 88% | **collapse. 1→0. VICTORY 2★**, 7/10 alive, +215 |

**The three things the beat asked, answered:**

- **Ten packed bodies read as MEN, not a wall.** The Aiming frame
  shows two legible fire teams — 7 riflemen left, the bulkier 2
  grenadiers + sniper right — separated by the anchor gap
  (−7.4 / −5.9 / −4.6 at packing 0.8). The class silhouettes
  are distinguishable at 6°. The walk-back did what it was for.
- **The T6 wipe is GONE.** 08-25 at 13 bodies: bunker down T6,
  13 standing, ZERO casualties — the overshoot that caused the
  walk-back. At 10: first casualty T3, three dead by the end,
  **2★ not 3★** (*"Lost 3 of 10 — keep 8 alive for 3 stars"*).
  The level now costs something. **Do not walk it back further.**
- **The tower collapse is literally the goal text.** *"Cut the
  tower's legs — what stands on it falls with it."* The base fell
  and took the platform AND its rider: enemy 1→0 without the
  sniper ever being hit directly. Both Tower lines leave the HUD
  while Command Bunker stays at 37 — it never had to fall, because
  the win is units dead.

**The honest limit, and it is a DESIGN finding.** L5's designNotes
say *"the first level where the ANGLE matters more than the power."*
**This run never changed the angle.** Every one of the twelve
volleys was 45°; the whole level was solved by walking power
78 → 82 → 67 → 86 → 88. What the level actually teaches is a
POWER lesson with an unusually tight band: **78% hits the wall
face for 24 chip damage and 82% clears onto the deck for six
dead** — four points of power between nothing and the level.
That band is a good beat. But it is not the angle beat the notes
claim, and one of the two should move. **Ask Rob which.**

Smaller, unresolved, not chased:

- **The bunker deck shows 4 bodies where 8 are authored** — the
  same undercount recorded 08-25 for L6 (4 visible on a deck
  authored for 8) and L9. Third level, same shape. Not looked at
  with CAM this sitting.
- **The sniper died T3** having visibly delivered nothing. The
  ledge placement and `flatTrajectory` are untested by this run.
- Ballistics model that predicted every shot, for the next play:
  power% is v/9.5, g=4, so a 45° round reaches height
  `y = x − 4x²/v²`. 331 px per axis = v8. It matched the game on
  all twelve throws.

### Played 08-27 — L4 shells=3; L5 bodies walked back

Beat 1 of the owed list. Override is 3. PlayerTank stays at 5.
Played on device: stock 8 riflemen (L1 picker → stepper to L4),
Standard, HOLD T1–T4 then ARM T5–T7. **Defeat turn 12.**

- Magazine on L4 Aiming: 3 pips, `Tank shells: 3`. `[Cannon]`
  HOLD then ARM both logged. `PortSelfTest` ALL PASS.
  `BalanceAudit` L4 240 vs **288**. `DISPLAY=:1` is gone; `:0`.

**The HOLD-then-ARM line, as thrown:**

| Turn | Gun | Result |
|---|---|---|
| T1 | HOLD 62% | miss, 10 v 27, barracks 150 |
| T2 | HOLD 68% | 10→9 / 27→26 |
| T3 | HOLD 56% | miss, 9 v 26 |
| T4 | HOLD 69% | 9→8 / 26→25 |
| T5 | ARM cluster 76% | **shell missed the wall**, 150→142, 8 v 25, 2 left. Chargers on the right edge of Aiming. |
| T6 | ARM deepest 84% | melee, 8→3 / 25→13, barracks 142→138, 1 left |
| T7 | ARM deepest 83% | **shell hit**, 138→36, 3→2 / 13→12, SPENT |
| T8–11 | dry | rifle chip 36→32→30→28→24, 2→1 |
| T12 | — | **0 v 11**, barracks 22, outpost 90 |

Fail card: *"The garrison is still firing — bring the building
down."* +24 (4085→4109). Same card as 08-24 run 1 and the 08-25
five-into-dirt throwaway. Correct.

**Honest limit of this play:** 2 of 3 shells did not hit a wall
(T5 +8, T6 +4). Only T7 delivered the 96. A HOLD that then
lands all three on the barracks would still be 288 vs 240 —
this run does **not** prove 3 is too tight, and it does not
ask to bump it. The 08-24 raze-from-T1 at 3 (auto-fire, no
panel) was the coin-flip; this is the other line, thrown
crooked. Leave the override. Do not widen the aim frame.

**L5 bodies walked back, packing kept.** Riflemen 10→7 (squad
13→10), `deployBudget` 16→13, `playerSpacingScale` **stays 0.8**.
The 08-25 grouping read as men; the extra three bodies were the
T6 wipe (13 standing, zero casualties). Authored mix is 7 rifle
+ 2 grenadier + 1 sniper = 13/13. Sniper ledge/flat/miss-long
untouched. Tank stays off. `PortSelfTest` ALL PASS — ten bodies,
line 1.20 wide vs 1.49 at scale 1. `BalanceAudit` stock race
**0.7x** again (player 3.0 / enemy 4.5, HP 288). That is the
pre-ease arithmetic; packing still concentrates the volley.
**Play through the picker still owed** (see pick-up). The APK
has the walk-back; do not rebuild just to play it.

**Elbow kept.** Rob: *"elbow is fine, let's keep it."* Phase 2:
all seven GLBs have the child joints, scene rebuilt. Gameplay
Aiming is still the hold-the-gun read; close CAM showed two
hands on the rifle. Not a new silhouette at 6°, and that is
accepted.

**Last-aim on the phone.** After an 86% / 45° drag, T2 HUD
reads `Last: power 86%    angle 45°` under Your turn. Matches
the volley log. Outpost down, 14→4, shells 5→4.

### Signed 08-25 evening sitting

- **Default ARMED.** Keep it. `GAME_DESIGN_LOCKS.md` updated.
  The panel teaches the hold; flipping the default is not taste.
- **Flat sniper miss-long.** Leave it. L5 play took no hits; the
  54/60 vs 58/60 is characterisation. Per-class jitter is off
  the table unless a later play shows rounds sailing over the
  line as the problem.
- **Weapon hold is a carry.** Shipped. Elbow is a plan, not a
  LateUpdate tweak and not a clip rewrite.

### Played / looked 08-25 evening sitting — the numbers

**L4 at 5 shells.** Stock 8 riflemen via RETRY picker, Standard,
ARMED. Raze-the-buildings line. T1 barracks 150→42, T2 collapsed
(27→15), T3–4 outpost gone (15→7), **2 shells left**. Contact
turn 6 at **10 v 4**. T7 9 v 2. Then the tail: last two in the
WRECK, off-screen during Aiming. Free camera x −2 / y 4.4 / z
17.6. Deepest derived drag missed. Stopped turn 11 at 9 v 2.
**Readability, not a miss-fest. Do not widen the aim frame.**
Throwaway (stepper leftover 10, dirt, ARMED) died turn 8 on
*"The garrison is still firing"* +24. Five into the dirt is the
same dead end as three. Contact union holds player line +
chargers, tank in; next Aiming beat the chargers sit on the
right edge.

**L5 eased race.** Picker 13 (10 rifle + 2 grenadier + 1 sniper,
16/16), Standard, no tank. Packed line reads as men. Bunker
down T6, **13 standing, zero casualties**, then 13 v 3. Leftover
street MG off-screen during Aiming (same family as L4 wreck).
Sniper on the ledge is visible. Overshot.

**MountainBunker.** Handover said L6/L8; **L8 does not field
one.** Campaign: L6 x 4 (8 authored) and L9 x 4.5 (6 authored).
Both at y 1.20: garrison on the player-facing lip, not under
the roof. L6 A/B x 5.11 z 9.75: four red on the front edge at
y 1.20, same four from y 8.85. **Honest count: 4 visible on a
deck authored for 8** (`standWidth` 1). Overhead the rest of
the roof is empty, so the other four are packed into that same
lip, not hidden by it. Offset +0.33 holds. No model change.

**08-07 L9/L12 siege audit, re-run at 5 shells.** Headless
`BalanceAudit.Report`: 0 errors, 21 warnings. Stock siege **ok**
on every tanked campaign level (L9 229 vs 480, L12 280 vs 480).
Only remaining SIEGE DEFICIT is L5 (no tank). Device: L9 both
structures down by turn 4, 2 shells left; L12 both fortress
tiers gone by turn 6, shells spent, 9 v 10. **Neither is
unclearable at five.** Caveat: L9/L12 were played with L5's
carried 13, not each level's stock — the shells are the siege,
and stock L12 fields rockets besides.

### Tree

**Ask git** — but as of 09-01 the working tree is **CLEAN**, which
is a correction: the 08-28 handover described it as dirty from
08-25 through 08-28. That work is committed as `c9b8d40`
(*"L4 at three shells, L5 walked back, mid-90s dash"*). Three
commits sit **UNPUSHED** on `session/2026-08-25-shell-art-ragdoll`:
`c9b8d40`, `8e6ba38`, `4d61ffc`. Untracked and not wired: Kenney
`Particles/` + `TracerSprite.mat`. Do not push, commit or tidy
unless he asks.

**This file is 3000+ lines.** 08-05/06 is in
`HANDOVER_ARCHIVE.md`. 08-13 → 08-21 is closed history and
the obvious next split — **ask before archiving**.

### What the 08-25 CODE sitting shipped — the index

Every one has a block further down with the numbers and the traps (5
and 6 share one). **The blocks are in REVERSE order below** — newest
first, with the L4 play at the top because it is the oldest debt and
the one that started that sitting. This list is only so a new session
can find them.

1. **L4 played honestly, twice** (the 08-24 debt). Both defeats, and
   they fail differently. Found: **the tank shells ARE the demolition
   budget, and the level's own goal text spends them.**
2. **The shell is ARMED or NOT ARMED, and the tank carries 5** —
   `shellsOverride` per level, and a magazine panel pinned UNDER THE
   TANK that exists only during the aim.
3. **L5 eased** — 13 bodies in a line packed by the new
   `playerSpacingScale` (0.8), sniper moved to the ledge and given
   `flatTrajectory` so it shoots a direct 12-20 degree line.
4. **"The units are too dark" was a BACKWARDS KEY LIGHT** — every
   camera-facing surface was lit by AMBIENT ALONE. How long that had
   been true is not established; the same stale angle was sitting in
   five other scene builders and previews. Gear and skin lifted after.
5. **The soldiers have faces** — the helmet brim was burying a nose,
   jaw and ears that were already modelled. Blender, crown untouched.
6. **They hold the rifle with BOTH HANDS** — arm correction plus moving
   the weapon inboard; neither works without the other.
7. **Ground deaths are knocked BACK** — they used to be thrown toward
   the enemy, into the fire that killed them.
8. **Garrisons were hidden by their own roof**, not by a barrier —
   `deckStandZOffset` on five structures.
9. **The corpse "glitch" near the ground was the settle spring
   snapping** — capped at `FlopMaxSettleSpeed` 120.

**Two new instruments, both kept:** `UnitPosePreview.Shots` (renders
the shipped prefab sampling the shipped clip — ~2 min against ~8 for a
device round trip) and `RagdollProbe.Run` (prints a dying body's own
numbers per tick). Each one caught a defect in its first run that the
code read as innocent, and each caught MY OWN mistakes too — read their
docstrings before reusing them.

**L4 WAS PLAYED — 08-24. The debt is paid; read what it found.**
Fresh build, fresh install, stock 8 riflemen, real drags, no Auto and
nothing fired into the dirt. TWO full runs, both DEFEATS, and the two
losses are not the same loss:

- **Run 1 — volley chases the charge. Blowout.** Aimed every volley at
  the advancing shield bearers, which is what `levelGoal` tells you to
  do. Dead on turn 9, having killed **5 of 27**. Fail card was right and
  read well: *"The garrison is still firing — bring the building down."*
  +24, and the coin line moved 4045 -> 4069.
- **Run 2 — volley razes the buildings. Lost on the last man.** Same
  squad, aim on the cluster instead. Barracks 150 -> 40 on turn 1,
  **COLLAPSED on turn 2 taking all 12 of its garrison** (enemy 27 -> 15)
  with zero losses to me. Checkpoint down by turn 16 (-7 more). Died on
  turn 30 at **0 v 1**.

**So: L4 is winnable-SHAPED and I did not win it.** Nothing here says
it is unwinnable; it says a stock squad playing the intended line ends
in a coin-flip on the last body. That is a verdict for Rob, not for me.

**THE FINDING — the tank shells ARE the level, and the level's own goal
text spends them for you.** 3 shells x 96 = 288 against 240 of
garrisoned structure HP, so the shells are the ENTIRE demolition budget
(`BalanceAudit`: *"siege ok"*). A rifleman does **8 x 0.25 = 2** to a
wall; 8 of them need ~10 clean volleys for the barracks alone while
dying at 1-2 a turn. The shell follows the volley's landing point — so
aim at the chargers on turns 1-3, exactly as *"Break the assault before
it reaches your line"* instructs, and all three shells go into the dirt.
**After that the level cannot be won by anyone, and nothing tells the
player.** Run 1 is that state, reached by obeying the goal text. Either
the goal line or the shell wants a look; **ask Rob which.**

**Also seen, unasked:** turns 16-30 of run 2 were a **14-turn 1 v 1
tail** with neither side landing a hit — ~10 volleys including a full
power sweep 200 -> 360 px, and the last enemy was never located. The
free camera was opened for it and the battle ended first. Nothing is
proven broken here; it is a pacing/readability smell at the end of a
level, and it is unmeasured.

**The 08-21/24 arc holds up in play.** The armour reads exactly as
costed: a full 10-round volley into the shield wall killed **exactly
one** (7 rounds-to-kill), and all 5 chargers reached the line and took
5 of my 10 with them — the guaranteed mutual trade, on schedule
(`BalanceAudit` predicted contact at 5.2 turns; it came on turn 6).
**Not yet looked at with eyes: the contact framing and the run gait in
a REAL play.** Both are signed, both were seen in the HUD, neither was
recorded. That is the one piece of this still owed.

**`BalanceAudit` called this level before the phone did.** It flags L4
*"2.7x BEHIND the race — drag this one"* and *"melee arrives before the
field can be cleared"*. Both landed. **`BalanceAudit.Drags` prints the
aimed swipe per level** — L4 cluster is
`adb shell input swipe 540 1150 242 1448 400`, deepest is
`540 1150 210 1480 400`. Use those instead of deriving a drag by hand;
mine agreed to within 5 px and cost an hour.

**Two things the file was wrong about, settled on the device:**

- **Uninstall/reinstall did NOT wipe the coins.** Android auto-backup
  restored them — the balance was 4045 before and after. The re-earn
  tax the RIGS note is costed against may not be real on this phone.
  RIGS is still the right tool; the reason for it is weaker.
- **RETRY returns to the LOADOUT screen, not the battle.** Three drags
  fired straight into the +/- steppers and silently rebuilt the squad
  as 6 riflemen + 2 grenadiers. Re-screenshot the loadout after every
  RETRY before firing.

**L4 gives a 14-point deploy budget and the default squad spends 8.**
Six points sit unspent on the screen the player is looking at. Not
touched, not judged — but the "stock" the balance audit measures is not
the strongest legal squad by a wide margin.

**THE CORPSE "GLITCH" NEAR THE GROUND WAS THE SETTLE SPRING SNAPPING**
(08-25, Rob: *"when the unit falls off of a building, when they are
at/near the ground, they seem to start glitching/going into some kind
of animation loop before they finally disappear into the ground."*)

**A corpse plays NO CLIP** — `UnitAnim.Set(Die)` stops every one of
them and its `dead` guard means it cannot re-trigger — so whatever
moves is the SIMULATION, and that is printable. `RagdollProbe.Run`
(new, kept) dumps a real deck-fall body's state per tick.

**The cause, measured:** a landed body ROLLS while it slides
(`ShouldRoll`/`StepRoll`), so it hands over to `StepFlopToSide` at an
ARBITRARY angle — up to 90 degrees from the nearest side-lie. At
`FlopSpring` 140 the spring crossed that in about an eighth of a second
and REVERSED on the way: **+57 deg/s of roll became -158 deg/s**, then
-231 in a later sample. That whip is the "glitch".

**Fixed with a speed ceiling, `FlopMaxSettleSpeed = 120`**, so a large
error eases over ~0.3s instead of snapping. The spring is not the wrong
model and its constants are right for the small errors a DIRT death
hands it (a ~20 degree lean); small errors never reach the cap, so the
tip-over is untouched. Measured peak 231 -> 120, still ending flat at
-90.

**Two fixes tried FIRST and reverted, because the probe said they
missed** — worth knowing so nobody retries them: dropping
`RollMinSpeed` to bleed the roll off (it made the handover error
WORSE, 32 -> 49 degrees) and bleeding the inherited opposing spin (the
spring's own error term dominates it). **The velocity was never the
problem; the spring's stiffness against a big error was.**

The new check drives a REAL rolling handover and asserts the fastest
turn the settle applies — the thing on screen — not the constant. Seen
red without the clamp (148 in that harness). **Its message names both
numbers**, because the 231 came from the full sim and the harness peaks
lower; a failure message that misstates its own figures is worse than
none.

**Device: a volley onto L4's barracks with the free camera parked** —
bodies thrown off the roof, arcing, landing in front, lying still. At
8fps a 0.3s settle is ~2 frames, so the device evidence SUPPORTS the
fix rather than proving it; the measurement is what proves it.

**GARRISONS WERE HIDDEN BY THEIR OWN ROOF — NOT BY A BARRIER**
(08-25, Rob on L2: *"you can't see them - they're behind a barrier or
something. let's make the barrier smaller... modify the model if
needed. check other levels for this as well."*)

**No model was changed and none needed to be.** The garrison stood
MID-DECK, and the camera sits at y 1.2 against a 2.5 deck — you look UP
at the building, so its own roof mass hides anyone standing back from
the edge. Proved with two frames at an IDENTICAL free-camera position
(x 6.1, z 8.35): at **y 1.20 the deck reads empty**, at **y 5.06 all
twelve men are there**.

Fixed with `deckStandZOffset`, which already existed for this and was 0
on every affected structure while the FortressTiers had -0.19/-0.80.
**+z is the camera side** — the builders put the front deck lip at
glTF +z and the rear cupola at -z — so positive moves the row FORWARD.
Set from real deck extents with a 0.30 margin: **GarrisonPost +0.955,
WatchTower +0.48, BarracksBlock +0.355, TowerPlatform +0.355,
MountainBunker +0.33.**

**MY FIRST SURVEY WAS WRONG AND THIS FILE'S OWN NEIGHBOUR SAID WHY.**
Ranking structures by "how far geometry rises above the deck", measured
off per-mesh BOUNDING BOXES, called the Outpost the worst offender at
3.4x body height. That number was its ROOF CUPOLA. For GarrisonPost the
tall mesh was `accent_GarrisonPost` — a JOINED mesh whose top is the
REAR cupola, nowhere near in front of anyone.
`StructureDefinitionSO.deckY`'s comment already says it: *"NEVER off a
node's bounding box. A bbox top is as likely to be a chimney, a guard
rail, a cupola or a damage chunk; that mistake stood four of five
garrisons in mid-air."* **Use `tools/measure_decks.py`, then LOOK.**

**The Outpost was left at 0 and verified fine** — its deck runs
REARWARD (model z -0.624..+0.176), so the row already sits within 0.32
of the front edge and there is nowhere forward to move it. The level
everyone plays first was never broken.

**Device-verified garrison visible: L1, L2 (the report, matched A/B),
L3, L4, L5. `MountainBunker` (L6/L8) is measured the same way but was
NOT looked at** — check it before assuming.

**Blender was NOT used and is still not connected** (`get_addon_status`
-> `source: "missing"`). The CLI at `~/blender/blender-5.1.2-linux-x64`
works if a model ever does need cutting.

**GROUND DEATHS ARE KNOCKED BACK NOW, NOT TIPPED OVER** (08-25, Rob:
*"when a unit is on the ground and they die, they just tip over. let's
make them get blown back but not as dramatic as the one falling off the
building."*)

**And the old one went the WRONG WAY.** `CosmeticSystems.ImpulseFor`'s
non-tumble branch threw at **`-sign`** — i.e. TOWARD the enemy — on
reasoning about shoving bodies off a building, in a comment that no
longer described that branch at all, since anything standing on a
structure takes the tumble path. A man shot on the dirt fell forwards,
into the fire that killed him.

Now `sign *`, the same "still backwards" convention the deck tumble
uses, with the magnitudes deliberately BETWEEN the old tip-over and the
deck fall — `RagdollKnockVx` 0.75-1.35 (tip-over was 0.35-0.80, deck is
0.90-2.10), `Vy` 0.30-0.70 (was 0.05-0.20, deck 1.60-3.40), spin
100-150 (was 70-110, deck 120-200). **No yaw or tilt spin**: the 3-axis
cartwheel is most of what makes a deck fall dramatic and is exactly the
half not wanted. At `RollFrictionPerTick` the throw carries about
`Vx / 2.3`, so a body slides ~0.3-0.6 units — a few body widths.

**Vy above `RagdollAirborneVy` (0.4) is SAFE here**, which is not
obvious: the flail is gated on `d.Tumble && RagdollAirborne(d)`, and a
dirt death is never `Tumble`, so it cannot reach the dramatic airborne
draw however fast it leaves.

**A PRE-EXISTING CHECK ASSERTED THE OLD, WRONG DIRECTION** — `dirt.Vx <
0f`, i.e. "dirt tips AWAY from the building". It was RETARGETED, not
deleted: it still guards the things that remain true (a dirt death is
flatter than a deck fall and does not cartwheel) and now demands the
same throw direction as the deck fall. **When a behaviour change turns
a test red, read the test before touching the code — this one was
encoding the bug.**

The new check asserts the DISPLACEMENT a real ragdoll step produces,
both sides, because the sign is mirrored per side and a one-sided test
passes on a bug that flips only the other. Seen red against the old
direction: *"player 0.53, enemy -0.53 (want opposite signs, player
negative)"*. Confirmed on device with the FREE CAMERA PARKED — it holds
through a volley, so the body's travel is unambiguous instead of being
chased across a panning frame.

**"THE UNITS ARE TOO DARK" WAS A BACKWARDS KEY LIGHT, NOT ART**
(08-25, Rob: *"i feel like the units are too dark... can we make them
more humanlike? use the blender mcp for this"*)

**It was never the albedo and Blender could not have fixed it.** The
camera sits at +Z looking toward -Z and every unit faces glTF +Z, so a
camera-facing surface has normal +Z. `SpikeSceneBattle` built the key
light at `Euler(50, -30)`, whose forward is **(-0.32, -0.77, +0.56)** —
a POSITIVE z, travelling from behind the army toward the lens. N.L on
every camera-facing face was -0.56, clamped to zero. **The whole army,
and the front of every structure, was lit by AMBIENT ALONE.**

**The tell is in any pre-fix screenshot and costs nothing to check:** a
bunker's horizontal TOP is bright while its vertical camera-facing
FRONT is nearly black — same material, so it can only be direction.
Fixed by yaw -30 -> **210**, which keeps the 50-degree pitch, mirrors z
to (-0.32, -0.77, -0.56), and leaves the GROUND untouched (normal +Y,
N.L unchanged at 0.77). Verified as a matched A/B on device.

**Colour edits in Blender would have been DISCARDED.** The GLB supplies
geometry and MESH NAMES only; the tones are Unity material assets that
`RiggedUnits.Tone` assigns by `skin*`/`accent*`/`trim*` prefix and
`FactionPaint` repaints per faction at runtime. Anything painted in
Blender is overwritten before it is ever seen. **Unit colour lives in
five places, none of them the model:** `PlayerUniform.mat`,
`PlayerGear.mat`, `UnitSkin.mat`, the faction assets
(`Redguard`/`IroncladLegion`, enemy only — `ApplyFaction` never touches
the player) and `Cosmetics`' camo entries (player only; Olive is null
and falls back to the build-time material).

**Second pass, after the light:** gear was the black mass — helmet,
webbing and boots all sat at ~0.17 and merged with the body into one
lump, which is the failure `UNIT_VARIETY_DESIGN.md` has already
recorded once ("the jaw merged with the collar into one dark mass").
PlayerGear 0.17/0.19/0.16 -> **0.27/0.29/0.25**, EnemyGear and
Redguard's gearColor -> **0.31/0.23/0.21**, Ironclad's -> 0.24/0.28/
0.34, UnitSkin 0.76/0.56/0.42 -> **0.85/0.65/0.50**. Hands now read as
flesh on the rifle and the helmet separates from the head.

**THE FACE AND THE HOLD ARE DONE (08-25) — see UNIT_VARIETY_DESIGN.md
Attempt 8 for the full account.** Helmet brim raised to HEAD_C + 0.02
in `build_units_rigged.py` (height 0.38 -> 0.27, CROWN UNMOVED — the
exported bbox is identical, which is the check that matters because
Normalize scales by the tallest point). Only `unit_rifleman_rigged.glb`
was copied across; the builder rebuilds all seven and the other six
were left alone. Hold corrected in `UnitAnim.LateUpdate` — left arm 35
inward / 4 down, right 5 / 4.

**`UnitPosePreview` is the new instrument** and it earned itself back
three times over: `-executeMethod UnitPosePreview.Shots` renders the
shipped prefab sampling the shipped clip in ~2 minutes against ~8 for a
device round trip. **Its first frame of any batchmode session renders
UNLIT**, and the control is always first — so the control came out
black beside a lit candidate, which reads as the pose change having
fixed the colour. There is a discarded warm-up frame now. It also
applies `ReadyDrop` before judging, because the runtime always has one.

**THE FIRST POSE ATTEMPT WAS WRONG IN THE SIGN and reached the phone.**
Rob: *"still see the same - one arm out, other arm holds weapon."* The
"inward" yaw swung the left hand OUTWARD — measured, x went -0.421 ->
-0.979 while the rifle sat at +0.625, so the gap GREW from 0.84 to
1.51. It survived a look at a 3/4 render because an arm swinging
outward reads as "crossing" from that angle. **Two arms and a weapon
are three lateral positions: judge them as NUMBERS.** `UnitPosePreview`
prints all three now (`[PoseMeasure]`), and that is what caught it —
after a first version measured the mesh NODES, which sit on the joints,
and reported both hands unchanged at +/-0.421 for every candidate.

**No arm angle alone could ever have fixed it.** The rifle hung
outboard of the right shoulder and one bone per arm reaches at most an
arm's length across. The weapon had to come inboard too:
`AttachGun` x 0.10 -> **-0.15**, paired with left 45 / right 6. The two
only work as a PAIR. Shipped state measures left hand 0.138, gun 0.268,
right hand 0.338 — the weapon between the hands, confirmed on device.

**Honest limit:** one bone per arm, no elbow, so this is not a firing
stance — the forward hand sits at the receiver, not out on the
forestock, because 45 degrees of yaw also costs 30% of the arm's
forward reach. Going further needs an elbow, which invalidates every
clip. **Ask first.**

**Superseded — kept because the reasoning is still the map:** The soldiers have
no visible face, and the reason is not that one was never modelled:
`build_units_rigged.py`'s rifleman head already carries a skin sphere,
a NOSE box, a jaw, two ears and a neck cylinder. They are **buried
under the ACH pot** — `cylinder(head_r * 1.18, 0.38, (0, 0.01,
HEAD_C + 0.10))`, wider than the head and reaching down to
HEAD_C - 0.09, i.e. below the sphere's centre. The fix is to raise the
BRIM without lowering the CROWN (keep the top at HEAD_C + 0.29, cut the
height to ~0.27, recentre to ~HEAD_C + 0.155).

**THE TRAP IN THAT FIX, written down before anyone attempts it:** the
helmet's TOP is load-bearing. Its own comment says *"Must overshoot to
~2.97 ... AttachGun is in model units and Normalize scales by the
tallest point"* — so shortening the pot from the top moves the rifle to
the crown and rescales the whole figure. `PortSelfTest` also samples
the walk clip and asserts model height ~2.70, hip swing and foot carry,
so a re-export that moves the head goes red. Raise the brim only.

**Blender MCP is NOT connected** — `get_addon_status` returns
`source: "missing"`, no version. It needs `uvx blender-mcp install-addon`
and Blender running with the addon started. **The CLI works without any
of that** (`~/blender/blender-5.1.2-linux-x64/blender` 5.1.2), and the
builders are headless scripts, so the face pass can be driven straight
from the command line if the MCP stays down.

**L5 EASED — MORE MEN, TIGHTER LINE, AND A SNIPER THAT SHOOTS FLAT**
(08-25, Rob: *"on level 5, we should group player units together more
closely, add a few more player units.... plays difficult. also, can we
move the enemy sniper closer to the ledge and make their shot more on a
direct line instead of an arc."*)

- **Squad 10 -> 13** (riflemen 7 -> 10) and **deployBudget 13 -> 16**.
  The budget is not optional bookkeeping: at 13 points for 13 units the
  picker would have silently truncated the squad and the level would
  have fielded the old ten with a bigger number sitting in the asset.
- **THE AUTHORED ANCHORS ARE NOT THE PLAYER'S LINE.** This is the trap
  in this ask and it would have eaten a whole session. `playerGroups`'
  `anchorX` values are thrown away the moment a squad is PICKED:
  `Loadout.ToPlayerGroups` tiles the chosen units on its own uniform
  pitch centred on `GroundAnchorX`, and `Formation.Clustered` spaces
  each group from `DefaultColumnSpacing`. The authored anchors are read
  only when nothing runs the picker — i.e. the **◀ ▶ stepper**. Edit
  them and you have tuned the debug path and moved nothing the player
  will ever see.
- So the knob is new and it is a SPACING SCALE:
  **`LevelDefinitionSO.playerSpacingScale`**, 1 everywhere, **0.8 on
  L5**. It multiplies both the tiling pitch and the ground cluster (not
  garrison rows — those are clamped to a deck). Built line is now
  **1.15 wide holding 13**, against 1.44 holding 10.
- **It is a DIFFICULTY change, not dressing.** Every unit fires with
  ONE shared launch velocity from its OWN x, so the volley's beaten
  zone is about as wide as the line that threw it. Tightening the line
  puts more of the same volley onto the same men. Race went
  **0.7x -> 0.4x** (player 3.0 -> 2.4 clean volleys, enemy 4.5 -> 6.0
  as player HP went 288 -> 384).
- **Do not take the scale far below ~0.75.** `Formation.Clustered`
  packs within a group at 0.62x of it and a body is ~0.131 wide. At 0.8
  L5 is already the tightest line in the campaign — the co-location
  check reports **0.149** there, against 0.158 on L1 before this.
- **The sniper moved to the ledge**, anchorX 10 -> 9.4 (the deck spans
  9.25..10.75), and gained **`UnitDefinitionSO.flatTrajectory`**: a
  12-20 degree direct shot in place of `EnemyAI`'s 35-60 lob. It is a
  per-CLASS trait, not a per-level tweak, because it is
  characterisation — any sniper anywhere should shoot like this. Flat
  shooters also get their own **`MaxFlatLaunchSpeed` 16** (the lobbing
  cap is 12): covering the same ground on a shallow arc costs more
  speed, and at 12 the solve was being clamped and pitching rounds into
  the dirt short of the line.

**Two things measured that are worth knowing before tuning it further:**

- **A flat shooter's misses go LONG, not short.** The +/-2 aim jitter is
  applied to the target POINT including its HEIGHT, and a shallow
  trajectory travels a long way horizontally while dropping 2 units. On
  L5 the sniper puts **54 of 60** on the line where the lobbed version
  put 58 — so the direct shot is slightly LESS accurate, by mechanism,
  not by tuning. Fixing that means giving snipers their own jitter,
  which is an accuracy change nobody has asked for. **Ask first.**
- **The 11.6-separation warning on L5 is OLDER than this work** and is
  unchanged by it — it reads the AUTHORED front rank (-4.6), which is
  the stepper's line, not the player's.

**Both new checks were seen RED first**, each naming its defect:
*"the built line is 1.44 wide against 1.44 at scale 1"* and
*"steepest 59.8"*. The sniper check is **SEEDED** (`Random.InitState`,
state saved and restored): `AimAt` rolls off `UnityEngine.Random`, and
unseeded the check passed four runs and then reported a short round on
the fifth. **A check that only sometimes goes red is not a check.**

**ALL OF IT IS DEVICE-VERIFIED (08-25).** 13 units in a visibly packed
line that still reads as men rather than a pile; the sniper standing on
the platform's PLAYER-FACING edge; and the flat shot confirmed as a
CONTROL SHOT — one frame, during the enemy windup with the free camera
parked between the two, showing the four bunker riflemen with rifles
angled UP to lob while the tower sniper's sits level. The windup poses
each rifle at the angle it will fire (`EnemyAimDegrees`), so the pose
IS the shot, and the lobbers in the same frame are the control.

**TWO TRAPS THIS COST, both about how you REACH a level:**

1. **The ◀ ▶ stepper carries the PREVIOUS level's picked squad.**
   `LoadLevel` passes `playerGroupsOverride: loadoutGroups` and the
   stepper never clears that field — only `EnterLevel` (the picker
   path) rewrites it. Stepping L1 -> L5 fielded L1's 8 riflemen plus
   its 2 TANK CREW, and since L5 has no `player_tank` the crew fell to
   the ground as ordinary bodies: **"Your units: 10" on a level
   authored for 13**, which reads exactly like the data change having
   failed. It had not. `LoadLevel`'s own comment says the stepper
   "carries nothing" — that is about CONSUMABLES; the squad is carried.
   **To see a level as a player sees it, go through the picker**: win
   into it, or take a defeat and press RETRY.
2. **A locked roster hides a budget overrun in the self-test.**
   `ProgressStore.ResetAll()` leaves the grenadier and sniper LOCKED,
   so `Loadout.Default` substitutes cheap riflemen and the squad prices
   at 13 — while the real unlocked mix costs **16 against a budget of
   exactly 16**. The device said "16/16 points" where the test said 13.
   The check now prices the AUTHORED mix unlock-independently, because
   the failure it guards is silent: the picker just fields fewer men
   and the level looks authored-but-easier. **L5 has ZERO points of
   headroom — adding any unit there needs `deployBudget` raised too.**

**THE SHELL IS NOW ARMED OR NOT ARMED, AND THE TANK CARRIES FIVE**
(08-24, Rob's ask, straight out of the L4 finding above). The mechanic
is in `GAME_DESIGN_LOCKS.md`; what a new session needs:

- `CannonArmed` was ALREADY in `GameState`, already gating
  `BattleTick.CannonShells`, already self-tested — defaulting true with
  **nothing on earth able to set it**. Only the UI had been dropped in
  the port. The fix was a control, not a mechanic.
- **5 shells**, on `PlayerTank.cannon.ammoPerBattle` (was 3). A level
  may override per placement — `shellsOverride` + `hasShellsOverride`,
  same shape as `standWidth`/`hasStandWidth`, and zero is a legal
  override ("this level's tank has a cold gun") which is why it is a
  HAS flag and not a -1 sentinel. `LevelBuilder` reads it from the
  PLACEMENTS now, not the built entities.
- **The panel is a magazine, not a button, and it is PINNED TO THE
  TANK** — world-anchored under the hull's base, not parked in a HUD
  corner. Rob: *"should not follow across the screen — it should be
  selectable during the player aim... and it should be underneath the
  tank as well."* A screen-anchored panel rode the camera through the
  whole volley and the resolve and read as permanent furniture rather
  than as a property of the gun. It exists ONLY during the player's
  aiming phase and is gone the instant the volley leaves.
  Armed is a THICK GOLD box reading `ARMED` with the next round capped
  white; not-armed is a thin grey outline reading `NOT ARMED` (his
  wording — an earlier pass said `FIRES THIS VOLLEY`/`HOLDING`, which
  is noise once the panel only exists during the aim). The border
  WEIGHT difference is deliberate: colour alone does not carry on a HUD
  looked at in a hurry.
  - It is CLAMPED to the screen, which can pull it off true centre
    under the tank. An unreachable control is worse than an off-centre
    one, and the tank sits at the end of the line the aim frame crops
    tightest.
- **The state PERSISTS across turns** and does NOT re-arm per volley.
  Arming spends nothing, so auto-disarm would tax the player who wants
  to shell straight through.
- **Default is ARMED. Signed 2026-08-25.** Do not flip it as taste.

**IT BROKE THE WHOLE TOP BAR ONCE — the trap is worth more than the
feature.** Rob, from the phone: *"now i can't switch levels and none of
the top buttons work."* RIGS, CAM and the ◀ ▶ stepper all DREW
correctly and not one of them answered a tap.

**IMGUI hands out control IDs BY CALL ORDER and matches them across
passes BY POSITION IN THE SEQUENCE.** OnGUI runs several times per
frame — Layout, Repaint, then one pass per input event. A control that
exists in one pass and not the next shifts the ID of everything
declared AFTER it, and those controls' events go to the wrong place or
nowhere. `DrawLevelNav()` is called immediately after
`DrawShellToggle()`, so the entire top bar sat downstream of the fault.

Two things in the panel were unstable within a single frame, and both
had to be fixed:
- **Its rect is anchored to the tank through `cam.WorldToScreenPoint`**,
  so it MOVED between passes and vanished when the phase flipped. It is
  computed ONCE now, in `Update` after `ApplyCamera`, and cached in
  `shellPanelRect`; every OnGUI pass in that frame reads the same rect.
- **Its `GUI.Button` was called conditionally** (`if (canUse && GUI.Button(...))`
  — C# short-circuits, so the control was not declared at all when
  `canUse` was false, and `canUse` reads `dragging`). It is declared
  UNCONDITIONALLY now, parked off-screen at zero size when there is no
  panel, with `GUI.enabled` doing the gating. `DrawConsumables` had
  this right all along and is the pattern to copy.

**The rule, for anything added to this HUD: the NUMBER of IMGUI
controls a frame declares must not depend on anything that can change
between passes** — camera position, drag state, turn phase. Draw calls
(`GUI.Label`, `GUI.DrawTexture`) allocate no IDs and may be hidden
freely; `GUI.Button` and friends may not.

**Verified on device, as OUTPUT, not as a handler running.** Same drag
twice on L1: **HELD leaves the count at 5/5** and the outpost merely
takes infantry chip damage (90 -> 58); **ARMED spends 5 -> 4 and
collapses the outpost outright**, enemy 14 -> 4. That is the whole
mechanic in two shots. `[Cannon] armed=False, shells 5/5` in logcat
confirms the tap. The PLACEMENT was verified the same way: the panel
sits under the hull on the player's turn, is **absent from the mid-
volley frame** while the camera rides the shot downrange, and is back
under the tank next turn still reading `NOT ARMED` at 5/5 — so the
choice persists and an unarmed volley really does spend nothing.

**The new self-test was seen RED against the old builder first**, and
it named the defect: *"same level with a placement override of 2 built
5"*. It asserts the BUILT STATE, never the asset field — the total is
summed through a side/cannon filter, so a filter that dropped the tank
would leave `ammoPerBattle: 5` in the asset while the battle started
with nothing. The override half runs on a `Instantiate` CLONE so a test
cannot dirty a real campaign level.

**Balance moved and it is not nothing: siege capacity 288 -> 480**
across every level with a tank. `BalanceAudit` still reports 0 errors
and the same 21 warnings, and the only remaining SIEGE DEFICIT is L5,
which has no tank at all and is therefore untouched by shell count.
**Nobody has PLAYED a level at 5 shells yet** — L1 was fired twice to
prove the toggle, not to judge difficulty. Every level just got a
materially bigger demolition budget; **L4 in particular now has 480
against 240 of garrisoned HP and may well be too easy.** That is the
next thing to feel, and `shellsOverride` is the knob for it.

**The control-ID fix IS device-verified (08-24).** On the fixed build:
◀ ▶ walks L1 -> L2 -> L3 -> L2; CAM raises the free-camera pad and its
x/y/z readout; RIGS reads `RIGS ON` and the reachable count goes
`L2 (2/12)` -> `L2 (2/29)`. The check that actually matters is the last
one — **tap the shell panel and then immediately tap ▶** — because
interacting with the panel is what shifted the ID stream. Toggled to
`NOT ARMED`, then the stepper answered on the next tap and moved
L2 -> L3. That is the regression path, walked.

`PortSelfTest` green and `BalanceAudit` clean at the time this landed.
**Tree state for the whole session is at the top of the file** — it is
not repeated per item, because six copies of it is six things to go
stale.

**The L4 arc (08-21 → 08-24).** Three asks, one level. Read all three
before touching it; the second and third exist because of the first.

1. **The enemy shield bearer had no armour at all.**
   `EnemyShieldBearer.asset` was missing `damageTakenMultiplier`
   entirely while the player's carried 0.5 — so the class whose whole
   mechanic IS armour, and the one that actually CHARGES, was a bare
   40 hp body a converged volley wiped on approach. Rob: *"the melee
   force should not die immediately."* Same family as the machine
   gunner's burst: **a signature living on one side's asset only.**
   Set to 0.5, then **retuned to 0.75 on 08-24** — Rob: *"should not
   have double hp but just a bit more than they originally had."* A
   rifle round does 6 instead of 8: **7 rounds to kill against 5
   bare**, up 40% rather than 100%.

2. **The contact frame was 70% wider than its own engagement.**
   `ContactHalfWidthMin = 4f` on a fight that wants ±2.36. That 4 was
   never geometry — it was paying for SPRING LAG, and a fixed lag
   needs a fixed addition, not a minimum: a floor over-pays a small
   engagement and is swallowed by a large one. Floor 2.5, union
   carries `ContactSpringMargin = 0.7`; L4 goes ±4.00 → ±3.06 with the
   tank rear still held by 0.61. The signed-off UNION is untouched.
   See `CAMERA_ARCHITECTURE.md`. **Both existing camera checks only
   asked whether the frame HOLDS the force**, which any frame big
   enough passes — ±4.00 stood on a ±2.36 fight under two green tests.
   There is a ceiling now as well as a floor.

3. **The charge is a RUN.** Rob: *"the leg movements are too dramatic
   — can we make it look more like a run?"* It played Kenney's `walk`
   raw. **Measured, that clip is not a run and never was: ±60° at the
   hip (120° of scissor) at 3 steps a second** — a sprinter's
   amplitude on a stroller's cadence, exactly backwards.
   `UnitAnim.ChargeStride = 0.75` puts the hip at ±45° (measured 44.9
   on the rendered rig against 59.9), and the cadence is **DERIVED**,
   not a second constant: `GaitSpeed` solves the clip speed that makes
   the feet carry the body. Charge lands on the `MaxGaitSpeed` clamp
   at x1.70 = 5.1 steps/s.
   - **The clamp, and the skate it leaves, are deliberate.** Matching
     2.4 u/s outright wants 7.9 steps/s, a blur. It is affordable only
     because the camera FOLLOWS the charge: with no still ground to
     measure against, amplitude and cadence are what the eye reads.
   - **It fixed a live bug nobody had filed** — a wire-slowed charger
     (`WireSlowFactor` 0.35) crawled at 0.84 u/s while windmilling at
     full rate. `AdvanceSystems.MarchSpeed` was pulled out of `March`
     so the renderer asks the same question instead of keeping a
     second copy of the wire test.
   - **`AdvanceSpeed` 2.4 was NOT touched** — signed off 08-13,
     *"the march is fine."* Slowing the ground to ~1.0 u/s is the only
     way to kill the skate outright, and that is a pacing call, not a
     gait one. **Ask first.**

**Three traps banked from that arc — all of them cost a session:**

- **`damageTakenMultiplier` IS QUANTISED. Do not tune it as if it were
  continuous.** `CollisionSystem.Soaked` rounds to an int, so against
  an 8-damage rifle round every multiplier in **[0.6875, 0.8125)
  resolves to the same 6**, and 0.50 and 0.55 are indistinguishable.
  There are only FOUR reachable settings between half and none. If an
  ask wants a value between two steps, the knob is `maxHp`, which is
  continuous. This is why the self-test states melee toughness in
  ROUNDS-TO-KILL: asserting a multiplier would assert an input the
  engine does not honour at that resolution.
- **"They died right after taking a player unit out" is a LOCK, not a
  bug, and not an HP consequence.** `StepSkirmishes` kills BOTH bodies
  on `sk.Age >= SkirmishDuration` — no HP, no armour, no roll is read.
  `GAME_DESIGN_LOCKS.md`: *"after ~1s BOTH fall as mutual kills. One
  fighter through = one soldier lost, guaranteed."* **Armour only ever
  buys the APPROACH; it can never buy survival of the fight.** Making
  melee a damage roll would undo the guaranteed-trade maths the whole
  mechanic is costed on — ask before doing it.
- **A DOC LIED AND THE ASSET SETTLED IT.** `UnitAnim` said "a 60° hip
  jog"; the clip is ±60°, i.e. twice that, and every conclusion drawn
  from the comment was wrong by a factor of two. Same family as the
  TMP "ASCII only" note. `PortSelfTest` now SAMPLES the clip for
  swing, cycle length and foot carry and asserts the constants against
  the rig, so a re-export that moves the hip goes red instead of
  quietly regressing the gait.

Every new check in this arc was **seen red against the old code
first**, each naming its defect: `worst 8 of 8`, `±4.00 against 2.36
needed + 0.70 air`, `charge 59.9`, `x1.00 = 3.0 steps/s`,
`(enemy_shield_bearer)`.

**Older, still current: 08-20/21 range + L5.** L5 is 3 MG in the
street, one sniper on the tower, no tank. Ask before restoring L3's
three snipers, rolling MG/sniper roles across the campaign, or selling
the tank.

**Signed — do not reopen as taste**

- Charge gait (08-24). Rob: *"yes, that looks good."*
  `ChargeStride` 0.75 + derived cadence. `AdvanceSpeed` 2.4
  stays. Do not re-raise the stride to play the clip raw.
- Enemy charge armour 0.75 (08-24). Rob: *"ok that's fine."*
  Not 0.5 — he asked for a bit more, not double. The PLAYER's
  shield bearer stays 0.5; its roster line sells being double.
- New distance. Rob: *"that actually plays better"* / *"i
  like the new distance."* v 9→9.5 (flat max 22.56). SpeedScale
  0.0064→0.00677. L1 outpost 7→9, ground 4.5→6.5, tank −9.5.
  Built infantry gap 12.4. **Do not widen the aim frame.**
- L2–L12 enemy side +2 (08-20). Shield charges 1.1/1.0/1.2 →
  1.5/1.3/1.5 so the extra street does not add turns. Melee
  does **not** volley (`FireEnemyVolley` skipped `meleeDamage`;
  that lock was prose). L11 heavies still walk-and-shoot at
  1.2. Player tanks stayed except L5.
- Punch (3+ kills) and miss scorch. Rob: punch *"looks good"*;
  scorch *"easier to see."*
- Arrival headlines gone. Rob: *"ok this is fine."* Keep the
  L10/L11 telegraph strip. HUD names the phase. Camera still
  holds on an arrived group. Do not restore `ThreatLine`,
  `levelGoal` flash, or "The Sovereign will not yield".
- 08-19: body-aim, enemy raise, mid-ground variety, authored
  funnel, phase banners. 08-18: hold, Forest/Mountains/Winter,
  Cluster 3.2x, L1 car, ragdoll tumble/rest, collapse camera.
- Melee camera (L4/L8/L9/L12): hold 1.5s, march 2.4, Grapple
  0.75. Do not share `MarchHalfWidthMin` with contact.

**On the phone, not a taste sign-off**

- **L5 no tank.** Rob: *"ok, fine for now."* Crew folded into
  the ground line (2+5→7). No HP retune. TankArrive still jogs
  the infantry. Rule 4 falls back to front rank → dominant.
  L3 still has a tank. Shop parked in `_plans/BACKLOG.md`.
- **L5 roles.** Those "snipers" were six MG on the platform.
  Now 3 MG in the street, ONE sniper on the tower. Principle:
  MG forward, snipers elevated/back. Not applied to L4/L6/L9/
  L10/L11. L3 left at one sniper from the mix-up.
- **L4 fail cards — WALKED ON DEVICE 08-21, both losses.** Loss 1
  "Charge reached your line", no nudge. Loss 2 "The garrison is
  still firing" + **"You have an Airstrike — take it on the
  retry."** Both +24. The reason tracks the ACTUAL last blow, so
  the same level gives a different card run to run — L4 is not
  reliably the charge card. `a Airstrike` is dead; seen right.
  L1 Smoke nudge still uncalled. Campaign +2 past L1 not walked
  as a set.
- **RIGS now carries the fail card.** `AwardDefeat(level, state,
  testSupply)`. It read `ProgressStore.OwnedConsumables` direct,
  so on a release build the you-have-one branch — the one that
  shipped as "a Airstrike" — could not be reached without
  earning 250 real coins. The new check was seen RED against the
  old code and asserts the economy stays at zero after.

**Do not start:** city-road option 2; L4 march zoom as its own
beat. Wind blocked. Overwatch Flare not sold. Next biome/unit
only if he names one. Do not drop gravity. Do not sell the
tank until L5 is felt without it. Do not restore L3's three
snipers or campaign-wide MG/sniper swaps unprompted.

**If he asks for the leftover:** lose L1 twice to a volley —
Smoke, not Overwatch. L5: if
the tower reads as a wall, cut `hpScale` before a shop.

**Product stack:** 0, 1.1, 1.3, 2.1–2.4 built. 1.2 = waves
only. 1.4 heli shut. Plans: `_plans/RANGE.md`,
`_plans/FAIL_JUICE.md`.

**Traps this sitting paid for — do not re-learn:**

1. **Cannot slide the enemy +2 at v=9.** Flat max is 20.25;
   a roofed garrison goes over 100%. Raise `MaxAimMagnitude`
   (now 9.5, range 22.56) **with** `ProjectileSpeedScale` so
   a ~525 px drag is still 100%. Rule 4 checker max is 20.
2. **`FireEnemyVolley` fired melee.** GAME_DESIGN_LOCKS said
   shield bearers never volley; the path did not skip
   `meleeDamage > 0`. Skip in Prepare and Fire. L11 heavies
   are a firing line — do not make them melee.
3. **L5's "snipers" were machine gunners.** Six MG crowd on
   `tow_top`. Assert the class (`enemy_sniper` vs
   `machine_gunner`), not the silhouette. MG belong in the
   street; snipers on the far elevated deck.
4. **No tank → rule 4 uses the front rank**, not "not
   measurable". `TankArrive` still jogs ground troops.
5. **Do not widen the aim frame.** Seeing both lines during
   the drag is a mechanic change. The emptiness is vertical.
6. **Kenney Nature Kit GLBs have no atlas.** `leafsDark` is aqua
   `(0.17, 0.65, 0.67)`, `woodBarkDark` is peach `(0.80, 0.46,
   0.37)`. That is the kit. We left those colours on once as a
   control shot; Rob: *"now the trees are like an aqua color.
   what is this."* `PlaceStripLayer` paints `leaf*` with the
   layer's silhouette green and `wood*`/`Bark*` brown. **Do not
   restore the kit colours.**
7. **Do not copy the kit into `Assets/Models`.** The scene builder
   wires every GLB there. Source lives in
   `tools/blender/kenney_nature/` (CC0, builder input only).
8. **Forest foreground stays off.** `StripFore` returns null.
   The 2026-08-17 shrubs were magnified ~7x and were never the
   ask. `ForeZ` / `build_fore` stay so a later biome can opt in.
   Do not re-wire Forest. The unused `backdrop_forest_fore.glb`
   can sit.
9. **A snow solid at 6° is a white object**, not snow. L7's cap
   mesh read as icebergs, then a mesa, then a chimney. Winter
   already has a white ground and `snowfall`. `snow_from=2` so
   `make_snow_mesh` emits nothing. Do not put a cap mesh back
   on Winter or Mountains.
10. **Isolated cones / high-octave ridge = a picket.** L1 far was
   `cycles=10, octaves=4` with a white triangle on every horn.
   Far mountains use `kind=range` (broad massifs, 2 octaves),
   `far_foothill`, snow only on wide crests (and Winter none).
   Do not go back to isolated cones.
11. **Do not flip Kenney's hold 180°.** The body already faces
   Unity −X (screen-right / the enemy). Flipping the arms made
   them reach backward — Rob: *"now they're facing the wrong
   direction."* The first "opposite gun" was the mesh vs the
   imported root's +X, not the clip.
12. **Assert the rendered rifle, not `TransformPoint(+X)`.**
   glTFast wraps a root. Span-along-X was GREEN while every
   muzzle pointed at the tank. Mesh bounds-centre along
   `facing.forward` is the check. `LookRotation(forward, left)`.
13. **Mid-ground is z ≈ −8, not the play plane.** z −0.75 sat
   on the squad. Scale 4.1 there was two office towers.
   Backdrop NearZ is −30 and cannot fill the tan.
14. **`keepColors` on wreck / cactus / tree.** Default Tone
   paints every prop player-olive. Sandbags and wire stay
   painted.
15. **Do not play `die` in the air.** It is a sit-down pose.
    The GO tumbles; landing flops to ±90.
16. **Dirt rest is `RagdollRestY(0)`.** `RagdollRestY(spin)` at
    ±90 is a 0.5-unit phantom floor. Roofs and wreck lids raise
    the surface; the live spin does not.
17. **Wreck.Y is the visual BASE** (`st.Y - size/2`), same as
    the wreck GO. The standing centre plus 0.32 sat bodies at
    ~1.6 after the hut had collapsed to the dirt.
18. **A road is kerbs and slabs, not a decal.** 6° turns a
    flat strip into a smear. `PropPlacement.absoluteScale`
    skips Normalize — a 14-unit boulevard at scale 1 would
    otherwise become a postage stamp.
19. **Do not restore the narration banners.** Phase copy and
    arrival headlines are gone on purpose. The telegraph strip
    is the remaining event channel. HUD phase labels stay.
    The camera still holds on an arrived group.
20. **Do not share `MarchHalfWidthMin` with contact.** 2.5 on
    contact crops the tank. The self-test failed at
    cam −6.67 ±2.82 vs tank rear −9.59 when they were one
    constant. Contact is the signed union; march is the
    distant escort.

21. **Containment is a ONE-WAY check.** "The frame holds the force"
    is satisfied by any frame big enough, so a contact shot sat at
    ±4.00 on a ±2.36 engagement for months with two green camera
    tests over it. Whenever a check asserts something FITS, ask what
    stops it fitting with room to spare, and assert that too.
22. **A floor cannot express a fixed lag.** The 4f contact floor was
    paying for the camera's ~0.55 spring trail; a minimum over-pays on
    a small set and vanishes on a large one. Lag is an ADDITION.
23. **Armour, damage and every other signature exist PER ASSET, not
    per class.** The player's shield bearer soaked and the enemy's did
    not, from the same design note. When a mechanic is verified, ask
    which OTHER asset was supposed to have it — every pair in
    `Assets/GameData/Units` is now diffed and only this one differed.
24. **RIGS is not a global — it is a parameter, and every path
    that reads the economy has to take it.** The loadout honoured
    the test supply; `AwardDefeat` read PlayerPrefs directly, so
    one branch of the fail card was unreachable on the only build
    worth measuring. When a feature is verified under RIGS, ask
    which OTHER reads of the balance it goes through.

**Do not widen the aim frame.** That analysis still stands.
The collapse follow was a separate, explicit ask and is signed.

---

**Stop.** Everything below is scar tissue. Pickup above is current.
A dated heading is history, not a job list.

**2026-08-17 — WINTER/MOUNTAINS AND FOREST REWORKED.** History.
The 08-18 sitting superseded the forest cones and the mountain
horn/snow-cap look; those three biomes are now signed. Three
findings from this day, all defects rather than taste, all
fixed in `tools/blender/`:

1. **Winter WAS Mountains.** `build_one` called
   `build_mountains(0.62,"Mountains")` and `build_mountains(0.42,"Winter")`
   — same widths, same cycles, **same hardcoded seeds** (401/523).
   "Backgrounds still look the same" was literally true. Seeds and
   cycles are arguments now; Winter seeds stay 911/947. Verified by
   bucketing crest heights out of the two exported GLBs: mean
   difference **3.83** on a 17-unit range. Snow line 0.42 -> 0.60
   still made L7 a field of white objects; **08-18 set `snow_from=2`
   and `kind=range`**. Do not restore 0.60 or `cycles=7.5`.
2. **`face_dress` could never be seen.** It built rock slabs "so the 6°
   view is not a flat card" and then joined them into the body object,
   which takes ONE flat unlit body colour — the slabs were the exact
   colour of the surface behind them. Splitting them out as `trim_`
   made them visible and WORSE: flat unlit has no lighting, so a slab
   on a face reads as a dark rectangle stuck to a hill. **Depth here is
   bought with OVERLAPPING SILHOUETTES at different values, never with
   surface detail.** Replaced with a third range: a darker foothill
   ridge standing in front of the near one.
3. **Forest: the pine was trunk + ONE cone**, and every crown, bush and
   hill went into one flat green. Now three overlapping skirts (notched
   outline), ~1 in 4 broadleaf, and a third of the stand built in the
   TRIM material so the canopy carries two greens. `ridge()` built the
   land mass out of **boxes** — the pale crates and the "bucket on a
   pole" seen through gaps in the stand were those, not trees; it is a
   continuous noise profile now. Far stand tightened (spacing 2.05 ->
   ~1.38, pines only) so it reads as a mass. `Forest.asset`
   `silhouetteFar` **(0.576,0.737,0.627) -> (0.42,0.565,0.455)** —
   luminance 0.68 against near's 0.35 made every gap read as a bright
   hole rather than distance. Measured on the preview render: far body
   is `(107,144,116)`, was `(147,188,160)`.

4. **Forest detail pass** (same day, after Rob: *"i feel like we could add
   details to the trees/background"*). Emergents at ~1.4x height in both
   layers so the canopy top is not one band; **dead snags** (bare leaning
   trunk + two stub branches) — the only silhouette in a stand that reads
   as a gap, without which a treeline is a hedge; **birch** with the trunk
   in `accent_`, the third value slot the forest had never used, which
   gives a pale vertical and is the single most legible piece of detail at
   6°; ferns at the base; near spacing 2.15 -> 1.85.
   **The far HILL in `accent_` was tried and REVERTED** — the runtime's
   accent is body lerped **78% to WHITE**, which is right for snow on a
   peak and reads as bright holes punched through a dark wood. There is no
   mid-tone slot per layer, so the far hill and far canopy share a value
   and separate on silhouette alone. The comment in `build_far` says so;
   do not re-try it.

5. **THE BACKDROP STRIP HAS A THIRD PLANE** (Rob: *"a little better, but
   more would be preferred"*). Two strips capped the wood at four depth
   steps and six material slots, and no amount of detail on either plane
   fixed it reading as two cut-outs — that ceiling, not the tree models,
   was what "more" was blocked on. `BackdropRuntime.StripMid(style)` sits
   at the existing `Backdrop.MidZ` (-38, previously used only by the
   procedural fallback) and is **OPTIONAL**: only Forest declares one, and
   a style without one keeps drawing its two. A missing mid deliberately
   does NOT drop the strip back to the profile — it logs and carries on —
   so the reference being wired is asserted in `PortSelfTest` instead.
   **Its colour is `Lerp(silhouetteFar, silhouetteNear, 0.5)`**, so a biome
   opts in by adding one GLB and nothing else; no `BackgroundDefinition`
   grows a field. The scene builder already wires EVERY glb in
   `Assets/Models`, so a new layer needs a scene rebuild and no editor
   change. `BackdropPreview` had to learn to register the mid too, or it
   renders a picture the device will not — which is the one thing that
   tool exists to prevent.
   **The new check was seen RED** against a deliberately broken
   `ForestMidModel` (`[FAIL] Forest declares mid strip
   backdrop_forest_mid_MISSING and Battle.unity wires it`) and restored.

6. **FOREGROUND STRIP + the measurement that reframed this whole job.**
   After a third *"still feels meh"* I stopped tuning trees and measured
   **Archery Bastions on the device** instead (it is installed —
   `com.bastion.archers`). Its backdrop is FLATTER than ours: two plain
   mountain silhouettes and a couple of torii. What fills its screen is
   the **objective** — a fortress spanning ~68% of the width and ~25% of
   the height, carpeted in massed units, plus one sky object and framing
   gates at the edges. Ours: treeline is a ~10% band, ~34% empty sky above
   it, ~27% empty ground below, six small units, and the enemy structure
   **off-screen entirely** during Aiming. **The backdrop was being asked
   to carry a frame it occupies a tenth of** — which is why three passes
   of genuinely better trees each moved it a little and then stalled.
   Edge-pixel density, the fairer of the two proxies: **ours 0.60% ->
   1.65% with the foreground; the reference is 6.08%.** Still ~3.7x short,
   and the rest of that gap is composition, not backdrop.
   `Backdrop.ForeZ = 3` — the only layer at POSITIVE z, in front of the
   play plane. It must stay inside `CameraDirector.ZMin` (5.5) or a tight
   frame puts the camera behind it; asserted in `PortSelfTest`.
   **Two things this layer CANNOT do, both learned on device:**
   - **No trunks, and no framing at the screen EDGES.** A world-fixed strip
     does not move with the camera, and the camera pans the length of the
     battlefield, so "a trunk at the left and right edges" is not
     expressible here. Built anyway to test it: one trunk is ~110px wide,
     runs the FULL height of the screen, and cut the frame in half through
     the band the shells arc through. Anything tall belongs in the near
     strip, behind the units.
   - **Scale cannot be reasoned about, only measured.** This layer is
     magnified ~**7x** against the play plane — far more than the naive
     `(camZ - ForeZ)` ratio predicts, because the projection is tilted and
     off-centre. Fronds authored at 0.42-0.80 filled the bottom third as
     dark pyramids; at 0.16-0.32 they were still a row of little tents with
     legible gaps. The numbers that work (**0.03-0.10**, overlapping at
     0.07-0.14 spacing, with per-frond width and lean jitter) were read
     back off device screenshots, and a frond finishes about a third of a
     soldier's height.
   Watch the vert budget: an ico-sphere bush is ~240 exported verts against
   a frond's ~30, and at this density the bushes alone were 60k. Forest is
   **64k verts over four layers** (fore 34k) in 8 draw calls, 60 fps held.

### Unit hold + emptiness — 2026-08-18

Two jobs in one sitting. Both on the phone. Ask git.

**Hold.** Identity attach pointed `placeholder_gun` (+X barrel) at
the camera. Aligning the bone then flipping the hold 180° pointed
the mesh downfield and turned the silhouette around. Reverted the
hold. Final attach is `LookRotation(forward, left)`. Checks: player
`facing.forward.x < −0.7`, mesh centre on that forward. Ready-drop
16° + 2.4° breathe; idle damp is per-joint. Rob: *"ok good. that
works."*

**Emptiness.** Measured on device, L1, play-area crop (HUD and ammo
out). Same classifier on every phase:

| phase   | content | edges | ground |
|---------|---------|-------|--------|
| Arrive  | 10.2%   | 1.16% | 10.8%  |
| Scout   | 20.0%   | 1.65% |  8.6%  |
| Aiming  |  6.5%   | 0.64% | 14.9%  |

Aiming is the empty beat. Scout is fuller because the outpost is
tall. **Do not widen the camera** (see below). Lever (1) is
play-space mass behind the player line.

Block berms (`prop_flank`) were a stand-in — too tall, then too
close. Rob asked for real things. `prop_wreck_car` (two-box
hatchback, 28° yaw, charcoal) + `prop_dead_tree` on L1 at z ≈ −8.
Car: *"yeah think that's good."* Then all twelve campaign levels
got two `keepColors` plants. Rob: *"ok. seems repetitive but we
can address later."* Parked in `_plans/BACKLOG.md`.

Builder: `tools/blender/build_prop_scenery.py`. Placement is
`LevelDefinitionSO.props` with `keepColors`. `LevelScenery` skips
`Tone` when that is set.

### Widening the aim frame — ASKED 2026-08-17, ANALYSED, NOT RULED ON

Rob, after the foreground: *"if you were to modify camera architecture,
what would you do? do you have a clear understanding?"* Read
`CAMERA_ARCHITECTURE.md` (still LOCKED) and `CameraDirector` before
re-opening this. **The recommendation is DO NOT WIDEN IT**, for three
reasons, and none of them is reluctance to touch a locked file:

- **It does not fit, and it makes everything smaller.** `camZ =
  (halfWidth + FramePad) / ZHalfFovTan`, so `GameplayZ` 22 caps half-width
  at `22 * 0.45 - 0.6 = 9.3` — about 18.6 units of framable width.
  Composition rule 4 puts tank -> dominant structure at **14-18** units,
  the player line is ~6 wide and the enemy cluster up to ~11, so the union
  is **20-28 units**. It does not fit under the current Z ceiling at the
  far end, and where it does, the units shrink. **The reference's frame
  is not full because it is WIDE — it is full because its fortress is TALL
  and CLOSE and carpeted in units.** Zooming out is the wrong direction.
- **It is a MECHANIC change.** Seeing the player line and the enemy in one
  frame during the drag hands the player a direct visual read of range —
  the guess-angle/power mechanic a landing marker and an aim-pan were both
  built and REVERTED for. `PlayerScout` already shows the enemy before the
  aim; the tight frame is what stops it being measured during the drag.
  This needs a difficulty re-tune, not a camera tweak.
- **There is a documented bug waiting.** Widening the half-width without
  moving the camera X anchor reproduces the L12 `staticCamera` failure
  already written up in that doc: *a half-width only frames its subject
  about the centre the camera actually uses.*

**The diagnosis instead: the emptiness is VERTICAL and structural.**
Framing by WIDTH on a 1080x2404 portrait screen means ~6 units of framed
width buys ~13 units of visible HEIGHT. The content is a flat line of
2.7-unit figures on flat ground, so most of the frame is empty whatever
the camera does. Levers, cheapest first: **(1) vertical content on the
player side** — flanking trees in the near strip, a tall player-side
structure, terrain; no camera change, no gameplay change, NOT locked, and
this is the recommended next move. **(2) horizon placement**, redistributing
the 34% sky / 27% ground split — moves emptiness rather than removing it.
**(3) widen**, only with the mechanic change accepted and `GameplayZ` raised.

**CAVEAT THAT MUST BE CHECKED BEFORE ANY OF THIS.** Every number quoted
here and in item 6 was measured on the **Aiming** frame, which is the
tightest and emptiest moment in the game BY DESIGN. The whole job has been
judged on its worst frame. Resolving, PlayerScout, TankArrive and a march
are all wider and were never measured. **Measure those first** — if the
game is only empty during the aim beat, this is a much smaller problem
than the session treated it as, and lever (1) covers it.

**`BackdropPreview.Shots` is the fast loop for this work** — it loads
the GLBs BY PATH, so it re-renders in ~1 minute against a Blender
re-export with no scene rebuild and no APK. Use it to iterate, then
confirm on the device. It cannot catch a broken scene reference (that
is the point of the city trap below), so the device run is still
required before believing anything.

**GUARD EVERY adb INPUT WITH A FOCUS CHECK — `install -r` KILLS THE
RUNNING APP.** On 2026-08-17 a reinstall tore the app down, the relaunch
had not settled, and a dozen queued taps went into the LAUNCHER and opened
Rob's browser. Nothing was typed or navigated, but it is his real phone.
The fix is one line in front of every step, and it has since aborted a run
cleanly instead of repeating the mistake:

```bash
chk(){ adb shell dumpsys window | grep -q "mCurrentFocus.*$PKG" \
       || { echo "FOCUS LOST — aborting"; exit 1; }; }
```

Also: a **fresh install lands on the LOADOUT screen, not the battle** —
press BEGIN before any ◀ ▶ / RIGS navigation, or the taps hit nothing. And
the `UnityApplication::DestroyInstance` crash in `logcat -b crash` after a
reinstall is that teardown, **not** a gameplay fault.

**Sampling pixels beat eyeballing twice this sitting.** A preview
thumbnail is small enough that a real colour change looked like no
change at all; `PIL` on the PNG settled it in one command. And an
ffmpeg `blend=difference` of two device frames proved a change had
NOT reached the screen when the two shots looked different to me.

Builders in **this** repo (`tools/blender/`):
- `build_backdrop_city.py` — signed
- `build_backdrop_forest.py` — far/mid/near Kenney Nature Kit
  (CC0, `tools/blender/kenney_nature/`). Foreground unwired.
  Runtime retints leaf* / wood* — do not ship kit aqua/peach.
- `build_backdrop_land.py` — mountains / winter / desert.
  Mountains and Winter: `kind=range`, far foothill. Winter
  `snow_from=2` (no cap mesh). Desert still a smooth dune.
- `build_prop_scenery.py` — signed three (wreck_car,
  dead_tree, cactus) plus the 2026-08-19 biome set
  (pine, stump, boulder, rubble, lamp, barrel_cactus,
  snow_pine, skiff, piling). `keepColors` on the
  placement. Do not re-export the signed three.
  `prop_flank.glb` is the unused box-berm — do not plant it.
Re-export changes GLB root fileIDs. After every export:
`SpikeSceneBattle.Build` then the APK. Never `Normalize` a strip.
Never `bpy.ops.wm.read_factory_settings` in a live MCP Blender.

**What this sitting signed — do not reopen as taste:**

- Player ground line MARCHES (`MarchStride` 0.5 / `MarchAnimSpeed`
  0.7). Enemy charge still full walk.
- Outpost collapse + fire/smoke; rest of destroyable wrecks shipped
  from it.
- L8 flyover. Rob: *"ok saw it in action, looks good."*
- Deaths glass pane. Lip / sink / flail knobs stay.
- Garrison wreck fire nestled in the pile. Rob: *"looks good."*
- L8 leftover rubble grid + flying chunks (cap 0.14, TTL 8s).
  Rob: *"yes. this looks good."*
- **Airstrike ride** (not the 08-11 cut). Rob: *"yes. looking good."*
  UI ticks on RIGS / CAM / ◀ ▶ press and release.
- **Forest backdrop** (Kenney meshes, biome green, brown bark,
  no foreground). Rob: *"much better. let's keep that."*
- **L1 Mountains** far range (broad massifs, no tooth-snow).
  Rob: *"looks good."*
- **L7 Winter** (same range on a snow field, no cap mesh).
  Rob: *"ok looks good."*
- **Unit hold** (guns downfield). Rob: *"ok good. that works."*
- **L1 wrecked car** (two-box hatchback, mid-ground). Rob:
  *"yeah think that's good."*

**Parked — ask before starting any of these:**

1. **Biome strips.** City, Forest, L1 Mountains, L7 Winter are
   signed. **Desert and Ocean were deliberately NOT touched**:
   Desert is still a smooth dune ridge and shares
   `build_backdrop_land.py`, so it inherits the seed/cycle
   argument but has had no pass; Ocean is still the unpainted
   sun/surf plan. MountainsDusk was not judged on its own (L1
   mesh, dusk tints). Before dressing another biome, read the
   camera section — a fourth biome pass may be the wrong spend.

2. **L11 elite wave — on the phone, not called.** Lands IN FRONT
   of the post (`anchorX` 3, box left edge 4.13) and walks
   (`advancePerTurn` 1.2). Heavies have no melee — they hold as
   a firing line. Was x 9 in the post's shadow. Rule 8 green.
   Confirm on Oceanfront turn 4 ("Elite squad inbound").

3. **Procedural wrecks in general.** Outpost is the only
   hand-keyed clip. Everything else is loose-part fold + XYZ
   (glTF import is QUATERNION — Euler keys without
   `rotation_mode = "XYZ"` export location only). Watch is a
   pile (28%); garrison leftover was the hut, now a rubble
   grid. Tank is not a wreck.

4. **Armour zoom** (L4 first enemy march / L12 leftover),
   2026-08-13, **never signed off.** `FramePad` 0.6. Closer
   only if he says so.

5. **L1 rifleman v2 at Aiming distance** — look, do not remodel.
   If it still reads as the old box man, stop adding pouches.

6. **Mid-ground scenery variety** — signed 2026-08-19.
   Rob: *"ok, looks nice."* L1 car slot unchanged. A further
   taste pass may come later. See `_plans/archive/MIDGROUND_VARIETY.md`.

**If a sitting starts with the phone:**

1. **L11 Oceanfront turn 4** — elites in front, walking.
2. **L1 after BEGIN** — rifleman v2. Then L4/L12 armour zoom.
3. Emptiness on Aiming vs Scout/Arrive is measured (table above).
   Mid-ground variety is signed. City-road street objects and
   the closer L4 march are on the phone, not called.

**Already signed off, do not reopen as tuning:**

- **Melee** — L4. Hold 1.5s, march 2.4, GrappleGap 0.75. Swing is bound.
- **MG burst fan** — *"yeah, think this looks fine."*
- **Opening scout** — `TurnPhase.PlayerScout` after the arrive, first
  battle only. Arrive sits *in front*; do not fold them together.
- **L12 Sovereign stays in the gate's shadow.** *"sovereign is fine on l12."*
  Do not set `triggerStructureIds: [citadel, gate]`.
- **Tank mesh / CityRuins / Forest (Kenney + green/brown) / L1
  Mountains / L7 Winter / deaths (including L8 flyover) / wreck
  fire / leftover rubble / flying-chunk size / airstrike ride** —
  see above. Do not reopen lip / sink / flail, the glass-pane
  numbers, the wreck-fire nestle, the leftover grid, the
  airstrike cut, Kenney aqua, Forest foreground, or a snow-cap
  mesh.
- **Whole-body aim / enemy raise / authored funnel / narration
  banners.** 2026-08-19. Arrival headlines gone 2026-08-20.
  Do not restore `ThreatLine`, the levelGoal flash, or
  "The Sovereign will not yield". Do not reopen the aim pose
  as taste.

**City strip — traps that cost a device session:**

- **Re-exporting `backdrop_city_*.glb` changes the GLB root fileID.**
  The scene keeps the *name* and the prefab slot goes missing. The
  phone then draws `Backdrop.City()` — the old orange-window picket
  fence — with no error. `BackdropPreview` loads by path and will
  still look right. After every city export: `SpikeSceneBattle.Build`,
  then the APK. `PortSelfTest` now asserts both scene refs are live.
  `BackdropRuntime` requires **both** far and near, and logs if either
  is missing.
- **Flame z.** Positive z at ground = orange squares in the street.
  Deep negative z = inside the wall, invisible. Window mouth: marker
  `fx_fire_*` x/y, z just proud of the facade (`−0.05..+0.18`).
  `RuinFx.CollectMarks` plants tongues there; glow stays deeper.
- **Never `Normalize` the city GLBs.** Width is the span.
- Builder: `tools/blender/build_backdrop_city.py` in **this** repo.
  Facades face Blender −Y (Unity +Z). Mass goes +Y / Unity −Z.

**Blender MCP — how this sitting worked, and what kills it:**

- Blender is `DISPLAY=:1 ~/blender/blender-5.1.2-linux-x64/blender`.
- The addon auto-starts on **localhost:9876**. Grok's `blender-mcp` server
  stays up even when Blender dies. If tools say connection refused, do
  **not** re-add the MCP — relaunch Blender:
  `DISPLAY=:1 …/blender --python /tmp/start_blendermcp.py`
  (`/tmp/start_blendermcp.py` enables `blender_mcp_addon`).
- **Never `bpy.ops.wm.read_factory_settings`.** It unregisters the addon
  and drops the socket. Clear objects by hand.
- Viewport screenshots come back black. Render Eevee to a `/tmp` PNG and
  read that.
- Colour still binds to **mesh name prefix** (`skin` / `trim` / `accent`).
  Animation binds to joint paths. Normalize scales by the **longest
  axis** — a short helmet is how the rifle went over their heads.
- Rifleman stays the skinny baseline (`UNIT_VARIETY_DESIGN.md`). Tank
  keeps origin at the base-center, **+X toward the enemy**,
  `accent_pivot_TankGun` at the trunnion, `accent_wheel*` as separate
  nodes, X-span ~1.27 so `Normalize(1.5)` and muzzle `1.08 / 0.72` still
  land.
- Builders: rifleman is `build_units_rigged.py` `build_rifleman()` plus
  `tools/blender/build_rifleman_v2.py` in the retired repo. Tank is
  `tools/blender/build_tank_v2.py` (same place);
  `build_early_structures.build_tank` delegates to it. City is
  `tools/blender/build_backdrop_city.py` in **this** repo. Live GLBs
  are here: `Assets/Models/unit_rifleman_rigged.glb`,
  `placeholder_tank.glb`, `backdrop_city_far.glb`,
  `backdrop_city_near.glb`.

**What the zoom sitting built** (detail in `CAMERA_ARCHITECTURE.md` and §2):

- Aiming frames the GROUND LINE, not the tank crew.
- Enemy frame recaptures when a structure falls or a boss/wave lands, never on a
  casualty.
- Announcement push-in on the arrived group, 2.5s.
- March sits on the chargers until they are inside 5 of the player line;
  contact keeps the signed-off union.
- **TankArrive** (2026-08-14) is a new phase *before* the scout. Camera
  holds the union of tank + crew + ground line while they enter. Not a
  cut. See `CAMERA_ARCHITECTURE.md` item 0.

**THE TIER STACK IS PARKED FOR THIS SITTING, not unfinished.** Tiers 0,
1.1, 1.3, 2.1–2.4 are built; 1.2 is waves only (wind parked); 1.4 heli
is shut. C / D / E below are still true and still waiting — they are
not what to open a new session with unless Rob asks.

#### C. DECISIONS WAITING ON ROB — no work starts until he calls them

- **Did the closer shot make the riot shield readable?** On the phone now. If no,
  a marker is a unit-art call on that build.
- **Overwatch Flare.** Charge is a signed-off threat. Catalog entry +
  `EnemyAI.AdvanceBudget(..., halved)`.
- ~~**Is Cluster's 3.2x spread too wide to connect?**~~ **CLOSED 2026-08-18.**
  Rob: *"ok cluster is fine."* `spreadScale` 3.2 stays.

Still open as a **fairness read**, not a constant: does losing the tank crew to a
charge feel fair, or cheap?

#### D. WARM-UPS, if a session wants one — small, bounded, and none of them urgent

- ~~**`Loadout.GroundAnchorX` averages disjoint groups**~~ **CLOSED 2026-08-13.** If the
  count-weighted mean sits inside an enemy collision box (or closer to an enemy structure
  than to any ground group) it is the gap trap, and the largest authored flank is the
  line. Do not also filter those flanks by the same box — the parade's scale-reference
  groups brush CliffOutcrop and MountainBunker, so both get thrown away and the mean
  comes back. Campaign centres are unchanged (asserted). Seen red: `GroundAnchorX 0.00`,
  4 bodies in RidgeWatchtower; then green at `-5.60`, in-ridge 0. Test rigs still skip
  the picker in play (`EnterLevel`); this is the function BalanceAudit / a future
  rig-loadout would have called.
- **Flames outlive their bodies by a frame or two** (`_plans/BACKLOG.md`). Diagnosed from
  the code 2026-08-13: flame and ragdoll share the dying entity's xyz on the same frame.
  A missing body with a flame present is either a silent ragdoll `Take` miss (now warned)
  or the die clip folding a garrison into the bunker until the impulse lifts it. The
  ragdoll / structure second mechanism (stuck on the lip) was fixed and seen on
  device 2026-08-16. Optional; not a pickup.
- ~~**Incendiary's `burnDamage = 6`**~~ **STALE, already 8.** The asset and `AmmoSetup`
  both say 8. The 6-vs-8hp-Sniper note was resolved when the burn was re-derived against
  the live roster (frailest crowd body is 12 hp; `PortSelfTest` anchors to that). Do not
  re-raise it as a warm-up.

#### E. PARKED BY DECISION — do not reopen these as stale flags

- **Wind** — `windAccelZ` drifts the round in Z while collision is X/Y only, so wind cannot change
  what a shot hits. Rob parked it 2026-08-10. Making it real is a PHYSICS change and needs an ask.
- **The heli (Tier 1.4)** — `HELI_ENABLED=false` is a camera-load decision.
- **Tier 3 habit glue** — chests and the daily bonus shipped as stubs; real ads/IAP is gated behind
  "≥5 fun sessions exist" in `PRODUCT_DIRECTION.md`.
- **The crowd split's second doubling** — CLOSED 2026-08-12 on arithmetic, not on taste. Read
  "THE CROWD SPLIT HAS NO REMAINING LEVER" below before re-deriving it: x4 is 8 hp and the
  incendiary burn is 8, so the rifleman one-shots to a single tick.

### Art sitting — 2026-08-14, Blender MCP

Rob: park the tiers briefly and improve how the game looks. This sitting did
three things. Detail is in the pickup and in `UNIT_VARIETY_DESIGN.md` /
`CAMERA_ARCHITECTURE.md`.

- **Rifleman v2** — ACH pot, plate + mag row, neck/hands/boots. Same Kenney
  joints, still the skinny class. First helmet 2.64 put the hold-pose rifles
  over their heads (`Normalize` × `AttachGun` in model units). Helmet now
  2.93. `PortSelfTest` samples the hold on the built prefab.
- **Tank v2** — glacis, visible road wheels, round barrel, bustle. Rob:
  *"looks good."* `accent_pivot_TankGun` and `accent_wheel*` kept.
- **Opening arrive** — `TurnFlow.StartBattle` / `TurnPhase.TankArrive`.
  Tank rolls 3.6, crew ride, ground line jogs the same distance on
  `MarchTargetX` + the walk clip, 2.0s, then the signed-off scout. Rob:
  *"cool!"*
- **CityRuins strip** — Rob: *"ok, looks good."* Two world-scale
  GLBs on FarZ / NearZ. Ashen charcoal (`CityRuins.asset`), not the
  imported warm brown. Facade dress (floor bands, soot, recessed
  windows), scorch stains, rubble piles on the camera side of the
  facade. Tongues on `fx_fire_*` markers in the window mouths;
  interior glow deeper; smoke from the rubble. `Backdrop.City` is
  the fallback / test contract only. Builder:
  `tools/blender/build_backdrop_city.py`. Worn by L4 and L10.
  Traps are in the pickup — re-export needs a scene rebuild.

Do not start the next biome until he names one.

### Ragdoll contact + sink — 2026-08-14/16, ON THE PHONE

Rob: twitching on death, and bodies near a structure lip hanging instead
of falling. He wanted them to **bend against** masonry. Then sink into
the dirt, then the ground twitch was still too much.

Not a new clip and not Unity `Rigidbody`s (locked: cosmetic, tick-owned).

1. **Twitch (first).** `ApplyFlail` is a sine on the limbs. The renderer
   compared Y to `RagdollRestY` (dirt, ~0.05). A garrison on a deck at
   y=2.5 was airborne for the whole 5s TTL. `SupportY` is the surface
   they landed on.
2. **Stuck on the lip.** Gravity dips a roof-sitter a hair below `topY`.
   The face test saw "spawned inside" and killed `vx`; the roof test
   snapped them back up. Roof and face are mutually exclusive. Within
   `RagdollLipMargin` (0.55) of a face they are pushed off and fall.
   `Bend` folds the torso toward the contact.
3. **Sink.** Render-only `RagdollSinkY` on the last 0.9s of the TTL,
   dirt only (`SupportY` ≤ body height). Roofs and the tank deck do not
   sink. Rob: *"the sink into the ground looks good."*
4. **Twitch (second), on the dirt.** Leftover `vx` still counted as
   airborne, so the sine ran while they slid. Airborne is height / `vy`
   only. Flail cut to 10°/5° at ~1–2 Hz. Rob: *"ok looks better."*

Contact first look: *"ok looks better."* Do not reopen as a taste pass.

### 0. WARM-UPS — 2026-08-13, NO DEVICE

GroundAnchorX and a code read of the flame artefact. No APK. Details in D; the only
lesson worth keeping out of the pickup: **a flank authored against scenery will fail
the same box test that caught the mean.** Filtering both "clear" flanks returned the
rejected average. The authored groups are the answer.

### 1. ADVANCING SQUADS + MELEE — BUILT 2026-08-12, SEEN ON A DEVICE THREE TIMES SINCE

**The eighth dead system is alive.** `AdvanceSystems.cs` ports the mechanic from the retired
Kotlin, which is the only implementation it has ever had. Enemy assault squads bank a budget on
the edge into EnemyWindup, walk at the player's line during the windup, hold just short of it, and
a fighter that arrives claims a soldier and trades itself for him.

**It activates SHIPPED DATA on FOUR campaign levels — L4, L8, L9 and L12** — not the two this file
said. Every one of them has authored `advancePerTurn` since the port and has been fielding a class
that stood still.

What is in the build:

- **`AdvanceSystems.BankBudget / March / Claim / StepSkirmishes`**, engine-independent, called from
  `BattleTick` in a new section 7b. The budget is banked ONCE on the handover edge, the same shape
  as the incendiary burn two blocks below it, for the same reason: one legible step per turn.
- **The windup countdown is FROZEN while anyone is still walking** (`BattleRunner`), so the march
  owns its own beat instead of racing the volley for the same 1.5 seconds.
- **A skirmish HOLDS the turn open.** `TurnFlow.EvaluateVolley` has taken a skirmish count since
  the port and had never once been passed a non-zero one.
- **The bodies WALK.** Kenney's `walk` is bound as a fifth clip (the melee swing is the sixth,
  2026-08-13) and plays on layer 0 in place of the idle, with the two-handed hold left on the
  arms — legs march, rifle stays carried.
  **This needed a scene rebuild**, being a prefab change.

**THE SHIELD BEARER'S 12 MELEE DAMAGE IS STILL DEAD, and this file predicted otherwise.** It said
the number "goes live the day advancing squads do". It does not, and the reason is worth keeping:
**`meleeDamage` is only ever read as a FLAG** — "does this class fight hand-to-hand" — in the
reference build as well as this one, and **a skirmish is a MUTUAL KILL rather than a damage roll**,
so no melee number is arithmetic on either side. It also only reaches the ENEMY copy, because
skirmishes are claimed by ADVANCING attackers and `LevelBuilder` pins every PLAYER unit's
`AdvancePerTurn` to 0 — which the locked turn structure requires. The player's shield bearer keeps
ARMOUR as its distinctness, exactly as Tier 2.3 gave it. `RosterAudit`'s warning has been rewritten
to say this; it used to promise the opposite.

**644 checks, and five were seen RED against the unwired tick first**, with the numbers recorded:
budget `0.00`, the marcher's x `0.03 -> 0.03` (it never moved), `0 fight(s)`. The first draft of
that check THREW instead of failing when no fight started, which aborted every check after it — a
check that explodes is not a check that failed, and it now reports red and returns.

**TWO THINGS THE FIRST DEVICE BUILD FOUND, both fixed the same session.** Rob played it and
reported exactly two problems, and each was a system that had been PORTED and never CONNECTED —
the same shape as the mechanic itself:

1. **"We need to see the melee/assault force attacking the line. That happens off camera and it's
   weird."** `CameraDirector.PhaseHalfWidth` has had a marcher branch since the port and
   `BattleTick` fed it **`0f, false` from a literal**, while the windup anchor was the level's
   fixed ENEMY-side value. So the camera watched the shooters standing still while the assault
   walked into the player's line off the left edge. Fixed by feeding it the real march and
   skirmish sets and porting `EnemyWindupAnchorX` — **three beats**: ride the march, then HOLD on
   the skirmish line until every fight resolves (an engaged attacker is no longer a marcher, so a
   target built from marchers alone snaps away ~1s before the mutual-kill payoff), then pan back
   to the RANGED shooters, who are the ones about to fire. Seen red: camera pinned at the enemy
   anchor `4.53` with the marchers at `-0.72`.
2. **"The player standing on the tank never gets touched by the assault force."** True, and it
   made the whole mechanic toothless: the reference build exempts anyone standing on a structure,
   which is right for the ENEMY side (every garrison is on a wall or tower) and wrong for the
   player's, whose only garrison is the TANK CREW at **0.60** up on a vehicle. Kill the ground
   line and the chargers had nobody left they were allowed to touch, so melee could never lose the
   battle. **Reach is now a HEIGHT, not a flag** — `AdvanceSystems.MeleeReachHeight = 1.0`, read
   off the unit's own Y so it cannot disagree with what is drawn. The measured gap is wide: the
   tank deck is 0.60 and every enemy structure is 1.40, 1.63, 2.50 or 3.75. The hold line moved to
   "front-most REACHABLE body" for the same reason. Seen red with every ground unit removed: the
   crew survived 900 ticks untouched, `2 -> 2`.

**AND A THIRD, from the build that fixed the first two:** *"when the actual melee attack takes
place, the camera should stay on that until it's complete."* Holding the fight inside the WINDUP
branch was not enough — **a skirmish spans phases** (the handover gate waits for it by design), so
a fight still running when the windup ended handed the frame to the volley chase, which is by
definition somewhere else on the field. The fight now owns the camera — anchor AND framing — in
any phase, outranking the volley chase rather than averaging with it, and **the windup countdown is
frozen while a fight runs** as well as while a march does. That second half is not optional: with
the volley free to fire over a running scuffle, the camera is locked on the melee exactly as a
dozen rounds leave the far side of the field and the player sees neither. The sequence now always
reads **march -> fight -> volley**, one at a time.

**AND A FOURTH:** *"we need to focus the camera on the whole attack so the player can see what's
happening to their force."* Holding on the fight framed the fight and nothing else — on L4 that put
the camera at x -5.1 with a half-width of 4.0, covering -9.1 to -1.1: **half the picture is empty
ground to the RIGHT and the TANK CREW at -9.59 is cropped out**, which is precisely the force the
player wants to watch being attacked. Contact still frames that UNION. The MARCH no
longer does: sitting back for the whole field made L12's escort a speck (Rob,
2026-08-13, on the armour). Far chargers own the frame; the threatened front enters
inside 5; contact takes the union. Seen red at `cam -5.24 ±4.00` against a rear rank
at -9.59.

**AND A FIFTH:** *"we still are in a hurry to zoom back to the main force. we need to show the
melee assault the whole time and pause so it registers with the player."* The camera was released
on the TICK the skirmish list emptied — so the payoff, the two bodies actually falling, played
while the camera was already leaving. Measured: half a second after the last pair fell the camera
had travelled from **-7.52 to +3.53**, eleven units away.

`GameState.MeleeHold` is a post-melee dwell of **1.5s**, the same family as `TurnHandoverDelay`
(which exists because the handover used to tread on an impact the player was still reading) and
sized against `PostVolleyPauseSeconds`' 1.6s. Three things about it are load-bearing:

- **The frame is CARRIED, not recomputed.** Once the fight is over its participants are gone from
  the unit lists, so a recomputed frame snaps to whatever is left on the tick the hold begins —
  the exact lurch the hold exists to prevent. The anchor and half-width are captured on the last
  fighting tick.
- **It decays on EVERY tick path, including the cosmetic one.** A melee mutual kill can be the blow
  that ENDS the battle, and a hold left frozen on the victory screen is a value that never decays
  again — the standing rule in CLAUDE.md, and this is the first thing to hit it since the flame.
- **The volley is held off for the duration.** Otherwise the shooting starts while the camera is
  parked on two bodies falling — the same mistake the windup freeze was added to prevent, one beat
  later.

**654 checks.** All five fixes were seen red against the build Rob played, with the numbers above.
The framing check asserts **CONTAINMENT, not proximity** — the camera deliberately does not sit on
the fight, so a distance test would fail the correct behaviour; and the half-width it measures is
recovered from `CameraFollowZ` through `TargetZ`'s own inverse rather than re-derived, so it cannot
drift from the camera the game actually uses.

**AND ONE CHECK THAT PASSED AGAINST THE BUG IT WAS WRITTEN FOR**, which is the standing lesson
turning up again in a new costume. The camera check first seeded the camera ON the fight and
stepped ONE tick — a spring that has not had time to move is not evidence of a spring that stayed,
and it went green against the reverted code. Stepping 40 ticks (two thirds of a second, short of
`SkirmishDuration`) made it real: **cam 3.92 against a fight at -5.12**, nine units away. `PUT A
CHECK IN A STATE WHERE IT COULD FAIL` is in CLAUDE.md and it still took a revert run to catch.

**ROB'S VERDICT AFTER THE FIFTH BUILD: "ok, better. we'll refine in another session."** That
session ran 2026-08-13. **The mechanic is signed off.** The three constants and the swing stay.
What that refining session looked at:

1. **`AdvanceSystems.PostMeleeHoldSeconds` (1.5s)** — the dwell was judged once, at one value. It
   is one constant and the most likely thing to be wrong.
2. ~~The fight has no ANIMATION.~~ **THE SWING SHIPPED 2026-08-13 and was confirmed on device** —
   see "The melee swing" below. Still no blood: the Kotlin's blood debris takes its colour from a
   STRUCTURE definition and does not port cleanly.
3. ~~**The march's own pacing**~~ **SIGNED OFF 2026-08-13.** `AdvanceSpeed` 2.4 and the frozen
   windup stay. Rob, L4: the march is fine. **The GAIT was reopened 2026-08-24** — the pacing
   was never the complaint, the legs were. Speed untouched; see the 08-24 block at the top.
4. **Overwatch Flare**, which this unblocks. The advance is now a signed-off threat, so a
   counter is a real product call rather than a guess.

**THE SECOND DEVICE PASS RAN ON 2026-08-13**, alongside the melee swing — see "The melee swing"
below. The third pass the same day signed the three constants. The questions:
- ~~is the fight legible now that the camera holds on it?~~ **Yes** (2026-08-13).
- ~~does it hold for the right length?~~ **Yes** — 1.5s stays (2026-08-13).
- **does losing the tank crew to a charge feel fair, or cheap?** Still open. Melee can END the
  battle; the crew is the last thing standing on most levels. Not a constant — a fairness read.
- ~~does a squad walking at the line read as PRESSURE, or as men wandering forward?~~ **The
  march is fine** (2026-08-13).
- ~~is the frozen windup a beat, or a stall?~~ **Fine** with the march (2026-08-13).
- ~~does a mutual kill read as a fight, or as two bodies falling over at once?~~ **A fight.**
  Both ends swing. Still no blood.

**OVERWATCH FLARE IS NOW UNBLOCKED** — the one Tier 1.3 consumable deliberately not built, because
it had nothing to watch for. `EnemyAI.AdvanceBudget(basePerTurn, halved)` is called with `halved:
false` from one place; the consumable is a catalog entry plus that one bool. **Judge whether the
advance is threatening BEFORE building its counter** — a counter to a mechanic nobody fears is
worse than no counter.

### THE MELEE SWING — shipped and CONFIRMED ON DEVICE, 2026-08-13

The first item on the refinement list above, and the one it called "the biggest remaining gap
between the mechanic is real and the mechanic reads". `attack-melee-right` is bound as a SIXTH
clip and both ends of every skirmish now swing.

**What it took, in case a seventh clip is ever wanted: `UnitAnim.Melee` + one entry in
`RiggedUnits.Wanted` + a `Layer()` line + a scene rebuild.** That is the whole cost, and it is the
same shape `walk` paid the day before.

- **Layer 2, WrapMode.Loop, SHARING the layer with `shoot`.** They are alternatives rather than a
  stack — a man swinging a rifle butt is not also firing it — and Legacy resolves same-layer clips
  by whoever played last. They never compete in practice anyway: the volley is held off for the
  duration of a fight.
- **`SetFighting` fades OUT rather than stopping**, and that is not tidiness. **A fight does not
  always end in a death**: kill the attacker mid-scuffle and his victim is spared, which is the
  mechanic's whole counter-play, so a SURVIVOR has to put his arms down. A hard Stop on a looping
  clip drops him into the hold in one frame.
- **BOTH ENDS SWING.** `BattleRunner` tests `AttackerId == u.Id || VictimId == u.Id`, which covers
  the enemy charger and his claimed victim from one call without knowing which side it is drawing
  — ids are unique across the two sides, `LevelBuilder` gives the enemy its own base. The victim
  fighting back is the point: a skirmish is a MUTUAL kill, and a man standing at ease while
  someone kills him reads as a bug rather than as a trade.
- **THE AIM IS SUPPRESSED WHILE FIGHTING.** `SyncUnits` hands the player's whole line one aim pose
  and does not know a man is busy, so without this his victim holds the live drag elevation
  through the entire scuffle.
- **`attack-melee-right` DRIVES THE ROOT** — a ±0.10 lunge in local Z, the step into the strike.
  Third clip to do it after `die` and `walk`, so it takes the same exemption from `LateUpdate`'s
  root clamp; clamping it deletes the step and leaves a man swinging from the waist.
- **A locked fighter no longer counts as WALKING.** That was the best available answer while the
  swing was unbound. The melee clip drives the legs itself and outranks the walk from layer 2, so
  the march now means only the march.

**656 checks.** Two new ones, and the swing check **failed its own control on the first run**,
which is the whole reason it carries one: it measures the right arm's travel and compares it
against `holding-both`, a STATIC two-handed pose that must read ~0 by the same ruler. The first
draft pooled the quaternion's x/y/z/w into one range — which measures the POSE, not the motion —
and read the constant hold as a **90.0 degree swing**. Per component it is 0.0 against the melee's
59.4. A travel measurement that cannot report zero is not a measurement.

**CONFIRMED ON DEVICE, L4 Ash Boulevard, 2026-08-13.** Five turns of real drags to contact, then
the assault reached the line: engaged bodies are unmistakably in a different pose from the rank
behind them — legs split wide, weapon up over the head — against a squad still holding the static
firing stance four units away. The battle ended **DEFEAT — "Your line was overrun"** on turn 9,
which is the melee killing the player outright, so the reach fix from the previous session holds
under real play as well as in a check.

**WHAT THE BUILD SHOWED THAT IS STILL OPEN**, and both are Rob's calls rather than bugs:
- **The fight cluster INTERPENETRATES.** `GrappleGap` was 0.30 and the bodies visibly overlapped
  at contact — a scrum rather than pairs. Trial is 0.75 (Rob, 2026-08-13). The march still holds
  at 0.55; only a lunge to a deeper victim uses the new gap.
- **Still no blood.** The Kotlin's blood debris takes its colour from a STRUCTURE definition, so it
  does not port cleanly; nothing marks the moment of the kill except the two ragdolls.

### 2. WAITING ON ROB, not on anyone's time

**L12's Sovereign stays in the gate's shadow.** Rob, 2026-08-13: *"sovereign is fine on
l12."* The one-field fix (`triggerStructureIds: [citadel, gate]`) is still supported and
still changes what the finale demands (~280 masonry vs ~288 stock siege). Do not apply
it. He spawns at x 5.42; the gate box is `x[1.25,3.75]`, top 2.00.

Note this is a SHADOW, not an embedding, and rule 8 does not flag it — correctly. The crude "is it
behind a taller box" heuristic used to find it fires on plenty of harmless geometry (L10 has
arrivals 5.94 clear of a 1.40 box). Deciding which shadows are real needs the game's own
trajectory solver, not a ratio someone invented. **That is a rule 9 and it does not exist.**

**The shield bearer's armour is a CAMERA problem, not a missing decal.** Rob, looking
at it: *"it's zoomed so far out i can't see it. we need to do better about zooming in
in that scenario."* Half-damage is still real and still has no icon
(`CollisionSystem.Soaked`, `40hp x2.0 armour = 80`). The closer frames are what this
sitting built; a marker is still a unit-art call if those are not enough.

### 3. TIER 2.3 IS MECHANICALLY DONE AND HALF-LEGIBLE — what the device actually showed

Both changes were verified on device on 2026-08-12, and both hit the same wall: **the mechanic is
real and the player cannot see it.**

- **The burst is live and now FANS.** Confirmed by measurement, not by eye: four machine gunners
  put 1.83x the tracer area per shooter of a rifle squad, so six shooters out-tracered ten. It was
  invisible because all three rounds shared one jitter value across Vx and Vy and flew down the
  same 45 degree line. **Fixed** — two independent draws. **The fan has NOT been looked at on a
  device**, and that is the one thing this owes: does three rounds now read as suppressing fire?
- **The armour is live and invisible.** Equal-size squads on L3, four Auto turns each, identical
  enemy attrition (15 -> 11 both runs): 6 shield bearers + 2 crew lost ONE body, 6 riflemen + 2
  crew lost NONE. **That is not evidence the armour is broken** — one death against zero over four
  turns is noise, and Auto changes which enemies survive to shoot back. It is evidence that
  doubling a unit's effective HP produced nothing a player could perceive.

### 4. SMALL AND WELL-DEFINED, if a session wants a warm-up

- ~~The advancing exemption never verifies the unit leaves.~~ **CLOSED 2026-08-12**, alongside the
  squads that made it load-bearing. Rule 8 now exempts an advancer only if it clears the box on its
  FIRST march; one that clears eventually is a **WARNING**, and one that never clears is the Error
  it always was. The severities mean different things and the blanket exemption conflated them.
  **It found one case immediately: L12's boss shield escort** starts 1.91 inside the gate's box at
  1.20/turn, so it takes two marches to become hittable. Left as a warning rather than fixed —
  it is the same gate as item 6 and the same beat Rob signed off, so it is his call, not a
  data edit to make quietly. `PortSelfTest` fails on Errors only now and LOGS the advisory.
- ~~**`Loadout.GroundAnchorX` averages disjoint groups.**~~ **CLOSED 2026-08-13.** See D.
- **Wind is still cosmetic and PARKED** (Rob, 2026-08-10). `windAccelZ` drifts the round in Z while
  collision is X/Y only, so wind cannot change what a shot hits. **Do not author a wind level until
  someone decides whether collision goes 3D.**
- **Tier 1.4 (Heli) stays shut.** `HELI_ENABLED=false` is a camera-load decision, not a stale flag.

### 5. WHAT 2026-08-12'S THIRD SESSION TAUGHT, which is one lesson four times

**A check asserts the slice of the world it happens to look at, and nobody notices the rest is
unexamined.** Four instances in one session:

1. **Rule 8 read turn 0 only** — boss phases and reinforcement waves were invisible, and four
   embedded units had shipped across L6/L10/L11. Found by Rob playing L12.
2. **Rule 7 read turn 0 only** — same hole, same fix, now closed. A wave authored past the
   ballistic envelope passed every check in the project.
3. **The burst check asserted "each round on its own jitter"** and passed for a day against
   collinear rounds, because distinct aims was already true of the broken version. An INPUT
   assertion wearing the costume of an output one.
4. **The glyph check never covered the victory banners**, and `"New 3★ Best!"` had been drawing a
   missing-glyph box on the congratulations screen the whole time — carrying the exact codepoint
   the same check uses two lines earlier to prove it can fail.

**And one lesson about the fixer, not the checks: DO NOT RE-DERIVE WHAT A TOOL ALREADY COMPUTES.**
While fixing rule 8 this session I hand-rolled a reach estimate — `x - backRank > 20.25` — decided
L11's wave could not be moved behind its post, and moved the whole wave to the far side of the map,
changing a signed-off beat. `BalanceAudit.ReachRule` puts that same body at 91% power from the
front rank and 99% from the back: **it was always in reach.** The estimate ignored `dy` and the
launch envelope, `v² = g(dy + √(dx²+dy²))`; 20.25 is the flat `dy = 0` case and nothing else. The
wave is back at anchorX 9 and the beat is intact. **Ask `ReachRule`. It is the only implementation
that counts**, and this happened in the same session that added a rule whose entire justification
is not re-deriving placement.

**A YAML footnote that cost a real scare:** appending to a level's `designNotes` inserted
unescaped apostrophes into a single-quoted scalar, and `Oceanfront.asset` and `RubbleYard.asset`
stopped parsing. **It hid because a failed import falls back to the Library cache** — the report
kept showing all twelve levels green off stale data, and a reimport on a clean checkout would have
loaded those levels with default fields. Double apostrophes inside single-quoted YAML, and treat
"Unable to parse" in a batch log as a failure even when the run says 0 errors.

### Rule 8 now covers MID-BATTLE ARRIVALS — 2026-08-12, and it found four shipped bugs

Rob, playing L12 on a fresh build: *"there are enemies behind the structure, making them
impossible to hit unless you destroy it. which you can't do if you don't have any tank rounds
left."* He was right, and the cause is that **rule 8 only ever read `BuildInitialState`** — every
boss phase and reinforcement wave was invisible to it. Third instance in one day of the same
shape: a check that asserts only the slice of the world it happens to look at.

Extended to judge turn 0 plus every arrival, placed through the same `LevelBuilder.BuildUnits`
call `BattleTick.Spawn` uses. **Seen RED against the shipped data first**, which is how the four
were found:

| level | arrival | was |
|---|---|---|
| L6 boss | 2 of 3 heavy escort | inside MountainBunker `x[0.88,3.13]` — not the phase's trigger, so still standing |
| L10 wave t4 | 1 of 4 heavies | inside GarrisonPost, by 0.09 |
| L11 wave t4 | 1 of 3 heavies | inside GarrisonPost, by 0.71 |
| L12 boss | the Sovereign | **not embedded** — shadowed by the gate. STILL OPEN, item 6 |

**A boss phase's own trigger structures are exempt for that phase.** L12's Sovereign spawns dead
centre of the citadel it bursts out of, and flagging that would assert a state the game can never
be in. A wave has no trigger, so it is judged against everything standing.

**The fixes.** L6's escort moved 3 -> 4.5 and its Sovereign 5 -> 6.5 (both now emerge from the
breached keep's footprint, which is rubble by then). L10's wave 9 -> 9.4. **L11's wave 8 -> 9**,
which clears the box edge (7.88) by 0.28. All three keep their beats.

**L11 TOOK TWO GOES AND THE FIRST ONE WAS WRONG** — see the "do not re-derive" lesson in the pick-up
section. It was briefly moved to anchorX 0, in FRONT of the post, on a hand-rolled reach estimate
that said there was no room behind it. `BalanceAudit.ReachRule` puts that body at 91% power from
the front rank and 99% from the back: it was always in reach, and the beat did not need to change.

**RULE 7 NOW COVERS ARRIVALS TOO** (same session, second commit) — it had the identical turn-0
hole. Each boss phase and wave is measured ALONE through `ReachRule`, so a finding names the wave
rather than re-reporting whichever turn-0 body is deepest. Seen red at anchorX 16: "108% power,
UNWINNABLE".

**ONE LATENT HOLE DELIBERATELY LEFT**, recorded in `LEVEL_AUTHORING.md`: **the ADVANCING exemption
does not verify the unit actually leaves.** L11's wave, had it been given an advance instead of
moved, would have started 0.71 deep and needed three turns at 1.2 a turn to clear the box.
"Advancing" is not the same claim as "hittable soon".

**WIND IS PARKED** — Rob's call, 2026-08-10, and it is the only thing Tier 1.2 still owes. Work
continues around it rather than waiting on it.

**1. GET ROB'S EYES ON THE TIER 2.3 CHANGES.** *(Half done — the armour was measured on device
2026-08-12 and the burst's FAN still has not been seen in motion. The live version of this item is
B in "Pick up here"; what follows is the reasoning behind it.)* Both change how a battle FEELS
rather than how it reads in a table:
- **The machine gunner now fires three rounds a volley, each on its own jitter.** The thing to
  judge is whether a squad of them reads as suppressing fire or as noise — three times the rounds
  in the air is the biggest change to what a volley looks like since cluster ammo.
- **The shield bearer now takes half damage.** Nothing on screen says so. Its health bar simply
  falls slower, and if that is illegible the mechanic is real but invisible — which is the
  failure mode `UNIT_VARIETY_DESIGN.md`'s "honest limit" is about. **A visible marker was
  deliberately NOT built**: adding one is a unit-art change and those are decided on a device,
  never in advance.
  **And armour is now the class's PERMANENT mechanic, not a stand-in.** Melee shipped on
  2026-08-12 and did NOT give the player's shield bearer anything: `meleeDamage` is read as a flag
  and only on the enemy copy — see item 1.
Buy both with RIGS on (free supply), and note it takes 250 and 500 coins to reach them otherwise.

**2. THE CROWD SPLIT HAS NO REMAINING LEVER. CLOSED 2026-08-12 — do not re-open it on the old
note.** This entry used to read *"doubling again (rifleman x4 at 8hp/2) is exact and burn-safe, so
'more' is a one-line change to `CrowdSplit.Factors`."* **It is exact and it is NOT burn-safe**, and
the paragraph directly below it said so all along about the sniper — same number, opposite
conclusion, two paragraphs apart. Rob's call on being shown the arithmetic: leave the split at x2.

- **The rifleman is HARD-CAPPED AT x2 by the burn.** x4 is 8 hp; the incendiary burn is 8;
  `BattleTick` does `hp = u.Hp - burnDamage; if (hp > 0)`, so 8 - 8 = 0 and the body dies to one
  tick. `CrowdSplit`'s `maxHp / factor <= BurnDamage` guard already rejects it, and
  `PortSelfTest`'s `burn < frailest` anchor would go red as the frailest unit fell 12 -> 8. The
  rifleman carries almost every garrison in the game, so this is most of the lever gone.
- **The only class that CAN double again is the machine gunner** (x4 = 10 hp / 1 dmg, exact, above
  the burn) — and the locked 7-30 roster blocks three of its six decks: L10 would go to 31, L4 to
  35, L6 to 37. Only L5 (24), L9 (30, at the cap) and L11 (22) fit.
- **And those six decks share ONE `EnemyMachineGunnerCrowd` asset**, so re-splitting three of them
  means editing the asset the other three read, silently halving HP on levels Rob has signed off.
  It would need a second `...Crowd2` variant — two machine-gunner bodies in one game — for a
  change visible on three levels. Not worth it.

**The standing lesson here is the one this file keeps paying for: a number written into prose goes
stale, and a DERIVED number written into prose is stale the moment either input moves.** The
burn-safety of a factor is arithmetic over `burnDamage` and `maxHp`; it belongs in `CrowdSplit`'s
guard, where it already lived and was already correct, not in a sentence.

Also worth knowing: **the sniper is deliberately not split** (16 hp only halves to 8, into the same
burn problem), and **L12's gate is deliberately not split** (both its garrisons would take the
roster to 31, past the locked 7-30). Both are enforced in `CrowdSplit`, not just remembered.

Before any further crowd/art pass, read `UNIT_VARIETY_DESIGN.md`'s "honest limit" twice: stance,
faces and limb fold each cleared "is it correct" and failed "does it survive the frame", and the
only changes that ever DID read were large-scale layout.

**3. RULE 8 NOW REPORTS WHERE THE OTHER SEVEN DO — DONE 2026-08-12.** It lived only in
`PortSelfTest.CheckNobodyStandsInAWall`, so it failed the SUITE while rules 1-7 rendered live in
`LevelDefinitionInspector` beside the level being edited; an author saw seven rules where there
are eight. It is now `LevelComposition.CollisionBoxRule`, called from `LevelComposition.Check`,
and the suite check DELEGATES to it rather than re-measuring — one rule with two implementations
is the second-source-of-truth failure this project has already paid for, and is why rule 7 is
called out of `BalanceAudit` instead of copied.

It reports as an **ERROR**, the only non-roster error in the file, so `LevelComposition.Report`
now exits 1 on it exactly as the suite always did. Rules 1-6 are framing judgements a level may
bend for a reason it records in `designNotes`; rule 8 says a unit the player is asked to kill
cannot be hit.

**Seen RED before being trusted**, per the standing rule: L6's two heavy riflemen moved from
x -1 to the keep's x 6 put them 1.73 INSIDE the box, and that failed `LevelComposition.Report`
(exit 1) and `PortSelfTest` together, naming `FortressTier edge 3.88` — the same edge from the
original bug. Restored, and both are green again at **628 checks**, unchanged by the move.

**4. THE TWO BIGGEST OPEN THINGS, both physics/AI asks rather than scheduling jobs:**
- **Advancing squads + melee are unported** — an EIGHTH dead system, and the one that holds
  Overwatch Flare. `AdvanceRemaining` is written nowhere and `SkirmishEntity` is never created.
  `PROGRESSION_DESIGN`'s whole survival/defend archetype is made of this.
- **Wind is still cosmetic** — `windAccelZ` drifts the round in Z while the collision test is X/Y
  only, so wind cannot change what a shot hits. A wind schedule would telegraph a change the
  player cannot feel. **Do not author a wind level until someone decides whether collision goes 3D.**

**6. L12'S SOVEREIGN IS STILL IN THE GATE'S SHADOW — ROB'S CALL, HELD DELIBERATELY.**
He spawns at x 5.42, static, and the **gate** (`FortressTierSmall`, box `x[1.25,3.75]`, top 2.00)
is still standing 1.67 to his left. Hitting him means clearing a 2.0 wall and dropping 2.0 within
1.67 of travel. It is **not unwinnable** — a rifleman does 8 x 0.25 = 2 masonry, so seven of them
strip the gate's remaining hp in a few volleys — but the requirement is invisible and is
discovered only after the tank shells are gone.

The proposed fix is ONE FIELD: `triggerStructureIds: [citadel, gate]`, so the Sovereign emerges
only once both are down and nothing shadows him. Already supported —
`ShouldTriggerBossPhase` does `triggerStructureIds.All(isDefeated)`. Worth knowing before taking
it: citadel 165 + gate 115 = 280 against a stock siege capacity of ~288, so it makes the
requirement HONEST rather than roomy. **Do not apply it without Rob** — it changes what the
finale demands.

Note this is a SHADOW, not an embedding, and rule 8 does not flag it: the crude "is it behind a
taller box" heuristic used to find it fires on plenty of harmless geometry (L10 has arrivals 5.94
clear of a 1.40 box). Deciding which shadows are real needs the game's own trajectory solver, not
a ratio someone invented — that is a rule 9, and it does not exist.

**5. Tier 1.4 (Heli) stays shut.** `HELI_ENABLED=false` is a camera-load decision, not a stale
flag. Do not flip it.

**THE AIRSTRIKE IS DONE AND SIGNED OFF** ("ok this will work"). One small open note: the aircraft
*"gets fairly large as it passes nearest the camera"* — `BattleTick.PlaneY` is a one-constant fix
if it reads as too much, judged at full speed rather than on a contact sheet. The pass-by sound is
explicitly closed: *"the aircraft sound is fine for the moment."*

**THE PLAYER HAS NO HERO ANYWHERE, and that is a decision** (Rob, 2026-08-11: enemy-only for now).
`HeavyRifleman` is not among the six pickable roster slots. Revisit only with a build in hand.

**Device state at handover:** the installed build is **CURRENT** — advancing squads, melee, and all
five camera/reach fixes from the 2026-08-12 assault session. Installed by uninstall/reinstall, so
the economy is at a **fresh zero** and RIGS is the way to reach anything buyable. Checked and left
CLEAN: stay-awake off, DND off, auto-rotate on, no captures on `/sdcard`. **Nothing is committed** —
the whole assault session is in the working tree.

<details>
<summary>The Tier 1.3 briefing as it stood before the work — kept because its reasoning is the
standing lesson, not because it is current</summary>

1. **TIER 1.3 IS THE NEXT TIER ITEM, AND ITS FIRST HALF IS NOT WHAT THE TABLE SAYS.**
   `PRODUCT_DIRECTION.md` calls 1.3 "tactical consumable expansion — Smoke / Overwatch", gated on
   *"when base consumables already feel good"*. **In this port there are no base consumables at
   all.** `ConsumableType`, `ProgressStore.OwnedConsumables/AddConsumable/SpendConsumable`,
   `EconomyStore.PurchaseConsumable` and `GameState.LoadedConsumables` are all ported — and a grep
   for callers outside those files returns **NOTHING**. No UI, no arming, no spend, no effect.

   **This is the SIXTH dead system** (see "assume NOTHING is wired"). The design docs say
   "shipped" because their Status tables record the **RETIRED ANDROID BUILD** — `PROGRESSION_DESIGN`
   Phase 2 and `DYNAMISM_DESIGN` Phase C both shipped there on 2026-07-20. **Check the Unity
   callers, not the status table**, and treat those docs as the SPEC for what to build here rather
   than as a record of what exists here.

   So 1.3 is really: **wire the three base consumables (Airstrike / Early Reinforcements / Trauma
   Kit) first**, then Smoke / Overwatch on top. The specs are `PROGRESSION_DESIGN.md` Phase 2 and
   `DYNAMISM_DESIGN.md` Phase C, including the cap-2-per-battle rule and the arm-vs-spend
   distinction (Airstrike's toggle pattern, not Trauma Kit's).

</details>

### Also standing, and not on the list above because none of it is a next task

- **Tier 1.1 is CLOSED.** Cluster's 3.2x spread is fine (Rob, 2026-08-18). The last
  leftover is cosmetic and parked in `_plans/BACKLOG.md`: **flames outlive their
  bodies by a frame or two** at the moment the burn kills (diagnosed 2026-08-13 as
  ragdoll/structure or a silent `Take`, not a second flame position).
- ~~A per-frame NullReferenceException on the LOADOUT screen.~~ **FIXED 2026-08-10** — the tick
  running with no battle to tick. That screen is clean, which matters because the consumable UI is
  built on it.
- **`_plans/BACKLOG.md` is the only LIVE plan**, and holds what Rob has parked: a **nuclear
  reactor structure** (the open question is what MECHANIC it owns — a blast on destruction would
  make it the first structure with one), a **crowd-runner bonus level**, **mid-ground scenery
  variety**, and the **look-pass** (next biome/unit unnamed). Sink is signed (08-16). The
  ragdoll / structure / mid-air hang is signed (08-18/19). The three finished plans moved to
  `_plans/archive/` on 2026-08-11; nothing there describes current behaviour.

### How the rest of this file is ordered

Everything below "Pick up here" is HISTORY, newest first: the 2026-08-17 winter/forest
rework, then the 2026-08-13 warm-up sitting, then melee / opening scout, then the two
2026-08-12 sessions (the Tier 2.3 audit, then Tier 2.2's crowd half), then the three
2026-08-11 sessions, then 2026-08-07 to 08-10,
then the standing reference sections — **"Where things are", "What works", "The workflow",
"Traps already paid for"** and **"Open items"/"Things that will bite"**, which are the
parts that are still TRUE rather than still interesting. The closed 2026-08-05/06 port
entries are in `HANDOVER_ARCHIVE.md`.

### The Tier 2.3 audit — 2026-08-12. Three of six classes were not what they were sold as, and two are fixed

**Tier 2.3 is "keep the roster mechanic-distinct", and the honest way to read it is not from the
data sheets.** Six classes differ on paper; the .asset files carry seven distinguishing fields
between them. The product question is what the player gets when they FIRE, and in this build those
were three different things: a field only differentiates a class if some live path reads it.

`RosterAudit.Report` is the instrument, and it is the "assert the OUTPUT, not the input" rule
pointed at the roster — it fields an all-of-one-class squad, fires a REAL volley through
`BattleTick.FireVolley` (the function the drag calls), and measures what comes out. Run it:

```bash
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod RosterAudit.Report -logFile -
```

**WHAT IT FOUND, and none of it was visible in the assets:**

| class | pt | coins | hp | delivered per shooter, per volley |
|---|---|---|---|---|
| Rifleman | 1 | 0 | 32 | 1 round x 8 = 8 dmg, 2 masonry |
| MachineGunner | 2 | 250 | 40 | **was 1 round x 4**, now 3 x 4 = 12 dmg, 3 masonry |
| Grenadier | 2 | 350 | 24 | 1 x 6 = 6 dmg, 12 masonry, 0.9 splash |
| Sniper | 2 | 400 | 16 | 1 x 20 = 20 dmg, 10 masonry |
| ShieldBearer | 2 | 500 | 40 | 1 x 4 = 4 dmg, 1 masonry, **now x2 armour = 80 effective hp** |
| RocketTrooper | 3 | 700 | 24 | 1 x 4 = 4 dmg, 24 masonry |

- **THE MACHINE GUNNER'S BURST HAD NEVER REACHED THE PLAYER.** `projectilesPerVolley` was read by
  `AutoFire` and by nothing else, so the class the store sells for 250 coins as *"fires a burst
  instead of a round"* fired ONE round: a rifleman at half damage for twice the points.
  **It measured IDENTICALLY to the shield bearer on every axis** — which is precisely the failure
  Tier 2.3 exists to forbid. **FIXED**, and the jitter moved inside the burst so three rounds land
  as three (one jitter for all three would be one round doing triple damage, which is not what the
  copy promises). This is the fourth member of a family the code comments right beside it already
  describe: `Type`, `SplashRadius` and `StructureDamageMultiplier` went missing from the player's
  volley the same way and were fixed earlier. **When one field turns out to be read only by the
  debug driver, audit the whole record — not that field.**
- **THE SHIELD BEARER SOLD A MECHANIC THIS BUILD DOES NOT HAVE.** `meleeDamage` has no runtime
  reader at all, `SkirmishEntity` is never constructed, and `LevelBuilder` pins every PLAYER
  unit's `AdvancePerTurn` to 0 — so the 500-coin class advertised as *"walks forward and fights
  hand to hand"* could not advance even in principle. It was a rifleman with +8 hp and -4 damage.
  **FIXED with ARMOUR** (Rob's call): `damageTakenMultiplier` is 0.5 on the player's shield
  bearer, so it takes half of every round and presents 80 effective hp against a rifleman's 32,
  while dealing half the damage. That is a real trade rather than a stat tweak — over a long fight
  it out-damages a rifleman (80/8 = 10 volleys x 4 = 40, against 32/8 = 4 x 8 = 32) and over a
  short one it loses. **`meleeDamage` is now dead DATA rather than a missing mechanic**, and goes
  live if advancing squads ever land.
  Applied in `CollisionSystem.Soaked`, at BOTH damage write sites — the direct hit and the splash.
  A multiplier on one path only is precisely how the incendiary burn and the structure multiplier
  each went missing in this port, and the check asserts both plus the FLOOR: a soaked round never
  rounds to 0, because that is not toughness, it is immortality, and a battle that cannot end is
  not something a damage assertion would ever catch.
  **The ENEMY shield bearer is deliberately NOT armoured** — four campaign levels field it (L4,
  L8, L9, L12) and arming it would make signed-off content harder, the same call Rob made on the
  enemy burst.
- **`bulletVariant` is dead data.** A three-value enum (Standard/MachineGun/Sniper) authored on
  every unit, carried on the projectile record, and read by no runtime code. It distinguishes
  nothing, mechanically or visually.
- **Enemy machine gunners fire one round while declaring three.** `FireEnemyVolley` has the
  player's old bug. Left alone DELIBERATELY: fixing it triples those bodies' output across seven
  signed-off levels, which is a balance decision, not a bug fix. **DECIDED 2026-08-12: leave the
  enemy at one round.** The campaign is tuned against what the engine actually throws, and
  `BalanceAudit` now counts each side by its own fire path rather than by the assets — the player's
  volley WITH the burst, the enemy's without.

**THE METRIC WAS WRONG BEFORE THE FINDING WAS.** The first domination check rated classes per
POINT and duly condemned the machine gunner immediately after its burst was restored. That is the
tell: *a metric that indicts the thing you just fixed is the wrong metric.* Slots cap BODIES and
points cap QUALITY, and which one binds MOVES THROUGH THE CAMPAIGN — every level has 8 slots while
the budget climbs 8 -> 16, so L1-L2 run at parity (a premium pick is paid for in bodies, per-point
is the honest lens) and from L3 the budget outruns the slots (the line is full whatever you pick,
so per-SLOT is). The audit now reports both and only errors when a class loses on both.

**And the first RUN of the audit was wrong too, in a way worth keeping:** it reported the 4-damage
machine gunner as doing 5.3. `Loadout.ToPlayerGroups` keeps the level's garrisoned player groups —
the tank crew — because the loadout is forbidden to touch them, so every class's measured volley
silently carried the same two riflemen and the average measured the garrison as much as the class.
**A per-unit average over a squad you did not fully control is measuring the level, not the unit.**

### What 2026-08-12 changed — Tier 2.2's crowd half, and an approach that died to arithmetic

**The measurement came before the edit, and it is what saved the session.** The obvious fix for
"a small clump on a wide deck" is the mirror of the cause — the body shrank 0.77 -> 0.48 and the
buildings did not, so shrink the buildings. Rob picked that option. **`DeckFillReport` then killed
it in one run**, and the numbers are worth carrying because the reasoning generalises:

- **No single factor works**: the decks are already 4.6x inconsistent with their own garrisons
  (L6's MountainBunker 69%, L12's FortressTierWide 15%), so any global shrink overflows the tight
  decks before it fills the loose ones.
- **Per-structure factors are worse.** For each deck's CURRENT garrison to fill it, GarrisonPost
  needs **x0.22** and FortressTierWide **x0.21**.
- **And the scale is uniform** — `LevelScenery` draws every enemy structure as
  `Vector3.one * worldScale`, there is no per-axis squash — so GarrisonPost at x0.22 stands **0.55
  units tall against a soldier's 0.48**. The building would be the size of the men on it, and
  rule 3 is built on there being one dominant structure.

**The inversion is the part to keep: our decks are already sized like the reference's.** A 3.13
deck seats 17 per rank at the derived pitch and the reference runs ~15. The geometry was never
wrong; the roster is a third of the size. Shrinking the building would have made it un-reference-
like in order to hide a roster gap.

**So the crowd split is what shipped**: every garrisoned group becomes more, weaker bodies at
constant HP, damage and structure damage. 155 garrisoned bodies -> 248. Full write-up, tables and
traps in `UNIT_VARIETY_DESIGN.md` "Tier 2.2, part four". What belongs here:

**THE INVARIANT WAS PROVED, NOT ASSERTED.** Every level was built on both data sets and all three
totals matched on all twelve; `BalanceAudit.Report` came back **byte-identical across all 61
findings**. That is the control shot for "no level Rob has signed off gets harder", and it is the
only reason this was safe to apply to signed-off content at all.

**THREE CONSTRAINTS PICKED THE FACTORS, AND TWO OF THEM WERE FOUND BY A CHECK GOING RED:**

- The factor must divide HP and damage EXACTLY, or the split silently retunes the level.
- **No crowd body may fall to the incendiary burn's 8 damage.** The first table split the sniper
  x2 and the grenadier x3 — both land on exactly 8 hp — and the roster-frailty check went red
  immediately: the burn stops CHIPPING and starts one-shotting. That check exists because
  HANDOVER's own open item asked for it to be anchored to the live roster so it could not expire
  silently. It earned its keep today. The sniper is now not split at all.
- **The 7-30 roster scale is a LOCK**, and `LevelComposition` caught L12 at 31. `CrowdSplit` takes
  groups worst-clump-first and refuses any split that would breach it, so L12 splits its citadel
  and leaves its gate.

**ONE THING IS NOT NEUTRAL AND IT IS MEASURED RATHER THAN GLOSSED.** Aggregate HP is preserved, but
kills happen per BODY and the last round into each body wastes its overkill: a 40 hp machine gunner
takes 5 rifle rounds, two 20 hp ones take 6. Riflemen are unaffected (16 and 32 are both multiples
of 8). Campaign rounds-to-clear **670 -> 695, +3.7%**, concentrated on seven levels at 3-5 rounds
each — a third to a half of one player volley. Five levels including L12 are untouched. **There is
no factor that removes it**: 40 has no divisor that is both burn-safe and a multiple of 8. And
`BalanceAudit` models HP in aggregate, so it cannot see this at all.

**THE PROJECTILE POOL OVERFLOWS SILENTLY, and this change walked up to it.** The draw loop skips
any round past the end of the pool while it still flies and still damages. L12's enemy volley went
~23 -> 51 bullets against a pool of 64. `ProjectilePoolSize` is **96** now and `PortSelfTest`
measures the campaign's real peak against the real constant rather than a copy of it. Negative run
at 48: `worst is L12 Bullet at 51 rounds against a pool of 48`.

**NO SCENE REBUILD IS NEEDED**, and that is a property of how it was built rather than luck: the
crowd variants share their parent's `modelAsset`, which is what `BattleRunner.UnitClassKey` keys
on, so they reuse the same prefab and the same data-sized slot pool. They are separate definitions
rather than a per-entity stat override because `UnitEntity` reads its stats off `Definition` in
eight places — "grep for EVERY READER" is a trap this repo has paid for twice.

**`DeckFillReport.Run` is the new instrument** and it only measures; `CrowdSplit.Apply` is the
authoring step and is idempotent.

### What the SECOND 2026-08-11 session changed — Tier 2.1 and 2.4, the two repaint features

Both are in their own sections below. What belongs at the top is the cross-cutting lesson, because
it is the fifth session running to produce one:

**A CHECK CAN BE UNFALSIFIABLE FOR MORE THAN ONE REASON AT A TIME, and fixing the first one does
not make it a check.** The camo vanity check — same seeded volley under two camo sets, demanding
identical damage — passed against a build where the camo really did buff damage 50%. Twice:

1. the volley never landed, so both runs did zero damage and zero equals zero; then
2. once that was fixed, the set was never actually WORN — `SelectedCosmetic` validates on read,
   so selecting a set the player does not own silently returns Olive, and it was comparing Olive
   with Olive.

Only after both did it read `enemy 280/276`. **Fix one hole and re-run the breakage, do not assume
the check is now live.**

**AND A CHECK CAN INDICT THE WRONG THING WHEN ITS OWN METRIC IS NEW.** The faction distinctness
check began as a luma-weighted rgb distance, which weights blue at 0.11; it scored Ironclad's steel
blue-grey at 0.082 from the player's olive green and failed a palette the Kotlin build shipped and
played fine. The metric was three hours old and the palette was three weeks old. It is an
opponent-colour distance now, and only a coarse floor. Same family as the "ASCII only" glyph check
that flagged 23 strings a device screenshot then showed rendering perfectly: **be suspicious when a
brand-new check indicts long-standing, apparently working content.**

**A THIRD ONE, about where checks can reach at all.** `PortSelfTest` does not drive MonoBehaviours,
so it tested `Cosmetics.TestOverride` rather than the tile's tap handler — and a test supply that
quietly UNLOCKED a camo for real passed every check in the file. `BattleUIPreview` is the only
harness that drives real uGUI; it now taps the tile and asks the STORE whether anything moved, and
it caught that breakage on the first run (`unlocked 0->1, worn Olive->Arctic`).

### What the FIRST 2026-08-11 session changed — the airstrike, rebuilt in five passes

**Signed off by Rob: "ok this will work."** The whole session was one loop — build, put it on the
device, let Rob look at it, be told what was actually wrong. **Every single pass was rejected for a
reason a green test suite could not see, and three of my own checks were worthless when written.**
That is the lesson worth carrying, more than any constant below.

| Rob said | What was actually wrong | Fix |
|---|---|---|
| "i don't really see a difference — looks like only one" | Round COUNT was never the bottleneck. A 0.22-scale dot at 25 u/s covers a fifth of the gap it opens between frames, so 7 and 14 draw the same dotted chain | `IsStrafe` + a **tracer STREAK** — 4.5x along flight, 0.7x across |
| "the plane should come from the left... it seems to just appear in the middle" | Not the spawn. The camera began the run over the player line and **swept past** the aircraft | The run **CUTS** to its anchor and holds. `CAMERA_ARCHITECTURE.md` exception, asked for and granted |
| "the strafe should spread further horizontally" | 4 units inside a ~10 unit frame reads as a cluster | Walk widened; `PlaneRunHalfLength` pays for it |
| "it's not hitting the structure" | The walk ENDED on the aim point, approaching from the left — every round but the last landed SHORT of whatever you aimed at | Then superseded ↓ |
| "the strafe is independent of the player unit volley... cover the whole enemy position and its structures" | The rake was defined relative to the AIM, so its ground moved with every drag. Every fix above was tuning an offset from the wrong origin | **`StrafeSpan`** — enemy units + structure EDGES, carried on the aircraft |
| "sync the player projectile volley with the plane. it's a little awkward" | The two halves were ADDED: 4.53s on an ordinary shot, a third of it watching a plane with none of the player's rounds in the air | Aligned on their IMPACTS — `max(flight, run)`, 2.91s |

**The design rule that fell out of it, and it is the one to keep:** the BOMB belongs to the player's
aim; the GUNS belong to the enemy's position. Those are different origins and conflating them is
what produced four of the six rejections above.

**Everything the aircraft does now lives on the ALWAYS-RUN physics path** — motion, guns, and (as of
this session) the bomb release. Three separate things have had to move out of `TurnPhase
.AirstrikeRun` after freezing or silently dropping work when the phase ended. **Assume the fourth
will too**: the run is a phase whose subject deliberately outlives it.

**Numbers that matter now:** `StrafeRounds 28` at `StrafeDamage 1` (budget held at 28, item total
52), `StrafeMargin 1.5`, `PlaneRunHalfLength 11` as a FLOOR not a spawn, `StrafeRoundStretch 4.5`.
**The beat is no longer one fixed length across the campaign** — it is derived from the enemy's
width and the shot's flight time, so a wide level holds longer than L1's ~1.4s.

### The three checks that were WORTHLESS when written, all in one session

Each passed against the exact broken code it was written for. All three failed for the same reason:
**the check was not in a state where its failure was reachable.**

1. **The camera-entry check.** `fresh` has never ticked, so `CameraFollowX` is null — the spring
   then begins AT the anchor and sweeps past nothing. Seeded onto the player line, it went red with
   `camera -7.44 (anchor 9.42)`, which is Rob's bug in numbers.
2. **The whole-burst check.** Written with the block's standard aim, which lands PAST the enemy —
   so the rake finished before the bomb and a phase-bound firing loop dropped nothing. Re-pointed
   at an aim landing SHORT (the ordinary case) it read `17/28`: eleven rounds dropped in silence.
   It now asserts the aim IS short as part of its own condition.
3. **The "spawned off-frame" claim**, inherited from 2026-08-10 — a message naming a property about
   the FRAME that the assertion never looked at.

**This is now four sessions running.** With the empty-purse check and the `ReferenceEquals` refusal
test, the standing rule has earned its place in `CLAUDE.md`: **ask what STATE the failure needs to
be reachable in, then put the check in it** — and never trust a new check until you have watched it
go red.

### And two things only the DEVICE found, both from the same cause

The aircraft is now HELD BACK while the volley flies, and two things were still anchored to the
release:

- **The pass-by sound played over empty sky**, a second before the plane existed. Now on the
  true->false edge of `AirstrikeSpawnDelay`. The clip's peak is cut to land as the plane crosses its
  drop point and that offset is measured from the START OF THE RUN — anchor it anywhere else and
  the peak is thrown away silently.
- **The release log said `volley held` when the volley was already away.** THIRD false reading from
  that one line (`volley: 0 rounds`, then strafe tracers counted as volley rounds, now this). It is
  the only instrument a release build has. It reports the three real cases now.

**Neither was visible to any check, and both were caused by a timing change three files away.**
After touching this beat, fire one on a device and read the log AND listen.

### The docs pass — 2026-08-11, end of the third session

**`LEVEL_AUTHORING.md` is the EIGHT composition rules now.** Rule 8 — every GROUND unit stands
clear of every structure's COLLISION BOX, which is `hitWidth` wide and nothing like the width of
the building you see — was enforced by a check for a whole session before it was written down
anywhere an author would look. `CLAUDE.md`'s three "seven rules" references were corrected with it.
**Rule 8 is checked by `PortSelfTest`, NOT by the inspector or `LevelComposition.Report`**, so it
fails the suite rather than appearing beside the level you are editing. That asymmetry is recorded
in `LEVEL_AUTHORING.md` rather than quietly tolerated, and is worth closing next time someone is
in `LevelComposition`.

**`_plans/` was carrying three finished plans as though they were live.** They are in
`_plans/archive/` now, and `TIER0_PLAN.md` is why it mattered: four days after the balance audit
was run and Tier 0 signed off, it still said the device half was "still owed". That is precisely
the failure `_plans/README.md`'s own opening paragraph warns about, and it survived because **a
finished plan sitting beside a live one looks live.** `BACKLOG.md` is the only current plan.

**`HANDOVER.md` was 3128 lines and is 2126.** The closed 2026-08-05/06 port entries — 21 sections,
the whole first two days of the port — moved to `HANDOVER_ARCHIVE.md`. Nothing was summarised or
deleted; the reasoning in this project is the valuable part. **"Traps already paid for" and "Open
items"/"Things will bite" deliberately STAYED**, because they are the two sections that are still
true rather than still interesting.

### What the THIRD 2026-08-11 session changed — Tier 2.2, part one

**The heroes were never composed.** Every hero group in the campaign was authored ONTO a
structure in counts of 4-5 (L6 x4, L7 x5, L11 x4, L12 x5), so `FormationFor`'s garrison branch
won every time and **`Formation.Heroes` — the entire "stands apart, individually" path — was
reached by exactly one thing in the game**, L10's turn-4 reinforcement wave. Not a bug in the
function; nothing called it. The engine half of crowd-vs-hero has been finished for months.

**`LevelComposition` passed all twelve levels the whole time**, because a hero is a legal garrison
member and spans, reach and garrison-majority were all satisfied. The seven rules measure
geometry, not casting. This is the shape to remember: **a green rule-checker is evidence about the
rules it has, not about the thing you are looking at.**

Heroes now stand on the GROUND in front of their structure, 1-2 per level, z 0.4 forward (free —
`SweptCollision` is x/y only). Surplus heavies swapped 1:1 for enemy riflemen in the garrison they
left, so **every roster total is unchanged and enemy damage output is identical**; the only
balance movement is enemy HP, **L6 -64, L7 -96, L11 -96, L12 -96**. Four levels clear slightly
faster. L11 fields ONE hero on purpose: two would have dropped its garrison to exactly half the
roster and broken rule 5.

**A third assertion died to measurement.** "Heroes stand forward in z" is FALSE — L12's deck
garrison sits at z 0.80 against the hero's 0.34, because `deckStandZOffset` and a staging offset
share an axis and mean different things. It is not in the check; asserting it would have asserted
a belief.

**A SECOND, OLDER DEFECT FELL OUT OF THE SAME AUDIT: two groups garrisoned on one structure
stood INSIDE one another.** `FormationFor` laid out each authored group separately and every one
centred on the same deck — L11's three riflemen and three machine gunners occupied an identical
`5.81..6.19`, dx 0.000 dz 0.000. Six men rendering as three. `LevelBuilder.DeckSpots` lays out
each deck ONCE across all its groups now, first group in the front rank and the next behind it.
**A reinforcement wave still builds in isolation and cannot see who is already up there** — no
campaign wave garrisons today, and the old path is kept for that case.

**The overlap detector was wrong before the code was right.** Its first version compared x-RANGES
per group and called L11 broken AFTER the fix landed, because a back rank legitimately spans the
same x as the front one. It is Chebyshev now. **A detector that reports a failure needs checking
for what it is actually finding, exactly as one that reports nothing does.**

**DECK FILL is measured and left alone deliberately**: garrisons occupy 12-56% of their deck, most
of them 12-25%, because the body shrank 0.77 -> 0.48 while `standWidth` is real structure geometry
and did not. Pitch is correct — it was derived against the reference in 2026-08-02. **Filling a
tier is a roster-size question and therefore a balance one**, so it is written up in
`UNIT_VARIETY_DESIGN.md` rather than applied. Do NOT fix it by spreading the row.

**608 checks.** `CheckHeroStaging` builds every campaign level and measures what a player would
see. Its negative run against the old data, per the standing rule:
`18 across the campaign, biggest group 5, 18 on a deck, 13 inside the 0.76 clearance floor,
tightest 0.00 on L12` — **0.00 is a hero at the same x as a crowd body**, interleaved in the
citadel's rifle row.

**The CONTROL SHOT was taken** — same drag, same frame, on a build carrying the old level data.
Before: four oversized red figures crowding the keep roofline as one lump, taller than its own
crenellations. After: two greatcoated heroes alone at the base, small crowd on the roof.
**Rob has not seen it.**

### The hero placement was WRONG and Rob caught it on the device

*"heroes are behind the structure which makes them really tough to hit without firing at a steep
angle."* Correct, and geometric. **A structure blocks as a box `hitWidth` wide, which is not the
width of the building you see** — L6's keep is drawn around x 6 and blocks from **x 3.88**. The
heroes were placed at 4.3, "in front of the keep", measured off its ANCHOR, and landed inside it.
L12's were **1.71 deep** in the citadel's box.

To hit a unit inside a box you must clear the box top and reach the ground within the same
fraction of a unit — L6 wanted a 2.0 drop in 0.02 of travel. **The hero pass had made them harder
to hit than when they stood on the roof**, because a garrison on a deck is above every box and
takes an ordinary arc.

Placement rule now: **clear of every enemy structure's box, with nothing between the hero and the
player** — only a box at LOWER x can shadow a left-to-right shot. L6 -1.0, L7 2.7, L11 3.2,
L12 0.6.

**Rule 7 cannot see this and no rule can.** It measures distance and height and asks whether the
roster has the POWER; there is nothing in its model about what is IN THE WAY. All twelve levels
passed all seven rules the whole time. **Reach and a clear line are different questions.**

**The new check indicted SHIPPED content and the content was wrong.** `CheckNobodyStandsInAWall`
found four riflemen the campaign already had — two on L9 inside the mountain bunker, two on L10
inside the outpost, none of them hittable without the same plunge. Moved out with the heroes. The
standing warning about a new check indicting old content is not "assume the check is wrong", it is
**go and measure which one is wrong**. ADVANCING units are exempt semantically: L9's shield bearers
start 0.01 inside on jitter and walk out on their first move.

Negative run: `10 of 43 ground units embedded, tightest -1.71 on L12`. Device-confirmed on L6 with
a deliberately SHALLOW drag (~241px per axis against L1's 331) that killed three, 16 -> 13.

## RIGS doubles as TEST SUPPLY — 2026-08-10

**While RIGS is ON, all four consumables are FREE to equip and none is ever spent.** The loadout
header says so in as many words (`Consumables — carry up to 2   [TEST SUPPLY — RIGS]`) and every
tile reads FREE, because a test mode that looks identical to the real one is how a "confirmed on
device" result gets recorded against a state no player can be in.

**Why it exists:** verifying one airstrike change cost a full re-earn of 250 coins. The release
build is not debuggable, so `run-as` cannot seed PlayerPrefs, and the protocol is
uninstall/reinstall — so every build wipes the balance. That was two Auto-driven levels per
iteration, four times in one session.

**It writes NOTHING** — no purchase, no coin change, no `SpendConsumable`. So a player who finds
RIGS cannot corrupt their own save, and switching it off leaves the economy exactly as it was. That
one property is what made reusing RIGS acceptable instead of adding a second hidden switch, and it
is asserted by `BattleUIPreview` (see below).

**TEST SUPPLY CARRIES ALL FOUR, IMMEDIATELY, AND IGNORES THE PICKER** (corrected 2026-08-11).
The first version carried only what the PICKER had selected, which made the switch nearly useless
for its own purpose: RIGS lives on the battle HUD, so turning it on mid-battle granted a free shelf
you could not reach without finishing the level to get back to a picker. Rob, with RIGS on and no
way to fire an airstrike: *"thought that would expose it by default."*

Two things make it work now. The carry under test supply is **every item at 1**, not the picker's
selection — which **deliberately exceeds the locked carry cap of TWO**, because a testing state has
to reach every item in one battle. And the RIGS toggle **re-reads the carry into the battle already
running**, so the shelf appears on the tap; it re-reads rather than adds, so switching RIGS back off
takes it away again and restores whatever the picker really equipped. Any ARMED flag is cleared on
the way, because those outlive the carry and an airstrike armed after its supply is withdrawn is a
volley spending an item the player has not got.

**The HUD buttons read `TEST` where the count goes**, for the same reason the loadout header does,
and it matters more here: this bar is showing FOUR items past a cap of two, and a state no player
can be in must never be mistaken for one they can.

**The old workflow, no longer needed:** BEGIN -> tap RIGS -> finish the level -> NEXT, one
Auto-driven level, about a minute. It is now BEGIN -> tap RIGS, and the bar is there. `showRigs` is deliberately NOT persisted, so a relaunch starts
clean — if that minute per session becomes annoying, persisting it is a one-line change, but it
would also mean a player who ever taps RIGS keeps free consumables forever.

**`PortSelfTest` cannot cover this** — it does not drive MonoBehaviours. `BattleUIPreview` does, and
it asserts the property that matters: it STAKES four items' worth of coins, equips under test supply,
and fails the run if either the balance or the owned count moved. The first version of that check
ran on the editor's real balance of ZERO, so a fall-through to the genuine purchase path simply
could not afford anything, wrote nothing, and the check passed against deliberately broken code —
**a check that could not fail.** Staking the coins is what gives it teeth, and the negative run then
reported `SmokeScreen owned 0->1, coins 800->600`.

**The device's progress was WIPED and re-earned on 2026-08-10**, four times over, testing the
aircraft. Nothing can be seeded — the release build is not debuggable, so `run-as` cannot reach
PlayerPrefs — and the testing protocol is uninstall/reinstall, so **every build costs the balance**.
Re-earning 250 coins is two Auto-driven levels and about two minutes; budget for it rather than
being surprised. It currently sits on L1 with ~205 coins and the first two levels cleared.

Note the flame's own assets are NEW and untracked until committed: `Assets/Prefabs/Flame.prefab`,
`Assets/Materials/Flame.mat`, `Assets/Materials/FlameTex.asset`, `Assets/Scripts/Render/FlameRig.cs`
and `Assets/Editor/FlamePreview.cs`.

### What changed on 2026-08-07, in one place

Sections at the end of this file carry the detail; this is the index.

| | |
|---|---|
| `BalanceAudit` + composition **rule 7** | reach, the volley race, the melee clock, and siege capacity — checked, not prose. Found **L7 unwinnable** and fixed it |
| `LEVEL_AUTHORING.md` rule 4 corrected | it claimed a "~49-unit max range"; the real figure is **20.25**, and that lie is what licensed L7 |
| The **siege capacity** finding | a stock squad can do a FIXED **288** structure damage per battle (3 shells x 96; a rifleman does 2). Five levels garrisoned more than that and were retuned |
| **The tank shell's aim** | it overshot the volley by **2.5-3.9 units**, so the only structure-breaking weapon could not be placed. Now solved onto the volley's landing point |
| **HUD lists structures separately** | a single total cannot say WHICH building still stands; it cost one audit run four volleys fired into rubble |
| **L9 roster cut 22 -> 15** | widest body ratio in the campaign, and its garrisons were over the decks they stood on |
| **Tier 1.1 ammo** | four types, a selector that also sells, the incendiary burn. A FIFTH dead system |
| **Ragdoll levitation fixed** | corpses flung at a wall were snapped up the face onto the roof |

### The lesson THIS session kept re-teaching: assert the OUTPUT, not the input

Three separate times, a check that passed was asserting the wrong thing, and the device or a
deliberate negative test found what it missed:

- **AP ammo** asserted `structureDamageScale == 2` and passed, while the real per-round effect was
  **1.2x** — the engine multiplies `Damage` by the multiplier, and the ammo had already scaled
  `Damage` down. Caught on a device: 128 off a 165hp citadel where ~192 was intended.
- **The ragdoll fix** was "verified" by two tests that both passed against buggy code, because
  neither ever reached the branch — a body at rest dips below the box's base, and one tick does
  not carry a thrown body into the box at all.
- **The tank shell** had a test asserting ammo was IMPORTED, never that a shell was fired where
  aimed.

**So: run a new check against the OLD code before trusting it.** Both the shell fix and the AP fix
were confirmed that way this session, and both negative runs are recorded with their numbers. A
check never seen to fail is not evidence.

**Both of these are now STANDING RULES in `CLAUDE.md`'s Debugging section**, so they apply every
session without anyone having to remember this file. They sit beside the sibling rules they
generalise — "prefer a PROBE to a detector" and "a search that finds nothing is not evidence of
absence".

**2026-08-10 added two more, both from the airstrike work:**

- **TAKE THE CONTROL SHOT.** Two write-ups in this file described the airstrike as "findable
  because one round falls nose-down while the rest fly arcs". Firing the identical drag with
  NOTHING armed shows that round anyway — it is the TANK SHELL, which fires on every volley for
  free. Nobody had ever actually seen the airstrike. An observation about a feature means nothing
  until the same observation has been made with the feature switched OFF.
- **A CHECK RUN ON AN EMPTY PURSE CANNOT FAIL.** The "test supply writes nothing to the economy"
  check was written against the editor's real balance of ZERO, so the broken code it was meant to
  catch simply could not afford anything, wrote nothing, and passed. Staking coins first gave it
  teeth. This is the same family as the `ReferenceEquals(Use(x), y)` refusal test from Tier 1.3 and
  the deleted phase-spread check from the flame work: **ask what STATE the check needs to be in for
  the failure to be reachable at all**, then put it in that state.

**2026-08-09 added a third costume, and it is the subtlest:** a check written against a NOTE IN A
DOC asserts the note, not the behaviour. The glyph check above was written as "ASCII only" on
CLAUDE.md's word, flagged 23 strings, and every one was a false positive. Assert against the
engine, the font, the device — not against the summary someone wrote of them.

### The other standing lesson: assume NOTHING is wired

**Nothing in this port is live just because it exists and has tests.** The running count is at
NINE systems found fully ported, unit-tested and reached by nothing — the economy, boss phases,
reinforcement waves, stages, ammo, consumables (2026-08-10), advancing squads + melee (the EIGHTH,
still unported, and the one holding Overwatch Flare) and **player cosmetics** (the NINTH, found
and wired 2026-08-11 by a grep that returned three hits, all inside the files that define them).
Every one but advancing squads is now live. **Grep for callers before believing a feature is
live.**

**Enemy factions were a different failure and worth telling apart**: not ported-and-dead but
ABSENT — the Unity tree had three comments mentioning a faction palette and no code at all, while
`DYNAMISM_DESIGN`'s status table called D1 shipped. Ported-and-dead greps as callers-inside-their-
own-file; absent greps as nothing. **Both look identical in the design docs.**

**And the design docs will not tell you.** Their Status tables say "shipped" about the **RETIRED
ANDROID BUILD** — consumables are marked shipped in `PROGRESSION_DESIGN.md` Phase 2 and
`DYNAMISM_DESIGN.md` Phase C, dated 2026-07-20, and nothing in this repo calls them. Read those
tables as the SPEC for what to build here, never as a record of what exists here. This is the same
failure as "a check written from a doc asserts the doc", one level up: **ask the code who calls it.**

Wind is worse than unwired: it is COSMETIC (`windAccelZ` drifts the round in Z; the collision test
is X/Y only), so **do not author a wind level or a wind schedule until wind does something**, and
making it real is a physics change that needs an ask.

Then read the traps sections — most cost a build to find, and several are invisible outside a real
device build.

Read this first, then `CLAUDE.md` for the standing rules, `LEVEL_AUTHORING.md` before touching a
level, and `SPIKE_RESULTS.md` / `MIGRATION_SCOPE.md` for port history. Everything below was
verified on the device, not assumed.

### Where the Tier 0 write-ups are — ARCHIVED 2026-08-11

**They are in `HANDOVER_ARCHIVE.md` now**, along with every other closed 2026-08-05 and
2026-08-06 port entry — 21 sections, moved when this file hit 3128 lines and the current state was
buried under two days of finished work. Each still says what shipped, what it cost and what it
found; none of them describes current behaviour, which is why they moved.

**What stayed here:** START HERE, everything from 2026-08-07 onward, "Traps already paid for",
and "Open items" with its "Things that will bite" list. If an archived entry seems to describe how
the game works today, check this file first — it is accurate about the day it was written and
about nothing else.

| Section (in `HANDOVER_ARCHIVE.md`) | What it covers |
|---|---|
| Data authoring moved into Unity | The importer disarmed, the sweep removed, the composition rules made checkable |
| Campaign split from the test rigs | RIGS gate; the renumbering chore retired |
| The victory screen and a live economy | The dead economy, and the TMP missing-glyph trap (which the 2026-08-09 section above CORRECTS — the font is not ASCII-only) |
| Campaign to twelve levels | The beat chart; wind found cosmetic; boss phases and waves wired |
| Enemy turn juice | Flash banner vs standing telegraph |
| Loadout | Slots vs points, and why that keeps the camera framing safe |
| Ruins, instead of blocks everywhere | Shed chunks were the real culprit, not the collapse |

**The design docs now live in this repo** (moved 2026-08-06): `GAME_DESIGN_LOCKS.md`,
`PROGRESSION_DESIGN.md`, `DYNAMISM_DESIGN.md`, `CAMERA_ARCHITECTURE.md`, `UNIT_VARIETY_DESIGN.md`,
`STRUCTURE_VARIETY_DESIGN.md`. They still govern.

**Product / retention direction (2026-08-06):** `PRODUCT_DIRECTION.md` — priority stack
(campaign spine → victory/meta juice → ammo/events → identity → daily/monetization), dopamine
model, 12-level beat chart, anti-goals, and soft-launch success criteria. Claude should plan
engagement/content work against that file; it does not override locks.

## Open items — in the order I would take them

**This list was written before Tier 0 and most of it has SHIPPED.** Kept for the reasoning, which
is still the record of why each thing was worth doing. Current state:

1. ~~Unit art: every class renders as the same rifleman.~~ **DONE 2026-08-06.**
2. ~~**A decision, not a task: re-tune incendiary, or leave it.**~~ **The number is 8**,
   re-derived against the live roster (frailest crowd body 12 hp). The old "still 6,
   calibrated to an 8hp Sniper" sentence was stale prose — the asset, `AmmoSetup` and
   the later "Verified on device" section already agreed. A further raise is still a
   balance call, not a warm-up.
3. ~~Loadout screen.~~ **DONE 2026-08-06** — see "Loadout" in `HANDOVER_ARCHIVE.md`.
4. **`snowfall` is imported and ignored** — Winter's falling flakes are not ported. STILL OPEN,
   and still low value: Winter is one campaign level.
5. **Release build gaps** — debug-signed, APK not AAB, `versionCode` never increments. STILL
   deliberately deferred; see the README.
6. ~~The unit parade rebuilt to a single row of six, unverified.~~ Swept on device since.

**The one thing Tier 0 itself owes is the BALANCE AUDIT** — see START HERE.

### Device sweep — DONE 2026-08-05, at 29 levels

Every level loaded on the Pixel 10 Pro XL via the ◀ ▶ nav, in the right order, with no exception
and no missing-model warning. **Per-level biomes confirmed on device** — green, desert,
city-ruins and winter backdrops all appear, which no build before this one could show.

Swept BEFORE the campaign was cut to 7 biome levels; the 7 survivors were re-swept afterwards and
all load. The 17 test rigs have not been re-swept since the roster cut, and two of them were
rebuilt by it (the unit parade and the demolition rig), so that is the cheapest sanity pass if
anything looks wrong.

On-screen buttons sit clear of the status-bar and gesture insets so an adb tap cannot land on the
system UI: ◀ (880, 235), ▶ (1000, 235), AUTO (180, 2259).

## Owed to the ANDROID repo

- ~~The garrison-ceiling bug is probably live there.~~ **FIXED there 2026-08-05**: `hitsStructure`
  now bounds the box by `deckY` where one is measured, with a regression test. Re-measuring the
  whole set says the outpost was the only mismatch.
- **Nothing is owed and nothing is uncommitted.** That repo is at `f9af006` on
  `projectile-refinement`, pushed, working tree clean, 50 tests 0 failures. The branch is 11
  commits ahead of its own `main` and has never been merged or PR'd — GitHub offered
  `https://github.com/rbfr/ArmedConflict/pull/new/projectile-refinement`.
- ~~**Game DATA still lives there.**~~ **NO LONGER TRUE as of 2026-08-06** — authoring moved into
  Unity and the ScriptableObjects are the source of truth. Nothing is owed to that repo now, and
  nothing in it needs to be edited to change a level, a unit, the roster or a stage.

## Archived — moved to `HANDOVER_ARCHIVE.md` on 2026-08-25

The file had passed 4000 lines. These sections are CLOSED — a system built, or a bug fixed — and
none is a statement about current behaviour, so they were moved WHOLE rather than summarised.
**Nothing was deleted.** The live rules they paid for stayed behind in "Traps already paid for".

| Section | What it holds |
|---|---|
| Enemy factions — Tier 2.1, built 08-11 | two stages, two armies, and the traps that cost |
| Player camo — Tier 2.4, built 08-11 | four sets; the NINTH ported-but-unreached system |
| Tier 1.3 — the consumables, built 08-10 | the four items, the cap of two, device proof per item |
| The loadout screen's per-frame NRE — FIXED 08-10 | `state` is null until BEGIN; the tick ran anyway |
| The incendiary flame — 08-09 | drawn off `BurningEnemyIds`; why the `[Burn]` log stays |
| Tier 1.2 — the telegraph and the schedule, 08-07 | the countdown is composed, never authored |
| The balance audit, DEVICE half — run 08-07 | **STALE — measured at THREE tank shells** |
| THE TANK SHELL DOES NOT LAND WHERE YOU AIM — found 08-07 | the overshoot, diagnosed |
| The shell now lands where you aim — FIXED 08-07 | solved onto the volley's landing point |
| Tier 1.1 — AMMO TYPES, built 08-07 | the four rounds, and asserting the OUTPUT not the factor |
| Corpses levitating onto roofs — FIXED 08-07 | a body may not land on a roof it never reached |

**What did NOT move, and why:** `Siege retune — 2026-08-07` says **PARTLY verified** in its own
title and carries the live `hpScale` table for five levels, so it is still open work.
`RIGS doubles as TEST SUPPLY` describes what RIGS does TODAY and is used every session.

## Siege retune — applied 2026-08-07, PARTLY verified

Option 1 from the section above: cut garrisoned structure HP under the 288 a stock squad can do.
Applied per PLACEMENT via `hpScale`, so no shared structure definition changed and no other level
moved. Each level's `designNotes` carries the numbers and the reason.

| Level | Garrisoned HP before | after | how |
|---|---|---|---|
| L3 Watchpost Ridge | 340 | **215** | bunker 0.5 (the TOWER snipers are the beat, so the bunker takes the cut) |
| L5 Tower Assault | 340 | **227** | bunker 0.55 (tower stack untouched — fighting upward is the beat) |
| L6 Ridge Bastion | 392 | **257** | bunker + keep both 0.66, scaled together so the keep stays dominant |
| L9 Dusk Redoubt | 330 | **229** | bunker + barracks both 0.7 |
| L12 The Citadel | 425 | **280** | gate + citadel both 0.66 — tightest margin in the game, correct for the finale |

All twelve now pass the SIEGE DEFICIT check, and the campaign ramps 90/135/215/240/227/257/240/
225/229/225/135/280. `PortSelfTest` ALL PASS, composition 12 levels 0 errors.

### What the device actually showed, and what it did NOT

**L9 only was re-run.** It is much better and probably still too hard.

- Played by hand with shells into the structures: 22 -> 9 enemies by volley 4 with 7 of 10 units
  alive, against the pre-tune run which sat at 17 enemies with the squad collapsing. The retune
  works.
- Then the same endgame problem as L4: the surviving shield bearers close to melee and a computed
  45-degree drag flies over them. **This is a limit of driving the game from adb, not a finding
  about the level** — a player has the aim preview.
- So it was re-run under **`Auto` as an upper bound on aim**: Auto never misses, so if perfect aim
  cannot clear it, nothing can. Auto took it from 22 enemies to **2 v 2** — and the run was cut
  short there when the phone was needed, so the final outcome is UNKNOWN.

**Read that 2v2 as a warning, not a pass.** Perfect aim finishing a level with 2 of 10 units left
is a level a real player loses, and `StarsFor` would score it 1 star at best. My reading is that
**L9 needs a second pass**, and that structure HP is not the whole story there:

**L9 fields 22 enemies against the player's 10 — the widest body ratio in the campaign**, and the
volley race flagged it worst at 4.1x before any of this. Cutting HP does not change that a 22-body
line out-shoots a 10-body line every single turn. The next lever for L9 is the ROSTER, not the
walls.

### Still owed — ALL SINCE RESOLVED

- ~~L3, L5, L6, L12 not re-run on device~~ — **closed by Rob's playtest 2026-08-07**, after the
  tank shell fix, which changed the answer for every level at once.
- ~~L9 likely wants an enemy-count cut~~ — **done**: 22 -> 15, race ratio 4.1x -> 1.9x.
- The **"Structure HP" HUD line is still a single total** across all enemy structures, so it cannot
  say which building still stands. It cost one run four volleys fired into rubble. `BattleRunner.cs`
  around line 1278 — the fix is to list surviving structures by `displayName`, and it needs no
  scene rebuild because that HUD is IMGUI.
