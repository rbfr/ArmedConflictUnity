# PRODUCT_DIRECTION.md — ArmedConflict

**Product / engagement direction for the Unity build.** Written 2026-08-06 after an
outside-eyes pass on the port + the existing design suite. This is the **build priority
and retention bible** Claude (or any agent) should plan work against.

## Relationship to other docs (read order when unsure)

| Doc | Role | Conflict rule |
|-----|------|---------------|
| `GAME_DESIGN_LOCKS.md` | CLOSED combat/aim/camera/win rules | **Always wins.** Nothing here overrides a lock. |
| `CAMERA_ARCHITECTURE.md` | CLOSED camera model | Do not touch without an explicit ask. |
| `PROGRESSION_DESIGN.md` | Coins, loadout, unlocks, consumables, daily/chests | **Implement and feel** — specs mostly shipped historically; Unity must make them player-visible. |
| `DYNAMISM_DESIGN.md` | Ammo types, mid-battle events, factions, cosmetics | **Next mechanic layer** after the product spine (Tier 1 below). |
| `UNIT_VARIETY_DESIGN.md` / `STRUCTURE_VARIETY_DESIGN.md` | Art legibility history + rules | Follow measurement rules; no new class sprawl. |
| **This file** | What to build *next* for a casual game people reopen | Priority, dopamine model, campaign packaging, anti-goals. |

If a task improves "depth" but fights a lock or this priority stack, **stop and ask**.

---

## Diagnosis (why this doc exists)

**What's proven:** the minute-to-minute verb — drag → convergent volley → watch the line break —
with locked guess-aim (no landing marker), concentration fire, roster win/loss, structure
theater, and a steady 60 fps Unity port across all levels.

**What's missing for a casual product:** the loop closes too early.

```
Anticipate → Act → Spectacle → Score → Progress → Next reason to open the app
```

- **Act → Spectacle** is strong (the volley *is* the game).
- **Score → Progress → Next reason** is where the product still feels like a spike:
  campaign is **7 levels (one per biome) + 17 test rigs**; meta (coins/loadout/unlocks)
  may exist in code history or stubs but must be **felt in the first 15 minutes** of
  player-facing play.
- Decision density per battle is still mostly **one axis** (the drag). Loadout + ammo +
  telegraphed events fix that **without** changing aim (see `DYNAMISM_DESIGN.md`).
- "Levels are disposable test rigs" was correct for mechanics. For product, the
  player-facing map must be a **short funnel of taught moments**, not orthogonal sandboxes.

**Unity is the product** (Android retired 2026-08-06). Content velocity and victory/meta
juice are now first-class systems, not leftovers from the port.

---

## Design pillars (soft locks for product work)

1. **Protect the core verb.** No landing marker, no aim-pan to impact, no real-time conversion,
   no camera redesign. Skill fantasy = "I aimed that."
2. **Skill-first difficulty** (same as `PROGRESSION_DESIGN.md`). Stock clear always possible;
   upgrades/consumables = comfort, never walls. No energy system.
3. **Session shape:** 2–3 battles, ~10 minutes. Battles 2–4 min. Unlock/upgrade every 1–2
   early sessions, every 3–4 late.
4. **Legible everything.** One-sentence ammo, upgrades, stars, events. Numbers the player can
   say out loud.
5. **Content before toys.** A polished 12–18 level arc retains better than a perfect unused system.
6. **Spectacle clarity over HP realism.** Concentration and kill readability beat chip attrition.
7. **Telegraph, don't blindside.** Mid-battle shape changes warn one full turn ahead.
8. **Default paths cost nothing.** Loadout auto-fills; skippers get full skill-first play.
9. **Engagement first, monetization stubbed.** Ads/IAP only after victory emotion and a real
   campaign exist; all money through one grant API.
10. **Test rigs are not the campaign.** Player-facing list is ordered and short; training/dev
    levels gated or hidden.

---

## The dopamine model (build juice against these layers)

Casual Archery Bastion / Castle War chemistry for *this* game:

### 1. Micro-hit (every 2–5s while resolving)

Unit drops, HP snap, structure crack stage, garrison fall-and-die, scorch, layered SFX.

**Rule:** every *successful* player volley should produce at least one unmistakable kill
**or** structure stage change. A "good" lob that only chips walls with nobody falling reads
as failure even when math says progress. Prefer kill readability over pure HP simulation.

### 2. Meso-hit (end of each player turn)

Gut-level one-liner: broke a bunker / stopped a charge / clipped a threat. Camera settle,
multi-kill emphasis, optional star-progress tick if near a threshold.

### 3. Macro-hit (end of battle)

- Stars with **shown reasons** every time ("why 2★").
- Coin count-up + first-clear / first-3★ bonuses animated (formula in `PROGRESSION_DESIGN.md`).
- Unlock / upgrade teaser: "Sniper 40% funded" / chest progress.

### 4. Session-hit (after 2–3 battles)

Something permanent changed: unit tier, ammo unlock, stage palette, star-milestone chest.

### 5. Comeback-hit (next day)

Daily first-win bonus; visible "one battle from X." **No energy** — energy fights star-farm
and replay income.

### Investment hierarchy (do not invert)

> **Spectacle clarity → battle-end juice → short campaign arc → light meta (loadout/ammo)
> → daily glue → monetization**

---

## What the casual player wants

Persona: phone in hand, 10-minute sessions, competence theater not spreadsheets.

| They want | We give them |
|-----------|----------------|
| "I'm a good shot" | Guess-aim skill + visible multi-kills |
| "I'm building an army" | Unlocks, tiers, loadout identity |
| "Tonight's different" | Stage signature mechanics + biomes/factions |
| "One more game" | Almost-3★, almost-unlock, daily bonus |
| "I'm not stupid" | No hidden paywall, no energy, clear failure |

**"One more game" trigger (primary):**

> I almost 3★'d / I almost unlocked the rocket / I know exactly what I'd do differently.

**Loss screens teach, don't scold.** Example: "Shield wall reached the line — focus them
next time" + cheap retry + optional consumable nudge after repeated fails on the same level
(not spam every death).

---

## Priority stack — build in this order

### Tier 0 — Product spine (NOW)

Do this before new combat toys.

| # | Work | Done when |
|---|------|-----------|
| 0.1 | **Player-facing campaign of 12–18 levels** (reuse biomes; two jobs per biome is fine) | Ordered map; every level teaches or twists one thing; test rigs hidden/gated |
| 0.2 | **First 10 minutes = onboarding arc** | By minute 2–3: multi-kill, structure collapse, victory coins, next unlock visible |
| 0.3 | **Victory screen is a feature** | Star fill, coin count-up, new best, progress toward unlock — not a silent Next modal |
| 0.4 | **Meta loop felt in-player** | Coin balance visible; something to buy that changes the next battle; default loadout; 3★/first-clear make replays feel smart |
| 0.5 | **Star criteria aspirational and fair** | Opaque or RNG-gated 3★ is a bug; always show *why* on victory |
| 0.6 | **Enemy turn juice** | Readable windup / charge / threat telegraph — fear is engagement, silence is punishment |

**Campaign packaging rules**

- Borrow stage structure from progression history: **introduce → escalate → combine → boss**.
- One-level-per-biome is an *art* constraint, not a content limit — **reuse biomes** with
  different jobs (see beat chart below).
- Content is mostly **enemy composition + structure layout + one signature mechanic**
  (data-driven). Polish mechanics; author levels as *teaching tools*, not art pieces.
- Composition rules at the top of the Kotlin campaign block still apply (aim frames player
  line; scout frames enemy cluster incl. structure edges; one dominant structure; etc.).

**Loadout balance note:** full extreme-comp sanity audit was deferred historically. Until
done: keep `deployBudget` tight to authored cost; structural 7–30 clamps stay; do not
widen loadout expression in a way that bricks/trivialises levels. A level that breaks under
a *legal* loadout is a product bug.

### Tier 1 — In-battle variety (highest engagement per effort)

After spine is playable:

| # | Work | Spec source | Notes |
|---|------|-------------|-------|
| 1.1 | **Ammo types** (Standard / Incendiary / AP / Cluster) | `DYNAMISM_DESIGN.md` Phase A | One free permanent choice per turn; unlock once with coins; never required to clear |
| 1.2 | **Telegraphed mid-battle events** | Phase B | Wind shifts + reinforcement waves; one full turn warning |
| 1.3 | **Tactical consumable expansion** | Phase C | Smoke / Overwatch when base consumables already feel good |
| 1.4 | **Heli** | locks + dynamism | Only when camera choreography is boring-stable. Premium spectacle, not day-one retention. Do not flip on broken framing. |

### Tier 2 — Fantasy & identity

| # | Work | Notes |
|---|------|-------|
| 2.1 | Enemy **factions per stage** (palettes) | Data + materials; "I hate that army" identity |
| 2.2 | **Crowd + hero** readability | Mass interchangeable units + few large heroes (see unit variety doc). Width/prop at gameplay framing, not Blender beauty shots |
| 2.3 | Keep roster **mechanic-distinct** | Six pickable is fine; no class sprawl that plays the same |
| 2.4 | Player **cosmetics** (vanity only) | Coin sink, zero balance effect |

### Tier 3 — Habit glue (after ≥5 fun sessions exist)

| # | Work | Spec source |
|---|------|-------------|
| 3.1 | Star-milestone chests | `PROGRESSION_DESIGN.md` Phase 4 |
| 3.2 | Daily first-win bonus | Phase 4 |
| 3.3 | Analytics events (even if unsent) | `battle_start/end`, unlock, upgrade, consumable, purchase_intent |
| 3.4 | Real ads/IAP | Double payout / coin packs only — never pay-to-pass |

---

## Soft-launch campaign beat chart (12 levels)

Author/order the **player-facing** campaign against this chart. Biomes may repeat. Map each
level to **one primary teach** so the list is a funnel, not a zoo.

| L# | Beat | Primary teach / twist | Dopamine beat | Systems to exercise |
|----|------|----------------------|---------------|---------------------|
| 1 | Teach aim + concentration | Multi-kill volley on a soft line | "I'm good at this" | Standard volley only; wide forgiving formation |
| 2 | Structures matter | Collapse + garrison fall | Spectacle upgrade | One dominant structure + garrison |
| 3 | Prioritize threats | MG nest / rocket / heavy in mixed line | Target reading | Mixed enemy types; still stock-clearable |
| 4 | Melee pressure intro | Kill the charge before it arrives | Panic → relief | `advancePerTurn` small; skirmish readable on camera |
| 5 | Elevation / deck fight | Aim for structure tops / garrisons | Vertical fantasy | `standingOnStructureId`; structure-first composition |
| 6 | Stage boss A | Phase trigger wave + capstone | Climax | `BossPhaseTrigger`; structure-or-garrison-clear counts |
| 7 | Wind intro | Drift is a new variable; same drag | "This level is different" | `windAccelZ`; telegraph pre-battle |
| 8 | Combine: wind + structure | Same skills under pressure | Competence | Reuse biome from L2/L5 |
| 9 | Survival / defend intro | Outnumbered but dug in; win still roster-based | Tension invert | Shield/advance groups; props (trench/wire) |
| 10 | Reinforcement race | Kill priority vs clock ("armor in 2 turns") | Anticipation | Telegraphed `arrivesOnTurn` wave |
| 11 | Optional premium threat | Heli **only if** framing stable; else heavy/rocket focus fire | Spectacle or skill exam | `heliChance` gated by readiness; else elite push |
| 12 | Stage boss B / finale | Combine 2–3 prior twists | "I finished the arc" | Boss phases + one prior mechanic; completion coin/unlock |

**After 12:** expand toward 15–18 by splitting escalate/combine beats (second melee shape,
second elevation, faction palette swap) — not by adding new verbs.

**Test / sandbox levels:** keep for dev (`PortSelfTest`, AUTO harness, composition experiments).
They must **not** appear as the main campaign path or inflate "24 levels" marketing in UI.

### Onboarding non-negotiables (L1–L3)

- First multi-kill and first structure collapse happen early and on-camera.
- Victory of L1 pays coins **visibly** and shows a next goal (unlock bar or L2 tease).
- No loadout friction: default squad = Begin in one tap.
- Difficulty measured with **real drags**, never Auto (Auto is optimal and structure-blind).

---

## Juice checklist (cheap, high ROI — do alongside Tier 0)

- [ ] **Kill confirm** — clearer falls; multi-kill emphasis (SFX/layering; light punch, not slow-mo spam)
- [ ] **Structure theater** — crack stages already exist; **final collapse** is the loudest moment in the game
- [ ] **Near-miss feedback** — dust/scorch on ground misses so aim skill feels continuous
- [ ] **Haptics** — volley release + big impacts (mobile expectation)
- [ ] **Event popups over permanent HUD clutter** — "Bunker destroyed", "Reinforcements!", "Wind rising →"
- [ ] **Victory sequence** — stars → coins → unlock teaser → primary CTA (Next / Retry for 3★)
- [ ] **Fail sequence** — one teaching line + Retry + optional consumable after repeat fails

---

## Explicit anti-goals (do not build)

- Real-time RTS, base-building, deep inventory, or hero ability bars that replace the volley.
- Landing markers / aim assists that solve the skill fantasy.
- Dozens of unit classes before six read at a glance in motion.
- Battles stretched past ~4–5 min for "epic" — epic is **phase structure**, not duration.
- Live ops / seasons before ~20 solid levels and a stable meta.
- Rewarded ads before victory emotion is polished.
- Opening camera/aim locks or turn structure "for engagement."
- Paywalls, energy, or levels tuned to require spending.
- Surprising mid-flight rule changes (wind etc. only between turns, telegraphed).

---

## Suggested 6–8 week build order

| Weeks | Focus | Outcome |
|-------|--------|---------|
| 1–2 | Campaign packaging + onboarding + victory juice + coins visible | 12-level player path; L1 dopamine by minute 2–3 |
| 2–3 | Loadout defaults + 2–3 meaningful unlocks + felt upgrade | Meta loop closes each session |
| 3–4 | Ammo (AP + Cluster first is enough) + HUD selector | One decision beyond the drag |
| 4–5 | Set-piece levels (melee, elevation, boss) as trailer moments | Memorable middle/end of arc |
| 5–6 | Star milestones + daily first win; fail/retry polish; consumable nudge | Habit hooks |
| 6–8 | Faction palettes + cosmetics *or* one new silhouette | Identity + vanity sink |

Parallel art: lock crowd-vs-hero in **moving** play; spend budget on structure theater and
props more than new mountain rows.

---

## Success criteria (soft launch)

Order-of-magnitude goals — instrument before arguing numbers:

| Signal | Target (starting point) |
|--------|-------------------------|
| Session length | Median ≥ 2 battles |
| "One more" | ≥ 30% of wins go to retry-for-stars **or** shop/upgrade before quit |
| Qualitative session-1 | Testers can name **what they'll buy or unlock next** |
| Skill-first gate | Every shipped level clearable at stock tier by a competent shooter |
| Feel | Steady 60 fps release builds; measure instructions not CPU% on device |

D1 retention depends on store/network context; use internal "second session same day or next
day" as the early proxy once analytics exist.

---

## Implementation notes for agents

1. **Read `HANDOVER.md` + `CLAUDE.md` first** for traps and workflow; this file for *what* to
   prioritise.
2. **`PortSelfTest.Run` after every change.** Prefer new checks that lock product invariants
   (e.g. levelNumber sequencing, economy grant routing) when you touch those systems.
3. **Data authoring is IN UNITY** as of 2026-08-06 — the ScriptableObjects in `Assets/GameData`
   are the source of truth, edited directly. Read `LEVEL_AUTHORING.md` before touching a level and
   run `LevelComposition.Report`. `LegacyKotlinImport` still exists and still overwrites
   everything; it refuses to run without `-iAcceptDataLoss`.
4. **Scene rebuild** when prefabs/materials/serialized refs change; code-only often does not.
5. **Verify juice and campaign feel on device** with real drags — not Auto, not editor-only.
6. **When implementing progression/dynamism**, update status tables in those docs; when
   priority or beat chart changes, update **this** file.
7. **Minimal diffs; no drive-by refactors.** Product work is content + UI juice + wiring
   existing systems more often than new engines.
8. **Commit/push only when asked.**

### Mapping existing systems → this plan

| Product need | Likely existing hook |
|--------------|----------------------|
| Coins / payouts | `EconomyStore`, `ProgressStore`, victory UI |
| Loadout / budget | `deployBudget`, roster unlocks, loadout screen |
| Consumables | Airstrike, Early Reinforcements, Trauma Kit (+ Smoke/Overwatch in dynamism) |
| Boss phases | `BossPhaseTrigger`, structure-or-garrison-clear |
| Melee / survival | `advancePerTurn`, ShieldBearer skirmish, Frostline-style data |
| Wind | `windAccelZ` / wind schedule |
| Heli | `heliChance` + enable flag; camera readiness gate |
| Ammo | `DYNAMISM` Phase A — payload on projectile + HUD selector |
| Factions | stage palette / material overrides |
| Stars | best-stars + thresholds; must gain **reasons UI** |

If a hook is missing in Unity but present in design history, **port/wire the minimum** — do
not re-speculate a parallel economy.

---

## One-line north star

> **I aimed well → the sky filled with my army's fire → something important broke → I got
> paid → I'm closer to a cooler army → I know the next fight's trick.**

Build the second half of that sentence with the same ruthlessness already applied to camera
locks and unit measurement.

---

## Status

| Item | State |
|------|--------|
| Direction captured | 2026-08-06 |
| Sequenced plan for Tier 0 | `_plans/archive/TIER0_PLAN.md`, phases A–F |
| 0.1 campaign restructure | **DONE 2026-08-06** — 12 levels, one beat each, rigs gated behind RIGS, 0 composition warnings |
| 0.2 onboarding arc | **DONE 2026-08-06** — L1-3 teach in order, L1 pays visibly, default squad is one tap |
| 0.3 victory screen | **DONE 2026-08-06**, confirmed on device. Stars, reasons, coin count-up, bonus banner, retry/next |
| 0.4a coins felt | **DONE** — the economy was fully ported and never called; one call site turned it on. Balance is now persistent on screen |
| 0.4b something to buy | **DONE 2026-08-06** — roster, picker, unlocks. Slots fixed / points buy quality, so no loadout can break the framing |
| 0.5 star criteria + reasons | **DONE** — pure roster survival, reason shown on every victory |
| 0.6 enemy turn juice | **DONE 2026-08-06**. Narration banners withdrawn 2026-08-19 (Kotlin remnant); Rob: *"ok that's good."* Standing telegraph strip for inbound waves remains; boss announcements still flash. HUD still names the phase. |
| Authored defaults + encounter ammo | **DONE 2026-08-19**, device-tested on a fresh L1→L7. `Loadout.Default` is the authored mix; reaching a level unlocks those units; AP after L2 / Incendiary after L4, pre-select until the player taps a chip. L1–L2 stay rifle. Rob: *"it works."* |
| Tier 1 ammo | per `DYNAMISM_DESIGN.md` status |
| 1.2 telegraphed events | **HALF DONE 2026-08-07.** Reinforcement waves ship with a live multi-turn countdown, and the schedule now covers L10 and L11 (both 2-turn leads). **Wind is NOT shipped and is not a scheduling problem** — `windAccelZ` drifts the round in Z while collision is X/Y only, so a wind schedule would telegraph a change that cannot alter what a shot hits. Making it real is a physics change and needs an ask |
| 1.3 consumables | **DONE 2026-08-10**, device-confirmed. Four items bought, carried and fired; the Airstrike's aircraft was rebuilt 2026-08-11 and signed off. Overwatch Flare is deliberately NOT sold — nothing in this port advances for it to halve |
| 1.4 heli | **SHUT.** `HELI_ENABLED=false` is a camera-load decision, not a stale flag |
| 2.1 enemy factions | **DONE 2026-08-11**, device-confirmed on L1 → L7 → L1. Redguard (Valley Front, the existing red, unchanged) and Ironclad Legion (Enemy Stronghold, steel blue-grey), authored as `FactionDefinitionSO` assets and attached to the stage. Enemy uniform + gear only; trim, skin, structures and the player are untouched. The level card reads "Enemy: &lt;faction&gt;" |
| 2.2 crowd + hero | **DONE 2026-08-12**, device-confirmed, both halves in one build. Heroes are `renderScale` 1.9 and staged clear of their structures (Rob rejected the first placement — "really tough to hit" — and rule 8 came out of that). The crowd half split every garrison into more, weaker bodies, 155 → 248, at CONSTANT hp, damage and structure damage — proved by building all twelve levels on both data sets and by a byte-identical `BalanceAudit`. Shrinking the structures instead was tried first and died to arithmetic |
| 2.3 roster distinct | **DONE 2026-08-12**, NOT yet device-confirmed. The audit found the roster was not distinct at all: the machine gunner's burst was read by `AutoFire` and by nothing else, so it fired one round and measured identically to the shield bearer, whose own sold mechanic (melee) is unported. Burst fixed; shield bearer given ARMOUR (`damageTakenMultiplier` 0.5) instead. `RosterAudit.Report` fires a REAL volley per class and is the standing instrument — 0 errors. Enemy bursts and enemy armour deliberately left alone: both would make signed-off levels harder |
| 2.4 player cosmetics | **DONE 2026-08-11**, device-confirmed. Four camo sets — Olive Drab free, Desert Tan 300c, Urban Grey 350c, Arctic White 400c — bought and worn on the loadout screen, repainting the player's units through the same path a faction repaints the enemy's. Pure vanity: asserted by running the same seeded volley under two sets and demanding identical damage. RIGS lends the whole wardrobe for testing and writes nothing |
| This doc | living — update priorities when Rob reorders work |
