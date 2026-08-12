# Tier 1.3 — Consumables — DONE 2026-08-10, confirmed on device

**All four shipped and were confirmed on a real device**, each by its OUTPUT: Trauma Kit
`hp 304 -> 320` (clamped, front rank only), Airstrike armed → fired → a 12-round volley where the
volley alone is 11, with the round visibly falling nose-down while the bullets fly their arcs and
the Garrison Post dropping 135 → 87, Early Reinforcements `10 -> 13 player units` formed up at the
right of the line, and Smoke Screen armed (button still reading `Smoke / ARMED`, not vanished) and
spent on the enemy's next volley. Purchases, the carry cap and the affordability tint were all
exercised with coins actually earned in play. `PortSelfTest` is at **576 checks** and every new one
was seen to FAIL against deliberately broken code first — then the block was CONSOLIDATED on
Rob's instruction from 50 assertions over 307 lines to 18 over 232, with the nine breakages re-run
to prove nothing was lost.

What changed from the plan below: nothing in scope. Overwatch Flare stayed out, for the reason
given.

---

`PRODUCT_DIRECTION.md` calls 1.3 "tactical consumable expansion — Smoke / Overwatch", gated on
*"when base consumables already feel good"*. **There are no base consumables in this port at all** —
`ConsumableType`, `ProgressStore.OwnedConsumables/AddConsumable/SpendConsumable`,
`EconomyStore.PurchaseConsumable` and `GameState.LoadedConsumables` are ported and a grep for callers
outside those files returns nothing. So 1.3 is: **build the base three, then the tactical two on
top.** Specs are `PROGRESSION_DESIGN.md` Phase 2 and `DYNAMISM_DESIGN.md` Phase C; the Kotlin's
shipped notes carry the arm/spend lessons and are the authority on behaviour.

## FOUR of the five ship. Overwatch Flare does NOT, and this is the reason

**Enemy units never advance in this port.** `UnitEntity.AdvancePerTurn` is imported by
`LevelBuilder` and read by `BattleRunner` only to count advancers for the threat line;
`AdvanceRemaining` is read in exactly one place (`GameState.IsVisuallyIdle`) and **written nowhere**. There
is no march step, and `SkirmishEntity` — the melee that an arrival resolves into — is defined,
counted in `IsVisuallyIdle`, and likewise never created. Advancing squads and melee are an EIGHTH dead
system.

Overwatch Flare halves the enemy's next advance budget (`EnemyAI.AdvanceBudget`, already ported).
With nothing to halve it is a 200-coin button that does nothing the player can feel — **which is
precisely wind's situation**, and this repo has already decided that case: *"do not author a wind
level or a wind schedule until wind does something"*. Shipping it would be worse than not shipping
it, because a bought item that changes nothing teaches the player their coins are decorative.

It is held, with its reason, rather than half-built. Porting advancing squads + melee is its own
piece of work and its own ask.

## What ships

| Item | Effect | Where it hooks |
|---|---|---|
| Airstrike (250c) | One synthetic splash round from overhead onto the volley's landing point | `BattleTick.FireVolley` |
| Early Reinforcements (200c) | Calls the relief squad on demand, shares `ReinforcementsSent` | new `BattleTick.UseEarlyReinforcements` |
| Trauma Kit (150c) | Heals the front rank a fixed amount, clamped to max HP | new `BattleTick.UseTraumaKit` |
| Smoke Screen (200c) | Next enemy volley fires at doubled jitter radius | `BattleTick.FireEnemyVolley` |

**Early Reinforcements drags a second port in with it.** The relief squad does not exist here
either — no builder, no march. The squad enters a formation's width BEHIND the player line and runs
to its slots on `MarchTargetX`, so without the march step the men bought and paid for stand off the
edge of the frame for the rest of the battle. The march is the item, not polish on it.

*(An earlier draft of this plan said an unwalked march would HANG THE TURN, because
`GameState.IsVisuallyIdle` is false while any player unit carries a march target. That was wrong and
the compiler caught it — the property is named `IsVisuallyIdle`, not `Settled`, the turn handover is
`TurnFlow.EvaluateVolley` and does not consult it, and nothing in this port reads it yet. The real
cost is a permanent latch on a ported facility that only `PortSelfTest` currently asks about. Left
here on purpose: it is the "assert the artefact, not the name you remember" rule catching the person
who wrote the rule down.)*

## Arm vs spend — the lesson the Kotlin paid for

Airstrike and Smoke are ARMED (a toggle), and spent only when they actually fire. A first Kotlin
implementation spent at arm time (Trauma Kit's instant pattern) and the HUD button — whose
visibility is gated on `LoadedConsumables > 0` — vanished the instant it was tapped, with no way to
see or un-arm the ARMED state. Trauma Kit and Early Reinforcements are instant: they resolve on the
tap, so tap-time IS use-time.

**The permanent `ProgressStore` spend lives in `BattleRunner`, not in the tick.** The two armed
items are consumed inside pure tick functions, and a `PlayerPrefs` write in there would fire on
every `PortSelfTest` call to `FireVolley` and quietly drain the editor's inventory. The runner
watches the ARMED FLAG's true→false transition, which is not a "delta inferred from a list length" —
the flag exists for exactly this and nothing else clears it.

## Order

1. `Consumables` catalog — type, name, price, description.
2. Tick effects, each pure: trauma kit, relief squad + march, airstrike, smoke.
3. Loadout screen: buy and equip, **cap 2 per battle**.
4. HUD triggers, on the player's Aiming phase only.
5. `PortSelfTest`, each check asserting the OUTPUT, each run against the old code first, and
   related facts asserted TOGETHER rather than one per line.
6. Device.

## Traps this work walks into

- **The loadout panel is a fixed layout built in code.** Adding a consumables section is exactly
  what pushed the Kotlin's Confirm button off the bottom of the screen — not clipped, absent from
  the tree and unreachable. `BattleUIPreview.Shots` renders the panel headless in seconds; use it
  before the device.
- **Auto tests none of this.** It bypasses the release path (so no Airstrike) as it bypasses ammo,
  and it is now a THREE-item list of what Auto cannot test.
- **Grenade is the airstrike's pipeline**, pool and visual included. Its flight time is its own
  fixed constant, never the player's — a flat drag can produce a sub-0.2s flight, and a 5-unit
  vertical drop compressed into a few frames reads as "nothing happened".
