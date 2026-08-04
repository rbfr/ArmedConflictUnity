# ArmedConflict — Unity Migration Spike

Evaluating whether ArmedConflict (native Android: Kotlin + Jetpack Compose + SceneView/Filament)
should move to Unity. **This is a spike, not a migration.** The shipping build stays in the
`ArmedConflict` Android repo; see `UNITY_SPIKE.md` there for the plan, pass/fail criteria and
kill criteria, and `GODOT_SPIKE.md` for the alternative under consideration.

- Unity 6000.0.80f1 (Unity 6 LTS), URP 17.0.4 Mobile, IL2CPP / arm64
- Target device: Pixel 10 Pro XL (Tensor G5, PowerVR D-Series), Android 16

## Results so far

| step | what it proves | result |
|---|---|---|
| 1 — renderer sanity | renders on this PowerVR GPU, steady 60 fps, clean logcat | **PASS** (Vulkan) |
| 2 — the camera solve | the locked ground-line solve survives a different engine | **PASS** |
| 3 — 19 units + structure | 60 fps, and no unit missing head/arms/gun | **PASS** |
| 4 — one drag-aimed shot | trajectory + swept collision, continuous drag at 60 fps | not started |

Details, including the two calibration traps found along the way, are in `SPIKE_RESULTS.md`.

## Layout

```
Assets/Editor/    headless project config, scene builders, APK build (-executeMethod)
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
