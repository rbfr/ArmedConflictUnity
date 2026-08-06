# ArmedConflict — Unity port

Started as a spike evaluating whether ArmedConflict (native Android: Kotlin + Jetpack Compose +
SceneView/Filament) should move to Unity. **All four spike steps passed and Unity was chosen on
2026-08-04**, so this is now the port rather than an evaluation. The Android repo remains the
shipping build until this can replace it.

L1 is playable end to end: drag to aim, volley, swept collision, damage, structure collapse,
turn handover, victory — with sound, biome backdrop, per-type projectiles, scorch, rubble and a
battle HUD, at a steady 60 fps.

- Unity 6000.0.80f1 (Unity 6 LTS), URP 17.0.4 Mobile, IL2CPP / arm64
- Target device: Pixel 10 Pro XL (Tensor G5, PowerVR D-Series), Android 16

## Results so far

| step | what it proves | result |
|---|---|---|
| 1 — renderer sanity | renders on this PowerVR GPU, steady 60 fps, clean logcat | **PASS** (Vulkan) |
| 2 — the camera solve | the locked ground-line solve survives a different engine | **PASS** |
| 3 — 19 units + structure | 60 fps, and no unit missing head/arms/gun | **PASS** |
| 4 — one drag-aimed shot | trajectory + swept collision, continuous drag at 60 fps | **PASS** |

All four steps pass. Details, including the calibration traps found along the way, are in
`SPIKE_RESULTS.md`.

**Unity was chosen on 2026-08-04.** Godot was considered and dropped without spiking.

Both graphics APIs (Vulkan and GLES3) are verified on the device. The SRP Batcher's mechanism
remains unconfirmed — wall-clock frame time cannot resolve it on Android, where the swap is tied
to the display — but draw-call headroom is not in question: 3,101 renderers, 25x the real scene,
render inside one 120Hz vsync quantum.

`MIGRATION_SCOPE.md` inventories what a FULL port would cost — the 90% the spike deliberately
did not touch. Short version: 13,270 lines of Kotlin, of which 7,851 port mechanically, 2,798
get deleted outright, and ~2,600 lines of Compose UI have no migration path and must be
rewritten. Roughly 6-9 weeks.

## Layout

```
Assets/Editor/    headless project config, scene builders, APK build, data importer
Assets/GameData/  imported ScriptableObjects (units, structures, levels, backgrounds, stages)
Assets/Scripts/   runtime: camera solve, coordinate convention, per-step probes
Assets/Models/    GLBs copied from the Android repo, imported via glTFast
```

## Headless workflow

The editor GUI runs over VNC on llvmpipe and is painful for anything visual, so everything
here is driven from the terminal:

```bash
U=~/Unity/Hub/Editor/6000.0.80f1/Editor/Unity
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod SpikeSetup.Configure  -logFile -
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod SpikeSceneL1.Build    -logFile -
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod SpikeBuild.Android    -logFile -
```

`DISPLAY=:1` is required — Unity Hub core-dumps without an X connection even with `--headless`.

## Game data — authored in Unity

The ScriptableObjects in `Assets/GameData/` **are** the source of truth, as of 2026-08-06. Edit
them directly. Read `LEVEL_AUTHORING.md` before authoring or editing a level — it carries the six
composition rules, and they are checked rather than merely written down:

```bash
U=~/Unity/Hub/Editor/6000.0.80f1/Editor/Unity
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod LevelComposition.Report -logFile -
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod SandboxLevels.Generate -logFile -
```

`LevelComposition.Report` measures each campaign level by building it and reading the same
half-widths the camera uses, so it cannot drift from the game. `SandboxLevels.Generate` rebuilds
the eight roster/grouping rigs — the only levels that are generated rather than authored.

Current data: 15 units, 19 structures, 7 backgrounds, 24 levels (7 campaign + 17 test rigs),
4 stages.

### The retired Kotlin pipeline

Until 2026-08-06 this data was authored in the Android repo's Kotlin and imported one way, through
`tools/export_kotlin_data.py` → `data.json` → the importer. That arrangement existed because
Android was the shipping build; it stopped being so, and authoring moved here.

`LegacyKotlinImport` is kept so the original import can be reproduced or audited. **It overwrites
every asset in `Assets/GameData` in place, with no undo**, and refuses to run without
`-iAcceptDataLoss`. The exporter's hard-won parsing (it refuses loudly on anything outside the
narrow Kotlin subset those files use) is already baked into the committed assets.

## game/ — in progress

Ported so far: `TrajectoryPhysics`, `CollisionSystem` (as `SweptCollision`), `Formation`,
`SpringFollow`, `EnemyAI`, `CameraFraming` — all under `ArmedConflict.Game`.

`PortSelfTest` asserts the properties each original was written to guarantee, because "it
compiles" is not evidence a port is faithful:

```bash
DISPLAY=:1 $UNITY -batchmode -quit -projectPath . -executeMethod PortSelfTest.Run -logFile -
```

`GameState` and its entity types are ported as C# `record`s, so `with` is Kotlin's `copy()` and
value equality carries over — the immutable-state architecture survives intact. Note Unity
6000.0 compiles at C# 9: plain `record` works, `record class` (C# 10) does not, and `init`
accessors need the one-line `IsExternalInit` shim in `Assets/Scripts/Game/`.

Still to port: `ProgressStore`/`EconomyStore` (persistence rewrite) and `GameViewModel`
(3,418 LOC — the single biggest item in the migration, and worth breaking up on the way across
rather than transliterating whole).

### GameViewModel — sliced, in progress

`GameViewModel.kt` is 3,418 lines and is being ported in slices, each verified against the
Kotlin's documented behaviour rather than transliterated whole.

| slice | Kotlin | status |
|---|---|---|
| level construction (`buildUnits`, `buildInitialState`) | ~250 | **done** — `LevelBuilder` |
| aim + fire (`aimVelocity`, `onAimDragUpdate`, `onAimRelease`, `testAutoFire`) | ~350 | Step 4 covers the drag/solve path; the volley remains |
| tick: combat core (`resolveHits`, collapse propagation) | ~200 | **done** — `CollisionSystem` |
| tick: projectile stepping + culling | ~120 | **done** — `ProjectileSystem` |
| tick: turn flow + win/loss + awards | ~180 | **done** — `TurnFlow` |
| tick: camera choreography | ~200 | **done** — `CameraDirector` |
| tick: cosmetic layers (ragdolls, debris, scorch, shake) | ~350 | **done** — `CosmeticSystems` |
| tick: helicopter state machine | ~320 | **done** — `HelicopterSystem` |
| tick: events layer (boss phases, waves, wind) | ~400 | **done** — `EventSystems` |
| ragdolls + knockback (`ragdollFrom`, `applyDamageAndKnockback`) | ~160 | not started |
| consumables + reinforcements | ~120 | not started |
| battle lifecycle (`startBattle`, `jumpToLevel`, `restart`, `nextLevel`) | ~50 | not started |

**All eight slices are ported, under test (261 checks), and ASSEMBLED into a playable battle.**

`BattleTick` runs the systems in order against one GameState; `BattleRunner` owns the state,
takes the drag and renders it. Verified on the Pixel 10: L1 played from turn 1 to **VICTORY on
turn 7**, 8 of 10 player units surviving, the outpost destroyed and its garrison falling with it,
60.0 fps throughout with a worst drag frame of 16.8 ms and no exceptions.

Unit weapons and the biome backdrop (sky gradient, silhouette ridges, ground colour) render from
the level's own `BackgroundDefinition`, so the scene is now comparable like-for-like with the
shipping build rather than a bare stage.

Sound effects (all 8 clips, with the original rate limits), explosions, props and the AUTO
debug button are in. Auto responds to `adb shell input tap`, so a level can be driven entirely
from the terminal — which the shipping build's Auto button did not allow.

Scorch marks, rubble and the battle HUD are in — the HUD reads the same fields as the shipping
build (unit counts, structure HP, turn state, aim readout while dragging).

Still missing before this is a GAME rather than a battle: ground detail texture, consumables,
loadout input, and the lifecycle beyond one level (`restart`/`nextLevel`). Note the remaining UI
is NOT mostly economy: of ~2,600 Compose lines the loadout/purchase screen is ~415, and the
battle HUD and aim overlay — which no shippable build can skip — are now ported.

## Before release (deferred, 2026-08-05)

Unity generates and drives Gradle itself, so there is no build file to maintain — but the
current setup is spike-grade and NOT shippable:

- **Signing**: `AndroidKeystoreName` is empty, so builds use Unity's bundled DEBUG keystore.
  Play needs your own upload key. Create it once, keep it safe, and never lose it — losing an
  upload key means a Google-assisted key reset before the app can be updated again.
- **Format**: builds an APK. Play requires an AAB.
- **Versioning**: `bundleVersion 1.0` / `versionCode 1`, never incremented. Play rejects a
  duplicate `versionCode`.

None of this blocks development — a debug-signed APK installs over adb, which is all the port
has needed. It blocks the first Play upload or internal test track, and no sooner.
