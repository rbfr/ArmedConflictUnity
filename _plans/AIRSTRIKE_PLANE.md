# Plan — the airstrike gets an aircraft, and its own beat

**Asked by Rob 2026-08-10, in two decisions:** the airstrike should show *"something fly across the
screen from the player's side and strafe the enemy"*, and **"plane should fly first before the
player volley"**.

The second decision is the one that matters, and it is not a presentation choice — it is what makes
the first one possible at all.

## Why the beat has to come first (measured, not assumed)

The 30 fps device capture on 2026-08-10 (`_plans/BACKLOG.md`, "the AIRSTRIKE has no author") showed
the bomb **detonating off-screen**: it lands on a fixed 1.4s fall while the volley-follow camera is
still mid-pan chasing eleven infantry arcs, and arrives ~0.85s before the camera does. A plane
crossing during that same window would be missed for exactly the same reason.

So the plane cannot share the frame with the volley. Flying it FIRST means nothing is in the air to
chase, the camera is free, and the pass owns the screen.

## The sequence

```
release (airstrike armed)
  -> TurnPhase.AirstrikeRun
     camera holds on the drop point, biased LEFT so the run enters frame
     plane enters from the player's side, crosses at 7 u/s
     at the drop lead it RELEASES the bomb (inherits forward speed, so it arcs)
     bomb lands: standard blast, scorch, 24 damage / 1.1 splash / 2x structures - UNCHANGED
  -> TurnPhase.Resolving
     the infantry volley launches, plane still exiting frame
```

**No fake strafing.** The plane drops its bomb and that is all it does. Gun flashes that deal no
damage would be the same mistake as a wind that telegraphs a change the player cannot feel — this
repo has already made that call twice. If the airstrike should strafe, that is a MECHANIC change
(damage spread along a line) and a separate ask.

## The numbers, and where each comes from

| | | |
|---|---|---|
| `PlaneSpeed` | 7.0 u/s | 14 units of travel = a 2.0s pass. Slower reads as sluggish, faster and the eye cannot follow a 4.5-unit object across a ~10-unit-wide frame |
| `BombFallTime` | 0.85s | The drop lead is `speed * fall` = 5.95 units, and the frame is only ~4.94 half-width at camZ 11. Longer and the release happens off-frame, which is the bug being fixed |
| camera bias | -1.5 units | Leads the subject: release lands at -4.45 (inside), impact at +1.5 (inside). Both in one frame is the whole point |
| spawn / despawn | target.x -+ 9.0 | Off-frame both ends, so it enters and leaves rather than popping |

**The bomb keeps its damage exactly.** This is a presentation and sequencing change; nothing about
what the airstrike DOES to the enemy moves, so no level's balance is touched.

## What it costs

- `TurnPhase.AirstrikeRun` — a fourth phase, and the first one that is not a turn HANDOVER.
- `GameState.AirstrikePlane` + `PendingVolleyAim` — the aim has to survive the run, because the
  volley is built from it a beat later.
- **A camera beat.** `CAMERA_ARCHITECTURE.md` is LOCKED; this is the ask being answered, and it is
  deliberately the smallest possible change — the existing anchor spring with a new target, not a
  new follow mode. No new camera behaviour, one new destination.
- **A scene rebuild**, because `BattleRunner` gains a serialized `planePrefab`.

## Steps

- [x] `build_attack_plane.py` — straight-wing attack aircraft, 500 tris, 4.47 x 4.31 x 1.13
- [x] `PlanePreview.Shots` — judged at gameplay framing; found the model must be BANKED ~45 deg or
      the span (which runs along DEPTH) projects vertically and reads as a cross-shaped blob
- [x] `AirstrikePlaneEntity` + state fields + `TurnPhase.AirstrikeRun`
- [x] `BattleTick`: split `FireVolley` so the volley can be launched a beat late; `BeginAirstrikeRun`;
      `StepAirstrikeRun`; the camera anchor for the new phase
- [x] `BattleRunner`: render the plane, banked, facing its travel
- [x] `PortSelfTest`: 582 checks, six new, each seen to FAIL first
- [x] Device: verified end to end on L1, 2026-08-10

## DONE — and the two bugs only the device found

Both were invisible to a green test suite, and both are now checked.

**1. The run took the AIMING framing and clipped the aircraft off the top of the frame.**
`TurnPhase.AirstrikeRun` fell through `CameraDirector.PhaseHalfWidth`'s `default:`, which returns
the player half-width — the tightest camera in the game, camZ 9.3. A 4.5-unit aeroplane banked 45
degrees does not fit there. Fixed with an explicit case and a floor (`AirstrikeRunHalfWidth` 5.1 ->
camZ 14), which can only ever pull the camera BACK: a wider enemy cluster still gets its own
framing. The check asserts camera DISTANCE, not that the switch has a case.

**2. The aircraft FROZE at handover and hung in the sky for the rest of the battle.** Its motion
lived inside the run's own step, which stops being called the instant the phase changes — so the
plane stopped dead in mid-air and then ballooned as the camera zoomed past it. This is the
"anything that decays must decay on EVERY tick path" rule wearing a new costume: the aircraft
OUTLIVES the phase that launched it, deliberately, because it exits over the top of the volley.
Motion moved to the physics section, and the despawn point is carried ON the entity (`ExitX`) so it
needs nothing the phase owns. The check asserts movement and then absence.

## Measured on device, L1, 2026-08-10

```
[Consumable] Airstrike armed=True
[Consumable] Airstrike fired
[Battle] airstrike run, volley held at 86% / 45.0deg
[Battle] volley: 11 rounds, after the airstrike        <- 1.10s later
```

The beat costs **1.10 seconds** before the volley — less than the 2.5s budgeted above, because the
handover fires the moment the bomb lands rather than waiting for the aircraft to leave. On the
frames: the plane crosses fully in shot, releases, the bomb hits the bunker deck, the plane exits,
the sky clears, and only then do the volley's rounds arc in. Outpost 90 -> 80 and the bunker comes
down on the volley, so nothing about the damage moved.

**The release log was corrected too.** It reported `volley: 0 rounds`, which was a lie told to the
one instrument a release build has — the volley had not been built yet. It now says
`airstrike run, volley held`, and the volley logs itself when it launches.
