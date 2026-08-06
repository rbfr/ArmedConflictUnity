# Handover — Unity port, as of 2026-08-05

## START HERE — state in ten lines

- **Both repos are COMMITTED AND PUSHED.** Unity `main` at `d56ba9f`; Android
  `projectile-refinement` at `f9af006` (a branch, not merged to its `main`).
- The Unity port **plays all 24 levels** end to end at a steady 60 fps, with animated units.
- **Campaign is 7 levels, ONE PER BIOME** (Mountains, Forest, MountainsDusk, Winter, Desert,
  CityRuins, Ocean) plus 17 test rigs. Total 24.
- **Roster is 8 unit definitions / 7 models / 6 pickable**, cut from 15 on 2026-08-05.
- **Self-test checks pass.** Run them after every change (command below).
- **THE ANDROID BUILD IS RETIRED** (2026-08-06, Rob: "we're not paying attention to the Android
  one anymore, we're going forward with Unity"). It is no longer the shipping build, no longer
  maintained, and its unmerged `projectile-refinement` branch is not a loose end anybody needs to
  tidy. Keep the repo for reference — its comments are the record of every trap this port inherited.
- **The Kotlin is still the DATA pipeline, and that is now the one thing keeping the old repo in
  play.** Levels and unit stats are authored in Kotlin, exported and imported; the ScriptableObjects
  are still generated and must never be hand-edited. That is a live decision to revisit, not a
  law — see "Data authoring, once Android is retired" below.
- **Every class now renders as itself** (2026-08-06) — seven rigged silhouettes, per-class render
  slots, and the fourth colour tone. See "Per-class unit art" below. That was the biggest open
  thread and it is closed; what is left on it is Rob's judgement in moving play.

Then read the traps sections — most of them cost a build to find, and several are invisible
outside a real device build.


Read this first, then `CLAUDE.md` for the standing rules, then `SPIKE_RESULTS.md` /
`MIGRATION_SCOPE.md` if you need the port history. Everything below was verified on the device,
not assumed.

**The design docs now live in this repo** (moved 2026-08-06): `GAME_DESIGN_LOCKS.md`,
`PROGRESSION_DESIGN.md`, `DYNAMISM_DESIGN.md`, `CAMERA_ARCHITECTURE.md`, `UNIT_VARIETY_DESIGN.md`,
`STRUCTURE_VARIETY_DESIGN.md`. They still govern.

**Product / retention direction (2026-08-06):** `PRODUCT_DIRECTION.md` — priority stack
(campaign spine → victory/meta juice → ammo/events → identity → daily/monetization), dopamine
model, 12-level beat chart, anti-goals, and soft-launch success criteria. Claude should plan
engagement/content work against that file; it does not override locks.

## Where things are

**Two repos, deliberately separate. Do not merge them.**

| | |
|---|---|
| `~/AndroidStudioProjects/ArmedConflict` | Kotlin + SceneView/Filament. **RETIRED 2026-08-06** — reference and data authoring only. |
| `~/UnityProjects/ArmedConflictSpike` | this repo → `github.com/rbfr/ArmedConflictUnity` |

Unity was chosen on 2026-08-04 after a four-step spike passed every criterion. Godot was
considered and dropped without spiking (`GODOT_SPIKE.md` in the Android repo is kept, not deleted).

Each repo has its OWN deploy key — GitHub scopes a deploy key to one repo, so the Android repo's
key cannot push here. This repo uses `~/.ssh/armedconflictunity_deploy` via the
`github-armedconflictunity` host alias in `~/.ssh/config`.

## What works

All 24 levels are reachable and play end to end at a steady 60 fps: drag to aim, volley, swept
collision, damage, structure collapse, turn handover, victory. With sound both sides, a per-level
biome backdrop, per-type projectiles, unit weapons, fading explosions, scorch marks, structures
that shed their own geometry as they take damage, a battle HUD, level navigation and an Auto
button. Units are ANIMATED — idle, a two-handed hold, recoil, death — and both lines raise their
rifles to the angle they are actually firing at.

All eight `GameViewModel` slices are ported (`LevelBuilder`, `CollisionSystem`,
`ProjectileSystem`, `TurnFlow`, `CameraDirector`, `CosmeticSystems`, `HelicopterSystem`,
`EventSystems`) plus `GameState`, `Formation`, `SpringFollow`, `EnemyAI`, `CameraFraming`,
`TrajectoryPhysics`, `SweptCollision`, `ProgressStore`, `EconomyStore`. `data/` is complete at
24 levels — 7 campaign (one per biome) plus 17 test rigs.

**281 checks, all passing.** They assert the behaviour the Kotlin comments describe, not just
that the code compiles. Run them after every change:

```bash
U=~/Unity/Hub/Editor/6000.0.80f1/Editor/Unity
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod PortSelfTest.Run -logFile -
```

## The workflow

Headless. The editor GUI runs over VNC on llvmpipe and is painful; you never need it.

```bash
U=~/Unity/Hub/Editor/6000.0.80f1/Editor/Unity
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod SpikeSceneBattle.Build -logFile -
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod SpikeBuild.Android  -logFile -

export PATH=$HOME/Android/Sdk/platform-tools:$PATH
export ANDROID_SERIAL=57121FDCQ005LC          # USB. The WIRELESS transport drops on long builds.
adb uninstall com.dullesengineering.armedconflictspike; adb install Builds/Step1.apk
adb shell monkey -p com.dullesengineering.armedconflictspike -c android.intent.category.LAUNCHER 1
adb shell input tap 180 2210                  # the AUTO button — drives a level from the terminal
```

`DISPLAY=:1` is mandatory for anything Unity/Hub. The app id is `...armedconflictspike`,
deliberately NOT the shipping id, so both builds sit on the phone for A/B.

The phone locks itself during long builds and a locked device backgrounds the app before
`Start()` runs — which reads as "no output" rather than as a lock. Check
`adb shell dumpsys trust | grep deviceLocked` before concluding anything from an empty log.

## Traps already paid for — do not rediscover these

**Unity/C#**
- Unity 6000.0 is **C# 9**. `record` works; `record class` is C# 10 and does NOT compile. `init`
  needs the `IsExternalInit` shim in `Assets/Scripts/Game/`.
- `GameState` declares **reference equality on purpose**. With ~90 fields the synthesized
  `Equals` chains ~90 `&&`, and IL2CPP exceeds clang's 256-bracket limit — the Android build
  fails outright. Value equality also bought nothing here (no StateFlow to conflate).
- `AssetDatabase.StartAssetEditing` DEFERS creation, so assets referenced by other assets made in
  the same batch serialise as `{fileID: 0}`. Do not batch the importer.
- A camera made with `new GameObject()` + `AddComponent<Camera>()` has **no AudioListener**, so
  nothing is audible, silently. Unity's default camera PREFAB has one; a hand-built camera doesn't.
- AudioClips must be preloaded, or the FIRST play of each clip is silent (`loadState=Unloaded`).
- IL2CPP segfaulted once mid-session. Deleting `Library/Bee/artifacts/Android/il2cppOutput`
  cleared it.

**Coordinates and rendering**
- `GameSpace.ToUnity` negates X. Unity is left-handed; with the camera at +Z looking toward -Z,
  screen-right becomes -X. Route EVERY placement through it — a mirrored scene looks plausible.
- The backdrop lives at NEGATIVE z. Unity's Quad primitive faces -Z, so it needs a 180° turn,
  and hand-built silhouette winding must be CCW from +Z or it is back-face culled.
- Backdrop geometry must be sized against the frustum AT ITS OWN DEPTH, not in absolute units.
  Use `Backdrop.DesignAspect`, never `Screen`: batchmode reports a placeholder DESKTOP resolution,
  and a landscape aspect makes every layer ~3x too wide.
- Pooled objects share a material: per-instance tinting needs a `MaterialPropertyBlock`.
- The SKY QUAD must be sized to the visible band at its own depth (280 tall at y=35, z=-120), and
  both directions are traps. Too short and its top edge is inside the frustum, so the camera's
  clear colour shows above the sky — the game shipped for weeks with a dark slab across the top
  9% of the screen that read as a HUD panel. Too tall and the gradient, which spans the QUAD and
  not the frame, stretches until only its bottom third is on screen and the sky goes flat.
- The GROUND PLANE must stop just in front of the nearest backdrop layer (far edge z = -28). It
  ran to -150, BEHIND the whole backdrop, so wherever a silhouette dipped, distant ground showed
  through above the horizon as a floating tan wedge. The backdrop makes the horizon; a ground
  plane that outruns it is a second, contradictory one.

**Data import**
- The pipeline is **ONE WAY**: Kotlin → `tools/export_kotlin_data.py` → `data.json` →
  `DataImporter` → ScriptableObjects. **Do not hand-edit the ScriptableObjects** — a re-import
  silently overwrites them. Edit levels in the Kotlin and re-export.
- Colour literals arrive under `__args` (positional-only ctor) OR `__positional` (mixed). Reading
  one imported every background pure BLACK, with a correct-looking asset count.
- Read ARGB doubles straight to `long`. A `float`'s 24-bit mantissa cannot hold `0xFF4A90D9` and
  the loss lands on the low byte — every colour came back with blue = 0.
- `val EnemyRifleman = Rifleman.copy(...)` parses as a ctor named `Rifleman.copy`. Missing that
  dropped all four Enemy* variants and with them every enemy reference in every level.

**The backdrop, rebuilt 2026-08-05**
`ArmedConflict.Render.Backdrop` (runtime, MonoBehaviour-free) owns the DESIGN — per style, a list
of layers each reduced to a sampled height profile; `SilhouetteMesh` turns a profile into a strip;
`BackdropRuntime` does the GameObjects and materials. Per-level biomes are LIVE — the plan
builds at runtime from each level's own BackgroundDefinition.

The original drew each layer as a row of INDEPENDENT isosceles triangles, which is why the
mountains read as pyramids. What the rewrite is actually made of, and each of these was a visible
failure first:
- A ridge is ONE continuous silhouette. Profiles normalise to `[floor, 1]`, and the floor matters:
  at floor 0 the valleys drop to nothing and two layers read as two separate GROUPS of peaks
  rather than one range behind another.
- Ridged fBm WITHOUT the textbook per-octave weighting. The weighting is right for a heightmap
  seen from above and wrong for a silhouette — it starves the shoulders and yields needles.
- Snow is a cap on the crests that earn it (line at 0.82 of height, 0.58 for Winter) on a
  WANDERING line. A flat line reads as a ruler; a sine-jittered one reads as surf.
- Depth ordering has to be carried by SIZE as well as haze: the near mountain row is foothills at
  about half the far range's angular height. At near-equal sizes the pale layer read as glass.
- Every body-relative shape is judged at gameplay framing. City blocks needed 3x width variation
  and a low rubble floor or they read as a PICKET FENCE; pines needed crowns overlapping their
  neighbours or the row read as GRASS.

`BackdropPreview.Shots` renders all seven biomes to `Builds/backdrops/*.png` headless in seconds —
use it. The campaign is now one level per biome, so judging the backdrop from a single level
sees a seventh of the game. `PortSelfTest` also covers the plan
(layer widths, profile range, depth ordering, snow coverage).

**Unit art — the CC0 rig prototype, 2026-08-05**
Kenney's Blocky Characters 2.0 (CC0, `Assets/Models/Kenney/`, licence kept beside the models) is
wired in as a free stand-in to answer the engineering questions before any pack is bought.
`SpikeSceneBattle.UseKenneyUnits` is the A/B switch — one const, rebuild the scene, nothing else
in the scene changes. It is currently TRUE, so the scene builds the stand-in, not shipping art.

What it settled:
- **Our own units cannot be animated at all as they stand.** They are grouped by MATERIAL
  (`accent_*`, `skin_upper_*`) rather than by limb — five flat mesh nodes, no elbow to bend.
  Kenney's rig is `root → leg-left, leg-right, torso → (arm-left, arm-right, head)`: six boxes,
  **0 skins, 0 bones**, 72 triangles, 27 clips of plain TRS curves. Any animated future needs the
  Blender builder re-authored around a limb hierarchy, whoever's meshes we end up using.
- **Animation is free here.** Whole-process CPU, L1 idle, three 20s samples each: static Blender
  units 81.5 / 82.4 / 80.4%, 19 animating Kenney units 80.8 / 80.0 / 80.1%. The animated build
  measures LOWER than the static one — the difference is inside the noise. Expected, given there
  is no skinning to do. Caveat: L1 fields 19 units, not 30, and /proc CPU% is a blunt instrument.
- **Team colour by tint works** at gameplay distance — green vs red reads instantly — but it
  multiplies over the character's whole texture, so it stains the face too. A real pack needs a
  tint MASK or per-side textures.
- Open cosmetic gaps in the stand-in: Kenney's proportions are squat next to the current soldiers,
  and the gun is still a separate object floating at chest height rather than held in the hands.

`UnitAnim` (runtime) is the whole integration: Legacy `Animation`, four clip names, a `Desync` so
a line of units is not a chorus line, and a re-arm on hidden→visible because a recycled slot comes
back holding the death pose. `BattleRunner` fires it from the three volley paths and swaps the
ragdoll's topple rotation for the `die` clip — applying both makes a body fold AND spin flat.

**And then the real thing: OUR soldier on that hierarchy (`RiggedUnits`, `Art = UnitArt.Rigged`).**
`tools/blender/build_unit_rigged.py` in the Android repo builds the rifleman around Kenney's joint
names at OUR proportions (hips 49% / shoulders 78%, against their cartoon 37% / 67%), 212 tris.
Verified on device: rifle line at the ready, volley, death, 60 fps, four-tone team colours with no
tint and no stained faces.

Three constraints bind, and only these three — the rest is free:
- **Node names and paths must match exactly.** Legacy clips address curves by path.
- **Model height must be 2.70**, Kenney's. Every clip is rotation-only EXCEPT `die`, which also
  translates `root`, in model units.
- **The soldier must face glTF +Z**, so it is built facing Blender **-Y** — the opposite of
  `build_units_v6.py`'s "faces +X". Rotation curves are local, so a model facing +X gets arms that
  swing out sideways.

Four traps, all of which fail SILENTLY and each of which cost a build:
- Kenney's curve paths are `character-m/root/torso/arm-left` — two segments longer than ours, so
  every curve binds to nothing and the limbs just never move. `RiggedUnits.Retarget` rewrites the
  prefix; `Probe` prints both sides before you trust it.
- A retargeted clip **must be saved as an asset**. A prefab cannot reference an in-memory clip; it
  serialises as null and the unit comes back unanimated with nothing logged.
- `AnimationClip.legacy` must be set **after** the curves go in. SetCurve silently no-ops on a clip
  already marked legacy.
- `die` animates the ROOT's rotation, so the facing rotation cannot live on the same transform the
  clip drives or the first frame of a death snaps the corpse to face the camera. Hence the extra
  `facing` pivot above the animated node.

`RiggedUnits.Verify` is the guard: it samples the built prefab and fails if a joint that HAS a
rotation curve never moves. Sample ACROSS the clip, not at its midpoint — a breathing idle returns
to neutral there, which reported four working joints as frozen on the first run.

Layering is the other half. Troops hold a rifle at rest, but `idle` is a whole-body loop that
swings the arms down, so `holding-both` runs on a higher layer restricted to the two arms by
mixing transforms, and `holding-both-shoot` sits above THAT or firing is invisible. The weapon
hangs off `arm-right` and `BattleRunner` suppresses the pooled gun for any unit carrying its own —
the pooled ones are placed from the unit's root at a fixed chest offset, which is fine for a body
that never moves and visibly wrong the moment an arm does.

**A lesson that recurred four times**
Verify CONTENT, not counts, and prefer positive evidence over a plausible cause. Backgrounds
imported with the right count and no colour. Audio had correct clips, correct triggers, correct
volumes and no listener. The camera "hitched every drag" because a max was latching. Sounds fired
for events that never happened because they were inferred from list-length deltas. In every case
the instrument was wrong, not the engine.

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

## Open items — in the order I would take them

1. **Unit art: every class still renders as the same rifleman.** The largest thread by far, and
   the port has NO class differentiation at all on screen today. The go/no-go passed — one
   rifleman rebuilt on Kenney's joint hierarchy, animated, on device — so what remains is
   rewriting `build_units_v6.py`'s `finish()` for the limb hierarchy, regenerating, and wiring
   `modelAsset` → prefab.
   **It is now a much smaller job than the docs elsewhere imply**: the roster cut took it from
   ten silhouettes to SIX, and the rig already carries aim elevation. Read
   `UNIT_VARIETY_DESIGN.md`'s "what's been tried" first — seven attempts are recorded, several of
   which looked right in a Blender render and failed in real un-zoomed gameplay. Note step 1
   touches the builder the shipping Android build also uses.

2. **A decision, not a task: re-tune incendiary, or leave it.** `burnDamage = 6` was calibrated to
   finish the 8hp Sniper in one tick and that unit no longer exists (the roster cut gave the
   Sniper the Marksman's 16hp). It was deliberately NOT raised — doubling a 300-coin consumable is
   a balance call, not a side effect of deleting a class — so a tick is now a ~37% chip rather
   than a kill. `AmmoTest` anchors to the roster's frailest unit, so it will not silently expire.

3. **Loadout screen** (~415 lines) — the last large UI item. Battle HUD, aim overlay, background,
   audio and level navigation are all done, so `MIGRATION_SCOPE.md`'s UI estimate is stale high.

4. **`snowfall` is imported and ignored** — Winter's falling flakes are not ported. Winter is one
   campaign level now rather than eleven, so this is much less urgent than it was.

5. **Release build gaps** — debug-signed, APK not AAB, `versionCode` never increments.
   Deliberately deferred; see the README.

6. **Unverified, small:** the unit parade (L9) was rebuilt from two rows to a single row of six
   and has NOT been looked at on device. Reasoned rather than measured: six at 1.1 spacing is a
   half-width of ~2.75 against the two-row version's ~2.2, so it should frame slightly wider but
   far tighter than the nine-in-a-row case that caused the two rows in the first place.

### Things that will bite, gathered in one place

- **`Auto` cannot test STRUCTURES.** It targets the nearest enemy UNIT, so on any rig whose only
  enemies are the off-screen immortals it throws the whole volley past the buildings and structure
  HP never moves. This is why "rubble never observed falling" survived for weeks. Structure work
  needs a real aimed drag — the demolition rig copies L2's geometry so the shot is solvable:
  16 units, range = v²/g, so v = 8, i.e. 89% of the 9 maximum at 45°.
- **Enemy structures are OFF-FRAME at aiming framing, and that is correct.** The Aiming camera
  frames the PLAYER LINE ONLY, so every campaign level looks structure-less in a still. Drive a
  volley and the follow camera pans onto them.
- **The device drops off USB.** Twice in one session, not enumerating in `lsusb` at all; `adb
  kill-server` does not recover it and it needs a physical replug.
- **Never judge a visual from the preview alone.** `BackdropPreview` renders from x = 0 while the
  game camera sits over the player line, and it silently rendered every biome as bare sky and
  ground for a whole session (Unity fake-null after an unused-asset unload).

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
- **Game DATA still lives there**, and that will not change while the Kotlin is the source of
  truth. Any level, unit, roster or stage edit is a Kotlin edit followed by
  `python3 tools/export_kotlin_data.py ~/AndroidStudioProjects/ArmedConflict` and
  `DataImporter.Import` — even when the session is otherwise entirely Unity work.

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


## Data authoring, once Android is retired — OPEN, 2026-08-06

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

## Data authoring — DECIDED 2026-08-06: it moves into Unity

Rob closed the question above: **authoring moves into Unity.** The ScriptableObjects become the
source of truth. Not yet executed — it is Phase A of `_plans/TIER0_PLAN.md`, and the work is
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
