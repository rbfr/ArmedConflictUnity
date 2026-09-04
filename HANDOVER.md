# Handover — Unity, as of 2026-09-04

## Pick up here

Last sitting **09-04**, a long one. **Everything is committed AND
PUSHED** — seven commits on `session/2026-08-25-shell-art-ragdoll`,
`c9b8d40..670c643`. The working tree is clean apart from the untracked
tracer leftovers below. **Ask git anyway** — this file does not track
commits, and Rob commits/pushes on an explicit ask.

`PortSelfTest.Run` after every change; it now carries rules 8 AND 9.
**RIGS** is the test supply (consumables, camo, classes, ammo) — the
clean install RESETS it, so check the button rather than assuming.
**Do not use Auto** for structures, ammo, or consumables. Android repo
is RETIRED. `DISPLAY=:0` (not `:1`).

**The phone has the 09-04 END-OF-DAY APK** — all seven commits. Boots
to L1's picker, coins **4085**, RIGS off, DND/stay-awake/auto-rotate
restored. Smoke-checked on L1 and L9 only.

### What 09-04 shipped, in the order the blocks appear below

| | |
|---|---|
| Rule 8 tightened | no unit starts inside a building, at all. Found two the old silent exemption hid (L9, L12) |
| **Rule 9, new** | the ballistic-shadow checker. FIRES REAL SHOTS. Found a man on L10 nobody could hit |
| L6 tested properly | picker entry, RIGS, 14/14 points, ammo + consumables. Three defeats. The spike is entirely the boss phase |
| Boss telegraph | pillar 7 finally reaches the boss. Threshold 0.5, measured |
| Ragdoll flail | it was INTEGRATING — 179.9° of wind-up. Now an offset from rest |
| Snow pine | had no snow in it. Rebuilt |
| L6's last folded deck | bunker 8 -> 6, the two men moved to the keep. Campaign is single-rank throughout |

### Owed, in the order I would take it

1. **L12's boss phase has never been seen on device.** Its escort
   moved 5.5 -> 7.0 and its Sovereign 7.5 -> 9.0 this sitting, and
   `LevelComposition` is the only thing that has checked them.
   Reaching that phase means bringing the citadel down. **This is the
   one unverified change on the phone.**
2. **L6 has never been won**, before or after any of today's work.
   Rule 9 says the Sovereign IS reachable, so this is difficulty and
   aim, not a bug — see the block on what three losses did establish.
   It wants a player, not more of my drags.
3. **A campaign run, L1 → L6, through the picker.** `PRODUCT_DIRECTION`
   gates Tier 3 on *"after >= 5 fun sessions exist"* and nobody has
   ever played the funnel end to end. Tiers 0-2 are otherwise closed.
4. The two standing composition warnings — L3 rule 7, L5 separation
   11.6 — both long-standing, both documented, neither touched today.

**Do not open unprompted:** tracer look, Kenney particles, a new death
clip. Leftover and deliberately NOT wired:
`Assets/Models/Kenney/Particles/` and `Assets/Materials/TracerSprite.mat`.
Do not tidy unless he asks.

### Two lessons 09-04 kept re-teaching

**An instrument beats a story.** I was sure L6's boss was unreachable —
nine powers, nothing killed, and a documented hole in rule 7 to blame.
Rule 9 cleared L6 on its first run. The bug was my aim. It then found a
REAL unhittable man on L10, which no amount of playing had suggested.

**A silent exemption is where things hide.** Rule 8 had waved
clears-on-first-march through with a bare `continue` for as long as it
had existed. Removing it surfaced a shield bearer standing 0.07 inside
L9's bunker. Nothing would ever have reported that.

### Closed 09-04 — NO UNIT STANDS INSIDE A BUILDING, and the exemption that hid two

Rob: *"i dont think we should have enemy units within the buildings...
that doesn't make sense."* Rule 8 now ERRORS on an advancing unit
inside a collision box, however fast it marches clear.

**This overrides a deliberate split.** Until now, clears-on-first-march
was waved through SILENTLY, clears-eventually was a Warning, and only a
static embed was an Error. That split reasoned about HITTABILITY and on
that axis it was right — but it answers the wrong question. A man
standing inside masonry is not a pacing judgement, and no march he makes
next turn changes what the player sees on the turn he arrives.

**Nothing is lost: rule 9 carries the hittability half and carries it
better**, firing real shots and following an advancer march by march.
Rule 8 is now free to mean the simple thing its name says.

**The tightening found two cases the old split had hidden:**

- **L12's boss escort** spawned at anchorX 5.5, inside the GATE's box
  [3.25, 5.75]. The citadel does not count — it is the phase's own
  trigger and rubble by then — but the gate is a separate structure
  still standing. Moved to **7.0**, with the **Sovereign 7.5 -> 9.0** to
  keep the 2.0 gap the design notes asked for so the two groups do not
  stand on each other. Deeper into the citadel footprint is where the
  beat always wanted them: emerging from the breach.
- **L9's charge group** — the one worth remembering. A shield bearer
  **0.07 of a unit inside the mountain bunker**, exempt for as long as
  the rule has existed because it cleared on its first march. Moved
  anchorX **3 -> 2.8**. A silent exemption is exactly where that hides.

**Red before green: 2 offending levels, exit 1**, with both named. Green
after. **Campaign now 2 warnings, 0 errors** — the only two left are the
long-standing L3 rule 7 and L5 separation.

One bug of my own, caught by the report disagreeing with itself: the
first cut of the change dropped a `continue`, so an advancing unit was
counted in BOTH buckets and L9 was reported under the static-embed
message. The numbers not adding up is what showed it.

### Built 09-04 — RULE 9, the ballistic-shadow checker, and it found a shipped bug

Offered twice, declined twice as theoretical, then earned. **L6's boss
phase was played three times and nothing in it could be killed, at NINE
distinct powers spanning the whole envelope (45 to 88%).** Difficulty
does not look like that, so the standing suspicion was that the
Sovereign was unreachable — `CLAUDE.md` had already written the hole
down: *"rule 7 still reads turn 0 only; an arrival placed out of the
ballistic envelope is caught by nothing."*

**`LevelComposition.BallisticShadowRule` FIRES THE SHOT.** A sweep of
real trajectories through `TrajectoryPhysics.Step` at the tick's own dt,
against `CollisionSystem`'s own boxes and `SweptCollision.UnitHitRadius`,
counting how many land on the man. Zero is an ERROR, a handful is a
NEEDLE warning. Turn 0 and every arrival, via rule 8's `ArrivalSets` —
`DeadByTrigger` included, or every boss in the game would be condemned
for the rubble it bursts out of.

**IT CLEARED L6.** The Sovereign IS reachable. **My hypothesis was
wrong and the instrument said so** — three lost boss phases were aim,
not a bug, which is exactly what the checker was built to settle.

**It found a real one on L10.** The turn-4 heavy wave has **one man who
cannot be hit by any drag**. The depot sits at x 8 with a `hitWidth` of
3.75, so its box ends at **x 9.875** and stands 1.25 tall; the leftmost
heavy lands at **x 10.2** — a third of a unit past the far edge.
Clearing the box there and dropping to head height needs ~70° of
descent, and 70° carries only 14.5 units when he is 17.4 out. **The
other three, further out, ARE reachable**, which is the shadow behaving
exactly as the geometry says and why nothing saw it by eye. The wave is
`advancePerTurn: 0`, so he never walks clear.

**Corroboration worth trusting.** On L12 rule 9 reports the boss shield
escort *hittable after 2 marches*, from fired trajectories — and rule 8,
measuring box geometry by a completely different method, independently
says *2 turns to clear*. Two unrelated instruments on the same number.

**FIXED 09-04: the depot moved x 8 -> x 7.** One structure, one unit
left. The wave did not move, no count changed, separation is 16.5 and
still inside the 14-20 band, and **all nine rules pass**.

**The wave was never the thing to move, and the search proved it.**
Behind the depot there is no good spot at all: the shadow reaches to
about x 11.4 and rule 7's comfortable band ends right about there.
anchorX 12.1 is still shadowed; 12.4 clears rule 9 and immediately
raises rule 7's back-rank warning; 13.0 the same. **The two constraints
close the gap between them.**

**Giving it an advance does NOT work here, which is worth knowing.**
Advancing moves a unit TOWARD the player — L12's escort clears because
it starts INSIDE a box and walks out the near edge, but L10's heavy is
behind the FAR edge, so a march walks him INTO the depot for several
turns. The two cases look identical in a report and are opposites.

Shortening the depot was rejected: `GarrisonPost` is shared with L2 and
L8, so it would ripple. Moving the placement fixes the CAUSE — the
shadow — without touching anything shared. x 7.4 still leaves a needle;
7.0 is clean.

**Rule 9 is now wired into `PortSelfTest`**, delegating to
`BallisticShadowRule` exactly as rule 8 delegates to `CollisionBoxRule`
— one implementation, not two. Wired AFTER the fix: landing a red suite
on shipped content ahead of the fix teaches the next person to ignore
it. **Red before green confirmed** — the depot put back at x 8 fails the
suite with the L10 heavy named, exit 1.

**Campaign now reports 4 warnings, 0 errors.**
`LevelComposition.Report` now exits 1 on that error, which is the
checker doing its job and not a regression.

### L6 TESTED PROPERLY 09-04 — three defeats, and the spike has a name

The owed picker play, done: RETRY into L6's own picker, **RIGS test
supply so nothing came off Rob's inventory**, 8/8 troops and 14/14
points (3 Rifleman + 3 Grenadier + 1 Sniper + 1 Rocket), Airstrike
and Early Reinforcements carried, Incendiary on decks and AP on the
boss. **It still lost.** But the two halves of the level tell
completely different stories, and that is the finding.

**The garrison half is well tuned.** Four volleys — 77% on the
bunker, 88% on the keep deck — took 27 -> 9, destroyed both
structures, and cost two men. The grenadiers shred a packed deck
exactly as the picker text promises. Nothing to do here.

**The boss half is where the level is decided, and it is a cliff.**
From 8-9 units the trade was roughly **1.5 losses per turn for 0.25
kills**. Across two full boss phases I killed ONE thing.

The mechanical reason: **the boss phase is a MOVING-TARGET problem in
a game that has taught nothing but static ones.** Power had to walk
`88 -> 77 -> 64 -> 54 -> 50 -> 45` as they closed, there is no
distance readout, and each wrong guess is a wasted turn costing two
men. In run 2 the band was found by RECORDING a volley and reading
the impact off the screen — 54% landed on them — and by the next turn
the band had moved again.

And the trigger compounds it: **the keep falling spawns the Sovereign,
so the level ambushes the player for doing the thing it just taught
them to do.** That is the intended beat, but it lands when the army
is spent.

**Limits of this result, stated plainly.** One player, three
attempts, and two of them had a clear aiming error. The pre-boss half
is repeatable and easy; the post-boss half won even when aimed
correctly with a saturated loadout. **The spike is real and it is
entirely in the boss phase.** L6 has still never been won.

### Closed 09-04 — pillar 7 reaches the boss at last

Rob: *"it's worth fixing."*

`ReinforcementWaveBeat` refuses to let a four-man squad skip its
warning — *"a wave is not allowed to opt out of it"* — and L10/L11
both carry two-turn leads. **Meanwhile the Sovereign, 260 hp and a
heavy escort, arrived on L6 and L12 with nothing at all.** Not a
decision: a boss fires on a STRUCTURE FALLING rather than on a turn,
so it was never wired to the telegraph strip.

**A boss cannot borrow the waves' countdown.** A wave has
`arrivesOnTurn`, so "2 turns" is a fact; the player owns the boss's
clock and a number would be a lie. What can honestly be warned is
PROXIMITY TO THE TRIGGER, so it is a HEALTH THRESHOLD on the
structure gating the phase, and the line carries no number.

- `BossPhaseTrigger.telegraphLabel` + `telegraphAtHealthFraction`
- `EventSystems.ShouldTelegraphBossPhase` — 0 is EXCLUDED, not
  clamped: the gate is already down, the phase fires that tick, and a
  warning arriving with the thing it warns about is not a telegraph
- A wave keeps the strip when both want it. Its deadline is fixed and
  the player cannot move it; the boss's stays true as long as they
  leave the gate standing.
- Both campaign bosses authored, ASCII only (the em-dash
  missing-glyph trap is what bit the one shipped wave telegraph).

**Red before green: `2 of 2 campaign boss phase(s) have no
telegraphLabel: L6, L12`** — which is exactly the state the game
shipped in. Device-verified on L6: **`Something moves behind the
keep`** in the strip at Fortress Tier 33/139, phase not yet fired.

**The threshold is 0.5, and 0.35 was wrong — measured.** One volley
took the keep **137 -> 33, 75% of the structure in a single turn**. A
narrow band is not crossed slowly, it is JUMPED: the first run of the
day never showed the line at all because the keep passed straight
through it. Widening does not buy more turns against a heavy volley,
which can still clear the whole band at once — **it buys the warning a
chance to fire against the lighter ones.** The strip was
device-verified at 0.35; 0.5 only widens the band on the same code
path and render.

**NOT claimed: that this makes L6 winnable.** It removes the
blindside, which is a pillar violation worth fixing on its own terms.
Whether the boss phase is still too steep is unmeasured, and L6 has
never been won.

### Closed 09-04 — the flail INTEGRATED, and the snow pine had no snow

Two reports off L7, unrelated, both mislabelled at the source.

**"Their arms are spinning out of control."** Real, and worse than it
looked. `UnitAnim.Wave` composed onto `t.localRotation` — its own
previous value — every frame:

```
t.localRotation = Quaternion.Euler(...) * t.localRotation;   // WRONG
```

That reads as additive, and the aim lift does exactly the same thing.
**The difference is what is underneath.** The aim lift sits on a clip
that rewrites the joint every frame; a CORPSE HAS NO CLIP PLAYING —
`Set(Die)` stops all of them — and `LateUpdate` deliberately skips
`RestoreStance` for a flailing body. With nothing re-establishing a
base the multiply INTEGRATED, so the longer the fall the faster the
spin. L7 drops them off a building, which is why it showed there.
`ApplySlump` had the identical defect on torso and head, and its
easing gives the intent away: `rise = 1 - exp(-8·age)` converges on a
FIXED fold, which means nothing if each frame stacks on the last.

Both are offsets from a captured rest now (torso and head needed
their rest capturing; the limbs already had theirs).

**Red before green, with numbers: 179.9° on `arm-left` — fully
inverted — against the old code, 20.5° with the fix**, which is the
true composed amplitude of an 18° X / 9.9° Z flail.

**Why nothing caught it, and the gap it leaves.** Every other ragdoll
check in `PortSelfTest` tests `CosmeticSystems` — the ragdoll's
PHYSICS, engine-independent and easy to assert. Its POSE lives in a
MonoBehaviour and was covered by NOTHING. The new
`CheckAFallingBodyDoesNotWindUp` drives the real `LateUpdate` on a
real prefab through reflection, because **asserting the arithmetic in
isolation would have stayed green** — the offset was always bounded;
it was the COMPOSITION that diverged. Assume the same hole exists for
anything else `UnitAnim` writes.

**The green tree was not a colouring bug.** `keepColors: 1` was
working correctly and faithfully drawing the tree it was handed:
**`prop_snow_pine.glb` had no snow in it.** Three materials — `bark`,
`needle`, `needle_dark` — and its needles were merely a DARKER GREEN
than `prop_pine.glb`'s. A snow pine by filename only, and the
filename is what everyone had been reading.

Rebuilt: two green tiers with white cones sitting on them, a rim of
green showing beneath each. At mid-ground scale (L7 plants it at
z -8.5) snow has to be a COLOUR BLOCK, not a dusting. Blue-white
`0.90, 0.93, 0.97`, not paper-white — the timberline backdrop is
already pale and a 1.0 white flares against it. Device-checked on L7.

**The builder is `tools/blender/build_snow_pine.py` in the RETIRED
repo**, where CLAUDE.md says builders live — there was none for the
pines, they had been authored ad hoc, which is how one shipped
misnamed. It is left UNCOMMITTED there, alongside the other builders
already sitting uncommitted on `projectile-refinement`; that repo is
not maintained. **The GLB it produces is committed here.**

### Closed 09-04 — L6's last two-rank deck, moved rather than cut

The residual above, fixed the same sitting. **The bunker and the keep
are garrisoned by the SAME unit definition**, so the fix was never a
unit-count call at all — it is two men moving four units right:

| group | deck | seats | was | now |
|---|---|---|---|---|
| `bunker` (MountainBunker) | 1.00 | 6 | 8 | **6** — 100% fill |
| `keep` (FortressTier) `83aeca…` | 3.00 | 17 | 4 | **6** |
| `keep` `036d9a…` | | | 10 | 10 |

**Enemy count stays 27. No definition, stat or new group.** That is
strictly better than the 8 -> 6 first proposed, which would have made
a boss ~7% easier to buy a legibility fix.

**All 21 campaign decks now stand in ONE rank** — the staggered path
is gone from the shipping product. So the check's floor was raised
from `body * 0.3` (0.039) to a **full body width** (0.131): adjacent
men may not overlap at all. **Run against the old level first and it
went RED** — 7 bodies hidden, tightest gap 0.094, exit 1 — then green
with the level fixed, tightest gap now 0.152 on L1. Re-introduce a
staggered rank and it goes red again, deliberately.

`LevelComposition.Report`: L6 **all eight rules ok**, unchanged, and
the campaign's three standing warnings (L3 rule 7, L5 separation
11.6, L12 rule 8) are exactly as before. `DeckFillReport`:
MountainBunker 79% -> **100%**, FortressTier 85% -> **98%**.

**Device, 09-04 APK.** Both L6 decks read: six countable men on the
bunker, sixteen on the keep, every one distinct. The before/after on
the bunker is the whole story — four merged pairs, then six men.

**WHAT THIS PLAY DID NOT ESTABLISH, and it matters.** I threw ten
near-identical blind drags and lost at turn 9 with the **Mountain
Bunker untouched at 118** — I never once aimed at it. That defeat
measures the drag, not the level. The case for neutrality is
STRUCTURAL — same count, same definitions — with one honest caveat:
the two men moved from a shorter structure at x=4 to a taller one at
x=8, so they are marginally further out, and a shell on the keep now
catches 16 where it caught 14.

### L6's BALANCE IS UNSIGNED — two defeats that do not count, 09-04

**L6 has never been played to a win, before the deck move or after.**
Two attempts, both defeats, and NEITHER is evidence about the level.
Recorded here so the third attempt does not repeat them.

**Attempt 1 — ten blind drags at 87-88%, defeat T9.** Never aimed at
the bunker; it finished untouched at 118.

**Attempt 2 — a real line, defeat T12, one unit against nine.**

| turn | shot | result |
|---|---|---|
| 1-4 | 76%, at the bunker | bunker destroyed, 27 -> 21 |
| 5 | 76% again | WASTED — empty ground where the bunker had been |
| 6-8 | 88%, at the keep | 21 -> 12, keep to 5 hp |
| 9-12 | keep falls, boss phase | 12 -> 9, mine 5 -> 1 |

**The range arithmetic is exact and worth keeping**: `range =
22.56 x power^2` (v = 9.5·power, g = 4), tank at x -9.5. So the
**bunker (x 4, 13.5 out) wants 76-77%** and the **keep (x 8, 17.5
out) wants 88%** — and a drag of 331 px on each axis reads as ~87%,
so px ≈ 331 x (power / 0.875). Confirmed on the HUD's `Last:` line
both times.

**Why neither defeat counts.** Three things a real player has that
neither attempt used:

1. **Standard ammo on all twelve volleys.** Incendiary, AP and
   Cluster sat unused in the HUD the whole battle.
2. **No consumables** — RIGS was off, so spending would have cost
   Rob's real inventory.
3. **The stepper SKIPS the picker**, so both attempts fielded the
   AUTHORED DEFAULT squad against a `deployBudget` of 14. A player
   entering L6 through the picker fields a stronger army than either
   attempt did.

**Auto cannot close this** and was declined when offered: L6 is won
or lost on STRUCTURES, and Auto targets the nearest enemy UNIT and
builds its own volley — it would never put a round on the bunker,
exactly the failure attempt 1 made by hand. An Auto win here is a
green that cannot go red.

**What the play DID establish: the beat lands.** Dropping the keep
feels like winning, and then the Sovereign walks out of the breach.
It caught me twice — once not knowing it was coming, once knowing.
`PRODUCT_DIRECTION.md` asks beat 6 for a stage boss whose level does
not end when the structure does; it delivers.

**Owed: one picker entry with ammo in play.** Not another twelve
mechanical drags.

**Also learned, and it bit mid-session: the ◀ ▶ stepper is RELATIVE,
and the app RESUMES AT THE LAST-PLAYED LEVEL.** Five ▶ taps from a
fresh launch landed on **L11, not L6**, and a live volley went into
L11 before the level indicator was read. Nothing persisted — a
stepper reload discards it — but **read `L? (?/12)` before firing,
never count taps from an assumed start.**

### Played 09-04 — L6 on the 09-02 APK. The fix holds; ONE deck still does not read

The owed beat from the 09-02 list, and it closes it. **The one-rank
fix is signed by play**: L1's outpost shows ten countable men and
**L6's FortressTier shows fourteen in a single rank**, each one
distinct — a 3.00 deck at 85% fill and the best-reading garrison in
the game. Difficulty is unchanged as predicted (no count moved):
turn 1 took the enemy 27 -> 19, turn 4 sat at 15 with all ten player
units alive, and the Fortress collapsed to rubble and flame by turn 5.
Route: L1 picker -> BEGIN -> stepper to L6, stock loadout, four real
drags at 87% / 45°.

**What the play found, and no check can see.** L6's `MountainBunker`
— the campaign's ONLY two-rank deck, and the one case the new code
path exists for — **still reads as four men when it holds eight**.
At 1:1 on the phone: four. `DeckFillReport` says 8 in 2 ranks. The
other four only resolve at 10x zoom, where each "man" turns out to
be a PAIR sharing most of a torso, two heads out of one red mass.

The numbers say why. The stagger is half a pitch — `0.094`, which is
**72% of a body width** (`0.131`) — and the rear man is also 0.16
further back, so he is drawn slightly higher and smaller with his
feet behind his neighbour's shoulder.
`CheckEveryGarrisonBodyReadsOnScreen` passes it because its floor is
`body * 0.3` = `0.039`, **4.3x more permissive than the gap it is
measuring**.

That floor is not wrong about the bug it was written for — 0.000 vs
0.094 is exactly the red-then-green the fix was proven with, and 18
of the 19 decks genuinely do read. But it asserts NOT PERFECTLY
ECLIPSED where the name on the tin says CAN BE COUNTED. **It is the
house lesson in a new costume: it tests the input (is there a gap),
not the output (can a player count the men).**

**Not fixed, and deliberately not.** The cheap fix is not code.
`MountainBunker` is a 1.00 deck seating 6 per rank; **L9 puts exactly
6 on the same structure and gets 100% fill in one rank**, while L6
asks it to carry 8. Dropping L6's bunker 8 -> 6 makes the whole
campaign single-rank and deletes the staggered path from the shipping
product. That moves a unit count on a BOSS level, so it is Rob's call
and was left alone. If the 8 is kept instead, the check's floor wants
to mean countable (a full body width, not 0.3 of one) — but that
would go RED against L6 today, so it ships WITH the level edit, never
before it.

### Closed 08-27 → 08-28 — do not reopen as taste

- **L4 shellsOverride = 3.** Played HOLD-then-ARM, defeat T12. 2 of 3
  shells missed walls — does **not** ask to bump. Leave it. Do not
  widen the aim frame. PlayerTank stays at 5.
- **L5 walk-back is on the APK** (riflemen 10→7, budget 16→13,
  packing **0.8** kept). Authored mix 7 rifle + 2 grenadier + 1
  sniper = 13/13. Tank stays off. **Played through the picker
  09-01, won 2★** — the walk-back is signed. Do not walk it
  back further.
- **Elbow kept.** All seven classes. Aiming is still the
  hold-the-gun read. Rob: *"elbow is fine, let's keep it."*
- **Last-aim HUD.** `Last: power N%    angle N°` under Your turn.
- **Rifle tracer signed.** Un-tapered flat orange dash, opaque, no
  tail. Rob: *"ok, we can use this. kind of goes with the theme...
  not super realistic, maybe mid-90s feel."* Teardrop = rocket;
  Kenney `trace_01` = glow streak; both rejected. Rockets /
  grenades / shells keep their meshes. `GAME_DESIGN_LOCKS.md`.
- **L1 tank operator.** One rider on the hull, nine on the ground,
  `deployBudget` 9. If he dies: panel `NO GUNNER`, no shell, ammo
  unspent. Loadout cannot replace him. **Other campaign tanks still
  field two** until asked.
- **Dirt deaths skid.** Stay on the dirt, slide backwards, flop
  over. No hop (that bounced), no log-roll, no Kenney `die`. Rob:
  *"ok this is fine."* A crumple clip is not the next move unless
  he asks. Deck falls unchanged.

### Ask which beat — SUPERSEDED 09-04

The 08-28 and 09-02 lists are both spent; every item on them was taken
or closed. What is owed now is at the top of this file, under **Owed**.
Kept only so a reader following a back-reference lands somewhere true.

### Closed 09-02 — half of every garrison was INVISIBLE

Picked as the next beat and it was not the bug it was reported as.
L5's bunker deck showing 4 of 8 (and L6's, and L9's) is not an
undercount, a pool fault or a render fault. Every man was built,
placed and drawn. `Formation.Mounted` laid a garrison in two ranks
of equal size, which puts **every rear man at exactly his front
man's x**, 0.16 behind him — and at the camera's real height that
is 0.024 of screen rise, 5% of a 0.48 body, behind a shoulder
0.131 wide. Two ranks of four render as four men.

**It was the whole campaign, not three levels: 85 of 178
garrisoned bodies hidden across 19 decks, every one at a column
gap of exactly 0.000.** That number is the check going red against
the old code before the fix was trusted.

**The fix.** One rank until the deck runs out — which is what the
reference measurement always said (*castle tiers pack two ranks
UNTIL THE BODIES OVERLAP*); the `while` loop that grows the rank
count IS that sentence and the old code just never let it start at
one. A genuinely-needed second rank is now STAGGERED half a pitch
so it reads between shoulders. Unfolding the ranks then exposed a
second error: the deck clamp compared CENTRES against the full
roof, so a single rank of ten hung 109% of L1's outpost and 113%
of L6's bunker. `Formation.BodyWidth` is a real constant now and a
rank is laid into `width - BodyWidth`.

**This reverses a deliberate 2026-08-02 decision** ("rank depth
flipped back to two") and the argument is written up in
`UNIT_VARIETY_DESIGN.md` Tier 2.2 part five, not just applied.
Short form: that flip was right that the clump read was a SPACING
problem, and wrong to assume a camera that can see depth. Spacing
is untouched.

**It also closes the deck-FILL question** that document has
carried since 08-02, for free and without a roster call. Campaign
median fill 34% -> 70% against a 75% target; L1's outpost 59% ->
100%, L4/L9's barracks 47% -> 97%. The garrisons were never too
small for their decks — they were folded in half. **This is not
licence to spread a row**; nothing was widened, the row was
unsplit.

**Verified.** `PortSelfTest` ALL PASS, including a new
`CheckEveryGarrisonBodyReadsOnScreen` (0 hidden over 178 on 19
decks) whose depth premise is asserted rather than commented.
`LevelComposition.Report` byte-identical before and after — the
three standing warnings (L3 rule 7, L5 separation 11.6, L12 rule
8) are unchanged and pre-existing. `DeckFillReport` shows 20 of 21
decks in one rank; the exception is L6's MountainBunker, eight men
on a 1.00 roof, staggered and countable. Device: L1's outpost deck
at turn 1 shows **ten individually countable men** where it used
to show five.

No unit, stat, count or level asset moved. Code-only —
`Formation.cs` plus the checks — so **no scene rebuild was
needed** and none was run.

### Closed 09-02 — L5's angle claim retired, the level untouched

The 09-01 design question, answered by MEASUREMENT and signed by
Rob: **the notes move, the level does not.** No code changed, no
level data changed, no rebuild — `TowerAssault.asset` designNotes
only. `PortSelfTest` **ALL PASS**.

The 09-01 play was one line through the level, so it could not
prove the angle was free — only that one angle worked. So the
ballistics were rebuilt outside the engine (g=4, v=9.5×power,
semi-implicit Euler at dt=1/60, the real structure AABBs and the
0.38 unit hit radius) and **calibrated against the twelve recorded
throws before being trusted**. At a launch x of **−8.6** it
reproduces the whole line: 78% → bunker face, 82% → deck kills,
86% → tower base once the deck is empty, 88% → collapse. Only
then was it swept.

| angle | power band that kills the deck | band that hits the tower sniper |
|---|---|---|
| 25° | 93–100% | — |
| 30° | 87–95% | — |
| 35° | 84–91% | — |
| 40° | 82–88% | 97–99% |
| **45°** | **81–87%** | 95–97% |
| 50° | 81–88% | 95–96% |
| 55° | 83–89% | 96–98% |
| 60° | 87–93% | 99–100% |
| 65° | 92–99% | — |
| 70°+ | out of range | — |

**Every angle from 25° to 65° wins the deck.** A 40-degree-wide
legal window is not a skill axis; the ~7-point power band at each
angle is. The level teaches power precision an order of magnitude
more sharply than angle, so the old line — *"the first level where
the ANGLE matters more than the power"* — was backwards, not
merely unproven.

**The cause is geometric and it generalises.** A round clears a
structure's leading edge at `tan(angle) × half-hitWidth`. The
bunker is 2.5 wide and 1.125 tall, so its half-width (1.25)
already exceeds its deck height and **45° clears the face at any
power**. Forcing a loft anywhere needs deck height GREATER than
half-width. Do not reshape L5 to get it — the 2★ balance was
walked back twice to land, and `PRODUCT_DIRECTION.md`'s chart asks
L5 for *elevation / deck fight*, never for angle. It delivers the
beat it was assigned.

**Sniper footnote, same sweep.** The tower rider IS directly
hittable, but only in a 2–3 point needle (95–97% at 45°) — the
platform box shadows him. In practice he is a COLLAPSE target,
which is what the goal text already said. That explains why the
09-01 run killed him without ever hitting him.

**Offered and NOT taken:** extending `LevelComposition` with this
shadow rule, since rule 7 reads flat range at 45° and would pass a
garrison that is a needle or unreachable. Rob took the notes-only
option. Do not build the checker unasked.

### Played 09-01 — L5 through the picker, WON 2★ (the owed beat)

Beat 1 of the 08-28 list, and it closes it. Played on the 08-28
APK, no rebuild. Route: L1 picker → BEGIN → stepper ▶ to L5 →
throwaway defeat (short shots into the dirt, T14, +16) → **RETRY**,
which is the only path that opens a level's OWN picker. `EnterLevel`
raises the picker; the ◀ ▶ stepper calls `LoadLevel` and skips it.
Stock, Standard, **no Airstrike taken** — RIGS was off, so spending
it would have cost Rob's real inventory.

**The picker reads exactly as authored: 10/10 troops, 13/13 points,
7 Rifleman + 2 Grenadier + 1 Sniper.** Both caps saturate at the
default mix — there is no room to re-compose without dropping a
body or wasting a point. That is the walk-back landing coherently.

**The line as thrown** (45° throughout, power varied):

| Turn | Power | Result |
|---|---|---|
| T1 | 78% | **wall, not deck.** Bunker 137→113, no kills |
| T2 | 82% | **deck loft — enemy 12→6.** Tower base 135→101 |
| T3 | 83% | 6→4, **first casualty 10→9** (the sniper) |
| T4 | 83% | no kills — survivors outside the box. Bunker 89→63 |
| T5 | 67% | ground trio, 4→3 |
| T6–7 | 66–67% | 3→1, player 9→8 |
| T8–10 | 86% | tower base 67→19 |
| T11 | 86% | tower base 19→**1**, player 8→7 |
| T12 | 88% | **collapse. 1→0. VICTORY 2★**, 7/10 alive, +215 |

**The three things the beat asked, answered:**

- **Ten packed bodies read as MEN, not a wall.** The Aiming frame
  shows two legible fire teams — 7 riflemen left, the bulkier 2
  grenadiers + sniper right — separated by the anchor gap
  (−7.4 / −5.9 / −4.6 at packing 0.8). The class silhouettes
  are distinguishable at 6°. The walk-back did what it was for.
- **The T6 wipe is GONE.** 08-25 at 13 bodies: bunker down T6,
  13 standing, ZERO casualties — the overshoot that caused the
  walk-back. At 10: first casualty T3, three dead by the end,
  **2★ not 3★** (*"Lost 3 of 10 — keep 8 alive for 3 stars"*).
  The level now costs something. **Do not walk it back further.**
- **The tower collapse is literally the goal text.** *"Cut the
  tower's legs — what stands on it falls with it."* The base fell
  and took the platform AND its rider: enemy 1→0 without the
  sniper ever being hit directly. Both Tower lines leave the HUD
  while Command Bunker stays at 37 — it never had to fall, because
  the win is units dead.

**The honest limit, and it is a DESIGN finding.** L5's designNotes
say *"the first level where the ANGLE matters more than the power."*
**This run never changed the angle.** Every one of the twelve
volleys was 45°; the whole level was solved by walking power
78 → 82 → 67 → 86 → 88. What the level actually teaches is a
POWER lesson with an unusually tight band: **78% hits the wall
face for 24 chip damage and 82% clears onto the deck for six
dead** — four points of power between nothing and the level.
That band is a good beat. But it is not the angle beat the notes
claim, and one of the two should move. **Ask Rob which.**

Smaller, unresolved, not chased:

- **The bunker deck shows 4 bodies where 8 are authored** — the
  same undercount recorded 08-25 for L6 (4 visible on a deck
  authored for 8) and L9. Third level, same shape. Not looked at
  with CAM this sitting.
- **The sniper died T3** having visibly delivered nothing. The
  ledge placement and `flatTrajectory` are untested by this run.
- Ballistics model that predicted every shot, for the next play:
  power% is v/9.5, g=4, so a 45° round reaches height
  `y = x − 4x²/v²`. 331 px per axis = v8. It matched the game on
  all twelve throws.

### Played 08-27 — L4 shells=3; L5 bodies walked back

Beat 1 of the owed list. Override is 3. PlayerTank stays at 5.
Played on device: stock 8 riflemen (L1 picker → stepper to L4),
Standard, HOLD T1–T4 then ARM T5–T7. **Defeat turn 12.**

- Magazine on L4 Aiming: 3 pips, `Tank shells: 3`. `[Cannon]`
  HOLD then ARM both logged. `PortSelfTest` ALL PASS.
  `BalanceAudit` L4 240 vs **288**. `DISPLAY=:1` is gone; `:0`.

**The HOLD-then-ARM line, as thrown:**

| Turn | Gun | Result |
|---|---|---|
| T1 | HOLD 62% | miss, 10 v 27, barracks 150 |
| T2 | HOLD 68% | 10→9 / 27→26 |
| T3 | HOLD 56% | miss, 9 v 26 |
| T4 | HOLD 69% | 9→8 / 26→25 |
| T5 | ARM cluster 76% | **shell missed the wall**, 150→142, 8 v 25, 2 left. Chargers on the right edge of Aiming. |
| T6 | ARM deepest 84% | melee, 8→3 / 25→13, barracks 142→138, 1 left |
| T7 | ARM deepest 83% | **shell hit**, 138→36, 3→2 / 13→12, SPENT |
| T8–11 | dry | rifle chip 36→32→30→28→24, 2→1 |
| T12 | — | **0 v 11**, barracks 22, outpost 90 |

Fail card: *"The garrison is still firing — bring the building
down."* +24 (4085→4109). Same card as 08-24 run 1 and the 08-25
five-into-dirt throwaway. Correct.

**Honest limit of this play:** 2 of 3 shells did not hit a wall
(T5 +8, T6 +4). Only T7 delivered the 96. A HOLD that then
lands all three on the barracks would still be 288 vs 240 —
this run does **not** prove 3 is too tight, and it does not
ask to bump it. The 08-24 raze-from-T1 at 3 (auto-fire, no
panel) was the coin-flip; this is the other line, thrown
crooked. Leave the override. Do not widen the aim frame.

**L5 bodies walked back, packing kept.** Riflemen 10→7 (squad
13→10), `deployBudget` 16→13, `playerSpacingScale` **stays 0.8**.
The 08-25 grouping read as men; the extra three bodies were the
T6 wipe (13 standing, zero casualties). Authored mix is 7 rifle
+ 2 grenadier + 1 sniper = 13/13. Sniper ledge/flat/miss-long
untouched. Tank stays off. `PortSelfTest` ALL PASS — ten bodies,
line 1.20 wide vs 1.49 at scale 1. `BalanceAudit` stock race
**0.7x** again (player 3.0 / enemy 4.5, HP 288). That is the
pre-ease arithmetic; packing still concentrates the volley.
**Play through the picker still owed** (see pick-up). The APK
has the walk-back; do not rebuild just to play it.

**Elbow kept.** Rob: *"elbow is fine, let's keep it."* Phase 2:
all seven GLBs have the child joints, scene rebuilt. Gameplay
Aiming is still the hold-the-gun read; close CAM showed two
hands on the rifle. Not a new silhouette at 6°, and that is
accepted.

**Last-aim on the phone.** After an 86% / 45° drag, T2 HUD
reads `Last: power 86%    angle 45°` under Your turn. Matches
the volley log. Outpost down, 14→4, shells 5→4.

### Signed 08-25 evening sitting

- **Default ARMED.** Keep it. `GAME_DESIGN_LOCKS.md` updated.
  The panel teaches the hold; flipping the default is not taste.
- **Flat sniper miss-long.** Leave it. L5 play took no hits; the
  54/60 vs 58/60 is characterisation. Per-class jitter is off
  the table unless a later play shows rounds sailing over the
  line as the problem.
- **Weapon hold is a carry.** Shipped. Elbow is a plan, not a
  LateUpdate tweak and not a clip rewrite.

### Played / looked 08-25 evening sitting — the numbers

**L4 at 5 shells.** Stock 8 riflemen via RETRY picker, Standard,
ARMED. Raze-the-buildings line. T1 barracks 150→42, T2 collapsed
(27→15), T3–4 outpost gone (15→7), **2 shells left**. Contact
turn 6 at **10 v 4**. T7 9 v 2. Then the tail: last two in the
WRECK, off-screen during Aiming. Free camera x −2 / y 4.4 / z
17.6. Deepest derived drag missed. Stopped turn 11 at 9 v 2.
**Readability, not a miss-fest. Do not widen the aim frame.**
Throwaway (stepper leftover 10, dirt, ARMED) died turn 8 on
*"The garrison is still firing"* +24. Five into the dirt is the
same dead end as three. Contact union holds player line +
chargers, tank in; next Aiming beat the chargers sit on the
right edge.

**L5 eased race.** Picker 13 (10 rifle + 2 grenadier + 1 sniper,
16/16), Standard, no tank. Packed line reads as men. Bunker
down T6, **13 standing, zero casualties**, then 13 v 3. Leftover
street MG off-screen during Aiming (same family as L4 wreck).
Sniper on the ledge is visible. Overshot.

**MountainBunker.** Handover said L6/L8; **L8 does not field
one.** Campaign: L6 x 4 (8 authored) and L9 x 4.5 (6 authored).
Both at y 1.20: garrison on the player-facing lip, not under
the roof. L6 A/B x 5.11 z 9.75: four red on the front edge at
y 1.20, same four from y 8.85. **Honest count: 4 visible on a
deck authored for 8** (`standWidth` 1). Overhead the rest of
the roof is empty, so the other four are packed into that same
lip, not hidden by it. Offset +0.33 holds. No model change.

**08-07 L9/L12 siege audit, re-run at 5 shells.** Headless
`BalanceAudit.Report`: 0 errors, 21 warnings. Stock siege **ok**
on every tanked campaign level (L9 229 vs 480, L12 280 vs 480).
Only remaining SIEGE DEFICIT is L5 (no tank). Device: L9 both
structures down by turn 4, 2 shells left; L12 both fortress
tiers gone by turn 6, shells spent, 9 v 10. **Neither is
unclearable at five.** Caveat: L9/L12 were played with L5's
carried 13, not each level's stock — the shells are the siege,
and stock L12 fields rockets besides.

### Tree

**Ask git** — but as of 09-01 the working tree is **CLEAN**, which
is a correction: the 08-28 handover described it as dirty from
08-25 through 08-28. That work is committed as `c9b8d40`
(*"L4 at three shells, L5 walked back, mid-90s dash"*). Three
commits sit **UNPUSHED** on `session/2026-08-25-shell-art-ragdoll`:
`c9b8d40`, `8e6ba38`, `4d61ffc`. Untracked and not wired: Kenney
`Particles/` + `TracerSprite.mat`. Do not push, commit or tidy
unless he asks.

**This file is 3000+ lines.** 08-05/06 is in
`HANDOVER_ARCHIVE.md`. 08-13 → 08-21 is closed history and
the obvious next split — **ask before archiving**.

### What the 08-25 CODE sitting shipped — the index

Every one has a block further down with the numbers and the traps (5
and 6 share one). **The blocks are in REVERSE order below** — newest
first, with the L4 play at the top because it is the oldest debt and
the one that started that sitting. This list is only so a new session
can find them.

1. **L4 played honestly, twice** (the 08-24 debt). Both defeats, and
   they fail differently. Found: **the tank shells ARE the demolition
   budget, and the level's own goal text spends them.**
2. **The shell is ARMED or NOT ARMED, and the tank carries 5** —
   `shellsOverride` per level, and a magazine panel pinned UNDER THE
   TANK that exists only during the aim.
3. **L5 eased** — 13 bodies in a line packed by the new
   `playerSpacingScale` (0.8), sniper moved to the ledge and given
   `flatTrajectory` so it shoots a direct 12-20 degree line.
4. **"The units are too dark" was a BACKWARDS KEY LIGHT** — every
   camera-facing surface was lit by AMBIENT ALONE. How long that had
   been true is not established; the same stale angle was sitting in
   five other scene builders and previews. Gear and skin lifted after.
5. **The soldiers have faces** — the helmet brim was burying a nose,
   jaw and ears that were already modelled. Blender, crown untouched.
6. **They hold the rifle with BOTH HANDS** — arm correction plus moving
   the weapon inboard; neither works without the other.
7. **Ground deaths are knocked BACK** — they used to be thrown toward
   the enemy, into the fire that killed them.
8. **Garrisons were hidden by their own roof**, not by a barrier —
   `deckStandZOffset` on five structures.
9. **The corpse "glitch" near the ground was the settle spring
   snapping** — capped at `FlopMaxSettleSpeed` 120.

**Two new instruments, both kept:** `UnitPosePreview.Shots` (renders
the shipped prefab sampling the shipped clip — ~2 min against ~8 for a
device round trip) and `RagdollProbe.Run` (prints a dying body's own
numbers per tick). Each one caught a defect in its first run that the
code read as innocent, and each caught MY OWN mistakes too — read their
docstrings before reusing them.

**L4 WAS PLAYED — 08-24. The debt is paid; read what it found.**
Fresh build, fresh install, stock 8 riflemen, real drags, no Auto and
nothing fired into the dirt. TWO full runs, both DEFEATS, and the two
losses are not the same loss:

- **Run 1 — volley chases the charge. Blowout.** Aimed every volley at
  the advancing shield bearers, which is what `levelGoal` tells you to
  do. Dead on turn 9, having killed **5 of 27**. Fail card was right and
  read well: *"The garrison is still firing — bring the building down."*
  +24, and the coin line moved 4045 -> 4069.
- **Run 2 — volley razes the buildings. Lost on the last man.** Same
  squad, aim on the cluster instead. Barracks 150 -> 40 on turn 1,
  **COLLAPSED on turn 2 taking all 12 of its garrison** (enemy 27 -> 15)
  with zero losses to me. Checkpoint down by turn 16 (-7 more). Died on
  turn 30 at **0 v 1**.

**So: L4 is winnable-SHAPED and I did not win it.** Nothing here says
it is unwinnable; it says a stock squad playing the intended line ends
in a coin-flip on the last body. That is a verdict for Rob, not for me.

**THE FINDING — the tank shells ARE the level, and the level's own goal
text spends them for you.** 3 shells x 96 = 288 against 240 of
garrisoned structure HP, so the shells are the ENTIRE demolition budget
(`BalanceAudit`: *"siege ok"*). A rifleman does **8 x 0.25 = 2** to a
wall; 8 of them need ~10 clean volleys for the barracks alone while
dying at 1-2 a turn. The shell follows the volley's landing point — so
aim at the chargers on turns 1-3, exactly as *"Break the assault before
it reaches your line"* instructs, and all three shells go into the dirt.
**After that the level cannot be won by anyone, and nothing tells the
player.** Run 1 is that state, reached by obeying the goal text. Either
the goal line or the shell wants a look; **ask Rob which.**

**Also seen, unasked:** turns 16-30 of run 2 were a **14-turn 1 v 1
tail** with neither side landing a hit — ~10 volleys including a full
power sweep 200 -> 360 px, and the last enemy was never located. The
free camera was opened for it and the battle ended first. Nothing is
proven broken here; it is a pacing/readability smell at the end of a
level, and it is unmeasured.

**The 08-21/24 arc holds up in play.** The armour reads exactly as
costed: a full 10-round volley into the shield wall killed **exactly
one** (7 rounds-to-kill), and all 5 chargers reached the line and took
5 of my 10 with them — the guaranteed mutual trade, on schedule
(`BalanceAudit` predicted contact at 5.2 turns; it came on turn 6).
**Not yet looked at with eyes: the contact framing and the run gait in
a REAL play.** Both are signed, both were seen in the HUD, neither was
recorded. That is the one piece of this still owed.

**`BalanceAudit` called this level before the phone did.** It flags L4
*"2.7x BEHIND the race — drag this one"* and *"melee arrives before the
field can be cleared"*. Both landed. **`BalanceAudit.Drags` prints the
aimed swipe per level** — L4 cluster is
`adb shell input swipe 540 1150 242 1448 400`, deepest is
`540 1150 210 1480 400`. Use those instead of deriving a drag by hand;
mine agreed to within 5 px and cost an hour.

**Two things the file was wrong about, settled on the device:**

- **Uninstall/reinstall did NOT wipe the coins.** Android auto-backup
  restored them — the balance was 4045 before and after. The re-earn
  tax the RIGS note is costed against may not be real on this phone.
  RIGS is still the right tool; the reason for it is weaker.
- **RETRY returns to the LOADOUT screen, not the battle.** Three drags
  fired straight into the +/- steppers and silently rebuilt the squad
  as 6 riflemen + 2 grenadiers. Re-screenshot the loadout after every
  RETRY before firing.

**L4 gives a 14-point deploy budget and the default squad spends 8.**
Six points sit unspent on the screen the player is looking at. Not
touched, not judged — but the "stock" the balance audit measures is not
the strongest legal squad by a wide margin.

**THE CORPSE "GLITCH" NEAR THE GROUND WAS THE SETTLE SPRING SNAPPING**
(08-25, Rob: *"when the unit falls off of a building, when they are
at/near the ground, they seem to start glitching/going into some kind
of animation loop before they finally disappear into the ground."*)

**A corpse plays NO CLIP** — `UnitAnim.Set(Die)` stops every one of
them and its `dead` guard means it cannot re-trigger — so whatever
moves is the SIMULATION, and that is printable. `RagdollProbe.Run`
(new, kept) dumps a real deck-fall body's state per tick.

**The cause, measured:** a landed body ROLLS while it slides
(`ShouldRoll`/`StepRoll`), so it hands over to `StepFlopToSide` at an
ARBITRARY angle — up to 90 degrees from the nearest side-lie. At
`FlopSpring` 140 the spring crossed that in about an eighth of a second
and REVERSED on the way: **+57 deg/s of roll became -158 deg/s**, then
-231 in a later sample. That whip is the "glitch".

**Fixed with a speed ceiling, `FlopMaxSettleSpeed = 120`**, so a large
error eases over ~0.3s instead of snapping. The spring is not the wrong
model and its constants are right for the small errors a DIRT death
hands it (a ~20 degree lean); small errors never reach the cap, so the
tip-over is untouched. Measured peak 231 -> 120, still ending flat at
-90.

**Two fixes tried FIRST and reverted, because the probe said they
missed** — worth knowing so nobody retries them: dropping
`RollMinSpeed` to bleed the roll off (it made the handover error
WORSE, 32 -> 49 degrees) and bleeding the inherited opposing spin (the
spring's own error term dominates it). **The velocity was never the
problem; the spring's stiffness against a big error was.**

The new check drives a REAL rolling handover and asserts the fastest
turn the settle applies — the thing on screen — not the constant. Seen
red without the clamp (148 in that harness). **Its message names both
numbers**, because the 231 came from the full sim and the harness peaks
lower; a failure message that misstates its own figures is worse than
none.

**Device: a volley onto L4's barracks with the free camera parked** —
bodies thrown off the roof, arcing, landing in front, lying still. At
8fps a 0.3s settle is ~2 frames, so the device evidence SUPPORTS the
fix rather than proving it; the measurement is what proves it.

**GARRISONS WERE HIDDEN BY THEIR OWN ROOF — NOT BY A BARRIER**
(08-25, Rob on L2: *"you can't see them - they're behind a barrier or
something. let's make the barrier smaller... modify the model if
needed. check other levels for this as well."*)

**No model was changed and none needed to be.** The garrison stood
MID-DECK, and the camera sits at y 1.2 against a 2.5 deck — you look UP
at the building, so its own roof mass hides anyone standing back from
the edge. Proved with two frames at an IDENTICAL free-camera position
(x 6.1, z 8.35): at **y 1.20 the deck reads empty**, at **y 5.06 all
twelve men are there**.

Fixed with `deckStandZOffset`, which already existed for this and was 0
on every affected structure while the FortressTiers had -0.19/-0.80.
**+z is the camera side** — the builders put the front deck lip at
glTF +z and the rear cupola at -z — so positive moves the row FORWARD.
Set from real deck extents with a 0.30 margin: **GarrisonPost +0.955,
WatchTower +0.48, BarracksBlock +0.355, TowerPlatform +0.355,
MountainBunker +0.33.**

**MY FIRST SURVEY WAS WRONG AND THIS FILE'S OWN NEIGHBOUR SAID WHY.**
Ranking structures by "how far geometry rises above the deck", measured
off per-mesh BOUNDING BOXES, called the Outpost the worst offender at
3.4x body height. That number was its ROOF CUPOLA. For GarrisonPost the
tall mesh was `accent_GarrisonPost` — a JOINED mesh whose top is the
REAR cupola, nowhere near in front of anyone.
`StructureDefinitionSO.deckY`'s comment already says it: *"NEVER off a
node's bounding box. A bbox top is as likely to be a chimney, a guard
rail, a cupola or a damage chunk; that mistake stood four of five
garrisons in mid-air."* **Use `tools/measure_decks.py`, then LOOK.**

**The Outpost was left at 0 and verified fine** — its deck runs
REARWARD (model z -0.624..+0.176), so the row already sits within 0.32
of the front edge and there is nowhere forward to move it. The level
everyone plays first was never broken.

**Device-verified garrison visible: L1, L2 (the report, matched A/B),
L3, L4, L5. `MountainBunker` (L6/L8) is measured the same way but was
NOT looked at** — check it before assuming.

**Blender was NOT used and is still not connected** (`get_addon_status`
-> `source: "missing"`). The CLI at `~/blender/blender-5.1.2-linux-x64`
works if a model ever does need cutting.

**GROUND DEATHS ARE KNOCKED BACK NOW, NOT TIPPED OVER** (08-25, Rob:
*"when a unit is on the ground and they die, they just tip over. let's
make them get blown back but not as dramatic as the one falling off the
building."*)

**And the old one went the WRONG WAY.** `CosmeticSystems.ImpulseFor`'s
non-tumble branch threw at **`-sign`** — i.e. TOWARD the enemy — on
reasoning about shoving bodies off a building, in a comment that no
longer described that branch at all, since anything standing on a
structure takes the tumble path. A man shot on the dirt fell forwards,
into the fire that killed him.

Now `sign *`, the same "still backwards" convention the deck tumble
uses, with the magnitudes deliberately BETWEEN the old tip-over and the
deck fall — `RagdollKnockVx` 0.75-1.35 (tip-over was 0.35-0.80, deck is
0.90-2.10), `Vy` 0.30-0.70 (was 0.05-0.20, deck 1.60-3.40), spin
100-150 (was 70-110, deck 120-200). **No yaw or tilt spin**: the 3-axis
cartwheel is most of what makes a deck fall dramatic and is exactly the
half not wanted. At `RollFrictionPerTick` the throw carries about
`Vx / 2.3`, so a body slides ~0.3-0.6 units — a few body widths.

**Vy above `RagdollAirborneVy` (0.4) is SAFE here**, which is not
obvious: the flail is gated on `d.Tumble && RagdollAirborne(d)`, and a
dirt death is never `Tumble`, so it cannot reach the dramatic airborne
draw however fast it leaves.

**A PRE-EXISTING CHECK ASSERTED THE OLD, WRONG DIRECTION** — `dirt.Vx <
0f`, i.e. "dirt tips AWAY from the building". It was RETARGETED, not
deleted: it still guards the things that remain true (a dirt death is
flatter than a deck fall and does not cartwheel) and now demands the
same throw direction as the deck fall. **When a behaviour change turns
a test red, read the test before touching the code — this one was
encoding the bug.**

The new check asserts the DISPLACEMENT a real ragdoll step produces,
both sides, because the sign is mirrored per side and a one-sided test
passes on a bug that flips only the other. Seen red against the old
direction: *"player 0.53, enemy -0.53 (want opposite signs, player
negative)"*. Confirmed on device with the FREE CAMERA PARKED — it holds
through a volley, so the body's travel is unambiguous instead of being
chased across a panning frame.

**"THE UNITS ARE TOO DARK" WAS A BACKWARDS KEY LIGHT, NOT ART**
(08-25, Rob: *"i feel like the units are too dark... can we make them
more humanlike? use the blender mcp for this"*)

**It was never the albedo and Blender could not have fixed it.** The
camera sits at +Z looking toward -Z and every unit faces glTF +Z, so a
camera-facing surface has normal +Z. `SpikeSceneBattle` built the key
light at `Euler(50, -30)`, whose forward is **(-0.32, -0.77, +0.56)** —
a POSITIVE z, travelling from behind the army toward the lens. N.L on
every camera-facing face was -0.56, clamped to zero. **The whole army,
and the front of every structure, was lit by AMBIENT ALONE.**

**The tell is in any pre-fix screenshot and costs nothing to check:** a
bunker's horizontal TOP is bright while its vertical camera-facing
FRONT is nearly black — same material, so it can only be direction.
Fixed by yaw -30 -> **210**, which keeps the 50-degree pitch, mirrors z
to (-0.32, -0.77, -0.56), and leaves the GROUND untouched (normal +Y,
N.L unchanged at 0.77). Verified as a matched A/B on device.

**Colour edits in Blender would have been DISCARDED.** The GLB supplies
geometry and MESH NAMES only; the tones are Unity material assets that
`RiggedUnits.Tone` assigns by `skin*`/`accent*`/`trim*` prefix and
`FactionPaint` repaints per faction at runtime. Anything painted in
Blender is overwritten before it is ever seen. **Unit colour lives in
five places, none of them the model:** `PlayerUniform.mat`,
`PlayerGear.mat`, `UnitSkin.mat`, the faction assets
(`Redguard`/`IroncladLegion`, enemy only — `ApplyFaction` never touches
the player) and `Cosmetics`' camo entries (player only; Olive is null
and falls back to the build-time material).

**Second pass, after the light:** gear was the black mass — helmet,
webbing and boots all sat at ~0.17 and merged with the body into one
lump, which is the failure `UNIT_VARIETY_DESIGN.md` has already
recorded once ("the jaw merged with the collar into one dark mass").
PlayerGear 0.17/0.19/0.16 -> **0.27/0.29/0.25**, EnemyGear and
Redguard's gearColor -> **0.31/0.23/0.21**, Ironclad's -> 0.24/0.28/
0.34, UnitSkin 0.76/0.56/0.42 -> **0.85/0.65/0.50**. Hands now read as
flesh on the rifle and the helmet separates from the head.

**THE FACE AND THE HOLD ARE DONE (08-25) — see UNIT_VARIETY_DESIGN.md
Attempt 8 for the full account.** Helmet brim raised to HEAD_C + 0.02
in `build_units_rigged.py` (height 0.38 -> 0.27, CROWN UNMOVED — the
exported bbox is identical, which is the check that matters because
Normalize scales by the tallest point). Only `unit_rifleman_rigged.glb`
was copied across; the builder rebuilds all seven and the other six
were left alone. Hold corrected in `UnitAnim.LateUpdate` — left arm 35
inward / 4 down, right 5 / 4.

**`UnitPosePreview` is the new instrument** and it earned itself back
three times over: `-executeMethod UnitPosePreview.Shots` renders the
shipped prefab sampling the shipped clip in ~2 minutes against ~8 for a
device round trip. **Its first frame of any batchmode session renders
UNLIT**, and the control is always first — so the control came out
black beside a lit candidate, which reads as the pose change having
fixed the colour. There is a discarded warm-up frame now. It also
applies `ReadyDrop` before judging, because the runtime always has one.

**THE FIRST POSE ATTEMPT WAS WRONG IN THE SIGN and reached the phone.**
Rob: *"still see the same - one arm out, other arm holds weapon."* The
"inward" yaw swung the left hand OUTWARD — measured, x went -0.421 ->
-0.979 while the rifle sat at +0.625, so the gap GREW from 0.84 to
1.51. It survived a look at a 3/4 render because an arm swinging
outward reads as "crossing" from that angle. **Two arms and a weapon
are three lateral positions: judge them as NUMBERS.** `UnitPosePreview`
prints all three now (`[PoseMeasure]`), and that is what caught it —
after a first version measured the mesh NODES, which sit on the joints,
and reported both hands unchanged at +/-0.421 for every candidate.

**No arm angle alone could ever have fixed it.** The rifle hung
outboard of the right shoulder and one bone per arm reaches at most an
arm's length across. The weapon had to come inboard too:
`AttachGun` x 0.10 -> **-0.15**, paired with left 45 / right 6. The two
only work as a PAIR. Shipped state measures left hand 0.138, gun 0.268,
right hand 0.338 — the weapon between the hands, confirmed on device.

**Honest limit:** one bone per arm, no elbow, so this is not a firing
stance — the forward hand sits at the receiver, not out on the
forestock, because 45 degrees of yaw also costs 30% of the arm's
forward reach. Going further needs an elbow, which invalidates every
clip. **Ask first.**

**Superseded — kept because the reasoning is still the map:** The soldiers have
no visible face, and the reason is not that one was never modelled:
`build_units_rigged.py`'s rifleman head already carries a skin sphere,
a NOSE box, a jaw, two ears and a neck cylinder. They are **buried
under the ACH pot** — `cylinder(head_r * 1.18, 0.38, (0, 0.01,
HEAD_C + 0.10))`, wider than the head and reaching down to
HEAD_C - 0.09, i.e. below the sphere's centre. The fix is to raise the
BRIM without lowering the CROWN (keep the top at HEAD_C + 0.29, cut the
height to ~0.27, recentre to ~HEAD_C + 0.155).

**THE TRAP IN THAT FIX, written down before anyone attempts it:** the
helmet's TOP is load-bearing. Its own comment says *"Must overshoot to
~2.97 ... AttachGun is in model units and Normalize scales by the
tallest point"* — so shortening the pot from the top moves the rifle to
the crown and rescales the whole figure. `PortSelfTest` also samples
the walk clip and asserts model height ~2.70, hip swing and foot carry,
so a re-export that moves the head goes red. Raise the brim only.

**Blender MCP is NOT connected** — `get_addon_status` returns
`source: "missing"`, no version. It needs `uvx blender-mcp install-addon`
and Blender running with the addon started. **The CLI works without any
of that** (`~/blender/blender-5.1.2-linux-x64/blender` 5.1.2), and the
builders are headless scripts, so the face pass can be driven straight
from the command line if the MCP stays down.

**L5 EASED — MORE MEN, TIGHTER LINE, AND A SNIPER THAT SHOOTS FLAT**
(08-25, Rob: *"on level 5, we should group player units together more
closely, add a few more player units.... plays difficult. also, can we
move the enemy sniper closer to the ledge and make their shot more on a
direct line instead of an arc."*)

- **Squad 10 -> 13** (riflemen 7 -> 10) and **deployBudget 13 -> 16**.
  The budget is not optional bookkeeping: at 13 points for 13 units the
  picker would have silently truncated the squad and the level would
  have fielded the old ten with a bigger number sitting in the asset.
- **THE AUTHORED ANCHORS ARE NOT THE PLAYER'S LINE.** This is the trap
  in this ask and it would have eaten a whole session. `playerGroups`'
  `anchorX` values are thrown away the moment a squad is PICKED:
  `Loadout.ToPlayerGroups` tiles the chosen units on its own uniform
  pitch centred on `GroundAnchorX`, and `Formation.Clustered` spaces
  each group from `DefaultColumnSpacing`. The authored anchors are read
  only when nothing runs the picker — i.e. the **◀ ▶ stepper**. Edit
  them and you have tuned the debug path and moved nothing the player
  will ever see.
- So the knob is new and it is a SPACING SCALE:
  **`LevelDefinitionSO.playerSpacingScale`**, 1 everywhere, **0.8 on
  L5**. It multiplies both the tiling pitch and the ground cluster (not
  garrison rows — those are clamped to a deck). Built line is now
  **1.15 wide holding 13**, against 1.44 holding 10.
- **It is a DIFFICULTY change, not dressing.** Every unit fires with
  ONE shared launch velocity from its OWN x, so the volley's beaten
  zone is about as wide as the line that threw it. Tightening the line
  puts more of the same volley onto the same men. Race went
  **0.7x -> 0.4x** (player 3.0 -> 2.4 clean volleys, enemy 4.5 -> 6.0
  as player HP went 288 -> 384).
- **Do not take the scale far below ~0.75.** `Formation.Clustered`
  packs within a group at 0.62x of it and a body is ~0.131 wide. At 0.8
  L5 is already the tightest line in the campaign — the co-location
  check reports **0.149** there, against 0.158 on L1 before this.
- **The sniper moved to the ledge**, anchorX 10 -> 9.4 (the deck spans
  9.25..10.75), and gained **`UnitDefinitionSO.flatTrajectory`**: a
  12-20 degree direct shot in place of `EnemyAI`'s 35-60 lob. It is a
  per-CLASS trait, not a per-level tweak, because it is
  characterisation — any sniper anywhere should shoot like this. Flat
  shooters also get their own **`MaxFlatLaunchSpeed` 16** (the lobbing
  cap is 12): covering the same ground on a shallow arc costs more
  speed, and at 12 the solve was being clamped and pitching rounds into
  the dirt short of the line.

**Two things measured that are worth knowing before tuning it further:**

- **A flat shooter's misses go LONG, not short.** The +/-2 aim jitter is
  applied to the target POINT including its HEIGHT, and a shallow
  trajectory travels a long way horizontally while dropping 2 units. On
  L5 the sniper puts **54 of 60** on the line where the lobbed version
  put 58 — so the direct shot is slightly LESS accurate, by mechanism,
  not by tuning. Fixing that means giving snipers their own jitter,
  which is an accuracy change nobody has asked for. **Ask first.**
- **The 11.6-separation warning on L5 is OLDER than this work** and is
  unchanged by it — it reads the AUTHORED front rank (-4.6), which is
  the stepper's line, not the player's.

**Both new checks were seen RED first**, each naming its defect:
*"the built line is 1.44 wide against 1.44 at scale 1"* and
*"steepest 59.8"*. The sniper check is **SEEDED** (`Random.InitState`,
state saved and restored): `AimAt` rolls off `UnityEngine.Random`, and
unseeded the check passed four runs and then reported a short round on
the fifth. **A check that only sometimes goes red is not a check.**

**ALL OF IT IS DEVICE-VERIFIED (08-25).** 13 units in a visibly packed
line that still reads as men rather than a pile; the sniper standing on
the platform's PLAYER-FACING edge; and the flat shot confirmed as a
CONTROL SHOT — one frame, during the enemy windup with the free camera
parked between the two, showing the four bunker riflemen with rifles
angled UP to lob while the tower sniper's sits level. The windup poses
each rifle at the angle it will fire (`EnemyAimDegrees`), so the pose
IS the shot, and the lobbers in the same frame are the control.

**TWO TRAPS THIS COST, both about how you REACH a level:**

1. **The ◀ ▶ stepper carries the PREVIOUS level's picked squad.**
   `LoadLevel` passes `playerGroupsOverride: loadoutGroups` and the
   stepper never clears that field — only `EnterLevel` (the picker
   path) rewrites it. Stepping L1 -> L5 fielded L1's 8 riflemen plus
   its 2 TANK CREW, and since L5 has no `player_tank` the crew fell to
   the ground as ordinary bodies: **"Your units: 10" on a level
   authored for 13**, which reads exactly like the data change having
   failed. It had not. `LoadLevel`'s own comment says the stepper
   "carries nothing" — that is about CONSUMABLES; the squad is carried.
   **To see a level as a player sees it, go through the picker**: win
   into it, or take a defeat and press RETRY.
2. **A locked roster hides a budget overrun in the self-test.**
   `ProgressStore.ResetAll()` leaves the grenadier and sniper LOCKED,
   so `Loadout.Default` substitutes cheap riflemen and the squad prices
   at 13 — while the real unlocked mix costs **16 against a budget of
   exactly 16**. The device said "16/16 points" where the test said 13.
   The check now prices the AUTHORED mix unlock-independently, because
   the failure it guards is silent: the picker just fields fewer men
   and the level looks authored-but-easier. **L5 has ZERO points of
   headroom — adding any unit there needs `deployBudget` raised too.**

**THE SHELL IS NOW ARMED OR NOT ARMED, AND THE TANK CARRIES FIVE**
(08-24, Rob's ask, straight out of the L4 finding above). The mechanic
is in `GAME_DESIGN_LOCKS.md`; what a new session needs:

- `CannonArmed` was ALREADY in `GameState`, already gating
  `BattleTick.CannonShells`, already self-tested — defaulting true with
  **nothing on earth able to set it**. Only the UI had been dropped in
  the port. The fix was a control, not a mechanic.
- **5 shells**, on `PlayerTank.cannon.ammoPerBattle` (was 3). A level
  may override per placement — `shellsOverride` + `hasShellsOverride`,
  same shape as `standWidth`/`hasStandWidth`, and zero is a legal
  override ("this level's tank has a cold gun") which is why it is a
  HAS flag and not a -1 sentinel. `LevelBuilder` reads it from the
  PLACEMENTS now, not the built entities.
- **The panel is a magazine, not a button, and it is PINNED TO THE
  TANK** — world-anchored under the hull's base, not parked in a HUD
  corner. Rob: *"should not follow across the screen — it should be
  selectable during the player aim... and it should be underneath the
  tank as well."* A screen-anchored panel rode the camera through the
  whole volley and the resolve and read as permanent furniture rather
  than as a property of the gun. It exists ONLY during the player's
  aiming phase and is gone the instant the volley leaves.
  Armed is a THICK GOLD box reading `ARMED` with the next round capped
  white; not-armed is a thin grey outline reading `NOT ARMED` (his
  wording — an earlier pass said `FIRES THIS VOLLEY`/`HOLDING`, which
  is noise once the panel only exists during the aim). The border
  WEIGHT difference is deliberate: colour alone does not carry on a HUD
  looked at in a hurry.
  - It is CLAMPED to the screen, which can pull it off true centre
    under the tank. An unreachable control is worse than an off-centre
    one, and the tank sits at the end of the line the aim frame crops
    tightest.
- **The state PERSISTS across turns** and does NOT re-arm per volley.
  Arming spends nothing, so auto-disarm would tax the player who wants
  to shell straight through.
- **Default is ARMED. Signed 2026-08-25.** Do not flip it as taste.

**IT BROKE THE WHOLE TOP BAR ONCE — the trap is worth more than the
feature.** Rob, from the phone: *"now i can't switch levels and none of
the top buttons work."* RIGS, CAM and the ◀ ▶ stepper all DREW
correctly and not one of them answered a tap.

**IMGUI hands out control IDs BY CALL ORDER and matches them across
passes BY POSITION IN THE SEQUENCE.** OnGUI runs several times per
frame — Layout, Repaint, then one pass per input event. A control that
exists in one pass and not the next shifts the ID of everything
declared AFTER it, and those controls' events go to the wrong place or
nowhere. `DrawLevelNav()` is called immediately after
`DrawShellToggle()`, so the entire top bar sat downstream of the fault.

Two things in the panel were unstable within a single frame, and both
had to be fixed:
- **Its rect is anchored to the tank through `cam.WorldToScreenPoint`**,
  so it MOVED between passes and vanished when the phase flipped. It is
  computed ONCE now, in `Update` after `ApplyCamera`, and cached in
  `shellPanelRect`; every OnGUI pass in that frame reads the same rect.
- **Its `GUI.Button` was called conditionally** (`if (canUse && GUI.Button(...))`
  — C# short-circuits, so the control was not declared at all when
  `canUse` was false, and `canUse` reads `dragging`). It is declared
  UNCONDITIONALLY now, parked off-screen at zero size when there is no
  panel, with `GUI.enabled` doing the gating. `DrawConsumables` had
  this right all along and is the pattern to copy.

**The rule, for anything added to this HUD: the NUMBER of IMGUI
controls a frame declares must not depend on anything that can change
between passes** — camera position, drag state, turn phase. Draw calls
(`GUI.Label`, `GUI.DrawTexture`) allocate no IDs and may be hidden
freely; `GUI.Button` and friends may not.

**Verified on device, as OUTPUT, not as a handler running.** Same drag
twice on L1: **HELD leaves the count at 5/5** and the outpost merely
takes infantry chip damage (90 -> 58); **ARMED spends 5 -> 4 and
collapses the outpost outright**, enemy 14 -> 4. That is the whole
mechanic in two shots. `[Cannon] armed=False, shells 5/5` in logcat
confirms the tap. The PLACEMENT was verified the same way: the panel
sits under the hull on the player's turn, is **absent from the mid-
volley frame** while the camera rides the shot downrange, and is back
under the tank next turn still reading `NOT ARMED` at 5/5 — so the
choice persists and an unarmed volley really does spend nothing.

**The new self-test was seen RED against the old builder first**, and
it named the defect: *"same level with a placement override of 2 built
5"*. It asserts the BUILT STATE, never the asset field — the total is
summed through a side/cannon filter, so a filter that dropped the tank
would leave `ammoPerBattle: 5` in the asset while the battle started
with nothing. The override half runs on a `Instantiate` CLONE so a test
cannot dirty a real campaign level.

**Balance moved and it is not nothing: siege capacity 288 -> 480**
across every level with a tank. `BalanceAudit` still reports 0 errors
and the same 21 warnings, and the only remaining SIEGE DEFICIT is L5,
which has no tank at all and is therefore untouched by shell count.
**Nobody has PLAYED a level at 5 shells yet** — L1 was fired twice to
prove the toggle, not to judge difficulty. Every level just got a
materially bigger demolition budget; **L4 in particular now has 480
against 240 of garrisoned HP and may well be too easy.** That is the
next thing to feel, and `shellsOverride` is the knob for it.

**The control-ID fix IS device-verified (08-24).** On the fixed build:
◀ ▶ walks L1 -> L2 -> L3 -> L2; CAM raises the free-camera pad and its
x/y/z readout; RIGS reads `RIGS ON` and the reachable count goes
`L2 (2/12)` -> `L2 (2/29)`. The check that actually matters is the last
one — **tap the shell panel and then immediately tap ▶** — because
interacting with the panel is what shifted the ID stream. Toggled to
`NOT ARMED`, then the stepper answered on the next tap and moved
L2 -> L3. That is the regression path, walked.

`PortSelfTest` green and `BalanceAudit` clean at the time this landed.
**Tree state for the whole session is at the top of the file** — it is
not repeated per item, because six copies of it is six things to go
stale.

**The L4 arc (08-21 → 08-24).** Three asks, one level. Read all three
before touching it; the second and third exist because of the first.

1. **The enemy shield bearer had no armour at all.**
   `EnemyShieldBearer.asset` was missing `damageTakenMultiplier`
   entirely while the player's carried 0.5 — so the class whose whole
   mechanic IS armour, and the one that actually CHARGES, was a bare
   40 hp body a converged volley wiped on approach. Rob: *"the melee
   force should not die immediately."* Same family as the machine
   gunner's burst: **a signature living on one side's asset only.**
   Set to 0.5, then **retuned to 0.75 on 08-24** — Rob: *"should not
   have double hp but just a bit more than they originally had."* A
   rifle round does 6 instead of 8: **7 rounds to kill against 5
   bare**, up 40% rather than 100%.

2. **The contact frame was 70% wider than its own engagement.**
   `ContactHalfWidthMin = 4f` on a fight that wants ±2.36. That 4 was
   never geometry — it was paying for SPRING LAG, and a fixed lag
   needs a fixed addition, not a minimum: a floor over-pays a small
   engagement and is swallowed by a large one. Floor 2.5, union
   carries `ContactSpringMargin = 0.7`; L4 goes ±4.00 → ±3.06 with the
   tank rear still held by 0.61. The signed-off UNION is untouched.
   See `CAMERA_ARCHITECTURE.md`. **Both existing camera checks only
   asked whether the frame HOLDS the force**, which any frame big
   enough passes — ±4.00 stood on a ±2.36 fight under two green tests.
   There is a ceiling now as well as a floor.

3. **The charge is a RUN.** Rob: *"the leg movements are too dramatic
   — can we make it look more like a run?"* It played Kenney's `walk`
   raw. **Measured, that clip is not a run and never was: ±60° at the
   hip (120° of scissor) at 3 steps a second** — a sprinter's
   amplitude on a stroller's cadence, exactly backwards.
   `UnitAnim.ChargeStride = 0.75` puts the hip at ±45° (measured 44.9
   on the rendered rig against 59.9), and the cadence is **DERIVED**,
   not a second constant: `GaitSpeed` solves the clip speed that makes
   the feet carry the body. Charge lands on the `MaxGaitSpeed` clamp
   at x1.70 = 5.1 steps/s.
   - **The clamp, and the skate it leaves, are deliberate.** Matching
     2.4 u/s outright wants 7.9 steps/s, a blur. It is affordable only
     because the camera FOLLOWS the charge: with no still ground to
     measure against, amplitude and cadence are what the eye reads.
   - **It fixed a live bug nobody had filed** — a wire-slowed charger
     (`WireSlowFactor` 0.35) crawled at 0.84 u/s while windmilling at
     full rate. `AdvanceSystems.MarchSpeed` was pulled out of `March`
     so the renderer asks the same question instead of keeping a
     second copy of the wire test.
   - **`AdvanceSpeed` 2.4 was NOT touched** — signed off 08-13,
     *"the march is fine."* Slowing the ground to ~1.0 u/s is the only
     way to kill the skate outright, and that is a pacing call, not a
     gait one. **Ask first.**

**Three traps banked from that arc — all of them cost a session:**

- **`damageTakenMultiplier` IS QUANTISED. Do not tune it as if it were
  continuous.** `CollisionSystem.Soaked` rounds to an int, so against
  an 8-damage rifle round every multiplier in **[0.6875, 0.8125)
  resolves to the same 6**, and 0.50 and 0.55 are indistinguishable.
  There are only FOUR reachable settings between half and none. If an
  ask wants a value between two steps, the knob is `maxHp`, which is
  continuous. This is why the self-test states melee toughness in
  ROUNDS-TO-KILL: asserting a multiplier would assert an input the
  engine does not honour at that resolution.
- **"They died right after taking a player unit out" is a LOCK, not a
  bug, and not an HP consequence.** `StepSkirmishes` kills BOTH bodies
  on `sk.Age >= SkirmishDuration` — no HP, no armour, no roll is read.
  `GAME_DESIGN_LOCKS.md`: *"after ~1s BOTH fall as mutual kills. One
  fighter through = one soldier lost, guaranteed."* **Armour only ever
  buys the APPROACH; it can never buy survival of the fight.** Making
  melee a damage roll would undo the guaranteed-trade maths the whole
  mechanic is costed on — ask before doing it.
- **A DOC LIED AND THE ASSET SETTLED IT.** `UnitAnim` said "a 60° hip
  jog"; the clip is ±60°, i.e. twice that, and every conclusion drawn
  from the comment was wrong by a factor of two. Same family as the
  TMP "ASCII only" note. `PortSelfTest` now SAMPLES the clip for
  swing, cycle length and foot carry and asserts the constants against
  the rig, so a re-export that moves the hip goes red instead of
  quietly regressing the gait.

Every new check in this arc was **seen red against the old code
first**, each naming its defect: `worst 8 of 8`, `±4.00 against 2.36
needed + 0.70 air`, `charge 59.9`, `x1.00 = 3.0 steps/s`,
`(enemy_shield_bearer)`.

**Older, still current: 08-20/21 range + L5.** L5 is 3 MG in the
street, one sniper on the tower, no tank. Ask before restoring L3's
three snipers, rolling MG/sniper roles across the campaign, or selling
the tank.

**Signed — do not reopen as taste**

- Charge gait (08-24). Rob: *"yes, that looks good."*
  `ChargeStride` 0.75 + derived cadence. `AdvanceSpeed` 2.4
  stays. Do not re-raise the stride to play the clip raw.
- Enemy charge armour 0.75 (08-24). Rob: *"ok that's fine."*
  Not 0.5 — he asked for a bit more, not double. The PLAYER's
  shield bearer stays 0.5; its roster line sells being double.
- New distance. Rob: *"that actually plays better"* / *"i
  like the new distance."* v 9→9.5 (flat max 22.56). SpeedScale
  0.0064→0.00677. L1 outpost 7→9, ground 4.5→6.5, tank −9.5.
  Built infantry gap 12.4. **Do not widen the aim frame.**
- L2–L12 enemy side +2 (08-20). Shield charges 1.1/1.0/1.2 →
  1.5/1.3/1.5 so the extra street does not add turns. Melee
  does **not** volley (`FireEnemyVolley` skipped `meleeDamage`;
  that lock was prose). L11 heavies still walk-and-shoot at
  1.2. Player tanks stayed except L5.
- Punch (3+ kills) and miss scorch. Rob: punch *"looks good"*;
  scorch *"easier to see."*
- Arrival headlines gone. Rob: *"ok this is fine."* Keep the
  L10/L11 telegraph strip. HUD names the phase. Camera still
  holds on an arrived group. Do not restore `ThreatLine`,
  `levelGoal` flash, or "The Sovereign will not yield".
- 08-19: body-aim, enemy raise, mid-ground variety, authored
  funnel, phase banners. 08-18: hold, Forest/Mountains/Winter,
  Cluster 3.2x, L1 car, ragdoll tumble/rest, collapse camera.
- Melee camera (L4/L8/L9/L12): hold 1.5s, march 2.4, Grapple
  0.75. Do not share `MarchHalfWidthMin` with contact.

**On the phone, not a taste sign-off**

- **L5 no tank.** Rob: *"ok, fine for now."* Crew folded into
  the ground line (2+5→7). No HP retune. TankArrive still jogs
  the infantry. Rule 4 falls back to front rank → dominant.
  L3 still has a tank. Shop parked in `_plans/BACKLOG.md`.
- **L5 roles.** Those "snipers" were six MG on the platform.
  Now 3 MG in the street, ONE sniper on the tower. Principle:
  MG forward, snipers elevated/back. Not applied to L4/L6/L9/
  L10/L11. L3 left at one sniper from the mix-up.
- **L4 fail cards — WALKED ON DEVICE 08-21, both losses.** Loss 1
  "Charge reached your line", no nudge. Loss 2 "The garrison is
  still firing" + **"You have an Airstrike — take it on the
  retry."** Both +24. The reason tracks the ACTUAL last blow, so
  the same level gives a different card run to run — L4 is not
  reliably the charge card. `a Airstrike` is dead; seen right.
  L1 Smoke nudge still uncalled. Campaign +2 past L1 not walked
  as a set.
- **RIGS now carries the fail card.** `AwardDefeat(level, state,
  testSupply)`. It read `ProgressStore.OwnedConsumables` direct,
  so on a release build the you-have-one branch — the one that
  shipped as "a Airstrike" — could not be reached without
  earning 250 real coins. The new check was seen RED against the
  old code and asserts the economy stays at zero after.

**Do not start:** city-road option 2; L4 march zoom as its own
beat. Wind blocked. Overwatch Flare not sold. Next biome/unit
only if he names one. Do not drop gravity. Do not sell the
tank until L5 is felt without it. Do not restore L3's three
snipers or campaign-wide MG/sniper swaps unprompted.

**If he asks for the leftover:** lose L1 twice to a volley —
Smoke, not Overwatch. L5: if
the tower reads as a wall, cut `hpScale` before a shop.

**Product stack:** 0, 1.1, 1.3, 2.1–2.4 built. 1.2 = waves
only. 1.4 heli shut. Plans: `_plans/RANGE.md`,
`_plans/FAIL_JUICE.md`.

**Traps this sitting paid for — do not re-learn:**

1. **Cannot slide the enemy +2 at v=9.** Flat max is 20.25;
   a roofed garrison goes over 100%. Raise `MaxAimMagnitude`
   (now 9.5, range 22.56) **with** `ProjectileSpeedScale` so
   a ~525 px drag is still 100%. Rule 4 checker max is 20.
2. **`FireEnemyVolley` fired melee.** GAME_DESIGN_LOCKS said
   shield bearers never volley; the path did not skip
   `meleeDamage > 0`. Skip in Prepare and Fire. L11 heavies
   are a firing line — do not make them melee.
3. **L5's "snipers" were machine gunners.** Six MG crowd on
   `tow_top`. Assert the class (`enemy_sniper` vs
   `machine_gunner`), not the silhouette. MG belong in the
   street; snipers on the far elevated deck.
4. **No tank → rule 4 uses the front rank**, not "not
   measurable". `TankArrive` still jogs ground troops.
5. **Do not widen the aim frame.** Seeing both lines during
   the drag is a mechanic change. The emptiness is vertical.
6. **Kenney Nature Kit GLBs have no atlas.** `leafsDark` is aqua
   `(0.17, 0.65, 0.67)`, `woodBarkDark` is peach `(0.80, 0.46,
   0.37)`. That is the kit. We left those colours on once as a
   control shot; Rob: *"now the trees are like an aqua color.
   what is this."* `PlaceStripLayer` paints `leaf*` with the
   layer's silhouette green and `wood*`/`Bark*` brown. **Do not
   restore the kit colours.**
7. **Do not copy the kit into `Assets/Models`.** The scene builder
   wires every GLB there. Source lives in
   `tools/blender/kenney_nature/` (CC0, builder input only).
8. **Forest foreground stays off.** `StripFore` returns null.
   The 2026-08-17 shrubs were magnified ~7x and were never the
   ask. `ForeZ` / `build_fore` stay so a later biome can opt in.
   Do not re-wire Forest. The unused `backdrop_forest_fore.glb`
   can sit.
9. **A snow solid at 6° is a white object**, not snow. L7's cap
   mesh read as icebergs, then a mesa, then a chimney. Winter
   already has a white ground and `snowfall`. `snow_from=2` so
   `make_snow_mesh` emits nothing. Do not put a cap mesh back
   on Winter or Mountains.
10. **Isolated cones / high-octave ridge = a picket.** L1 far was
   `cycles=10, octaves=4` with a white triangle on every horn.
   Far mountains use `kind=range` (broad massifs, 2 octaves),
   `far_foothill`, snow only on wide crests (and Winter none).
   Do not go back to isolated cones.
11. **Do not flip Kenney's hold 180°.** The body already faces
   Unity −X (screen-right / the enemy). Flipping the arms made
   them reach backward — Rob: *"now they're facing the wrong
   direction."* The first "opposite gun" was the mesh vs the
   imported root's +X, not the clip.
12. **Assert the rendered rifle, not `TransformPoint(+X)`.**
   glTFast wraps a root. Span-along-X was GREEN while every
   muzzle pointed at the tank. Mesh bounds-centre along
   `facing.forward` is the check. `LookRotation(forward, left)`.
13. **Mid-ground is z ≈ −8, not the play plane.** z −0.75 sat
   on the squad. Scale 4.1 there was two office towers.
   Backdrop NearZ is −30 and cannot fill the tan.
14. **`keepColors` on wreck / cactus / tree.** Default Tone
   paints every prop player-olive. Sandbags and wire stay
   painted.
15. **Do not play `die` in the air.** It is a sit-down pose.
    The GO tumbles; landing flops to ±90.
16. **Dirt rest is `RagdollRestY(0)`.** `RagdollRestY(spin)` at
    ±90 is a 0.5-unit phantom floor. Roofs and wreck lids raise
    the surface; the live spin does not.
17. **Wreck.Y is the visual BASE** (`st.Y - size/2`), same as
    the wreck GO. The standing centre plus 0.32 sat bodies at
    ~1.6 after the hut had collapsed to the dirt.
18. **A road is kerbs and slabs, not a decal.** 6° turns a
    flat strip into a smear. `PropPlacement.absoluteScale`
    skips Normalize — a 14-unit boulevard at scale 1 would
    otherwise become a postage stamp.
19. **Do not restore the narration banners.** Phase copy and
    arrival headlines are gone on purpose. The telegraph strip
    is the remaining event channel. HUD phase labels stay.
    The camera still holds on an arrived group.
20. **Do not share `MarchHalfWidthMin` with contact.** 2.5 on
    contact crops the tank. The self-test failed at
    cam −6.67 ±2.82 vs tank rear −9.59 when they were one
    constant. Contact is the signed union; march is the
    distant escort.

21. **Containment is a ONE-WAY check.** "The frame holds the force"
    is satisfied by any frame big enough, so a contact shot sat at
    ±4.00 on a ±2.36 engagement for months with two green camera
    tests over it. Whenever a check asserts something FITS, ask what
    stops it fitting with room to spare, and assert that too.
22. **A floor cannot express a fixed lag.** The 4f contact floor was
    paying for the camera's ~0.55 spring trail; a minimum over-pays on
    a small set and vanishes on a large one. Lag is an ADDITION.
23. **Armour, damage and every other signature exist PER ASSET, not
    per class.** The player's shield bearer soaked and the enemy's did
    not, from the same design note. When a mechanic is verified, ask
    which OTHER asset was supposed to have it — every pair in
    `Assets/GameData/Units` is now diffed and only this one differed.
24. **RIGS is not a global — it is a parameter, and every path
    that reads the economy has to take it.** The loadout honoured
    the test supply; `AwardDefeat` read PlayerPrefs directly, so
    one branch of the fail card was unreachable on the only build
    worth measuring. When a feature is verified under RIGS, ask
    which OTHER reads of the balance it goes through.

**Do not widen the aim frame.** That analysis still stands.
The collapse follow was a separate, explicit ask and is signed.

---

**Stop.** Everything below is scar tissue. Pickup above is current.
A dated heading is history, not a job list.

**2026-08-17 — WINTER/MOUNTAINS AND FOREST REWORKED.** History.
The 08-18 sitting superseded the forest cones and the mountain
horn/snow-cap look; those three biomes are now signed. Three
findings from this day, all defects rather than taste, all
fixed in `tools/blender/`:

1. **Winter WAS Mountains.** `build_one` called
   `build_mountains(0.62,"Mountains")` and `build_mountains(0.42,"Winter")`
   — same widths, same cycles, **same hardcoded seeds** (401/523).
   "Backgrounds still look the same" was literally true. Seeds and
   cycles are arguments now; Winter seeds stay 911/947. Verified by
   bucketing crest heights out of the two exported GLBs: mean
   difference **3.83** on a 17-unit range. Snow line 0.42 -> 0.60
   still made L7 a field of white objects; **08-18 set `snow_from=2`
   and `kind=range`**. Do not restore 0.60 or `cycles=7.5`.
2. **`face_dress` could never be seen.** It built rock slabs "so the 6°
   view is not a flat card" and then joined them into the body object,
   which takes ONE flat unlit body colour — the slabs were the exact
   colour of the surface behind them. Splitting them out as `trim_`
   made them visible and WORSE: flat unlit has no lighting, so a slab
   on a face reads as a dark rectangle stuck to a hill. **Depth here is
   bought with OVERLAPPING SILHOUETTES at different values, never with
   surface detail.** Replaced with a third range: a darker foothill
   ridge standing in front of the near one.
3. **Forest: the pine was trunk + ONE cone**, and every crown, bush and
   hill went into one flat green. Now three overlapping skirts (notched
   outline), ~1 in 4 broadleaf, and a third of the stand built in the
   TRIM material so the canopy carries two greens. `ridge()` built the
   land mass out of **boxes** — the pale crates and the "bucket on a
   pole" seen through gaps in the stand were those, not trees; it is a
   continuous noise profile now. Far stand tightened (spacing 2.05 ->
   ~1.38, pines only) so it reads as a mass. `Forest.asset`
   `silhouetteFar` **(0.576,0.737,0.627) -> (0.42,0.565,0.455)** —
   luminance 0.68 against near's 0.35 made every gap read as a bright
   hole rather than distance. Measured on the preview render: far body
   is `(107,144,116)`, was `(147,188,160)`.

4. **Forest detail pass** (same day, after Rob: *"i feel like we could add
   details to the trees/background"*). Emergents at ~1.4x height in both
   layers so the canopy top is not one band; **dead snags** (bare leaning
   trunk + two stub branches) — the only silhouette in a stand that reads
   as a gap, without which a treeline is a hedge; **birch** with the trunk
   in `accent_`, the third value slot the forest had never used, which
   gives a pale vertical and is the single most legible piece of detail at
   6°; ferns at the base; near spacing 2.15 -> 1.85.
   **The far HILL in `accent_` was tried and REVERTED** — the runtime's
   accent is body lerped **78% to WHITE**, which is right for snow on a
   peak and reads as bright holes punched through a dark wood. There is no
   mid-tone slot per layer, so the far hill and far canopy share a value
   and separate on silhouette alone. The comment in `build_far` says so;
   do not re-try it.

5. **THE BACKDROP STRIP HAS A THIRD PLANE** (Rob: *"a little better, but
   more would be preferred"*). Two strips capped the wood at four depth
   steps and six material slots, and no amount of detail on either plane
   fixed it reading as two cut-outs — that ceiling, not the tree models,
   was what "more" was blocked on. `BackdropRuntime.StripMid(style)` sits
   at the existing `Backdrop.MidZ` (-38, previously used only by the
   procedural fallback) and is **OPTIONAL**: only Forest declares one, and
   a style without one keeps drawing its two. A missing mid deliberately
   does NOT drop the strip back to the profile — it logs and carries on —
   so the reference being wired is asserted in `PortSelfTest` instead.
   **Its colour is `Lerp(silhouetteFar, silhouetteNear, 0.5)`**, so a biome
   opts in by adding one GLB and nothing else; no `BackgroundDefinition`
   grows a field. The scene builder already wires EVERY glb in
   `Assets/Models`, so a new layer needs a scene rebuild and no editor
   change. `BackdropPreview` had to learn to register the mid too, or it
   renders a picture the device will not — which is the one thing that
   tool exists to prevent.
   **The new check was seen RED** against a deliberately broken
   `ForestMidModel` (`[FAIL] Forest declares mid strip
   backdrop_forest_mid_MISSING and Battle.unity wires it`) and restored.

6. **FOREGROUND STRIP + the measurement that reframed this whole job.**
   After a third *"still feels meh"* I stopped tuning trees and measured
   **Archery Bastions on the device** instead (it is installed —
   `com.bastion.archers`). Its backdrop is FLATTER than ours: two plain
   mountain silhouettes and a couple of torii. What fills its screen is
   the **objective** — a fortress spanning ~68% of the width and ~25% of
   the height, carpeted in massed units, plus one sky object and framing
   gates at the edges. Ours: treeline is a ~10% band, ~34% empty sky above
   it, ~27% empty ground below, six small units, and the enemy structure
   **off-screen entirely** during Aiming. **The backdrop was being asked
   to carry a frame it occupies a tenth of** — which is why three passes
   of genuinely better trees each moved it a little and then stalled.
   Edge-pixel density, the fairer of the two proxies: **ours 0.60% ->
   1.65% with the foreground; the reference is 6.08%.** Still ~3.7x short,
   and the rest of that gap is composition, not backdrop.
   `Backdrop.ForeZ = 3` — the only layer at POSITIVE z, in front of the
   play plane. It must stay inside `CameraDirector.ZMin` (5.5) or a tight
   frame puts the camera behind it; asserted in `PortSelfTest`.
   **Two things this layer CANNOT do, both learned on device:**
   - **No trunks, and no framing at the screen EDGES.** A world-fixed strip
     does not move with the camera, and the camera pans the length of the
     battlefield, so "a trunk at the left and right edges" is not
     expressible here. Built anyway to test it: one trunk is ~110px wide,
     runs the FULL height of the screen, and cut the frame in half through
     the band the shells arc through. Anything tall belongs in the near
     strip, behind the units.
   - **Scale cannot be reasoned about, only measured.** This layer is
     magnified ~**7x** against the play plane — far more than the naive
     `(camZ - ForeZ)` ratio predicts, because the projection is tilted and
     off-centre. Fronds authored at 0.42-0.80 filled the bottom third as
     dark pyramids; at 0.16-0.32 they were still a row of little tents with
     legible gaps. The numbers that work (**0.03-0.10**, overlapping at
     0.07-0.14 spacing, with per-frond width and lean jitter) were read
     back off device screenshots, and a frond finishes about a third of a
     soldier's height.
   Watch the vert budget: an ico-sphere bush is ~240 exported verts against
   a frond's ~30, and at this density the bushes alone were 60k. Forest is
   **64k verts over four layers** (fore 34k) in 8 draw calls, 60 fps held.

### Unit hold + emptiness — 2026-08-18

Two jobs in one sitting. Both on the phone. Ask git.

**Hold.** Identity attach pointed `placeholder_gun` (+X barrel) at
the camera. Aligning the bone then flipping the hold 180° pointed
the mesh downfield and turned the silhouette around. Reverted the
hold. Final attach is `LookRotation(forward, left)`. Checks: player
`facing.forward.x < −0.7`, mesh centre on that forward. Ready-drop
16° + 2.4° breathe; idle damp is per-joint. Rob: *"ok good. that
works."*

**Emptiness.** Measured on device, L1, play-area crop (HUD and ammo
out). Same classifier on every phase:

| phase   | content | edges | ground |
|---------|---------|-------|--------|
| Arrive  | 10.2%   | 1.16% | 10.8%  |
| Scout   | 20.0%   | 1.65% |  8.6%  |
| Aiming  |  6.5%   | 0.64% | 14.9%  |

Aiming is the empty beat. Scout is fuller because the outpost is
tall. **Do not widen the camera** (see below). Lever (1) is
play-space mass behind the player line.

Block berms (`prop_flank`) were a stand-in — too tall, then too
close. Rob asked for real things. `prop_wreck_car` (two-box
hatchback, 28° yaw, charcoal) + `prop_dead_tree` on L1 at z ≈ −8.
Car: *"yeah think that's good."* Then all twelve campaign levels
got two `keepColors` plants. Rob: *"ok. seems repetitive but we
can address later."* Parked in `_plans/BACKLOG.md`.

Builder: `tools/blender/build_prop_scenery.py`. Placement is
`LevelDefinitionSO.props` with `keepColors`. `LevelScenery` skips
`Tone` when that is set.

### Widening the aim frame — ASKED 2026-08-17, ANALYSED, NOT RULED ON

Rob, after the foreground: *"if you were to modify camera architecture,
what would you do? do you have a clear understanding?"* Read
`CAMERA_ARCHITECTURE.md` (still LOCKED) and `CameraDirector` before
re-opening this. **The recommendation is DO NOT WIDEN IT**, for three
reasons, and none of them is reluctance to touch a locked file:

- **It does not fit, and it makes everything smaller.** `camZ =
  (halfWidth + FramePad) / ZHalfFovTan`, so `GameplayZ` 22 caps half-width
  at `22 * 0.45 - 0.6 = 9.3` — about 18.6 units of framable width.
  Composition rule 4 puts tank -> dominant structure at **14-18** units,
  the player line is ~6 wide and the enemy cluster up to ~11, so the union
  is **20-28 units**. It does not fit under the current Z ceiling at the
  far end, and where it does, the units shrink. **The reference's frame
  is not full because it is WIDE — it is full because its fortress is TALL
  and CLOSE and carpeted in units.** Zooming out is the wrong direction.
- **It is a MECHANIC change.** Seeing the player line and the enemy in one
  frame during the drag hands the player a direct visual read of range —
  the guess-angle/power mechanic a landing marker and an aim-pan were both
  built and REVERTED for. `PlayerScout` already shows the enemy before the
  aim; the tight frame is what stops it being measured during the drag.
  This needs a difficulty re-tune, not a camera tweak.
- **There is a documented bug waiting.** Widening the half-width without
  moving the camera X anchor reproduces the L12 `staticCamera` failure
  already written up in that doc: *a half-width only frames its subject
  about the centre the camera actually uses.*

**The diagnosis instead: the emptiness is VERTICAL and structural.**
Framing by WIDTH on a 1080x2404 portrait screen means ~6 units of framed
width buys ~13 units of visible HEIGHT. The content is a flat line of
2.7-unit figures on flat ground, so most of the frame is empty whatever
the camera does. Levers, cheapest first: **(1) vertical content on the
player side** — flanking trees in the near strip, a tall player-side
structure, terrain; no camera change, no gameplay change, NOT locked, and
this is the recommended next move. **(2) horizon placement**, redistributing
the 34% sky / 27% ground split — moves emptiness rather than removing it.
**(3) widen**, only with the mechanic change accepted and `GameplayZ` raised.

**CAVEAT THAT MUST BE CHECKED BEFORE ANY OF THIS.** Every number quoted
here and in item 6 was measured on the **Aiming** frame, which is the
tightest and emptiest moment in the game BY DESIGN. The whole job has been
judged on its worst frame. Resolving, PlayerScout, TankArrive and a march
are all wider and were never measured. **Measure those first** — if the
game is only empty during the aim beat, this is a much smaller problem
than the session treated it as, and lever (1) covers it.

**`BackdropPreview.Shots` is the fast loop for this work** — it loads
the GLBs BY PATH, so it re-renders in ~1 minute against a Blender
re-export with no scene rebuild and no APK. Use it to iterate, then
confirm on the device. It cannot catch a broken scene reference (that
is the point of the city trap below), so the device run is still
required before believing anything.

**GUARD EVERY adb INPUT WITH A FOCUS CHECK — `install -r` KILLS THE
RUNNING APP.** On 2026-08-17 a reinstall tore the app down, the relaunch
had not settled, and a dozen queued taps went into the LAUNCHER and opened
Rob's browser. Nothing was typed or navigated, but it is his real phone.
The fix is one line in front of every step, and it has since aborted a run
cleanly instead of repeating the mistake:

```bash
chk(){ adb shell dumpsys window | grep -q "mCurrentFocus.*$PKG" \
       || { echo "FOCUS LOST — aborting"; exit 1; }; }
```

Also: a **fresh install lands on the LOADOUT screen, not the battle** —
press BEGIN before any ◀ ▶ / RIGS navigation, or the taps hit nothing. And
the `UnityApplication::DestroyInstance` crash in `logcat -b crash` after a
reinstall is that teardown, **not** a gameplay fault.

**Sampling pixels beat eyeballing twice this sitting.** A preview
thumbnail is small enough that a real colour change looked like no
change at all; `PIL` on the PNG settled it in one command. And an
ffmpeg `blend=difference` of two device frames proved a change had
NOT reached the screen when the two shots looked different to me.

Builders in **this** repo (`tools/blender/`):
- `build_backdrop_city.py` — signed
- `build_backdrop_forest.py` — far/mid/near Kenney Nature Kit
  (CC0, `tools/blender/kenney_nature/`). Foreground unwired.
  Runtime retints leaf* / wood* — do not ship kit aqua/peach.
- `build_backdrop_land.py` — mountains / winter / desert.
  Mountains and Winter: `kind=range`, far foothill. Winter
  `snow_from=2` (no cap mesh). Desert still a smooth dune.
- `build_prop_scenery.py` — signed three (wreck_car,
  dead_tree, cactus) plus the 2026-08-19 biome set
  (pine, stump, boulder, rubble, lamp, barrel_cactus,
  snow_pine, skiff, piling). `keepColors` on the
  placement. Do not re-export the signed three.
  `prop_flank.glb` is the unused box-berm — do not plant it.
Re-export changes GLB root fileIDs. After every export:
`SpikeSceneBattle.Build` then the APK. Never `Normalize` a strip.
Never `bpy.ops.wm.read_factory_settings` in a live MCP Blender.

**What this sitting signed — do not reopen as taste:**

- Player ground line MARCHES (`MarchStride` 0.5 / `MarchAnimSpeed`
  0.7). Enemy charge still full walk.
- Outpost collapse + fire/smoke; rest of destroyable wrecks shipped
  from it.
- L8 flyover. Rob: *"ok saw it in action, looks good."*
- Deaths glass pane. Lip / sink / flail knobs stay.
- Garrison wreck fire nestled in the pile. Rob: *"looks good."*
- L8 leftover rubble grid + flying chunks (cap 0.14, TTL 8s).
  Rob: *"yes. this looks good."*
- **Airstrike ride** (not the 08-11 cut). Rob: *"yes. looking good."*
  UI ticks on RIGS / CAM / ◀ ▶ press and release.
- **Forest backdrop** (Kenney meshes, biome green, brown bark,
  no foreground). Rob: *"much better. let's keep that."*
- **L1 Mountains** far range (broad massifs, no tooth-snow).
  Rob: *"looks good."*
- **L7 Winter** (same range on a snow field, no cap mesh).
  Rob: *"ok looks good."*
- **Unit hold** (guns downfield). Rob: *"ok good. that works."*
- **L1 wrecked car** (two-box hatchback, mid-ground). Rob:
  *"yeah think that's good."*

**Parked — ask before starting any of these:**

1. **Biome strips.** City, Forest, L1 Mountains, L7 Winter are
   signed. **Desert and Ocean were deliberately NOT touched**:
   Desert is still a smooth dune ridge and shares
   `build_backdrop_land.py`, so it inherits the seed/cycle
   argument but has had no pass; Ocean is still the unpainted
   sun/surf plan. MountainsDusk was not judged on its own (L1
   mesh, dusk tints). Before dressing another biome, read the
   camera section — a fourth biome pass may be the wrong spend.

2. **L11 elite wave — on the phone, not called.** Lands IN FRONT
   of the post (`anchorX` 3, box left edge 4.13) and walks
   (`advancePerTurn` 1.2). Heavies have no melee — they hold as
   a firing line. Was x 9 in the post's shadow. Rule 8 green.
   Confirm on Oceanfront turn 4 ("Elite squad inbound").

3. **Procedural wrecks in general.** Outpost is the only
   hand-keyed clip. Everything else is loose-part fold + XYZ
   (glTF import is QUATERNION — Euler keys without
   `rotation_mode = "XYZ"` export location only). Watch is a
   pile (28%); garrison leftover was the hut, now a rubble
   grid. Tank is not a wreck.

4. **Armour zoom** (L4 first enemy march / L12 leftover),
   2026-08-13, **never signed off.** `FramePad` 0.6. Closer
   only if he says so.

5. **L1 rifleman v2 at Aiming distance** — look, do not remodel.
   If it still reads as the old box man, stop adding pouches.

6. **Mid-ground scenery variety** — signed 2026-08-19.
   Rob: *"ok, looks nice."* L1 car slot unchanged. A further
   taste pass may come later. See `_plans/archive/MIDGROUND_VARIETY.md`.

**If a sitting starts with the phone:**

1. **L11 Oceanfront turn 4** — elites in front, walking.
2. **L1 after BEGIN** — rifleman v2. Then L4/L12 armour zoom.
3. Emptiness on Aiming vs Scout/Arrive is measured (table above).
   Mid-ground variety is signed. City-road street objects and
   the closer L4 march are on the phone, not called.

**Already signed off, do not reopen as tuning:**

- **Melee** — L4. Hold 1.5s, march 2.4, GrappleGap 0.75. Swing is bound.
- **MG burst fan** — *"yeah, think this looks fine."*
- **Opening scout** — `TurnPhase.PlayerScout` after the arrive, first
  battle only. Arrive sits *in front*; do not fold them together.
- **L12 Sovereign stays in the gate's shadow.** *"sovereign is fine on l12."*
  Do not set `triggerStructureIds: [citadel, gate]`.
- **Tank mesh / CityRuins / Forest (Kenney + green/brown) / L1
  Mountains / L7 Winter / deaths (including L8 flyover) / wreck
  fire / leftover rubble / flying-chunk size / airstrike ride** —
  see above. Do not reopen lip / sink / flail, the glass-pane
  numbers, the wreck-fire nestle, the leftover grid, the
  airstrike cut, Kenney aqua, Forest foreground, or a snow-cap
  mesh.
- **Whole-body aim / enemy raise / authored funnel / narration
  banners.** 2026-08-19. Arrival headlines gone 2026-08-20.
  Do not restore `ThreatLine`, the levelGoal flash, or
  "The Sovereign will not yield". Do not reopen the aim pose
  as taste.

**City strip — traps that cost a device session:**

- **Re-exporting `backdrop_city_*.glb` changes the GLB root fileID.**
  The scene keeps the *name* and the prefab slot goes missing. The
  phone then draws `Backdrop.City()` — the old orange-window picket
  fence — with no error. `BackdropPreview` loads by path and will
  still look right. After every city export: `SpikeSceneBattle.Build`,
  then the APK. `PortSelfTest` now asserts both scene refs are live.
  `BackdropRuntime` requires **both** far and near, and logs if either
  is missing.
- **Flame z.** Positive z at ground = orange squares in the street.
  Deep negative z = inside the wall, invisible. Window mouth: marker
  `fx_fire_*` x/y, z just proud of the facade (`−0.05..+0.18`).
  `RuinFx.CollectMarks` plants tongues there; glow stays deeper.
- **Never `Normalize` the city GLBs.** Width is the span.
- Builder: `tools/blender/build_backdrop_city.py` in **this** repo.
  Facades face Blender −Y (Unity +Z). Mass goes +Y / Unity −Z.

**Blender MCP — how this sitting worked, and what kills it:**

- Blender is `DISPLAY=:1 ~/blender/blender-5.1.2-linux-x64/blender`.
- The addon auto-starts on **localhost:9876**. Grok's `blender-mcp` server
  stays up even when Blender dies. If tools say connection refused, do
  **not** re-add the MCP — relaunch Blender:
  `DISPLAY=:1 …/blender --python /tmp/start_blendermcp.py`
  (`/tmp/start_blendermcp.py` enables `blender_mcp_addon`).
- **Never `bpy.ops.wm.read_factory_settings`.** It unregisters the addon
  and drops the socket. Clear objects by hand.
- Viewport screenshots come back black. Render Eevee to a `/tmp` PNG and
  read that.
- Colour still binds to **mesh name prefix** (`skin` / `trim` / `accent`).
  Animation binds to joint paths. Normalize scales by the **longest
  axis** — a short helmet is how the rifle went over their heads.
- Rifleman stays the skinny baseline (`UNIT_VARIETY_DESIGN.md`). Tank
  keeps origin at the base-center, **+X toward the enemy**,
  `accent_pivot_TankGun` at the trunnion, `accent_wheel*` as separate
  nodes, X-span ~1.27 so `Normalize(1.5)` and muzzle `1.08 / 0.72` still
  land.
- Builders: rifleman is `build_units_rigged.py` `build_rifleman()` plus
  `tools/blender/build_rifleman_v2.py` in the retired repo. Tank is
  `tools/blender/build_tank_v2.py` (same place);
  `build_early_structures.build_tank` delegates to it. City is
  `tools/blender/build_backdrop_city.py` in **this** repo. Live GLBs
  are here: `Assets/Models/unit_rifleman_rigged.glb`,
  `placeholder_tank.glb`, `backdrop_city_far.glb`,
  `backdrop_city_near.glb`.

**What the zoom sitting built** (detail in `CAMERA_ARCHITECTURE.md` and §2):

- Aiming frames the GROUND LINE, not the tank crew.
- Enemy frame recaptures when a structure falls or a boss/wave lands, never on a
  casualty.
- Announcement push-in on the arrived group, 2.5s.
- March sits on the chargers until they are inside 5 of the player line;
  contact keeps the signed-off union.
- **TankArrive** (2026-08-14) is a new phase *before* the scout. Camera
  holds the union of tank + crew + ground line while they enter. Not a
  cut. See `CAMERA_ARCHITECTURE.md` item 0.

**THE TIER STACK IS PARKED FOR THIS SITTING, not unfinished.** Tiers 0,
1.1, 1.3, 2.1–2.4 are built; 1.2 is waves only (wind parked); 1.4 heli
is shut. C / D / E below are still true and still waiting — they are
not what to open a new session with unless Rob asks.

#### C. DECISIONS WAITING ON ROB — no work starts until he calls them

- **Did the closer shot make the riot shield readable?** On the phone now. If no,
  a marker is a unit-art call on that build.
- **Overwatch Flare.** Charge is a signed-off threat. Catalog entry +
  `EnemyAI.AdvanceBudget(..., halved)`.
- ~~**Is Cluster's 3.2x spread too wide to connect?**~~ **CLOSED 2026-08-18.**
  Rob: *"ok cluster is fine."* `spreadScale` 3.2 stays.

Still open as a **fairness read**, not a constant: does losing the tank crew to a
charge feel fair, or cheap?

#### D. WARM-UPS, if a session wants one — small, bounded, and none of them urgent

- ~~**`Loadout.GroundAnchorX` averages disjoint groups**~~ **CLOSED 2026-08-13.** If the
  count-weighted mean sits inside an enemy collision box (or closer to an enemy structure
  than to any ground group) it is the gap trap, and the largest authored flank is the
  line. Do not also filter those flanks by the same box — the parade's scale-reference
  groups brush CliffOutcrop and MountainBunker, so both get thrown away and the mean
  comes back. Campaign centres are unchanged (asserted). Seen red: `GroundAnchorX 0.00`,
  4 bodies in RidgeWatchtower; then green at `-5.60`, in-ridge 0. Test rigs still skip
  the picker in play (`EnterLevel`); this is the function BalanceAudit / a future
  rig-loadout would have called.
- **Flames outlive their bodies by a frame or two** (`_plans/BACKLOG.md`). Diagnosed from
  the code 2026-08-13: flame and ragdoll share the dying entity's xyz on the same frame.
  A missing body with a flame present is either a silent ragdoll `Take` miss (now warned)
  or the die clip folding a garrison into the bunker until the impulse lifts it. The
  ragdoll / structure second mechanism (stuck on the lip) was fixed and seen on
  device 2026-08-16. Optional; not a pickup.
- ~~**Incendiary's `burnDamage = 6`**~~ **STALE, already 8.** The asset and `AmmoSetup`
  both say 8. The 6-vs-8hp-Sniper note was resolved when the burn was re-derived against
  the live roster (frailest crowd body is 12 hp; `PortSelfTest` anchors to that). Do not
  re-raise it as a warm-up.

#### E. PARKED BY DECISION — do not reopen these as stale flags

- **Wind** — `windAccelZ` drifts the round in Z while collision is X/Y only, so wind cannot change
  what a shot hits. Rob parked it 2026-08-10. Making it real is a PHYSICS change and needs an ask.
- **The heli (Tier 1.4)** — `HELI_ENABLED=false` is a camera-load decision.
- **Tier 3 habit glue** — chests and the daily bonus shipped as stubs; real ads/IAP is gated behind
  "≥5 fun sessions exist" in `PRODUCT_DIRECTION.md`.
- **The crowd split's second doubling** — CLOSED 2026-08-12 on arithmetic, not on taste. Read
  "THE CROWD SPLIT HAS NO REMAINING LEVER" below before re-deriving it: x4 is 8 hp and the
  incendiary burn is 8, so the rifleman one-shots to a single tick.

### Art sitting — 2026-08-14, Blender MCP

Rob: park the tiers briefly and improve how the game looks. This sitting did
three things. Detail is in the pickup and in `UNIT_VARIETY_DESIGN.md` /
`CAMERA_ARCHITECTURE.md`.

- **Rifleman v2** — ACH pot, plate + mag row, neck/hands/boots. Same Kenney
  joints, still the skinny class. First helmet 2.64 put the hold-pose rifles
  over their heads (`Normalize` × `AttachGun` in model units). Helmet now
  2.93. `PortSelfTest` samples the hold on the built prefab.
- **Tank v2** — glacis, visible road wheels, round barrel, bustle. Rob:
  *"looks good."* `accent_pivot_TankGun` and `accent_wheel*` kept.
- **Opening arrive** — `TurnFlow.StartBattle` / `TurnPhase.TankArrive`.
  Tank rolls 3.6, crew ride, ground line jogs the same distance on
  `MarchTargetX` + the walk clip, 2.0s, then the signed-off scout. Rob:
  *"cool!"*
- **CityRuins strip** — Rob: *"ok, looks good."* Two world-scale
  GLBs on FarZ / NearZ. Ashen charcoal (`CityRuins.asset`), not the
  imported warm brown. Facade dress (floor bands, soot, recessed
  windows), scorch stains, rubble piles on the camera side of the
  facade. Tongues on `fx_fire_*` markers in the window mouths;
  interior glow deeper; smoke from the rubble. `Backdrop.City` is
  the fallback / test contract only. Builder:
  `tools/blender/build_backdrop_city.py`. Worn by L4 and L10.
  Traps are in the pickup — re-export needs a scene rebuild.

Do not start the next biome until he names one.

### Ragdoll contact + sink — 2026-08-14/16, ON THE PHONE

Rob: twitching on death, and bodies near a structure lip hanging instead
of falling. He wanted them to **bend against** masonry. Then sink into
the dirt, then the ground twitch was still too much.

Not a new clip and not Unity `Rigidbody`s (locked: cosmetic, tick-owned).

1. **Twitch (first).** `ApplyFlail` is a sine on the limbs. The renderer
   compared Y to `RagdollRestY` (dirt, ~0.05). A garrison on a deck at
   y=2.5 was airborne for the whole 5s TTL. `SupportY` is the surface
   they landed on.
2. **Stuck on the lip.** Gravity dips a roof-sitter a hair below `topY`.
   The face test saw "spawned inside" and killed `vx`; the roof test
   snapped them back up. Roof and face are mutually exclusive. Within
   `RagdollLipMargin` (0.55) of a face they are pushed off and fall.
   `Bend` folds the torso toward the contact.
3. **Sink.** Render-only `RagdollSinkY` on the last 0.9s of the TTL,
   dirt only (`SupportY` ≤ body height). Roofs and the tank deck do not
   sink. Rob: *"the sink into the ground looks good."*
4. **Twitch (second), on the dirt.** Leftover `vx` still counted as
   airborne, so the sine ran while they slid. Airborne is height / `vy`
   only. Flail cut to 10°/5° at ~1–2 Hz. Rob: *"ok looks better."*

Contact first look: *"ok looks better."* Do not reopen as a taste pass.

### 0. WARM-UPS — 2026-08-13, NO DEVICE

GroundAnchorX and a code read of the flame artefact. No APK. Details in D; the only
lesson worth keeping out of the pickup: **a flank authored against scenery will fail
the same box test that caught the mean.** Filtering both "clear" flanks returned the
rejected average. The authored groups are the answer.

### 1. ADVANCING SQUADS + MELEE — BUILT 2026-08-12, SEEN ON A DEVICE THREE TIMES SINCE

**The eighth dead system is alive.** `AdvanceSystems.cs` ports the mechanic from the retired
Kotlin, which is the only implementation it has ever had. Enemy assault squads bank a budget on
the edge into EnemyWindup, walk at the player's line during the windup, hold just short of it, and
a fighter that arrives claims a soldier and trades itself for him.

**It activates SHIPPED DATA on FOUR campaign levels — L4, L8, L9 and L12** — not the two this file
said. Every one of them has authored `advancePerTurn` since the port and has been fielding a class
that stood still.

What is in the build:

- **`AdvanceSystems.BankBudget / March / Claim / StepSkirmishes`**, engine-independent, called from
  `BattleTick` in a new section 7b. The budget is banked ONCE on the handover edge, the same shape
  as the incendiary burn two blocks below it, for the same reason: one legible step per turn.
- **The windup countdown is FROZEN while anyone is still walking** (`BattleRunner`), so the march
  owns its own beat instead of racing the volley for the same 1.5 seconds.
- **A skirmish HOLDS the turn open.** `TurnFlow.EvaluateVolley` has taken a skirmish count since
  the port and had never once been passed a non-zero one.
- **The bodies WALK.** Kenney's `walk` is bound as a fifth clip (the melee swing is the sixth,
  2026-08-13) and plays on layer 0 in place of the idle, with the two-handed hold left on the
  arms — legs march, rifle stays carried.
  **This needed a scene rebuild**, being a prefab change.

**THE SHIELD BEARER'S 12 MELEE DAMAGE IS STILL DEAD, and this file predicted otherwise.** It said
the number "goes live the day advancing squads do". It does not, and the reason is worth keeping:
**`meleeDamage` is only ever read as a FLAG** — "does this class fight hand-to-hand" — in the
reference build as well as this one, and **a skirmish is a MUTUAL KILL rather than a damage roll**,
so no melee number is arithmetic on either side. It also only reaches the ENEMY copy, because
skirmishes are claimed by ADVANCING attackers and `LevelBuilder` pins every PLAYER unit's
`AdvancePerTurn` to 0 — which the locked turn structure requires. The player's shield bearer keeps
ARMOUR as its distinctness, exactly as Tier 2.3 gave it. `RosterAudit`'s warning has been rewritten
to say this; it used to promise the opposite.

**644 checks, and five were seen RED against the unwired tick first**, with the numbers recorded:
budget `0.00`, the marcher's x `0.03 -> 0.03` (it never moved), `0 fight(s)`. The first draft of
that check THREW instead of failing when no fight started, which aborted every check after it — a
check that explodes is not a check that failed, and it now reports red and returns.

**TWO THINGS THE FIRST DEVICE BUILD FOUND, both fixed the same session.** Rob played it and
reported exactly two problems, and each was a system that had been PORTED and never CONNECTED —
the same shape as the mechanic itself:

1. **"We need to see the melee/assault force attacking the line. That happens off camera and it's
   weird."** `CameraDirector.PhaseHalfWidth` has had a marcher branch since the port and
   `BattleTick` fed it **`0f, false` from a literal**, while the windup anchor was the level's
   fixed ENEMY-side value. So the camera watched the shooters standing still while the assault
   walked into the player's line off the left edge. Fixed by feeding it the real march and
   skirmish sets and porting `EnemyWindupAnchorX` — **three beats**: ride the march, then HOLD on
   the skirmish line until every fight resolves (an engaged attacker is no longer a marcher, so a
   target built from marchers alone snaps away ~1s before the mutual-kill payoff), then pan back
   to the RANGED shooters, who are the ones about to fire. Seen red: camera pinned at the enemy
   anchor `4.53` with the marchers at `-0.72`.
2. **"The player standing on the tank never gets touched by the assault force."** True, and it
   made the whole mechanic toothless: the reference build exempts anyone standing on a structure,
   which is right for the ENEMY side (every garrison is on a wall or tower) and wrong for the
   player's, whose only garrison is the TANK CREW at **0.60** up on a vehicle. Kill the ground
   line and the chargers had nobody left they were allowed to touch, so melee could never lose the
   battle. **Reach is now a HEIGHT, not a flag** — `AdvanceSystems.MeleeReachHeight = 1.0`, read
   off the unit's own Y so it cannot disagree with what is drawn. The measured gap is wide: the
   tank deck is 0.60 and every enemy structure is 1.40, 1.63, 2.50 or 3.75. The hold line moved to
   "front-most REACHABLE body" for the same reason. Seen red with every ground unit removed: the
   crew survived 900 ticks untouched, `2 -> 2`.

**AND A THIRD, from the build that fixed the first two:** *"when the actual melee attack takes
place, the camera should stay on that until it's complete."* Holding the fight inside the WINDUP
branch was not enough — **a skirmish spans phases** (the handover gate waits for it by design), so
a fight still running when the windup ended handed the frame to the volley chase, which is by
definition somewhere else on the field. The fight now owns the camera — anchor AND framing — in
any phase, outranking the volley chase rather than averaging with it, and **the windup countdown is
frozen while a fight runs** as well as while a march does. That second half is not optional: with
the volley free to fire over a running scuffle, the camera is locked on the melee exactly as a
dozen rounds leave the far side of the field and the player sees neither. The sequence now always
reads **march -> fight -> volley**, one at a time.

**AND A FOURTH:** *"we need to focus the camera on the whole attack so the player can see what's
happening to their force."* Holding on the fight framed the fight and nothing else — on L4 that put
the camera at x -5.1 with a half-width of 4.0, covering -9.1 to -1.1: **half the picture is empty
ground to the RIGHT and the TANK CREW at -9.59 is cropped out**, which is precisely the force the
player wants to watch being attacked. Contact still frames that UNION. The MARCH no
longer does: sitting back for the whole field made L12's escort a speck (Rob,
2026-08-13, on the armour). Far chargers own the frame; the threatened front enters
inside 5; contact takes the union. Seen red at `cam -5.24 ±4.00` against a rear rank
at -9.59.

**AND A FIFTH:** *"we still are in a hurry to zoom back to the main force. we need to show the
melee assault the whole time and pause so it registers with the player."* The camera was released
on the TICK the skirmish list emptied — so the payoff, the two bodies actually falling, played
while the camera was already leaving. Measured: half a second after the last pair fell the camera
had travelled from **-7.52 to +3.53**, eleven units away.

`GameState.MeleeHold` is a post-melee dwell of **1.5s**, the same family as `TurnHandoverDelay`
(which exists because the handover used to tread on an impact the player was still reading) and
sized against `PostVolleyPauseSeconds`' 1.6s. Three things about it are load-bearing:

- **The frame is CARRIED, not recomputed.** Once the fight is over its participants are gone from
  the unit lists, so a recomputed frame snaps to whatever is left on the tick the hold begins —
  the exact lurch the hold exists to prevent. The anchor and half-width are captured on the last
  fighting tick.
- **It decays on EVERY tick path, including the cosmetic one.** A melee mutual kill can be the blow
  that ENDS the battle, and a hold left frozen on the victory screen is a value that never decays
  again — the standing rule in CLAUDE.md, and this is the first thing to hit it since the flame.
- **The volley is held off for the duration.** Otherwise the shooting starts while the camera is
  parked on two bodies falling — the same mistake the windup freeze was added to prevent, one beat
  later.

**654 checks.** All five fixes were seen red against the build Rob played, with the numbers above.
The framing check asserts **CONTAINMENT, not proximity** — the camera deliberately does not sit on
the fight, so a distance test would fail the correct behaviour; and the half-width it measures is
recovered from `CameraFollowZ` through `TargetZ`'s own inverse rather than re-derived, so it cannot
drift from the camera the game actually uses.

**AND ONE CHECK THAT PASSED AGAINST THE BUG IT WAS WRITTEN FOR**, which is the standing lesson
turning up again in a new costume. The camera check first seeded the camera ON the fight and
stepped ONE tick — a spring that has not had time to move is not evidence of a spring that stayed,
and it went green against the reverted code. Stepping 40 ticks (two thirds of a second, short of
`SkirmishDuration`) made it real: **cam 3.92 against a fight at -5.12**, nine units away. `PUT A
CHECK IN A STATE WHERE IT COULD FAIL` is in CLAUDE.md and it still took a revert run to catch.

**ROB'S VERDICT AFTER THE FIFTH BUILD: "ok, better. we'll refine in another session."** That
session ran 2026-08-13. **The mechanic is signed off.** The three constants and the swing stay.
What that refining session looked at:

1. **`AdvanceSystems.PostMeleeHoldSeconds` (1.5s)** — the dwell was judged once, at one value. It
   is one constant and the most likely thing to be wrong.
2. ~~The fight has no ANIMATION.~~ **THE SWING SHIPPED 2026-08-13 and was confirmed on device** —
   see "The melee swing" below. Still no blood: the Kotlin's blood debris takes its colour from a
   STRUCTURE definition and does not port cleanly.
3. ~~**The march's own pacing**~~ **SIGNED OFF 2026-08-13.** `AdvanceSpeed` 2.4 and the frozen
   windup stay. Rob, L4: the march is fine. **The GAIT was reopened 2026-08-24** — the pacing
   was never the complaint, the legs were. Speed untouched; see the 08-24 block at the top.
4. **Overwatch Flare**, which this unblocks. The advance is now a signed-off threat, so a
   counter is a real product call rather than a guess.

**THE SECOND DEVICE PASS RAN ON 2026-08-13**, alongside the melee swing — see "The melee swing"
below. The third pass the same day signed the three constants. The questions:
- ~~is the fight legible now that the camera holds on it?~~ **Yes** (2026-08-13).
- ~~does it hold for the right length?~~ **Yes** — 1.5s stays (2026-08-13).
- **does losing the tank crew to a charge feel fair, or cheap?** Still open. Melee can END the
  battle; the crew is the last thing standing on most levels. Not a constant — a fairness read.
- ~~does a squad walking at the line read as PRESSURE, or as men wandering forward?~~ **The
  march is fine** (2026-08-13).
- ~~is the frozen windup a beat, or a stall?~~ **Fine** with the march (2026-08-13).
- ~~does a mutual kill read as a fight, or as two bodies falling over at once?~~ **A fight.**
  Both ends swing. Still no blood.

**OVERWATCH FLARE IS NOW UNBLOCKED** — the one Tier 1.3 consumable deliberately not built, because
it had nothing to watch for. `EnemyAI.AdvanceBudget(basePerTurn, halved)` is called with `halved:
false` from one place; the consumable is a catalog entry plus that one bool. **Judge whether the
advance is threatening BEFORE building its counter** — a counter to a mechanic nobody fears is
worse than no counter.

### THE MELEE SWING — shipped and CONFIRMED ON DEVICE, 2026-08-13

The first item on the refinement list above, and the one it called "the biggest remaining gap
between the mechanic is real and the mechanic reads". `attack-melee-right` is bound as a SIXTH
clip and both ends of every skirmish now swing.

**What it took, in case a seventh clip is ever wanted: `UnitAnim.Melee` + one entry in
`RiggedUnits.Wanted` + a `Layer()` line + a scene rebuild.** That is the whole cost, and it is the
same shape `walk` paid the day before.

- **Layer 2, WrapMode.Loop, SHARING the layer with `shoot`.** They are alternatives rather than a
  stack — a man swinging a rifle butt is not also firing it — and Legacy resolves same-layer clips
  by whoever played last. They never compete in practice anyway: the volley is held off for the
  duration of a fight.
- **`SetFighting` fades OUT rather than stopping**, and that is not tidiness. **A fight does not
  always end in a death**: kill the attacker mid-scuffle and his victim is spared, which is the
  mechanic's whole counter-play, so a SURVIVOR has to put his arms down. A hard Stop on a looping
  clip drops him into the hold in one frame.
- **BOTH ENDS SWING.** `BattleRunner` tests `AttackerId == u.Id || VictimId == u.Id`, which covers
  the enemy charger and his claimed victim from one call without knowing which side it is drawing
  — ids are unique across the two sides, `LevelBuilder` gives the enemy its own base. The victim
  fighting back is the point: a skirmish is a MUTUAL kill, and a man standing at ease while
  someone kills him reads as a bug rather than as a trade.
- **THE AIM IS SUPPRESSED WHILE FIGHTING.** `SyncUnits` hands the player's whole line one aim pose
  and does not know a man is busy, so without this his victim holds the live drag elevation
  through the entire scuffle.
- **`attack-melee-right` DRIVES THE ROOT** — a ±0.10 lunge in local Z, the step into the strike.
  Third clip to do it after `die` and `walk`, so it takes the same exemption from `LateUpdate`'s
  root clamp; clamping it deletes the step and leaves a man swinging from the waist.
- **A locked fighter no longer counts as WALKING.** That was the best available answer while the
  swing was unbound. The melee clip drives the legs itself and outranks the walk from layer 2, so
  the march now means only the march.

**656 checks.** Two new ones, and the swing check **failed its own control on the first run**,
which is the whole reason it carries one: it measures the right arm's travel and compares it
against `holding-both`, a STATIC two-handed pose that must read ~0 by the same ruler. The first
draft pooled the quaternion's x/y/z/w into one range — which measures the POSE, not the motion —
and read the constant hold as a **90.0 degree swing**. Per component it is 0.0 against the melee's
59.4. A travel measurement that cannot report zero is not a measurement.

**CONFIRMED ON DEVICE, L4 Ash Boulevard, 2026-08-13.** Five turns of real drags to contact, then
the assault reached the line: engaged bodies are unmistakably in a different pose from the rank
behind them — legs split wide, weapon up over the head — against a squad still holding the static
firing stance four units away. The battle ended **DEFEAT — "Your line was overrun"** on turn 9,
which is the melee killing the player outright, so the reach fix from the previous session holds
under real play as well as in a check.

**WHAT THE BUILD SHOWED THAT IS STILL OPEN**, and both are Rob's calls rather than bugs:
- **The fight cluster INTERPENETRATES.** `GrappleGap` was 0.30 and the bodies visibly overlapped
  at contact — a scrum rather than pairs. Trial is 0.75 (Rob, 2026-08-13). The march still holds
  at 0.55; only a lunge to a deeper victim uses the new gap.
- **Still no blood.** The Kotlin's blood debris takes its colour from a STRUCTURE definition, so it
  does not port cleanly; nothing marks the moment of the kill except the two ragdolls.

### 2. WAITING ON ROB, not on anyone's time

**L12's Sovereign stays in the gate's shadow.** Rob, 2026-08-13: *"sovereign is fine on
l12."* The one-field fix (`triggerStructureIds: [citadel, gate]`) is still supported and
still changes what the finale demands (~280 masonry vs ~288 stock siege). Do not apply
it. He spawns at x 5.42; the gate box is `x[1.25,3.75]`, top 2.00.

Note this is a SHADOW, not an embedding, and rule 8 does not flag it — correctly. The crude "is it
behind a taller box" heuristic used to find it fires on plenty of harmless geometry (L10 has
arrivals 5.94 clear of a 1.40 box). Deciding which shadows are real needs the game's own
trajectory solver, not a ratio someone invented. **That is a rule 9 and it does not exist.**

**The shield bearer's armour is a CAMERA problem, not a missing decal.** Rob, looking
at it: *"it's zoomed so far out i can't see it. we need to do better about zooming in
in that scenario."* Half-damage is still real and still has no icon
(`CollisionSystem.Soaked`, `40hp x2.0 armour = 80`). The closer frames are what this
sitting built; a marker is still a unit-art call if those are not enough.

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

- ~~The advancing exemption never verifies the unit leaves.~~ **CLOSED 2026-08-12**, alongside the
  squads that made it load-bearing. Rule 8 now exempts an advancer only if it clears the box on its
  FIRST march; one that clears eventually is a **WARNING**, and one that never clears is the Error
  it always was. The severities mean different things and the blanket exemption conflated them.
  **It found one case immediately: L12's boss shield escort** starts 1.91 inside the gate's box at
  1.20/turn, so it takes two marches to become hittable. Left as a warning rather than fixed —
  it is the same gate as item 6 and the same beat Rob signed off, so it is his call, not a
  data edit to make quietly. `PortSelfTest` fails on Errors only now and LOGS the advisory.
- ~~**`Loadout.GroundAnchorX` averages disjoint groups.**~~ **CLOSED 2026-08-13.** See D.
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

**1. GET ROB'S EYES ON THE TIER 2.3 CHANGES.** *(Half done — the armour was measured on device
2026-08-12 and the burst's FAN still has not been seen in motion. The live version of this item is
B in "Pick up here"; what follows is the reasoning behind it.)* Both change how a battle FEELS
rather than how it reads in a table:
- **The machine gunner now fires three rounds a volley, each on its own jitter.** The thing to
  judge is whether a squad of them reads as suppressing fire or as noise — three times the rounds
  in the air is the biggest change to what a volley looks like since cluster ammo.
- **The shield bearer now takes half damage.** Nothing on screen says so. Its health bar simply
  falls slower, and if that is illegible the mechanic is real but invisible — which is the
  failure mode `UNIT_VARIETY_DESIGN.md`'s "honest limit" is about. **A visible marker was
  deliberately NOT built**: adding one is a unit-art change and those are decided on a device,
  never in advance.
  **And armour is now the class's PERMANENT mechanic, not a stand-in.** Melee shipped on
  2026-08-12 and did NOT give the player's shield bearer anything: `meleeDamage` is read as a flag
  and only on the enemy copy — see item 1.
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

**Device state at handover:** the installed build is **CURRENT** — advancing squads, melee, and all
five camera/reach fixes from the 2026-08-12 assault session. Installed by uninstall/reinstall, so
the economy is at a **fresh zero** and RIGS is the way to reach anything buyable. Checked and left
CLEAN: stay-awake off, DND off, auto-rotate on, no captures on `/sdcard`. **Nothing is committed** —
the whole assault session is in the working tree.

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

- **Tier 1.1 is CLOSED.** Cluster's 3.2x spread is fine (Rob, 2026-08-18). The last
  leftover is cosmetic and parked in `_plans/BACKLOG.md`: **flames outlive their
  bodies by a frame or two** at the moment the burn kills (diagnosed 2026-08-13 as
  ragdoll/structure or a silent `Take`, not a second flame position).
- ~~A per-frame NullReferenceException on the LOADOUT screen.~~ **FIXED 2026-08-10** — the tick
  running with no battle to tick. That screen is clean, which matters because the consumable UI is
  built on it.
- **`_plans/BACKLOG.md` is the only LIVE plan**, and holds what Rob has parked: a **nuclear
  reactor structure** (the open question is what MECHANIC it owns — a blast on destruction would
  make it the first structure with one), a **crowd-runner bonus level**, **mid-ground scenery
  variety**, and the **look-pass** (next biome/unit unnamed). Sink is signed (08-16). The
  ragdoll / structure / mid-air hang is signed (08-18/19). The three finished plans moved to
  `_plans/archive/` on 2026-08-11; nothing there describes current behaviour.

### How the rest of this file is ordered

Everything below "Pick up here" is HISTORY, newest first: the 2026-08-17 winter/forest
rework, then the 2026-08-13 warm-up sitting, then melee / opening scout, then the two
2026-08-12 sessions (the Tier 2.3 audit, then Tier 2.2's crowd half), then the three
2026-08-11 sessions, then 2026-08-07 to 08-10,
then the standing reference sections — **"Where things are", "What works", "The workflow",
"Traps already paid for"** and **"Open items"/"Things that will bite"**, which are the
parts that are still TRUE rather than still interesting. The closed 2026-08-05/06 port
entries are in `HANDOVER_ARCHIVE.md`.

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
2. ~~**A decision, not a task: re-tune incendiary, or leave it.**~~ **The number is 8**,
   re-derived against the live roster (frailest crowd body 12 hp). The old "still 6,
   calibrated to an 8hp Sniper" sentence was stale prose — the asset, `AmmoSetup` and
   the later "Verified on device" section already agreed. A further raise is still a
   balance call, not a warm-up.
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

## Archived — moved to `HANDOVER_ARCHIVE.md` on 2026-08-25

The file had passed 4000 lines. These sections are CLOSED — a system built, or a bug fixed — and
none is a statement about current behaviour, so they were moved WHOLE rather than summarised.
**Nothing was deleted.** The live rules they paid for stayed behind in "Traps already paid for".

| Section | What it holds |
|---|---|
| Enemy factions — Tier 2.1, built 08-11 | two stages, two armies, and the traps that cost |
| Player camo — Tier 2.4, built 08-11 | four sets; the NINTH ported-but-unreached system |
| Tier 1.3 — the consumables, built 08-10 | the four items, the cap of two, device proof per item |
| The loadout screen's per-frame NRE — FIXED 08-10 | `state` is null until BEGIN; the tick ran anyway |
| The incendiary flame — 08-09 | drawn off `BurningEnemyIds`; why the `[Burn]` log stays |
| Tier 1.2 — the telegraph and the schedule, 08-07 | the countdown is composed, never authored |
| The balance audit, DEVICE half — run 08-07 | **STALE — measured at THREE tank shells** |
| THE TANK SHELL DOES NOT LAND WHERE YOU AIM — found 08-07 | the overshoot, diagnosed |
| The shell now lands where you aim — FIXED 08-07 | solved onto the volley's landing point |
| Tier 1.1 — AMMO TYPES, built 08-07 | the four rounds, and asserting the OUTPUT not the factor |
| Corpses levitating onto roofs — FIXED 08-07 | a body may not land on a roof it never reached |

**What did NOT move, and why:** `Siege retune — 2026-08-07` says **PARTLY verified** in its own
title and carries the live `hpScale` table for five levels, so it is still open work.
`RIGS doubles as TEST SUPPLY` describes what RIGS does TODAY and is used every session.

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
