# Migration scope — what a full Unity port actually costs

Measured against the ArmedConflict Android repo, 2026-08-04. Line counts are real, effort
estimates are judgement. **This is an inventory, not a decision.**

The spike deliberately measured the risky 10% (camera solve, renderer, physics, one shot) and
all four steps passed. This document covers the other 90%, which is not risky but is large.

## The codebase: 13,270 lines of Kotlin across 30 files

| layer | LOC | fate in Unity |
|---|---|---|
| `game/` | 5,265 | **ports** — mechanical translation to C# |
| `data/` | 2,586 | **ports, and improves** — becomes ScriptableObjects |
| `ui/` | 5,397 | **split**: 2,798 deleted, ~2,600 rebuilt from scratch |

### `game/` — 5,265 LOC, ports almost directly

Only TWO files touch Android at all: `ProgressStore` (SharedPreferences → `PlayerPrefs` or JSON)
and `GameViewModel` (extends `ViewModel`). Everything else is plain Kotlin.

| file | LOC | notes |
|---|---|---|
| `GameViewModel.kt` | 3,418 | the tick. Big, but engine-independent logic — turn flow, volleys, damage, ragdolls, economy, events |
| `GameState.kt` | 624 | immutable state; C# records or structs |
| `Formation.kt` | 257 | pure math, already partly ported for Step 3 |
| `CollisionSystem.kt` | 236 | **ported in Step 4** |
| `ProgressStore.kt` | 192 | needs a persistence rewrite |
| `EconomyStore.kt` | 191 | ditto |
| `TrajectoryPhysics.kt` | 154 | **ported in Step 4** |
| `SpringFollow.kt` | 95 | the shared smoothing primitive |
| `EnemyAI.kt` | 73 | trivial |
| `CameraFraming.kt` | 25 | trivial |

The immutable-state-plus-tick architecture survives intact — Unity does not force `MonoBehaviour`
soup on gameplay logic, and the locked "physics in the tick, not Rigidbody" call already holds.

`GameViewModel` at 3,418 lines is the single biggest item in the whole migration and deserves to
be broken up on the way across rather than transliterated whole.

### `data/` — 2,586 LOC, the one area that gets BETTER

12 levels, 9 unit classes, 12 structures, 7 backgrounds, 5 stages. Exactly one file
(`BackgroundDefinition.kt`) imports anything Android — a Compose `Color`.

As ScriptableObjects this gets an inspector for free, which is the one place Unity is genuinely
nicer than what exists today. The 12 campaign levels plus 17 test levels are data, not code.

### `ui/` — 5,397 LOC, the real cost

| file | LOC | fate |
|---|---|---|
| `SceneHost.kt` | 2,798 | **DELETED.** This is the renderer the migration exists to remove |
| `BattleScreen.kt` | 920 | rebuilt in Unity UI |
| `BattleBackground.kt` | 630 | rebuilt — and likely simplified, since a real 3D ground replaces the painted band |
| `LoadoutScreen.kt` | 415 | rebuilt |
| `AimOverlay.kt` | 268 | rebuilt (gesture logic ported in Step 4) |
| `SoundEffects.kt` | 177 | rebuilt on Unity audio |
| `DebugCamera.kt` | 89 | rebuilt (trivial) |
| theme | 100 | discard |

**~2,600 lines of Compose have no migration path.** Jetpack Compose and Unity UI share no
concepts; this is a rewrite, not a port. It is also the least interesting work in the project.

## What SceneHost's deletion is actually worth

`SceneHost.kt` is 2,798 lines: **1,158 comment lines (41.4%)**, 1,585 code, 55 blank.

That 41% is the number the spike doc quotes, and it is comment density — overwhelmingly
archaeology documenting Filament workarounds. Each of these disappears entirely:

- `enforceTransform()` per frame — TransformManager corruption under concurrent GLB loads
- `setCulling(false)` re-armed per slot — wrongly-culled renderables that read as headless units
- `warmUnit()`, one new unit slot per frame — the hero that rendered a gun and no body
- projectile pool pre-warm one-per-frame via `withFrameNanos` — "fires and damages but never draws"
- `ProjectileIdSpace` bands and zero-disposal registries — the session that gets 2.1x more expensive
- `NodeLifecycle` race workarounds
- `groundLift()` — **confirmed unnecessary in Step 2** with a real 3D ground plane

Not one of these is a feature. They are the cost of hand-writing a renderer against Filament.

## What carries over untouched

- **35 Python tools**, including 32 Blender build scripts. The asset pipeline does not change.
- **52 GLB models** — imported directly via glTFast, node names intact (verified in Step 3).
- 8 sound files.
- Every measured constant, and the reasoning in CLAUDE.md that produced them.

## Rough effort

| area | estimate | confidence |
|---|---|---|
| `data/` → ScriptableObjects (incl. 29 levels) | 3-4 days | good |
| `game/` → C# (3,418-line ViewModel dominates) | 8-12 days | medium |
| `ui/` rebuild in Unity UI | 8-12 days | **low** — hardest to estimate, most tedious |
| scene/rendering layer (replaces SceneHost) | 4-6 days | medium — Steps 2-4 already did the hard parts |
| audio, persistence, build/release plumbing | 2-3 days | good |
| handedness audit across all level data | 0.5-1 day | good — `GameSpace` exists, needs applying |
| re-verification on device against the current build | 3-5 days | low |

**Total: roughly 6-9 weeks of focused work.** The UI rebuild and the re-verification are the
soft numbers; everything else is bounded.

## The honest framing

Nothing here is risky — the spike already retired the risk. The question is whether 6-9 weeks
buys enough. What it buys:

- the whole class of "valid state, never draws" bugs stops existing
- session accumulation (2.1x work growth over a session) **cannot** exist — it is a consequence
  of zero-disposal registries
- 6-10 draw calls per soldier becomes an SRP Batcher problem rather than a hand-tuned one
- measurement becomes a Frame Debugger session instead of `simpleperf` plus arithmetic
- iOS stops being impossible

What it costs, beyond the weeks: ~2,600 lines of working, iterated UI thrown away, and a fresh
crop of unknown engine quirks replacing a known set.

## Open before any decision

- GLES3 unverified (Vulkan is first in the API list)
- SRP Batcher draw-call collapse unconfirmed (needs the editor Frame Debugger)
- No fair visual A/B yet — same biome, same framing, both builds side by side
- The 0.7% dropped frames during drag were measured with synthetic input, not a finger
- `GODOT_SPIKE.md` has never been run at all
