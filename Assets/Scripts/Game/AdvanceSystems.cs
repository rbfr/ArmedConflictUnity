using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// ADVANCING ASSAULT SQUADS AND THE MELEE THEY EXIST FOR — ported 2026-08-12 from the retired
    /// Kotlin's `GameViewModel`, which is the only implementation this mechanic has ever had.
    ///
    /// This was the EIGHTH dead system: `UnitEntity.AdvanceRemaining` was declared and never
    /// written, `SkirmishEntity` was declared and never constructed, and `EnemyAI.AdvanceBudget`
    /// was ported and never called. Every level that authored `advancePerTurn` — L9 and L12's
    /// shield bearers — fielded a class that simply stood still.
    ///
    /// THE MECHANIC IS A TARGET-PRIORITY DECISION, not a second damage source. A squad walking at
    /// the player's line forces the question "stop the chargers, or hit the force behind them?"
    /// every turn, and answering it wrong costs a soldier per attacker that arrives. That is the
    /// whole design; the melee itself is deliberately not a damage roll (see StepSkirmishes).
    ///
    /// WHY EVERYTHING HERE IS ENEMY-SIDE. `LevelBuilder` pins every PLAYER unit's AdvancePerTurn
    /// to 0 and this port keeps that: the player fires from a fixed line, which is the locked
    /// turn structure. The player's counter-play is the volley, not a counter-charge.
    ///
    /// Pure functions over the state, engine-independent like the rest of `ArmedConflict.Game` —
    /// `BattleTick` owns the sequencing and the ragdolls.
    /// </summary>
    public static class AdvanceSystems
    {
        /// <summary>
        /// March speed while a unit is spending its banked budget. Sized so a full
        /// `advancePerTurn` (~1.2 on the authored levels, ~2.2 at the Kotlin's widest) is spent
        /// INSIDE the 1.5s windup — the march must finish before the volley it precedes, or the
        /// shooters fire while the camera is still following the chargers.
        /// </summary>
        public const float AdvanceSpeed = 2.4f;

        /// <summary>
        /// How close to the player's front line an advance is allowed to press before it holds.
        /// Just short of <see cref="MeleeRange"/> on purpose: chargers close to arm's length and
        /// then fight, rather than walking through the line and out the other side.
        /// </summary>
        public const float AdvanceStopGap = 0.55f;

        /// <summary>How near the front line a melee unit must be before it claims a victim.</summary>
        public const float MeleeRange = 0.7f;

        /// <summary>
        /// How long a locked pair scuffles before both fall. Long enough to read as a fight and
        /// short enough that the turn handover — which WAITS for it, see TurnFlow.EvaluateVolley —
        /// is not visibly stalled.
        /// </summary>
        public const float SkirmishDuration = 1.05f;

        /// <summary>
        /// How far from itself an arrived fighter may claim a soldier. Generous on purpose: the
        /// whole front cluster is reachable, so N arrivals really do cost N soldiers rather than
        /// the two that happen to stand closest.
        /// </summary>
        public const float SkirmishEngageRange = 1.8f;

        /// <summary>Sprint speed over the last gap once a victim is claimed. Faster than the
        /// march — the arrival should read as a lunge, not as more walking.</summary>
        public const float SkirmishLungeSpeed = 5f;

        /// <summary>
        /// Where the attacker stops relative to its victim: grappling distance.
        /// 0.30 stacked the cluster into a scrum (device, 2026-08-13). 0.75 is the trial
        /// so each pair reads as two bodies rather than one blob. The march still holds at
        /// <see cref="AdvanceStopGap"/> (0.55); this only changes the lunge, and only when
        /// the claimed victim sits deeper than 0.75 — raising the stop itself above
        /// <see cref="MeleeRange"/> would park chargers outside claim range.
        /// </summary>
        public const float GrappleGap = 0.75f;

        /// <summary>
        /// HOW HIGH A DECK A MAN ON THE GROUND CAN STILL REACH.
        ///
        /// The reference build asked "is this unit standing on a structure?" and exempted anyone
        /// who was. That is the right answer for the ENEMY side, where every garrison sits on a
        /// wall, a post or a tower — decks of 1.4 to 3.75 — and completely wrong for the PLAYER
        /// side, where the only garrison in the game is the TANK CREW, standing 0.60 up on a
        /// vehicle. Rob, first device build: *"the player standing on the tank never gets touched
        /// by the assault force."* He was right, and the whole assault was toothless because of
        /// it: kill the ground line and the chargers had nobody left they were allowed to touch,
        /// so the battle could not be lost to melee at all.
        ///
        /// Asking the HEIGHT instead of the flag separates the two cleanly — the measured decks
        /// are 0.60 (tank) against 1.40 / 1.63 / 2.50 / 3.75 (every enemy structure) — and it
        /// reads off the unit's own Y, which the renderer already draws it at. A predicate that
        /// re-derived the deck from the structure list could disagree with what is on screen; this
        /// one cannot.
        /// </summary>
        public const float MeleeReachHeight = 1.0f;

        /// <summary>
        /// Can a man standing on the ground put a rifle butt into this one? Feet-height only —
        /// see <see cref="MeleeReachHeight"/>.
        /// </summary>
        public static bool Reachable(UnitEntity u) => u.Y <= MeleeReachHeight;

        /// <summary>
        /// Barbed wire (`PropPlacement.slowsAdvance`) crawls a charge to this fraction of march
        /// speed while it is inside the prop's span. Wire in the lane buys the player an extra
        /// volley or two, which is the only lever a level has over this mechanic's pacing.
        /// </summary>
        public const float WireSlowFactor = 0.35f;

        /// <summary>
        /// How long the camera stays on a melee after the last pair has fallen.
        ///
        /// The fight itself is <see cref="SkirmishDuration"/> = 1.05s, and its whole payoff lands
        /// in the final frames of it — so releasing the camera the instant the skirmish list
        /// empties plays the kill as the camera is already leaving. Sized against the beat this
        /// project already uses for the same job: `TurnFlow.PostVolleyPauseSeconds` is 1.6s of
        /// holding on an impact the player is still reading, and this is the same ask.
        /// </summary>
        public const float PostMeleeHoldSeconds = 1.5f;

        /// <summary>
        /// Banks each advancer's budget for the coming windup. Called ONCE, on the edge into
        /// EnemyWindup — a per-tick top-up would make the march continuous rather than one
        /// legible step per turn.
        ///
        /// <paramref name="overwatchFlare"/> is Overwatch Flare's counter-play: it halves what
        /// every advancer banks this turn. The consumable is not built yet (it had nothing to
        /// watch for until now), so the parameter exists for the one caller that will pass true.
        /// </summary>
        public static List<UnitEntity> BankBudget(IReadOnlyList<UnitEntity> enemyUnits,
                                                  bool overwatchFlare = false)
        {
            var banked = new List<UnitEntity>(enemyUnits.Count);
            foreach (var u in enemyUnits)
            {
                banked.Add(u.AdvancePerTurn > 0f
                    ? u with { AdvanceRemaining = EnemyAI.AdvanceBudget(u.AdvancePerTurn, overwatchFlare) }
                    : u);
            }
            return banked;
        }

        /// <summary>
        /// Ground speed this advancer is actually moving at, wire included. Pulled out of
        /// <see cref="March"/> 2026-08-24 so the RENDERER can ask the same question: the charge
        /// gait is derived from ground speed (<c>UnitAnim.GaitSpeed</c>), and a renderer that
        /// re-derived the wire test would be a second copy of it waiting to disagree.
        /// </summary>
        public static float MarchSpeed(UnitEntity u, IReadOnlyList<PropPlacement> props)
        {
            foreach (var w in props)
            {
                if (!w.slowsAdvance) continue;
                if (Mathf.Abs(u.X - w.x) <= w.halfWidth * w.scale)
                    return AdvanceSpeed * WireSlowFactor;
            }
            return AdvanceSpeed;
        }

        /// <summary>True while any advancer still has budget to spend.</summary>
        public static bool Marching(IReadOnlyList<UnitEntity> enemyUnits)
        {
            foreach (var u in enemyUnits) if (u.AdvanceRemaining > 0f) return true;
            return false;
        }

        /// <summary>
        /// Walks the advancers toward the player line, spending budget as they go.
        ///
        /// THE HOLD LINE IS ANCHORED TO WHAT THE SQUAD CAN ACTUALLY REACH — see
        /// <see cref="MeleeReachHeight"/>. Anchoring to ALL units sends it sprinting at a tower
        /// garrison it can never touch, which the reference build reports as the squad "running
        /// off" after a kill; anchoring to GROUND units alone made the tank crew untouchable.
        ///
        /// A unit already locked in a skirmish is skipped: it lunges (see StepSkirmishes), and
        /// two systems moving the same body fight each other.
        /// </summary>
        public static List<UnitEntity> March(IReadOnlyList<UnitEntity> enemyUnits,
                                             IReadOnlyList<UnitEntity> playerUnits,
                                             IReadOnlyList<PropPlacement> props,
                                             IReadOnlyList<SkirmishEntity> skirmishes,
                                             float dt)
        {
            // THE HOLD LINE IS THE FRONT-MOST REACHABLE BODY, which is not the same as the
            // front-most GROUND body: the tank crew stands 0.60 up and is a perfectly good target,
            // so a squad that has finished the ground line still has somewhere to be. Anchoring to
            // ground units alone is what left the crew untouchable — and anchoring to ALL units
            // would send the squad sprinting at a tower garrison it can never reach, which is the
            // "running off after the kill" the reference build documents.
            float? frontlineX = null;
            foreach (var p in playerUnits)
            {
                if (!Reachable(p)) continue;
                if (frontlineX == null || p.X > frontlineX.Value) frontlineX = p.X;
            }

            var marched = new List<UnitEntity>(enemyUnits.Count);

            // Nobody left they can reach: bank is dropped so the squad stands.
            if (frontlineX == null)
            {
                foreach (var u in enemyUnits)
                    marched.Add(u.AdvanceRemaining > 0f ? u with { AdvanceRemaining = 0f } : u);
                return marched;
            }

            float holdX = frontlineX.Value + AdvanceStopGap;
            var engaged = new HashSet<int>(skirmishes.Select(sk => sk.AttackerId));

            foreach (var u in enemyUnits)
            {
                if (u.AdvanceRemaining <= 0f || engaged.Contains(u.Id)) { marched.Add(u); continue; }

                float speed = MarchSpeed(u, props);
                float step = Mathf.Min(speed * dt, u.AdvanceRemaining);
                float newX = Mathf.Max(u.X - step, holdX);
                marched.Add(u with
                {
                    X = newX,
                    AdvanceRemaining = newX <= holdX ? 0f : u.AdvanceRemaining - step,
                });
            }
            return marched;
        }

        /// <summary>
        /// Locks arrived fighters onto victims.
        ///
        /// CLAIMING HAPPENS THE MOMENT A FIGHTER ARRIVES — mid-march, in any phase — rather than
        /// at the volley-fire instant. An arrival that engages immediately reads right on camera,
        /// and it closes the race the Kotlin hit where the enemy's own volley killed the claimed
        /// soldier before a fire-time skirmish had started.
        ///
        /// One victim per attacker, nearest first, and never a garrisoned soldier: a unit
        /// standing on a structure is out of reach of a man on the ground.
        /// </summary>
        public static List<SkirmishEntity> Claim(IReadOnlyList<SkirmishEntity> skirmishes,
                                                 IReadOnlyList<UnitEntity> enemyUnits,
                                                 IReadOnlyList<UnitEntity> playerUnits)
        {
            var result = new List<SkirmishEntity>(skirmishes);
            bool anyMelee = false;
            foreach (var u in enemyUnits) if (u.Definition.meleeDamage > 0) { anyMelee = true; break; }
            if (!anyMelee) return result;

            var claimed = new HashSet<int>(skirmishes.Select(sk => sk.VictimId));
            var engaged = new HashSet<int>(skirmishes.Select(sk => sk.AttackerId));

            float? frontX = null;
            foreach (var p in playerUnits)
            {
                if (!Reachable(p)) continue;
                if (frontX == null || p.X > frontX.Value) frontX = p.X;
            }
            if (frontX == null) return result;

            foreach (var attacker in enemyUnits
                         .Where(u => u.Definition.meleeDamage > 0 && !engaged.Contains(u.Id))
                         .OrderBy(u => u.X))
            {
                if (attacker.X - frontX.Value > MeleeRange) continue;

                UnitEntity victim = null;
                float best = float.MaxValue;
                foreach (var p in playerUnits)
                {
                    if (!Reachable(p) || claimed.Contains(p.Id)) continue;
                    float gap = Mathf.Abs(p.X - attacker.X);
                    if (gap < best) { best = gap; victim = p; }
                }
                if (victim == null || best > SkirmishEngageRange) continue;

                claimed.Add(victim.Id);
                result.Add(new SkirmishEntity(attacker.Id, victim.Id));
            }
            return result;
        }

        /// <summary>What a tick of skirmish resolution did. The caller owns ragdolls and tallies.</summary>
        public class SkirmishStep
        {
            public List<SkirmishEntity> Skirmishes = new();
            public List<UnitEntity> PlayerUnits;
            public List<UnitEntity> EnemyUnits;
            /// <summary>Bodies that fell THIS tick — mutual kills, victims and attackers both.</summary>
            public List<UnitEntity> KilledPlayers = new();
            public List<UnitEntity> KilledEnemies = new();
        }

        /// <summary>
        /// Ages every locked pair, lunges the attacker in, and resolves the fight.
        ///
        /// A SKIRMISH IS A MUTUAL KILL, NOT A DAMAGE ROLL, and that is the ported design rather
        /// than a simplification: `meleeDamage` is only ever read as a FLAG in the reference
        /// implementation — "this class fights hand-to-hand" — and never as a number. A soldier
        /// who lets a charger reach him trades himself for it. That makes the cost of ignoring an
        /// advance exactly countable, which is what makes the target-priority decision readable.
        ///
        /// TWO PIECES OF COUNTER-PLAY LIVE HERE, both worth keeping:
        ///  - kill the ATTACKER mid-scuffle and the soldier is spared outright;
        ///  - kill the claimed VICTIM with a stray round and the fighter RE-TARGETS rather than
        ///    standing down, so "N arrivals = N dead" holds even under a chaotic volley.
        /// </summary>
        public static SkirmishStep StepSkirmishes(IReadOnlyList<SkirmishEntity> skirmishes,
                                                  IReadOnlyList<UnitEntity> playerUnits,
                                                  IReadOnlyList<UnitEntity> enemyUnits,
                                                  float dt)
        {
            var result = new SkirmishStep
            {
                PlayerUnits = playerUnits.ToList(),
                EnemyUnits = enemyUnits.ToList(),
            };
            if (skirmishes.Count == 0) return result;

            var aged = skirmishes.Select(sk => sk with { Age = sk.Age + dt }).ToList();

            foreach (var sk in aged)
            {
                var attacker = result.EnemyUnits.FirstOrDefault(u => u.Id == sk.AttackerId);
                // Shot dead mid-scuffle: the fight simply ends and the soldier lives. This is the
                // counter-play, so it must come before anything else can resolve.
                if (attacker == null) continue;

                var victim = result.PlayerUnits.FirstOrDefault(u => u.Id == sk.VictimId);
                if (victim == null)
                {
                    // Claimed soldier killed by stray fire first — re-target the next nearest.
                    var taken = new HashSet<int>(result.Skirmishes.Select(o => o.VictimId));
                    foreach (var o in aged) if (!ReferenceEquals(o, sk)) taken.Add(o.VictimId);

                    UnitEntity next = null;
                    float best = float.MaxValue;
                    foreach (var p in result.PlayerUnits)
                    {
                        if (!Reachable(p) || taken.Contains(p.Id)) continue;
                        float gap = Mathf.Abs(p.X - attacker.X);
                        if (gap < best) { best = gap; next = p; }
                    }
                    // Nobody left in reach: stand down.
                    if (next == null || best > SkirmishEngageRange) continue;

                    result.Skirmishes.Add(new SkirmishEntity(attacker.Id, next.Id));
                    continue;
                }

                if (sk.Age >= SkirmishDuration)
                {
                    result.PlayerUnits = result.PlayerUnits.Where(u => u.Id != victim.Id).ToList();
                    result.EnemyUnits = result.EnemyUnits.Where(u => u.Id != attacker.Id).ToList();
                    result.KilledPlayers.Add(victim);
                    result.KilledEnemies.Add(attacker);
                    continue;
                }

                // Lunge: sprint the last gap, stopping at grappling distance.
                float grappleX = victim.X + GrappleGap;
                if (attacker.X > grappleX)
                {
                    float newX = Mathf.Max(attacker.X - SkirmishLungeSpeed * dt, grappleX);
                    result.EnemyUnits = result.EnemyUnits
                        .Select(u => u.Id == attacker.Id ? u with { X = newX } : u).ToList();
                }

                // Blows land on two beats. The knockback flinch is the ONLY thing on screen that
                // says a blow connected — there is no blood debris in this port (the Kotlin's
                // pipeline is structure-chunk shaped here and takes its colour from a structure
                // definition), so if the fight reads as two men standing still, this is the line
                // that owes the fix.
                float prevAge = sk.Age - dt;
                foreach (float beat in Beats)
                {
                    if (prevAge >= beat || sk.Age < beat) continue;
                    result.PlayerUnits = result.PlayerUnits
                        .Select(u => u.Id == victim.Id
                                     ? u with { KnockbackAge = 0f, KnockbackDirX = -1f, LastHitAge = 0f }
                                     : u)
                        .ToList();
                }

                result.Skirmishes.Add(sk);
            }
            return result;
        }

        static readonly float[] Beats = { 0.35f, 0.7f };
    }
}
