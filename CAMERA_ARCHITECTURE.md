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

## AirstrikeRun rides the aircraft — asked 2026-08-17

The 2026-08-10 cut (hold on the drop, plane crosses a still frame) is **withdrawn**.
Rob: the plane should come in from the left where the player units are, fly
across, strafe, go off screen; the camera moves with it, then goes back to
the player units — it is their turn to fire.

**Why the cut existed:** the camera target was the drop point, ~17 units right
of the player line. The spring raced the plane and overtook it, so the
aircraft appeared mid-frame. That target was the bug, not the spring.

**What it does now:**
1. Spawn LEFT of the player line. Camera is already there.
2. Target = `plane.X + PlaneCameraBias`. The spring RIDES the aircraft.
3. Plane exits. Target snaps back to the player line.
4. After `AirstrikeReturnSeconds` the infantry volley fires.

No cut. The default spring stays. The bomb still drops on the aim during
the pass; the guns still rake the enemy position. Only the camera and the
volley timing moved.

## Zoom in on the leftover and the charge (Unity, 2026-08-13)

Asked: the riot shield / armour is unreadable because the camera sits at fortress
distance. Not a new marker. Three discrete recaptures, never a live span:

0. **TankArrive (2026-08-14).** After BEGIN, a level that fields a player cannon
   holds the camera on the union of the ground line, the tank, and the crew while
   the vehicle rolls in from the left. Two seconds, cubic ease, then the signed-off
   scout. Without this beat the roll is off-camera: scout looks at the enemy, aiming
   excludes the tank. Not a new cut — the existing spring walks onto that union.
1. **Aiming frames the ground line**, not the tank crew. Rule 1.
2. **Enemy half-width recaptures when a structure leaves or a boss/wave lands.** Casualties
   do not — that membership twitch is the class of bug this document exists to prevent.
   The announcement (2.5s) then frames the arrived group tighter still.
3. **A march frames the chargers** until they are within 5 of the player line. **A fight
   still frames the whole player force** — that union was signed off on L4 and stays.

No new cut. The existing spring walks between these targets.

## The contact floor became a spring margin (2026-08-21)

Asked, on L4: *"we're zooming out way too much here."* The contact frame
kept its signed-off UNION — the whole player force plus the fight, that
did not change — but it was floored at `ContactHalfWidthMin = 4f`, and
that 4 was never geometry. On L4 the engagement wants **±2.36** (force
−9.59..−5.42, fight −4.87) and the camera showed **±4.00**: 70% more air
than the fight needed, on every contact shot in the game.

What the 4 actually paid for was SPRING LAG. The camera is smoothed, so
at the moment a fight starts it trails its anchor — measured at ~0.55 on
L4 (cam −6.68 against anchor −7.23) — and a floor was the crude way to
keep the tank crew from falling off the left edge while it caught up.
A fixed lag needs a fixed ADDITION, not a minimum: a floor over-pays on a
small engagement and would be swallowed whole by a large one.

So the floor drops to 2.5 (matching march) and the union carries
`ContactSpringMargin = 0.7` instead. L4's contact frame goes **±4.00 →
±3.06**, the live camera ±3.52, with the tank rear still contained by
0.61. Verified on device: the tank sits at the left edge of the contact
shot with no dead space beyond it.

**Containment alone could never have caught this.** The two existing
checks ask whether the frame HOLDS the force, which any big enough frame
satisfies — which is how ±4.00 stood on a ±2.36 engagement without a
single test going red. There is now a ceiling as well as a floor, stated
as union + margin so it cannot drift into another magic constant.

## FramePad 1.2 → 0.6 (Unity, 2026-08-14)

Asked: zoom in a little, camera feels far, hard to see. This is the air
around every framed set, not who is in the set. `CameraDirector.FramePad`
was the `+ 1.2` on every `TargetZ` call. 0.6 keeps the same subjects and
the airstrike still clears camZ 11. Composition rule 1's player line is
still ~6 wide.
