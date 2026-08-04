# Spike results — measured, not estimated

Device: Pixel 10 Pro XL (Tensor G5, PowerVR D-Series DXT-48-1536 MC1), Android 16, portrait
1080x2404. All builds RELEASE (IL2CPP, arm64, stripping on) — a debuggable build is not a
measurable one.

## Step 1 — renderer sanity: PASS

```
graphicsDeviceType=Vulkan  name=PowerVR D-Series DXT-48-1536 MC1
version=Vulkan 1.1.0 [1.634.2906]  srp=SpikePipeline
60.0 fps, 16.67 ms, no errors in logcat
```

The PowerVR driver line was the spike's biggest day-one risk (a live suspect in some of the
stranger Filament behaviour the Android build has fought). Unity on Vulkan is clean on it.

**GLES3 is still unverified.** Vulkan is first in the API list, so GLES3 only exercises where
Vulkan is unavailable. Proving it needs a second build with the order reversed.

## Step 2 — the camera solve: PASS

Ported from `SceneHost.kt:1303-1318` directly, not from the spike doc's summary, and re-solved
every frame as the original does.

```
 camZ | groundFracFromTop |     err | poleMeas | poleExact |   err | doc 1200/z
  4.0 |           0.68500 | 0.00000 |    308.5 |     308.5 | 0.00% |    300.5
  6.0 |           0.68500 | 0.00000 |    213.2 |     213.2 | 0.00% |    200.3
  8.4 |           0.68500 | 0.00000 |    155.3 |     155.3 | 0.00% |    143.1
 10.4 |           0.68500 | 0.00000 |    126.6 |     126.6 | 0.00% |    115.6
 14.0 |           0.68500 | 0.00000 |     95.0 |      95.0 | 0.00% |     85.9
 20.0 |           0.68500 | 0.00000 |     67.0 |      67.0 | 0.00% |     60.1
 40.0 |           0.68500 | 0.00000 |     33.8 |      33.8 | 0.00% |     30.1
```

The ground plane lands at 0.685 to five decimals across the whole clamp range (4..40) — a 10x
zoom span. The failure mode this criterion exists to catch is alignment holding at only one zoom.

`groundLift` is confirmed unnecessary: with a real 3D ground plane, poles placed off-centre in z
are correctly grounded with no correction.

### Trap 1: `px_per_world_unit = 1200 / camZ` is an on-axis approximation

It is exact only for something at depth camZ on the view axis. A ground-standing object sits
below the axis and is therefore NEARER than camZ, so it measures **~9.5% larger** at aiming
framing (126.6 px vs 115.6 px for a 1.0-unit object at camZ 10.4), and the gap widens with camZ.

This first showed up as a spurious Step 2 FAIL — the harness was checking against the
approximation. Projecting through the camera basis by hand gives 126.7 px against Unity's
measured 126.6, which settled it: the projection was right and the predictor was wrong.
**Any px-denominated target derived from `1200/camZ` carries this error.**

### Trap 2: CLAUDE.md's "89 px crowd unit" cannot be checked against a 0.48-scale unit

89 / 115.6 = 0.77 — the OLD `UNIT_SCALE_UNITS`. The 0.77 -> 0.48 shrink held apparent size
roughly constant only because the camera closed in to compensate, so camZ moved too. Comparing a
0.48 unit at the old camZ mixes two eras and proves nothing.

### Orientation is load-bearing

`GROUND_SCREEN_FRACTION` is a fraction of viewport HEIGHT. In landscape the height is 1080 rather
than 2404, the screen scale becomes 540/camZ, and every pixel check comes out ~2.2x small and
reads as a failed camera port. The project is pinned to portrait for this reason.

## Step 3 — 19 units + structure: PASS

L1 reproduced statically: 19 riflemen in formation, player tank, outpost with a 3-man garrison,
sandbags. Assets are the Android repo's own GLBs, imported via glTFast with node names intact.

```
units=19/19  guns=19/19  missingOrEmpty=0  totalRenderers=125
per-unit renderer histogram: 5rend x19
after 600 frames: avg=16.67ms (60.0 fps) worstFrame=16.94ms
```

Zero dropped frames. The audit counts renderers per unit and checks each has a non-empty mesh
rather than trusting the scene graph, because "valid state, never draws" is exactly the class of
bug that cost three separate investigations on Filament.

The four-tone unit split (`skin*` / `trim*` / `accent*` / uniform) works as ordinary glTF
materials assigned at build time — no runtime node-name override needed.

**Not yet verified:** that the SRP Batcher is collapsing the per-material draw calls. `UnityStats`
is editor-only and the Frame Debugger needs the editor GUI. 60 fps with headroom says it is not a
problem; it does not confirm the mechanism.

### The handedness bug — found, and worth the doc's warning

The first build rendered the green player squad on the RIGHT and the red enemy on the LEFT, the
reverse of L1's data. It looked entirely plausible, which is precisely why the spike doc says to
check against a known asymmetric layout.

The whole correction is one negation. ArmedConflict's world is Filament's: right-handed, camera at
+Z looking toward -Z, so +X is screen-right. Unity is left-handed; with the camera in the same
place, screen-right becomes -X while depth keeps its sense. So:

```
unity = (-gameX, gameY, gameZ)
```

This lives in ONE place (`GameSpace.ToUnity`) so it cannot be half-applied. Everything the spike
doc lists as needing an audit — `xSign`, gun offsets, `gunRotZ = 180 - gunAngle`,
`CAMERA_MIDFIELD_X`, `CAMERA_ENEMY_LEAN_X`, per-level x placement — must route through it.

## Environment notes

- `unityhub --headless install-modules -m android` STALLS on an interactive child-module prompt
  (`android-open-jdk`) when backgrounded. `--childModules` is required.
- URP 17.0.4 ships bundled with the editor and resolves offline; glTFast comes from the registry.
- The Pixel locks itself during long builds and a locked device backgrounds the app before
  `Start()` runs — which reads as "no output" rather than as a lock.
