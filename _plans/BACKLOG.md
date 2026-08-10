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

## A NullReferenceException every frame on the LOADOUT screen — found 2026-08-10

Not asked for; found while device-testing the flame, and **pre-existing** — the flame is not
implicated (see below). Nothing visibly breaks: the loadout screen renders correctly, BEGIN works,
and the battle runs clean at 60 fps.

**The measurement**, taken with the app focused and logcat cleared immediately before each window:

```
195 NullReferenceExceptions in 3 seconds on the LOADOUT screen  (~65/s = one per frame)
  0 NullReferenceExceptions in 3 seconds IN BATTLE
```

`BattleRunner.Update` is the only frame in the IL2CPP trace — a release build carries no line
numbers, so that is as far as the stack goes.

**Why the flame is ruled out:** `SyncFlames` runs from `Render()`, which `Update` calls on EVERY
frame in BOTH contexts. Code that runs in both cannot explain an exception that occurs in only one.

**Where to look.** `Update` runs the whole tick and `Render` while the picker is open — only
`HandleInput` early-returns on `ui.LoadoutOpen`. So something the tick or the renderer touches is
null *before a battle has been entered* and not after. Prime suspects, cheapest first: `level`, and
anything `Render` reads off the not-yet-loaded level. **Put a probe in rather than reading for it**
— one build, a log at the top of each Sync\* — and note that the release build's stack will not
narrow it for you.

Worth fixing rather than ignoring: it is a thrown exception plus a stack capture per frame, on the
one screen where the player is sitting still and reading.

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
