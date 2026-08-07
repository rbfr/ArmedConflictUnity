# `_plans/BACKLOG.md` — asked for, not yet scheduled

Ideas Rob has asked to keep, with enough context that whoever picks one up does not have to
re-derive why. **Not sequenced and not started.** When one is scheduled it gets its own plan file
per `_plans/README.md`; when it ships, its conclusions move to the relevant design doc.

Nothing here overrides `GAME_DESIGN_LOCKS.md` or `CAMERA_ARCHITECTURE.md`.

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
