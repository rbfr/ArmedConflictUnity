# `_plans/BACKLOG.md` — asked for, not yet scheduled

Two kinds of thing, both parked and neither started:

- **Ideas Rob has asked to keep**, with enough context that whoever picks one up does not have to
  re-derive why.
- **`UNRESOLVED:` items** — questions and defects found during other work and deliberately left
  open rather than quietly dropped. Each says what is actually known, what is only a hypothesis,
  and what would settle it. An open question with its reasoning recorded is worth more than a
  confident guess.

**Not sequenced.** When one is scheduled it gets its own plan file per `_plans/README.md`; when it
ships, its conclusions move to the relevant design doc.

Nothing here overrides `GAME_DESIGN_LOCKS.md` or `CAMERA_ARCHITECTURE.md`.

---

## UNRESOLVED: is Cluster's 3.2x spread too wide to connect? — open since 2026-08-07

**A balance question, and only a human playing it can answer.** It has been open since Tier 1.1
shipped and is the last thing that tier owes.

Cluster trades concentration for coverage: `spreadScale` 3.2x on the per-shooter jitter,
`unitDamageScale` 0.65x. The intent (`DYNAMISM_DESIGN.md` Phase A) is "more men hit, each one
lighter". The risk is that at 3.2x the volley stops overlapping any single target enough to kill
anything, and a 500-coin purchase reads as strictly worse than Standard.

**Why no test settles it.** `AmmoTest`-style checks can only assert the multipliers are applied —
and this repo has already been burned by exactly that (AP asserted `structureDamageScale == 2`
and passed while the real per-round effect was 1.2x). The question is not "is 3.2 applied", it is
"does a 3.2x volley connect". That is a felt property of a real drag against a real formation.

**And `Auto` cannot answer it either**, for two independent reasons now documented in `CLAUDE.md`:
it fires STANDARD rounds whatever is selected, so it never fires Cluster at all; and it targets
each unit's nearest enemy with no jitter, which is the opposite of the spread being tested.

**How to actually judge it:** buy Cluster, play three or four levels with a mixed enemy line and a
garrison, and compare against the same levels with Standard. The number to watch is not damage
dealt — it is whether ANY single enemy dies to one volley. If Cluster only ever wounds, the spread
is too wide and the honest fix is to lower `spreadScale`, not to raise the damage.

---

## UNRESOLVED: flames outlive their bodies by a frame or two — found 2026-08-10

At the moment the incendiary burn KILLS a garrison, a contact sheet shows one sampled frame in
which two flames stand on the bunker deck **with no bodies under them**, before the corpses appear
in flight with their flames still attached.

**The current explanation is a hypothesis, not a diagnosis.** Best reading is the already-recorded
"a unit's slot is not stable across frames" corpse handover: the unit leaves `EnemyUnits` and
becomes a `DyingUnitEntity` in the same tick, and the renderer may take a frame to place the
ragdoll while the flame — which is driven from the ENTITY and follows `DyingUnits` by design — is
already drawn at the death position. That would make the flame correct and the BODY late.

**It was seen on one recording sampled at 12 fps**, so even "a frame or two" is an estimate. It
could be a single frame.

Worth knowing before anyone spends time on it:

- **The flame following the corpse is deliberate and should stay.** A man the fire finishes leaves
  `EnemyUnits` on the frame he dies, and drawing only the living would snuff his flame at the exact
  moment it does the most work. The fix here is about the BODY arriving late, not the fire.
- **Prefer a PROBE to another recording.** One build logging the dying unit's render position and
  the flame's on the same frames settles in one run what frame-hunting will get wrong. This repo has
  twice declared a working feature broken by inferring from a detector.
- It is cosmetic, brief, and reads as a puff of fire. Low priority — but it is the kind of
  one-frame artefact this project has decided twice before is worth retiring (the health bar, the
  backdrop layer), so it should not be closed as "fine" without a look.
- Related and still open: the **ragdoll / structure report** further down this file.

---

## RESOLVED 2026-08-10: the AIRSTRIKE has an author — it is flown in by an aircraft

**Fixed the same day it was raised.** Rob's second decision settled it: *"plane should fly first
before the player volley."* A straight-wing attack aircraft now crosses from the player's side,
releases the bomb, and exits — and only then does the infantry volley launch. Full write-up and the
two device-only bugs in `_plans/archive/AIRSTRIKE_PLANE.md`.

The measurement below is kept because it is WHY the fix took the shape it did: the problem was
never only that the bomb was ugly, it was that it detonated off-screen, and no amount of art fixes
a moment the camera is not pointed at.

<details>
<summary>The original entry, as raised</summary>

## UNRESOLVED: the AIRSTRIKE has no author — raised by Rob 2026-08-10

**Asked, on the day it shipped: "do we actually show something fly across the screen from the
player's side and strafe the enemy, or is it just explosions out of nowhere? user needs to see
more value if the latter."** It is much closer to the latter, and the answer below is read off the
code, not remembered.

**What the player actually sees today:**

- A single **grenade** — literally the grenadier's `projectile_grenade` prefab, olive-lime, 0.16
  scale — **pops into existence in mid-air**, no fade-in, nothing preceding it.
- It falls **straight down** (`vx = 0`) for **1.4s** onto the volley's landing point, mixed in
  among eleven infantry arcs.
- Impact runs the **standard splash path**: same blast, same scorch, same `PlayExplosion` as any
  grenade. `ProjectileEntity.IsAirstrike` is set and **read nowhere** — it has no rendering
  meaning at all.
- **No aircraft, no approach, no strafing run, no dedicated sound, no ground telegraph.**

**Two specifics that make it worse than that sounds:**

1. **It is the same object the grenadier throws**, in the same material at the same size. Nothing
   distinguishes a 250-coin airstrike from one more round in the volley. ~~On the device capture it
   was only findable because one round falls nose-down while the rest fly arcs.~~ **That sentence
   was WRONG and a control run disproved it — see the measurement below. The nose-down round is the
   TANK SHELL**, which fires on every volley for free.
2. **It does not come from off-screen.** `AirstrikeOriginY` is 5.0 and a soldier is ~1.30 world
   units tall, so it spawns under four soldier-heights up — about a fifth of the frame height,
   comfortably inside the picture. *The code comment on that constant used to claim it read as
   coming from off the top of the frame. It does not; the comment is corrected.*

**The design problem, stated plainly: the most expensive consumable has the least legible
presentation.** Its mechanics are fine — 24 damage, 1.1 splash, 2x against structures, the only
thing besides the tank shell that hurts a building — but the player cannot SEE what they bought.
That is worse for a consumable than for a permanent unlock, because it is gone after one use and
there is no second chance to appreciate it. `PRODUCT_DIRECTION.md`'s dopamine model is the doc
that governs.

**Candidate fixes, cheapest first. None is chosen:**

- **A growing ground shadow under the falling round.** Probably the best value of the lot: it
  telegraphs WHERE, which makes the 1.4s of hang time suspenseful instead of merely long, and it
  is the one change that adds gameplay information rather than decoration. The shadow pool exists
  but currently serves units only, and ground decals are sized in DEPTH, never width — the camera
  sits ~6 degrees above the plane.
- **Its own silhouette and its own blast.** A larger, darker bomb shape and a bigger explosion
  scale, so it is not the grenadier's round. `IsAirstrike` is already on the projectile and read
  by nothing, so the renderer has the hook it needs for free.
- **Its own sound** — a whistle on the way down. Audio is per-event already (`PlayExplosion`,
  `PlayGroundImpact`); there is no airstrike entry.
- **Raise the spawn genuinely off-frame**, so it enters the picture rather than appearing in it.
  Cheap, but on its own it just moves the pop-in somewhere less visible.
- **A plane that crosses and strafes** is the expensive option, and it is not obviously right:
  it drags in a second moving subject for the camera to hold at the same time as the ground
  exchange, which is exactly the load problem that keeps `HELI_ENABLED` switched off. Read
  `CAMERA_ARCHITECTURE.md` before costing this one — the camera is LOCKED.

### MEASURED ON DEVICE 2026-08-10, and it is worse than the above: THE BLAST IS OFF-SCREEN

An airstrike was bought with coins earned in play, armed, and fired into L1's bunker on the
documented drag (`input swipe 300 900 631 1231 600`), captured at 12 Mbit and read back at **30
fps** — not the 12 fps contact sheet the judgement above came from.

```
[Consumable] Airstrike armed=True
[Consumable] Airstrike fired
[Battle] volley: 12 rounds at 86% / 45.0deg      <- the volley alone is 11
release  t = 4.15s   (muzzle flashes, pinned off the frames)
airstrike impact  t = 4.15 + 1.4 = ~5.55s
volley impact     t = ~6.4s
```

**At t = 5.35–6.05s the camera is panning across BARE GROUND.** No bomb, no blast, no scorch — the
bunker is not even in frame yet; it enters at ~5.9s. The only explosions anywhere between release
and 7.2s are the volley's own cluster at ~6.4s. **The one thing the player paid 250 coins for
detonates roughly 0.85s before the camera arrives, and in this capture they never see it at all.**

The cause is a TIMING mismatch, not only an art gap. `AirstrikeFlightTime` is a **fixed 1.4s**,
chosen so the fall is legible however the player aimed — but the volley-follow camera is chasing
the ROUNDS, and on any arc longer than 1.4s it is still mid-pan when the airstrike lands. The
airstrike is not excluded from that mean either (`BattleTick` filters only `IsHeliShot`), so it
sits at the landing point from spawn and pulls the follow target forward while contributing nothing
the player can see.

**A control run settles the attribution.** The identical drag with NOTHING armed
(`[Battle] volley: 11 rounds`) shows the same dark nose-down round arcing over the field. That
round is the **tank shell**, which fires every volley. So the airstrike has never been seen on a
device by anyone: what two write-ups took for it was the shell. *Assert the output, not the input —
and take the control shot.*

**This reorders the candidate fixes.** Timing is now a PREREQUISITE, not a polish item: a plane, a
shadow, a whistle or a bigger blast are all wasted on a moment the camera is not pointed at.
Cheapest honest fix first — **make the airstrike arrive WITH the volley** (or a beat after it, as
punctuation), or give it its own camera beat. Neither is free of `CAMERA_ARCHITECTURE.md`, which is
LOCKED, so it needs an ask.

What is still NOT measured: how any of this feels at full speed to a person watching. The frames
prove the blast is off-frame; they cannot prove what a fixed version would feel like.

---

</details>

## A crowd-runner BONUS LEVEL — asked 2026-08-10

Rob's ask, in his words: "a bonus level that plays like those vertical scrollers where you have a
small force of units initially, you go through gates like x3 or +10 to increase the size, with
enemies coming down".

**The genre is the crowd runner** — *Count Masters: Crowd Runner*, *Join Clash*, *Crowd City*. The
loop: a squad runs forward on rails, the player steers left/right only, multiplier gates (`x3`,
`+10`, and their punishing siblings `-15`, `÷2`) sit in pairs so picking one refuses the other, and
obstacles and oncoming enemies shave the crowd. It ends in a mass collision where the surviving
COUNT is the whole result. The reason it works is that the number on screen is the score, the health
bar and the power-up at once, and steering is the only verb.

**Why it is a genuinely good fit here, and worth more than a novelty:**

- The game already owns a **crowd of soldiers as its unit of value** — the loadout picker is
  literally a count of troops, and a level is won or lost on how many men are left. A mode whose
  entire scoreboard is "how many did you finish with" speaks the game's existing language.
- It is the **first mode that could pay in TROOPS rather than coins**, which `PRODUCT_DIRECTION.md`
  has no answer for today.
- One rails camera and one input axis means it borrows nothing from `CAMERA_ARCHITECTURE.md`, which
  is LOCKED. It cannot destabilise the thing this project has spent the most time protecting.

**Open questions, none of them small:**

- **It shares no mechanic with the game.** No drag, no arc, no guessing angle and power — the one
  thing `GAME_DESIGN_LOCKS.md` builds everything else around. That is either the point (a bonus
  level is a palate cleanser) or it is a second game wearing this one's uniform. Worth deciding
  deliberately, because it also decides how much art and tech it may honestly borrow.
- **What does it pay, and can it be farmed?** If it pays coins it competes with the campaign as a
  grind; if it pays a one-off unlock it is content, not a loop. The stuck-player valve is what
  consumables are for, so this should probably NOT be another one.
- **Where does it sit?** A gate between stages, a daily, or an always-available side door — each
  implies a different amount of it.
- **What does the crowd DO at the end?** The genre's finale is a mass melee against a boss blob.
  This game has no melee: `SkirmishEntity` is defined and, like advancing squads, **is not ported**
  (see the Tier 1.3 write-up). So either the finale is a volley — which quietly returns it to the
  real game and might be the best idea in this whole entry — or melee gets built.
- **Art cost is the real bill**, not code: a rails runner wants a forward-facing camera and
  therefore front-on unit silhouettes, and every unit in this game is authored and MEASURED for a
  side-on camera at ~6° (`UNIT_VARIETY_DESIGN.md`, "width is all there is"). A crowd seen from
  behind is a new silhouette problem, not the solved one.

Not scheduled, and nothing above is a decision.

---

## Nuclear reactor structure — asked 2026-08-07

A new enemy structure: a nuclear reactor.

Nothing about its behaviour was specified, so the interesting question is what MECHANIC it owns.
`STRUCTURE_VARIETY_DESIGN.md` is the doc that governs, and its standing rule is that a structure
has to be distinct in SILHOUETTE at gameplay framing, not merely in texture — a wide cooling tower
with a flared top is about as far from the current set (boxes, tiers, a mast) as this game's
shapes get, so the silhouette half looks promising.

Open questions worth settling before modelling anything:

- **What does destroying it DO?** The obvious and good answer is that it is the first structure
  whose destruction hurts the things around it — a blast that damages nearby enemy units, so the
  reactor is a target you WANT to bring down rather than another HP bar. That would make it the
  first structure with a mechanic at all; today they differ only in HP, footprint and garrison
  capacity. `CollisionSystem` already has splash, and `EventSystems` already fires on a named
  structure being defeated (the boss-phase trigger reads exactly that), so the wiring largely
  exists.
- **Does it garrison?** Composition rule 5 wants the majority of the roster on structures. A
  reactor that carries no garrison spends a structure slot without helping that rule.
- **HP against the siege budget.** Per the 2026-08-07 audit a stock squad can do a FIXED 288
  structure damage per battle, so a reactor's HP is not a free number — see
  `project-siege-capacity` and the SIEGE DEFICIT check in `BalanceAudit`.
- Which biome(s)? CityRuins and Desert both have room; it would want its own backdrop read.

Builder would go in the OLD repo's `tools/blender/`, exported as `.glb` per `CLAUDE.md`, and
measured with `tools/measure_structures.py` BEFORE authoring a level around it.

---

## Dead units should SINK into the ground — asked 2026-08-07

Today a corpse ragdolls, lies there, and then **disappears** when its TTL expires. Rob wants it to
sink into the ground instead.

Why it is worth doing: a body vanishing in one frame is the same class of artefact this repo has
already paid for twice — the health bar that "held full strength and then vanished in one frame"
before it was given a transparent material, and the backdrop layer before it. A pop is read as a
bug even when the timing is right; a sink is read as the field clearing itself.

What it touches:

- The corpse lifetime already exists — find the ragdoll TTL in `BattleTick`/`CosmeticSystems` and
  give the last stretch of it a downward offset applied by the renderer, exactly as the health bar
  spends its last 0.7s fading.
- **It must be a RENDER-only offset.** Nothing in the tick may move, or the ragdoll's resting
  position stops agreeing with where the body actually fell.
- **`dt` VARIES** (`CLAUDE.md`): the sink must be dt-parameterised, not a per-tick constant.
- It has to sink far enough to be fully under the ground plane before the slot is recycled, or a
  reused slot pops a half-buried body back to standing. `UnitAnim.Stand()` already restores the
  root on recycle — the sink offset needs clearing on the same path, and that is exactly the
  "stopping an animation does not undo it" trap this repo has already hit once.
- Cheap and worth checking: at this camera's ~6 degrees a body lying flat is seen nearly edge-on,
  so a sink of well under a unit may be enough to read as gone.

---

## Dead units interact with structures in physically impossible ways — reported 2026-08-07
## PARTLY FIXED 2026-08-07 — see "Corpses levitating onto roofs" in HANDOVER.md

One reproducible mechanism is fixed: bodies were rested on a roof whenever they were horizontally
inside the footprint and above the box's BASE, so a corpse flung at a wall was snapped up the face
onto the roof. **Kept open** because the report came from play without a screenshot, so there may
be a second mechanism. The original notes follow.

Rob: "dead units can have physically impossible interactions with structures." Not yet reproduced
or characterised — no screenshot, so the exact failure is open.

**There is prior work here, and it is the first place to look.** `StepRagdolls` originally had no
notion of structures at all, so a body sailed straight THROUGH a building; it was given structure
awareness (see "Ragdolls: lean, and stopping at walls" in `HANDOVER.md`). Rob's report says that
is not the whole story. Plausible shapes, all consistent with "impossible":

- A corpse coming to rest INSIDE a structure's box rather than against its face.
- A body resting in mid-air where a structure USED to be — a garrison dies with its building, and
  if the ragdoll still collides against the dead structure's box it would settle on nothing.
- A corpse on a deck standing on a face it could not have reached, or intersecting a wall it was
  thrown at from the wrong side.
- The collision box is an AABB while the model is not, so a body can visually clip a sloped or
  narrow silhouette (a comms mast, a tower platform) while being "outside" the box.

The last two are the most likely given how the fix was written, and the SECOND one is the one I
would check first: structures are removed from `state.Structures` when destroyed, so whether a
ragdoll keeps colliding with a building that no longer exists depends on which list `StepRagdolls`
is handed and when.

**Capturing it is feasible and I should not have implied otherwise.** The FREE CAMERA (CAM) holds
through volleys and the victory screen and flies anywhere, which is exactly the tool for parking
in front of a corpse and looking at where it is relative to the wall — `HANDOVER.md` records that
half this project's visual bugs were confirmed in seconds once the camera could be parked in front
of the thing. A rig with a big structure and a garrison that dies on it (the demolition rig, or
L12's citadel) plus a few volleys should reproduce it. Prefer a PROBE over a pixel search: log the
ragdoll's resting position against the structure's box at rest, both ends, in one build.
