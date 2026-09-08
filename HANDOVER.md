# Handover — Unity, as of 2026-09-08

## Pick up here

Branch `session/2026-08-25-shell-art-ragdoll`. Sitting is on origin
(`e12f995` L13/look). Ask git before writing on top.

Rob signed 09-06 on device: muzzle *"ok that looks good."* Camera
*"ok this looks much better."* Wrecks *"yeah that's better."* L6 at 160 hp
*"looks more reasonable."*

**This sitting (09-08):** L13 **Scorched Apron** (Ashfield, starsToUnlock 16)
plus tank roll-in audio. On the phone, iterated, **not signed as a set**.
USB `57121FDCQ005LC`. `PortSelfTest` ALL PASS. Scene 30 levels / 91 models.
`Builds/Step1.apk` ~633MB.

### L13 as it stands

Open-mouth hangar at **x 7.8**, worldScale **2.5**, hitWidth **3.40**,
deckY **2.05**, catwalk `deckStandZOffset` **1.45** (no parapet — helmets
only at 6°). CORE mesh is `Hangar` + trim/accent; chunks are skin + a
roof scar. Collapse is **Legacy Animation** (`hangar_collapse.glb.meta`
`animationMethod: 1`).

In-bay jet: `prop_wreck_fighter.glb` at **(7.8, 0.90) scale 1.7**,
`collapsesWith: hangar` — hides and throws two blasts when the hangar
dies. Apron wrecks stay. Charge 5 rifle at x 3.0 advance 1.4; 2 MG at
4.4; garrison 10 rifle + 4 grenadier crowd on the hangar.

Wrecks are the **CC0 Jetliner** (`tools/blender/wreck_from_real_planes.py`,
`tools/blender/cc0_planes/`). Fighter yaw **+0.55** longest **3.8**;
transport yaw **−0.50** longest **4.4**. `build_airport.py` must **not**
overwrite those glbs — it only exports hangar / runway / tower.

**Tank tracks:** `Assets/Audio/tank_tracks.wav` (converted from the MP3,
mono 44.1 PCM, DecompressOnLoad). Plays on `TurnPhase.TankArrive`,
**0.55s fade** when the hull parks. Hard-stop on `LoadLevel`. Own
`AudioSource` so Stop cannot kill a one-shot.

### Tried and rejected this sitting — do not reopen

- Hangar every plate `chunk_N` — one shell hid the roof, garrison floated.
- Thick lid (0.42) — warehouse. *"that's not what we want."*
- Front-facing dark slab in the mouth — reads as a closed garage door.
- Hanging door chunk in the mouth (stripped after a zoom-in).
- Stripping the in-bay jet with the door — Rob wanted the jet **in** the
  hangar; it was pulled forward to z 0.90 instead.
- Box-built wreck planes; fighter on its side (~80° roll); nose-plant;
  Jetliner +90° X (stood on its tail). Rest on tarmac, yaw only.
- `hangar_collapse.glb` Mecanim (`animationMethod: 2`) — wreck sat at
  rest, still standing. All other `*_collapse.glb` are method **1**.

### Next session, if playing on device

Phone is on the loadout of this APK. Campaign run L1→L6 through the
picker is still owed. L13 is playable — ask him to sign the hangar,
the planes, the collapse+jet cookoff, and the tank rattle fade.

### Signed 09-06 — do not reopen

**Wrecks sit back, bosses stay on the ground line.** `LevelScenery.WreckBackZ`
**-1.6**. L6 Sovereign **8.5** / heavy **6.5**, z **0.4**. Tried and rejected:
z-forward (Sovereign looked closer to the camera), near-flank (behind the
still-standing bunker), past-the-wreck (still in the collapsed splat).

Rule 10 samples collapse **last frame** for MaxZ, rest pose for width/height
(the fallen clip is a pancake that swallowed neighbouring garrisons in X).
L6 dirt trio 2.25 → **1.70** (heavies 0.28). L7 mast 10.6 → **10.9**. L12
bosses still `anchorZ` **3.4** — wreck-back is global, they may now sit
further forward than they need; do not pull them without an ask.

**Structure hits:** whole-building charcoal was tried and rejected (the mesh
just turned dark). Live buildings stamp **soot + crater at the impact**
(shells/rockets/grenades a hole; rifle a chip). Chunk shedding and the death
wreck stay.

**Camera:** `CameraDirector.ShooterHoldSeconds` **0.45s** on the people who
fired, then the existing volley chase. Windup and post-volley rest frame
**living bodies** (`LivingActors`), not the captured side (structure edges
stay on scout). A lone Sovereign is a portrait, not the empty middle of
their half. Aim frame is still locked — do not widen it.

**Muzzle flash:** Blender low-poly `Assets/Models/fx_muzzle.glb` (star + cone
along +X), `Assets/Prefabs/MuzzleFlash.prefab`, 64-slot pool in
`BattleRunner`. Additive, infantry and tank. Skip heli / strafe / airstrike.
`GameSpace.FromToRotation` so the cone follows the shot (game +X = Unity -X).
Built over Blender MCP; addon protocol is a version behind,
`execute_blender_code` still works. Never `read_factory_settings` (kills the
session and drops port 9876). Splash/`window_close` also killed Blender —
relaunch stock so auto-start works.

### Closed 09-05 — do not reopen as open work

- Sovereign **160 hp** (was 260). L6 escort **3 → 1**. Boss telegraph banners
  gone (empty `telegraphLabel` is authored). Wave telegraphs stay.
- Rifle tracer **0.22**, rocket **0.18**, grenade 0.16, shell 0.34.
- Auto writes `Last:` from the first shooter's launch (unclamped).
- Rules 10 (wreck neighbours) and 11 (live mesh) are standing.

## Standing

Rules **8–11**. Wrecks sit back (`WreckBackZ` -1.6). L6 bosses at the keep, z 0.4.
L12 `anchorZ` 3.4. Volley holds **0.45s** on shooters, then chases; windup looks
at living actors. Heroes 1.45 / Sovereign 1.65 with brass-gold trim. Sovereign
**160 hp**. Campaign: 13 levels (L13 Scorched Apron / Ashfield), 2 warnings
(L3 rule 7, L5 separation 11.6), 0 errors. `PortSelfTest` ALL PASS after authoring.

`PortSelfTest.Run` after every change. **RIGS** is the test supply (consumables,
classes, ammo) — a clean install resets it. **Do not use Auto** for structures,
ammo, or consumables. Android repo is RETIRED. `DISPLAY=:0`. **Ask git.**
Uninstall/reinstall does **not** wipe coins (Android backup); it does reset RIGS.

L13 kit, muzzle, and tank tracks are in `e12f995`. Leftover and **not
wired** — still untracked, do not treat as product:
`Assets/Models/Kenney/Particles/` and `Assets/Materials/TracerSprite.mat`.

### Owed

1. A campaign run **L1 → L6 through the picker**.
2. Rocket **0.18** at melee, if it has not been signed on a charge.
3. Optional, ask first: pull L12 bosses back now that wrecks sit behind the
   ground line.
4. **Sign L13 on device** — hangar, apron wrecks, collapse + bay-jet
   cookoff, tank-track fade. Composition green, APK current; the look
   has not been signed as a set.

### Do not open unprompted

Tracer look, Kenney particles, a new death clip. Leftover files above.

Wind is cosmetic and parked (collision is X/Y). Heli stays shut. Do not
widen the aim frame — analysed 2026-08-17, recommendation is no; camera is
locked. Whole-building charcoal — tried 09-06, rejected. Z-forward bosses —
tried 09-06, rejected (looked closer to the camera). Hangar as a warehouse
lid, a closed-door mouth, or Mecanim collapse — tried 09-08, rejected.
Do not re-export the CC0 wrecks from `build_airport.py`.

### Closed 08-27 → 08-28 — do not reopen as taste

- **L4 shellsOverride = 3.** Played HOLD-then-ARM, defeat T12. 2 of 3
  shells missed walls — does **not** ask to bump. Leave it. Do not
  widen the aim frame. PlayerTank stays at 5.
- **L5 walk-back is on the APK** (riflemen 10→7, budget 16→13,
  packing **0.8** kept). Authored mix 7 rifle + 2 grenadier + 1
  sniper = 13/13. Tank stays off. **Played through the picker
  09-01, won 2★** — the walk-back is signed. Do not walk it
  back further.
- **Elbow kept.** All seven classes. Aiming is still the
  hold-the-gun read. Rob: *"elbow is fine, let's keep it."*
- **Last-aim HUD.** `Last: power N%    angle N°` under Your turn.
- **Rifle tracer signed.** Un-tapered flat orange dash, opaque, no
  tail. Rob: *"ok, we can use this. kind of goes with the theme...
  not super realistic, maybe mid-90s feel."* Teardrop = rocket;
  Kenney `trace_01` = glow streak; both rejected. Rockets /
  grenades / shells keep their meshes. `GAME_DESIGN_LOCKS.md`.
- **L1 tank operator.** One rider on the hull, nine on the ground,
  `deployBudget` 9. If he dies: panel `NO GUNNER`, no shell, ammo
  unspent. Loadout cannot replace him. **Other campaign tanks still
  field two** until asked.
- **Dirt deaths skid.** Stay on the dirt, slide backwards, flop
  over. No hop (that bounced), no log-roll, no Kenney `die`. Rob:
  *"ok this is fine."* A crumple clip is not the next move unless
  he asks. Deck falls unchanged.

## Closed history

Moved WHOLE to `HANDOVER_ARCHIVE.md` on 2026-09-05 (third split): the 09-04 sitting logs,
08-12 → 09-02 closed work, the 08-25 code sitting, and the 08-07 siege-retune write-up
(since resolved). Nothing was deleted. Traps that can still bite stayed in this file.

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

All 30 levels are reachable and play end to end at a steady 60 fps: drag to aim, volley, swept
collision, damage, structure collapse, turn handover, victory. With sound both sides, a per-level
biome backdrop, per-type projectiles, unit weapons, fading explosions, scorch marks, structures
that shed their own geometry and leave a ruin when they fall, a battle HUD, level navigation and
an Auto button. Units are ANIMATED — idle, a two-handed hold, recoil, death — and both lines raise
their rifles to the angle they are actually firing at. Infantry and the tank fire a pooled
low-poly muzzle burst (`fx_muzzle.glb`). Live buildings take a soot+crater stamp at the impact
instead of a whole-mesh tint.

**The product spine is in** (Tier 0, 2026-08-06): a 12-level campaign with the test rigs gated
behind a RIGS toggle, a pre-battle LOADOUT picker, a VICTORY CARD paying coins and stars with the
reason shown, mid-battle EVENTS that fire and announce themselves a turn ahead, and a live
economy — coins, stars, unlocks, first-clear and daily bonuses, milestone chests.

All eight `GameViewModel` slices are ported (`LevelBuilder`, `CollisionSystem`,
`ProjectileSystem`, `TurnFlow`, `CameraDirector`, `CosmeticSystems`, `HelicopterSystem`,
`EventSystems`) plus `GameState`, `Formation`, `SpringFollow`, `EnemyAI`, `CameraFraming`,
`TrajectoryPhysics`, `SweptCollision`, `ProgressStore`, `EconomyStore`. Campaign is 13 levels plus 17 test rigs (30 total).

**`PortSelfTest` asserts behaviour**, not that the code compiles. Run it after every change:

```bash
U=~/Unity/Hub/Editor/6000.0.80f1/Editor/Unity
DISPLAY=:0 $U -batchmode -quit -projectPath . -executeMethod PortSelfTest.Run -logFile -
```

## The workflow

Headless. The editor GUI runs over VNC on llvmpipe and is painful; you never need it.

```bash
U=~/Unity/Hub/Editor/6000.0.80f1/Editor/Unity
DISPLAY=:0 $U -batchmode -quit -projectPath . -executeMethod SpikeSceneBattle.Build -logFile -
DISPLAY=:0 $U -batchmode -quit -projectPath . -executeMethod SpikeBuild.Android  -logFile -

export PATH=$HOME/Android/Sdk/platform-tools:$PATH
export ANDROID_SERIAL=57121FDCQ005LC          # USB. The WIRELESS transport drops on long builds.
adb uninstall com.dullesengineering.armedconflictspike; adb install Builds/Step1.apk
adb shell monkey -p com.dullesengineering.armedconflictspike -c android.intent.category.LAUNCHER 1
adb shell input tap 180 2210                  # the AUTO button — drives a level from the terminal
```

`DISPLAY=:0` is mandatory for anything Unity/Hub (the old `:1` note is stale). The app id is `...armedconflictspike`,
deliberately NOT the shipping id, so both builds sit on the phone for A/B.

### CHECK `mCurrentFocus` BEFORE EVERY adb INPUT BATCH. It is Rob's real phone.

**This has now hijacked into personal apps FOUR times**, most recently on 2026-08-10: DND had been
switched off at what looked like the end of device work, a notification arrived mid-sequence, and a
tap aimed at the RIGS button opened a private conversation instead. One frame of it was captured
and deleted; no further input reached that app.

```bash
adb shell dumpsys window | grep -i mCurrentFocus | grep -q armedconflictspike || exit 1
```

Put that line before each batch and abort on it, rather than trusting that the game was in front a
minute ago. Also:

- **DND ON for the whole session**, and only off when the phone is actually being handed back —
  not when device work merely looks finished. **`settings put global zen_mode 2` NO LONGER WORKS
  on this device** (2026-08-10): it returns success, `settings get` reads back `0`, and DND stays
  off — a silent no-op in the exact place a silent no-op is most expensive. Use
  `adb shell cmd notification set_dnd priority`, and VERIFY with
  `dumpsys notification | grep mZenMode` — `ZEN_MODE_OFF` means it did not take.
- **Never `KEYCODE_BACK` for in-game navigation.** Use HOME to leave, and uiautomator-found bounds
  to press things.
- **Restore what you changed**: auto-rotate, DND, `svc power stayon`.

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
  SFX live as **WAV** (DecompressOnLoad, mono). An MP3 imports, then decompresses to the
  same PCM — convert with ffmpeg (`-ac 1 -ar 44100 pcm_s16le`) into `Assets/Audio/` and
  run `AudioImportSettings.Apply`. A loop that must cut with a beat **fades**; `Stop()`
  is a click (tank tracks, 09-08).
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
- **`Assets/GameData/` IS the source of truth** (since 2026-08-06). Edit the
  ScriptableObjects directly. The Kotlin export pipeline is retired.
- **`LegacyKotlinImport` still exists and will destroy your work.** It overwrites
  every asset in `Assets/GameData` from `data.json`, in place, with no undo. It
  refuses to run without `-iAcceptDataLoss`. Do not remove that guard.
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
  kill-server` does not recover it and it needs a physical replug. Hit again 09-06 after the
  Blender-mesh APK (`DEVICE_NOT_READY`).
- **Never `bpy.ops.wm.read_factory_settings` over Blender MCP.** It kills the process and drops
  port 9876. Splash/`window_close` did the same. Relaunch stock Blender so auto-start comes back.
  The addon protocol is a version behind; `execute_blender_code` still works.
- **glTF imports as QUATERNION.** Set `rotation_mode = "XYZ"` before Euler keys or
  `rotation_euler` does nothing (fighter wreck sat belly-down until this).
- **Collapse glbs must be Legacy Animation** (`animationMethod: 1` in the `.meta`).
  Hangar shipped as Mecanim (2) — `WreckAnim` plays `Animation`, the intact rest
  pose sat there "still standing". Overwriting the glb does not fix a stale meta.
- **`build_airport.py` must not export the wreck planes.** Those glbs are the CC0
  airliner from `wreck_from_real_planes.py`. The box builders in `build_airport.py`
  are reference only. Jetliner.obj is already Y-along-fuselage / Z-up after
  `wm.obj_import` — a 90° X stands it on its tail.
- **Never judge a visual from the preview alone.** `BackdropPreview` renders from x = 0 while the
  game camera sits over the player line, and it silently rendered every biome as bare sky and
  ground for a whole session (Unity fake-null after an unused-asset unload).

## Still open

- **`snowfall` is imported and ignored** — Winter's falling flakes are not ported. Low
  value: Winter is one campaign level.
- **Release build gaps** — debug-signed, APK not AAB, `versionCode` never increments.
  Deliberately deferred; see the README.
- **MG burst fan** has not been looked at on a device since it was made to fan.
- **Shield armour is live and invisible** at gameplay framing.
- **Still no blood.**
- **Flames outlive their bodies** by a frame or two at the kill. Parked in `_plans/BACKLOG.md`.
- **`_plans/BACKLOG.md`** also holds: nuclear reactor structure (mechanic TBD), crowd-runner
  bonus level, mid-ground scenery variety, look-pass.

## RIGS doubles as TEST SUPPLY

While RIGS is ON, consumables, classes, camo and ammo are free to equip and nothing is
spent or written. The HUD reads `TEST`. Carry under test supply is every item at 1,
ignoring the picker cap of two, and the toggle re-reads into the battle already running.
`showRigs` is not persisted. `BattleUIPreview` stakes coins so the check can actually fail.
