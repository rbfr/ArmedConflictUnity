# Elbow — firing stance

**Asked 2026-08-25.** The hold is a carry: both hands on the rifle, forward
hand at the receiver, not out on the forestock. Rob wants a firing stance.
Do not model until Phase 0 has been looked at.

## Decision

A firing stance is wanted. The shipped carry stays until a cheap experiment
reads at Aiming distance. This is not a LateUpdate yaw tweak and it is not
a new clip set.

## Why the handover said "invalidates every clip"

That is true of one shape and false of another.

Kenney's curves bind `torso/arm-left` and `torso/arm-right`. **Inserting** a
joint into that path (torso → elbow → arm) breaks every clip. **Parenting a
child** under the existing arm (`torso/arm-left/elbow-left`) does not: the
clips keep writing the shoulder, the forearm holds a rest bend the clips
never touch. Walk, idle, hold, shoot, melee, die all still play.

The KenneyUnits comment about "no elbow to bend" is about the old
material-grouped meshes, not this constraint.

## The actual risk

`UNIT_VARIETY_DESIGN.md` Attempt 1 already tried a bent elbow at gameplay
scale and it did not read. Height is not an axis; the camera sits ~6° off
the ground. An elbow that looks right in `UnitPosePreview` can still be
invisible in the Aiming frame. That is the thing to settle before any of
the seven GLBs are rebuilt.

## Phases

### 0. LOOK — DONE 2026-08-25, during the L4 5-shell play.

Watched the shipped carry in a real Aiming frame (8 riflemen, player
line, 6 degrees). Both hands on the rifle; it reads as a carry, not as
one arm out. At that distance it already reads as holding the gun. A
firing stance is still wanted; it is not required to make the pose
legible. Phase 1 is still the cheap experiment, not a remodel.

### 1. CHEAP EXPERIMENT — rifleman only, clips untouched — DONE 2026-08-27 (preview; device frame owed)

Child joints `torso/arm-left/elbow-left` (and right). First export parented
while the arm empty's matrix was identity, so localPos was WORLD and a
flex flung the forearm off the body. `view_layer.update()` after each
joint; PortSelfTest now asserts localPos (0, −0.42, 0).

Left flex −40° about local Z (bone axis is Y; X lifts the hand off the
gun). Right elbow stays identity so the gun on `arm-right` does not part
from the grip. UnitPosePreview 3/4 reads as both hands on the rifle,
forward hand on the forestock.

**Device 2026-08-27, L1 Aiming.** At gameplay distance it is the same
hold-the-gun read Phase 0 already signed — the elbow is not a new
silhouette. Free camera z 5.50 → 1.50: two hands on the rifle, no
flying forearm. Attempt 1 still holds: height is not an axis at 6°.
Rob 2026-08-27: *"elbow is fine, let's keep it."* Signed. Phase 2 follows
so the other six are not left on the old one-bone arms.

### 2. THE OTHER SIX — DONE 2026-08-27

`core()` got the same child elbows. All seven GLBs copied. Scene rebuild.
PortSelfTest asserts every class prefab has elbow-left as a child offset
down the arm, not a world loc.

### Do not

- Re-author Kenney clips.
- Insert a joint into `torso/arm-*` (that really does invalidate the set).
- Spend yaw past `HoldLeftInward` 45 — it already costs 30% of forward
  reach and then the rifle passes the left hand.
- Re-open a clip rewrite; the child-joint shape is the one that kept Kenney.

## Instrument

`UnitPosePreview.Shots`. Same traps as 08-25: first batchmode frame is
unlit, measure mesh bounds not joint nodes, apply `ReadyDrop` before
judging.
