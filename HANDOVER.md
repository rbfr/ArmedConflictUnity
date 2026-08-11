# Handover — Unity, as of 2026-08-11 (second session that day)

## START HERE

- **EVERYTHING IS COMMITTED AND PUSHED**, as of the close of 2026-08-11's THIRD session — Tier
  2.2's hero staging and deck-overlap fix went up as one commit. The SECOND session's twenty
  commits (2026-08-10's airstrike arc, 2026-08-11's airstrike rework, Tier 2.1 factions and Tier
  2.4 camo) went up before it.
  **`git status` and `git log --oneline origin/main..HEAD` are the answer, never this bullet** —
  it has been wrong before and the previous version of it claimed the opposite for a day.
  Rob commits and pushes on an explicit ask.
  The Android repo's `projectile-refinement` is never being merged; **the Android build is
  RETIRED**, reference only.
- **TWO SESSIONS RAN ON 2026-08-11 and this file distinguishes them by subject, not by date.**
  The first rebuilt the airstrike ("What 2026-08-11 changed", below) and Rob signed it off. The
  second built Tier 2.1 and 2.4. Anything dated 2026-08-12 in an older revision of these docs was
  mislabelled and has been corrected.
- **ALL OF TIER 0 IS DONE AND SIGNED OFF.** The Phase E balance audit — the last thing it owed —
  was run on 2026-08-07 in both halves, and Rob played the campaign afterwards and reported the
  levels feel fine. That closed it.
- **Tier 1.1 (ammo types) IS BUILT** and confirmed on device. Its last owed piece, the **FLAME on
  a burning unit, shipped 2026-08-09 and was confirmed on device on 2026-08-10.**
- **Tier 1.2 is HALF DONE**: reinforcement waves are telegraphed with a live countdown and the
  schedule covers two levels. **Wind is the other half and is still blocked** — see below.
- **TIER 1.3 IS BUILT** — four consumables, bought, carried and fired, confirmed on device
  2026-08-10. **Overwatch Flare is deliberately not among them**; see its section.
- **THE AIRSTRIKE HAS AN AIRCRAFT** (2026-08-10), rebuilt almost entirely on 2026-08-11. It cuts
  the camera to the strike, enters across the LEFT EDGE, rakes the WHOLE ENEMY POSITION with
  tracer streaks, and its bomb LANDS WITH the player's volley rather than before it.
  **Rob signed it off: "ok this will work."** See "What 2026-08-11 changed".
- **TIER 2.1 (ENEMY FACTIONS) AND TIER 2.4 (PLAYER CAMO) ARE BOTH BUILT** and both
  device-confirmed. Factions: L1's Redguard red, L7's Ironclad Legion steel blue-grey, and L1 red
  AGAIN after stepping back — the third of those is the evidence, not the second. Camo: Arctic
  White on the infantry and the tank crew with the tank itself unchanged, and Olive back the moment
  RIGS was switched off. See both sections below. **NEITHER HAS BEEN SEEN BY ROB** — every device
  confirmation in them is mine, not his.
- **RIGS IS THE TEST SUPPLY FOR BOTH CONSUMABLES AND CAMO** — every item free to equip, every camo
  free to wear, nothing spent and nothing written to the economy or the wardrobe. Use it: the
  release build is not debuggable, `run-as` cannot reach PlayerPrefs, and the test protocol is
  uninstall/reinstall — so without it, verifying one consumable costs a ~250-coin re-earn per
  build and one camo costs up to 400.
- **608 self-test checks, all passing — run `PortSelfTest.Run` after every change.** It was 281 at
  the start of 2026-08-06, 411 at the end of it, 444 on 2026-08-07, 539 after the Tier 1.2 and
  glyph-coverage blocks, 559 with the flame and the Auto-ammo pair, 576 with Tier 1.3's
  consumables, 582 with the airstrike's aircraft, 585 with its strafing burst, 587 with the burst's
  absolute count-and-budget check and the aircraft's left-edge entry, 592 with 2026-08-11's
  rake-coverage, aim-independence, whole-burst and impact-alignment checks, 599 with Tier 2.1's
  seven faction checks, 606 with Tier 2.4's camo block, 607 with Tier 2.2's hero-staging check, and 608 with its deck-overlap check. **Assert related facts TOGETHER** —
  Tier 1.3's block was first written as 50 assertions over 307 lines and is 18 over 232, with the
  same nine breakages still caught. A failure message naming three properties is as diagnostic as
  three checks, and this file is read by people.

### What the SECOND 2026-08-11 session changed — Tier 2.1 and 2.4, the two repaint features

Both are in their own sections below. What belongs at the top is the cross-cutting lesson, because
it is the fifth session running to produce one:

**A CHECK CAN BE UNFALSIFIABLE FOR MORE THAN ONE REASON AT A TIME, and fixing the first one does
not make it a check.** The camo vanity check — same seeded volley under two camo sets, demanding
identical damage — passed against a build where the camo really did buff damage 50%. Twice:

1. the volley never landed, so both runs did zero damage and zero equals zero; then
2. once that was fixed, the set was never actually WORN — `SelectedCosmetic` validates on read,
   so selecting a set the player does not own silently returns Olive, and it was comparing Olive
   with Olive.

Only after both did it read `enemy 280/276`. **Fix one hole and re-run the breakage, do not assume
the check is now live.**

**AND A CHECK CAN INDICT THE WRONG THING WHEN ITS OWN METRIC IS NEW.** The faction distinctness
check began as a luma-weighted rgb distance, which weights blue at 0.11; it scored Ironclad's steel
blue-grey at 0.082 from the player's olive green and failed a palette the Kotlin build shipped and
played fine. The metric was three hours old and the palette was three weeks old. It is an
opponent-colour distance now, and only a coarse floor. Same family as the "ASCII only" glyph check
that flagged 23 strings a device screenshot then showed rendering perfectly: **be suspicious when a
brand-new check indicts long-standing, apparently working content.**

**A THIRD ONE, about where checks can reach at all.** `PortSelfTest` does not drive MonoBehaviours,
so it tested `Cosmetics.TestOverride` rather than the tile's tap handler — and a test supply that
quietly UNLOCKED a camo for real passed every check in the file. `BattleUIPreview` is the only
harness that drives real uGUI; it now taps the tile and asks the STORE whether anything moved, and
it caught that breakage on the first run (`unlocked 0->1, worn Olive->Arctic`).

### What the FIRST 2026-08-11 session changed — the airstrike, rebuilt in five passes

**Signed off by Rob: "ok this will work."** The whole session was one loop — build, put it on the
device, let Rob look at it, be told what was actually wrong. **Every single pass was rejected for a
reason a green test suite could not see, and three of my own checks were worthless when written.**
That is the lesson worth carrying, more than any constant below.

| Rob said | What was actually wrong | Fix |
|---|---|---|
| "i don't really see a difference — looks like only one" | Round COUNT was never the bottleneck. A 0.22-scale dot at 25 u/s covers a fifth of the gap it opens between frames, so 7 and 14 draw the same dotted chain | `IsStrafe` + a **tracer STREAK** — 4.5x along flight, 0.7x across |
| "the plane should come from the left... it seems to just appear in the middle" | Not the spawn. The camera began the run over the player line and **swept past** the aircraft | The run **CUTS** to its anchor and holds. `CAMERA_ARCHITECTURE.md` exception, asked for and granted |
| "the strafe should spread further horizontally" | 4 units inside a ~10 unit frame reads as a cluster | Walk widened; `PlaneRunHalfLength` pays for it |
| "it's not hitting the structure" | The walk ENDED on the aim point, approaching from the left — every round but the last landed SHORT of whatever you aimed at | Then superseded ↓ |
| "the strafe is independent of the player unit volley... cover the whole enemy position and its structures" | The rake was defined relative to the AIM, so its ground moved with every drag. Every fix above was tuning an offset from the wrong origin | **`StrafeSpan`** — enemy units + structure EDGES, carried on the aircraft |
| "sync the player projectile volley with the plane. it's a little awkward" | The two halves were ADDED: 4.53s on an ordinary shot, a third of it watching a plane with none of the player's rounds in the air | Aligned on their IMPACTS — `max(flight, run)`, 2.91s |

**The design rule that fell out of it, and it is the one to keep:** the BOMB belongs to the player's
aim; the GUNS belong to the enemy's position. Those are different origins and conflating them is
what produced four of the six rejections above.

**Everything the aircraft does now lives on the ALWAYS-RUN physics path** — motion, guns, and (as of
this session) the bomb release. Three separate things have had to move out of `TurnPhase
.AirstrikeRun` after freezing or silently dropping work when the phase ended. **Assume the fourth
will too**: the run is a phase whose subject deliberately outlives it.

**Numbers that matter now:** `StrafeRounds 28` at `StrafeDamage 1` (budget held at 28, item total
52), `StrafeMargin 1.5`, `PlaneRunHalfLength 11` as a FLOOR not a spawn, `StrafeRoundStretch 4.5`.
**The beat is no longer one fixed length across the campaign** — it is derived from the enemy's
width and the shot's flight time, so a wide level holds longer than L1's ~1.4s.

### The three checks that were WORTHLESS when written, all in one session

Each passed against the exact broken code it was written for. All three failed for the same reason:
**the check was not in a state where its failure was reachable.**

1. **The camera-entry check.** `fresh` has never ticked, so `CameraFollowX` is null — the spring
   then begins AT the anchor and sweeps past nothing. Seeded onto the player line, it went red with
   `camera -7.44 (anchor 9.42)`, which is Rob's bug in numbers.
2. **The whole-burst check.** Written with the block's standard aim, which lands PAST the enemy —
   so the rake finished before the bomb and a phase-bound firing loop dropped nothing. Re-pointed
   at an aim landing SHORT (the ordinary case) it read `17/28`: eleven rounds dropped in silence.
   It now asserts the aim IS short as part of its own condition.
3. **The "spawned off-frame" claim**, inherited from 2026-08-10 — a message naming a property about
   the FRAME that the assertion never looked at.

**This is now four sessions running.** With the empty-purse check and the `ReferenceEquals` refusal
test, the standing rule has earned its place in `CLAUDE.md`: **ask what STATE the failure needs to
be reachable in, then put the check in it** — and never trust a new check until you have watched it
go red.

### And two things only the DEVICE found, both from the same cause

The aircraft is now HELD BACK while the volley flies, and two things were still anchored to the
release:

- **The pass-by sound played over empty sky**, a second before the plane existed. Now on the
  true->false edge of `AirstrikeSpawnDelay`. The clip's peak is cut to land as the plane crosses its
  drop point and that offset is measured from the START OF THE RUN — anchor it anywhere else and
  the peak is thrown away silently.
- **The release log said `volley held` when the volley was already away.** THIRD false reading from
  that one line (`volley: 0 rounds`, then strafe tracers counted as volley rounds, now this). It is
  the only instrument a release build has. It reports the three real cases now.

**Neither was visible to any check, and both were caused by a timing change three files away.**
After touching this beat, fire one on a device and read the log AND listen.

### What THIS session changed — Tier 2.2, part one

**The heroes were never composed.** Every hero group in the campaign was authored ONTO a
structure in counts of 4-5 (L6 x4, L7 x5, L11 x4, L12 x5), so `FormationFor`'s garrison branch
won every time and **`Formation.Heroes` — the entire "stands apart, individually" path — was
reached by exactly one thing in the game**, L10's turn-4 reinforcement wave. Not a bug in the
function; nothing called it. The engine half of crowd-vs-hero has been finished for months.

**`LevelComposition` passed all twelve levels the whole time**, because a hero is a legal garrison
member and spans, reach and garrison-majority were all satisfied. The seven rules measure
geometry, not casting. This is the shape to remember: **a green rule-checker is evidence about the
rules it has, not about the thing you are looking at.**

Heroes now stand on the GROUND in front of their structure, 1-2 per level, z 0.4 forward (free —
`SweptCollision` is x/y only). Surplus heavies swapped 1:1 for enemy riflemen in the garrison they
left, so **every roster total is unchanged and enemy damage output is identical**; the only
balance movement is enemy HP, **L6 -64, L7 -96, L11 -96, L12 -96**. Four levels clear slightly
faster. L11 fields ONE hero on purpose: two would have dropped its garrison to exactly half the
roster and broken rule 5.

**A third assertion died to measurement.** "Heroes stand forward in z" is FALSE — L12's deck
garrison sits at z 0.80 against the hero's 0.34, because `deckStandZOffset` and a staging offset
share an axis and mean different things. It is not in the check; asserting it would have asserted
a belief.

**A SECOND, OLDER DEFECT FELL OUT OF THE SAME AUDIT: two groups garrisoned on one structure
stood INSIDE one another.** `FormationFor` laid out each authored group separately and every one
centred on the same deck — L11's three riflemen and three machine gunners occupied an identical
`5.81..6.19`, dx 0.000 dz 0.000. Six men rendering as three. `LevelBuilder.DeckSpots` lays out
each deck ONCE across all its groups now, first group in the front rank and the next behind it.
**A reinforcement wave still builds in isolation and cannot see who is already up there** — no
campaign wave garrisons today, and the old path is kept for that case.

**The overlap detector was wrong before the code was right.** Its first version compared x-RANGES
per group and called L11 broken AFTER the fix landed, because a back rank legitimately spans the
same x as the front one. It is Chebyshev now. **A detector that reports a failure needs checking
for what it is actually finding, exactly as one that reports nothing does.**

**DECK FILL is measured and left alone deliberately**: garrisons occupy 12-56% of their deck, most
of them 12-25%, because the body shrank 0.77 -> 0.48 while `standWidth` is real structure geometry
and did not. Pitch is correct — it was derived against the reference in 2026-08-02. **Filling a
tier is a roster-size question and therefore a balance one**, so it is written up in
`UNIT_VARIETY_DESIGN.md` rather than applied. Do NOT fix it by spreading the row.

**608 checks.** `CheckHeroStaging` builds every campaign level and measures what a player would
see. Its negative run against the old data, per the standing rule:
`18 across the campaign, biggest group 5, 18 on a deck, 13 inside the 0.76 clearance floor,
tightest 0.00 on L12` — **0.00 is a hero at the same x as a crowd body**, interleaved in the
citadel's rifle row.

**The CONTROL SHOT was taken** — same drag, same frame, on a build carrying the old level data.
Before: four oversized red figures crowding the keep roofline as one lump, taller than its own
crenellations. After: two greatcoated heroes alone at the base, small crowd on the roof.
**Rob has not seen it.**

### Pick up here

**WIND IS PARKED** — Rob's call, 2026-08-10. It is the only thing Tier 1.2 still owes and it is
blocked on a physics decision (below), so tier work continues around it rather than waiting on it.

0. **NOTHING IS HALF-BUILT AND NOTHING IS UNCOMMITTED.** The session closed on a clean tree, a
   pushed `main`, 606 green checks and two features Rob has not yet looked at. **The first thing
   worth doing is putting the build in his hands** — factions read on the stage change, camo reads
   on the loadout screen, and both are the kind of change only he can accept.
   `_plans/AIRSTRIKE_PLANE.md` is finished and can be archived per `_plans/README.md` whenever
   someone is tidying.

   **TIER 2.1 (FACTIONS), 2.4 (PLAYER CAMO) AND THE HERO HALF OF 2.2 ARE DONE** (2026-08-11).
   **2.2 still owes the CROWD half** — this session fixed which units are staged as heroes and
   where they stand, which is not what 2.2's entry in `PRODUCT_DIRECTION.md` names. Before
   spending a pass on crowd readability, read `UNIT_VARIETY_DESIGN.md`'s "honest limit" twice:
   three attempts (stance, faces, limb fold) each cleared "is it correct" and failed "does it
   survive the frame", and the only two changes that ever DID read were large-scale layout.
   **2.3 keep the roster mechanic-distinct** is still unclaimed and is likely a short audit
   rather than a build.

   **The player has no hero anywhere, and that is now a decision** (Rob, 2026-08-11:
   enemy-only for now). `HeavyRifleman` is not among the six pickable roster slots and no
   campaign level puts one in `playerGroups`. Revisit only with the build in hand.

   **The two biggest OPEN things in the port are named and costed, and both are physics/AI asks
   rather than scheduling jobs:**
   - **Advancing squads + melee are unported** — an EIGHTH dead system, and the one that holds
     Overwatch Flare. `AdvanceRemaining` is written nowhere and `SkirmishEntity` is never created.
     `PROGRESSION_DESIGN`'s whole survival/defend archetype is made of this.
   - **Wind is still cosmetic** — `windAccelZ` drifts the round in Z while the collision test is
     X/Y only, so wind cannot change what a shot hits.

   **THE AIRSTRIKE IS DONE AND SIGNED OFF** (2026-08-11, "ok this will work"). Read "What the
   FIRST 2026-08-11 session changed" before touching it — six rejections in one session, none of
   which a green suite could see.

   **One SMALL open note, and one now CLOSED:**
   - The aircraft *"gets fairly large as it passes nearest the camera"* — brief and arguably the
     point, but `BattleTick.PlaneY` is a one-constant fix if it reads as too much. Judge it at full
     speed, not on a contact sheet.
   - ~~The pass-by sound's offset has never been checked by ear.~~ **Rob's call, later on
     2026-08-11: "the aircraft sound is fine for the moment."** Do not spend a session on it.

   **The device was left with the app installed and 230 coins earned by Auto**, RIGS off, nothing
   written to the wardrobe, and the screen timeout raised to 30 minutes. The coins are a test
   install's, not a save worth protecting — the protocol is uninstall/reinstall anyway.

<details>
<summary>The Tier 1.3 briefing as it stood before the work — kept because its reasoning is the
standing lesson, not because it is current</summary>

1. **TIER 1.3 IS THE NEXT TIER ITEM, AND ITS FIRST HALF IS NOT WHAT THE TABLE SAYS.**
   `PRODUCT_DIRECTION.md` calls 1.3 "tactical consumable expansion — Smoke / Overwatch", gated on
   *"when base consumables already feel good"*. **In this port there are no base consumables at
   all.** `ConsumableType`, `ProgressStore.OwnedConsumables/AddConsumable/SpendConsumable`,
   `EconomyStore.PurchaseConsumable` and `GameState.LoadedConsumables` are all ported — and a grep
   for callers outside those files returns **NOTHING**. No UI, no arming, no spend, no effect.

   **This is the SIXTH dead system** (see "assume NOTHING is wired"). The design docs say
   "shipped" because their Status tables record the **RETIRED ANDROID BUILD** — `PROGRESSION_DESIGN`
   Phase 2 and `DYNAMISM_DESIGN` Phase C both shipped there on 2026-07-20. **Check the Unity
   callers, not the status table**, and treat those docs as the SPEC for what to build here rather
   than as a record of what exists here.

   So 1.3 is really: **wire the three base consumables (Airstrike / Early Reinforcements / Trauma
   Kit) first**, then Smoke / Overwatch on top. The specs are `PROGRESSION_DESIGN.md` Phase 2 and
   `DYNAMISM_DESIGN.md` Phase C, including the cap-2-per-battle rule and the arm-vs-spend
   distinction (Airstrike's toggle pattern, not Trauma Kit's).

</details>

2. **Tier 1.4 (Heli) stays shut.** `HELI_ENABLED=false` is a camera-load decision, not a stale
   flag, and `PRODUCT_DIRECTION` gates it on "camera choreography is boring-stable". Do not flip it.

3. **Tier 1.1 is CLOSED except for two UNRESOLVED items**, both tracked in `_plans/BACKLOG.md`:
   **is Cluster's 3.2x spread too wide to connect?** (needs Rob at the controls — a scripted drag
   cannot settle it), and **flames outlive their bodies by a frame or two** at the moment the burn
   kills. The flame Tier 1.1 owed shipped and was confirmed on device on 2026-08-10.

4. **Wind, when it is picked back up**, is a physics ask, not a scheduling job. `windAccelZ` drifts
   the round in Z while the collision test is X/Y only, so wind cannot change what a shot hits. A
   wind schedule would telegraph a change the player cannot feel, which is worse than no wind.
   **Do not author a wind level or a wind schedule until someone decides whether the collision test
   becomes 3D.**

5. ~~A per-frame NullReferenceException on the LOADOUT screen.~~ **FIXED 2026-08-10** — it was the
   tick running with no battle to tick. See its own section below. That screen is now clean, which
   matters because the consumable UI 1.3 needs will be built on it.

6. **`_plans/BACKLOG.md`** holds what Rob has parked: a **nuclear reactor structure** (open
   question is what MECHANIC it owns — a blast on destruction would make it the first structure
   with one), **dead units sinking** into the ground instead of vanishing, and the **ragdoll /
   structure report**, which is PARTLY fixed and deliberately left open.

## Enemy factions — Tier 2.1, built 2026-08-11

**Two stages, two armies**: Redguard (Valley Front) is the existing enemy red, UNCHANGED, and
Ironclad Legion (Enemy Stronghold) is steel blue-grey. The full reasoning — what a faction may
touch, why the data lives in `Assets/GameData/Factions` rather than in the UI layer as the Kotlin
had it, and why only two — is in `DYNAMISM_DESIGN.md`'s "Phase D1 in UNITY" section. What belongs
here is the traps.

**IT IS A POOL RESET, NOT A PAINT.** Pools are built once and survive a level switch, so the enemy
is repainted in `BattleRunner.ApplyFaction` on every `LoadLevel`, beside the scorch re-material and
`TintShadows`. The failure mode is the one this repo already paid for in scorch marks, shadows and
structure chunks: a recycled slot wearing the PREVIOUS level's colour. **A single paint cannot show
it — the device run is L1 red → L7 blue → L1 red again, and the third leg is the evidence.**

**Renderers are classified against the two build-time MATERIALS, not against the `skin*`/`trim*`/
`accent*` mesh-name prefixes.** That convention belongs to `RiggedUnits.Tone` in the art pipeline,
and a second copy of it at the render end is a copy that can disagree with the first. The
classification runs once with the pools; the per-switch cost is a list walk.

**`FactionPaint.Recolour` CLONES the material.** Tinting the source in place edits
`Assets/Materials/EnemyUniform.mat` ON DISK — the negative run proved it, leaving both .mat assets
modified in `git status` — and every faction then shares whichever colour was applied last, in the
editor and in every build after it.

**What is asserted and what is NOT.** The seven new checks cover the lookup (12/12 campaign levels
field a faction, 0/17 rigs do), the palettes being visibly different armies, and the repaint itself
on the SHIPPED rifleman prefab through three successive paints. **They do not cover the call
site** — `PortSelfTest` does not drive `MonoBehaviour` frame callbacks, so "ApplyFaction is called
on every LoadLevel" is device-verified only. Same considered gap as the loadout NRE guard.

**The distinctness check was WRONG when written, and it is the lesson of the session.** It began as
a luma-weighted rgb distance, which weights blue at 0.11; it scored steel blue-grey at 0.082 from
the player's olive green — under its own threshold — and indicted a palette the Kotlin build
shipped and played fine. **The metric, not the palette, was the thing that was three hours old.**
Equal-brightness opposite hues are trivially told apart, and hue is the axis the whole feature
works in. It is an opponent-colour distance now and is deliberately only a coarse floor. This is
the same family as the "ASCII only" glyph check that flagged 23 strings a device screenshot then
showed rendering perfectly: **be suspicious when a brand-new check indicts long-standing content.**

**All three negative runs are recorded**, per the standing rule:

```
[FAIL] the shared EnemyUniform/EnemyGear ASSETS come out of it unchanged
       (RGBA(0.270, 0.330, 0.420) was RGBA(0.520, 0.200, 0.180))   <- Recolour tinting in place
[FAIL] the rifleman splits into uniform / gear / neither (5 / 6 / 0 renderers)
                                                          <- skin+trim swept into the repaint
[FAIL] every campaign level fields a faction and no rig does (12/12 campaign, 17/17 rigs)
                                                          <- lookup ignoring stage membership
```

**A scene rebuild was required** — three new `[SerializeField]`s on `BattleRunner` (`stages`, and
the two enemy side-materials the classification keys on). The materials must be the SAME asset
references the enemy prefabs were toned with; reference equality is the whole mechanism.

## Player camo — Tier 2.4, built 2026-08-11

**The NINTH dead system** (factions, the other half of this session, were ABSENT rather than
dead — see "assume NOTHING is wired"). `CosmeticSet`, `ProgressStore`'s cosmetic block and
`EconomyStore.PurchaseCosmetic` were all ported and reached by nothing. Four sets now: Olive Drab
free, Desert Tan 300c, Urban Grey 350c, Arctic White 400c, bought and worn in a strip below the
consumables. Design detail is in `DYNAMISM_DESIGN.md`'s "Phase D4 in UNITY"; the traps are here.

**It rides the faction repaint, pointed at the other army.** Same `FactionPaint` classify-once /
apply-on-switch machinery, so the same pool-reset reasoning applies unchanged.

**Olive stores NO colour and that is deliberate**: selecting it repaints back to the build-time
material ASSETS. A default you can return to has to be a real destination — a "paint it once"
implementation has nowhere to go back to.

**RIGS lends the wardrobe** through `Cosmetics.TestOverride`, session-only, writing nothing. Switch
RIGS off and the borrowed camo is withdrawn IN THE BATTLE YOU ARE STANDING IN — the repaint is
otherwise only read by `LoadLevel`, which is the same round trip the consumable supply had to fix.

**THE VANITY CHECK WAS UNFALSIFIABLE TWICE OVER, and this is the entry worth reading.** It fires
the same seeded volley under two camo sets and demands identical damage. Against a deliberately
broken build where the camo really did buff damage 50%, it passed — twice, for two different
reasons:

1. **The volley never landed.** Both runs did zero damage, and zero equals zero. It now asserts
   the enemy's HP actually FELL as part of its own condition.
2. **The camo was never worn.** `SelectedCosmetic` validates on read, so selecting a set the
   player does not own silently returns Olive — the check was comparing Olive with Olive. It now
   unlocks the set first (and locks it again afterwards, via a new `ProgressStore.LockCosmetic`
   that exists for exactly this) and READS BACK what the store holds rather than trusting what it
   asked for.

Only after both fixes did it read `enemy 280/276` against the broken build. **Two independent
reasons a check could not fail, in one check, in one session** — ask what state the failure needs
and then verify you are actually in it.

**The UI's tap path needed its own spy.** `PortSelfTest` tests `Cosmetics.TestOverride`, not
`TapCamo`, so a test supply that quietly UNLOCKED the set for real passed every check.
`BattleUIPreview` now taps the tile and asks the STORE whether anything moved — it caught the
breakage immediately (`unlocked 0->1, worn Olive->Arctic`). That preview is the only harness that
drives real MonoBehaviour UI.

**Two palettes now compete for the same colour space.** Urban Grey is boxed in on three sides —
Ironclad's steel, the player's own Olive (measured 0.159, barely over the floor) and Desert Tan.
Before adding a fifth camo or a third faction, run the distinctness checks first and expect to
have to move something.

## Tier 1.3 — the consumables, built 2026-08-10

Found fully ported and reached by nothing (the SIXTH such system), and now live: **Airstrike 250c,
Early Reinforcements 200c, Trauma Kit 150c, Smoke Screen 200c** — bought and equipped on the
loadout screen at the locked cap of TWO, triggered from the battle HUD on the player's own Aiming
phase. `Consumables` is the catalog, `ConsumableActions` holds the effects, and each is confirmed
on a device by what it DID, not by the fact a button existed.

### Overwatch Flare is NOT built, and that is the most important line in this section

It halves the enemy's next advance budget. **Nothing in this port ever advances.**
`UnitEntity.AdvancePerTurn` is imported and read only to count advancers for a threat line;
`AdvanceRemaining` is written NOWHERE; there is no enemy march step; and `SkirmishEntity` — the
melee an arrival resolves into — is defined, counted in `IsVisuallyIdle`, and never created.
**Advancing squads and melee are an EIGHTH dead system**, and a large one: they are what
`PROGRESSION_DESIGN`'s whole survival/defend archetype is made of.

A 200-coin button that changes nothing teaches the player that coins are decorative, which is
worse than having no button. That is the same call already made about wind. `PortSelfTest` asserts
BOTH halves — Overwatch is not sold, AND no enemy ever banks an advance — so the day advancing
squads land, the check goes red and adding one catalog entry is the fix.

### Confirmed on device, each by its output

```
Trauma Kit    [Consumable] TraumaKit: hp 304 -> 320      (clamped; front rank only)
Airstrike     [Consumable] Airstrike armed=True -> Airstrike fired
              [Battle] volley: 12 rounds   <- the volley alone is 11
              Garrison Post 135 -> 87, round visibly falling NOSE-DOWN among the arcs
Reinforce     [Consumable] reinforcements: 10 -> 13 player units, formed up right of the line
Smoke         [Consumable] SmokeScreen armed=True (button reads `Smoke / ARMED`, still THERE)
              [Consumable] Smoke Screen spent on the enemy volley
```

Bought with coins earned in play, because the release build is not debuggable and `run-as` cannot
seed PlayerPrefs — which turned out to be a better test than seeding would have been: the purchase,
the balance, the affordability tint and the carry cap were all exercised for real.

### RESOLVED: the Airstrike now has an aircraft, and it flies BEFORE the volley

Rob asked, the day Tier 1.3 shipped, whether anything actually flies across the screen. It did not:
a single grenade popped into existence in mid-air and fell. **A 30 fps device capture then found
something worse than ugly — the bomb was detonating OFF-SCREEN**, ~0.85s before the volley-follow
camera finished panning to the target. Nobody had ever seen it.

**A control run corrected two earlier write-ups in this file.** The same drag with nothing armed
shows the identical "round falling nose-down among the arcs" that two sessions had taken for the
airstrike. **That is the TANK SHELL**, which fires on every volley for free. Take the control shot.

**The fix is a sequencing change, not an art change**, and Rob directed it: *"plane should fly first
before the player volley."* A straight-wing attack aircraft (`Assets/Models/attack_plane.glb`, built
by `build_attack_plane.py`) crosses from the player's side in a new `TurnPhase.AirstrikeRun`,
releases the bomb, and exits; the infantry volley launches the moment that bomb lands. With no
rounds in the air yet there is nothing for the camera to chase, so the pass owns the frame. The beat
costs **1.10s** and no damage number moved.

**THREE THINGS THIS COST, all of which a green test suite could not see:**

- **The model had to be BANKED ~45 degrees, and the SIGN matters as much as the angle.** The
  wingspan runs along DEPTH and `BattleCamera` looks UP ~14 degrees, so an unbanked aircraft
  projects its span vertically and reads as a cross-shaped blob. Rolled the WRONG way it shows the
  camera the bare top of the wing — the surface the builder deliberately leaves undetailed, because
  the player only ever sees the underside. It is `-45` with no yaw. Free: a runtime rotation.
- **IT SHIPPED FLYING BACKWARDS, and only Rob looking at it caught that.** The facing was reasoned
  out from the axis conventions — "the GLB is authored nose toward +X" plus "GameSpace negates X" —
  which produced a 180 degree yaw, a cannon trailing behind the tail, and a green test suite. The
  import chain in fact lands the authored nose pointing screen-right already. **Do not re-derive
  the facing from the conventions; ask `PlanePreview.Orientation`**, which renders all four
  yaw/bank combinations against a rank of soldiers. Same family as every other "assert the artefact,
  not the note about it" entry in this file, and the second time TODAY that reasoning about this
  aircraft's axes was wrong.
- **It flies at y=9.5 with a PASS-BY SOUND** (`Assets/Audio/plane_passby.wav`). The source is an
  8.3s recording; it is cut to 3.0s starting at 2.01s so its PEAK — which sits at 3.30s in the
  original — lands as the aircraft crosses the drop point, 1.29s into the run. Play the whole file
  and the loudest part arrives over empty sky, seconds after the plane has gone. Height does NOT
  move the release or the impact: the drop lead is `PlaneSpeed * BombFallTime`, and neither is a
  function of height.
- **`PlanePreview` was rendering a different aircraft than the game** and both halves are fixed: it
  had a hardcoded camZ **11** against the run's real **14**, and defaulted to a yaw the game had
  stopped using. It now derives camZ from `CameraDirector.AirstrikeRunHalfWidth` and reads
  `BattleRunner.PlaneBank` / `PlaneScale` directly, so it cannot drift again. **Delete
  `Builds/plane` before reading a sheet from it** — stale frames from an earlier run are glob-matched
  alongside the new ones and produced one thoroughly misleading comparison.
- ~~**STILL OPEN — Rob wants MORE ROUNDS coming from the plane**~~ **DONE** (2026-08-10) — 14
  rounds over the same walk at half the damage each, and then STRETCHED INTO STREAKS, which is the
  half that actually made a difference. See "The strafing burst" below.
- **THE BOMB IS A BULLET, not a grenade** (2026-08-10). The grenade prefab is olive-lime at 0.16
  scale and was, in Rob's words, hard to see; the bullet draws as the bright unlit TRACER. It is
  told apart from the aircraft's own cannon fire by `IsAirstrike`, which the renderer scales 2.4x —
  the flag had been set since Tier 1.3 and read NOWHERE. The scale is assigned on EVERY round
  because the slots are POOLED, and exactly one round may carry the flag; both are asserted.
  **AND IT IS MULTIPLIED ONTO THE PREFAB'S OWN SCALE, NEVER ONTO `Vector3.one`.** Every projectile
  prefab is authored at its own size — bullet 0.22, grenade 0.16, rocket 0.30, shell 0.34 — so the
  first version, which reset unflagged rounds to `Vector3.one`, drew EVERY ROUND IN THE GAME at
  raw GLB size, about 4.5x too big. Rob caught it in one look. **No test covers a per-frame
  transform write clobbering an authored value**, and the device is the only instrument that sees
  it: check an ORDINARY volley after touching this path, not just the case being added.
- **IT STRAFES, with REAL rounds** (added on Rob's ask, 2026-08-10). Seven cannon rounds at 4
  damage, walked along the ground into the bomb's own impact point. The earlier decision NOT to add
  gunfire is reversed and its reasoning survives: refusing a cue that does NOTHING was right, so
  these do something. **The Airstrike is stronger for it — 24 damage becomes 52** — a deliberate
  correction for the dearest item in the shop, but a balance change; `StrafeDamage` is the knob.
  **The first version was mechanically perfect and INVISIBLE**: rounds inheriting the aircraft's
  speed and dropping from 9.5 units arrive near-vertically at ~31 u/s and vanish in a few frames.
  Gunfire rakes FORWARD — each round is now solved onto its own point of the walk, giving it 10 u/s
  horizontally so it outruns the aircraft and draws a streak.
- **It was too big at 1.0 and is rendered at 0.85** (`BattleRunner.PlaneScale`), Rob's call after
  seeing it pass. Judged in the preview beside a rank of soldiers: 0.70 starts reading as distant
  scenery rather than as the thing you just paid 250 coins for.
- **The run inherited the AIMING framing and clipped the aircraft off the top of the frame.**
  `TurnPhase.AirstrikeRun` fell through `PhaseHalfWidth`'s `default:` to the tightest camera in the
  game (camZ 9.3). Fixed with an explicit case and a floor, `AirstrikeRunHalfWidth` -> camZ 14.
- **The aircraft FROZE at handover and hung in the sky for the rest of the battle.** Its motion sat
  inside the run's own step, which stops being called the instant the phase changes — and this
  aircraft deliberately OUTLIVES its phase, exiting over the top of the volley. It is the same rule
  as "anything that decays must decay on EVERY tick path". Motion moved to the physics section and
  the despawn point is carried on the entity, so it depends on nothing the phase owns.

### The strafing burst — doubled, and that ALONE CHANGED NOTHING. 2026-08-10

**"Still want to see more rounds coming from the plane."** The count went **`StrafeRounds` 7 -> 14
with `StrafeDamage` 4 -> 2** — twice the rounds over the same 4-unit walk, ~25 rounds/sec at
`PlaneSpeed 7`, with the burst's contribution held at 28 so the Airstrike's total stays 52.

**Rob then looked at the real thing and reported NO VISIBLE DIFFERENCE.** That verdict is the most
useful thing in this section, so it is recorded before the fix:

> "hmm i don't really see a difference — looks like only one."

**The count was never the bottleneck, and the device capture that "confirmed" it was measuring the
wrong thing.** The capture showed eight tracers in the air at once against seven with gaps, which is
true, and irrelevant: a **0.22-scale bullet travelling ~25 u/s covers a fifth of the gap it opens
between frames**, so seven of them and fourteen of them both draw a faint DOTTED CHAIN. A dot is the
same shape whether it is moving or not. This is "assert the OUTPUT, not the input" wearing a new
costume and it fooled a device screenshot: I asserted the round COUNT — an input — and read the
frames for confirmation of it rather than for what the burst LOOKED like.

**The fix is the round's SHAPE.** `ProjectileEntity.IsStrafe` marks the aircraft's cannon fire, and
the renderer stretches those rounds **4.5x along their own flight and 0.7x across it**
(`BattleRunner.StrafeRoundStretch` / `StrafeRoundWidth`), turning each into a tracer STREAK that
bridges most of the gap to the next frame. It costs nothing — the round is already rotated onto its
velocity, so local X is the direction of travel. **A bigger dot was the obvious change and is the
wrong one**: what fails to read is the shape, not the area, and the bomb already owns "big round
dot" (`IsAirstrike`, 2.4x). Three shapes now come out of one pooled prefab and the two flags are
the whole distinction, so `PortSelfTest` asserts they are MUTUALLY EXCLUSIVE.

**Density was still the only count lever with room.** The first round is fired
`StrafeLength + StrafeLead` = **8 units** behind the target and the aircraft spawns
`PlaneRunHalfLength` = **9** back — one unit of headroom — so a longer walk or lead needs the spawn
moved, which lengthens the beat. A shorter `StrafeFallTime` would raise the cadence and must NOT be
used: 0.40s is already short, and the first version proved short flights are what made these rounds
invisible.

**The damage budget was held deliberately.** More rounds at `StrafeDamage 4` would have been a
straight buff to an item that had already gone 24 -> 52 the same day, smuggled in under a
presentation ask. Count is presentation; the total is what the campaign feels, and `BalanceAudit`
does not know about consumables at all. The guarding check asserts ABSOLUTES (`>= 12` rounds, total
in `[24, 32]`) because the existing one asserted `Count == StrafeRounds` and `Damage ==
StrafeDamage` — self-consistency with its own constants, green on the tap Rob rejected and green on
a silent doubling. Run against both: `(7 rounds)` red, `(56, held at 28)` red.

### The rake had to SPREAD, not just fire more. 2026-08-11

> "the strafe should spread further horizontally. right now it seems to be directed at one or spots
> that are close together. it's a strafe — as plane moves to the right, the rounds should also move
> that way. it's more of a burst at the moment."

**Third verdict on this burst, and the third time the wrong dimension had been turned up.** Count
(7 -> 14) did nothing visible; SHAPE (dots -> streaks) made the rounds legible; neither moved the
one thing that makes gunfire read as strafing, which is the impacts WALKING across the shot. Four
units of walk inside a ~10.2-unit frame is a third of the screen — a cluster, whatever is in it.

**`StrafeLength` 4 -> 6, paid for with `PlaneRunHalfLength` 9 -> 11.** The two are locked together
by one inequality, and it is worth keeping in mind before touching either:

```
PlaneRunHalfLength >= StrafeLength + StrafeLead + 1
```

The first round is fired `StrafeLength + StrafeLead` behind the target, so the aircraft has to
exist that far back. **The spare unit is not slack**: the firing loop fires every round whose point
the plane has already passed, so a spawn at or beyond the first firing point dumps several rounds
from ONE position in a single tick — a literal burst, which is the thing being fixed.

**6 is the frame's limit, not a taste call.** The walk ends on the bomb, so it can only grow
leftward, and the run's frame reaches `PlaneCameraBias + AirstrikeRunHalfWidth` = 6.6 units left of
the target at the half-width FLOOR. Past that the opening rounds land off-screen: more spread, less
visible strafe. 6 leaves 0.6 units of margin on the tightest level.

**The cost is beat length** — the run is `(PlaneRunHalfLength - PlaneSpeed * BombFallTime) /
PlaneSpeed + BombFallTime`, so +2 units is +0.29s, taking the beat ~1.15s -> ~1.44s. That is the
price of a 50% wider rake and there is no cheaper lever: `StrafeLead` cannot shrink much (it is
`lead / fall` = 10 u/s of forward speed, and below `PlaneSpeed 7` the rounds stop outrunning the
aircraft, which is what made them invisible in the first place), and `StrafeFallTime` must not
shorten for the same reason.

**One thing to LISTEN to, not measurable from here: the pass-by sound.** Its peak is cut to land as
the aircraft crosses the drop point, and the drop now happens 0.72s into the run rather than 0.44s.
Nothing in the build can check that — `screenrecord` captures no audio — so it wants a human ear.

### The rake had to CROSS the target, not stop on it. 2026-08-11

> "the airstrike should continue to fire until the plane reaches the right side. it's not hitting
> the structure."

**A probe found the cause immediately, and it was not the walk's length.** The walk ENDED on the
aim point and approached it from the left, so every round but the last landed SHORT of whatever the
player aimed at:

```
[Geom] target=10.92   walk = [4.92, 10.92]
[Geom] structure 'Outpost'  span=[6.00, 8.00]
[Geom] enemyXs=4.0,4.3,4.7,4.9,6.8,7.0,7.2,6.9,7.1
```

Aim at a building and the burst rakes the dirt in front of it and stops at the near wall. **The fix
is `StrafeOvershoot = 3` — the walk now crosses the bomb's own impact point** and carries on past
it, so the rake goes over the target rather than up to it. Rounds land on BOTH sides.

**3 is the frame's right-hand limit, and it is smaller than the left one, because the camera LEADS
the aircraft.** `PlaneCameraBias` puts the frame 6.6 units left of the target and only 3.6 right of
it — so the overshoot spends the short side, and 3 keeps the same 0.6 of margin the left end has.

**Keeping the overshoot UNDER `StrafeLead` is what kept this a two-constant change.** The last
round is fired when the aircraft is `StrafeLead` short of it, at `target + 3 - 4`, which is still
before the bomb lands and the phase ends. Push it past the lead and rounds want to fire AFTER
handover — and the firing loop lives inside the run's own step, which stops being called the
instant the phase changes. That is the trap the aircraft's own motion already paid for. **The
negative run at `StrafeOvershoot 7` shows it exactly: 21 of 28 rounds ever fired**, the rest
silently dropped on the floor with no error anywhere.

**Density held: `StrafeRounds` 14 -> 28 and `StrafeDamage` 2 -> 1.** The count has now been raised
twice for the same reason — to hold the SPACING near 0.33 units as the walk grew 4 -> 6 -> 9 — and
the damage halved each time so the burst's contribution stays 28 and the item's total stays 52.

**One thing the budget arithmetic does NOT capture, and it is worth knowing before tuning:** a wider
rake spreads the same nominal damage over more empty ground, so its EFFECTIVE damage falls even
though the total is unchanged. The burst is presentation that happens to hurt. If it ever needs to
hurt a FIXED amount, that is a different design and wants a different mechanism than a walk of
independent rounds.

### The volley and the pass now land TOGETHER. 2026-08-11

> "i wonder if we can sync the player projectile volley with the plane. right now it's a little
> awkward."

**The two halves used to be ADDED.** The aircraft made its whole pass, its bomb landed, and only
then did the volley launch. Measured across the power range before changing anything:

```
power   plane run -> impact   volley flight   TOTAL NOW   if synced
 40%          1.57s               1.47s         3.05s       1.57s
 65%          1.57s               2.24s         3.81s       2.24s
 86%          1.62s               2.91s         4.53s       2.91s
100%          2.43s               3.36s         5.79s       3.36s
```

An ordinary shot cost **4.53 seconds** from release to impact, a third of it spent watching an
aircraft with none of the player's own rounds in the air. That is the awkwardness, and the table is
why it was not a matter of shaving a constant.

**Whichever half takes LONGER to reach the target now starts first, and the other is delayed by the
difference**, so both land together and the beat costs `max(flight, run)` instead of their sum.
`GameState.AirstrikeSpawnDelay` and `PendingVolleyDelay` are the two halves of that one alignment
and **at most one is ever non-zero**. At any usable power the volley is the slower half, so in
practice the volley goes at the moment of release — the game feels responsive to the drag again —
and the aircraft is held back to catch up.

**The phase stays `AirstrikeRun` even though the volley is away.** That is deliberate and it is
what keeps the earlier fixes intact: the run's camera cuts to the strike and HOLDS, so the aircraft
still enters across the left edge and the player's rounds arc into the same held frame rather than
dragging the camera off after them. Hits land either way — **collision runs on the always-run path,
not inside a phase** — which is the fact that made this possible at all.

**Everything the aircraft does moved to the always-run path.** Motion was already there; the guns
went there when the rake started outliving the bomb; and the BOMB RELEASE went there now, because
an aircraft held back is routinely still short of its drop point long after the phase has moved on.
`AirstrikePlaneEntity.BombTargetX` carries the target, because the aim it came from is cleared the
instant the volley launches — which is now usually BEFORE the aircraft is even released.

**A held aircraft must not be drawn.** The entity exists from the moment of release so nothing has
to be recomputed when it is let go, but a stationary aeroplane parked at its spawn for a second and
a half is a worse artefact than the one the delay fixes. The renderer gates on the same value the
tick does.

**TWO THINGS THE DEVICE FOUND THAT NO CHECK DID**, both caused by the aircraft now being HELD:

- **The pass-by sound fired at the moment of release**, over empty sky, a second before the plane
  existed. It used to be the same instant; it is not any more. It now plays on the true->false edge
  of `AirstrikeSpawnDelay` — the moment the aircraft is actually let go — because the clip is cut
  so its peak lands as the plane crosses its drop point, and that offset is measured from the START
  OF THE RUN. Anchor it anywhere else and the peak is silently thrown away, which is exactly what
  the original 8.3s clip did.
- **The release log said `volley held` when the volley was already away.** Third false reading from
  that one line — `volley: 0 rounds`, then strafe tracers counted as volley rounds, now this. Each
  time the beat changed under it. It reports the three real cases now: held, away with an airstrike
  inbound, or a plain volley.

**The check measures IMPACT TIMES out of a real stepped flight**, not the arithmetic that schedules
them:

```
[ok  ] bomb at 3.08s, volley at 3.17s from release (0.08s apart)
[FAIL] bomb at 1.92s, volley at 5.08s from release (3.17s apart)   <- the old added beat
```

That 3.17s gap in the negative run is the awkwardness, in numbers.

### The rake belongs to the ENEMY, not to the volley. 2026-08-11 — the design change

> "the strafe is independent of the player unit volley. it should start from the left, strafe
> should cover the whole enemy position and its structures."

**This is the change that made the previous three unnecessary.** The burst had always been defined
relative to the player's landing point — walk to it, then walk 3 past it — so its ground moved with
every drag: aim short and it raked open dirt, aim long and it raked past the line. Every fix before
this one was tuning an offset from the wrong origin.

**The rake is now derived from the enemy position and carried on the aircraft.**
`BattleTick.StrafeSpan` takes the enemy units and the enemy STRUCTURE EDGES — edges, because an
outpost is 2 units wide and raking to its centre leaves half the building unhit — plus
`StrafeMargin` at each end. `AirstrikePlaneEntity.StrafeFromX/ToX` carry it, fixed at the moment the
aircraft is committed. Carried rather than recomputed because the run outlives its own phase, and
because the enemy set SHRINKS as the rake kills, which would walk the far end backwards mid-burst.

**The bomb is the only part of an airstrike that cares where you aimed.** That split is the whole
design and it is what `PortSelfTest` now asserts, by firing the item at two different aims and
demanding the identical ground — the one property no arrangement of aim-relative constants can
fake.

**Three things had to move with it:**

- **The SPAWN is derived, not a fixed offset.** The aircraft must exist `StrafeLead` before the
  rake's first firing point AND still be short of the release when it drops, so it spawns at
  whichever is further back. `PlaneRunHalfLength` is now a FLOOR, not the spawn. Consequence worth
  knowing: **the beat is no longer one fixed length across the campaign** — a wider enemy line
  costs a longer pass.
- **The GUNS moved to the always-run physics path**, beside the aircraft's motion, for exactly the
  reason that motion moved there. See below.
- **The CAMERA frames the rake AND the bomb**, which are now different places. The anchor is the
  midpoint of everything the pass must show; the half-width covers all three points and is still
  floored by `AirstrikeRunHalfWidth`, so it can only ever pull back.

### The guns had to leave the phase, and the check that proved it was itself broken first

The rake reaches past the bomb's impact whenever the player aims short of the enemy's far edge —
the ordinary case — so the last rounds are fired AFTER the phase has handed over to the volley.
While the firing loop lived in the run's own step those rounds were **never fired at all**: no
error, no log, just a burst that stopped early. Same family as the aircraft freezing in mid-air,
and the second time this beat has paid for it.

**The check written for it PASSED against the broken code.** It used the same synthetic aim as
everything else in that block — one landing PAST the enemy's far edge — where the rake finishes
before the bomb and a phase-bound loop drops nothing. Re-pointed at an aim landing SHORT, which is
the only state where the failure is reachable, it reads:

```
[ok  ] a shot aimed SHORT ... fires the whole burst (28/28) — impact 3.16 vs rake end 10.13
[FAIL] a shot aimed SHORT ... fires the whole burst (17/28)      <- guns confined to the phase
```

**Eleven of twenty-eight rounds, dropped in silence.** The check now asserts the aim IS short as
part of its own condition, so it cannot quietly stop testing this if the geometry moves. This is
the third time in two days that a check had to be put into the state where its failure was
reachable before it was worth anything — see the empty purse and the null CameraFollowX.

### The check that caught the burst outliving its own phase

A pre-existing check went red on this change and was RIGHT to: "the volley that follows the run is
the volley the player aimed" counted `!IsAirstrike`, which had quietly meant "the volley" only
because the burst always finished before handover. It does not any more — the last rounds land
after the bomb — so they were being counted as volley rounds. It now excludes `IsStrafe` as well.
Worth recording because the check noticed a real behavioural change before any device did.

### The check that guards it asserts the FRAME, and the FIRING POSITIONS

Written against `StrafeLength` it would have been green through all three verdicts. It asks instead
for an impact span of `>= 5.5` units with the first landing inside the frame's left edge — the
frame being the thing the spread is actually competing with.

**And it asserts where the rounds were FIRED FROM, which the landings cannot see.** Every round is
solved onto its own point, so a clumped burst still produces a perfect walk of landing points. The
negative run proves it: spawning the aircraft too far forward left the impacts spanning a flawless
`6.00` while the firing positions collapsed from `5.95` to `3.97`.

```
[FAIL] ... impacts span 4.00 units ...          <- the old 4-unit walk
[FAIL] ... fired from 3.97 units ... not one    <- the clump, invisible in the impacts
```

### The aircraft did not FLY IN — the camera swept past it. 2026-08-10

> "the plane should come from the left side of the screen. it seems to just appear in the
> middle/left middle of the shot."

**It was never the spawn point, and no spawn distance could have fixed it.** A probe against L1:

```
target=10.92  spawn=1.92
AIMING frame  centre=-7.54  half=2.05  left=-9.59   spawnInside=False
RUN    frame  centre= 9.42  half=5.10  left= 4.32   spawnInside=False
```

The aircraft spawns off-frame under BOTH framings. But the run BEGINS with the camera still over
the player's own line at **-7.54**, and it then springs **17 units right** at
`MarchEscortSmoothTime` while the plane sits at 1.92 doing 7 u/s. **The camera overtakes the
aircraft and arrives to find it already mid-frame.** A camera travelling the same direction faster
than the plane always will.

**So the run CUTS to its anchor and holds, and it is the only phase that does.** Everything else
keeps the one continuous spring, deliberately — "every phase change TELEPORTED the camera" is a bug
this project already fixed once. `CAMERA_ARCHITECTURE.md` is LOCKED, so **this exception was asked
for and granted, not assumed**. It also delivers what the phase always claimed ("a hold, not a
chase"): the aircraft now crosses a frame that is already still, entering across the left edge
~0.34s into the run.

**The check that shipped this bug had the answer in its own failure message.** It read "aircraft
spawned off-frame" and asserted only `spawn == target - PlaneRunHalfLength` — the message named a
property about the FRAME that nothing in the check ever looked at. That is a doc asserting itself
one level down, and it is why the bug reached a device. The replacement asserts the camera is AT
the run anchor and the spawn is outside its left edge.

**And the first version of that replacement was worthless, for this file's favourite reason.**
`fresh` has never ticked, so its `CameraFollowX` is NULL — the spring then begins AT the anchor,
travels nothing, and sweeps past nothing. **The check passed against the sweeping code it was
written to catch.** Seeding the camera onto the player line first — where a real battle leaves it —
is what gave it teeth, and the negative run then read:

```
[FAIL] the run CUTS to its own framing and the aircraft enters across the LEFT EDGE —
       camera -7.44 (anchor 9.42, edge 4.32), spawn 1.92
```

That is Rob's bug, in numbers. Same family as the empty-purse check and the `ReferenceEquals`
refusal test: **ask what STATE the failure needs to be reachable in, then put the check in it.**

### Both confirmed on device, L1, 2026-08-10 — with the control in the same capture

Fresh install, RIGS test supply, armed from the HUD, a real drag
(`input swipe 300 900 631 1231 600`), recorded at 60 fps:

```
[Consumable] Airstrike armed=True -> Airstrike fired (TEST supply)
[Battle] airstrike run, volley held at 86% / 45.0deg
[Battle] volley: 11 rounds, after the airstrike          <- 1.15s later, beat unchanged
```

On the frames: the camera CUTS to the strike, holds on an empty frame for ~0.3s, and the aircraft's
nose crosses the LEFT EDGE — the frame at 2.02s catches it half in. The cannon fire is now a line
of distinct elongated TRACERS running from the aircraft to the ground, with impacts kicking up
across the enemy rank; outpost 90 -> 82 and the burst visible on every frame of the pass.

**The control is the same capture's ordinary volley**, which is where the pooled-scale regression
would show: the eleven infantry rounds arrive at normal size and normal shape, so the stretch does
not leak into a recycled slot. That is asserted nowhere and can only be seen — the same class of
bug as the `Vector3.one` scale regression, and the reason the volley is now checked on every device
run that touches this path.

**And the release log was lying — TWICE, for different reasons.** First it reported
`volley: 0 rounds`, because the volley had not been built yet. Then, once the burst began outliving
the run, a raw `Projectiles.Count` swept its tracers into the total and reported **18 rounds for an
11-round volley** on device. It now counts the volley and nothing else. A lying instrument is worse
than a missing one when it is the only instrument a release build has, and this line has now earned
that warning twice:

```
[Battle] airstrike run, volley held at 86% / 45.0deg
[Battle] volley: 11 rounds, after the airstrike        <- 1.10s later
```

### The arm/spend split, which is the one piece of this that cost something

**Airstrike and Smoke are ARMED and spent only when they FIRE. Trauma Kit and Reinforcements
resolve on the tap.** A first Kotlin implementation spent at arm time and the HUD button — whose
visibility is gated on the equipped count — vanished the instant it was tapped, with no ARMED state
to see and no way to change your mind. That was found on a device, not in a test suite. The device
shot above showing `Smoke / ARMED` still on screen is the evidence that this port did not repeat it,
and `PortSelfTest` asserts arming does not decrement.

**The permanent `ProgressStore` spend lives in `BattleRunner`, never in the tick.** The two armed
items are consumed inside pure tick functions, and a `PlayerPrefs` write in there would fire on
every `PortSelfTest` call to `FireVolley` and quietly drain the editor's own inventory. The runner
watches the armed flag's true→false transition — which is NOT the "inferred from a list-length
delta" trap this project has been bitten by, because the flag exists for exactly this and nothing
else clears it.

### Early Reinforcements dragged a second port in with it

The relief squad did not exist here either — no builder, no march. It enters a formation's width
BEHIND the player line and runs to its slots on `MarchTargetX`, so **without a march step the men
bought and paid for would stand off the framed edge for the rest of the battle.** `BattleTick
.StepMarch` is that step, and it runs on the battle-over tick path too: a jogging man frozen the
instant victory lands is on screen, because that path deliberately re-frames onto the survivors.

The squad is built from the player's OWN commonest ground unit rather than a hardcoded Rifleman as
the Kotlin does — `BattleTick` has no asset table, and giving it one means a serialized reference
and a scene rebuild for a squad the player already described at the loadout screen.

*A claim in the first draft of the plan was WRONG and the compiler caught it: I wrote that an
unwalked march would hang the turn via `GameState.Settled`. The property is `IsVisuallyIdle`, the
handover is `TurnFlow.EvaluateVolley` and does not consult it, and nothing in the port reads it yet.
The real cost is a permanent latch on a ported facility. Recorded because it is this file's own
standing rule — assert the artefact, not the name you remember — catching the person applying it.*

### The checks: 576, and every new one was seen to fail first

`PortSelfTest` went 559 → 576 (17 checks, deliberately consolidated — see below). Per the standing rule, the new block was run against **nine
deliberate breakages** and each one turned the intended check red: smoke wired to nothing
(`1.87 -> 1.97` spread, refused), the airstrike aimed 3 units off (`13.92 vs 10.92`), the march
removed, the trauma kit healing everyone, arming spending the carry, the cap truncating instead of
refusing, Overwatch sold, and — the ninth, added after the first pass — the airstrike reusing the
PLAYER's flight time, the real Kotlin bug, which a flat drag compresses to 0.63s and an arced one
stretches to 3.08s against its own fixed 1.4s.

**The block was then CONSOLIDATED, on Rob's instruction** — 50 assertions over 307 lines became
18 over 232, and the same nine breakages were re-run to prove nothing was lost. Coverage went UP:
a merged check caught the flight-time constant the split version had missed. Related facts belong
in ONE check whose message names them all; a failure naming three properties is as diagnostic as
three checks, and this file is read by people. Keep a check separate only when it can fail
independently for a reason worth naming. **Consolidating is not a licence to drop coverage —
re-run the breakages after merging.**

**One of the first-pass checks was worthless and was rewritten**: a refusal test written as
`ReferenceEquals(Use(hurt with {...}), hurt with {...})` allocates two different records, so it was
false whatever the code did — a check that could never fail, wearing the costume of a refusal test.
The same family as the phase-spread check deleted during the flame work.

**And one check was self-referential**: the airstrike's fall time was asserted against the same
constant that defines it, so setting that constant to 0.18s passed every check. It now carries an
absolute floor (`>= 0.8f`, "legible means SECONDS, not frames") alongside the flat-drag comparison
that does the real work.

### What the headless preview caught before the device

`BattleUIPreview.Shots` now renders the loadout panel in three states (nothing owned, owned, and
carrying) and **fails the run if any Button lays out off screen**. That is precisely the failure the
Kotlin hit when it added a consumables section: Confirm was pushed past the bottom of the screen,
not clipped but ABSENT from the tree and unreachable by any input, found on a locked device with no
way to start a battle. The strip here is positioned from the panel's own top rather than stacked
after the roster rows, so a longer roster cannot push it anywhere, and `PortSelfTest` pins the
arithmetic against the live roster's row count.

## The loadout screen's per-frame NullReferenceException — FIXED 2026-08-10

**The tick was running before there was a battle to tick.** `Start` calls `EnterLevel(0)`, which for
a campaign level opens the picker and RETURNS — `LoadLevel` does not run until the player presses
BEGIN. `GameState` is a `record`, so it is a CLASS and `state` is null for that whole screen, and
`Update` entered `BattleTick.Step` anyway. Its first line is `s.SelectedAmmo`. One thrown exception
and one stack capture per frame, on the one screen where the player is sitting still and reading.

The guard is `if (state == null) return;` at the top of `Update`, and it is on the STATE rather than
on `ui.LoadoutOpen` deliberately: a LATER picker (RETRY, NEXT LEVEL) opens over a state that exists
and ticks through it perfectly well. What must not run is a tick with nothing to tick.

**Why it hid.** Nothing looked wrong, because the picker is uGUI on its own canvas and both
`HandleInput` and `OnGUI` already stand down while it is open — so the screen drew correctly, BEGIN
worked, and the battle ran clean at 60 fps. The release build's IL2CPP trace carries no line
numbers, and `BattleRunner.Update` was the only frame in it.

**Measured both ways, same instrument, same 3-second window, same screen** — which is what makes the
numbers mean anything, per the standing rule:

```
OLD code   186 NullReferenceExceptions in 3s on the LOADOUT screen  (~62/s = one per frame)
FIXED        0 in 3s on the LOADOUT screen, 0 in 3s in battle, 0 across a real volley
```

The negative run cost one extra build and is the only thing proving the guard reaches the bug. The
instrument was proved too, because a silent logcat is not evidence: the same capture shows
`[Battle] L1 Patrol Encounter: 10 player, 9 enemy, 2 structures` on the BEGIN press, and a real drag
(`input swipe 300 900 631 1231 600`) took the enemy line 9 -> 4 at a steady 60 fps.

**No self-test covers this**, and that is a considered choice rather than an omission: `PortSelfTest`
does not drive `MonoBehaviour` frame callbacks, and a check on the guard's CONDITION would assert the
fix's own restatement of itself — the "assert the output, not the input" trap in its purest form. The
device count IS the assertion here, and both halves of it are recorded above.

**The diagnosis was READ, not probed** — the backlog entry recommended a probe and the code answered
faster. That is not a correction to the rule: the probe is right when a static read leaves a
hypothesis, and here the read produced a null field, a dereference of it, and an exact match to both
measured contexts (null only before the first `LoadLevel`; zero after). The negative run then did the
job the probe would have.

## The incendiary flame — 2026-08-09

The burn had dealt damage since Tier 1.1 with **nothing to see**. The only way to confirm from a
device that it had fired was the `[Burn]` log, which is why that log was kept and why it stays.

**It needs no new tick state.** The flame is drawn straight off `GameState.BurningEnemyIds` — a set
that is filled when the round lands and cleared when the burn resolves at the turn handover. That
window is the whole post-volley pause, which makes the fire a **telegraph** as well as a cue: it
says these men are about to take damage, and the health bars drop as it goes out.

**Two tongues per man, one quad each, flickering out of phase.** One tongue is a shape that changes
size; two are a fire. The flicker is `CosmeticSystems.FlameScale`, a **sine of absolute time** — dt
VARIES, so anything integrated per frame would run at a different rate on a stuttering one, and a
phase accumulated per slot would need clearing on recycle. Height and width swing in ANTIPHASE (a
flame narrows as it licks up); swung together the tongue just zooms and reads as a throbbing
sticker.

**`FlamePhase` is keyed on the UNIT ID, not the render slot.** Slots are handed out in roster order
and shift down as men die, so a slot-keyed phase would make every surviving flame jump the instant
a neighbour fell — the same reasoning as `UnitAnim.Desync`.

**The colour is in the TEXTURE, not in a tint.** Hot yellow core, deep orange tips: that gradient is
the whole difference between "fire" and "an orange triangle" at this size, and a per-instance tint
can only scale the lot. The property block is left for the guttering alpha, which is per-slot.

**A pooled flame, bounded and pre-warmed**, sized from the enemy roster including waves and boss
phases. Minting one the frame a volley lands — alongside the blast, scorch and debris pools — is
exactly the mid-session mint the Filament build kept paying for.

**It gutters out over half a second rather than stopping.** The burn resolves on ONE frame, and a
bright orange object vanishing in one frame is the artefact this repo has already paid for twice
(the health bar and a backdrop layer both held full strength and then blinked out). There is no
matching fade IN — fire catches instantly and dies slowly.

**And the flame follows a body the burn KILLS.** A man the fire finishes leaves `EnemyUnits` on the
frame he dies, so drawing only the living would snuff his flame at the exact moment it did the most
work. `DyingUnitEntity` carries the same `Id`, so the corpse keeps its own guttering half-second and
the fire falls with him.

### Two things the preview caught that no test could

`FlamePreview.Shots` renders the flame on a rank of soldiers at gameplay framing, in seconds,
**through `Render/FlameRig` and the shipped `Flame.prefab`** — the same placement and the same art
the game uses. It is deliberately NOT a second implementation: `BackdropPreview` once was, and spent
a whole session producing plausible, wrong pictures.

Its first render found both of these in one frame:

- **The flame was UPSIDE DOWN** — fat hot base licking down at the boots, tapering to a point above
  the head. The prefab copied the health bar's 180-degree turn about **X**, which mirrors the
  VERTICAL, and the texture is already generated the right way up. It is a turn about **Y** now,
  which mirrors the horizontal and costs only the direction of the tip's lean. *The health bar takes
  the opposite choice for the mirror-image reason: its fill anchors to one END, so it cannot afford
  a horizontal mirror and can afford a vertical one.* Both are 180-degree turns that "face the quad
  at the camera", and they are not interchangeable.
- **It read as a CANDLE, not a man alight** — a taper of `(1-t)^0.62` kept the tongue
  narrow-but-present all the way up and drew a needle, so six burning soldiers looked like six
  rocket exhausts. Steeper taper (0.85), a wider body (0.50), and a tip fade tripled to 0.34 so the
  fade ends the flame rather than the profile. Shorter and broader overall: 1.05x body height at
  0.76x width, from 1.15 and 0.60.

### Confirmed on device, L1, 2026-08-10

A real drag (`input swipe 300 900 631 1231 600`) into L1's bunker, incendiary selected:

```
[Probe] ammo=Incendiary unitsHit=5 incendiaryHits=5 survivorsMarked=5
[Burn] 4 burning took 8 (4 died)
```

And on the frames: **two garrison soldiers alight on the bunker DECK** — standing on the deck, not
the world floor, so the entity-relative y is right — each flame the correct way up, wide hot base at
the boots licking just past the helmet, and the two **visibly different in size and lean on the same
frame**, which is the per-unit phase doing its job. Fire-coloured and unmistakable against the red
enemy uniforms.

Then the whole death sequence, which is better than designed: the burn kills them, **the ragdoll is
thrown and the fire goes with it**, guttering out as the body tumbles. **60 fps throughout**, read
off the HUD on four consecutive samples during the burn.

**One artefact, UNRESOLVED and deliberately not chased** — tracked in `_plans/BACKLOG.md` as
"Flames outlive their bodies by a frame or two". For a frame or two at the moment of death the two
flames stand on the deck with NO BODIES under them, before the corpses appear in flight. Best
current reading is the already-documented "a unit's slot is not stable across frames"
corpse-handover timing, which the flame has made visible for the first time — but that is a
hypothesis from one contact sheet at 12 fps, not a diagnosis.

**What the preview could not show and the device did:** the guttering, the flame on a garrison
rather than on flat ground, the frame rate, and the death sequence.

### The trap that cost most of the session: AUTO IGNORES THE AMMO SELECTION

Six incendiary volleys were fired with the AUTO button and **not one man ever caught fire.** Nothing
was wrong with the flame, the burn, or the marking. `AutoFire` builds its own `ProjectileEntity` and
**never sets `Ammo`**, so every round it throws is Standard however loudly the HUD says Incendiary.

It is deliberate — `CannonShells` documents the identity default in as many words — and it is the
exact sibling of the long-standing "**Auto cannot test STRUCTURES**". Auto is a test harness, not
the player, and the list of what it cannot test is now two items long.

**What settled it was a PROBE, after two rounds of guessing had not.** One build, one line, both
ends of the path:

```
[Probe] ammo=Incendiary unitsHit=1 incendiaryHits=0 survivorsMarked=0    <- AUTO
[Probe] ammo=Incendiary unitsHit=5 incendiaryHits=5 survivorsMarked=5    <- a real DRAG
```

The state said Incendiary in BOTH. Only the rounds differed — which is precisely the "assert the
OUTPUT, not the input" rule wearing yet another costume, and the reason the probe printed the state
and the rounds side by side instead of either alone.

**Both facts are now `PortSelfTest` checks**, so nobody has to rediscover this against a phone:
Auto's rounds must be Standard while the state says Incendiary, and a real volley must carry the
selection. The second is not decoration — without it the first would still pass if ammo were broken
everywhere. The Auto limitation is also in `CLAUDE.md` beside its structures sibling, with the drag
that clears L1's 16 units.

### The checks, and the one that was deleted for failing its own negative test

`PortSelfTest` asserts the flicker arithmetic directly, and asks the TEXTURE about its shape —
because the failure being guarded is "the texture is wrong, so the game draws an orange RECTANGLE
over every burning soldier", which no test of the generator's inputs can see. The shape checks carry
their **own negative case in the same run**: a plain white square must fail every one of them.

Every check was then run against deliberately broken code, per the standing rule. That is how three
of them were confirmed (taper, neck, antiphase all went red) — and how one was found worthless:

- A check asserted the **spread between the largest and smallest neighbour phase gap**, claiming to
  catch "a wave marching along the rank". A ramp (`unitId * 0.1f`) sailed through it at 6.08 rad,
  because the wrap-around manufactures one enormous gap. **Deleted.** A check that names a failure it
  cannot detect is worse than no check: it reads as coverage.
- Its neighbour, "neighbouring units rarely flicker together", scored **394 of 400 pairs** on that
  same ramp. That is the one that works.
- An earlier version asserted a FLOOR on the closest pair, and failed on the honest implementation:
  among 40 random phases some pair is almost certainly within a few hundredths of a radian. That is
  what randomness looks like, and it is invisible in a crowd of thirty. **Assert the distribution,
  not the extreme.**

## Tier 1.2 — the telegraph and the schedule, 2026-08-07

The mechanism was already firing (Phase D wired arrival). This is the half that makes a wave
something the player can PLAY AGAINST rather than something that happens to them.

**The countdown is composed, not authored.** `ReinforcementWave.telegraphText` — one authored
sentence with the number baked into it — is now `telegraphLabel` (what is coming) plus
`telegraphLeadTurns`. `EventSystems.TelegraphLine` builds the line every tick from the live turn
gap. A number in the label held still for the whole warning, which tells the player the clock has
stopped, and sat one copy-paste from disagreeing with `arrivesOnTurn` with nothing checking it.
`ReinforcementWaveBeat` takes the lead; a lead below 1 is CLAMPED, not honoured — an individual
wave does not get to opt out of pillar 7. Where two leads overlap the strip shows the NEAREST
wave, because there is one strip and a flickering one reads as neither.

**Confirmed on device, whole cycle, L10 Rubble Yard:** turn 2 `Heavy support inbound - 2 turns`,
turn 3 `Heavy support inbound - 1 turn`, turn 4 the strip clears and enemy units go 8 -> 12. The
strip also sits correctly UNDER the "Enemy turn" banner when both are up — the two channels were
built to stack and this is the first build in which both have been on screen together.

**The schedule is two levels, both stage 2, both 2-turn leads.** L10 is the beat chart's
"reinforcement race" and went 1 -> 2 to match the chart's own words ("armor in 2 turns"). L11
Oceanfront was the only stage-2 level `CampaignAudit` called NO MECHANIC — its beat offers a heli
it cannot have — so its own "else elite push" fallback is delivered as a telegraphed wave (3
heavies, turn 4) instead of three more bodies in the opening formation. **L11's wave was NOT
device-tested**; it is the same code path and data shape as L10's and is driven by `PortSelfTest`.

**L12 was deliberately left alone.** It already combines the boss phase with the charge, and its
`designNotes` record a device-measured margin against the 288 siege capacity. Adding enemies to
the finale would have quietly undone a number someone paid a defeat to find.

**`BalanceAudit` now checks reach for units that are not on the field yet.** Rule 7 measured the
opening roster only, so a wave could be authored past maximum range and every rule-7 check would
pass while the level was unwinnable from turn 4 — the L7 bug, one turn later. It STEPS the tick to
the wave's arrival and re-runs the reach rule on the real spawned positions rather than
re-deriving them from `anchorX`, which would be a second implementation to disagree. Proved by
pushing L11's wave to x 22: `121% power ... UNWINNABLE`, 2 errors. At the authored x 8 both wave
levels clear (L10 89%/97%, L11 89%/98%).

### The check that was right about the mechanism and wrong about the rule

A glyph-coverage check over every authored string that reaches TMP was written as **"ASCII only"**,
because that is what `CLAUDE.md` said. It flagged **23 strings on its first run** — every campaign
`levelGoal` and all 17 test-rig names, all of which use an em dash — and all 23 were "fixed".

**A device screenshot then showed an em dash rendering perfectly in the loadout panel.**
LiberationSans SDF covers Latin-1 and General Punctuation; what it lacks is SYMBOLS — `★` U+2605,
`◆` U+25C6, emoji, arrows — which is why the star and coin are drawn as sprites. All 23 edits were
reverted, and the check now asks `TMP_Settings.defaultFontAsset.HasCharacter` instead of asserting
a range. It carries its own negative case in the same run (the star must be reported MISSING), and
it does catch one real thing: wind's announcement strings came from the Kotlin with a wind emoji
and two arrows.

**The lesson, and it is a new costume on the standing rule:** a check written against a NOTE IN A
DOC asserts the note. The doc was a compressed heuristic that had been true about the two symbols
it was written for. Ask the thing itself — the font, the engine, the device.

### The state of the checks, as of the handover

```
PortSelfTest.Run          592 checks, ALL PASS
LevelComposition.Report   12 campaign levels, 0 errors, 2 accepted warnings (L3, L5 rule 7 —
                          reasons in their designNotes; both beats are about height)
BalanceAudit.Report       0 errors, 19 warnings (race-ratio flags on the dearest-squad
                          extreme, which is informational)
```

**A scene rebuild is NOT pending** — one was run on 2026-08-10 after `BattleRunner` gained its
`planePrefab` field (and earlier the same day for `flamePrefab`), so `Assets/Scenes/Battle.unity` and several materials are dirty because of it.
**The APK on the device is current** — rebuilt on 2026-08-11 with the whole airstrike rework: tracer
streaks, the run's camera cut, the enemy-derived rake, the impact realignment, the pass-by sound's
new anchor, the honest release log, and test supply carrying all four consumables. Everything above
was confirmed on that build. **All of 2026-08-11 is CODE-ONLY — no scene rebuild is pending.**
The device is on L1 with RIGS ON and a fresh install's zero coins, which costs nothing because test
supply is free.

## RIGS doubles as TEST SUPPLY — 2026-08-10

**While RIGS is ON, all four consumables are FREE to equip and none is ever spent.** The loadout
header says so in as many words (`Consumables — carry up to 2   [TEST SUPPLY — RIGS]`) and every
tile reads FREE, because a test mode that looks identical to the real one is how a "confirmed on
device" result gets recorded against a state no player can be in.

**Why it exists:** verifying one airstrike change cost a full re-earn of 250 coins. The release
build is not debuggable, so `run-as` cannot seed PlayerPrefs, and the protocol is
uninstall/reinstall — so every build wipes the balance. That was two Auto-driven levels per
iteration, four times in one session.

**It writes NOTHING** — no purchase, no coin change, no `SpendConsumable`. So a player who finds
RIGS cannot corrupt their own save, and switching it off leaves the economy exactly as it was. That
one property is what made reusing RIGS acceptable instead of adding a second hidden switch, and it
is asserted by `BattleUIPreview` (see below).

**TEST SUPPLY CARRIES ALL FOUR, IMMEDIATELY, AND IGNORES THE PICKER** (corrected 2026-08-11).
The first version carried only what the PICKER had selected, which made the switch nearly useless
for its own purpose: RIGS lives on the battle HUD, so turning it on mid-battle granted a free shelf
you could not reach without finishing the level to get back to a picker. Rob, with RIGS on and no
way to fire an airstrike: *"thought that would expose it by default."*

Two things make it work now. The carry under test supply is **every item at 1**, not the picker's
selection — which **deliberately exceeds the locked carry cap of TWO**, because a testing state has
to reach every item in one battle. And the RIGS toggle **re-reads the carry into the battle already
running**, so the shelf appears on the tap; it re-reads rather than adds, so switching RIGS back off
takes it away again and restores whatever the picker really equipped. Any ARMED flag is cleared on
the way, because those outlive the carry and an airstrike armed after its supply is withdrawn is a
volley spending an item the player has not got.

**The HUD buttons read `TEST` where the count goes**, for the same reason the loadout header does,
and it matters more here: this bar is showing FOUR items past a cap of two, and a state no player
can be in must never be mistaken for one they can.

**The old workflow, no longer needed:** BEGIN -> tap RIGS -> finish the level -> NEXT, one
Auto-driven level, about a minute. It is now BEGIN -> tap RIGS, and the bar is there. `showRigs` is deliberately NOT persisted, so a relaunch starts
clean — if that minute per session becomes annoying, persisting it is a one-line change, but it
would also mean a player who ever taps RIGS keeps free consumables forever.

**`PortSelfTest` cannot cover this** — it does not drive MonoBehaviours. `BattleUIPreview` does, and
it asserts the property that matters: it STAKES four items' worth of coins, equips under test supply,
and fails the run if either the balance or the owned count moved. The first version of that check
ran on the editor's real balance of ZERO, so a fall-through to the genuine purchase path simply
could not afford anything, wrote nothing, and the check passed against deliberately broken code —
**a check that could not fail.** Staking the coins is what gives it teeth, and the negative run then
reported `SmokeScreen owned 0->1, coins 800->600`.

**The device's progress was WIPED and re-earned on 2026-08-10**, four times over, testing the
aircraft. Nothing can be seeded — the release build is not debuggable, so `run-as` cannot reach
PlayerPrefs — and the testing protocol is uninstall/reinstall, so **every build costs the balance**.
Re-earning 250 coins is two Auto-driven levels and about two minutes; budget for it rather than
being surprised. It currently sits on L1 with ~205 coins and the first two levels cleared.

Note the flame's own assets are NEW and untracked until committed: `Assets/Prefabs/Flame.prefab`,
`Assets/Materials/Flame.mat`, `Assets/Materials/FlameTex.asset`, `Assets/Scripts/Render/FlameRig.cs`
and `Assets/Editor/FlamePreview.cs`.

### What changed on 2026-08-07, in one place

Sections at the end of this file carry the detail; this is the index.

| | |
|---|---|
| `BalanceAudit` + composition **rule 7** | reach, the volley race, the melee clock, and siege capacity — checked, not prose. Found **L7 unwinnable** and fixed it |
| `LEVEL_AUTHORING.md` rule 4 corrected | it claimed a "~49-unit max range"; the real figure is **20.25**, and that lie is what licensed L7 |
| The **siege capacity** finding | a stock squad can do a FIXED **288** structure damage per battle (3 shells x 96; a rifleman does 2). Five levels garrisoned more than that and were retuned |
| **The tank shell's aim** | it overshot the volley by **2.5-3.9 units**, so the only structure-breaking weapon could not be placed. Now solved onto the volley's landing point |
| **HUD lists structures separately** | a single total cannot say WHICH building still stands; it cost one audit run four volleys fired into rubble |
| **L9 roster cut 22 -> 15** | widest body ratio in the campaign, and its garrisons were over the decks they stood on |
| **Tier 1.1 ammo** | four types, a selector that also sells, the incendiary burn. A FIFTH dead system |
| **Ragdoll levitation fixed** | corpses flung at a wall were snapped up the face onto the roof |

### The lesson THIS session kept re-teaching: assert the OUTPUT, not the input

Three separate times, a check that passed was asserting the wrong thing, and the device or a
deliberate negative test found what it missed:

- **AP ammo** asserted `structureDamageScale == 2` and passed, while the real per-round effect was
  **1.2x** — the engine multiplies `Damage` by the multiplier, and the ammo had already scaled
  `Damage` down. Caught on a device: 128 off a 165hp citadel where ~192 was intended.
- **The ragdoll fix** was "verified" by two tests that both passed against buggy code, because
  neither ever reached the branch — a body at rest dips below the box's base, and one tick does
  not carry a thrown body into the box at all.
- **The tank shell** had a test asserting ammo was IMPORTED, never that a shell was fired where
  aimed.

**So: run a new check against the OLD code before trusting it.** Both the shell fix and the AP fix
were confirmed that way this session, and both negative runs are recorded with their numbers. A
check never seen to fail is not evidence.

**Both of these are now STANDING RULES in `CLAUDE.md`'s Debugging section**, so they apply every
session without anyone having to remember this file. They sit beside the sibling rules they
generalise — "prefer a PROBE to a detector" and "a search that finds nothing is not evidence of
absence".

**2026-08-10 added two more, both from the airstrike work:**

- **TAKE THE CONTROL SHOT.** Two write-ups in this file described the airstrike as "findable
  because one round falls nose-down while the rest fly arcs". Firing the identical drag with
  NOTHING armed shows that round anyway — it is the TANK SHELL, which fires on every volley for
  free. Nobody had ever actually seen the airstrike. An observation about a feature means nothing
  until the same observation has been made with the feature switched OFF.
- **A CHECK RUN ON AN EMPTY PURSE CANNOT FAIL.** The "test supply writes nothing to the economy"
  check was written against the editor's real balance of ZERO, so the broken code it was meant to
  catch simply could not afford anything, wrote nothing, and passed. Staking coins first gave it
  teeth. This is the same family as the `ReferenceEquals(Use(x), y)` refusal test from Tier 1.3 and
  the deleted phase-spread check from the flame work: **ask what STATE the check needs to be in for
  the failure to be reachable at all**, then put it in that state.

**2026-08-09 added a third costume, and it is the subtlest:** a check written against a NOTE IN A
DOC asserts the note, not the behaviour. The glyph check above was written as "ASCII only" on
CLAUDE.md's word, flagged 23 strings, and every one was a false positive. Assert against the
engine, the font, the device — not against the summary someone wrote of them.

### The other standing lesson: assume NOTHING is wired

**Nothing in this port is live just because it exists and has tests.** The running count is at
NINE systems found fully ported, unit-tested and reached by nothing — the economy, boss phases,
reinforcement waves, stages, ammo, consumables (2026-08-10), advancing squads + melee (the EIGHTH,
still unported, and the one holding Overwatch Flare) and **player cosmetics** (the NINTH, found
and wired 2026-08-11 by a grep that returned three hits, all inside the files that define them).
Every one but advancing squads is now live. **Grep for callers before believing a feature is
live.**

**Enemy factions were a different failure and worth telling apart**: not ported-and-dead but
ABSENT — the Unity tree had three comments mentioning a faction palette and no code at all, while
`DYNAMISM_DESIGN`'s status table called D1 shipped. Ported-and-dead greps as callers-inside-their-
own-file; absent greps as nothing. **Both look identical in the design docs.**

**And the design docs will not tell you.** Their Status tables say "shipped" about the **RETIRED
ANDROID BUILD** — consumables are marked shipped in `PROGRESSION_DESIGN.md` Phase 2 and
`DYNAMISM_DESIGN.md` Phase C, dated 2026-07-20, and nothing in this repo calls them. Read those
tables as the SPEC for what to build here, never as a record of what exists here. This is the same
failure as "a check written from a doc asserts the doc", one level up: **ask the code who calls it.**

Wind is worse than unwired: it is COSMETIC (`windAccelZ` drifts the round in Z; the collision test
is X/Y only), so **do not author a wind level or a wind schedule until wind does something**, and
making it real is a physics change that needs an ask.

Then read the traps sections — most cost a build to find, and several are invisible outside a real
device build.

Read this first, then `CLAUDE.md` for the standing rules, `LEVEL_AUTHORING.md` before touching a
level, and `SPIKE_RESULTS.md` / `MIGRATION_SCOPE.md` for port history. Everything below was
verified on the device, not assumed.

### Where the Tier 0 write-ups are

This file is chronological and long. The 2026-08-06 product work is at the end, in build order —
each section says what shipped, what it cost, and what it found:

| Section | What it covers |
|---|---|
| Data authoring moved into Unity | The importer disarmed, the sweep removed, the composition rules made checkable |
| Campaign split from the test rigs | RIGS gate; the renumbering chore retired |
| The victory screen and a live economy | The dead economy, and the TMP missing-glyph trap (which the 2026-08-09 section above CORRECTS — the font is not ASCII-only) |
| Campaign to twelve levels | The beat chart; wind found cosmetic; boss phases and waves wired |
| Enemy turn juice | Flash banner vs standing telegraph |
| Loadout | Slots vs points, and why that keeps the camera framing safe |
| Ruins, instead of blocks everywhere | Shed chunks were the real culprit, not the collapse |

**The design docs now live in this repo** (moved 2026-08-06): `GAME_DESIGN_LOCKS.md`,
`PROGRESSION_DESIGN.md`, `DYNAMISM_DESIGN.md`, `CAMERA_ARCHITECTURE.md`, `UNIT_VARIETY_DESIGN.md`,
`STRUCTURE_VARIETY_DESIGN.md`. They still govern.

**Product / retention direction (2026-08-06):** `PRODUCT_DIRECTION.md` — priority stack
(campaign spine → victory/meta juice → ammo/events → identity → daily/monetization), dopamine
model, 12-level beat chart, anti-goals, and soft-launch success criteria. Claude should plan
engagement/content work against that file; it does not override locks.

## Where things are

**Two repos, deliberately separate. Do not merge them.**

| | |
|---|---|
| `~/AndroidStudioProjects/ArmedConflict` | Kotlin + SceneView/Filament. **RETIRED 2026-08-06** — reference and data authoring only. |
| `~/UnityProjects/ArmedConflictSpike` | this repo → `github.com/rbfr/ArmedConflictUnity` |

Unity was chosen on 2026-08-04 after a four-step spike passed every criterion. Godot was
considered and dropped without spiking (`GODOT_SPIKE.md` in the Android repo is kept, not deleted).

Each repo has its OWN deploy key — GitHub scopes a deploy key to one repo, so the Android repo's
key cannot push here. This repo uses `~/.ssh/armedconflictunity_deploy` via the
`github-armedconflictunity` host alias in `~/.ssh/config`.

## What works

All 29 levels are reachable and play end to end at a steady 60 fps: drag to aim, volley, swept
collision, damage, structure collapse, turn handover, victory. With sound both sides, a per-level
biome backdrop, per-type projectiles, unit weapons, fading explosions, scorch marks, structures
that shed their own geometry and leave a ruin when they fall, a battle HUD, level navigation and
an Auto button. Units are ANIMATED — idle, a two-handed hold, recoil, death — and both lines raise
their rifles to the angle they are actually firing at.

**The product spine is in** (Tier 0, 2026-08-06): a 12-level campaign with the test rigs gated
behind a RIGS toggle, a pre-battle LOADOUT picker, a VICTORY CARD paying coins and stars with the
reason shown, mid-battle EVENTS that fire and announce themselves a turn ahead, and a live
economy — coins, stars, unlocks, first-clear and daily bonuses, milestone chests.

All eight `GameViewModel` slices are ported (`LevelBuilder`, `CollisionSystem`,
`ProjectileSystem`, `TurnFlow`, `CameraDirector`, `CosmeticSystems`, `HelicopterSystem`,
`EventSystems`) plus `GameState`, `Formation`, `SpringFollow`, `EnemyAI`, `CameraFraming`,
`TrajectoryPhysics`, `SweptCollision`, `ProgressStore`, `EconomyStore`. `data/` is complete at
24 levels — 7 campaign (one per biome) plus 17 test rigs.

**281 checks, all passing** (as of that date — it is 539 now; see START HERE). They assert the
behaviour the Kotlin comments describe, not just that the code compiles. Run them after every
change:

```bash
U=~/Unity/Hub/Editor/6000.0.80f1/Editor/Unity
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod PortSelfTest.Run -logFile -
```

## The workflow

Headless. The editor GUI runs over VNC on llvmpipe and is painful; you never need it.

```bash
U=~/Unity/Hub/Editor/6000.0.80f1/Editor/Unity
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod SpikeSceneBattle.Build -logFile -
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod SpikeBuild.Android  -logFile -

export PATH=$HOME/Android/Sdk/platform-tools:$PATH
export ANDROID_SERIAL=57121FDCQ005LC          # USB. The WIRELESS transport drops on long builds.
adb uninstall com.dullesengineering.armedconflictspike; adb install Builds/Step1.apk
adb shell monkey -p com.dullesengineering.armedconflictspike -c android.intent.category.LAUNCHER 1
adb shell input tap 180 2210                  # the AUTO button — drives a level from the terminal
```

`DISPLAY=:1` is mandatory for anything Unity/Hub. The app id is `...armedconflictspike`,
deliberately NOT the shipping id, so both builds sit on the phone for A/B.

### CHECK `mCurrentFocus` BEFORE EVERY adb INPUT BATCH. It is Rob's real phone.

**This has now hijacked into personal apps FOUR times**, most recently on 2026-08-10: DND had been
switched off at what looked like the end of device work, a notification arrived mid-sequence, and a
tap aimed at the RIGS button opened a private conversation instead. One frame of it was captured
and deleted; no further input reached that app.

```bash
adb shell dumpsys window | grep -i mCurrentFocus | grep -q armedconflictspike || exit 1
```

Put that line before each batch and abort on it, rather than trusting that the game was in front a
minute ago. Also:

- **DND ON for the whole session**, and only off when the phone is actually being handed back —
  not when device work merely looks finished. **`settings put global zen_mode 2` NO LONGER WORKS
  on this device** (2026-08-10): it returns success, `settings get` reads back `0`, and DND stays
  off — a silent no-op in the exact place a silent no-op is most expensive. Use
  `adb shell cmd notification set_dnd priority`, and VERIFY with
  `dumpsys notification | grep mZenMode` — `ZEN_MODE_OFF` means it did not take.
- **Never `KEYCODE_BACK` for in-game navigation.** Use HOME to leave, and uiautomator-found bounds
  to press things.
- **Restore what you changed**: auto-rotate, DND, `svc power stayon`.

The phone locks itself during long builds and a locked device backgrounds the app before
`Start()` runs — which reads as "no output" rather than as a lock. Check
`adb shell dumpsys trust | grep deviceLocked` before concluding anything from an empty log.

## Traps already paid for — do not rediscover these

**Unity/C#**
- Unity 6000.0 is **C# 9**. `record` works; `record class` is C# 10 and does NOT compile. `init`
  needs the `IsExternalInit` shim in `Assets/Scripts/Game/`.
- `GameState` declares **reference equality on purpose**. With ~90 fields the synthesized
  `Equals` chains ~90 `&&`, and IL2CPP exceeds clang's 256-bracket limit — the Android build
  fails outright. Value equality also bought nothing here (no StateFlow to conflate).
- `AssetDatabase.StartAssetEditing` DEFERS creation, so assets referenced by other assets made in
  the same batch serialise as `{fileID: 0}`. Do not batch the importer.
- A camera made with `new GameObject()` + `AddComponent<Camera>()` has **no AudioListener**, so
  nothing is audible, silently. Unity's default camera PREFAB has one; a hand-built camera doesn't.
- AudioClips must be preloaded, or the FIRST play of each clip is silent (`loadState=Unloaded`).
- IL2CPP segfaulted once mid-session. Deleting `Library/Bee/artifacts/Android/il2cppOutput`
  cleared it.

**Coordinates and rendering**
- `GameSpace.ToUnity` negates X. Unity is left-handed; with the camera at +Z looking toward -Z,
  screen-right becomes -X. Route EVERY placement through it — a mirrored scene looks plausible.
- The backdrop lives at NEGATIVE z. Unity's Quad primitive faces -Z, so it needs a 180° turn,
  and hand-built silhouette winding must be CCW from +Z or it is back-face culled.
- Backdrop geometry must be sized against the frustum AT ITS OWN DEPTH, not in absolute units.
  Use `Backdrop.DesignAspect`, never `Screen`: batchmode reports a placeholder DESKTOP resolution,
  and a landscape aspect makes every layer ~3x too wide.
- Pooled objects share a material: per-instance tinting needs a `MaterialPropertyBlock`.
- The SKY QUAD must be sized to the visible band at its own depth (280 tall at y=35, z=-120), and
  both directions are traps. Too short and its top edge is inside the frustum, so the camera's
  clear colour shows above the sky — the game shipped for weeks with a dark slab across the top
  9% of the screen that read as a HUD panel. Too tall and the gradient, which spans the QUAD and
  not the frame, stretches until only its bottom third is on screen and the sky goes flat.
- The GROUND PLANE must stop just in front of the nearest backdrop layer (far edge z = -28). It
  ran to -150, BEHIND the whole backdrop, so wherever a silhouette dipped, distant ground showed
  through above the horizon as a floating tan wedge. The backdrop makes the horizon; a ground
  plane that outruns it is a second, contradictory one.

**Data import**
- The pipeline is **ONE WAY**: Kotlin → `tools/export_kotlin_data.py` → `data.json` →
  `DataImporter` → ScriptableObjects. **Do not hand-edit the ScriptableObjects** — a re-import
  silently overwrites them. Edit levels in the Kotlin and re-export.
- Colour literals arrive under `__args` (positional-only ctor) OR `__positional` (mixed). Reading
  one imported every background pure BLACK, with a correct-looking asset count.
- Read ARGB doubles straight to `long`. A `float`'s 24-bit mantissa cannot hold `0xFF4A90D9` and
  the loss lands on the low byte — every colour came back with blue = 0.
- `val EnemyRifleman = Rifleman.copy(...)` parses as a ctor named `Rifleman.copy`. Missing that
  dropped all four Enemy* variants and with them every enemy reference in every level.

**The backdrop, rebuilt 2026-08-05**
`ArmedConflict.Render.Backdrop` (runtime, MonoBehaviour-free) owns the DESIGN — per style, a list
of layers each reduced to a sampled height profile; `SilhouetteMesh` turns a profile into a strip;
`BackdropRuntime` does the GameObjects and materials. Per-level biomes are LIVE — the plan
builds at runtime from each level's own BackgroundDefinition.

The original drew each layer as a row of INDEPENDENT isosceles triangles, which is why the
mountains read as pyramids. What the rewrite is actually made of, and each of these was a visible
failure first:
- A ridge is ONE continuous silhouette. Profiles normalise to `[floor, 1]`, and the floor matters:
  at floor 0 the valleys drop to nothing and two layers read as two separate GROUPS of peaks
  rather than one range behind another.
- Ridged fBm WITHOUT the textbook per-octave weighting. The weighting is right for a heightmap
  seen from above and wrong for a silhouette — it starves the shoulders and yields needles.
- Snow is a cap on the crests that earn it (line at 0.82 of height, 0.58 for Winter) on a
  WANDERING line. A flat line reads as a ruler; a sine-jittered one reads as surf.
- Depth ordering has to be carried by SIZE as well as haze: the near mountain row is foothills at
  about half the far range's angular height. At near-equal sizes the pale layer read as glass.
- Every body-relative shape is judged at gameplay framing. City blocks needed 3x width variation
  and a low rubble floor or they read as a PICKET FENCE; pines needed crowns overlapping their
  neighbours or the row read as GRASS.

`BackdropPreview.Shots` renders all seven biomes to `Builds/backdrops/*.png` headless in seconds —
use it. The campaign is now one level per biome, so judging the backdrop from a single level
sees a seventh of the game. `PortSelfTest` also covers the plan
(layer widths, profile range, depth ordering, snow coverage).

**Unit art — the CC0 rig prototype, 2026-08-05**
Kenney's Blocky Characters 2.0 (CC0, `Assets/Models/Kenney/`, licence kept beside the models) is
wired in as a free stand-in to answer the engineering questions before any pack is bought.
`SpikeSceneBattle.UseKenneyUnits` is the A/B switch — one const, rebuild the scene, nothing else
in the scene changes. It is currently TRUE, so the scene builds the stand-in, not shipping art.

What it settled:
- **Our own units cannot be animated at all as they stand.** They are grouped by MATERIAL
  (`accent_*`, `skin_upper_*`) rather than by limb — five flat mesh nodes, no elbow to bend.
  Kenney's rig is `root → leg-left, leg-right, torso → (arm-left, arm-right, head)`: six boxes,
  **0 skins, 0 bones**, 72 triangles, 27 clips of plain TRS curves. Any animated future needs the
  Blender builder re-authored around a limb hierarchy, whoever's meshes we end up using.
- **Animation is free here.** Whole-process CPU, L1 idle, three 20s samples each: static Blender
  units 81.5 / 82.4 / 80.4%, 19 animating Kenney units 80.8 / 80.0 / 80.1%. The animated build
  measures LOWER than the static one — the difference is inside the noise. Expected, given there
  is no skinning to do. Caveat: L1 fields 19 units, not 30, and /proc CPU% is a blunt instrument.
- **Team colour by tint works** at gameplay distance — green vs red reads instantly — but it
  multiplies over the character's whole texture, so it stains the face too. A real pack needs a
  tint MASK or per-side textures.
- Open cosmetic gaps in the stand-in: Kenney's proportions are squat next to the current soldiers,
  and the gun is still a separate object floating at chest height rather than held in the hands.

`UnitAnim` (runtime) is the whole integration: Legacy `Animation`, four clip names, a `Desync` so
a line of units is not a chorus line, and a re-arm on hidden→visible because a recycled slot comes
back holding the death pose. `BattleRunner` fires it from the three volley paths and swaps the
ragdoll's topple rotation for the `die` clip — applying both makes a body fold AND spin flat.

**And then the real thing: OUR soldier on that hierarchy (`RiggedUnits`, `Art = UnitArt.Rigged`).**
`tools/blender/build_unit_rigged.py` in the Android repo builds the rifleman around Kenney's joint
names at OUR proportions (hips 49% / shoulders 78%, against their cartoon 37% / 67%), 212 tris.
Verified on device: rifle line at the ready, volley, death, 60 fps, four-tone team colours with no
tint and no stained faces.

Three constraints bind, and only these three — the rest is free:
- **Node names and paths must match exactly.** Legacy clips address curves by path.
- **Model height must be 2.70**, Kenney's. Every clip is rotation-only EXCEPT `die`, which also
  translates `root`, in model units.
- **The soldier must face glTF +Z**, so it is built facing Blender **-Y** — the opposite of
  `build_units_v6.py`'s "faces +X". Rotation curves are local, so a model facing +X gets arms that
  swing out sideways.

Four traps, all of which fail SILENTLY and each of which cost a build:
- Kenney's curve paths are `character-m/root/torso/arm-left` — two segments longer than ours, so
  every curve binds to nothing and the limbs just never move. `RiggedUnits.Retarget` rewrites the
  prefix; `Probe` prints both sides before you trust it.
- A retargeted clip **must be saved as an asset**. A prefab cannot reference an in-memory clip; it
  serialises as null and the unit comes back unanimated with nothing logged.
- `AnimationClip.legacy` must be set **after** the curves go in. SetCurve silently no-ops on a clip
  already marked legacy.
- `die` animates the ROOT's rotation, so the facing rotation cannot live on the same transform the
  clip drives or the first frame of a death snaps the corpse to face the camera. Hence the extra
  `facing` pivot above the animated node.

`RiggedUnits.Verify` is the guard: it samples the built prefab and fails if a joint that HAS a
rotation curve never moves. Sample ACROSS the clip, not at its midpoint — a breathing idle returns
to neutral there, which reported four working joints as frozen on the first run.

Layering is the other half. Troops hold a rifle at rest, but `idle` is a whole-body loop that
swings the arms down, so `holding-both` runs on a higher layer restricted to the two arms by
mixing transforms, and `holding-both-shoot` sits above THAT or firing is invisible. The weapon
hangs off `arm-right` and `BattleRunner` suppresses the pooled gun for any unit carrying its own —
the pooled ones are placed from the unit's root at a fixed chest offset, which is fine for a body
that never moves and visibly wrong the moment an arm does.

**A lesson that recurred four times**
Verify CONTENT, not counts, and prefer positive evidence over a plausible cause. Backgrounds
imported with the right count and no colour. Audio had correct clips, correct triggers, correct
volumes and no listener. The camera "hitched every drag" because a max was latching. Sounds fired
for events that never happened because they were inferred from list-length deltas. In every case
the instrument was wrong, not the engine.

## Level navigation — DONE, 2026-08-05

All levels are reachable (29 at the time; 24 after the biome cut below). `LevelScenery` (runtime) builds the ground, structures, props and
biome backdrop from the level asset and tears them down again; `BattleRunner.LoadLevel(index)` is
Clear + Build. RESTART / NEXT LEVEL appear on the victory/defeat screen, and a ◀ ▶ stepper with a
level readout is always on so the whole set can be swept from adb without a rebuild per level.

What that touched, and the parts worth knowing:

- **Nothing about a level is baked into `Battle.unity` any more.** Baking is what made a second
  level unreachable, and it also meant the one biome L1 happens to use was the only one anybody
  saw in the game. `Assets/Editor/BackdropBuilder.cs` is GONE, replaced by
  `Scripts/Render/BackdropRuntime.cs`; `BackdropPreview` now renders through that same code, so
  the preview and the game can no longer drift.
- **Runtime Materials/Textures/Meshes are not reclaimed when their GameObject dies.** Unity
  collects assets, not instances. Every one `LevelScenery` creates is tracked and destroyed on
  Clear — skip that and walking the campaign leaks a backdrop per level, which is the exact shape
  of the Android build's "a session gets progressively more expensive" defect.
- **Pools are still built ONCE and survive a level switch.** Minting render slots mid-session is
  the failure the Filament build paid for repeatedly. What has to be reset is everything that
  reads a slot's PREVIOUS occupant — a hidden slot still holds the last level's pose and position
  (`HideAll`), and the scorch pool needs re-materialling because its tint comes from the level's
  own ground colour.
- Model prefabs reach the runtime as a name→prefab table on `LevelScenery`, filled by
  `SpikeSceneBattle` from `Assets/Models`: there is no AssetDatabase in a player. Kenney's models
  are excluded and a duplicate bare name is logged, because the table would silently overwrite
  and one structure would quietly render as another.
- **31 more GLBs were imported.** Only outpost/sandbags/rifleman/projectiles had ever been
  brought over — enough for L1, and nothing else.

### The trap that only a device build can show: NO `CreatePrimitive` IN RUNTIME CODE

`GameObject.CreatePrimitive` always attaches a Collider, and IL2CPP MANAGED STRIPPING removes
collider classes from a build that never otherwise references them — this game has no physics at
all. On device the first call logged `Can't add component because class 'MeshCollider' doesn't
exist!` and then threw ArgumentNullException on the `Destroy` of the collider that was never
added, taking the whole level build down with it. The app launched to an empty scene.

It could not have shown up earlier: editor code strips nothing, so the same call is fine in
`SpikeSceneBattle` and `BackdropPreview`, and every primitive used to be baked at author time.
`PortSelfTest` and the headless scene build both passed clean immediately before it.

`Render/QuadMesh.cs` is the fix — a shared unit quad carrying Unity's own vertex layout (normal
-Z, so every caller's 180° face-the-camera turn and the scorch's 90° lie-flat stay correct), plus
`Create(name, parent, mat)`. It fixes the root rather than null-guarding the Destroy: a collider
on a backdrop quad was never wanted. **Use it for any new runtime geometry.**

### The other build-order trap: CreateAsset REPLACES, so references taken earlier dangle

`MakeScorchPrefab` calls `AssetDatabase.CreateAsset` on `Scorch.mat`, which does not overwrite in
place — it replaces the asset and mints a NEW guid. `WireScenery` ran first and loaded the old
one, so `scorchSource` serialised as `{fileID: 0}`: one null among dozens of correct references
in a scene file that otherwise looked perfect. On device it threw ArgumentNullException from
inside Material's copy constructor. The prefab is now built BEFORE the scenery is wired, and
`WireScenery` logs an error at build time for any null material — this class of failure should
never again reach a device to be diagnosed.

### Two silent data-loss bugs found on the way, both now fixed

Neither could show up while only L1 was reachable, and neither was visible in any count.

1. **`FortressTier` never imported at all.** `val FortressTier = FortressTierUnscaled.scaled()`
   has no `.copy` in it. The exporter's ident reader swallows dots, so it arrives as a ctor NAMED
   `FortressTierUnscaled.scaled`, and `extract_vals` accepted only the `.copy` form — so it was
   dropped. Worse, a bare identifier does not start with a wanted ctor name, so `looks_wanted`
   was false and it was not even recorded as unparsed. **Five levels place it** (L6, L9, and the
   bastion / structure-parade-B / tier-collapse rigs) and every one threw a
   NullReferenceException on load. Fixed in both `export_kotlin_data.py` and `DataImporter`:
   any DERIVING method counts, not just copy.
2. **`Capture` dropped every optional field**, so a `.copy()`/`.scaled()` that did not restate one
   silently lost it. It hid because the wide and small tiers restate all of theirs, and the one
   val that restates nothing was the one being dropped by (1). The three PLAYER fortress tiers
   were live victims: no `hitWidth` (so the collision box fell back to `size`) and NO damage
   chunks (so a player structure could never shed geometry). Now captures hitWidth, deckY,
   cannon, flagMount and damageChunks — hitWidth/deckY only when the base HAS them, since their
   presence is the signal and an unconditional -1 reads as "measured, and it is -1".

`PortSelfTest` now builds an initial state for EVERY level, checks `levelNumber == index + 1`
(the switcher indexes by position), and checks every structure and prop the campaign places has
an imported model. That check finds this class of bug in the same second as a typo; a device
sweep finds it at about a minute a level.

## Forest reworked, and the preview was lying — 2026-08-05

**`BackdropPreview` rendered EVERY biome as bare sky and ground, and reported success.**
`EditorSceneManager.NewScene` triggers an unused-asset unload, and a freshly emptied scene
references nothing — so a `BackgroundDefinitionSO` loaded BEFORE it has its native object freed
and becomes Unity's fake null: `bg == null` is true while `bg.style` and `bg.groundColor` still
read correctly off the managed wrapper. The old preview never noticed because it only ever read
fields; `BackdropRuntime` opens with a null guard, right for the game and silently true here.
Fixed by loading the background AFTER the scene, and the preview now logs an error if a biome
builds zero layers. **Do not trust a preview you have not sanity-checked against the device** —
this one passed the eye test for a whole session by producing plausible sky-and-ground images.

**Forest read as GREEN MOUNTAINS**, on the one campaign level that uses it (L2). Two causes:
- The hills were made TALLER than the treeline (15 units vs 11) to stop the ridge hiding behind
  the woods. That won the argument and lost the biome — the pale ridge owned the skyline.
- Nine crowns spanning the frame makes each one an eighth of the screen wide, and a triangle that
  wide is a hill however it is shaded.

Now ordered by ANGULAR height — hills 0.22 < mid trees 0.30 < near trees 0.42 — so the trees own
the skyline and the hills show through the gaps as a backdrop mass. `Treeline` gained two
parameters rather than having its constants fought: `crownScale` (a conifer at this distance is
about half as wide as it is tall; at 1.0 with a high count the spire comes out nearer a fifth,
and a row of those is REEDS) and `floor` (the solid canopy mass under the crowns — at 0.35 the
sky came down between every pair and the band read as a fringe). **The floor also WANDERS now**:
a constant one is a ruler laid across the full frame, the same failure a flat snowline has.

Both documented failure modes were re-hit while tuning this — 24 narrow trees gave the "reads as
GRASS" result exactly as the old comments predict, and 9 wide ones give hills. The window is
narrow; change count and crown width TOGETHER, and judge which band owns the skyline.

## Ocean ported and given a level — 2026-08-05

`BackgroundDefinitions.Ocean` was authored and referenced by NOTHING, in the Kotlin and the port
alike, so no build had ever displayed it. It now has **L30 `TEST — Oceanfront`** — authored in the
Kotlin and re-exported, because the pipeline is one way. The campaign+test total is 30; it was
APPENDED rather than filed with the other rigs, since the switcher indexes by position and
inserting mid-list would silently renumber everything after it.

The plan itself was one flat teal band. Ported from the Filament `drawOcean`: sea gradient, a sun
with a radial glow sitting ON the horizon, the scattered sun-glitter path, and the scalloped foam
surf line. **The ripple rows are NOT ported** — a ripple is a wavy LINE and the decal mechanism
draws rectangles, so away from the sun they read as debris floating on the water. That wants a
strip mesh like the silhouettes have. The drift does not need porting at all: the Filament version
scrolls each row by a hand-tuned fraction of pixels-per-unit, and here real depths parallax free.

Three traps, all of which cost a render:

- **`Mathf.SmoothStep` is NOT GLSL's `smoothstep`.** It is a smoothed LERP BETWEEN its first two
  arguments, so `Mathf.SmoothStep(0.26f, 0.34f, d)` returns a value in [0.26, 0.34] for every d
  and `1 - that` never falls below 0.66. That is a near-constant alpha across the whole quad,
  which drew the sun as a cream RECTANGLE with a brighter blob in it. `BackdropRuntime.Threshold`
  is the real thing. **Note `MakeScorchPrefab` uses the same call** and gets away with it only
  because its edges happen to be 0.45 and 1.
- **Anything shaped by alpha must clone a TRANSPARENT material ASSET** (`BackdropFadeSource.mat`).
  `unlitSource` is opaque and a copy ignores alpha entirely. Flipping `_Surface` and the blend
  modes on the copy at runtime is not a reliable substitute.
- **A layer sunk the way a RIDGE is sunk disappears behind the ground plane.** The surf was
  authored at BaseY -1.9 of a 2.8 band, so the ground occluded all but the tallest scallops and
  the foam came out as one straight white rule — the exact thing it exists to prevent.

And one that only the DEVICE could show, because `BackdropPreview` renders from x = 0:
**a fixed backdrop feature is offset from the WORLD ORIGIN, not from the frame.** The backdrop is
world-fixed and the camera is not — at Aiming it sits over the PLAYER LINE, around game x -9.5.
A sun placed at a frame-relative-looking -0.20 of the sea width landed 92% of a half-frame right
of that centre and was cut in half by the screen edge, while looking perfectly placed in the
preview. Judge any fixed feature at the camera position the PLAYER sees, and leave it room to
travel: the pan is real parallax, so the sun crosses the frame during a volley.

## Campaign cut to ONE LEVEL PER BIOME — 2026-08-05

Seven campaign levels, one per background: L1 Mountains, L2 Forest, L3 MountainsDusk, L4 Winter,
L5 Desert, L6 CityRuins, L7 Ocean (promoted from the test rig). Six levels whose biome was already
covered were DELETED from the Kotlin — they are in git. The 17 test rigs are kept for reference
and renumbered to L8-L24. Four stages over the seven, 2/2/2/1, gates at 0/3/6/9.

Total is now **24**, not 29. Two things in this repo carried the old count and both are fixed:

- **The importer never deleted ORPHANS.** It creates and updates, so a level removed from the
  Kotlin left its `.asset` behind — and `SpikeSceneBattle` collects EVERY `LevelDefinitionSO` it
  can find and orders them by `levelNumber`, so a deleted level rejoined the campaign silently at
  whatever number it used to hold. Six were stranded. `DataImporter` now sweeps any level asset
  the Kotlin no longer declares. The Kotlin is the source of truth in BOTH directions.
- **`BuildSandboxLevels` was a SECOND source of truth for level numbering.** The exporter cannot
  parse `rosterSandbox`, so the importer rebuilds those eight — with their numbers hardcoded at
  21-28. The Kotlin renumbered them to 16-23 and the importer silently did not, breaking
  `levelNumber == index + 1` and with it the level switcher. It now derives the number from the
  level's position in `levelOrder`. The composition is duplicated because it has to be; the
  ordering is not. **`PortSelfTest` caught this** — it is exactly what that check is for.

The Android repo's long-standing test failure is also gone. `FactionPaletteTest` hardcoded level
numbers 1/7/13/19 as one-per-stage, which were correct for the ORIGINAL 25-level campaign and
meaningless after it was rebuilt — by now two of the four were TEST levels, which sit in no stage
and deliberately fall back to the last one, so it asserted 4 distinct factions against 2. It now
derives its numbers from `StageDefinitions`. **50 tests, 0 failures.** A test that hardcodes level
numbers expires the next time the campaign is re-cut.

## Structures shed their own geometry — ported 2026-08-05

Reported as "just squares/bricks that fly" against the Filament build's real damage. The port had
the DATA (`damageChunks`, measured per structure), the entity field (`StructureEntity.ShedChunks`)
and the curve (`StructureDamage.ShedChunkCount`) — and nothing called any of them. Destruction
threw ten random cubes sized off `size`, so a hit building shed bricks that had never been part of
it, and only ever at the moment it died.

Now, as in the Kotlin: `chunk_N` groups vanish from the model in ascending N as HP drops, and the
tick spawns the SAME group as falling rubble from exactly where that geometry stood. The gap in
the silhouette plus the pile at the foot is the damage read, and it persists for the battle.

Both halves derive from `ShedChunkCount` — the renderer reads the tick's own `ShedChunks` rather
than recomputing, so they cannot disagree and drop a piece the building still has.

Carried across from the Kotlin, each of which was a visible failure there first:
- A group splits along its LONGEST axis, so a sandbag course scatters as bags instead of dropping
  as one long bar.
- A piece is sized from its VOLUME, cube-rooted and clamped — NOT the mean of its dimensions. The
  mean is dominated by the long axis of a flat plate: a wide tier's wall plate means out at 0.73,
  three times the largest destruction chunk, which read as slabs bigger than the wall they fell off.
- Barely thrown (vy 0.5, vx spread 0.9): it is coming loose under its own weight, so it reads as
  falling OFF the building rather than being launched.

Unity-side notes: chunk groups are collected ONCE at scenery build time, because grouping is a
string parse over every child node and doing it per frame per structure is the per-slot rescan the
Filament profile warns about. Grouping is by TRAILING NUMBER, not prefix — `chunk_3`,
`accent_chunk_3` and `trim_chunk_3` are one group, and matching the prefix would shed a wall's
stone and leave its trim hanging. Renderers are toggled rather than GameObjects, since a chunk
node may carry children.

Verified on device on the demolition rig: the garrison post's wall panels vanish one at a time as
HP falls 225 → 121, and shed rubble settles against its base.

## Open items — in the order I would take them

**This list was written before Tier 0 and most of it has SHIPPED.** Kept for the reasoning, which
is still the record of why each thing was worth doing. Current state:

1. ~~Unit art: every class renders as the same rifleman.~~ **DONE 2026-08-06.**
2. **A decision, not a task: re-tune incendiary, or leave it.** STILL OPEN. `burnDamage = 6` was
   calibrated to finish the 8hp Sniper in one tick and that unit no longer exists (the roster cut
   gave the Sniper the Marksman's 16hp). Deliberately NOT raised — doubling a 300-coin consumable
   is a balance call, not a side effect of deleting a class — so a tick is now a ~37% chip rather
   than a kill. `AmmoTest` anchors to the roster's frailest unit, so it will not silently expire.
3. ~~Loadout screen.~~ **DONE 2026-08-06** — see "Loadout" below.
4. **`snowfall` is imported and ignored** — Winter's falling flakes are not ported. STILL OPEN,
   and still low value: Winter is one campaign level.
5. **Release build gaps** — debug-signed, APK not AAB, `versionCode` never increments. STILL
   deliberately deferred; see the README.
6. ~~The unit parade rebuilt to a single row of six, unverified.~~ Swept on device since.

**The one thing Tier 0 itself owes is the BALANCE AUDIT** — see START HERE.

### Things that will bite, gathered in one place

- **`Auto` cannot test STRUCTURES.** It targets the nearest enemy UNIT, so on any rig whose only
  enemies are the off-screen immortals it throws the whole volley past the buildings and structure
  HP never moves. This is why "rubble never observed falling" survived for weeks. Structure work
  needs a real aimed drag — the demolition rig copies L2's geometry so the shot is solvable:
  16 units, range = v²/g, so v = 8, i.e. 89% of the 9 maximum at 45°.
- **Enemy structures are OFF-FRAME at aiming framing, and that is correct.** The Aiming camera
  frames the PLAYER LINE ONLY, so every campaign level looks structure-less in a still. Drive a
  volley and the follow camera pans onto them.
- **The device drops off USB.** Twice in one session, not enumerating in `lsusb` at all; `adb
  kill-server` does not recover it and it needs a physical replug.
- **Never judge a visual from the preview alone.** `BackdropPreview` renders from x = 0 while the
  game camera sits over the player line, and it silently rendered every biome as bare sky and
  ground for a whole session (Unity fake-null after an unused-asset unload).

### Device sweep — DONE 2026-08-05, at 29 levels

Every level loaded on the Pixel 10 Pro XL via the ◀ ▶ nav, in the right order, with no exception
and no missing-model warning. **Per-level biomes confirmed on device** — green, desert,
city-ruins and winter backdrops all appear, which no build before this one could show.

Swept BEFORE the campaign was cut to 7 biome levels; the 7 survivors were re-swept afterwards and
all load. The 17 test rigs have not been re-swept since the roster cut, and two of them were
rebuilt by it (the unit parade and the demolition rig), so that is the cheapest sanity pass if
anything looks wrong.

On-screen buttons sit clear of the status-bar and gesture insets so an adb tap cannot land on the
system UI: ◀ (880, 235), ▶ (1000, 235), AUTO (180, 2259).

## Owed to the ANDROID repo

- ~~The garrison-ceiling bug is probably live there.~~ **FIXED there 2026-08-05**: `hitsStructure`
  now bounds the box by `deckY` where one is measured, with a regression test. Re-measuring the
  whole set says the outpost was the only mismatch.
- **Nothing is owed and nothing is uncommitted.** That repo is at `f9af006` on
  `projectile-refinement`, pushed, working tree clean, 50 tests 0 failures. The branch is 11
  commits ahead of its own `main` and has never been merged or PR'd — GitHub offered
  `https://github.com/rbfr/ArmedConflict/pull/new/projectile-refinement`.
- ~~**Game DATA still lives there.**~~ **NO LONGER TRUE as of 2026-08-06** — authoring moved into
  Unity and the ScriptableObjects are the source of truth. Nothing is owed to that repo now, and
  nothing in it needs to be edited to change a level, a unit, the roster or a stage.

## Per-class unit art — DONE, 2026-08-06

Every unit class used to render as the same rifleman. It now renders as itself: seven rigged
silhouettes (six crowd classes plus the hero), on the SAME skeleton, so one set of retargeted
Kenney clips still drives all of them. Verified on device — the L9 parade shows six readable
outlines, a 24-level sweep logs no missing slots, and a four-volley run on L18 (26 v 26) holds
60 fps with no exceptions.

Three things had to change together, and only the first is art:

**The models.** `tools/blender/build_units_rigged.py` in the Android repo supersedes
`build_unit_rigged.py` (which built the rifleman alone as the go/no-go test). It ports v6's
per-class props — ghillie, ammo drum, rocket tubes, shell bags, riot shield, greatcoat and cap —
onto the limb hierarchy, keeping v6's own measurements and comments, because those numbers are
the output of seven documented attempts in `UNIT_VARIETY_DESIGN.md`.

- **POSE is gone and that is fine.** v6 differentiated partly with a lean, a hunch and a
  fore/aft stagger; the idle clip owns those now. Per that doc every pose-only pass was reported
  as "the same soldier" at gameplay scale, so the loss is small. STANCE survives — a leg pivot's
  position is free, and the machine gunner still stands wider than the sniper.
- **Z is remapped through LANDMARKS, not scaled flat.** v6 puts a shoulder at 72% of height and
  the rig at 80%, and the hero has its own landmarks again (its waist is a belt at 0.86, not a leg
  seam at 0.67). A flat `z * K` floats a pauldron most of a shoulder off the body and the hero's
  cap a head above its neck.
- The port is checked by MEASUREMENT: `python3 tools/measure_units.py` reports the legs/torso/head
  band profile for the whole set, and `--legacy` measures the v6 originals for comparison. The
  rigged set reproduces the legacy spread almost exactly (hero 37/32/27 px against 38/33/29).
  **Judge the SPREAD ACROSS THE SET, never one class alone** — hitting every individual target is
  what destroyed the spread in that doc's Attempt 7. Note the projection plane differs from
  `measure_structures.py`: a unit is seen in PROFILE, and UP IS ALWAYS glTF Y (Blender's exporter
  converts Z-up on the way out — reading Z as up measures the model from above and every band
  comes back the same width, which is exactly what the first run of that tool did).

**The fourth tone.** `Tone()` implemented skin / accent / uniform and had no `trim`, so every
prop above fell through to the side's uniform colour — the ghillie, the ammo drum and the rocket
tips were all just more green. `RiggedUnits.TrimColor` carries SceneHost's per-class palette over
verbatim. Trim is held CONSTANT across both armies on purpose: the uniform says which side a
soldier is on and the trim says which class he is, and a faction palette touching the trim would
collapse the two readings into one.

**Per-class render slots.** `BattleRunner` pooled one prefab per side, which cannot work once the
classes have different geometry — swapping the model on a live slot is exactly the mid-session
mint the Filament build kept paying for. `UnitSlots` is a pool PER CLASS per side, and the sizes
come from the level data (`ClassCounts`), not a constant:

- Live units and RAGDOLLS share a pool, so a class is sized by everything a level ever SPAWNS
  rather than by everything alive at once — a corpse holds its slot while the live roster shrinks.
- Index arithmetic that assumed one flat pool had to go. `VolleyAnim` used to fire the first N
  slots; with per-class pools "the first N" is the first N of whichever class enumerates first,
  which would fire some soldiers twice and leave others standing. It reads `UnitSlots.Live`, which
  `SyncUnits` fills in roster order.
- `PortSelfTest` asserts every class the campaign FIELDS has both a rigged model and a per-side
  prefab. A class added to the Kotlin roster with no builder fails there in a second; without it,
  it is a soldier who never appears, found on a device.

**And `renderScale` reached the port as a formation number only** — it spread the heroes apart
and never made them bigger, so a hero authored at 1.9x rendered at exactly crowd size. Invisible
while every class shared one model, and the whole point of the hero the moment it has its own
greatcoat-and-cap body. `SyncUnits` now multiplies it onto the prefab's normalised scale.

## Health bars and the free camera — 2026-08-06

**A damaged unit now carries a health bar.** Before this, a wounded soldier was audible and
nothing else — the tick counted `TotalWoundedHits`, but a running total can say that SOMETHING was
hit and never WHICH, and with 32 HP against 8 damage most hits wound rather than kill, so the
common case was the unreported one.

- **Hidden until the unit has taken damage**, which needs NO new state: "has been hit" is
  `Hp < Definition.maxHp`. A whole line at the start of a turn carries nothing.
- **It FADES OUT a few seconds after the hit** (`CosmeticSystems.HealthBarSeconds`, 3s, with the
  last 0.7s spent fading), driven by `UnitEntity.LastHitAge` rather than by "is currently
  wounded". It first shipped persistent-while-damaged and that was rejected in play: the player
  has read the hit by then, and a bar over every damaged survivor turns a 26-strong line into a
  second HUD laid on top of the army. Re-armed from zero on every hit, so a unit under sustained
  fire keeps its bar rather than having it expire mid-bombardment.
- **BOTH quads fade, not just the fill.** Fading the coloured fill alone leaves the dark backing
  plate behind as a floating black tick over the soldier's head — a worse artefact than the bar it
  was retiring.
- **The material has to be a TRANSPARENT asset** (`HealthBarFadeSource.mat`, from the same
  `FadeSource` helper the ocean sun uses). An opaque URP/Unlit ignores alpha completely: the bar
  would hold full strength and then vanish on a single frame, which is the failure this repo
  already paid for once on the backdrop.
- Green above 0.6, amber above 0.3, red below. The fill is anchored to the LEFT edge, so damage
  eats it from one side; a centred fill shrinks toward the middle from both ends and reads as a
  charging meter rather than a wound.
- **The fill never drops below `BarMinFill` (22%) of the track**, and the empty track is DARK RATHER
  THAN BLACK. Reported as "I see a black health bar — shouldn't that mean they're dead?", and that
  was the cue failing exactly where it mattered most: the bar is ~30px wide, so a linear fill at
  25% health is SIX PIXELS of colour against a near-black track, which reads as a broken bar rather
  than as a dying soldier. The floor deliberately breaks the linear mapping at the bottom end,
  which is the right trade — down there the COLOUR carries the message and the exact fraction does
  not, and a message too small to see carries nothing. Note the COLOUR is still picked from the
  TRUE fraction; flooring both would make a dying unit read as merely wounded.
- **Both sides.** The tactically useful reading is which ENEMY is nearly dead, and the player's
  line has to answer the same question when it is being shot at.

Three things about how it is built, each of which is a rule this repo already paid for:

- **Sized against `UnitGeometry.UnitScaleUnits`**, like every body-relative thing here. The WIDTH
  is bounded by `Formation.MountedColumnSpacing` (0.187) rather than by the body: a garrison packs
  tighter than a ground line, so a bar sized to look right on open ground overlaps its neighbour's
  on a parapet — which is exactly where damaged units most need counting. It does NOT scale with
  `renderScale`; only its height offset does, so a hero's bar clears his cap without becoming a
  bigger, more important-looking bar.
- **Quads come from `QuadMesh`, never `GameObject.CreatePrimitive`** — IL2CPP strips the collider
  classes CreatePrimitive silently attaches, and on device that took the whole level build down.
  The bar is turned to face the camera with a 180° flip about **X, not Y**: turning about Y would
  also mirror local x, and the fill anchors to one end, so the bar would drain right-to-left.
- **Pre-warmed with every other pool**, sized from the level data. A bar minted the frame a unit is
  first wounded is a render slot created mid-gameplay, which is the failure the Filament build paid
  for repeatedly.

The bar REPLACED a hit flash (a near-white tint for 0.12s) built earlier the same day. The flash
worked and was rejected on the ask: it says a unit was hit and cannot say how badly, and "how
badly" is the part that changes what you aim at next. Its `HitFlashAge` field, tick step and
self-test checks were all removed rather than left dormant.

**And the free camera is back**, ported from Android's `ui/battle/DebugCamera.kt`: a CAM button
beside the level stepper, a six-button pad, and a live x/y/z readout. It HOLDS, through volleys and
the victory screen — that is the whole feature. It confirmed L1's bunker garrison stands correctly
on its deck in about ten seconds, which is the kind of question that otherwise costs a volley, a
screen recording and a frame hunt.

Two things it is worth knowing about:

- **Its x is GAME space, not Unity space.** `GameSpace.CameraX` negates, so a raw Unity x made the
  "→" button pan the view LEFT — it visibly did on the first device run. The readout matters as
  much as the button: it exists to be written down and compared against level data, which is
  authored in game x, and a tool that reports the mirror image of the coordinate you are hunting
  is worse than no readout at all.
- **It suppresses shake.** A tool for judging whether a thing is in the right PLACE cannot have
  the view jittering under it.
- **The pad is HELD, not tapped** (`GUI.RepeatButton` — a plain `Button` only fires on release,
  which is why the first version cost a tap per step). Movement is a RATE integrated against dt,
  not a per-frame step, and it ACCELERATES to 4x over 1.2s of holding: crossing a level is ~15
  units, nearly four seconds at a flat rate and about one and a half ramped, while the first
  moments stay slow enough to place the camera precisely. Measured on device: one 2s hold on OUT
  moved z 6.26 -> 31.16, which is 50 taps of the old pad; a 0.15s tap still moves 0.73 units.
- The held direction is recorded in OnGUI and CONSUMED IN UPDATE. OnGUI runs several times per
  frame — once per input event plus Layout and Repaint — so moving the camera inside it applies
  the movement an unpredictable number of times and the speed then depends on how much input the
  OS delivered.
- **A touch that starts on the pad is excluded from the aim drag.** With tap-to-step this never
  mattered, since `Release()` ignores a drag under a threshold and a tap barely moves; a finger
  resting on OUT for two seconds drifts on the glass, and on release that fired a volley and ended
  the turn. The camera tool must not be able to play the game.
- **From adb, `input tap` is now too brief.** Press-and-hold is `input swipe X Y X Y 600` — the
  same point twice, with a duration.

### Method note, because it cost an hour

The flash was diagnosed as "not rendering" from a screen recording twice before it turned out to
be working the whole time. Both times the detector was wrong, not the code: the first pass hunted
near-white pixels on WINTER ground, which is near-white, and the second sampled frames five
seconds after the volley instead of the one second where the rounds actually land. What settled it
was a temporary probe logging both ends — the tick arming the flash and the renderer applying it —
which printed `flash=True renderers=11 mat=Universal Render Pipeline/Lit` on the first run.

That is the same lesson this file already records four times over, in a new costume: **verify
CONTENT, and prefer positive evidence over a plausible cause.** A pixel search that finds nothing
is not evidence of absence until you have proved the search can find the thing when it IS there.

### Known, pre-existing: a unit's slot is not stable across frames

`UnitSlots.Take` hands slots out in roster order, so when a soldier dies everyone behind him
shifts down one slot. Per frame the assignment is still a bijection — every live unit gets a slot
of its own class at its own position — so the flash and the positions are correct. But anything
slot-STICKY drifts: `UnitAnim`'s clip time and its hidden→visible re-arm belong to the SLOT, not
to the unit, so a soldier can inherit a neighbour's animation phase when the rank in front of him
thins. The old flat pool indexed by order too, so this is not a regression from the per-class
change, and with full-roster volleys every unit is playing the same clip anyway. If per-unit
animation state ever matters, the fix is to key slots by unit id rather than by position.


## The tank shell, restored — 2026-08-06

**The player tank never fired.** `TankShellsRemaining` and `CannonArmed` were in `GameState`,
`LevelBuilder` totalled the ammo from every player structure with a cannon, `CannonSpec` imported
cleanly with its muzzle offsets and its `velocityBoost`, the Shell projectile type existed and its
prefab was pooled — and nothing ever built a shell. `FireVolley` spawned one bullet per unit and
stopped.

`BattleTick.CannonShells` is the missing piece: one heavy round per player-side structure that
mounts a cannon, added to the volley the infantry just threw. It is OFF-ROSTER — built from a
STRUCTURE, not a unit — so losing every soldier does not silence the tank, and the tank is not a
body the enemy can shoot at. Ammo is finite and `CannonArmed` gates it, so a level can field a
tank with a cold gun. No jitter: the infantry are spread on purpose, but a rifled gun puts its
round where it is pointed and a wandering shell reads as a bug.

**A test was passing over the hole the whole time.** `"the player tank contributes its cannon
shells"` asserted `TankShellsRemaining > 0` after the level was built — that the ammo had been
IMPORTED, never that anything fired it. Same family as the four failures this file already
records: it measured the input and called it the output. The checks now fire a volley and assert
a Shell comes out, that it carries its structure multiplier, that the ammo is SPENT, that it
stops at zero, and that `CannonArmed=false` fires nothing.

**And the same edit found a second hole.** The PLAYER's volley left `Type`, `SplashRadius` and
`StructureDamageMultiplier` at their defaults, so every round a human fired was a plain bullet
with no splash and a 1x structure multiplier. `AutoFire`, three methods down, set all three
correctly — so the rocket trooper's 6x against buildings and the grenadier's 2x existed only
under the debug driver, and a rocket rendered as a tracer. **Auto and the player firing through
different code is exactly how that survived**; anything that only Auto exercises is not tested.


## The corpse that came back — 2026-08-06

Restarting a level brought the whole enemy line back **lying on their backs**, playing a perfect
breathing loop on the ground.

`die` is the ONLY clip that drives the ROOT; every other clip is rotation on the joints below it.
Legacy `Animation` leaves a transform wherever the clip last sampled it when you stop, so
`anim.Stop()` + restart-the-idle brings back every joint EXCEPT the root — which stays face-down
on the floor. `UnitAnim.Stand()` now restores the root's authored rest transform explicitly,
captured in `Awake` before any clip has played (the only moment it is guaranteed to be at rest
rather than at the last frame of whatever ran on that slot).

**This was LATENT, and per-class pooling exposed it.** With one flat 48-slot pool per side, a
corpse took a high index that a fresh roster of ten never reached, so the death pose sat in a slot
nobody looked at. Per-class pools are sized to what the level actually fields, so a corpse takes
the slot immediately after the living — and a reload hands those exact slots straight to the new
roster. The pooling change did not break this; it stopped hiding it.

`PortSelfTest` asserts the SHAPE rather than the symptom: `die` drives the root and `idle` does
not. A future clip set that breaks that assumption says so in a second instead of on a device.

**The general rule, worth applying to anything else recycled: stopping an animation does not undo
it.** Ask what each clip WRITES, and make sure something restores every one of those channels —
not just the ones the next clip happens to drive.


## Contact shadows, and what this camera does to ground decals — 2026-08-06

Reported as: on the snow level the soldiers look like they are "standing on white space".

**The port had no unit shadows at all** — only a ported COMMENT in `BackgroundDefinitionSO`
mentioning that `groundNear` feeds the contact-shadow tone. The Filament build has them; the port
never got them. On the tan biomes that is nearly invisible, because the ground is far darker than
the sky and the horizon carries the ground read on its own. On WINTER the ground is near-white
under a pale sky, so with no shadow there is nothing at all saying where the surface is.

Two things had to be right, and only the first is obvious:

- **Tone comes from THIS level's ground**, scaled by 0.58 / 0.62 / 0.72 — the Filament build's
  numbers, and they are not uniform on purpose. A flat grey that works on snow is a black blob on
  CityRuins ash and invisible on Forest green, and BLUE is kept highest so the shade COOLS rather
  than muddies. Snow shadow goes blue, not grey-brown.
- **The ellipse is stretched 3.2x in DEPTH**, and that is forced by the camera rather than being a
  style choice. The battle camera sits ~1.2 up at ~10 back — about SIX DEGREES above the ground
  plane — so a decal lying flat is seen almost edge-on and its on-screen HEIGHT is its world depth
  times the sine of that angle, about a tenth. A round shadow 28px wide projects to a 3px smear,
  which is exactly what the first pass drew and why it read as nothing. Widening does not help
  (it just makes a wider smear, and it collides with the neighbour's); DEPTH is free, because the
  camera looks along it, and it is the only axis that buys screen height.

**This applies to every ground decal in the game, not just shadows** — scorch marks are subject to
the same projection and are why a burn reads as a smear. Anything new that lies flat on the ground
has to be sized in depth, not in width.

The falloff also needed a real solid core. The first version shouldered from 0.12 — nearly all
penumbra — which on snow is a smudge too faint to be anything. And note `Mathf.SmoothStep` is a
smoothed LERP BETWEEN its arguments rather than GLSL's `smoothstep`, so the useful knob is where
the ramp STARTS, not a threshold; the texture builder now ramps explicitly instead.

### Health bar: the track fades faster than the fill

Equal alpha is not equal legibility. The track is near-black and the fill is a saturated colour,
so against any of this game's grounds the dark track keeps far more contrast at the same alpha.
Faded together, the colour washes out first and the bar spends its last half-second as a DARK
HUSK over a soldier's head — which is very likely what "black means dead, right?" was actually
reporting, more than low health was. `HealthBarTrackAlpha` squares the fill's alpha, so a bar
always dissolves down to its COLOUR and never down to a black rectangle.


## Ragdolls: lean, and stopping at walls — 2026-08-06

Two reports: bodies flew backwards perfectly upright, and they flew THROUGH structures.

**The lean.** The tick has always spun a corpse at 220 deg/s, and the renderer was throwing that
away for animated units (`rotation = identity`) — correctly, at the time, because applying the
full spin on top of the `die` clip made a body fold AND cartwheel. Discarding it went too far the
other way: a statue on rails. `RagdollLeanDegrees` shows a FRACTION of the tumble (0.32) with a
CAP (38 deg), so the body pitches back as it is thrown and then holds that lean while the clip
does the folding. The cap is reached about a third of a second in, so it rises and settles rather
than winding up. Signed by side, because the two lines are thrown in opposite directions.

**The walls.** `StepRagdolls` had no notion of structures at all, so a body sailed through a
bunker — which is the one place a purely cosmetic system stops being cosmetic, because a body
passing through a building says the building is not there. `BlockOnStructures` stops it at the
face it arrived through and rests it on the ROOF if it cleared the wall.

It blocks on EVERY structure, not just the opposing side's. Projectiles deliberately pass through
FRIENDLY structures so a garrison can fire over its own fortress; a body has no such excuse, and
the most visible case is a player unit thrown backwards into the player's own tank.

`CollisionSystem.StructureBox` is now the one place that builds a structure's solid box — the
same box the projectile path uses, including the deck-vs-size distinction that once made a
garrison unkillable. Two hand-rolled copies of that arithmetic is exactly how the two would drift.

NOT yet judged in play: whether 0.32/38 is the right amount of lean. It is deliberately subtle.


## Data authoring, once Android is retired — CLOSED 2026-08-06 (kept for the reasoning)

**Decided and executed: authoring moved INTO UNITY.** The section below is the question as it
stood; what actually happened is two sections down, under "Data authoring moved into Unity".

The Android build stopped being the shipping build on 2026-08-06. One thing did not move with it:
**game DATA is still authored in Kotlin** and reaches Unity one way, through
`tools/export_kotlin_data.py` -> `data.json` -> `DataImporter` -> ScriptableObjects.

That was obviously right while Android was the product and Unity was the port. It is no longer
obviously right, and it is worth an explicit decision rather than drifting:

- **Keeping it** costs a second repo, a second toolchain and an export step on every level tweak,
  in a codebase nobody ships any more. It also keeps a real hazard alive: `DataImporter` REBUILDS
  the eight roster/grouping sandboxes itself because the exporter cannot parse their Kotlin
  generator, so the two halves of the level list already come from different places.
- **Moving authoring into Unity** means the ScriptableObjects become the source and can be edited
  directly — but it throws away a parser that has been debugged hard (`FortressTier` silently
  dropped, `Capture` losing optional fields, ARGB losing its low byte to a float mantissa), and
  the Kotlin files carry a great deal of design commentary that would need a home.

Nothing here is urgent — the pipeline works. But the reason it exists is gone, so the next person
to be annoyed by an export step should treat that annoyance as a real signal, not as friction to
be absorbed.

**Resolved the same day.** The annoyance was real and the move was smaller than feared: nothing
had to be migrated at all.

## Data authoring — DECIDED 2026-08-06: it moves into Unity

Rob closed the question above: **authoring moves into Unity.** The ScriptableObjects become the
source of truth. Not yet executed — it is Phase A of `_plans/TIER0_PLAN.md`, and the work is
mostly DISARMING the importer rather than migrating anything, because the assets are already
correct and nothing gets re-parsed.

The one thing that must not be skipped: `DataImporter.Sweep` deletes any asset the Kotlin no
longer declares. That is correct while Kotlin is authoritative and is a data-destroying bug the
moment Unity is. It goes, `BuildSandboxLevels` comes out of the import path, and `Import` gets a
guard rather than the "never re-run this" comment it has carried for months.

## The victory screen and a live economy — 2026-08-06

`PRODUCT_DIRECTION.md` Tier 0.3/0.4a/0.5. **The port had a complete, tested, entirely DEAD
economy**: `EconomyStore`, `ProgressStore` and `TurnFlow.AwardVictory` were all ported and correct,
`AwardVictory` had ZERO callers, and no coin was ever earned or star ever recorded in a running
build. The whole of it came alive through one call site — `BattleRunner.ResolveBattleEnd`.

Keyed on `battleId`, NOT on a `Playing -> over` edge. An edge is one frame and the award has to
survive everything that keeps ticking after it (the free camera alone keeps a finished battle
running indefinitely); keying on the battle makes "pay once per battle" the literal invariant. A
replay pays again on purpose — the one-time parts are gated inside `GrantVictoryPayout` by
`previousBestStars`.

### The UI layer is BUILT IN CODE, and that is deliberate

`ArmedConflict.UI.BattleUI` constructs its whole hierarchy at runtime — no prefab, no serialized
references, therefore **no scene rebuild for any UI change**. The editor GUI runs over VNC on
llvmpipe where laying out a canvas by hand is genuinely painful, and there is no designer who
would edit it in the inspector. It is still real retained-mode uGUI, built once, allocating
nothing per frame.

`Build()` is called explicitly from `Create()` rather than from `Awake` — **Awake does not run in
edit mode** without `[ExecuteAlways]`, which left every widget null the first time the preview
harness built this canvas from an editor method.

### Traps this phase paid for

- **NOTHING OUTSIDE ASCII MAY APPEAR IN A TMP STRING.** The default `LiberationSans SDF` font
  asset is built over ASCII only, so `★` and `◆` render as missing-glyph boxes — silently, with no
  error. This was written into the code with a comment explaining it, and then `★` and `◆` were
  used in four strings anyway; only the rendered image caught it. The panel's stars and the coin
  icon are DRAWN SPRITES for this reason, and `TurnFlow.StarReason` says "3 stars" in ASCII with a
  self-test check asserting it contains no `★`. (The em-dash `—` does render — the asset covers
  Latin-1 punctuation. Verify anything else before using it.)
- **`AssetDatabase.ImportPackage` is ASYNCHRONOUS and imports NOTHING under `-quit`.** It is the
  documented way to install TMP's essential resources and it silently does nothing headless. They
  are unpacked directly instead by `tools/import_tmp_essentials.py` — a `.unitypackage` is a
  gzipped tar of one folder per asset holding `asset`, `asset.meta` (the GUID, which must come
  across) and `pathname`. One-time; the output is committed.
- **IMGUI always draws AFTER a ScreenSpaceOverlay canvas.** The old RESTART / NEXT buttons had to
  be REMOVED, not merely covered — they would have painted over the card and gone on eating its
  taps.
- **A ScreenSpaceOverlay canvas never appears in a camera's target texture.** An offscreen shot of
  one comes back empty; `BattleUIPreview` switches the canvas to `ScreenSpaceCamera` for the render.
- **Do not measure "did the text render" in pixels.** The first attempt counted pixels differing
  from the backdrop and reported 98.5% — meaningless, because the card's full-screen dim covers
  every pixel whether a glyph resolved or not. Ask TMP: `textInfo.characterCount` is non-zero only
  when a font asset resolved AND the string laid out. Count ACTIVE labels only; a hidden button's
  label never lays out and reads as a false failure.

### CONFIRMED ON DEVICE 2026-08-06

Pixel 10 Pro XL, release build. L1 driven to victory on AUTO:

```
[Battle] victory: 3★, +230 coins (Daily Bonus!), balance 230
```

Fired exactly once. The card rendered with every glyph, held a steady 60 fps, and the coin pill
carried 230 into L2. **NEXT was tapped and L2 loaded** — the EventSystem, touch and uGUI buttons
all work on hardware, which nothing in the editor could have shown. The card cleared on the level
switch. CAM hid the whole canvas and brought it back.

`Auto` is enough to confirm the card, the payout and the buttons. It says nothing about
difficulty, and the 3★ it produces is optimistic — measure balance with real drags.

**The dim looked broken and was not.** Eyeballing the screenshot said the full-screen dim had
failed to render; sampling the same pixels with the canvas hidden said otherwise — ratio 0.55,
which is exactly a 0.72-alpha black composited in LINEAR space and written out as sRGB
(0.28^(1/2.2) = 0.56). A URP overlay dim always reads far lighter than its alpha suggests. Do not
judge one by eye, and do not "fix" it by raising the alpha.

### Verify this again with

`DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod BattleUIPreview.Shots -logFile -`
writes the three cards to `Builds/ui/` and reports how many labels actually laid out glyphs.

## Data authoring moved into Unity — DONE 2026-08-06

Phase A of `_plans/TIER0_PLAN.md`. **The ScriptableObjects in `Assets/GameData/` are now the
source of truth.** `CLAUDE.md`, `README.md` and `PRODUCT_DIRECTION.md` all say so; the section
above describing the one-way Kotlin pipeline is history, not instruction.

Nothing was migrated, because nothing needed to be. Re-running the exporter produced a `data.json`
byte-identical to the committed one, so the assets were already at the Kotlin's last word and the
exporter's hard-won parsing (FortressTier, Capture's optional fields, ARGB's low byte) is baked
into them. **The work was disarming the importer, not moving data.**

### What changed

- **`DataImporter` → `LegacyKotlinImport.ImportOnce`, and it REFUSES to run** without
  `-iAcceptDataLoss`. It still overwrites every asset in place with no undo. It carried a "never
  re-run this" comment for months while remaining one command away from destroying a day's
  authoring; a guard is cheaper than the incident. **Do not remove it.**
- **The orphan sweep is GONE.** It deleted any asset the Kotlin no longer declared — correct while
  the Kotlin was authoritative in both directions, and a shredder now, since a level authored in
  Unity is by definition one the Kotlin does not declare. The price, stated plainly: an asset
  deleted from the Kotlin now survives here, and `PortSelfTest`'s `levelNumber` contiguity check
  is the only thing left that catches a stranded level rejoining the campaign at its old number.
- **Sandbox generation is now `SandboxLevels.Generate`**, a command rather than a side effect of
  every import. That was the second source of truth for the level list. It reads the
  ScriptableObjects and **preserves each rig's existing `levelNumber` and `id`** rather than
  deriving them from `levelOrder`. Verified faithful: regenerating produced assets byte-identical
  to the committed ones.
- **`LEVEL_AUTHORING.md`** carries the six composition rules, moved out of `LevelDefinition.kt`
  before that file became unreachable. Dozens of Kotlin comments still point at "the composition
  rules at the top of the campaign block" — they mean that file now.
- **`LevelDefinitionSO.designNotes`** ([TextArea]) is where per-level reasoning goes. The Kotlin
  carried a great deal of it in comments and the migration would otherwise have stranded all of it.

### The rules are CHECKED now, not just written down

`LevelComposition.Report` (headless) and the level inspector run the same six checks. Both measure
by **building the level and reading the same half-widths the camera uses** — re-deriving spans
from anchors would create a second source of truth about framing, and would be wrong anyway
because a group's real width comes from Formation, not its anchor.

Warnings are advisory: a level may bend a rule for a reason, and that reason belongs in its
`designNotes`. An author who cannot ship a deliberate exception stops running the check at all.
Errors are the locked 7-30 roster scale.

### It immediately found real faults in shipped levels

```
L1 Patrol Encounter  warn  rule 5: 3/9 garrisoned (33%)
L3 Watchpost Ridge   warn  rules 4/6: separation 13.3 (14-18)
L5 Tower Assault     warn  rules 4/6: separation 11.3 (14-18)
L6 Ash Boulevard     warn  rules 4/6: separation 18.1; rule 5: 7/16 garrisoned (44%)
L7 Oceanfront        ERROR player roster 6, enemy roster 6 — the LOCK is 7-30 per side
```

**L7 violates a lock**, verified independently against the asset rather than taken from the tool.
None of these were fixed here: retuning levels is Phase D work, done against the beat chart, and
Phase A's deliverable is the tooling. They are the first real evidence that the campaign needs
that pass.

One limitation to know: "dominant structure" is resolved as the WIDEST enemy structure
(`hitWidth`, falling back to `size`). For a tall-narrow tower that is a weak proxy, and on L5 it
picked the CommandBunker over the tower the level is named for.

## Campaign split from the test rigs — DONE 2026-08-06

Phase B of `_plans/TIER0_PLAN.md`. `PRODUCT_DIRECTION.md` pillar 10: "test rigs are not the
campaign."

Done with ONE array, not two. `SpikeSceneBattle` now orders **campaign-then-rigs** (`OrderBy
isTestLevel, ThenBy levelNumber`), so the campaign block leads and is contiguous, and the
player-facing path is simply `index < campaignCount`. A second serialized array would have meant
two indexing schemes and a conversion between them at every call site.

- **The ◀ ▶ stepper walks the campaign only.** A `RIGS` button unlocks the test block.
  Deliberately a runtime toggle and NOT `Debug.isDebugBuild`: the rigs have to stay reachable in a
  RELEASE build, because that is the only build performance may be measured on and sweeping them
  from adb is how missing geometry gets found. Locking them while standing on one snaps back to
  the last campaign level.
- **NEXT on the victory card is bounded by the campaign**, so winning the last campaign level no
  longer offers to walk the player into the unit parade.
- **The nav readout counts within the reachable block** — "L7 (7/7)", not 7 of 24 — and marks a
  rig with `RIG`.

### The renumbering chore is retired

`PortSelfTest` asserted `levelNumber == index + 1` across all 24, which is what forced every test
rig to be renumbered whenever the campaign changed size. It now asserts contiguity **within the
campaign only**; a rig's number indexes nothing. Phase D changes the campaign's size by five or
more levels, so this had to come first.

That half of the check matters MORE than it used to: the orphan sweep is gone, so a stale level
asset can no longer be deleted for us, and this is the only thing that catches one rejoining the
campaign at its old number. A duplicate-id check was added alongside it — ids key the saved star
results, so a duplicate silently makes two levels share a best-star record.

### Confirmed on device

Release build on the Pixel 10 Pro XL: ten ▶ taps from L1 stop at L7 and stay there; `RIGS` then
reaches L8 (TEST — Tier Collapse); locking again snaps L8 back to L7; the readout reads L7 (7/7).

## Campaign to twelve levels — DONE 2026-08-06

Phase D of `_plans/TIER0_PLAN.md`. **12 campaign levels + 17 rigs = 29.** Every level owes one
beat from `PRODUCT_DIRECTION.md`'s chart and says which in its `designNotes`. Two stages of six,
bosses on 6 and 12. **`LevelComposition.Report`: 12 levels, 0 warnings, 0 errors** — the five that
were breaking their own rules are fixed.

| L | Level | Biome | Beat |
|---|---|---|---|
| 1 | Patrol Encounter | Mountains | teach the drag |
| 2 | Garrison Post | Forest | structures matter |
| 3 | Watchpost Ridge | MountainsDusk | prioritise threats |
| 4 | Ash Boulevard | CityRuins | the charge |
| 5 | Tower Assault | Desert | elevation |
| 6 | **Ridge Bastion** | Mountains | **stage boss A** |
| 7 | Barracks Line | Winter | toughness |
| 8 | **Timberline Crossing** | Forest | combine |
| 9 | **Dusk Redoubt** | MountainsDusk | outnumbered |
| 10 | **Rubble Yard** | CityRuins | reinforcement race |
| 11 | Oceanfront | Ocean | elite exam |
| 12 | **The Citadel** | Desert | finale |

Bold are new. Ash Boulevard moved from 6 to 4 (panic belongs early), Barracks Line from 4 to 7,
Oceanfront from 7 to 11.

**Campaign assets are named for their IDENTITY now** — `AshBoulevard.asset`, not `Level4.asset`.
The order moves as the funnel is tuned and a filename disagreeing with `levelNumber` is a trap.

### Two systems were dead and are now wired

This phase found the same shape of bug Phase C did, twice.

- **WIND IS COSMETIC.** `TrajectoryPhysics` applies `windAccelZ` to Z; the collision test is
  X/Y only (`SegmentDistanceSq(prevX, prevY, ...)`) and Z appears in `CollisionSystem` solely to
  place the detonation visual. Wind cannot change what a shot hits. It has also never been set on
  a level in either build. Beats 7 and 8 were built on wind and were re-cut onto real variables —
  toughness (HeavyRifleman at 64 hp, forcing concentration) and a combine of elevation + melee.
  **Do not author a wind level until wind does something.** Making it real is a PHYSICS change and
  needs an ask.
- **BOSS PHASES AND REINFORCEMENT WAVES WERE NEVER FIRED.** `EventSystems` has decided both
  correctly since the port and nothing ever called it: `bossPhases` and `reinforcementWaves` were
  read only by `BattleRunner`, and only to size the pools. Now wired into `BattleTick` step 7b,
  spawning through `LevelBuilder.BuildUnits` so an arrival is built exactly like the opening
  roster. Confirmed on device — L10 turn 4: `EVENT: Their heavies are here! (enemies 6 -> 10)`.

### What Auto still cannot test

`Auto` cannot trigger a BOSS PHASE. It targets the nearest enemy unit, so on Ridge Bastion it
clears everything else before the keep's garrison and the level resolves as a victory first. The
boss path is covered end-to-end by `PortSelfTest` instead — it razes the trigger structure, runs a
real `BattleTick.Step`, and asserts the phase fires once, spawns, announces, and does not re-fire.
**Seeing the Sovereign on a real device still needs an aimed drag at the keep.**

A trap that check paid for immediately: `LevelBuilder.BuildInitialState` does NOT set `Phase`
(`BattleRunner.LoadLevel` does, right after), so a state built for a test takes `Step`'s
cosmetic-only early return and no event fires. Set `Phase = Playing` on any hand-built state.

### One-off authoring script, deliberately deleted

The 12 levels were written by `CampaignAuthor.cs`, run once and then removed — creating five
levels' worth of GUID references by hand is not viable, but a script that can rewrite every level
wholesale is exactly the hazard `LegacyKotlinImport` was guarded against. The assets are the
artifact. `CampaignAudit.Dump` is kept: it is read-only and prints what each level actually is.

## Enemy turn juice — DONE 2026-08-06

Phase F of `_plans/TIER0_PLAN.md`, `PRODUCT_DIRECTION.md` 0.6. Phase D made the events FIRE; this
makes them SAY something. `telegraphText` and `announcement` had been imported and displayed
nowhere since the port.

**Two channels, and the difference between them is the whole of pillar 7.**

- The **banner** is a flash — something just happened ("Their heavies are here!"), or the turn just
  changed.
- The **telegraph strip** is a standing condition — something is ABOUT to happen, and it stays up
  for the entire turn being warned about. `GameState.TelegraphText`, recomputed from scratch every
  tick rather than latched, so it clears itself the moment the wave lands. A warning with a fade
  timer has blindsided anyone who looked away, which is the thing the pillar exists to prevent.

**The turn handover names the threat, not the phase.** `ThreatLine` reports the ADVANCE first —
"3 closing on your line" — because a marching group reaching the line is the only thing that can
lose the level this turn, and counting rifles does not matter if it arrives. It falls back to
"Enemy turn". An event outranks it: both land on the same frame when a wave arrives on the
handover, and two competing banners tell the player nothing.

Confirmed on device, L10: the red strip reads "Heavy support inbound — 1 turn" through the whole
of turn 3, the wave lands on turn 4, the strip clears itself.

The strip started at y-104 and ran straight through the CAM / RIGS / stepper cluster. Harmless for
input (it takes no raycasts) but it read as a broken layout; it sits below the banner now.

## Loadout — DONE 2026-08-06

Phase E, `PRODUCT_DIRECTION.md` 0.4b: "something to buy that changes the next battle".

**SLOTS AND POINTS ARE SEPARATE, and that is the whole design.**

- **Slots** = the number of ground troops the level was AUTHORED with, read off the level. Fixed,
  because composition rule 1 measures the PLAYER LINE'S WIDTH and the aiming camera is framed on
  it. A loadout that could field more bodies than the level was drawn for would zoom the camera
  out, and nothing else in the layout can compensate.
- **Points** = `deployBudget`, and they buy QUALITY. Eight slots and eight points is eight
  riflemen; eight slots and sixteen points is four heavies and four riflemen, or two snipers and
  six riflemen.

So the squad never gets WIDER as the campaign goes on — it gets BETTER. Every authored level stays
framed exactly as it was measured, the locked 7-30 scale holds by construction, and the budgets
authored in Phase D turned out to need no change at all.

`Loadout.ToPlayerGroups` TILES the picks across the authored width, so a three-type squad is
exactly as wide as a one-type squad. Anchoring every pick at the same x would stack them; giving
each a fixed spacing would make rule 1 fail on the player's choices rather than on the level.

**The default is the old behaviour.** `Loadout.Default` fills every slot with the cheapest
unlocked unit, which reproduces what each level fielded before the picker existed — pillar 8,
"default paths cost nothing". BEGIN is live the moment the panel opens.

Garrisoned player groups are NEVER touched: the tank crew is level geometry standing on a
structure at a fixed anchor, not a squad pick.

### Checks that matter

`PortSelfTest` asserts, for EVERY campaign level: the default loadout is legal and fills every
slot; the default squad is no wider than the authored line, measured through the real
`LevelBuilder` on the same `PlayerCamHalfWidth` `LevelComposition` reads; an all-dearest-unit
squad also fits that frame; and `deployBudget` covers at least one cheap body per slot. Plus the
edges — an empty loadout is illegal, overfilling slots is illegal even when points allow it,
under-filling is legal, and a locked unit cannot be fielded.

### Two traps, both the same one

IMGUI draws AFTER the canvas. The loadout panel is modal, so `OnGUI` returns early while it is
open — otherwise the HUD and the ◀ ▶ stepper sit on top of the panel and stay TAPPABLE, and a
player could change level out from under the squad they were choosing. Identical to the
RESTART / NEXT problem in Phase C. The in-battle furniture (coin pill, banners) is also hidden
while the picker is up: it belongs to a battle that has not started, and it ghosted through the
panel's 97% fill.

### NOT DONE: the balance audit — SUPERSEDED, and the audit is now COMPLETE

**Both halves have since been built and run** (2026-08-06 arithmetic, 2026-08-07 device), and the
whole audit was CLOSED on 2026-08-07 by Rob playing the campaign and reporting the levels feel
fine. It found L7 unwinnable, made reach a checked rule, found the 288 siege ceiling and the tank
shell's overshoot. See the sections at the end of this file. The original text follows, and its
"has not been run" is no longer true.

`PRODUCT_DIRECTION.md` asks that every shipped level be clearable at stock tier by a competent
shooter, and calls a level that breaks under a LEGAL loadout a product bug. **That audit has not
been run** — it needs real drags per level, and `Auto` cannot measure difficulty (it never misses
and is structure-blind). The framing half is enforced by the checks above; the difficulty half is
still owed. It was deferred historically too; it is now the last open item in Tier 0.

## Ruins, instead of blocks everywhere — 2026-08-06

Rob: "I want to see better ruins when a structure is destroyed, not just the structure disappears
and then we have all of these blocks everywhere." Both halves of that were real, and they had
DIFFERENT causes.

**1. The building vanished.** Destruction removed the structure and threw TEN CUBES at random
angles with `Ttl = float.MaxValue`. Nothing marked where the building had stood.

Now a RUIN is PLACED rather than launched: 3-6 wide flat slabs lying inside the structure's own
footprint, already `Asleep`, persisting for the level. Sizes descend from the centre outward so it
reads as a collapsed mound rather than a row of equal lumps, and rotations are within ±11° —
masonry settles askew, it does not stand on end. `DebrisPiece.Squash` (0.3 for a slab, 1 for a
tumbling chunk) is what makes it lie FLAT: at this camera's ~6° the height of a lump is most of
what you can see of it, so a cube reads as a crate and a slab reads as fallen masonry. The
collapse still throws chunks, but they are transient now.

**2. "Blocks everywhere" was mostly NOT the destruction.** It was the SHED pieces — the chunks a
structure throws off as it takes damage, which also carried `DebrisRubbleTtl`. A structure sheds
up to a dozen chunk groups over its life, every one of them permanent, so they piled up across the
field as loose blocks with nothing to do with where the building stood. They are transient now.
The lasting record of DAMAGE is the structure's own missing geometry; the lasting record of
DESTRUCTION is the ruin.

**3. They were also nearly black.** The debris prefab used `structEnemyAccent` (0.30/0.24/0.18),
which at debris size on open ground reads as scorch rather than stone. It uses the structure BODY
tone now (0.52/0.44/0.34), so rubble reads as the building it came from.

### CONFIRMED ON DEVICE 2026-08-06

L1's outpost demolished with real aimed drags. Once its HP reached 0 the HUD's Structure line
cleared and the site holds a LOW, FLAT, CLUSTERED mound of slabs where the building stood — and
the field is otherwise clean, with none of the scattered blocks the original screenshot showed.

The diagnosis was confirmed first, and it is what made the fix the right one: that screenshot had
~14 near-black blocks strewn far wider than the structure's footprint, which identified the SHED
pieces rather than the destruction burst as the main culprit.

**A device-safety note.** Relaunching found the NOTIFICATION SHADE holding focus over the game
(`mCurrentFocus=NotificationShade` while `mFocusedApp` was still the game). Taps in that state are
exactly how earlier sessions ended up driving personal apps. `adb shell cmd statusbar collapse`
clears it cleanly — no synthesized input, and no KEYCODE_BACK, which is the thing to avoid.

To finish the check: L1, BEGIN, then repeat `input swipe 540 1150 204 1486 400`. That drag is
derived, not guessed — `ppu = 1080 * 0.0208 = 22.46 px` per drag-unit and `DragSpeedScale = 0.384`,
so L1's 16.5-unit tank→outpost separation needs `v = sqrt(16.5 * 4) = 8.12`, a 475 px drag, 336 px
on each axis at 45°, downward to launch upward. It lands on target: structure HP fell 90 → 50 → 28
over successive volleys. Budget ~10 volleys, since garrison units absorb hits first.

## The balance audit, arithmetic half — DONE 2026-08-06

`BalanceAudit.Report` (`Assets/Editor/BalanceAudit.cs`), the headless half of the last item Tier 0
owed. It cannot measure difficulty — that needs a human drag — but it settles the half that is
arithmetic and therefore needs no device at all, across BOTH ends of the legal loadout space
(stock, and the dearest legal squad), because the product rule is written over LEGAL loadouts.

**It found a shipped level that could not be won.** L7 Barracks Line garrisoned 3 grenadiers on
the CommsTower at x 8.6, 4.5 units above the muzzle: **100% power from the front rank, 108% from
the back**, and **101% — literally unwinnable — under a legal all-RocketTrooper squad**, which
tiles the line slightly further back. Verified by hand against the asset before anything was
changed: v = 8.96 against a 9.0 cap.

**All six composition rules passed it.** That is the finding under the finding. Rules 1-6 measure
FRAMING and HORIZONTAL separation; the power budget is spent on HEIGHT, and nothing measured it.

**And `LEVEL_AUTHORING.md` rule 4 was actively lying.** It described 14-18 separation as "well
inside the ~49-unit max range". The real figure is `AimSystem.MaxRange45` = v²/g = 81/4 =
**20.25 flat**, so the authored separation spends 70-89% of the whole envelope before a single
unit is lifted off the ground. That sentence is what licensed the level. It is corrected.

### What was changed

- **L7 fixed.** The grenadiers came off the mast onto a `TowerPlatform` at x 7.8 — reach 100% ->
  86%. The mast STAYS at 8.6 as the level's silhouette and identity, which keeps the enemy cluster
  depth; three enemy structures is still legal (one dominant + two supports). Moving them to the
  GROUND was tried first and rejected: it dropped the level to 45% garrisoned and broke rule 5.
  The beat is untouched — beat 7 is TOUGHNESS, carried by the 5 heavy riflemen on the barracks.
- **Reach is now RULE 7**, checked. Implemented once in `BalanceAudit.ReachRule` and CALLED by
  `LevelComposition`, so the audit and the level inspector cannot disagree about whether a level
  is playable. Front rank over 100% is an ERROR; back rank over 100%, or front over 92%, is a
  WARNING.
- **L3 and L5 carry accepted rule-7 warnings**, with the reason written into their `designNotes`,
  which is where a bent rule belongs. Both beats are explicitly about height ("fight upward", "the
  furthest target"), so their back rank — the tank crew — genuinely cannot reach and pulling the
  garrison in would pull the level's teeth.

### The three things it measures, and why each is honest

- **REACH.** Victory is every enemy UNIT dead, so an unreachable enemy is unwinnable at any skill
  level, forever. Uses the real envelope `v² = g(dy + √(dx²+dy²))`, NOT `MaxRange45` — height
  costs range twice, once for the climb and once for the longer slant, and using the flat figure
  would call a fortress-roof garrison reachable when it is not.
- **THE VOLLEY RACE at equal accuracy.** Both sides do fixed damage into a fixed HP pool, so the
  clean-volley count is exact and only accuracy is unknown; holding it EQUAL removes it. Warns
  past **2x**, not at break-even — the player also has the tank shell and per-turn attrition. At
  1.0 it warned on 21 of 24 squads, which is an instrument that discriminates nothing.
- **THE MELEE CLOCK.** `advancePerTurn` is authored, so turns-to-contact is known.

**Two ways to win, and the cheaper one is what the level costs.** A garrisoned unit dies with its
structure, so on a level that garrisons most of its roster — which rule 5 REQUIRES — razing can
clear the field for a fraction of the bodies' HP. Counting only the shoot route rated an
all-RocketTrooper squad at 20+ volleys and therefore hopeless, while that unit's entire design is
a 6x structure multiplier. With both routes, L12 The Citadel clears in 4.6 volleys by razing
against 21.6 by shooting: **the anti-structure unit is measurably the right pick on the fortress
level**, which is the roster working as designed.

### The device half — SINCE RUN, and CLOSED

Run the same day; the results are in the sections below. The ranking here is what chose which
levels to drag, and it was sound: L9 and L12, the two worst, were both unclearable at stock, and
L4, the least-flagged, was not. **Closed 2026-08-07 by Rob playing the campaign after the tank
shell was fixed and reporting the levels feel fine** — better evidence than the adb harness, which
has no aim preview and could never finish a mop-up phase.

**A systemic observation for whoever tunes difficulty next:** every campaign level needs 81-100%
power at its deepest enemy. The whole game lives in the top fifth of the aim range, so there is
almost no headroom anywhere and every level's aim demands roughly the same drag. Widening that
band means raising `AimSystem.MaxAimMagnitude`, which is a physics change touching all 29 levels
and needs an explicit ask — it was offered on 2026-08-06 and NOT taken.

## The balance audit, DEVICE half — run 2026-08-07

Real drags on the Pixel 10 Pro XL, stock squad (8 riflemen + 2 tank crew, 0 coins, nothing
unlocked — the level list steps straight into battle with the default loadout, so this is exactly
the baseline the arithmetic half modelled). Aim was DERIVED, not guessed: `BalanceAudit.Drags`
prints a 45-degree adb swipe per level from the level's own geometry.

**Result: L9 and L12 are NOT clearable at stock. L4 is, but only if the tank shells are spent
correctly, and nothing in the game says so.**

### The mechanism, and it is one number

**A rifleman's `structureDamageMultiplier` is 0.25.** His 8-damage round does **2** to a building,
so a ten-strong volley does **20 a volley if every round lands**. The tank shell is
`32 x 3 = 96`, and `ammoPerBattle` is **3**. So a stock squad's entire anti-structure capacity is
a FIXED **288**, spent in the first three volleys, after which a wall is effectively immune.

That collides head-on with composition rule 5, which REQUIRES the majority of the enemy roster to
be garrisoned. Where garrisoned structure HP exceeds 288, most of the enemy roster is standing
behind something the stock squad cannot break:

| Level | Garrisoned structure HP | vs 288 | Device result |
|---|---|---|---|
| L3 Watchpost Ridge | 340 | **deficit 52** | not run |
| L5 Tower Assault | 340 | **deficit 52** | not run |
| L6 Ridge Bastion | 392 | **deficit 104** | not run |
| L9 Dusk Redoubt | 330 | **deficit 42** | **3 runs, 3 total defeats** |
| L12 The Citadel | 425 | **deficit 137** | **defeat** |
| L4 Ash Boulevard | 240 | ok | every structure razed by shells alone |
| L1/L2/L7/L8/L10/L11 | 90-240 | ok | not run |

`BalanceAudit` now checks this directly (the SIEGE DEFICIT finding), and it is **predictive**: the
level with no deficit razed everything, the levels with one could not.

### What the runs actually showed

**L9, run 1** (fixed aim at the enemy mean): 22 -> 17 enemies, player 10 -> 0. Kills stopped DEAD
at 17 the moment the shells ran out, and structure damage fell to ~8 a volley.

**L9, run 2** (all fire on the bunker): bunker destroyed, but I kept firing at the empty site while
the barracks sat untouched. Defeat. Worth recording as an ERROR OF MINE, not a game fault — the
HUD's single "Structure HP" total cannot say WHICH structure still stands, and that is a genuine
readability gap.

**L9, run 3** (advancers first, then structures — the correct play): 22 -> 11 in three volleys with
only one loss, and the bunker's five machine gunners died with it, confirming the garrison-collapse
path works. Then the wall: ~6-10 structure damage a volley against 118 remaining, while losing ~1
unit a volley. Ended 1 unit vs 9 enemies.

**L12**: 18 -> 10 in three volleys (a roof garrison CAN be shot directly, as the self-tests claim),
then the same wall — ~12 a volley against 231 remaining. Player 10 -> 4 by volley 8.

**L4, run 1** (shells spent on the advancing shield bearers): 17 -> 7 with no losses, but the
structures were untouched and it stalled at the wall.
**L4, run 2** (all three shells into the structures): 17 -> 7 with **zero** losses, every enemy
structure destroyed by volley 10. It then stalled 7v7 because the survivors had closed to melee
range and my long derived drags flew over them — a limit of driving this from adb, since a real
player has the aim preview and would simply shorten the drag.

### Honest limits of this pass

- Drags were computed, not felt. In the endgame, when survivors close on the line, a computed
  45-degree drag overshoots and I had no preview to correct against. **A human is better than this
  harness at short range**, so L4's stall is not evidence that L4 is unwinnable.
- L3, L5, L6 were not run. Their deficits are known and L6's is large.
- Every run used the stock squad. Unlocking the rocket trooper (`structureDamageMultiplier` 6)
  changes the siege arithmetic completely, which is presumably the intent — but 0.4b says a level
  must be clearable at STOCK, and these are not.

### What this asks for — a decision, not a task

Three ways out, and this is Rob's call:

1. **Cut garrisoned structure HP under 288** on L3/L5/L6/L9/L12. Smallest change, keeps every beat,
   and the audit check enforces it from then on.
2. **Raise `ammoPerBattle`** from 3. One number, fixes all five at once, but it makes the tank the
   answer to every level and weakens the reason to ever buy a rocket trooper.
3. **Raise the rifleman's 0.25 structure multiplier.** Most invasive — it changes every level at
   once, including the seven that are currently fine.

My recommendation is **1**, plus a HUD change worth doing regardless: **"Structure HP" is a single
total across all enemy structures**, so it cannot tell the player which building is still standing.
That is what made run 2 waste four volleys on rubble, and a player has no better information than
I did.

## Siege retune — applied 2026-08-07, PARTLY verified

Option 1 from the section above: cut garrisoned structure HP under the 288 a stock squad can do.
Applied per PLACEMENT via `hpScale`, so no shared structure definition changed and no other level
moved. Each level's `designNotes` carries the numbers and the reason.

| Level | Garrisoned HP before | after | how |
|---|---|---|---|
| L3 Watchpost Ridge | 340 | **215** | bunker 0.5 (the TOWER snipers are the beat, so the bunker takes the cut) |
| L5 Tower Assault | 340 | **227** | bunker 0.55 (tower stack untouched — fighting upward is the beat) |
| L6 Ridge Bastion | 392 | **257** | bunker + keep both 0.66, scaled together so the keep stays dominant |
| L9 Dusk Redoubt | 330 | **229** | bunker + barracks both 0.7 |
| L12 The Citadel | 425 | **280** | gate + citadel both 0.66 — tightest margin in the game, correct for the finale |

All twelve now pass the SIEGE DEFICIT check, and the campaign ramps 90/135/215/240/227/257/240/
225/229/225/135/280. `PortSelfTest` ALL PASS, composition 12 levels 0 errors.

### What the device actually showed, and what it did NOT

**L9 only was re-run.** It is much better and probably still too hard.

- Played by hand with shells into the structures: 22 -> 9 enemies by volley 4 with 7 of 10 units
  alive, against the pre-tune run which sat at 17 enemies with the squad collapsing. The retune
  works.
- Then the same endgame problem as L4: the surviving shield bearers close to melee and a computed
  45-degree drag flies over them. **This is a limit of driving the game from adb, not a finding
  about the level** — a player has the aim preview.
- So it was re-run under **`Auto` as an upper bound on aim**: Auto never misses, so if perfect aim
  cannot clear it, nothing can. Auto took it from 22 enemies to **2 v 2** — and the run was cut
  short there when the phone was needed, so the final outcome is UNKNOWN.

**Read that 2v2 as a warning, not a pass.** Perfect aim finishing a level with 2 of 10 units left
is a level a real player loses, and `StarsFor` would score it 1 star at best. My reading is that
**L9 needs a second pass**, and that structure HP is not the whole story there:

**L9 fields 22 enemies against the player's 10 — the widest body ratio in the campaign**, and the
volley race flagged it worst at 4.1x before any of this. Cutting HP does not change that a 22-body
line out-shoots a 10-body line every single turn. The next lever for L9 is the ROSTER, not the
walls.

### Still owed — ALL SINCE RESOLVED

- ~~L3, L5, L6, L12 not re-run on device~~ — **closed by Rob's playtest 2026-08-07**, after the
  tank shell fix, which changed the answer for every level at once.
- ~~L9 likely wants an enemy-count cut~~ — **done**: 22 -> 15, race ratio 4.1x -> 1.9x.
- The **"Structure HP" HUD line is still a single total** across all enemy structures, so it cannot
  say which building still stands. It cost one run four volleys fired into rubble. `BattleRunner.cs`
  around line 1278 — the fix is to list surviving structures by `displayName`, and it needs no
  scene rebuild because that HUD is IMGUI.

## THE TANK SHELL DOES NOT LAND WHERE YOU AIM — found 2026-08-07

The most useful thing the whole balance audit turned up, and it was invisible until the HUD listed
structures separately.

**Measured on L12, one volley per drag, reading per-structure damage:**

| Drag (px per axis) | near tier | far tier | enemies |
|---|---|---|---|
| 272 | **-16** | 0 | 0 |
| **300** | -10 | **-96** | **-6** |
| 316 | -6 | -10 | -5 |

96 is exactly the shell (`cannon.damage 32 x structureDamageMultiplier 3`). So the shell lands on
the FAR tier when the infantry is aimed roughly at the NEAR one.

**The exact overshoot was pinned afterwards, in the harness rather than by eye: 2.5 to 3.9 units
depending on the aim** — at aim (6,6) the volley lands at 10.92 and the shell at 14.86. (The
device drag-deltas suggested ~1.3; that estimate was low, and the analytic figure supersedes it.)
`velocityBoost` is 1.12 and range goes as v², so the shell flies 1.2544x the infantry range. The
boost exists to stop the shell falling SHORT of the line's own volley (`BattleTick.CannonShells`
says so), and it overshoots instead.

**Why this matters more than any HP number.** The shell is the only weapon a stock squad has that
can break a structure — 96 against a rifleman's 2. The player aims ONE reticle and fires TWO
weapons that land in different places, and the one that matters is the one they cannot place. Every
failed run in this audit is at least partly this: shells thrown at a building and landing behind
it. My own first L12 run spent all three shells for ~58 total structure damage; the sweep above
put a single shell on target for 96.

**This is a candidate root cause for "levels are not clearable" that is INDEPENDENT of the HP
retune**, and it should be settled before any more level tuning. Three options, none taken:

1. **Aim the shell at the infantry's impact point** — solve the shell's velocity to match the
   volley's landing x rather than scaling the aim velocity by a constant. Most correct; it makes
   the single reticle honest. `TrajectoryPhysics.SolveVelocity` already solves speed to a target.
2. **Re-derive `velocityBoost` from the actual muzzle offset** rather than leaving it a hand-tuned
   1.12. Smallest change, still approximate, and it drifts the moment a tank moves on a level.
3. **Show the shell's own landing hint.** Rejected on sight — `CAMERA_ARCHITECTURE.md` and the
   HUD comment are explicit that a predicted landing marker was tried and reverted, because
   guessing angle and power IS the mechanic.

My recommendation is **1**.

### Structure HP is now listed PER STRUCTURE — done, confirmed on device

`BattleRunner.DrawHud` listed one summed "Structure HP" across all enemy structures, which cannot
say WHICH building still stands; it cost an earlier run four volleys fired into the site of an
already-destroyed bunker. It now lists each surviving enemy structure by `displayName`, nearest
first (which is also left-to-right on screen). Destroyed structures leave `state.Structures`, so
the list is automatically what is left to do.

**Duplicate names are real and would have rebuilt the exact ambiguity:** L12 places
`FortressTierSmall` and `FortressTierWide` and BOTH are called "Fortress Tier". A positional
qualifier is appended only when a name actually collides, so the common case stays clean.
Confirmed on device: `Fortress Tier (near): 115` / `Fortress Tier (far): 165`, and the qualifier
correctly disappears when one of them falls. Code-only, IMGUI — no scene rebuild.

### L9 roster cut 22 -> 15

L9 fielded 22 against the player's 10, the widest body ratio in the campaign, and the volley race
flagged it worst at 4.1x. Its garrisons were also over the decks they stand on:
`LEVEL_AUTHORING.md`'s capacity table rates a MountainBunker at ~2 and a BarracksBlock at ~4, and
this level had **5 and 8** on them. Now shields 6->4, forward rifles 3->2, bunker gunners 5->3,
barracks rifles 8->6. Garrison stays the majority at 9 of 15.

Effect on the audit: **race ratio 4.1x -> 1.9x**, under the warn threshold, with siege, melee clock
and all seven composition rules clean. Not re-run on device after the cut.

### Where L12 stands

Still not cleared, across two runs — but neither was an optimal line: the first mis-aimed all three
shells, the second spent them on the sweep above. Knowing that drag 300 puts a shell on the far
tier for 96, the untried optimal line is two volleys at 300 (far tier 165 dead, ten garrison with
it), then the near tier. **Try that before concluding anything about L12's tuning**, and ideally
after fixing the shell aim, which would change the answer for every level at once.

## The shell now lands where you aim — FIXED 2026-08-07

`BattleTick.FireVolley` no longer scales the aim velocity by `velocityBoost`. It takes the
**volley's own landing point** — `TrajectoryPhysics.LandingPoint` from the mean muzzle of the
firing line — and solves the gun onto it at the **same launch angle**, so the shell stays visually
part of the same volley.

`velocityBoost` survives with a real meaning: it is now the gun's speed **HEADROOM** over the drag
that ordered the shot — how much further back the tank may stand and still make it. A muzzle ~2
units behind the line needs about 1.07x, so the authored 1.12 covers it with room. It is a CAP on
the solved speed rather than a blind multiplier.

`InfantryMuzzleY` (0.35) is now a named constant used by the volley, the auto-fire path and the
shell's aim point. Two copies of that number would put the shell on a subtly different target.

### The size of the bug, pinned in the harness rather than by eye

The new self-tests were run against the OLD code before the fix was kept — because a check that
has never been seen to fail is not evidence. It failed exactly as it should:

| aim | volley lands | shell landed | overshoot |
|---|---|---|---|
| (5,5) | 5.41 | 7.95 | **+2.54** |
| (6,6) | 10.92 | 14.86 | **+3.94** |
| (7,5) | 10.60 | 14.50 | **+3.90** |

**The device drag-deltas had suggested ~1.3 units; that estimate was low and this supersedes it.**
After the fix all three agree to 0.01.

`PortSelfTest` now asserts the shell and the volley land together from their real (different)
origins across three aims, and that the shell never exceeds its boost headroom. Comparing LANDING
POINTS rather than velocities is the point — equal velocity from different origins is precisely
the bug.

### This did NOT make the siege retune unnecessary

Worth stating, because it was the open question when the fix was chosen. Shell capacity is
`3 x 96 = 288` either way; the fix lets the player RELIABLY LAND it rather than raising it. Every
pre-retune value (L3 340, L5 340, L6 392, L9 330, L12 425) still exceeds 288, so those levels were
unclearable on the arithmetic alone and the cuts stand. **Do not walk them back.**

What the fix does change is that the seven levels already under 288 got easier, because their tank
now reliably delivers 96s it used to throw past the target. None have been re-checked for being
too SOFT — that is the open risk from this change.

### On device: the structure phase now works, the ending is unconfirmed

L12, aiming directly at each structure (which is the whole point of the fix):

| volley | player | enemy | near tier | far tier |
|---|---|---|---|---|
| 0 | 10 | 18 | 115 | 165 |
| 1 | 10 | 18 | 109 | **59** |
| 2 | 9 | 13 | 107 | destroyed |
| 3 | 9 | 8 | destroyed | — |

**Both structures down by volley 3, the roster halved, nine of ten units alive.** Before the fix
the same level ate three shells for ~58 total structure damage. That is the fix working.

The ending is still unconfirmed, and honestly so: a second run with slightly looser drags left one
tier standing at 53 and sat at 6 v 13, and lost. Outcome is very sensitive to shell placement —
which is arguably RIGHT for a finale (three shells, 96 each, place them well) but means my adb
harness cannot reliably reproduce a win. **L12 is now plausibly winnable and has not been won.**

### Still owed — ALL SINCE RESOLVED

**Rob played the campaign after this fix and reported the levels feel fine (2026-08-07).** That
answered all three at once: a level clearing by hand, whether the seven levels already under 288
had gone too soft once the tank reliably landed, and L3/L5/L6 never having been played.

## Tier 1.1 — AMMO TYPES, built 2026-08-07

`PRODUCT_DIRECTION.md` Tier 1.1, spec `DYNAMISM_DESIGN.md` Phase A. **A fifth dead system**: the
`AmmoType` enum, `ProjectileEntity.Ammo`, `GameState.SelectedAmmo`, `GameState.BurningEnemyIds`,
the unlock/selection persistence in `ProgressStore`, `EconomyStore.PurchaseAmmo` and
`CollisionSystem.IncendiaryHitUnitIds` ALL existed since the port, and `FireVolley` never set
`Ammo`. Every round the game had ever fired was Standard, forever. This was wiring, not a build.

### What is there now

| | |
|---|---|
| `AmmoCatalogSO` + `Assets/GameData/AmmoCatalog.asset` | the four types, their prices and their numbers. Authored by `AmmoSetup.Build`, which is idempotent and safe to re-run |
| `AmmoModifiers` | the pure, testable projection the spec asks for — no ScriptableObject reaches the damage math |
| `BattleTick.FireVolley(.., ammoCatalog)` | stamps `Ammo` on every round INCLUDING the tank shell, and applies the scales |
| `BattleTick.Step(.., ammoCatalog)` | applies the incendiary burn on the handover edge |
| `BattleRunner.DrawAmmoSelector` | the in-battle selector, which also SELLS |

| Type | Effect | Price |
|---|---|---|
| Standard | the identity — cannot change a volley | free |
| Incendiary | 0.85x damage, and hit survivors take **8** at the enemy windup | 300c |
| AP | **2x to structures**, 0.6x to men | 400c |
| Cluster | **3.2x spread**, 0.65x damage — the wide-formation counter-pick | 500c |

**Standard is the IDENTITY and that is asserted**, which is what makes PRODUCT_DIRECTION's "no
ammo may be REQUIRED to clear a level" a checkable property rather than a promise: a level cleared
on Standard is a level cleared with every modifier at 1.

### The bug the DEVICE caught that the tests had passed over

Firing AP at L12's 165hp citadel took **128** off it where ~192 was intended.

The engine computes structure damage as `Damage * StructureDamageMultiplier`. The first version
scaled `Damage` down by AP's soft-target penalty, which then flowed through to masonry too — so
AP's real structure effect was `0.6 * 2 = 1.2x`, not 2x, and the type had almost no reason to
exist. **The test that passed was asserting the FACTOR (`StructureDamageScale == 2`) instead of
the PRODUCT.** `StructureMultiplier` now divides by `UnitDamageScale` so the two knobs are
independent and both read against the base round.

The replacement check asserts the NET per-round damage across three unit profiles, and was proven
to fail on the old form first: it reports 1.19x / 1.25x / 1.25x. Its tolerance is DERIVED from
integer rounding (`Damage` is an int, so an 8-damage round at 0.6 lands on 5, giving 2.08x) rather
than guessed, because a fixed epsilon would either fail that honestly-correct case or be too loose
to catch a 1.2x regression.

**The lesson is the one this file already records in four costumes: assert the OUTPUT, not the
input.** A multiplier being 2 is not the same claim as the damage being doubled.

### Decisions worth knowing

- **The selector SELLS.** Purchase lives in the in-battle selector rather than the loadout panel:
  the coin balance is already on that HUD and the panel is a fixed eight-row uGUI layout. Buying
  mid-battle is deliberately allowed — coins are earned, no ammo is required to clear anything,
  and "I want that one now" is the impulse a coin sink exists to catch. Buying also SELECTS, since
  buying then picking is a second step with no decision in it.
- **A tap on the selector can never start an aim drag.** `AmmoSelectorRect` is one definition read
  by both the drawing and the touch exclusion — the same trap the free-camera pad paid for, where
  a finger on a button also threw a volley and ended the turn.
- **No mid-drag switching, aiming phase only**, per the spec.
- The choice PERSISTS via `ProgressStore`, and is re-read on every level load, which also
  downgrades a selection the player no longer owns after a reset.
- **The burn is ONE tick, cleared as it is spent.** A unit that kept burning every turn off a
  single round would make the type a win button.
- `burnDamage` is **8**, re-derived against the CURRENT roster (it must chip, not one-shot, the
  frailest unit — now the 16hp Sniper). HANDOVER's old note about 6 being calibrated against an
  8hp Sniper is resolved; the check anchors to the live roster so it cannot expire silently again.

### Verified on device

- **The selector** renders and behaves. Purchase works end to end: 455 -> 55 buying AP, 325 -> 25
  buying Incendiary, 745 -> 245 buying Cluster, each button going gold and losing its price.
- **AP, after the correction.** One AP volley destroyed L12's 115hp gate outright and killed its
  five-man garrison with it. Standard could NOT have: its shell is 96, plus ~10 infantry, leaving
  the gate alive at 9. The 2x observed rather than derived.
- **The incendiary burn**, via the probe: `[Burn] 1 burning took 8 (0 died)` on L3. This is the
  one that could not be confirmed any other way — the burn has NO VISUAL, so unit counts cannot
  see an 8-point chip. The `[Burn]` log is KEPT for exactly that reason.
- The structure-HP retune is visible on device too: L3's Command Bunker now reads 125.

**Cluster's SPREAD was not isolated.** Four volleys — two Standard, two Cluster, same drag on a
fresh L4 — killed nothing either way, because the drag was aimed at the barracks rather than at
bodies. That measures MY AIM, not the ammo. The spread is one multiplier on the jitter the volley
already had, is covered by the tests, and shares the code path AP and Incendiary were confirmed
on. What is genuinely open is the BALANCE question — whether 3.2x is so wide that Cluster misses
everything — and that wants a human playing it rather than a scripted drag.

### Not done

- **Cluster's spread is unmeasured in play** — see above. Is 3.2x too wide to connect?
- **No flame VFX.** The burn is a damage event with no visual yet; the spec asks for a flicker on
  a burning unit, and `DYNAMISM_DESIGN` requires any new effect to use a BOUNDED slot pool.
- The spec mentions AP being strong against "armored units". **There is no armour concept in the
  roster** — no unit has such a field — so AP is implemented as structures-versus-men only.

## Corpses levitating onto roofs — FIXED 2026-08-07

Rob: "dead units can have physically impossible interactions with structures." Found by reading,
reproduced in the harness, fixed, and both halves covered by checks.

**`BlockOnStructures` rested a body on a structure's ROOF whenever it was horizontally inside the
footprint and at or above the box's BASE — and a ground structure's base IS the ground.** So a
corpse flung into a wall at chest height was snapped up the face and left standing on top of the
building. The condition's own comment already said *"a body that CLEARED THE WALL should land on
the roof"*; `y >= baseY` was never that test.

### Reproducing it took three attempts, and the first two passing is the interesting part

- A body resting ON the ground dips a hair BELOW `baseY` between ticks, so it escapes the branch.
- A single tick does not carry a thrown body far enough to enter the box at all.
- Only stepping until it actually penetrates shows it: **peak y 4.00 against a roof of 4.0, from a
  launch height of 1.50.**

**A check that never reaches the code it is testing is a green light that means nothing**, which
is the same lesson this file records for the hit flash, the backdrop and the AP multiplier.

### The fix is not simply `y >= topY`

That stops the levitation and then drops a body FALLING onto the roof straight through it, because
once it dips below the roof it no longer qualifies. Whether a body belongs on a roof is a question
about where it CAME FROM — exactly like the existing face test, which is why `fromX` was already
a parameter. `BlockOnStructures` now takes **`fromY`** and asks whether the body was above the
roof LAST tick. Both behaviours are asserted: thrown-at-a-wall never rises, fallen-from-above
still lands.

### Checked and NOT a bug, so nobody re-investigates it

`StepRagdolls` is handed the tick's OPENING structure list (`s.Structures`, before this tick's
destruction is applied), so a razed building keeps blocking ragdolls for at most one extra frame
at 1/60s. That was my first hypothesis and it is wrong.

### What the device pass did and did not show

The free camera was parked at the L3 bunker (x 6.84) and volleys fired into it. It confirmed the
garrison stands correctly ON the deck, and that the destroyed Watch Tower leaves flat ruin slabs —
no impossible placement visible. **It did not catch a corpse-against-a-wall moment**: ragdolls are
short-lived and the volleys that landed did not kill. The harness evidence is stronger and more
precise than a screenshot would have been here, so that is what this fix rests on.

**Caveat worth keeping:** this fixes ONE reproducible mechanism. Rob reported the symptom from his
own play without a screenshot, so if bodies still do something impossible, it is a DIFFERENT
mechanism and this section should not be taken as closing the report. The next things to suspect
are the "spawned inside: just stop, do not teleport" branch (a body whose x begins inside a
footprint stays inside it) and the fact that the collision box is an AABB while the models are not.
