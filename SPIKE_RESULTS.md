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

## Step 4 — one drag-aimed shot: PASS

`TrajectoryPhysics` and the swept-segment collision ported and run in the TICK, not in Unity's
Rigidbody system — the same locked call as the Android build. `Application.targetFrameRate = 60`,
one steady rate, never varied by game state.

### Landing accuracy

Full-power 45-degree shot, integrated landing vs the analytic `v^2/g`:

```
dt=8.33ms  (120Hz):  landed=20.1970  analytic=20.2500  err=-0.262%
dt=16.67ms  (60Hz):  landed=20.1439  analytic=20.2500  err=-0.524%
dt=33.33ms  (30Hz):  landed=20.0373  analytic=20.2500  err=-1.050%
```

Error is exactly LINEAR in dt, which is what semi-implicit Euler predicts: the discretised
flight time is short by dt, so the shot lands short by roughly `vx * dt / 2` at the interpolated
ground crossing. At 60Hz that is 0.106 world units on a 20-unit shot — about a quarter of the
0.380 hit radius. This is a property of the integrator, not of the port: the Android build has
the same behaviour by construction.

**This reproduces CLAUDE.md's documented dt sensitivity.** That note says a smaller dt makes
shots land "0.15-0.35% longer". Here 60Hz -> 120Hz is -0.524% -> -0.262%, i.e. **0.262% longer** —
inside the documented band. Independent confirmation the integrator ported faithfully.

Measuring this needs care: reporting the first sample BELOW ground measures the overshoot, which
grows with dt and cancels most of the integrator's own error. That made 30Hz and 60Hz runs look
identical (20.1526 vs 20.1525) before the crossing was interpolated.

### Aim scale

```
maxRange45=20.25   L1 separation=16.50   -> REACHABLE
drag= 10.0u -> speed=3.840  ( 43%)  angle=45.0deg
drag= 23.4u -> speed=8.986  (100%)  angle=45.0deg
drag= 40.0u -> speed=9.000  (100%)  angle=45.0deg
drag= 80.0u -> speed=9.000  (100%)  angle=45.0deg
hitRadius=0.3803
```

Speed saturates at MaxAimMagnitude rather than growing invisibly past the readout's 100% — the
clamp lives in `AimVelocity`, which the preview and the shot both call, so the hint cannot
describe a different round than the one that flies. Hit radius matches the Kotlin's ~0.38.

### The gesture — the criterion this whole spike is about

Seven consecutive drags, ~42 frames each (~295 drag frames total), measured PER GESTURE:

```
drag #1 frames= 5  worst=16.7ms  >20ms x0
drag #2 frames=42  worst=33.4ms  >20ms x1
drag #3 frames=41  worst=16.7ms  >20ms x0
drag #4 frames=43  worst=33.3ms  >20ms x1
drag #5 frames=42  worst=17.3ms  >20ms x0
drag #6 frames=42  worst=16.8ms  >20ms x0
drag #7 frames=42  worst=16.8ms  >20ms x0
```

**Five of seven drags are completely clean at 16.7-17.3ms. Two single dropped frames across
~295 drag frames (0.7%).** No rate transition under the finger, and nothing resembling the
Android build's defect — an aim drag spending its first ~400ms at 30Hz while the panel caught up.

The two dropped frames are real, not warm-up (they occur on drags #2 and #4, not #1), and their
cause is NOT isolated. They may be an artefact of synthetic event injection. Worth re-checking
with a real finger before this is called closed.

Caveat: measured with `adb shell input swipe`, not a hand. The frame time is objective;
"feels continuous" is not something a script can answer.

**Instrumentation warning, learned the hard way.** A LATCHING max across gestures reports one old
hitch forever and reads as "every drag hitches" — which is exactly how this was first misread,
reporting 33.2ms on ten consecutive drags when only one long frame had ever occurred. Reset
per-gesture counters at touch-down, and track a separate all-time max if you want one.

### Shot -> impact -> damage

```
fired 80% at 45.0deg -> predicted x=4.42
HIT unit 0 at x=3.85 y=0.53 hp=24 (flight 2.61s)
HIT unit 0 ... hp=16 / hp=8 / hp=0 KILLED
```

Predicted landing 4.42 against unit 0's actual x of 3.89 — the swept check catches the round on
the descending arc before it reaches the floor, which is the behaviour the sweep exists for.
Four rifle hits at 8 damage against 32 HP kills, matching `UnitDefinitions.Rifleman`.

## Environment notes

- `unityhub --headless install-modules -m android` STALLS on an interactive child-module prompt
  (`android-open-jdk`) when backgrounded. `--childModules` is required.
- URP 17.0.4 ships bundled with the editor and resolves offline; glTFast comes from the registry.
- The Pixel locks itself during long builds and a locked device backgrounds the app before
  `Start()` runs — which reads as "no output" rather than as a lock.
- The WIRELESS adb transport drops during long builds; the USB transport (`57121FDCQ005LC`)
  survived every drop. Prefer USB for a long session.
- IL2CPP segfaulted once mid-session (exit 139, no C# error). Deleting
  `Library/Bee/artifacts/Android/il2cppOutput` and rebuilding cleared it. Note this box has a
  history of hard lockups, so a toolchain crash is not automatically a Unity bug.
