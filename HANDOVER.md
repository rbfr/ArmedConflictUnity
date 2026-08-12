# Handover — Unity, as of 2026-08-12

## START HERE

- **THIS FILE NO LONGER RECORDS WHAT IS COMMITTED OR PUSHED. ASK GIT.**

  ```bash
  git status --short --branch
  git log --oneline origin/main..HEAD     # empty means everything is pushed
  ```

  A sentence naming the unpushed commits was here for four days and was **wrong four times** —
  on 2026-08-11 three times in one day, and again on 2026-08-12 within minutes of a push. Every
  time it was written TRUE and went stale on the next command, which is what a snapshot of
  mutable state does in a document nobody re-reads before editing. It has been deleted rather
  than corrected a fifth time. **Do not restore it.** What is durable, and all that is worth
  saying: **Rob commits and pushes on an explicit ask**, so unpushed work is normal and is not a
  loose end to tidy up unprompted.
  The Android repo's `projectile-refinement` is never being merged; **the Android build is
  RETIRED**, reference only.
- **SEVERAL SESSIONS RAN PER DAY AND THIS FILE DISTINGUISHES THEM BY SUBJECT, NOT BY DATE.**
  2026-08-11 ran three: the airstrike rebuild (signed off), Tier 2.1 + 2.4, then Tier 2.2's hero
  work and a docs pass. 2026-08-12 ran two: Tier 2.2's crowd half, then the Tier 2.3 audit.
  A date alone will not tell you which piece of work a section belongs to — read the heading.
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
- **TIER 2.1 (ENEMY FACTIONS) AND TIER 2.4 (PLAYER CAMO) ARE BUILT AND ACCEPTED BY ROB.** He
  looked at a build on 2026-08-11 and called it: *"ok, uniforms are fine for now."* That is the
  first of this tier's features to clear the only bar that has ever mattered here. Factions are
  L1's Redguard red and L7's Ironclad Legion steel blue-grey; camo is four sets bought and worn.
- **TIER 2.2 IS DONE AND SIGNED OFF BY ROB ON THE DEVICE, 2026-08-12** — both halves in one
  build. He rejected the first hero placement — *"heroes are behind the structure... really tough
  to hit"* — and the fix (rule 8, below) cleared his second look. The crowd half split every
  garrison into more, weaker bodies — 155 -> 248 — at **constant HP, damage and structure
  damage**, proved by building all twelve levels on both data sets and by a `BalanceAudit` run
  byte-identical across all 61 findings. The wide decks roughly doubled their fill (GarrisonPost
  16% -> 34%, BarracksBlock 22% -> 47%). **Shrinking the STRUCTURES was tried first and the
  arithmetic killed it** — see "The crowd split" below.
- **TIER 2.3 IS BUILT AND HAS NOT BEEN ON A DEVICE.** The audit found the roster was NOT
  mechanic-distinct: the machine gunner's burst had never reached the player's volley (one round,
  not three) and measured identically to the shield bearer, whose own advertised mechanic — melee
  — is unported. The burst is fixed and the shield bearer has ARMOUR instead. `RosterAudit.Report`
  now reports 0 errors. **Nothing here has been seen in motion**; see item 1 below.
- **RIGS IS THE TEST SUPPLY FOR EVERYTHING BUYABLE — consumables, camo, UNIT CLASSES and AMMO.**
  Every item free to equip, nothing spent, nothing written to the economy, wardrobe or roster.
  **Classes and ammo were added on 2026-08-12 and had NEVER been covered**, while this file told
  two sessions to "buy both with RIGS on" — which is how a Tier 2.3 device test spent 250 real
  coins on a machine gunner and then could not afford the 500 shield bearer. Use it: the release
  build is not debuggable, `run-as` cannot reach PlayerPrefs, and the test protocol is
  uninstall/reinstall, so every purchase is re-earned on every install.
- **637 self-test checks, all passing — run `PortSelfTest.Run` after every change.** It was 281 at
  the start of 2026-08-06, 411 at the end of it, 444 on 2026-08-07, 539 after the Tier 1.2 and
  glyph-coverage blocks, 559 with the flame and the Auto-ammo pair, 576 with Tier 1.3's
  consumables, 582 with the airstrike's aircraft, 585 with its strafing burst, 587 with the burst's
  absolute count-and-budget check and the aircraft's left-edge entry, 592 with 2026-08-11's
  rake-coverage, aim-independence, whole-burst and impact-alignment checks, 599 with Tier 2.1's
  seven faction checks, 606 with Tier 2.4's camo block, and 607/608/609 with Tier 2.2's
  hero-staging, deck-overlap and collision-box checks — the last of those written because Rob
  found the bug on a device — **625** after 2026-08-12's crowd split, and **628** with Tier 2.3's
  burst check, its roster guard and the armour check.
  Two things about that history are worth knowing before reading a changed number as a lost check.
  **The crowd split's jump was +16 for three new checks** (crowd-split balance, projectile-pool
  headroom, crowd frailty), because several existing assertions are DATA-DRIVEN and log per body —
  **this count moves when the level data moves**, and it was measured on both sides of that change
  rather than extrapolated. And **both of Tier 2.3's behaviour checks were seen RED against the
  old code before being trusted**, with the failing numbers recorded — a check never seen to fail
  is not evidence. **Assert related facts TOGETHER** —
  Tier 1.3's block was first written as 50 assertions over 307 lines and is 18 over 232, with the
  same nine breakages still caught. A failure message naming three properties is as diagnostic as
  three checks, and this file is read by people.

### Pick up here

**637 checks green, all 12 levels pass all eight composition rules, BalanceAudit and RosterAudit
both 0 errors, and everything is committed.** Five commits landed on 2026-08-12's third session:
rule 8 over arrivals + three level fixes, RIGS covering unit classes, rule 7 over arrivals + two
corrections, the burst fan, and ammo-under-RIGS + the victory banners.

**THE NEXT PIECE OF WORK IS ADVANCING SQUADS + MELEE.** It is the only large thing left and it is
a fresh start, not a continuation — everything below the next section is either done or is a
decision waiting on Rob.

### 1. ADVANCING SQUADS + MELEE — the next session's job

**The EIGHTH dead system, and the biggest genuine gap in the codebase.** `AdvanceRemaining` is
written nowhere, `SkirmishEntity` is never created, and `LevelBuilder` pins every PLAYER unit's
`AdvancePerTurn` to 0. What it unlocks:

- **Overwatch Flare**, the one Tier 1.3 consumable deliberately not built, because it has nothing
  to watch for.
- **`PROGRESSION_DESIGN`'s whole survival/defend archetype**, which is made of this and does not
  exist in the port.
- **The shield bearer's 12 melee damage**, which `RosterAudit` currently reports as dead DATA —
  the class was given ARMOUR on 2026-08-12 precisely because its advertised mechanic was unported.
  It goes live the day advancing squads do.

Read `PROGRESSION_DESIGN.md` for the spec, and treat its Status table as describing the RETIRED
ANDROID BUILD, not this one — that trap has cost two sessions. **Check the Unity callers, not the
status table.**

Enemy-side advancing already half-exists and is worth reading first: `advancePerTurn` is authored
on L12's boss shield bearers (1.2) and on L9's, and rule 8 exempts advancing units on the grounds
that they walk out of a wall on their first move. **That exemption is not verified** — see item 5.

### 2. WAITING ON ROB, not on anyone's time

**L12's Sovereign is still in the gate's shadow.** He spawns at x 5.42, static, and the gate
(`FortressTierSmall`, box `x[1.25,3.75]`, top 2.00) stands 1.67 to his left, so hitting him means
clearing a 2.0 wall and dropping 2.0 within 1.67 of travel. **Not unwinnable** — a rifleman does
8 x 0.25 = 2 masonry, so seven of them strip the gate in a few volleys — but the requirement is
invisible and is discovered only after the tank shells are gone, which is exactly how Rob found
it.

The fix is ONE FIELD: `triggerStructureIds: [citadel, gate]`, so the Sovereign emerges only once
both are down and nothing shadows him. Already supported — `ShouldTriggerBossPhase` does
`triggerStructureIds.All(isDefeated)`. Before taking it: citadel 165 + gate 115 = 280 against a
stock siege capacity of ~288, so it makes the requirement HONEST rather than roomy. **It changes
what the finale demands, so it is Rob's call.**

Note this is a SHADOW, not an embedding, and rule 8 does not flag it — correctly. The crude "is it
behind a taller box" heuristic used to find it fires on plenty of harmless geometry (L10 has
arrivals 5.94 clear of a 1.40 box). Deciding which shadows are real needs the game's own
trajectory solver, not a ratio someone invented. **That is a rule 9 and it does not exist.**

**Whether the shield bearer gets a visible armour marker.** Its half-damage is real
(`CollisionSystem.Soaked`, and `RosterAudit` measures `40hp x2.0 armour = 80`) and completely
invisible in play — see item 3. A marker is a unit-art change and this project decides those on a
device, never in advance.

### 3. TIER 2.3 IS MECHANICALLY DONE AND HALF-LEGIBLE — what the device actually showed

Both changes were verified on device on 2026-08-12, and both hit the same wall: **the mechanic is
real and the player cannot see it.**

- **The burst is live and now FANS.** Confirmed by measurement, not by eye: four machine gunners
  put 1.83x the tracer area per shooter of a rifle squad, so six shooters out-tracered ten. It was
  invisible because all three rounds shared one jitter value across Vx and Vy and flew down the
  same 45 degree line. **Fixed** — two independent draws. **The fan has NOT been looked at on a
  device**, and that is the one thing this owes: does three rounds now read as suppressing fire?
- **The armour is live and invisible.** Equal-size squads on L3, four Auto turns each, identical
  enemy attrition (15 -> 11 both runs): 6 shield bearers + 2 crew lost ONE body, 6 riflemen + 2
  crew lost NONE. **That is not evidence the armour is broken** — one death against zero over four
  turns is noise, and Auto changes which enemies survive to shoot back. It is evidence that
  doubling a unit's effective HP produced nothing a player could perceive.

### 4. SMALL AND WELL-DEFINED, if a session wants a warm-up

- **The advancing exemption never verifies the unit leaves.** Rule 8 waves through any unit with
  `advancePerTurn > 0` on the grounds that it walks out of the box on its first move. L11's wave,
  had it been given an advance instead of moved, would have started 0.71 deep and needed THREE
  turns at 1.2 a turn to clear. "Advancing" is not the same claim as "hittable soon". Worth
  closing before advancing squads land and make the exemption load-bearing.
- **`Loadout.GroundAnchorX` averages disjoint groups.** It takes the count-weighted mean of a
  level's ground groups, so a level authored with two flanking groups gets a squad centred in the
  GAP between them. Harmless on all twelve campaign levels (one contiguous line each) and wrong on
  `LevelNaturalParadeTest`, whose two scale-reference groups at -5.6 and +5.6 average to 0.00 —
  dead centre of `RidgeWatchtower`'s box, so a loadout squad spawns inside the structure. Left
  deliberately: the rigs are instruments and the fix touches the path all twelve signed-off levels
  run through.
- **Wind is still cosmetic and PARKED** (Rob, 2026-08-10). `windAccelZ` drifts the round in Z while
  collision is X/Y only, so wind cannot change what a shot hits. **Do not author a wind level until
  someone decides whether collision goes 3D.**
- **Tier 1.4 (Heli) stays shut.** `HELI_ENABLED=false` is a camera-load decision, not a stale flag.

### 5. WHAT 2026-08-12'S THIRD SESSION TAUGHT, which is one lesson four times

**A check asserts the slice of the world it happens to look at, and nobody notices the rest is
unexamined.** Four instances in one session:

1. **Rule 8 read turn 0 only** — boss phases and reinforcement waves were invisible, and four
   embedded units had shipped across L6/L10/L11. Found by Rob playing L12.
2. **Rule 7 read turn 0 only** — same hole, same fix, now closed. A wave authored past the
   ballistic envelope passed every check in the project.
3. **The burst check asserted "each round on its own jitter"** and passed for a day against
   collinear rounds, because distinct aims was already true of the broken version. An INPUT
   assertion wearing the costume of an output one.
4. **The glyph check never covered the victory banners**, and `"New 3★ Best!"` had been drawing a
   missing-glyph box on the congratulations screen the whole time — carrying the exact codepoint
   the same check uses two lines earlier to prove it can fail.

**And one lesson about the fixer, not the checks: DO NOT RE-DERIVE WHAT A TOOL ALREADY COMPUTES.**
While fixing rule 8 this session I hand-rolled a reach estimate — `x - backRank > 20.25` — decided
L11's wave could not be moved behind its post, and moved the whole wave to the far side of the map,
changing a signed-off beat. `BalanceAudit.ReachRule` puts that same body at 91% power from the
front rank and 99% from the back: **it was always in reach.** The estimate ignored `dy` and the
launch envelope, `v² = g(dy + √(dx²+dy²))`; 20.25 is the flat `dy = 0` case and nothing else. The
wave is back at anchorX 9 and the beat is intact. **Ask `ReachRule`. It is the only implementation
that counts**, and this happened in the same session that added a rule whose entire justification
is not re-deriving placement.

**A YAML footnote that cost a real scare:** appending to a level's `designNotes` inserted
unescaped apostrophes into a single-quoted scalar, and `Oceanfront.asset` and `RubbleYard.asset`
stopped parsing. **It hid because a failed import falls back to the Library cache** — the report
kept showing all twelve levels green off stale data, and a reimport on a clean checkout would have
loaded those levels with default fields. Double apostrophes inside single-quoted YAML, and treat
"Unable to parse" in a batch log as a failure even when the run says 0 errors.

### Rule 8 now covers MID-BATTLE ARRIVALS — 2026-08-12, and it found four shipped bugs

Rob, playing L12 on a fresh build: *"there are enemies behind the structure, making them
impossible to hit unless you destroy it. which you can't do if you don't have any tank rounds
left."* He was right, and the cause is that **rule 8 only ever read `BuildInitialState`** — every
boss phase and reinforcement wave was invisible to it. Third instance in one day of the same
shape: a check that asserts only the slice of the world it happens to look at.

Extended to judge turn 0 plus every arrival, placed through the same `LevelBuilder.BuildUnits`
call `BattleTick.Spawn` uses. **Seen RED against the shipped data first**, which is how the four
were found:

| level | arrival | was |
|---|---|---|
| L6 boss | 2 of 3 heavy escort | inside MountainBunker `x[0.88,3.13]` — not the phase's trigger, so still standing |
| L10 wave t4 | 1 of 4 heavies | inside GarrisonPost, by 0.09 |
| L11 wave t4 | 1 of 3 heavies | inside GarrisonPost, by 0.71 |
| L12 boss | the Sovereign | **not embedded** — shadowed by the gate. STILL OPEN, item 6 |

**A boss phase's own trigger structures are exempt for that phase.** L12's Sovereign spawns dead
centre of the citadel it bursts out of, and flagging that would assert a state the game can never
be in. A wave has no trigger, so it is judged against everything standing.

**The fixes.** L6's escort moved 3 -> 4.5 and its Sovereign 5 -> 6.5 (both now emerge from the
breached keep's footprint, which is rubble by then). L10's wave 9 -> 9.4. **L11's wave 8 -> 9**,
which clears the box edge (7.88) by 0.28. All three keep their beats.

**L11 TOOK TWO GOES AND THE FIRST ONE WAS WRONG** — see the "do not re-derive" lesson in the pick-up
section. It was briefly moved to anchorX 0, in FRONT of the post, on a hand-rolled reach estimate
that said there was no room behind it. `BalanceAudit.ReachRule` puts that body at 91% power from
the front rank and 99% from the back: it was always in reach, and the beat did not need to change.

**RULE 7 NOW COVERS ARRIVALS TOO** (same session, second commit) — it had the identical turn-0
hole. Each boss phase and wave is measured ALONE through `ReachRule`, so a finding names the wave
rather than re-reporting whichever turn-0 body is deepest. Seen red at anchorX 16: "108% power,
UNWINNABLE".

**ONE LATENT HOLE DELIBERATELY LEFT**, recorded in `LEVEL_AUTHORING.md`: **the ADVANCING exemption
does not verify the unit actually leaves.** L11's wave, had it been given an advance instead of
moved, would have started 0.71 deep and needed three turns at 1.2 a turn to clear the box.
"Advancing" is not the same claim as "hittable soon".

**WIND IS PARKED** — Rob's call, 2026-08-10, and it is the only thing Tier 1.2 still owes. Work
continues around it rather than waiting on it.

**1. GET ROB'S EYES ON THE TIER 2.3 CHANGES.** Neither has been seen in motion, and both change
how a battle FEELS rather than how it reads in a table:
- **The machine gunner now fires three rounds a volley, each on its own jitter.** The thing to
  judge is whether a squad of them reads as suppressing fire or as noise — three times the rounds
  in the air is the biggest change to what a volley looks like since cluster ammo.
- **The shield bearer now takes half damage.** Nothing on screen says so. Its health bar simply
  falls slower, and if that is illegible the mechanic is real but invisible — which is the
  failure mode `UNIT_VARIETY_DESIGN.md`'s "honest limit" is about. **A visible marker was
  deliberately NOT built**: adding one is a unit-art change and those are decided on a device,
  never in advance.
Buy both with RIGS on (free supply), and note it takes 250 and 500 coins to reach them otherwise.

**2. THE CROWD SPLIT HAS NO REMAINING LEVER. CLOSED 2026-08-12 — do not re-open it on the old
note.** This entry used to read *"doubling again (rifleman x4 at 8hp/2) is exact and burn-safe, so
'more' is a one-line change to `CrowdSplit.Factors`."* **It is exact and it is NOT burn-safe**, and
the paragraph directly below it said so all along about the sniper — same number, opposite
conclusion, two paragraphs apart. Rob's call on being shown the arithmetic: leave the split at x2.

- **The rifleman is HARD-CAPPED AT x2 by the burn.** x4 is 8 hp; the incendiary burn is 8;
  `BattleTick` does `hp = u.Hp - burnDamage; if (hp > 0)`, so 8 - 8 = 0 and the body dies to one
  tick. `CrowdSplit`'s `maxHp / factor <= BurnDamage` guard already rejects it, and
  `PortSelfTest`'s `burn < frailest` anchor would go red as the frailest unit fell 12 -> 8. The
  rifleman carries almost every garrison in the game, so this is most of the lever gone.
- **The only class that CAN double again is the machine gunner** (x4 = 10 hp / 1 dmg, exact, above
  the burn) — and the locked 7-30 roster blocks three of its six decks: L10 would go to 31, L4 to
  35, L6 to 37. Only L5 (24), L9 (30, at the cap) and L11 (22) fit.
- **And those six decks share ONE `EnemyMachineGunnerCrowd` asset**, so re-splitting three of them
  means editing the asset the other three read, silently halving HP on levels Rob has signed off.
  It would need a second `...Crowd2` variant — two machine-gunner bodies in one game — for a
  change visible on three levels. Not worth it.

**The standing lesson here is the one this file keeps paying for: a number written into prose goes
stale, and a DERIVED number written into prose is stale the moment either input moves.** The
burn-safety of a factor is arithmetic over `burnDamage` and `maxHp`; it belongs in `CrowdSplit`'s
guard, where it already lived and was already correct, not in a sentence.

Also worth knowing: **the sniper is deliberately not split** (16 hp only halves to 8, into the same
burn problem), and **L12's gate is deliberately not split** (both its garrisons would take the
roster to 31, past the locked 7-30). Both are enforced in `CrowdSplit`, not just remembered.

Before any further crowd/art pass, read `UNIT_VARIETY_DESIGN.md`'s "honest limit" twice: stance,
faces and limb fold each cleared "is it correct" and failed "does it survive the frame", and the
only changes that ever DID read were large-scale layout.

**3. RULE 8 NOW REPORTS WHERE THE OTHER SEVEN DO — DONE 2026-08-12.** It lived only in
`PortSelfTest.CheckNobodyStandsInAWall`, so it failed the SUITE while rules 1-7 rendered live in
`LevelDefinitionInspector` beside the level being edited; an author saw seven rules where there
are eight. It is now `LevelComposition.CollisionBoxRule`, called from `LevelComposition.Check`,
and the suite check DELEGATES to it rather than re-measuring — one rule with two implementations
is the second-source-of-truth failure this project has already paid for, and is why rule 7 is
called out of `BalanceAudit` instead of copied.

It reports as an **ERROR**, the only non-roster error in the file, so `LevelComposition.Report`
now exits 1 on it exactly as the suite always did. Rules 1-6 are framing judgements a level may
bend for a reason it records in `designNotes`; rule 8 says a unit the player is asked to kill
cannot be hit.

**Seen RED before being trusted**, per the standing rule: L6's two heavy riflemen moved from
x -1 to the keep's x 6 put them 1.73 INSIDE the box, and that failed `LevelComposition.Report`
(exit 1) and `PortSelfTest` together, naming `FortressTier edge 3.88` — the same edge from the
original bug. Restored, and both are green again at **628 checks**, unchanged by the move.

**4. THE TWO BIGGEST OPEN THINGS, both physics/AI asks rather than scheduling jobs:**
- **Advancing squads + melee are unported** — an EIGHTH dead system, and the one that holds
  Overwatch Flare. `AdvanceRemaining` is written nowhere and `SkirmishEntity` is never created.
  `PROGRESSION_DESIGN`'s whole survival/defend archetype is made of this.
- **Wind is still cosmetic** — `windAccelZ` drifts the round in Z while the collision test is X/Y
  only, so wind cannot change what a shot hits. A wind schedule would telegraph a change the
  player cannot feel. **Do not author a wind level until someone decides whether collision goes 3D.**

**6. L12'S SOVEREIGN IS STILL IN THE GATE'S SHADOW — ROB'S CALL, HELD DELIBERATELY.**
He spawns at x 5.42, static, and the **gate** (`FortressTierSmall`, box `x[1.25,3.75]`, top 2.00)
is still standing 1.67 to his left. Hitting him means clearing a 2.0 wall and dropping 2.0 within
1.67 of travel. It is **not unwinnable** — a rifleman does 8 x 0.25 = 2 masonry, so seven of them
strip the gate's remaining hp in a few volleys — but the requirement is invisible and is
discovered only after the tank shells are gone.

The proposed fix is ONE FIELD: `triggerStructureIds: [citadel, gate]`, so the Sovereign emerges
only once both are down and nothing shadows him. Already supported —
`ShouldTriggerBossPhase` does `triggerStructureIds.All(isDefeated)`. Worth knowing before taking
it: citadel 165 + gate 115 = 280 against a stock siege capacity of ~288, so it makes the
requirement HONEST rather than roomy. **Do not apply it without Rob** — it changes what the
finale demands.

Note this is a SHADOW, not an embedding, and rule 8 does not flag it: the crude "is it behind a
taller box" heuristic used to find it fires on plenty of harmless geometry (L10 has arrivals 5.94
clear of a 1.40 box). Deciding which shadows are real needs the game's own trajectory solver, not
a ratio someone invented — that is a rule 9, and it does not exist.

**5. Tier 1.4 (Heli) stays shut.** `HELI_ENABLED=false` is a camera-load decision, not a stale
flag. Do not flip it.

**THE AIRSTRIKE IS DONE AND SIGNED OFF** ("ok this will work"). One small open note: the aircraft
*"gets fairly large as it passes nearest the camera"* — `BattleTick.PlaneY` is a one-constant fix
if it reads as too much, judged at full speed rather than on a contact sheet. The pass-by sound is
explicitly closed: *"the aircraft sound is fine for the moment."*

**THE PLAYER HAS NO HERO ANYWHERE, and that is a decision** (Rob, 2026-08-11: enemy-only for now).
`HeavyRifleman` is not among the six pickable roster slots. Revisit only with a build in hand.

**Device state at handover:** the installed build is from 2026-08-12 but **PREDATES THE LAST TWO
COMMITS** — it has Tier 2.3, the level fixes and RIGS-for-classes, but NOT the burst fan, ammo
under RIGS, or the victory banners. **Build before judging the fan.** L3, **320 coins**, RIGS off,
nothing written to the wardrobe or roster, every dev capture cleared off `/sdcard`. **DND and
stay-awake were left ON** — it is Rob's real phone, so turn both back.

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

### Also standing, and not on the list above because none of it is a next task

- **Tier 1.1 is CLOSED except for two UNRESOLVED items**, both in `_plans/BACKLOG.md`: **is
  Cluster's 3.2x spread too wide to connect?** (needs Rob at the controls — a scripted drag cannot
  settle it), and **flames outlive their bodies by a frame or two** at the moment the burn kills.
- ~~A per-frame NullReferenceException on the LOADOUT screen.~~ **FIXED 2026-08-10** — the tick
  running with no battle to tick. That screen is clean, which matters because the consumable UI is
  built on it.
- **`_plans/BACKLOG.md` is the only LIVE plan**, and holds what Rob has parked: a **nuclear
  reactor structure** (the open question is what MECHANIC it owns — a blast on destruction would
  make it the first structure with one), **dead units sinking** into the ground instead of
  vanishing, the **ragdoll / structure report** (PARTLY fixed, deliberately open), and a
  **crowd-runner bonus level**. The three finished plans moved to `_plans/archive/` on 2026-08-11;
  nothing there describes current behaviour.

### How the rest of this file is ordered

Everything below "Pick up here" is HISTORY, newest first: the two 2026-08-12 sessions (the Tier
2.3 audit, then Tier 2.2's crowd half), then the three 2026-08-11 sessions, then
2026-08-07 to 08-10, then the standing reference sections — **"Where things are", "What works",
"The workflow", "Traps already paid for"** and **"Open items"/"Things that will bite"**, which are
the parts that are still TRUE rather than still interesting. The closed 2026-08-05/06 port entries
are in `HANDOVER_ARCHIVE.md`.

### The Tier 2.3 audit — 2026-08-12. Three of six classes were not what they were sold as, and two are fixed

**Tier 2.3 is "keep the roster mechanic-distinct", and the honest way to read it is not from the
data sheets.** Six classes differ on paper; the .asset files carry seven distinguishing fields
between them. The product question is what the player gets when they FIRE, and in this build those
were three different things: a field only differentiates a class if some live path reads it.

`RosterAudit.Report` is the instrument, and it is the "assert the OUTPUT, not the input" rule
pointed at the roster — it fields an all-of-one-class squad, fires a REAL volley through
`BattleTick.FireVolley` (the function the drag calls), and measures what comes out. Run it:

```bash
DISPLAY=:1 $U -batchmode -quit -projectPath . -executeMethod RosterAudit.Report -logFile -
```

**WHAT IT FOUND, and none of it was visible in the assets:**

| class | pt | coins | hp | delivered per shooter, per volley |
|---|---|---|---|---|
| Rifleman | 1 | 0 | 32 | 1 round x 8 = 8 dmg, 2 masonry |
| MachineGunner | 2 | 250 | 40 | **was 1 round x 4**, now 3 x 4 = 12 dmg, 3 masonry |
| Grenadier | 2 | 350 | 24 | 1 x 6 = 6 dmg, 12 masonry, 0.9 splash |
| Sniper | 2 | 400 | 16 | 1 x 20 = 20 dmg, 10 masonry |
| ShieldBearer | 2 | 500 | 40 | 1 x 4 = 4 dmg, 1 masonry, **now x2 armour = 80 effective hp** |
| RocketTrooper | 3 | 700 | 24 | 1 x 4 = 4 dmg, 24 masonry |

- **THE MACHINE GUNNER'S BURST HAD NEVER REACHED THE PLAYER.** `projectilesPerVolley` was read by
  `AutoFire` and by nothing else, so the class the store sells for 250 coins as *"fires a burst
  instead of a round"* fired ONE round: a rifleman at half damage for twice the points.
  **It measured IDENTICALLY to the shield bearer on every axis** — which is precisely the failure
  Tier 2.3 exists to forbid. **FIXED**, and the jitter moved inside the burst so three rounds land
  as three (one jitter for all three would be one round doing triple damage, which is not what the
  copy promises). This is the fourth member of a family the code comments right beside it already
  describe: `Type`, `SplashRadius` and `StructureDamageMultiplier` went missing from the player's
  volley the same way and were fixed earlier. **When one field turns out to be read only by the
  debug driver, audit the whole record — not that field.**
- **THE SHIELD BEARER SOLD A MECHANIC THIS BUILD DOES NOT HAVE.** `meleeDamage` has no runtime
  reader at all, `SkirmishEntity` is never constructed, and `LevelBuilder` pins every PLAYER
  unit's `AdvancePerTurn` to 0 — so the 500-coin class advertised as *"walks forward and fights
  hand to hand"* could not advance even in principle. It was a rifleman with +8 hp and -4 damage.
  **FIXED with ARMOUR** (Rob's call): `damageTakenMultiplier` is 0.5 on the player's shield
  bearer, so it takes half of every round and presents 80 effective hp against a rifleman's 32,
  while dealing half the damage. That is a real trade rather than a stat tweak — over a long fight
  it out-damages a rifleman (80/8 = 10 volleys x 4 = 40, against 32/8 = 4 x 8 = 32) and over a
  short one it loses. **`meleeDamage` is now dead DATA rather than a missing mechanic**, and goes
  live if advancing squads ever land.
  Applied in `CollisionSystem.Soaked`, at BOTH damage write sites — the direct hit and the splash.
  A multiplier on one path only is precisely how the incendiary burn and the structure multiplier
  each went missing in this port, and the check asserts both plus the FLOOR: a soaked round never
  rounds to 0, because that is not toughness, it is immortality, and a battle that cannot end is
  not something a damage assertion would ever catch.
  **The ENEMY shield bearer is deliberately NOT armoured** — four campaign levels field it (L4,
  L8, L9, L12) and arming it would make signed-off content harder, the same call Rob made on the
  enemy burst.
- **`bulletVariant` is dead data.** A three-value enum (Standard/MachineGun/Sniper) authored on
  every unit, carried on the projectile record, and read by no runtime code. It distinguishes
  nothing, mechanically or visually.
- **Enemy machine gunners fire one round while declaring three.** `FireEnemyVolley` has the
  player's old bug. Left alone DELIBERATELY: fixing it triples those bodies' output across seven
  signed-off levels, which is a balance decision, not a bug fix. **DECIDED 2026-08-12: leave the
  enemy at one round.** The campaign is tuned against what the engine actually throws, and
  `BalanceAudit` now counts each side by its own fire path rather than by the assets — the player's
  volley WITH the burst, the enemy's without.

**THE METRIC WAS WRONG BEFORE THE FINDING WAS.** The first domination check rated classes per
POINT and duly condemned the machine gunner immediately after its burst was restored. That is the
tell: *a metric that indicts the thing you just fixed is the wrong metric.* Slots cap BODIES and
points cap QUALITY, and which one binds MOVES THROUGH THE CAMPAIGN — every level has 8 slots while
the budget climbs 8 -> 16, so L1-L2 run at parity (a premium pick is paid for in bodies, per-point
is the honest lens) and from L3 the budget outruns the slots (the line is full whatever you pick,
so per-SLOT is). The audit now reports both and only errors when a class loses on both.

**And the first RUN of the audit was wrong too, in a way worth keeping:** it reported the 4-damage
machine gunner as doing 5.3. `Loadout.ToPlayerGroups` keeps the level's garrisoned player groups —
the tank crew — because the loadout is forbidden to touch them, so every class's measured volley
silently carried the same two riflemen and the average measured the garrison as much as the class.
**A per-unit average over a squad you did not fully control is measuring the level, not the unit.**

### What 2026-08-12 changed — Tier 2.2's crowd half, and an approach that died to arithmetic

**The measurement came before the edit, and it is what saved the session.** The obvious fix for
"a small clump on a wide deck" is the mirror of the cause — the body shrank 0.77 -> 0.48 and the
buildings did not, so shrink the buildings. Rob picked that option. **`DeckFillReport` then killed
it in one run**, and the numbers are worth carrying because the reasoning generalises:

- **No single factor works**: the decks are already 4.6x inconsistent with their own garrisons
  (L6's MountainBunker 69%, L12's FortressTierWide 15%), so any global shrink overflows the tight
  decks before it fills the loose ones.
- **Per-structure factors are worse.** For each deck's CURRENT garrison to fill it, GarrisonPost
  needs **x0.22** and FortressTierWide **x0.21**.
- **And the scale is uniform** — `LevelScenery` draws every enemy structure as
  `Vector3.one * worldScale`, there is no per-axis squash — so GarrisonPost at x0.22 stands **0.55
  units tall against a soldier's 0.48**. The building would be the size of the men on it, and
  rule 3 is built on there being one dominant structure.

**The inversion is the part to keep: our decks are already sized like the reference's.** A 3.13
deck seats 17 per rank at the derived pitch and the reference runs ~15. The geometry was never
wrong; the roster is a third of the size. Shrinking the building would have made it un-reference-
like in order to hide a roster gap.

**So the crowd split is what shipped**: every garrisoned group becomes more, weaker bodies at
constant HP, damage and structure damage. 155 garrisoned bodies -> 248. Full write-up, tables and
traps in `UNIT_VARIETY_DESIGN.md` "Tier 2.2, part four". What belongs here:

**THE INVARIANT WAS PROVED, NOT ASSERTED.** Every level was built on both data sets and all three
totals matched on all twelve; `BalanceAudit.Report` came back **byte-identical across all 61
findings**. That is the control shot for "no level Rob has signed off gets harder", and it is the
only reason this was safe to apply to signed-off content at all.

**THREE CONSTRAINTS PICKED THE FACTORS, AND TWO OF THEM WERE FOUND BY A CHECK GOING RED:**

- The factor must divide HP and damage EXACTLY, or the split silently retunes the level.
- **No crowd body may fall to the incendiary burn's 8 damage.** The first table split the sniper
  x2 and the grenadier x3 — both land on exactly 8 hp — and the roster-frailty check went red
  immediately: the burn stops CHIPPING and starts one-shotting. That check exists because
  HANDOVER's own open item asked for it to be anchored to the live roster so it could not expire
  silently. It earned its keep today. The sniper is now not split at all.
- **The 7-30 roster scale is a LOCK**, and `LevelComposition` caught L12 at 31. `CrowdSplit` takes
  groups worst-clump-first and refuses any split that would breach it, so L12 splits its citadel
  and leaves its gate.

**ONE THING IS NOT NEUTRAL AND IT IS MEASURED RATHER THAN GLOSSED.** Aggregate HP is preserved, but
kills happen per BODY and the last round into each body wastes its overkill: a 40 hp machine gunner
takes 5 rifle rounds, two 20 hp ones take 6. Riflemen are unaffected (16 and 32 are both multiples
of 8). Campaign rounds-to-clear **670 -> 695, +3.7%**, concentrated on seven levels at 3-5 rounds
each — a third to a half of one player volley. Five levels including L12 are untouched. **There is
no factor that removes it**: 40 has no divisor that is both burn-safe and a multiple of 8. And
`BalanceAudit` models HP in aggregate, so it cannot see this at all.

**THE PROJECTILE POOL OVERFLOWS SILENTLY, and this change walked up to it.** The draw loop skips
any round past the end of the pool while it still flies and still damages. L12's enemy volley went
~23 -> 51 bullets against a pool of 64. `ProjectilePoolSize` is **96** now and `PortSelfTest`
measures the campaign's real peak against the real constant rather than a copy of it. Negative run
at 48: `worst is L12 Bullet at 51 rounds against a pool of 48`.

**NO SCENE REBUILD IS NEEDED**, and that is a property of how it was built rather than luck: the
crowd variants share their parent's `modelAsset`, which is what `BattleRunner.UnitClassKey` keys
on, so they reuse the same prefab and the same data-sized slot pool. They are separate definitions
rather than a per-entity stat override because `UnitEntity` reads its stats off `Definition` in
eight places — "grep for EVERY READER" is a trap this repo has paid for twice.

**`DeckFillReport.Run` is the new instrument** and it only measures; `CrowdSplit.Apply` is the
authoring step and is idempotent.

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

### The docs pass — 2026-08-11, end of the third session

**`LEVEL_AUTHORING.md` is the EIGHT composition rules now.** Rule 8 — every GROUND unit stands
clear of every structure's COLLISION BOX, which is `hitWidth` wide and nothing like the width of
the building you see — was enforced by a check for a whole session before it was written down
anywhere an author would look. `CLAUDE.md`'s three "seven rules" references were corrected with it.
**Rule 8 is checked by `PortSelfTest`, NOT by the inspector or `LevelComposition.Report`**, so it
fails the suite rather than appearing beside the level you are editing. That asymmetry is recorded
in `LEVEL_AUTHORING.md` rather than quietly tolerated, and is worth closing next time someone is
in `LevelComposition`.

**`_plans/` was carrying three finished plans as though they were live.** They are in
`_plans/archive/` now, and `TIER0_PLAN.md` is why it mattered: four days after the balance audit
was run and Tier 0 signed off, it still said the device half was "still owed". That is precisely
the failure `_plans/README.md`'s own opening paragraph warns about, and it survived because **a
finished plan sitting beside a live one looks live.** `BACKLOG.md` is the only current plan.

**`HANDOVER.md` was 3128 lines and is 2126.** The closed 2026-08-05/06 port entries — 21 sections,
the whole first two days of the port — moved to `HANDOVER_ARCHIVE.md`. Nothing was summarised or
deleted; the reasoning in this project is the valuable part. **"Traps already paid for" and "Open
items"/"Things will bite" deliberately STAYED**, because they are the two sections that are still
true rather than still interesting.

### What the THIRD 2026-08-11 session changed — Tier 2.2, part one

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

### The hero placement was WRONG and Rob caught it on the device

*"heroes are behind the structure which makes them really tough to hit without firing at a steep
angle."* Correct, and geometric. **A structure blocks as a box `hitWidth` wide, which is not the
width of the building you see** — L6's keep is drawn around x 6 and blocks from **x 3.88**. The
heroes were placed at 4.3, "in front of the keep", measured off its ANCHOR, and landed inside it.
L12's were **1.71 deep** in the citadel's box.

To hit a unit inside a box you must clear the box top and reach the ground within the same
fraction of a unit — L6 wanted a 2.0 drop in 0.02 of travel. **The hero pass had made them harder
to hit than when they stood on the roof**, because a garrison on a deck is above every box and
takes an ordinary arc.

Placement rule now: **clear of every enemy structure's box, with nothing between the hero and the
player** — only a box at LOWER x can shadow a left-to-right shot. L6 -1.0, L7 2.7, L11 3.2,
L12 0.6.

**Rule 7 cannot see this and no rule can.** It measures distance and height and asks whether the
roster has the POWER; there is nothing in its model about what is IN THE WAY. All twelve levels
passed all seven rules the whole time. **Reach and a clear line are different questions.**

**The new check indicted SHIPPED content and the content was wrong.** `CheckNobodyStandsInAWall`
found four riflemen the campaign already had — two on L9 inside the mountain bunker, two on L10
inside the outpost, none of them hittable without the same plunge. Moved out with the heroes. The
standing warning about a new check indicting old content is not "assume the check is wrong", it is
**go and measure which one is wrong**. ADVANCING units are exempt semantically: L9's shield bearers
start 0.01 inside on jitter and walk out on their first move.

Negative run: `10 of 43 ground units embedded, tightest -1.71 on L12`. Device-confirmed on L6 with
a deliberately SHALLOW drag (~241px per axis against L1's 331) that killed three, 16 -> 13.

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

### Where the Tier 0 write-ups are — ARCHIVED 2026-08-11

**They are in `HANDOVER_ARCHIVE.md` now**, along with every other closed 2026-08-05 and
2026-08-06 port entry — 21 sections, moved when this file hit 3128 lines and the current state was
buried under two days of finished work. Each still says what shipped, what it cost and what it
found; none of them describes current behaviour, which is why they moved.

**What stayed here:** START HERE, everything from 2026-08-07 onward, "Traps already paid for",
and "Open items" with its "Things that will bite" list. If an archived entry seems to describe how
the game works today, check this file first — it is accurate about the day it was written and
about nothing else.

| Section (in `HANDOVER_ARCHIVE.md`) | What it covers |
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

## Open items — in the order I would take them

**This list was written before Tier 0 and most of it has SHIPPED.** Kept for the reasoning, which
is still the record of why each thing was worth doing. Current state:

1. ~~Unit art: every class renders as the same rifleman.~~ **DONE 2026-08-06.**
2. **A decision, not a task: re-tune incendiary, or leave it.** STILL OPEN. `burnDamage = 6` was
   calibrated to finish the 8hp Sniper in one tick and that unit no longer exists (the roster cut
   gave the Sniper the Marksman's 16hp). Deliberately NOT raised — doubling a 300-coin consumable
   is a balance call, not a side effect of deleting a class — so a tick is now a ~37% chip rather
   than a kill. `AmmoTest` anchors to the roster's frailest unit, so it will not silently expire.
3. ~~Loadout screen.~~ **DONE 2026-08-06** — see "Loadout" in `HANDOVER_ARCHIVE.md`.
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
