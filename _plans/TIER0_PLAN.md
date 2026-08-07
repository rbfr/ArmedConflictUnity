# TIER0_PLAN.md — implementation plan for `PRODUCT_DIRECTION.md` Tier 0

Written 2026-08-06. Sequenced plan for the product spine (0.1–0.6).

**ALL SIX PHASES ARE DONE** (2026-08-06), all confirmed on device. One item is PARTLY owed: the
Phase E BALANCE AUDIT. Its **arithmetic half is done** — `BalanceAudit.Report` checks reach, the
volley race and the melee clock headless over both ends of the legal loadout space, and it found
and fixed a shipped level (L7) that no loadout could win; reach is now checked composition rule 7.
The **device half — every level clearable at stock, measured with real drags — is still owed**,
and the audit says which levels to drag first. See `HANDOVER.md`.
Each phase heading below carries what actually shipped, including where it differed from the plan.

Two decisions were taken with Rob before this plan was written, and the sequencing depends on
both:

- **UI technology: uGUI Canvas + TextMeshPro.** There is no UI layer today — every pixel is IMGUI
  `OnGUI` inside `BattleRunner.cs`, and `com.unity.ugui` is not even in `manifest.json`.
- **Game data authoring MOVES INTO UNITY, now.** The ScriptableObjects become the source of truth.
  This closes `HANDOVER.md`'s "Data authoring, once Android is retired" open question.

---

## What the audit found

The starting position is better than `PRODUCT_DIRECTION.md`'s diagnosis assumes in one place and
worse in another.

**Better: the entire economy is already ported and tested — it is simply never called.**
`EconomyStore` and `ProgressStore` are complete (star multipliers, first-clear and first-3★
bonuses, defeat consolation, milestone chests at 25/50/75/100, daily first win, unit/tier/ammo/
cosmetic purchases). `TurnFlow.AwardVictory` composes all of it correctly, including the
order-of-operations trap its own comment documents.

`AwardVictory` has **zero runtime callers.** The only economy line in the running game is
`ProgressStore.AllLevels = levels` at `BattleRunner.cs:171`. No coins are ever earned, no stars
ever recorded, no daily bonus ever paid. Tier 0.3/0.4 is therefore mostly a call site plus a
presentation layer, not a new system.

**Worse: three things are imported and ignored.**

| Imported | Read at runtime? |
|---|---|
| `StageDefinitionSO` × 4 (star gates 0/3/6/9) | **No.** Nothing reads stages at all. |
| `deployBudget` on every level | **No.** Player squads come from hardcoded `playerGroups`. |
| `ReinforcementWave.telegraphText` / `announcement` | **No.** The waves fire; nothing announces them. |

The level list is one flat serialized array of 24 — 7 campaign plus 17 rigs — collected by
`SpikeSceneBattle` via `FindAssets("t:LevelDefinitionSO").OrderBy(levelNumber)` and driven by the
◀▶ debug stepper. `isTestLevel` is set correctly and `ProgressStore.TotalStars()` already excludes
rigs, but nothing keeps a player off the unit parade.

---

## Phase order and why

```
A. Data authoring → Unity      (unblocks D; must precede it or D is authored twice)
B. Campaign / test-rig split   (small, removes a recurring class of churn)
C. Victory screen + coins felt (0.3 / 0.4a / 0.5 — the only phase that pays off with zero content)
D. Campaign content to 12      (0.1 / 0.2 — the bulk of the calendar time)
E. Loadout and the first purchase (0.4b — a real build, not a wiring job)
F. Enemy turn juice            (0.6 — self-contained, slots anywhere)
```

**C can be pulled to the front** if you want the loop closed before anything else — it has no
dependency on A, B or D. The order above puts A first only because authoring 12 levels through an
export step from a retired repo and then migrating them is the one genuinely wasteful sequence.

---

## Phase A — move data authoring into Unity — DONE 2026-08-06

**Status: DONE.** Went as planned, with one thing the plan got right for the wrong reason: A1's
"final import" turned out to be unnecessary — re-running the exporter produced a byte-identical
`data.json`, so the assets were already current and nothing had to be migrated at all. The whole
phase was A2's disarming plus A3/A4.

Delivered: `LegacyKotlinImport.ImportOnce` behind `-iAcceptDataLoss` (sweep removed, sandbox
side effect removed), `SandboxLevels.Generate` (verified byte-identical output),
`LEVEL_AUTHORING.md`, `LevelDefinitionSO.designNotes`, and `LevelComposition.Report` + a live
inspector sharing one set of thresholds.

The validator found five warnings and one lock violation across the seven shipped campaign levels
— see `HANDOVER.md`. Not fixed here; that is Phase D.

The original plan text follows.

---


The pipeline works; the reason it exists is gone. The hazard is not the export step, it is that
`DataImporter.Import` **overwrites hand-authored assets in place** — so the moment Unity becomes
the authoring surface, the importer changes from a tool into a loaded gun.

**A1. Take the final import.** Run `export_kotlin_data.py` + `DataImporter.Import` once more,
confirm 24 levels / 8 units / 4 stages, run `PortSelfTest.Run`, commit. That commit is the
watermark: everything after it is Unity-authored.

**A2. Disarm the importer.** Three edits, in this order:

- **Delete `Sweep`.** It removes any asset the Kotlin no longer declares. That behaviour is what
  makes Kotlin authoritative; under Unity authorship it is a data-destroying bug that eats every
  new level on the next accidental run.
- **Extract `BuildSandboxLevels`** (51 lines) into its own editor menu command. Today it runs as a
  side effect of import, which is precisely why the level list comes from two places — the hazard
  `PortSelfTest`'s `levelNumber == index + 1` check exists to catch. As a standalone regenerator it
  stops being a hazard.
- **Rename `DataImporter.Import` → `LegacyKotlinImport.ImportOnce`** and have it refuse to run
  without an explicit `-iAcceptDataLoss` argument. Documenting "never re-run this" is what we have
  now, and CLAUDE.md has carried that warning for months; a guard is cheaper than the incident.

**A3. Rehouse the design commentary.** `LevelDefinition.kt` is 1021 lines and a large fraction is
reasoning, not data — including the six composition rules at the top of the campaign block that
CLAUDE.md tells every session to read. Two destinations:

- The six composition rules → a new `LEVEL_AUTHORING.md` at repo root.
- Per-level rationale → a `designNotes` string field on `LevelDefinitionSO`, so it sits in the
  inspector where the author is actually working.

Without this step the commentary is stranded in a repo nobody opens.

**A4. Build the authoring tool — the highest-leverage item in the phase.** Hand-editing `.asset`
YAML for 12–18 levels is not viable. A custom `LevelDefinitionSO` inspector that renders the six
composition rules as **live validation** turns them from prose into a gate:

- Aiming framing of the player line ~6 wide
- Scout/resolve framing from the enemy cluster including structure edges, under ~11
- One dominant structure, at most two small supports
- 14–18 units of separation, TANK → dominant structure
- Majority of the enemy roster garrisoned on structures
- `levelNumber` contiguous within the campaign

Every one of these is computable from the asset. This is what makes Phase D fast instead of
fiddly, and it is why A precedes D.

**A5. Update the record.** `CLAUDE.md` ("Game data is authored in KOTLIN — ONE WAY"),
`HANDOVER.md` (close the open question), `PRODUCT_DIRECTION.md` implementation note 3, and the
`project-unity-data-pipeline` memory, which currently says the opposite of what will be true.

**Verification.** The migration re-parses nothing — the assets already exist and are correct, and
the exporter's hard-won parsing fixes (`FortressTier` dropped, `Capture` losing optional fields,
ARGB losing its low byte) are baked into the current assets. So: `PortSelfTest.Run` must produce an
identical result before and after A2, plus a device sweep of all 24 levels.

---

## Phase B — split the campaign from the test rigs — DONE 2026-08-06

**Status: DONE**, and confirmed on device. Simpler than planned: done with ONE array ordered
campaign-then-rigs rather than the two serialized arrays B1 proposed, which avoids two indexing
schemes. The stepper is gated by a runtime `RIGS` toggle rather than a build flag, because the
rigs must stay reachable in a release build. See `HANDOVER.md`.

The original plan text follows.

---


**B1.** `SpikeSceneBattle` emits two serialized arrays instead of one: `campaignLevels[]`
(`!isTestLevel`, ordered by `levelNumber`) and `testLevels[]`.

**B2.** `BattleRunner`'s campaign path walks `campaignLevels`. The ◀▶ stepper stays — it is the
only way to sweep every level from adb without a three-minute rebuild — but it moves behind a
dev-build flag so a player can never land on the unit parade.

**B3. Retire the global `levelNumber == index + 1` invariant.** It currently spans all 24, which is
why CLAUDE.md has to warn that "test levels must be renumbered whenever the campaign changes size."
After the split, campaign levels number 1..N and rigs carry no campaign ordinal. `PortSelfTest`
asserts contiguity **within the campaign only**. That deletes a whole recurring class of churn
right before Phase D changes the campaign size by 5+ levels.

---

## Phase C — victory screen and coins felt (0.3, 0.4a, 0.5) — BUILT 2026-08-06

**Status: DONE.** Self-tests pass (314), release build clean, and **confirmed on device** — L1
driven to a 3★ victory paying 230 coins, card rendered at a steady 60 fps, NEXT tapped and L2
loaded, balance carried across. See `HANDOVER.md`.

Shipped: `TurnFlow.SurvivorsFor` / `StarReason`; `BattleRunner.ResolveBattleEnd` (the call site);
`ArmedConflict.UI.BattleUI` (canvas, coin pill, victory/defeat cards, the beat sequence);
`BattleUIPreview` (headless render harness); `tools/import_tmp_essentials.py`. Six new self-test
checks, 314 total, all passing. The IMGUI RESTART / NEXT buttons are gone — the card owns them.

Decisions taken while building, that the plan did not anticipate:

- **The UI is built in CODE at runtime, not as a prefab.** No serialized references means no scene
  rebuild for any UI change, which matters more here than inspector editability nobody uses.
- **The card waits ~1.1s before rising**, so the killing blow is not covered by a dim overlay the
  frame it lands.
- **The card yields to the free camera** (`SetVisible`), because inspecting a finished battle is
  most of what that tool is for.
- **Nothing outside ASCII may appear in a TMP string** — see the trap note in `HANDOVER.md`.

The original plan text follows.

**C1.** Add `com.unity.ugui` and TextMeshPro to `manifest.json`. Build a `BattleUI` prefab: a
persistent HUD (coin balance, level name, turn state) and victory / defeat panels. Because this is
prefabs and serialized references, **every UI change needs a scene rebuild** — expect that rhythm.

**C2. Wire the call site.** `BattleRunner.cs:732` already detects the `Playing → Victory` edge to
play the victory sting. Same edge, same pattern:

```
TurnFlow.AwardVictory(level, state.PlayerUnits.Count, state.InitialPlayerCount)
TurnFlow.AwardDefeat(level)                                    // on the Defeat edge
```

Guard it to fire exactly once per battle. `battleId` already advances per load, so key the
"awarded" flag on it. A replay *should* pay again — the one-time parts are handled inside
`GrantVictoryPayout` by `previousBestStars`.

This is the single highest-value change in Tier 0: one call site turns a fully-tested dead economy
into a live one.

**C3. The victory sequence.** Stars fill one at a time → coin count-up → bonus banner
(`VictoryAward.BonusTag` already yields "First Clear!" / "New 3★ Best!" / "Daily Bonus!") →
next-goal teaser → CTA (**NEXT** / **RETRY FOR 3★**).

**C4. Star reasons (0.5) come nearly free.** `StarsFor` is pure survival fraction with legible
thresholds, so the reason is always sayable out loud: *"Lost 4 of 14 — keep 11 alive for 3★."*
Shown on every 1★ and 2★ result. There is no RNG and nothing opaque to fix; 0.5 is satisfied by
printing what the formula already is.

**C5.** Coin balance persistent in the HUD, animating on grant.

**C6. New `PortSelfTest` checks**, per `PRODUCT_DIRECTION.md` note 2 (lock product invariants):
award fires exactly once per victory edge; first-clear pays only when `previousBest == 0`; the
star-reason string agrees with `StarsFor`'s thresholds.

---

## Phase D — campaign content to 12 levels — DONE 2026-08-06

**Status: DONE.** 12 levels, 0 composition warnings, swept on device. Two systems turned out to be
dead and were wired or dropped — wind is cosmetic (beats 7/8 re-cut onto real variables), and boss
phases and reinforcement waves had never fired. See `HANDOVER.md`.

D3's onboarding work is only PARTLY done: L1-3 teach in the right order and L1 pays visibly, but
"default squad = Begin in one tap" needs the loadout screen, which is Phase E.

The original plan text follows.

---


The bulk of the calendar time. Author against `PRODUCT_DIRECTION.md`'s beat chart: 7 existing biome
levels re-jobbed into the funnel, ~5 new. Biomes repeat — that is explicitly an art constraint, not
a content limit.

**D1.** Map the 7 survivors onto beats, identify the gaps, author the remainder with the Phase A
validator.

**D2. Rebuild the stage data.** Four stages currently hold 1–2 levels each; `GAME_DESIGN_LOCKS.md`
locks stages at ~6–7. Twelve levels = two stages of six. Stages also need to become *visible* —
the lock requires locked stages stay browsable (name, tagline, preview; only Begin gated), and
nothing reads `StageDefinitionSO` today.

**D3. Onboarding (0.2).** L1–L3 non-negotiables: first multi-kill and first structure collapse
early and on camera; L1 victory pays coins visibly and shows a next goal; default squad = Begin in
one tap.

**Verify with real drags on device.** `Auto` targets the nearest enemy unit with no jitter — it
kills rather than wounds and cannot test structures at all, so it cannot measure difficulty or
onboarding pacing.

**Two standing rules get reversed by this phase and must be rewritten, not quietly ignored:**
CLAUDE.md's *"ONE LEVEL PER BIOME — a new biome gets one level, not an arc"* and *"Levels are
disposable test rigs. Polish mechanics; do not tune levels."* Both were right for a mechanics port.
Neither survives a product campaign. The `project-levels-are-scrap` memory says the same and needs
the same correction.

---

## Phase E — loadout and the first meaningful purchase (0.4b) — DONE 2026-08-06

**Status: DONE**, confirmed on device, with one item explicitly NOT done: the balance audit. See
`HANDOVER.md`. Slots (fixed, from the level) and points (`deployBudget`, buying quality) turned
out to be the design that makes a loadout safe against composition rule 1.

The original plan text follows.

---


0.4 asks for "something to buy that changes the next battle." That is a real build, not wiring:

- `deployBudget` is imported and **ignored** — player squads come from hardcoded `playerGroups`.
- `RosterEntry` exists in `EconomyStore` but has **no data behind it**; `data.json` carries no
  roster, so per-unit `PointCost` / `CoinPrice` / tier costs must be authored fresh (a
  `RosterDefinitionSO`, now that Unity is the authoring surface).
- The screen itself is `HANDOVER.md`'s open item 3, ~415 lines.

Constraints from `PRODUCT_DIRECTION.md`: loadout auto-fills so skippers lose nothing, `deployBudget`
stays tight to authored cost, the 7–30 structural clamps stay, and **a level that breaks under a
legal loadout is a product bug** — so this phase owes a sanity pass across the extremes, which was
deferred historically and never done.

---

## Phase F — enemy turn juice (0.6) — DONE 2026-08-06

**Status: DONE**, confirmed on device. Two channels (flash banner, standing telegraph strip) and a
threat-naming turn handover. See `HANDOVER.md`.

The original plan text follows.

---


Self-contained; no dependency on any other phase. The windup already exists
(`enemyWindup` / `TurnFlow.EnemyWindupSeconds`). What is missing is that **the game never says
anything**: `ReinforcementWave.telegraphText`, `ReinforcementWave.announcement` and
`BossPhaseTrigger.announcement` are all imported, all populated, and never displayed.

- Event popups on the Phase C canvas: "Reinforcements next turn", "Bunker destroyed", "Wind rising →"
- A readable threat telegraph before the enemy volley
- Charge readability for `advancePerTurn` so melee pressure reads as approaching, not as teleporting

This also clears three of the juice-checklist items for the cost of one UI component.

---

## Sequencing summary

| Phase | Blocks | Blocked by | Rough shape |
|---|---|---|---|
| A — data → Unity | D | — | **DONE** — the migration was a no-op; the work was disarming the importer |
| B — campaign split | D | — | **DONE** — one ordered array, a RIGS gate, a test change |
| C — victory + coins | — | — | **DONE** — one call site + the first uGUI canvas |
| D — 12 levels | — | A, B | **DONE** — plus wiring two dead event systems |
| E — loadout | — | C (canvas) | **DONE**; balance audit arithmetic half done (rule 7 + L7 fix), device drags still owed |
| F — enemy juice | — | C (canvas) | **DONE** — two channels + a threat-naming handover |

Standing rules for every phase: `PortSelfTest.Run` after each change; scene rebuild whenever
prefabs, materials or serialized references move (all of Phase C); verify feel on device with real
drags, never `Auto`; minimal diffs; commit only when asked.
