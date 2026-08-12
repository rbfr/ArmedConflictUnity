# HANDOVER_ARCHIVE.md — closed sections from the port, 2026-08-05 and 2026-08-06

Split out of `HANDOVER.md` on 2026-08-11, when the live file had reached 3128 lines and the
current state was buried under two days of finished port work.

**Everything here is CLOSED.** These are the Unity port's first two days — the entries that record
how a thing was built, not what is true now. They were kept in full rather than summarised because
this project's record is that the reasoning is the valuable part, and several of these entries are
the only place a decision's WHY is written down.

**What did NOT move, and where to look instead:**

- **`HANDOVER.md` is still the live file** — START HERE, everything from 2026-08-07 onward, and
  the current tier state.
- **"Traps already paid for"** stayed live. Every trap in these archived entries that can still
  bite is distilled there or in `CLAUDE.md`; the long-form story is what moved.
- **"Open items" and "Things that will bite"** stayed live — they still carry open decisions
  (the incendiary retune, `snowfall`, the release-build gaps) and live gotchas.

If you are reading an entry here to understand current behaviour, stop and check the live file
first: an archived entry is accurate about the day it was written and about nothing else.

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

