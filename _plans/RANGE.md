# L1 range trial

Opened 2026-08-20. Product feel, not a look pass. Camera stays locked —
do not widen the aim frame.

## Decision

The infantry street, not the tank→structure number, is what read as
"too close". L1's men stood 11.5 apart while the outpost sat at 16.5.
Archery Bastions fills its frame with a tall close keep; the gap we
wanted is no-man's-land, not a wider camera.

Cannot slide the enemy without raising the envelope: flat max at v=9
is 20.25, and a roofed garrison at +2 goes over 100%. L1's outpost
deck is only 1.4, but the back rank on the new 18.5 was 99% at v=9.

## Phases

### A. L1 only — DONE 2026-08-20 (headless)

- `MaxAimMagnitude` 9 → 9.5 (`MaxRange45` 20.25 → 22.56)
- `ProjectileSpeedScale` 0.0064 → 0.00677 so a ~525 px drag is still 100%
- L1 outpost 7 → 9, ground squad 4.5 → 6.5, tank stays −9.5
- Built infantry gap ~10.4 → 12.4. Garrison 86% front / 94% back.
- Rule 4 checker max 18 → 20. Min stays 14 (other levels unmoved)
- Aim frame not touched

Side effect: the other eleven campaign levels are slightly easier until
they slide, because the envelope grew and they did not.

### B. Device — SIGNED 2026-08-20

Rob, after a real L1 drag: *"yes, that actually plays better in my
opinion."* Do not reopen as taste. Do not widen the aim frame.

### C. Rest of campaign — DONE 2026-08-20 (headless)

Rob: *"yes, let's do the rest. for the ones with melee, they should
move further on each turn to get closer to the player side. also,
they should not fire volley."*

- L2–L12 enemy groups, enemy structures, waves/bosses, and play-plane
  props with x>0 all +2. Player tanks, player line, mid-ground, roads
  unmoved. L1 not double-slid.
- Shield charges (L4/L8/L9/L12) step further so the extra street does
  not add turns: 1.1→1.5, 1.0→1.3, 1.2→1.5. L11 heavies still 1.2 —
  they are a firing line, not melee.
- `FireEnemyVolley` / `PrepareEnemyVolley` skip `meleeDamage > 0`.
  That lock was prose; shield bearers had been putting rounds in the
  air.

Device owed on a real drag, not Auto. Aim frame not widened.

### D. L5 no tank — on phone 2026-08-21

Rob: *"i like the new distance."* then *"ok, fine for now."* The
tank made the loft a 3-shell errand. Stripped L5 only: no
`player_tank`, crew folded into the ground line (2+5 → 7), no HP
retune. Ground jog-in still plays. Rule 4 falls back to front rank.
L3 still has a tank. Shop parked in `_plans/BACKLOG.md`. If the
tower reads as a wall, cut `hpScale` before inventing a shop.

### E. L5 roles — on phone 2026-08-21

Rob thought the tower was snipers; it was six MG. Now 3 MG in the
street, one sniper on the platform. Principle (not a checked rule):
MG forward, snipers elevated/back. L3 left at one sniper from the
wrong-level cut. Do not restore L3's three or retune L4/L6/L9/L10/L11
unprompted.

## Do not

- Widen the aim frame
- Drop gravity in the same pass
- Go to +4 / v=10
- Make L11's elite heavies melee; they hold as a firing line
- Sell the tank in the same pass as stripping L5 — skill-first
  says L5 must play at stock first
