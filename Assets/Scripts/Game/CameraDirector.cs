using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
        public const float GameplayZ = 22f;
        public const float ZMin = 5.5f;
        public const float ZHalfFovTan = 0.45f;

        /// <summary>Fast — the bullet cam must stay glued to a volley in flight.</summary>
        public const float VolleyFollowSmoothTime = 0.06f;

        /// <summary>Slow — escorting a march is a saunter, not a chase.</summary>
        public const float MarchEscortSmoothTime = 0.30f;

        /// <summary>
        /// A FLOOR on the march frame, so escorting a lone advancing unit does not zoom to a
        /// keyhole. Verified against the Kotlin (4f) rather than inferred — guessing it at 2
        /// would have framed every march twice as tight as intended.
        /// </summary>
        public const float MarchHalfWidthMin = 4f;

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
                case TurnPhase.PlayerScout:
                    return enemyHalfWidth;
                case TurnPhase.EnemyWindup:
                    return marchersActive ? marchHalfWidth : shooterHalfWidth;
                case TurnPhase.Resolving:
                    return turnSide == TurnSide.Enemy ? playerHalfWidth : enemyHalfWidth;
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
