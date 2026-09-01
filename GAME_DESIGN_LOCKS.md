# GAME_DESIGN_LOCKS.md — ArmedConflict

**Locked decisions — do not change without explicit approval.**

## Core Loop & Turn Structure
- Strict alternating "I go, you go" turns.
- One shared drag aims for the player's entire roster. Preview stays the short direction
  hint + angle%/power% readout ONLY — no landing marker, no arc-to-ground, no camera pan to
  the aim point. After the finger comes up the same two numbers stay on screen as
  `Last:` through the rest of the turn and the enemy's — Kotlin did this and the
  port dropped it. Guessing angle/power to deliver the volley to the right place IS the
  mechanic; this was tried (landing marker + aim-pan) and explicitly reverted. CONVERGENT
  FIRE (revised 2026-07): the drag defines a target ZONE — the reference arc (formation
  centroid, the aim hint's arc) is solved to its landing point and flight time; every shot
  solves its own velocity to land on an ACTUAL living enemy unit or enemy-side structure
  within `zoneHalfWidth` of that point (round-robin across in-reach targets + small jitter),
  arriving within ±0.15s of the reference (a ragged saturating salvo, not a synchronized
  pinpoint). `zoneHalfWidth` scales with the LIVE enemy formation's span each volley
  (`VOLLEY_ZONE_MIN/MAX_HALF_WIDTH`, floor 1.0 / ceiling 3.5) instead of a fixed 1.0 — a
  fixed width only ever saturated a slice of a wide late-game line. Targets are real units/
  structures, not a blind uniform fan across the raw span: real formations have GAPS between
  clusters, and fanning shots evenly across the full span put a chunk of every volley into
  those gaps (shots that visibly reached the enemy's general area and did nothing — read as
  "shots pass through"). Very flat drags (<5°) keep the legacy shared velocity (flat shots —
  heli sniping — aim by feel).
- The tank/cannon SHELL targets the same real in-reach target as the small-arms fire
  (nearest actual enemy/structure to the raw landing point), not the raw landing point
  itself — it used to beeline for the bare guess point while everything else snapped to a
  real target, so the shell visibly diverged from the rest of the volley (read as "the tank
  round is way ahead, hard to tell what to aim at"). Keeps its zero jitter — still the most
  precise round in the volley, just precise onto a real target.
- The shell is ARMED OR HELD, and the player decides (2026-08-24, Rob's ask: *"the player can
  choose whether to arm the shell or not"* — restoring the Kotlin/Compose behaviour). The tank
  carries **5** shells (`PlayerTank.cannon.ammoPerBattle`), and a level may override that on the
  placement (`StructurePlacement.shellsOverride`) without minting a second tank asset. L4 is 3:
  five dropped both buildings by turn 4 with two left (08-25 play); four would still raze both
  before contact. Three is 288 against 240 garrisoned HP — the magazine is the whole demolition
  budget again.
  - WHY IT MATTERS MORE THAN IT LOOKS: the shells are the squad's ENTIRE demolition budget. A
    rifleman does 8 x 0.25 = **2** damage to a wall, so the cannon is the only thing that brings a
    garrisoned structure down in a sane number of turns. While the shell fired itself on every
    volley, a player who spent their opening turns shooting at an advancing charge — which is
    exactly what L4's `levelGoal` tells them to do — put the whole budget into the dirt and
    reached a state no skill could win, unwarned. Found by PLAYING L4 on 2026-08-24: the run that
    chased the charge died on turn 9 having killed 5 of 27; the run that razed the buildings
    reached 1 v 1.
  - **DEFAULT IS ARMED. Signed 2026-08-25.** Held-by-default kills the L4 trap outright but
    lets a player who never notices the panel reach the same dead end by a quieter road. Armed is
    what the game did before the switch, so no level's measured balance moves on the default
    alone. The panel exists to teach the hold. Do not flip this as taste.
  - The choice PERSISTS across turns. Arming consumes nothing, so re-arming every volley would be
    a tax on a player who wants to shell straight through; what the HUD owes in exchange is that
    the state is unmissable, which is the magazine panel's job.
  - The panel is PINNED UNDER THE TANK and lives only during the player's aim (Rob, same day:
    *"should not follow across the screen... it should be underneath the tank as well"*). It is
    gone the instant the volley leaves — the decision has been taken by then. Reads `ARMED` /
    `NOT ARMED`, his wording.
  - **A living operator has to be on the hull.** Rob, 2026-08-28: *"one guy in the tank as
    the operator... if he dies, the tank can't fire the shell."* The shell is his gun. If he
    is gone the panel reads `NO GUNNER`, the magazine is not spent, and `CannonArmed` cannot
    fire a round. He is level geometry (loadout never touches him). L1 fields one; other
    tanks may still carry two until asked.
- ONE ROUND PER UNIT (2026-07-25 — REPLACES the 2026-07 "fire teams" lock). Every firing
  unit puts its own projectile in the air: one round, one entity, one visible model, one
  unit's damage. Nothing cosmetic stands in for a round that isn't simulated, and nothing
  simulated goes unrendered.
  - The old lock merged bullet-firing units into teams of 3 (one projectile carrying
    `volume = teamSize`, split back into separate hits at impact, with two fake "echo"
    tracers drawn so the volley still looked like a squad). It was justified on perf, but
    measured against real level data it cost MORE than it saved: 72 permanent render nodes
    (24 slots × 1 real + 2 echoes) against 48 for one-round-per-unit, and ~48 rounds vs ~30
    opposing units is only ~1,400 swept-segment tests a tick. The L18 stutter it was built
    for was GC from unbounded registries and per-frame node callbacks — fixed separately by
    the bounded pools — not projectile count.
  - What the merge really bought was damage CONCENTRATION, and that is still LOCKED: fire
    must pile onto one target until it drops rather than chip every enemy for a fraction of
    a kill (that dilution reads as "rounds slam into the line and nobody falls"). It now
    lives in the TARGETING layer — `ROUNDS_PER_TARGET` consecutive shooters are assigned the
    same aim point (`onAimRelease`), reproducing the old distribution. Spill is emergent:
    rounds aimed at the same spot hit whoever is nearest once the point man drops.
  - Verified on-device 2026-07-25: L5 volley 18 → 14 enemies in one turn; L25 (worst-case
    36-round enemy volley) with no frame drops.
- Enemy mirrors with a full-roster volley, one round per unit. Each enemy unit independently targets a random surviving player unit — enemy fire converges by construction (every unit solves toward the same shared target), so it needs no separate concentration pass.
- Enemy volley is computed once at the start of `EnemyWindup` and held for `ENEMY_WINDUP_SECONDS` (visible wind-up animation).
- A turn only ends once every projectile from the volley has hit or expired.

## AI Targeting
- `EnemyAI.aimAt`: Chooses randomized launch angle within `MIN_LAUNCH_ANGLE_DEGREES..MAX_LAUNCH_ANGLE_DEGREES` (lobbed arc).
- Solves for velocity that lands on a jittered target point at that angle (not the flattest direct trajectory).
- Accuracy controlled by jitter radius, not angle.

## Advancing Assault & Melee
- Enemy groups with `advancePerTurn > 0` march toward the player line during EnemyWindup
  (the phase where the camera watches the enemy side), consuming a per-turn budget.
- Advancers close to `ADVANCE_STOP_GAP` (arm's length) of the front-most GROUND unit.
- Units with `meleeDamage > 0` (Shield Bearer) are PURE melee — they never fire ranged
  shots, not even while closing. When one reaches the front line (within `MELEE_RANGE`
  as the enemy volley fires) it CLAIMS a unique soldier and locks into a SKIRMISH
  (`SkirmishEntity`): it lunges to grapple distance, two blood-beat blows land (shove +
  spray + wound scream), and after ~1s BOTH fall as mutual kills. One fighter through =
  one soldier lost, guaranteed — legible maths; the counter-play is killing the charge
  before it arrives. The turn handover waits for skirmishes to finish; a partner killed
  mid-scuffle by stray fire calls the fight off. Structure garrisons (tank deck,
  fortress tiers) cannot be meleed — once no ground units remain, advancers HOLD
  position rather than pursuing garrisons they can't touch.
- During EnemyWindup on assault levels the camera RIDES WITH the melee force while it
  closes, then pans BACK to the enemy line for the moment the shooters fire (volley-follow
  takes over from there)
  (tick-driven follow, like the volley camera) so the charge and the skirmish kills
  play out on screen instead of off-camera left. The volley-follow's tracked mean EXCLUDES
  the helicopter's door-gunner bullets (`ProjectileEntity.isHeliShot`) — the gun run crosses
  the field on its own independent schedule (sometimes overlapping the ground volley's
  Resolving phase) and must never pull the camera off the ground volley's path.
- Melee is one-directional (enemy → player). Player units don't fight back hand-to-hand;
  the counter-play is killing the advancers before they arrive.

## Helicopter Hover Round (revised 2026-07: the gun run is now counterable)
- Helicopters appear only on DESIGNATED heli levels (`LevelDefinition.heliChance` > 0 —
  currently L4/L7 at 0.3, L10 at 0.4), a spaced escalation rather than a fixture of every
  level past 3. On that chance per enemy turn (one at a time), an enemy gunship enters high off the
  enemy edge as the ground volley winds up and HOVERS just BEHIND the enemy's rear-most
  unit/structure (HELI_ALTITUDE). NOT over no-man's-land: a hover point in the firing lane
  intercepted every volley aimed at the ground troops, forcing the player to engage it.
- It HOLDS there through the player's next turn. While Entering/Hovering/GunRunning it is a
  physical target: player projectiles within HELI_HIT_RADIUS are consumed and damage its
  HELI_MAX_HP pool (spark flash + bullet-snap audio per hit; HP shown in the HUD).
  Killing it is an OPT-IN deliberate deep shot — normal volleys at the enemy line land
  before ever reaching it, so aiming is unaffected by its presence.
- No special camera: the heli lives where the normal choreography already looks. (An
  earlier midfield-hover design needed a dedicated wide-shot camera mode; it read as
  zoomed-out and awkward and made the aim arc illegible.)
- hp 0 → Crashing: gravity fall with tumble, ground-contact fireball, despawn. The crash is
  COSMETIC (no damage to anything) — consistent with the ragdoll rule.
- If it survives the player's turn UNTOUCHED, the next enemy volley flips it to its GUN
  RUN: crosses right-to-left, door gunner fires bursts of ORDINARY MG bullets at the player
  line (standard damage/wound/kill pipeline). The turn handover waits for the gunner's
  bursts.
- If it survives WOUNDED (any hp lost), it trails engine smoke and RETREATS off the enemy
  edge without firing instead — partial damage still cancels the gun run. It stays
  shootable while fleeing.
- It never fires outside enemy phases; its exit fly-off is cosmetic. If the battle ends
  while it hovers, it exits without firing.
- Heli-capable levels TELEGRAPH it: an unarmed flyby crosses the field behind the level
  preview card (Preview mode — not shootable), so the player knows the threat before Begin.

## Win / Loss
- Purely roster-based.
- Victory: `enemyUnits` empty while player still has units.
- Defeat: `playerUnits` empty.
- Outpost/structures are scoring/damage targets only — never win conditions.
- **Reinforcements** (once per battle): if a turn hands back to the player with the roster
  under 25% of total fielded units, a relief squad of riflemen (25% of the starting roster)
  enters from the player's edge and runs the length of the line to take over the FRONT
  rank — fresh bodies absorb the melee threat while the survivors become the rear (`marchTargetX`).
  They count into the star denominator (`initialPlayerCount` = total FIELDED), so a clean
  no-reinforcement win stays the premium result. Arrives only if someone is left to rescue —
  a wipe during the enemy volley is still Defeat.

## Structures & Units
- Collision resolves UNITS BEFORE STRUCTURES: a projectile checks for a direct unit hit
  first, and only blocks on a structure if no unit was struck. A structure's hitbox is a
  full-width AABB reaching ground level, so resolving structures first "shielded" any
  ground unit standing inside a wide structure's footprint (advancers crossing a fortress
  base, a garrisoned barracks' front rank) — their round was silently spent as a wall chip
  instead of damaging them.
- Structures can carry units via `EnemyGroup.standingOnStructureId`.
- Units on structures render at `STRUCTURE_TOP_Y`.
- Units standing on a structure die instantly when it is destroyed ("fall and die"), regardless of HP.
- Stacked structures (`restsOn`) collapse WITH their supporter, transitively: destroy the
  bottom tier and the whole stack comes down in ruins, garrisons falling to their deaths.
- **Armor HP model**: units take multiple hits scaled by equipment — sniper (no armor) dies to
  any direct hit, rifleman takes 4 rifle rounds, heavy (plate) takes 8 (HP roughly doubled from
  the original pass — see UnitDefinition.kt comment — so a single volley plus the tank's splash
  shell can't wipe a small roster outright; fights run several volleys of real attrition).
  Structure HP was raised alongside it by a similar factor. Damage accumulates across turns.
  Units do NOT change color with damage; damage feedback lives on structures (walls blacken in
  3 stages as HP drops, crack decals stamp at every hit point, plus chunk loss → charred
  shell on destruction).

## Physics & World
- `WORLD_FLOOR_Y = 0f`.
- Missed shots disappear instantly when they reach y = 0.
- **All physics runs in the GameViewModel tick on immutable state** — the renderer only
  displays positions. SceneView's `PhysicsNode`/`PhysicsBody` (evaluated at 4.18.0) is NOT
  used: it is a position-only Euler integrator (no angular velocity, no tumble, no
  impulses — `mass` is a deprecated no-op) and it drives `node.position` directly on the
  render thread, which conflicts with the state-driven zero-disposal node registry and the
  per-frame `enforceTransform()` corruption repair.
- **Ragdolls are cosmetic**: no collision with units or projectiles and they cannot be re-hit —
  but as of 2026-07-28 they DO stop at structure walls (`ragdollBlockedX`), horizontally and
  only below the roof line, so a body thrown over a structure still sails across and a garrison
  dying on its own deck stays free to fall off the edge. Nothing in the ragdoll consulted the
  structures before that; it was invisible while bodies barely travelled and obvious once they
  were thrown far enough to reach a wall. Death spawns a `DyingUnitEntity` whose impulse follows the KILLING BLOW: bullets
  and melee on the DIRT SKID BACKWARDS and flop (no hop — that bounced; signed
  2026-08-28 *"ok this is fine."* Kenney `die` stays off). Explosive blasts THROW
  (modest launch, apex ~half a body height, moderate tumble). Deck and
  structure-collapse deaths keep the roof throw. No universal arcade pop — that
  read as cartoonish.
- Ragdoll pipeline: gravity 9.8 + light drag in flight → dead-weight ground thud
  (restitution 0.16, most spin/horizontal speed absorbed) → brief heavy-friction roll with
  rotation locked to distance traveled → critically-damped angular spring flops the body to
  the nearest lying pose (never snaps) → **sleep** (integration skipped once settled,
  mirroring `PhysicsBody.isAsleep`) → culled at `RAGDOLL_MAX_AGE_SECONDS` (5s — longer
  lifetimes keep the dyingUnits list long, and every unit slot scans it per recomposition,
  which read as sluggishness in heavy battles). Ground
  contact uses a rotation-aware rest height (lowest corner of the rotated body silhouette)
  so no part of a tumbling body ever clips below the ground plane.
- Ragdoll TUNING (2026-07-28, user-directed): bodies throw about twice as far and keep their
  momentum through the landing into a real roll. Roll turns at 150 deg per world unit — BELOW
  rolling-without-slipping (~230 for this body size), so a limp body slides and drags rather
  than spinning like a wheel, which is what "balled up" was. The split upper body also TRAILS
  the direction of travel in proportion to speed, decaying to nothing as friction stops it, so
  limpness comes from the body's own motion rather than a canned pose.
- **The convergent player volley solves by ANGLE, not by shared flight time** (2026-07-28,
  `TrajectoryPhysics.velocityAtAngle`). Every round leaves at exactly the angle the player
  dragged and still lands on its target; speed is solved per round. The previous shared-time
  solve set each round's horizontal component to `(target.x - muzzle.x) / T`, a correction
  scaling as 1/T — and T shrinks with draw power, so the launch angle diverged further the
  WEAKER the shot: +4.8 degrees at 90% power, +14.6 at 55%, +24.1 at 45%, +46.2 at 35%, by
  which point a front-rank soldier fired almost straight up. Cost is a 0.15-0.32s spread in
  arrival times; TrajectoryPhysics' docstring records that shared-angle was rejected once
  before for stringing volleys "into waves", so that is the first thing to revisit if waves
  reappear. Degenerate geometry (target too high for the angle, target behind the muzzle,
  angle near vertical) falls back to the shared-time solve.
- The battlefield ground is a PAINTED 2D backdrop behind a transparent SceneView — there is
  no ground mesh. 3D geometry below y=0 renders: nothing occludes it, so it is DRAWN OVER the
  painted ground rather than clipped by it. Below-grade geometry can exploit this to read as
  ground genuinely removed (the trench prop does, see `build_war_props.py`), but only while it
  stays shallow — the illusion is doing all the work. Blast craters used to be the headline case
  and were REMOVED 2026-07-27: their per-hit Y "dig" scale inflated the whole model until the
  rim stood proud of the ground line and the sunken cone read as a black spike, worst on the
  bright daylight biomes. Ground marks are now flat scorch decals only. If you add below-grade
  geometry, cap how far it descends and never scale it per-event.
- Ragdolls carry no z impulse, but the tick GLIDES every dying body onto the z=0 plane:
  the painted horizon is exactly where z=0 meets y=0, so a body resting at a back
  formation row (z<0) hovers above the ground line and a front-row one (z>0) sinks into
  the foreground dirt. Anchoring to the line (plus a rest height matched to the model's
  real thickness) is what makes corpses read as lying ON the ground.

## Aim Mechanic (Current + Planned)
- Current: Drag-and-release. Horizontal direction locked rightward. Preview = short direction hint (`PREVIEW_HINT_SAMPLES`) + angle%/strength% readout.
- Planned (next major iteration): On-screen joystick in upper-right quadrant. Stick angle drives weapon rotation directly. Stick displacement = power. Release to fire.

## Campaign Structure: Stages & Stars
- The campaign is STAGES of ~6-7 levels (`StageDefinition`), not one flat level list. Each
  stage is a theme + ONE signature mechanic: introduced gently in its early levels,
  escalated through the middle, combined with earlier stages' mechanics near the end; the
  final level is the stage's boss/climax. Current: Stage 1 "Valley Front" (L1-6),
  Stage 2 "Enemy Stronghold" (L7-12); target shape is 7 levels with a purpose-built boss.
- Star results per victory, from roster survival: >=75% alive = 3 stars, >=40% = 2, any
  win = 1. Thresholds stay LEGIBLE ("lose a quarter / lose half") — the replay loop is
  chasing a cleaner win, not decoding a formula. Best result per level persists
  (`ProgressStore`, SharedPreferences).
- Stages unlock by TOTAL stars, not "beat everything" (Stage 2 = 8 of Stage 1's 18): the
  skilled player rushes ahead, the stuck player replays earlier levels for a better star
  result instead of hitting a wall. Locked stages stay BROWSABLE — name, tagline and
  preview visible (the "visible horizon"), only Begin is gated.
- `StageDefinition.unlockRewardId` reserves the stage-completion reward hook for the
  planned Roster/loadout system (below) — completing a stage will unlock units/upgrades,
  the between-sessions strategic layer.

## Army & Scale
- Pre-battle loadout from unlocked `Roster` (not yet implemented — currently hardcoded in `GameViewModel.buildInitialState()`).
- 7–30 units per side. Compact `Formation.grid`-family layout (`Formation.kt` now also has
  `clustered()`/`heroes()` variants — see `UNIT_VARIETY_DESIGN.md`, in-progress/paused). Keep
  new content at this scale.

## Camera & Presentation
- Fixed side-on view with slight 3D parallax/tilt. No free-roam.
- **LOCKED as of 2026-07-24** — see `CAMERA_ARCHITECTURE.md`. Do not modify camera or aim
  (`SpringFollow`, `cameraFollowX/Z`, `AimOverlay`) behavior without an explicit ask.
  2026-08-18 ask: after a garrisoned structure falls, the camera rides the
  falling bodies for 1.25s then pans back to the remaining enemy line
  (`CameraDirector.CollapseFollowSeconds`).
- **Rifle tracers are an un-tapered flat orange dash.** Opaque unlit, camera-facing,
  no tail. Signed 2026-08-28: mid-90s arcade, not realistic. A teardrop read as a
  rocket; Kenney Particle Pack streaks read as VFX, not rounds. Do not re-open as a
  mesh or sprite pass without an ask. Rockets, grenades, and the tank shell keep
  their own meshes.

## V1 Scope
- Single-player campaign vs AI only. No networking or PvP.

**These are hard constraints.** Always respect them in any implementation or suggestion.
