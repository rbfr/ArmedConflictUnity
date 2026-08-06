# Camera / Aiming / Projectile-Rendering Architecture

**Status:** partially implemented on `camera-rearchitecture-v2` (`main` still runs the
pre-rearchitecture system described in `L24_VOLLEY_HIT_CAMERA_PLAN.md` §11/§12). Shipped so
far: Phase 1 (`SpringFollow`, tested), Phase 2 (camera X through it), Phase 3 (camera Z/zoom,
including the bullet cam, moved into the tick and through `SpringFollow`), and Phase 5 (the
volley-convergence centroid smoothed the same way, plus two follow-on fixes below). Phase 4
(replace the phase `when` block with the `CameraFraming` focus-point-set model — `CameraFraming.kt`
exists and is tested but not yet wired in) and Phase 6 (consolidate echo/trail/convergence
into one render-state concept) are still open. This doc exists so the next round of work
doesn't reinvent the same fixes a sixth time — see "Why this doc exists" below.

## Why this doc exists

Between 2026-07-18 and 2026-07-22, the camera/projectile-rendering system was patched
**at least seven times** across two files (`SceneHost.kt`, `GameViewModel.kt`), each time
fixing a real, confirmed, on-device bug: melee camera pan-back, zoom clipping the shooter
line, zoom stuck wide on heli-capable levels, "shot comes from behind the building" (twice —
once wrongly diagnosed as camera, actually AI launch angle + missing flight trail), "goes
over their heads but still registers" (collision tunneling), "too much zoom/pan," and finally
a volley-convergence feature that shipped with a jitter bug. Every one of the last three bugs
had the **same root shape**: a per-tick value derived from the mean/bounds of a *changing
set* (live projectiles landing raggedly, melee units settling out of a march) is discontinuous
even when every individual member moves smoothly, because set *membership* changes
non-monotonically relative to position — and each time, it was fixed with a new hand-rolled
smoothing formula local to that one feature, not a shared primitive. See
`/home/rob/.claude/plans/rosy-skipping-stardust.md` (2026-07-22) for the full incident
writeup this doc's design responds to.

## Design principles

1. **One smoothing primitive, used everywhere a tick-synced value needs to follow a
   possibly-discontinuous target.** A critically-damped spring integrator (position +
   velocity, `dt`-parameterized), pure and unit-tested like `TrajectoryPhysics.step` —
   not a per-feature `value += (target - value) * magicConstant` line. Velocity continuity
   means a retarget (the live set's mean jumping tick-to-tick) doesn't read as a snap.
2. **Compose `Animatable` is for UI-only, one-shot transitions — driven with `spring()`, not
   `tween()`.** Per Jetpack Compose's own docs, `Animatable` cancels and smoothly retargets
   an in-flight animation on a new target *only* when using a spring spec; `tween()`
   restarts its time-based curve from scratch on every retarget. Anything tick-synced with
   game state (camera follow during a volley) does not belong in Compose at all — it beats
   against the tick clock and jitters (this constraint predates this doc and stays true).
3. **"What the camera frames" is one data-driven concept, not N bespoke half-width
   formulas.** Each phase/sub-state supplies a *focus point set* — the world-X coordinates
   that must stay in frame right now (e.g. "ranged shooters + structures" vs. "the live
   projectile cluster" vs. "the marching group"). One shared, tested function converts any
   focus point set → half-width → camera Z. Bugs like shooterReach silently including
   settled melee units happen when "who's in the set" and "how do we turn that into a zoom
   number" are tangled in the same bespoke formula; keeping them separate makes "who's
   included" the only thing a phase has to get right, with the conversion math centralized.
4. **Cosmetic (rendered) position vs. authoritative (collision) position have one explicit,
   named boundary**, not three independent "offset from truth" mechanisms (echo tracers,
   flight trail, volley convergence) each reasoning about safety in a comment. Collision,
   `Detonation` position, and hit-registration must be structurally incapable of reading a
   cosmetic offset — not just conventionally expected not to.

## Status of prior mechanisms (input to the redesign, not necessarily final)

- **Swept collision** (`CollisionSystem.kt`, `ProjectileEntity.prevX/Y/Z`): solid, tested,
  no known bug. Treat as a correctness bar to preserve through the redesign.
- **AI-launch-angle flight trail** (`AimOverlay.kt`): solves a real, confirmed problem
  (steep AI arcs visually "materializing" before the eye connects them to the muzzle).
  Likely survives in spirit even if the render-state consolidation (principle 4) changes
  its implementation.
- **Per-phase zoom, bullet cam**: moved into `GameState.cameraFollowZ` / the tick (Phase 3),
  smoothed via `SpringFollow` in place of the old Animatable+tween+hand-rolled-decay stack.
  Verified on-device (smooth, no oscillation across phase transitions). The `when` block
  itself is still the 5-variable formula from before — Phase 4 (route it through
  `CameraFraming.halfWidth`) hasn't happened yet, this only changed how the *result* is
  smoothed.
- **Volley convergence**: centroid smoothed via `SpringFollow` (Phase 5), fixing the original
  jitter bug. Two more bugs found by testing on minimal debug levels (rocket-only, then
  bullet-only — see git log on this branch) after the jitter fix made them visible: (1) the
  blend was pulling projectile *height* toward the group mean, which fights the convergent-fire
  targeting system's per-round arrival-time solve — fixed by dropping the Y blend entirely,
  X-only now. (2) the X blend had no cap, so fire teams aimed at genuinely different real
  targets (by design, see `onAimRelease`) got visibly pulled together then released near
  impact — fixed with an absolute pull cap (`CONVERGE_MAX_PULL_X`). (3) the model's rotation
  always uses true velocity (never the blended position) so its facing never lies, but that
  left the blend's own sideways motion unaccounted for on fast, thin bullet tracers — fixed by
  skipping the position blend entirely for `ProjectileType.Bullet`. Confirmed better on-device
  on both L1 and L24 (2026-07-22); zoom is "improved but not 100% there" per direct user
  feedback — likely a Phase 4/6 concern, not yet root-caused.

## Where to look

- `/home/rob/.claude/plans/rosy-skipping-stardust.md` — the approved rearchitecture plan
  (context, inventory, architecture, branch process, verification).
- `L24_VOLLEY_HIT_CAMERA_PLAN.md` — historical; superseded by this doc and the plan above.
  Kept for the incident history (§11/§12 in particular document the exact failure shape this
  doc's principle 3 is designed to prevent from recurring).
- Branch `camera-rearchitecture-v2`, commit `936dc70` — snapshot of the pre-redesign system
  as it stood at the end of the 2026-07-22 session, kept as reference/comparison material.

## Aim input and overlay projection (2026-07-28)

Two fixes here that are *not* camera-behaviour changes — the camera path, `HORIZON_FRACTION`,
`CAMERA_LOOK_AT_Y` and the trajectory are all untouched — but they live in the same files and
were both found by the user in play, so they belong in this doc.

**Drag-to-power is now a fraction of viewport WIDTH, not a fixed pixel count.**
`AimOverlay.PIXELS_PER_UNIT` was a flat 50 px per world unit. Full power needs
`|velocity| = MAX_AIM_MAGNITUDE` (14) and `velocityFromDrag` scales by
`PROJECTILE_SPEED_SCALE * 60` (0.6), so it takes 23.3 world units of drag = **1167 px**, about
825 px on EACH axis for a realistic diagonal pull. Landscape (2404 px wide) swallows that;
portrait (1080) does not, so 100% power was unreachable — reported as "can't seem to get full
power when in portrait mode". The scale is now `viewportWidth * 0.0208`, read from the overlay's
own layout size so it follows a rotation with no orientation branch. Width rather than height or
diagonal because the diagonal is identical in both orientations and the narrow axis binds;
0.0208 reproduces exactly 50 px at 2404 wide, so landscape is unchanged by construction.
Measured after: landscape 825 px/axis → 97%, portrait 371 px/axis → 95% (was ~45%).

**Every 2D overlay must include the camera-distance term.** The cannon badge projected with
`hPx * (HORIZON_FRACTION - 0.5) / CAMERA_LOOK_AT_Y` and no `CAMERA_Z_REF / actualCameraZ`
factor, which the impact overlay ten lines below has always had. It therefore projected at a
fixed ~120 px/world while the scene rendered at ~213, putting the badge ~170 px away from the
tank it belongs to. This is the same drift the impact overlay's own comment warns about,
surviving in the one overlay never fixed — assume any *other* 2D overlay added later has the
same bug until checked. It was invisible while the badge floated in empty sky above the tank
and became obvious the moment it was moved down beside a solid object.

Also: position overlays by MEASURING them (`onSizeChanged`), never by a constant nudge. The
badge used `sx - 60f` as a guess at half its own width, which cannot be right for a thing whose
width changes with `tankShellsRemaining` and with the ARMED/HOLD label.

## staticCamera is a zoom ceiling, not a lock (2026-07-30)

`LevelDefinition.staticCamera` (L12 is its only user) used to do two things: clamp camera Z into
`[staticCamZ * STATIC_CAMERA_ZOOM_IN_FRACTION, staticCamZ]`, and pin camera X to a fixed
`staticCamX` at the battlefield midpoint. The X pin is gone. Only the Z ceiling remains.

**Why the pin was wrong.** A non-null `cameraFollowX` always wins over SceneHost's per-phase
Animatable (see `currentCameraX`), so pinning it disabled the whole choreography this document
describes — PlayerScout's sweep to the enemy and Aiming's snap back to the player line never ran.
The camera sat at the midpoint for the entire battle.

That alone would only have been dull. What made it a bug is that **camera Z kept sizing each
phase from its subject's own span while X stayed at the midpoint** — the frame was the right size
centred in the wrong place. The general rule, which is worth stating because it will recur: *a
half-width only frames its subject about the centre the camera actually uses.* Measured on L12:

| phase | subject | frame it got | result |
|---|---|---|---|
| PlayerScout | enemy cluster, centred ~+4.8 | centred −1.25 | dominant structure cropped off the right edge |
| Aiming | player line, −10.5..−5.2 | x −5.1..+2.6 | empty snow — contained none of the player line |

Aiming was the worse of the two and had never been noticed, because a frame of plausible-looking
battlefield reads as intentional in a way a cropped structure does not.

**Nothing was needed in exchange.** The anti-swing containment the pin appeared to provide already
exists at the *target* level, which is the only correct place for it (clamping a `SpringFollow`'s
output leaves its velocity fighting the clamp — see the Z clamp's own comment): the volley follow
coerces its target into `[player min x, max enemy/structure x]`, and the windup escort targets a
mean of live enemies. Both are inside the battlefield by construction.

**Know that the surviving ceiling is inert at L12's geometry.** Its band is `[5.27, 26.34]`, while
`gameplayCamZ` is already coerced to `[CAMERA_Z_MIN 5.5, CAMERA_GAMEPLAY_Z 22]` and the bullet cam
to `[6, 14]`. Floor below the floor, ceiling above the ceiling. The flag only begins to do
anything on a level whose whole battlefield is *tighter* than `CAMERA_GAMEPLAY_Z`. Keep it for
intent, but if a framing swing ever shows up on L12, this is not what will stop it.
