using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ArmedConflict.Data;

namespace ArmedConflict.Game
{
    /// <summary>
    /// Fifth slice of the GameViewModel port: the per-phase camera choreography.
    ///
    /// Step 2 already proved the underlying ground-line SOLVE ports exactly (0.685 to five
    /// decimals across camZ 4..40). This is the layer above it — deciding, each phase, WHICH
    /// POINTS must stay in frame, and smoothing the result.
    ///
    /// Everything here runs IN THE TICK, never in a UI coroutine, so the camera and the
    /// projectiles it tracks advance atomically in one state. A separate follow loop beats
    /// against the tick clock and makes projectiles jitter on screen.
    ///
    /// The framing decision reduces to "which points are in the set", fed to
    /// CameraFraming.HalfWidth — one shared function rather than a bespoke span/anchor formula
    /// per phase. That duplication is exactly what once let a settled melee unit balloon the
    /// enemy-windup zoom, because two hand-written formulas quietly disagreed about who counted.
    /// </summary>
    public static class CameraDirector
    {
        /// <summary>
        /// Minimum half-width while the airstrike's aircraft is making its pass.
        ///
        /// Chosen through TargetZ rather than by eye: the caller adds 1.2, and camZ is
        /// halfWidth / ZHalfFovTan, so 5.1 puts the run at camZ 14 — comfortably wider than the
        /// camZ 11 at which `PlanePreview` judged the model to fit with margin, and wide enough
        /// that the aircraft and the ground it is bombing are in the same picture.
        /// </summary>
        public const float AirstrikeRunHalfWidth = 5.1f;

        public const float GameplayZ = 22f;
        public const float ZMin = 5.5f;
        public const float ZHalfFovTan = 0.45f;

        /// <summary>
        /// Air around every framed set, added before <see cref="TargetZ"/>.
        /// Was 1.2; Rob, 2026-08-14: camera feels far out, hard to see. 0.6 is
        /// a little closer without changing WHO is in the shot — composition
        /// still frames the same points, just with less empty sky. The airstrike
        /// floor still clears the 11 that fits the aircraft.
        /// </summary>
        public const float FramePad = 0.6f;

        /// <summary>Fast — the bullet cam must stay glued to a volley in flight.</summary>
        public const float VolleyFollowSmoothTime = 0.06f;

        /// <summary>Slow — escorting a march is a saunter, not a chase.</summary>
        public const float MarchEscortSmoothTime = 0.30f;

        /// <summary>
        /// How long the camera stays on the last kill after the battle is won.
        ///
        /// The cosmetic-over path used to spring to the survivors the same tick Phase became
        /// Victory, so the killing blow — a garrison coming off a bunker, the last man falling —
        /// played as the camera was already leaving. Rob, 2026-08-13: leave it there a couple of
        /// seconds so it registers. Same family as <see cref="GameState.MeleeHold"/>.
        /// BattleUI's victory card waits this long too, or the dim covers the beat.
        /// </summary>
        public const float VictoryCamHoldSeconds = 2.0f;

        /// <summary>
        /// How long a collapse owns the camera after a garrisoned structure
        /// comes down. The first <see cref="CollapseFollowSeconds"/> ride the
        /// falling bodies (the tumble is the shot); the rest of the hold
        /// releases the camera so it springs back to whoever is still
        /// standing. The hold itself also freezes the enemy windup, so they
        /// do not fire while the camera is still on the throw. Same family
        /// as <see cref="GameState.MeleeHold"/>.
        ///
        /// Rob, 2026-08-18, explicit ask against the camera lock: follow
        /// the fall to show the animation, then pan back to the live line.
        /// </summary>
        public const float CollapseHoldSeconds = 2.1f;
        public const float CollapseFollowSeconds = 1.25f;
        public const float CollapseHoldPad = 2.2f;
        public const float CollapseHoldHalfMin = 3.8f;
        /// <summary>
        /// Tight frame while riding the throw. The wide pad/min would keep
        /// the ruin in picture without the camera actually moving — the
        /// bodies only travel a couple of units.
        /// </summary>
        public const float CollapseFollowPad = 1.2f;
        public const float CollapseFollowHalfMin = 2.4f;
        /// <summary>Faster than a march escort, slower than the bullet cam.</summary>
        public const float CollapseFollowSmoothTime = 0.12f;

        public static bool CollapseIsFollowing(float collapseHold)
            => collapseHold > 0f
               && (CollapseHoldSeconds - collapseHold) < CollapseFollowSeconds;

        /// <summary>
        /// Frame for a collapse: the ruined structures and the men who stood on
        /// them, plus pad so a throw stays in picture. Carried on the hold —
        /// those men leave the unit lists the same tick.
        /// </summary>
        public static void CollapseFrame(IReadOnlyList<float> xs,
                                         out float anchor, out float half)
        {
            FrameXs(xs, CollapseHoldPad, CollapseHoldHalfMin, out anchor, out half);
        }

        /// <summary>
        /// Live frame of the tumbling garrison. Empty (no tumble bodies
        /// left) leaves the carried frame alone so a retarget cannot snap
        /// to an empty set.
        /// </summary>
        public static void CollapseFollowFrame(IReadOnlyList<DyingUnitEntity> dying,
                                              ref float anchor, ref float half)
        {
            if (dying == null || dying.Count == 0) return;
            var xs = new List<float>();
            for (int i = 0; i < dying.Count; i++)
                if (dying[i].Tumble) xs.Add(dying[i].X);
            if (xs.Count == 0) return;
            FrameXs(xs, CollapseFollowPad, CollapseFollowHalfMin, out anchor, out half);
        }

        /// <summary>
        /// Second beat: whoever is still standing. Empty (the last
        /// garrison just died) leaves the carried fall frame so the
        /// camera does not snap to a vacant default.
        /// </summary>
        public static void CollapseReturnFrame(IReadOnlyList<UnitEntity> liveEnemies,
                                              ref float anchor, ref float half)
        {
            if (liveEnemies == null || liveEnemies.Count == 0) return;
            var xs = new List<float>(liveEnemies.Count);
            for (int i = 0; i < liveEnemies.Count; i++)
                xs.Add(liveEnemies[i].X);
            FrameXs(xs, CollapseHoldPad, CollapseHoldHalfMin, out anchor, out half);
        }

        static void FrameXs(IReadOnlyList<float> xs, float pad, float halfMin,
                            out float anchor, out float half)
        {
            if (xs == null || xs.Count == 0)
            {
                anchor = 0f;
                half = halfMin;
                return;
            }
            float lo = xs[0], hi = xs[0];
            for (int i = 1; i < xs.Count; i++)
            {
                if (xs[i] < lo) lo = xs[i];
                if (xs[i] > hi) hi = xs[i];
            }
            anchor = (lo + hi) * 0.5f;
            half = Mathf.Max((hi - lo) * 0.5f + pad, halfMin);
        }

        /// <summary>
        /// A FLOOR on the march frame, so escorting a lone advancing unit does not zoom to a
        /// keyhole. Verified against the Kotlin (4f) rather than inferred — guessing it at 2
        /// would have framed every march twice as tight as intended.
        /// </summary>
        public const float MarchHalfWidthMin = 4f;

        /// <summary>
        /// How close a charger has to get before the player's line joins the march frame.
        ///
        /// Farther than this, the camera sits on the squad so a class (the riot shield)
        /// is readable. At this gap the threatened front starts entering, and at contact
        /// the signed-off union takes over. L12's escort starts ~8 units out; L4's ~6.
        /// </summary>
        public const float MarchIncludePlayerGap = 5f;

        static float Span(IReadOnlyList<float> xs)
            => xs.Count == 0 ? 0f : xs.Max() - xs.Min();

        /// <summary>
        /// Volley-follow camera x. Two properties matter and both are easy to lose:
        ///
        /// MONOTONIC PURSUIT — the target is clamped so the camera only ever moves the way the
        /// volley flies. A rising mean from rounds landing raggedly would otherwise drag the
        /// camera backwards mid-flight, which reads as a stutter.
        ///
        /// The mean is over GROUND volley rounds only; helicopter door-gunner bullets are
        /// excluded upstream via ProjectileEntity.IsHeliShot, so the heli can never pull the
        /// camera off the ground volley.
        /// </summary>
        public static float FollowVolley(float? currentX, float currentVelocity,
                                         IReadOnlyList<ProjectileEntity> groundVolley,
                                         IReadOnlyList<UnitEntity> playerUnits,
                                         IReadOnlyList<UnitEntity> enemyUnits,
                                         IReadOnlyList<StructureEntity> structures,
                                         bool resetForMelee,
                                         float dt,
                                         out float newVelocity)
        {
            float mean = groundVolley.Average(p => p.X);
            float lo = playerUnits.Count > 0 ? playerUnits.Min(u => u.X) : -7f;
            var hiCandidates = enemyUnits.Select(u => u.X).Concat(structures.Select(s => s.X)).ToList();
            float hi = hiCandidates.Count > 0 ? hiCandidates.Max() : 7f;

            float clampedMean = Mathf.Clamp(mean, lo, hi);
            float current = (resetForMelee ? null : currentX) ?? clampedMean;
            float velocity = resetForMelee ? 0f : currentVelocity;

            bool fliesRight = groundVolley[0].OwnerIsPlayer;
            float target = fliesRight ? Mathf.Max(clampedMean, current)
                                      : Mathf.Min(clampedMean, current);

            float value = current;
            SpringFollow.Step(ref value, ref velocity, target, dt, VolleyFollowSmoothTime);
            newVelocity = velocity;
            return value;
        }

        /// <summary>
        /// Which points each phase must keep in frame. This is the whole choreography, and the
        /// asymmetry is deliberate:
        ///
        ///  Aiming      — the PLAYER LINE ONLY. Keeping it tight is what makes the aim readable;
        ///                the campaign composition rules are written against this (~6 wide).
        ///  PlayerScout — the enemy cluster, INCLUDING structure edges, so the player sees what
        ///                they are shooting at before they aim.
        ///  EnemyWindup — the marchers if any are moving, otherwise the SHOOTERS. Melee units
        ///                are excluded from the shooter set: a settled melee unit far up the
        ///                field would otherwise widen the frame for no reason.
        ///  Resolving   — whichever side is being SHOT AT.
        /// </summary>
        public static float PhaseHalfWidth(
            TurnPhase turnPhase, TurnSide turnSide,
            float playerHalfWidth, float enemyHalfWidth, float shooterHalfWidth,
            float marchHalfWidth, bool marchersActive,
            float reinforceHalfWidth, bool playerReinforcing)
        {
            switch (turnPhase)
            {
                case TurnPhase.Aiming:
                    return playerReinforcing ? reinforceHalfWidth : playerHalfWidth;
                case TurnPhase.TankArrive:
                    // Fallback only — BattleTick feeds the live union (tank + crew + line).
                    return playerHalfWidth;
                case TurnPhase.PlayerScout:
                    return enemyHalfWidth;
                case TurnPhase.EnemyWindup:
                    return marchersActive ? marchHalfWidth : shooterHalfWidth;
                case TurnPhase.Resolving:
                    return turnSide == TurnSide.Enemy ? playerHalfWidth : enemyHalfWidth;
                case TurnPhase.AirstrikeRun:
                    // THE AIRCRAFT NEEDS ROOM THE GROUND FIGHT NEVER DOES. Falling through to the
                    // default here put the run on the AIMING framing — the tightest in the game,
                    // about camZ 9 — and a 4.5-unit aeroplane banked 45 degrees was then so large
                    // it was clipped by the top of the frame for the first half of its pass.
                    // Measured on device 2026-08-10, first build of the beat.
                    //
                    // The floor is a floor, not a fixed value: a level whose enemy cluster is wider
                    // still gets its own framing, so this can only ever pull the camera BACK.
                    return Mathf.Max(enemyHalfWidth, AirstrikeRunHalfWidth);
                default:
                    return playerHalfWidth;
            }
        }

        /// <summary>
        /// Half-width for the ENEMY set. Uses the shooters' own mean as the anchor for reach,
        /// so a structure far from the shooters still gets framed — CameraFraming covers both
        /// "distance from anchor" and "the set's own span", because the anchor is not always the
        /// centroid.
        /// </summary>
        /// <summary>
        /// The roll-in frame: the moving tank, the crew on its deck, and the ground
        /// line waiting for it. Mean of those points, then CameraFraming.HalfWidth
        /// so the set cannot disagree with every other phase.
        /// </summary>
        public static void TankArriveFrame(
            IReadOnlyList<StructureEntity> structures,
            IReadOnlyList<UnitEntity> playerUnits,
            out float anchorX, out float halfWidth)
        {
            var xs = new List<float>();
            foreach (var st in structures)
                if (st.Definition != null && st.Definition.isPlayerSide && st.Definition.hasCannon)
                    xs.Add(st.X);
            foreach (var u in playerUnits) xs.Add(u.X);
            if (xs.Count == 0) { anchorX = 0f; halfWidth = 3f; return; }
            float sum = 0f;
            for (int i = 0; i < xs.Count; i++) sum += xs[i];
            anchorX = sum / xs.Count;
            halfWidth = Mathf.Max(CameraFraming.HalfWidth(anchorX, xs), 2.5f);
        }

        public static float EnemyHalfWidth(IReadOnlyList<float> enemyXs, float shooterReach)
            => Mathf.Max(Span(enemyXs) / 2f, shooterReach);

        /// <summary>
        /// Reach of the SHOOTERS — melee units excluded. A melee unit that has marched deep into
        /// the field is not something the enemy-windup frame needs to contain, and including it
        /// is what once ballooned that phase's zoom.
        /// </summary>
        public static float ShooterReach(IReadOnlyList<float> shooterXs,
                                         IReadOnlyList<float> shooterRelevantXs)
        {
            if (shooterXs.Count == 0) return 0f;
            float mean = shooterXs.Average();
            return CameraFraming.HalfWidth(mean, shooterRelevantXs);
        }

        public static float MarchHalfWidth(IReadOnlyList<float> marchingXs,
                                           IReadOnlyList<float> skirmishXs)
        {
            var all = marchingXs.Concat(skirmishXs).ToList();
            return Mathf.Max(Span(all) / 2f, MarchHalfWidthMin);
        }

        /// <summary>
        /// TWO SHOTS, because they ask for opposite zooms.
        ///
        /// CONTACT (any skirmish): the signed-off union — chargers, both ends of every fight,
        /// and the whole player force. Framing the pair alone put the camera at x -5.1 ±4
        /// on L4 and cropped the tank crew at -9.59. Rob: *"so the player can see what's
        /// happening to their force."*
        ///
        /// MARCH (nobody fighting yet): the chargers, plus only the player units already
        /// inside <see cref="MarchIncludePlayerGap"/> of the lead. The old union sat the
        /// camera at citadel distance the moment a squad left the ruins, and four riot
        /// shields read as a speck — Rob, 2026-08-13, on the armour. As the gap closes the
        /// threatened front enters continuously; contact then takes the union. No cut.
        /// </summary>
        public static float AssaultFrame(IReadOnlyList<float> marchingXs,
                                         IReadOnlyList<float> skirmishXs,
                                         IReadOnlyList<float> playerXs,
                                         out float anchorX)
        {
            var all = new List<float>();
            all.AddRange(marchingXs);
            all.AddRange(skirmishXs);

            if (skirmishXs.Count > 0)
            {
                all.AddRange(playerXs);
            }
            else if (all.Count > 0 && playerXs.Count > 0)
            {
                float lead = all.Min();
                foreach (float px in playerXs)
                    if (px >= lead - MarchIncludePlayerGap) all.Add(px);
            }

            if (all.Count == 0) { anchorX = 0f; return MarchHalfWidthMin; }

            anchorX = (all.Min() + all.Max()) / 2f;
            return Mathf.Max(CameraFraming.HalfWidth(anchorX, all), MarchHalfWidthMin);
        }

        /// <summary>
        /// Where the camera looks during EnemyWindup when an assault force is on the move.
        ///
        /// THREE BEATS, and the middle one is the whole reason this exists:
        ///  1. while the melee force is CLOSING, ride with it — the advance happens ON SCREEN;
        ///  2. once a fighter ARRIVES and engages, HOLD on the skirmish line until every fight
        ///     has resolved. An engaged attacker is no longer a marcher (its budget is spent),
        ///     so a target built from marchers alone snaps away the instant the last one locks
        ///     on — panning off the fight ~1s before its own mutual-kill payoff lands;
        ///  3. only with both empty does the rest of the windup pan BACK to whoever actually
        ///     fires this volley — the RANGED shooters, not the roster mean. On a level where
        ///     advancers outnumber the rear line, that mean parks on the player line and the
        ///     shooters fire off-frustum.
        ///
        /// Ported 2026-08-12 with advancing squads. Rob, on the first device build: the melee
        /// "happens off camera and it's weird" — because the windup anchor was a fixed per-level
        /// value on the ENEMY side while the fight was at the player's line.
        /// </summary>
        public static float EnemyWindupAnchorX(IReadOnlyList<float> marchingXs,
                                               IReadOnlyList<float> skirmishXs,
                                               IReadOnlyList<float> shooterXs,
                                               IReadOnlyList<float> allEnemyXs,
                                               float fallback)
        {
            if (marchingXs.Count > 0 || skirmishXs.Count > 0)
                return marchingXs.Concat(skirmishXs).Average();
            if (shooterXs.Count > 0) return shooterXs.Average();
            // An all-melee force with nobody marching this tick: the roster mean beats dividing
            // by zero, and beats the per-level anchor, which is where nothing is standing.
            return allEnemyXs.Count > 0 ? allEnemyXs.Average() : fallback;
        }

        /// <summary>
        /// Half-width to camera distance, clamped into the usable band.
        ///
        /// StaticCamera is a ZOOM CEILING ONLY. It used to pin camera x as well, which disabled
        /// the whole per-phase choreography and left each phase sizing its zoom about a centre
        /// the camera was not using — cropping the subject instead of framing it.
        /// </summary>
        public static float TargetZ(float phaseHalfWidth, bool staticCamera, float staticCamZ)
        {
            float z = Mathf.Clamp(phaseHalfWidth / ZHalfFovTan, ZMin, GameplayZ);
            if (staticCamera)
                z = Mathf.Clamp(z, staticCamZ * StaticCameraZoomInFraction, staticCamZ);
            return z;
        }

        public const float StaticCameraZoomInFraction = 0.2f;
    }
}
