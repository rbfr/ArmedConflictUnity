# Mid-ground scenery variety

Opened 2026-08-19. Parked ask from 2026-08-18: Rob *"ok. seems repetitive
but we can address later."*

## Decisions (with Rob)

- Depth and the L1 car are **signed**. Do not move or rescale
  `prop_wreck_car` / `prop_dead_tree` / `prop_cactus`.
- Fix is a **wider per-biome set**, not a taste pass on those three.
- Do not widen the aim frame. Mid-ground stays z ≤ −6, typically ≈ −8.

## What ships

New GLBs from `tools/blender/build_prop_scenery.py`, `keepColors`,
same slots (x / z / scale untouched). L1 pair unchanged.

| L | Biome | Near | Far |
|---|---|---|---|
| 1 | Mountains | wreck_car (signed) | dead_tree (signed) |
| 2 | Forest | stump | dead_tree |
| 3 | MountainsDusk | boulder | dead_tree |
| 4 | CityRuins | wreck_car | rubble |
| 5 | Desert | cactus | barrel_cactus |
| 6 | Mountains | pine | boulder |
| 7 | Winter | snow_pine | boulder |
| 8 | Forest | pine | stump |
| 9 | MountainsDusk | pine | dead_tree |
| 10 | CityRuins | lamp | rubble |
| 11 | Ocean | skiff | piling |
| 12 | Desert | cactus | boulder |

Winter snow_pine is **dark**, no cap mesh — a white slab at 6° is a
white object, not snow.

## Status

SIGNED 2026-08-19. Rob: *"ok, looks nice. we may come back
and revisit but breaks up the monotony for the moment."*

## Done when

- No campaign level plants the same mid-ground model twice.
- L1 car slot numbers unchanged.
- `PortSelfTest.Run` green. On the phone for Rob.
