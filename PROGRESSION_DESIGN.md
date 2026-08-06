# PROGRESSION_DESIGN.md — ArmedConflict

Reference spec for the progression/engagement/economy layer. Planned 2026-07-16.
Companion to `GAME_DESIGN_LOCKS.md` — nothing here overrides a lock; the loadout system
fills the hook the locks explicitly reserve (`Roster`, `StageDefinition.unlockRewardId`).
The follow-on gameplay-dynamism layer (ammo types, mid-battle events, visual variety) is
spec'd in `DYNAMISM_DESIGN.md`.

## Design pillars (decided, treat as soft locks)

- **Engagement first, monetization stubbed.** Build the full coin/loadout/consumable loops
  now; IAP and rewarded ads land later behind stubs. Everything flows through one
  grant/spend API so the store is a retrofit, not a rework.
- **Skill-first difficulty.** Every level must be clearable at STOCK upgrade tier by a good
  shooter. Upgrades and consumables convert grind into comfort — they never gate progress.
  No level may be tuned to require spending (no "soft pressure walls"). This is a playtest
  gate on every new/re-tuned level.
- **Session shape: 2–3 battles, ~10 minutes.** Battles stay 2–4 min. Early game: an unlock
  or upgrade lands every 1–2 sessions, stretching to every 3–4 sessions late-campaign.
- **Legible numbers everywhere** (same principle as the star thresholds): round coin
  prices, upgrade bumps a player can say out loud ("+25% HP"), no formulas to decode.

## Diagnosis this plan answers

The minute-to-minute verb (drag → volley → watch) is proven and untouched. Repetition came
from everything around it being constant: the player roster is hardcoded (no pre-battle
decisions, no growth), stars are a gate with no payout attached, and every level shares one
implicit objective/shape. The three loops below fix each in turn.

---

## Phase 1 — Coins + Loadout (foundation; build first)

### 1a. Coins earned + shown

One soft currency: **coins**.

Victory payout = `levelBase × starMultiplier` + first-time bonuses:

| Result            | Multiplier / bonus              |
|-------------------|---------------------------------|
| 1★ win            | ×1.0                            |
| 2★ win            | ×1.5                            |
| 3★ win            | ×2.0                            |
| First clear ever  | +100% of levelBase (once)       |
| First 3★ on level | +150% of levelBase (once)       |
| Replay            | full formula, no bonus          |
| Defeat            | ~15% of levelBase (consolation) |

`levelBase` placeholder curve (tune from playtests; shape matters more than values):
L1–6 ≈ 40–80, L7–12 ≈ 90–150, L13–18 ≈ 160–250. Replays always pay — replaying old
levels for stars/coins is the intended "stuck player" path and must feel like income,
not homework.

Implementation: extend `ProgressStore` (coin balance alongside best-stars). Route ALL
grants and spends through a single `EconomyStore` API (see Phase 4) from day one.
Payout summary joins the victory screen; balance shown on stage/level select.

### 1b. Loadout screen (ships with the existing roster as the unlocked set)

- Pre-battle picker replacing the hardcoded roster in `GameViewModel.buildInitialState()`.
- Per-level **deployment budget in POINTS, not slots** — each unit type has a point cost
  (rifleman cheap, heavy/specialist expensive). Levels define `deployBudget`; the player
  composes any mix within it. Stays inside the locked 7–30 units/side scale.
- Default/"quick start" loadout auto-filled so a player who skips the screen gets the
  current hardcoded composition — the screen adds choice, never friction.
- **Sanity pass required**: audit all existing levels against min/max-budget
  compositions (all-cheap spam, all-expensive few). Some levels authored around the fixed
  roster may trivialize or brick under extreme mixes; adjust budgets or enemy comps.

### 1c. Unlocks + upgrade tiers

- `Roster` becomes real: each `UnitDefinition` gains unlock state + coin price. Keep 1–2
  types locked per stage so there's always a visible "saving up for" target.
- `StageDefinition.unlockRewardId` wired: each stage's signature unit is granted FREE on
  stage completion (e.g. Valley Front → Sniper, Stronghold → Mortar team, Ashfall → TBD).
  Coins buy things EARLY; completion always delivers. Locked units are BROWSABLE with
  stats visible (same "visible horizon" principle as locked stages).
- **Upgrades**: per-unit-type tiers, max 3–4, escalating cost, legible bumps
  (placeholder: +25% HP per tier OR +20% damage — pick ONE axis per tier so each purchase
  is describable in a sentence). Stats stay in Definition classes: tiers are data-driven
  multipliers, never hardcoded per-unit values.
- Persistence: `ProgressStore` grows unlockedUnitIds + tier per unit type (+ consumable
  inventory in Phase 2). Same SharedPreferences pattern.

**Shipped design note (resolves a conflict in the paragraph above):** the "Valley Front →
Sniper" example doesn't work as literally written — Sniper is already in Level 4's default
squad, inside Valley Front itself (L1-6), and every other unit type is likewise already
used in some level's default before its "home" stage would complete. There's no unit left
to gate without new 3D assets. Resolved (user-confirmed): unlock state gates the **loadout
picker only**, never a level's hardcoded default (a level always plays its authored
composition regardless of lock state). "Locked" means you can't yet *add* that unit to your
own loadout on a level earlier than where it's naturally introduced — unlock early with
coins, or for free when the owning stage is cleared (`unlockRewardId`, a safety-net grant
since sequential play already puts you past that point anyway). Assignments shipped: Sniper
(200c / Valley Front reward), HeavyRifleman (150c), MachineGunner (150c), Grenadier (200c),
RocketTrooper (250c / Enemy Stronghold reward). Rifleman is always unlocked. Ashfall City
has nothing left to gate this way (everything's unlocked by Level 9), so it pays a flat
`completionCoinBonus` (500) instead of an `unlockRewardId`. Tiers: 3 per unit, flat
150/350/700 ladder, fixed axis per unit (HP for Rifleman/HeavyRifleman, damage for the rest).

---

## Phase 2 — Consumables (the stuck-player valve + coin sink)

Single-use battle items, bought with coins, **cap 2 per battle**, selected on the loadout
screen, triggered from the battle HUD on the player's turn. All three reuse existing
systems — no new combat pipelines:

| Item                 | Effect                                          | Reuses                      |
|----------------------|--------------------------------------------------|-----------------------------|
| Airstrike            | One free off-roster splash volley on the drag    | projectile/splash pipeline  |
| Early Reinforcements | Calls the relief squad on demand (still 1×/battle) | existing reinforcement flow |
| Trauma Kit           | Heals the front rank a fixed amount              | armor-HP model              |

Rationale: a stuck player gets three honest outs — grind old levels, spend a consumable,
or (later) buy coins — instead of a wall. Walls convert a few and churn the rest.
Consumables are also the permanent coin sink that keeps veteran balances from saturating.

---

## Phase 3 — Level-shape variety (arrives with Stage 4+)

With loadout live, levels gain a second authored axis (deployBudget + enemy comp). New
archetypes land as **stage signature mechanics** (per the locked stage structure —
introduce gently, escalate, combine, boss finale):

- **Survival/defend**: nearly-all-advancer waves; the player is outnumbered but dug in.
  Win condition unchanged (roster-based — locked), tension inverted.
- **Elevation maps**: enemy on cliffs/rooftops via existing structure-carry
  (`standingOnStructureId`); mostly level geometry + new backgrounds.
- **Wind levels**: constant lateral drift, telegraphed pre-battle like the heli flyby.
  Changes aim-feel WITHOUT violating the no-landing-marker lock — the player still
  guesses, there's just a new variable to feel out.
- **Real bosses**: L6/L12/L18 retrofit + all future stage finales get a purpose-built
  centerpiece (multi-phase super-heavy armor, twin-heli finale) instead of "same but
  bigger." Boss HP/phases stay in Definition classes.

  **Shipped as data-driven trigger-gated waves, not a bespoke state machine** (the
  helicopter's `HeliMode` is the only real precedent for genuine behavior-changing phases,
  and rebuilding that 3x was rejected as disproportionate — see `LevelDefinition
  .BossPhaseTrigger`). Destroying a specific structure (or clearing its garrison directly,
  whichever the player does — see below) spawns a new enemy wave, escalating through one
  reinforcement wave and a high-HP capstone unit per level (`FirebaseCommander`/
  `NestCommander`/`CityHallGuardian` in `UnitDefinition.kt` — plain `.copy()`s of existing
  units, no new 3D assets). Verified end-to-end on L6: both phases fired, the capstone
  kept the battle alive until it was actually killed, Victory fired at the right moment,
  no crashes. L12/L18 use the identical code path with different level data — not
  separately re-verified on-device, but high confidence given L6's full pass.

  **Real gap found and fixed during testing**: the first on-device pass cleared L6 with
  the trigger structures (barracks/watchtower) still fully intact — the player had simply
  killed the garrison units directly, which is valid, expected play (the stepped-fortress
  design explicitly wants this: "a volley takes out a PART of the fortress, never the
  whole thing"). A trigger gated purely on structure destruction would rarely fire in
  normal games. Fixed: a trigger structure now counts as "defeated" if destroyed OR its
  garrison is cleared without ever collapsing the structure — both are equally valid ways
  to clear a position.

### Survival/defend — shipped

**Stage 4 (Frostline, L19–24)**: Dug-in outnumbered defense. The player faces multiple
simultaneous `advancePerTurn` ShieldBearer groups (the only melee-capable unit — pure
advancing assault, no ranged phase) instead of one. Six levels escalate the pressure shape:
introduce gently (L19: 2 groups) → escalate (L22: 4 groups) → combine with helicopter
support (L23: heliChance 0.35) → boss finale (L24: 5 groups + staged boss phases). No new
Kotlin — entirely data-driven via `EnemyGroup.advancePerTurn` and `PropPlacement`
(trench/wire/sandbags).

**Shipped on-device**: Confirmed L19 and L24 play through (ShieldBearers reach the line and
trigger skirmishes; player is NOT tuned to require spending; L24 boss phases fire and Victory
lands correctly). Spot-checked L20–23 for per-level new elements (wire belt, flanking split,
thin-line roster constraint, heli+wave). Frostline stage unlocks at 30 stars (≈56% of the
54 available from Stages 1–3), matching the Ashfall City ratio. Completion bonus: 700 coins.

---

## Phase 4 — Retention glue + monetization stubs

- **Star-milestone chests**: total-star thresholds (placeholder 25/50/75/100…) grant coin
  or unit rewards — the campaign star total becomes a progress bar of its own.
- **Daily first-win bonus**: flat coin bonus on the first victory each calendar day.
  Lightweight comeback trigger. **No energy system** — energy punishes exactly the replay
  behavior the star economy encourages.
- **`EconomyStore` stubs** (interfaces + no-op impls now, real later):
  - Rewarded ad: "double this battle's payout" hook on the victory screen.
  - IAP: coin packs. All grants route through the same API as battle payouts.
  - Analytics event names defined now even if unsent: `battle_start`, `battle_end`
    (result/stars/duration/level), `unit_unlocked`, `upgrade_purchased`,
    `consumable_used`, `purchase_intent`.

---

## Build order & status

| Slice | Contents | Status |
|-------|----------|--------|
| 1a | EconomyStore + coin payouts + balance UI | shipped |
| 1b | Loadout screen, deployBudget, level sanity pass | shipped (full sanity-pass audit deferred, see below) |
| 1c | Unlocks, upgrade tiers, unlockRewardId wiring | shipped |
| 2  | Consumables (3 items, HUD trigger, inventory) | shipped |
| 3  | Variety archetypes + boss retrofits | shipped — boss retrofits, survival/defend (Frostline), elevation maps (6672e84), wind levels (9df1a0c) |
| 4  | Chests, daily bonus, ad/IAP/analytics stubs | shipped (af0e158) — stubs only; real ads/IAP still future work |

Each slice ships independently and is playable on its own. Update the Status column as
slices land, and promote settled tuning values from "placeholder" to real numbers here.

**Phase 2 implementation note:** the debug "Auto" test-fire button (`GameViewModel
.testAutoFire()`) bypasses `onAimRelease` entirely and so never triggers the Airstrike
consumable (its firing logic lives only in the real drag-release path) — this is fine
since Auto is dev-only tooling, never shown to players, but worth knowing if Airstrike
ever looks like it "does nothing" while testing with Auto instead of a real drag.

### Slice 1b — non-negotiable requirements

1. **Level sanity pass is REQUIRED, not optional.** This is the plan's main design risk:
   the existing 18 levels were authored around one fixed roster. Before 1b ships, audit
   every level against extreme legal compositions (max-budget all-cheap spam AND
   all-expensive minimal count) and fix by adjusting `deployBudget` or enemy comps.
   A level that trivializes or bricks under a legal loadout is a 1b bug.
   **Status: deferred with the user's explicit sign-off** — there's no battle simulator
   in this codebase (the debug "Auto" button fires one volley, not a full battle), so a
   true audit means ~36 manually-played battles. 1b shipped with the structural
   safeguards only (`deployBudget` == today's exact squad cost, plus the locked 7-30
   units/side clamp enforced by `groundSquadMin`/`groundSquadMax` in
   `LevelDefinition.kt`), which rule out unbounded cheap-spam and illegal roster sizes
   by construction but say nothing about balance. **The full manual audit is still owed
   as a fast-follow session** before treating 1b as fully closed — do it before/alongside
   Slice 1c.
2. **Skipping the loadout screen must cost nothing.** The screen auto-fills a default
   loadout matching today's hardcoded composition; one tap on Begin plays exactly like
   the current game. Loadout adds choice, never friction — a player who never opens the
   picker keeps the full skill-first experience.
